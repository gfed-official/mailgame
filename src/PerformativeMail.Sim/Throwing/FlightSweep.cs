using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The one swept-segment contact test of Throwing (GDD <c>design/gdd/throwing-catching.md</c> F4 and T6; ADR-0015 section 4;
/// TR-throwing-003, TR-throwing-005 to 007), shared by the host's <c>Throwing.Step</c>, the aim preview and the catch
/// predictor. Each receiver in the <see cref="ReceiverView"/> yields an interval through <see cref="SegmentIntervals"/> or
/// <see cref="HeightfieldMarch"/> (never a second implementation) and a hit contributes <c>f = max(tEnter, 0)</c> permille.
/// The winner is the minimum of <c>(f, rank, dist², TieHash)</c>, with
/// <c>TieHash = SplitMix.Mix64(((ulong)flightId.Value &lt;&lt; 32) | receiverKey)</c>; <see cref="FlightId"/> is a hash input
/// and is never compared. The contact point is <c>P0 + Throw.RH(D * f, 1000)</c> for every kind.
/// <list type="bullet">
/// <item><description>Mailbox volumes count only on a descending segment (<c>P1.y &lt; P0.y</c>); a segment that starts inside
/// one and descends has <c>f = 0</c>. Rising and flat segments never touch them.</description></item>
/// <item><description>World solids, belt-lane top faces and mailbox volumes are slab tests; armed catchers are spheres and goons
/// and teammates flat-capped cylinders. A dynamic receiver (sphere or cylinder) is first rejected by a bounding-sphere test
/// against the segment, and only then reaches the quadratic form, whose own guard rejects <c>|R| &gt; 65 536</c> mm
/// (<see cref="SegmentInterval.None"/>, <c>SimAssertCode.GeometryBroadPhase</c> under <c>SIM_ASSERT</c>; ruling Evan
/// 2026-10-28, TD-179: no counter sink here).</description></item>
/// <item><description>The four vertical sides of <see cref="ReceiverView.Bounds"/> are rank-Solid receivers: a segment moving
/// outward across a side hits it at the crossing (keys <see cref="MapSideKeyBase"/> | side, no centre).</description></item>
/// <item><description>Terrain is <see cref="HeightfieldMarch.FirstContact"/> (its samples round with <c>Fix.MulDivRH</c>
/// inside the march, ruling Evan 2026-10-28, ADR-0015 section 2 erratum). Water is the slab <c>y &lt;= 0</c>: its crossing
/// fraction is computed, and it counts only where <c>IWorldQuery.WaterDepthMm</c> at that contact point is
/// positive (the authority, ADR-0026 section 3). Both are singletons with key 0 and no centre.</description></item>
/// </list>
/// <para>
/// dist² is measured from the contact point to the receiver centre (box centre, sphere centre, cylinder axis at mid-height)
/// in doubled coordinates, i.e. 4 x dist², so half-millimetre box centres stay exact with no division; receivers without a
/// centre use 0 (Evan 2026-10-28, TD-179). Only receivers of one rank are ever compared on it, so the scale never mixes
/// (Mail Dev default, pending Evan: the doubled scale, and the cylinder's mid-height axis point as its centre).
/// Coordinates are assumed inside the ADR-0026 map (|coord| up to 1 000 000 mm). Pure, static, integer-only,
/// allocation-free, reads nothing but its arguments.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // TC-F4-1: the F1 flight at tick 21 into MB-A.
/// var view = new ReceiverView(world, world.Bounds, ReceiverRanks.Default, ReceiverMask.All) { Mailboxes = volumes };
/// if (FlightSweep.FirstContact(new Vec3Mm(10250, 963, 10570), new Vec3Mm(10250, 837, 10114), in view, id, out Contact c))
/// {
///     // c.Kind == ReceiverKind.Mailbox, c.FPm == 154, c.Point == (10250, 944, 10500)
/// }
/// </code>
/// </example>
public static class FlightSweep
{
    /// <summary>The key of a map side is <c>MapSideKeyBase | side</c> (0 -X, 1 +X, 2 -Z, 3 +Z).</summary>
    public const uint MapSideKeyBase = 0xC000_0000u;

    private const uint SideMinX = 0;
    private const uint SideMaxX = 1;
    private const uint SideMinZ = 2;
    private const uint SideMaxZ = 3;

    // Largest doubled primitive radius the bounding-sphere test squares; anything larger is left to SegmentIntervals' guard.
    private const long MaxDoubledRadius = 2L * SegmentIntervals.BroadPhaseRadiusMm;

    /// <summary>Sweeps <c>p0 -&gt; p1</c> against every receiver in the view and returns the winning contact.</summary>
    /// <param name="p0">Segment start (the flight position at tick k - 1).</param>
    /// <param name="p1">Segment end (the flight position at tick k).</param>
    /// <param name="v">The receivers, world, bounds, rank table and mask.</param>
    /// <param name="id">The flight, used only as a tie-break hash input.</param>
    /// <param name="c">The winning contact; <c>default</c> when there is none.</param>
    /// <returns>True when the segment touches any receiver.</returns>
    /// <example>
    /// <code>bool hit = FlightSweep.FirstContact(in prev, in next, in view, flight.Id, out Contact c);</code>
    /// </example>
    public static bool FirstContact(in Vec3Mm p0, in Vec3Mm p1, in ReceiverView v, FlightId id, out Contact c)
    {
        var s = new Sweep(in p0, in p1, v.Ranks, id);

        if (s.Dy < 0)
        {
            foreach (ref readonly VolumeReceiver m in v.Mailboxes)
            {
                s.OfferBox(ReceiverKind.Mailbox, SegmentIntervals.Slab(in p0, in p1, in m.Box), m.Key, in m.Box);
            }
        }

        foreach (ref readonly SolidReceiver solid in v.Solids)
        {
            s.OfferBox(ReceiverKind.Solid, SegmentIntervals.Slab(in p0, in p1, in solid.Box), solid.Key, in solid.Box);
        }

        MapSides(ref s, in p0, in p1, in v);

        if (HeightfieldMarch.FirstContact(v.World, in p0, in p1, out int groundPm))
        {
            s.OfferPlain(ReceiverKind.Ground, groundPm, 0);
        }

        SegmentInterval water = SegmentIntervals.SlabAxis(p0.Y, p1.Y, int.MinValue, 0);
        if (water.Hit)
        {
            Vec3Mm at = s.PointAt(water.FractionPm);
            if (v.World.WaterDepthMm(at.X, at.Z) > 0)
            {
                s.OfferPlain(ReceiverKind.Water, water.FractionPm, 0);
            }
        }

        if (v.Mask == ReceiverMask.All)
        {
            Dynamic(ref s, in p0, in p1, in v);
        }

        return s.Finish(out c);
    }

    private static void MapSides(ref Sweep s, in Vec3Mm p0, in Vec3Mm p1, in ReceiverView v)
    {
        // Mail Dev default, pending Evan: a side counts only for a segment moving outward across it (D toward the outside),
        // as the closed half-space beyond the side, so a flight on the edge moving inward is not stopped by it.
        if (s.Dx < 0)
        {
            s.OfferSide(SegmentIntervals.SlabAxis(p0.X, p1.X, int.MinValue, (int)v.Bounds.MinXMm), MapSideKeyBase | SideMinX);
        }
        else if (s.Dx > 0)
        {
            s.OfferSide(SegmentIntervals.SlabAxis(p0.X, p1.X, (int)v.Bounds.MaxXMm, int.MaxValue), MapSideKeyBase | SideMaxX);
        }

        if (s.Dz < 0)
        {
            s.OfferSide(SegmentIntervals.SlabAxis(p0.Z, p1.Z, int.MinValue, (int)v.Bounds.MinZMm), MapSideKeyBase | SideMinZ);
        }
        else if (s.Dz > 0)
        {
            s.OfferSide(SegmentIntervals.SlabAxis(p0.Z, p1.Z, (int)v.Bounds.MaxZMm, int.MaxValue), MapSideKeyBase | SideMaxZ);
        }
    }

    // Mail Dev default, pending Evan: the cylinder and belt-lane loops are written here (on SegmentIntervals) although their
    // spans stay empty until M-1b, so TC-T6-1 and the ReceiverMask AC run through FirstContact on stub receivers.
    private static void Dynamic(ref Sweep s, in Vec3Mm p0, in Vec3Mm p1, in ReceiverView v)
    {
        // The segment's bounding sphere in doubled coordinates: centre P0 + P1, radius ceil(|D|). A segment longer than the
        // broad-phase bound on any axis skips the pre-test so that SegmentIntervals' own guard rejects it (and asserts).
        bool fits = Within(s.Dx) && Within(s.Dy) && Within(s.Dz);
        long length = fits ? CeilSqrt((s.Dx * s.Dx) + (s.Dy * s.Dy) + (s.Dz * s.Dz)) : 0;
        long mx = (long)p0.X + p1.X;
        long my = (long)p0.Y + p1.Y;
        long mz = (long)p0.Z + p1.Z;

        foreach (ref readonly SphereReceiver k in v.Catchers)
        {
            long cx = 2L * k.Centre.X;
            long cy = 2L * k.Centre.Y;
            long cz = 2L * k.Centre.Z;
            if (fits && !MayTouch(cx - mx, cy - my, cz - mz, 2L * k.RadiusMm, length))
            {
                continue;
            }

            s.OfferCentre(ReceiverKind.ArmedCatcher, SegmentIntervals.Sphere(in p0, in p1, in k.Centre, k.RadiusMm), k.Key, cx, cy, cz);
        }

        foreach (ref readonly CylinderReceiver y in v.Cylinders)
        {
            long cx = 2L * y.BaseCentre.X;
            long cy = (2L * y.BaseCentre.Y) + y.HeightMm;
            long cz = 2L * y.BaseCentre.Z;
            if (fits && !MayTouch(cx - mx, cy - my, cz - mz, CylinderRadius(y.RadiusMm, y.HeightMm), length))
            {
                continue;
            }

            SegmentInterval r = SegmentIntervals.Cylinder(in p0, in p1, in y.BaseCentre, y.RadiusMm, y.HeightMm);
            s.OfferCentre(y.Kind, r, y.Key, cx, cy, cz);
        }

        // Belts are static geometry, but the story 002 ReceiverMask AC skips them under StaticOnly; ADR-0015 l.141 and the
        // story's Implementation Notes put belts in the preview's static view. Mail Dev default, pending Evan: follow the AC.
        foreach (ref readonly LaneReceiver lane in v.Belts)
        {
            s.OfferBox(ReceiverKind.Belt, SegmentIntervals.Slab(in p0, in p1, in lane.TopFace), lane.Key, in lane.TopFace);
        }
    }

    // Doubled bounding-sphere radius of a flat-capped cylinder: ceil(sqrt((2r)^2 + h^2)); -1 (never reject) outside the
    // range the test can square exactly, leaving the call to SegmentIntervals' guard.
    private static long CylinderRadius(int radiusMm, int heightMm)
    {
        if (radiusMm < 0 || radiusMm > SegmentIntervals.BroadPhaseRadiusMm || heightMm < 0 || heightMm > MaxDoubledRadius)
        {
            return -1;
        }

        long d = 2L * radiusMm;
        return CeilSqrt((d * d) + ((long)heightMm * heightMm));
    }

    // Whether the primitive's bounding sphere (doubled radius) can meet the segment's (doubled radius = ceil |D|), given the
    // doubled centre offset. A radius outside 0 to MaxDoubledRadius is never rejected here.
    private static bool MayTouch(long ox, long oy, long oz, long radius, long length)
    {
        if (radius < 0 || radius > MaxDoubledRadius)
        {
            return true;
        }

        long reach = radius + length;
        if (ox > reach || ox < -reach || oy > reach || oy < -reach || oz > reach || oz < -reach)
        {
            return false;
        }

        return (ox * ox) + (oy * oy) + (oz * oz) <= reach * reach;
    }

    private static bool Within(long d) => d >= -SegmentIntervals.BroadPhaseRadiusMm && d <= SegmentIntervals.BroadPhaseRadiusMm;

    private static long CeilSqrt(long n)
    {
        long r = Fix.ISqrt((ulong)n);
        return r * r < n ? r + 1 : r;
    }

    /// <summary>The running winner of one sweep (stack-only; no allocation).</summary>
    private struct Sweep
    {
        public readonly long Dx;
        public readonly long Dy;
        public readonly long Dz;
        private readonly Vec3Mm _p0;
        private readonly ReceiverRanks _ranks;
        private readonly FlightId _id;
        private bool _found;
        private int _f;
        private byte _rank;
        private ReceiverKind _kind;
        private uint _key;
        private bool _hasCentre;
        private long _cx;
        private long _cy;
        private long _cz;

        public Sweep(in Vec3Mm p0, in Vec3Mm p1, ReceiverRanks ranks, FlightId id)
        {
            _p0 = p0;
            Dx = (long)p1.X - p0.X;
            Dy = (long)p1.Y - p0.Y;
            Dz = (long)p1.Z - p0.Z;
            _ranks = ranks;
            _id = id;
            _found = false;
            _f = 0;
            _rank = 0;
            _kind = default;
            _key = 0;
            _hasCentre = false;
            _cx = 0;
            _cy = 0;
            _cz = 0;
        }

        /// <summary><c>P0 + Throw.RH(D * f, 1000)</c>.</summary>
        public readonly Vec3Mm PointAt(int f) => new(
            (int)(_p0.X + Throw.RH(Dx * f, 1000)),
            (int)(_p0.Y + Throw.RH(Dy * f, 1000)),
            (int)(_p0.Z + Throw.RH(Dz * f, 1000)));

        public void OfferBox(ReceiverKind kind, SegmentInterval r, uint key, in AabbMm box)
        {
            if (r.Hit)
            {
                Offer(kind, r.FractionPm, key, true, (long)box.Min.X + box.Max.X, (long)box.Min.Y + box.Max.Y, (long)box.Min.Z + box.Max.Z);
            }
        }

        public void OfferCentre(ReceiverKind kind, SegmentInterval r, uint key, long cx, long cy, long cz)
        {
            if (r.Hit)
            {
                Offer(kind, r.FractionPm, key, true, cx, cy, cz);
            }
        }

        public void OfferSide(SegmentInterval side, uint key)
        {
            if (side.Hit)
            {
                Offer(ReceiverKind.Solid, side.FractionPm, key, false, 0, 0, 0);
            }
        }

        public void OfferPlain(ReceiverKind kind, int f, uint key) => Offer(kind, f, key, false, 0, 0, 0);

        public readonly bool Finish(out Contact c)
        {
            c = _found ? new Contact(_kind, _key, _f, PointAt(_f), _rank) : default;
            return _found;
        }

        private void Offer(ReceiverKind kind, int f, uint key, bool hasCentre, long cx, long cy, long cz)
        {
            byte rank = _ranks.Of(kind);
            if (_found && !Beats(f, rank, key, hasCentre, cx, cy, cz))
            {
                return;
            }

            _found = true;
            _f = f;
            _rank = rank;
            _kind = kind;
            _key = key;
            _hasCentre = hasCentre;
            _cx = cx;
            _cy = cy;
            _cz = cz;
        }

        // (f, rank, dist², TieHash) strictly smaller than the current winner's; a full tie (same key) keeps the current one.
        private readonly bool Beats(int f, byte rank, uint key, bool hasCentre, long cx, long cy, long cz)
        {
            if (f != _f)
            {
                return f < _f;
            }

            if (rank != _rank)
            {
                return rank < _rank;
            }

            Vec3Mm at = PointAt(f); // equal f: one contact point for both
            long dist = hasCentre ? Dist4(in at, cx, cy, cz) : 0;
            long best = _hasCentre ? Dist4(in at, _cx, _cy, _cz) : 0;
            if (dist != best)
            {
                return dist < best;
            }

            return TieHash(key) < TieHash(_key);
        }

        private readonly ulong TieHash(uint key) => SplitMix.Mix64(((ulong)_id.Value << 32) | key);

        // 4 x dist² from the point to a doubled centre.
        private static long Dist4(in Vec3Mm p, long cx, long cy, long cz)
        {
            long x = (2L * p.X) - cx;
            long y = (2L * p.Y) - cy;
            long z = (2L * p.Z) - cz;
            return (x * x) + (y * y) + (z * z);
        }
    }
}
