using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Throwing's canonical dump section (<see cref="SectionIds.Throwing"/>; ADR-0015 section 9, ADR-0006 section 4): the
/// <c>FlightId</c> serial <c>u16</c>; a <c>u32</c> count of live flights, then each live flight in creation order (ADR-0015 section 9
/// fixes creation order, not id order: ids wrap) with every stored field one by one: id <c>u16</c>, entry <c>u32</c>, units <c>u8</c>,
/// kindDef <c>u16</c>, thrower slot <c>u8</c>, classPts <c>i32</c>, origin 3 x <c>i32</c>, vel 3 x <c>i32</c>, launchTick <c>u32</c>,
/// thrownPm <c>i32</c>, releaseTick <c>u32</c>, openAtRelease <c>u8</c>, hop <c>u8</c>, originClamped <c>u8</c>; then <c>Tat</c> for
/// each of the 8 slots (<c>u32</c>, ascending slot); then the reserved catch-arm block, a <c>u32</c> count that is 0 until the catch
/// stories write arm state there (Evan approved (TD-197 item 4), 2026-10-29 3:24 PM PT: "Approve all four TD-197 defaults for throwing-catching 008"). Dead slots awaiting compaction and the run's
/// conservation counts are not written, so two tables with the same live flights dump the same bytes whatever their history.
/// Between ticks only; allocation-free.
/// </summary>
/// <example><code>builder.BindStateContributor(new ThrowingStateContributor(flights, tat));</code></example>
internal sealed class ThrowingStateContributor : ISimStateContributor
{
    private readonly FlightTable _flights;
    private readonly uint[] _tat;

    /// <summary>Creates the contributor.</summary>
    /// <param name="flights">The flight table.</param>
    /// <param name="tat">The per-slot <c>Tat</c>, <see cref="PlayerSlot.Count"/> long.</param>
    public ThrowingStateContributor(FlightTable flights, uint[] tat)
    {
        _flights = flights;
        _tat = tat;
    }

    /// <inheritdoc />
    public ushort SectionId => SectionIds.Throwing;

    /// <inheritdoc />
    public void Write(ref CanonicalWriter w)
    {
        w.WriteU16(_flights.NextFlightId);
        w.WriteU32((uint)_flights.LiveFlights);
        for (int i = 0; i < _flights.Count; i++)
        {
            if (_flights.IsDead(i))
            {
                continue;
            }

            w.WriteU16(_flights.Id(i).Value);
            w.WriteU32(_flights.Entry(i).Value);
            w.WriteU8(_flights.Units(i));
            w.WriteU16(_flights.KindDef(i));
            w.WriteU8(_flights.Thrower(i).Value);
            w.WriteI32(_flights.ClassPts(i));
            ref readonly Vec3Mm origin = ref _flights.Origin(i);
            w.WriteI32(origin.X);
            w.WriteI32(origin.Y);
            w.WriteI32(origin.Z);
            ref readonly Vec3Mm vel = ref _flights.Vel(i);
            w.WriteI32(vel.X);
            w.WriteI32(vel.Y);
            w.WriteI32(vel.Z);
            w.WriteU32(_flights.LaunchTick(i));
            w.WriteI32(_flights.ThrownPm(i));
            w.WriteU32(_flights.ReleaseTick(i));
            w.WriteBool(_flights.OpenAtRelease(i));
            w.WriteU8(_flights.Hop(i));
            w.WriteBool(_flights.OriginClamped(i));
        }

        for (int s = 0; s < PlayerSlot.Count; s++)
        {
            w.WriteU32(_tat[s]);
        }

        w.WriteU32(0);   // reserved catch-arm block: no arm state yet
    }
}
