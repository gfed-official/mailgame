using System.Collections.Generic;

namespace PerformativeMail.Sim.Net
{
    /// <summary>A player of a snapshot as the wire carries it: centimetres, 256ths of a turn, bit flags.</summary>
    public struct PlayerQ
    {
        public uint Id;
        public int X, Y, Z;
        public ushort Yaw;
        public byte HeldSlot, HeldTool, Flags, Hp, Ride, Heading, Kit, Look;
    }

    /// <summary>An enemy of a snapshot as the wire carries it: 2 cm steps, its kind, mega and state packed in a byte.</summary>
    public struct EnemyQ
    {
        public ushort Id;
        public byte Packed;
        public ushort X, Z;
        public byte Yaw, Hp;
    }

    /// <summary>What a client has decoded, by snapshot tick: the baselines a snapshot's deltas are read against.</summary>
    public interface ISnapshotBaselines
    {
        bool TryPlayers(uint tick, out IReadOnlyDictionary<uint, PlayerQ> players);
        bool TryEnemies(uint tick, out IReadOnlyDictionary<ushort, EnemyQ> enemies);
    }

    /// <summary>
    /// Doc 06 §4.2 "Snapshots are delta-compressed against the last acked snapshot per client (bitmask of
    /// changed fields per entity)" (design choice 146). Each player and enemy is written as its id, a
    /// mask of what differs from the baseline the client says it has, and only those fields; a position
    /// that moved little goes as a signed byte a coordinate. One not in the baseline goes whole.
    /// </summary>
    public static class SnapshotDelta
    {
        /// <summary>How many snapshots back a baseline may be: both ends keep this many.</summary>
        public const int RingLength = 32;

        private const byte PPosSmall = 1, PPosFull = 2, PYaw = 4, PHeld = 8, PFlags = 16, PHp = 32, PRide = 64, PKit = 128;
        private const byte EPacked = 1, EPosSmall = 2, EPosFull = 4, EYaw = 8, EHp = 16;

        public static void WritePlayer(NetWriter w, in PlayerQ p, PlayerQ? b)
        {
            byte mask;
            if (b is { } o)
            {
                mask = 0;
                int dx = p.X - o.X, dy = p.Y - o.Y, dz = p.Z - o.Z;
                if (dx != 0 || dy != 0 || dz != 0) mask |= Small(dx) && Small(dy) && Small(dz) ? PPosSmall : PPosFull;
                if (p.Yaw != o.Yaw) mask |= PYaw;
                if (p.HeldSlot != o.HeldSlot || p.HeldTool != o.HeldTool) mask |= PHeld;
                if (p.Flags != o.Flags) mask |= PFlags;
                if (p.Hp != o.Hp) mask |= PHp;
                if (p.Ride != o.Ride || p.Heading != o.Heading) mask |= PRide;
                // The look rides with the kit: both are the Lobby's and change seldom.
                if (p.Kit != o.Kit || p.Look != o.Look) mask |= PKit;
            }
            else mask = PPosFull | PYaw | PHeld | PFlags | PHp | PRide | PKit;
            w.U32(p.Id);
            w.U8(mask);
            if ((mask & PPosSmall) != 0) { var ob = b!.Value; w.I8((sbyte)(p.X - ob.X)); w.I8((sbyte)(p.Y - ob.Y)); w.I8((sbyte)(p.Z - ob.Z)); }
            if ((mask & PPosFull) != 0) { w.I32(p.X); w.I16((short)p.Y); w.I32(p.Z); }
            if ((mask & PYaw) != 0) w.U16(p.Yaw);
            if ((mask & PHeld) != 0) { w.U8(p.HeldSlot); w.U8(p.HeldTool); }
            if ((mask & PFlags) != 0) w.U8(p.Flags);
            if ((mask & PHp) != 0) w.U8(p.Hp);
            if ((mask & PRide) != 0) { w.U8(p.Ride); w.U8(p.Heading); }
            if ((mask & PKit) != 0) { w.U8(p.Kit); w.U8(p.Look); }
        }

        /// <summary>Reads one player; false when it is a delta against a baseline without it (the snapshot cannot be decoded).</summary>
        public static bool ReadPlayer(NetReader r, IReadOnlyDictionary<uint, PlayerQ>? baseline, out PlayerQ p)
        {
            uint id = r.U32();
            byte mask = r.U8();
            bool whole = (mask & (PPosFull | PYaw | PHeld | PFlags | PHp | PRide | PKit)) == (PPosFull | PYaw | PHeld | PFlags | PHp | PRide | PKit);
            bool known = false;
            p = default;
            if (baseline != null && baseline.TryGetValue(id, out var o)) { p = o; known = true; }
            p.Id = id;
            if ((mask & PPosSmall) != 0) { p.X += r.I8(); p.Y += r.I8(); p.Z += r.I8(); }
            if ((mask & PPosFull) != 0) { p.X = r.I32(); p.Y = r.I16(); p.Z = r.I32(); }
            if ((mask & PYaw) != 0) p.Yaw = r.U16();
            if ((mask & PHeld) != 0) { p.HeldSlot = r.U8(); p.HeldTool = r.U8(); }
            if ((mask & PFlags) != 0) p.Flags = r.U8();
            if ((mask & PHp) != 0) p.Hp = r.U8();
            if ((mask & PRide) != 0) { p.Ride = r.U8(); p.Heading = r.U8(); }
            if ((mask & PKit) != 0) { p.Kit = r.U8(); p.Look = r.U8(); }
            return known || whole;
        }

        public static void WriteEnemy(NetWriter w, in EnemyQ e, EnemyQ? b)
        {
            byte mask;
            if (b is { } o)
            {
                mask = 0;
                if (e.Packed != o.Packed) mask |= EPacked;
                int dx = e.X - o.X, dz = e.Z - o.Z;
                if (dx != 0 || dz != 0) mask |= Small(dx) && Small(dz) ? EPosSmall : EPosFull;
                if (e.Yaw != o.Yaw) mask |= EYaw;
                if (e.Hp != o.Hp) mask |= EHp;
            }
            else mask = EPacked | EPosFull | EYaw | EHp;
            w.U16(e.Id);
            w.U8(mask);
            if ((mask & EPacked) != 0) w.U8(e.Packed);
            if ((mask & EPosSmall) != 0) { var ob = b!.Value; w.I8((sbyte)(e.X - ob.X)); w.I8((sbyte)(e.Z - ob.Z)); }
            if ((mask & EPosFull) != 0) { w.U16(e.X); w.U16(e.Z); }
            if ((mask & EYaw) != 0) w.U8(e.Yaw);
            if ((mask & EHp) != 0) w.U8(e.Hp);
        }

        public static bool ReadEnemy(NetReader r, IReadOnlyDictionary<ushort, EnemyQ>? baseline, out EnemyQ e)
        {
            ushort id = r.U16();
            byte mask = r.U8();
            bool whole = (mask & (EPacked | EPosFull | EYaw | EHp)) == (EPacked | EPosFull | EYaw | EHp);
            bool known = false;
            e = default;
            if (baseline != null && baseline.TryGetValue(id, out var o)) { e = o; known = true; }
            e.Id = id;
            if ((mask & EPacked) != 0) e.Packed = r.U8();
            if ((mask & EPosSmall) != 0) { e.X = (ushort)(e.X + r.I8()); e.Z = (ushort)(e.Z + r.I8()); }
            if ((mask & EPosFull) != 0) { e.X = r.U16(); e.Z = r.U16(); }
            if ((mask & EYaw) != 0) e.Yaw = r.U8();
            if ((mask & EHp) != 0) e.Hp = r.U8();
            return known || whole;
        }

        /// <summary>The full tick of a 16-bit one at or before <paramref name="near"/>; 0 for 0 (none).</summary>
        public static uint Expand(ushort low, uint near)
        {
            if (low == 0) return 0;
            uint back = (ushort)((ushort)near - low);
            return back > near ? 0 : near - back;
        }

        private static bool Small(int d) => d >= sbyte.MinValue && d <= sbyte.MaxValue;
    }
}
