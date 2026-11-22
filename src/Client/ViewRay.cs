using System;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client
{
    /// <summary>
    /// The first-person view: a ray from the local player's eyes along the look direction. The
    /// camera draws from it and the interact prompt is aimed with it, so both always agree.
    /// Yaw 0 looks along −Z, positive pitch looks up (Godot's camera convention).
    /// </summary>
    public readonly struct ViewRay
    {
        /// <summary>Straight up or down would make yaw meaningless; the view stops a little short.</summary>
        public const float MaxPitch = 1.45f;

        public readonly float X, Y, Z;
        public readonly float DirX, DirY, DirZ;

        public ViewRay(float x, float y, float z, float yaw, float pitch)
        {
            X = x; Y = y; Z = z;
            float cp = MathF.Cos(pitch);
            DirX = -MathF.Sin(yaw) * cp;
            DirY = MathF.Sin(pitch);
            DirZ = -MathF.Cos(yaw) * cp;
        }

        /// <summary>The view of a player standing at <paramref name="feet"/>.</summary>
        public static ViewRay FromPlayer(in PlayerMoveState feet, float yaw, float pitch) =>
            new ViewRay(feet.X, feet.Y + SimConstants.EyeHeight, feet.Z, yaw, pitch);

        /// <summary>Yaw and pitch that look from the eyes of a player at <paramref name="feet"/> to a point.</summary>
        public static void Aim(in PlayerMoveState feet, float x, float y, float z, out float yaw, out float pitch)
        {
            float dx = x - feet.X, dy = y - (feet.Y + SimConstants.EyeHeight), dz = z - feet.Z;
            float flat = MathF.Sqrt(dx * dx + dz * dz);
            yaw = flat < 1e-4f ? 0f : MathF.Atan2(-dx, -dz);
            pitch = Math.Clamp(MathF.Atan2(dy, flat), -MaxPitch, MaxPitch);
        }

        /// <summary>Distance along the ray to an axis-aligned box, or −1 if the ray misses it (slab test).</summary>
        public float Hit(in AimBox box)
        {
            float tMin = 0f, tMax = float.MaxValue;
            if (!Slab(X, DirX, box.X - box.HalfX, box.X + box.HalfX, ref tMin, ref tMax)) return -1f;
            if (!Slab(Y, DirY, box.Y - box.HalfY, box.Y + box.HalfY, ref tMin, ref tMax)) return -1f;
            if (!Slab(Z, DirZ, box.Z - box.HalfZ, box.Z + box.HalfZ, ref tMin, ref tMax)) return -1f;
            return tMin;
        }

        private static bool Slab(float origin, float dir, float min, float max, ref float tMin, ref float tMax)
        {
            if (MathF.Abs(dir) < 1e-6f) return origin >= min && origin <= max;
            float t0 = (min - origin) / dir, t1 = (max - origin) / dir;
            if (t0 > t1) (t0, t1) = (t1, t0);
            if (t0 > tMin) tMin = t0;
            if (t1 < tMax) tMax = t1;
            return tMin <= tMax;
        }
    }

    /// <summary>The volume a player has to look at to target an interactable: centre and half extents in metres.</summary>
    public readonly struct AimBox
    {
        public readonly float X, Y, Z, HalfX, HalfY, HalfZ;

        public AimBox(float x, float y, float z, float halfX, float halfY, float halfZ)
        {
            X = x; Y = y; Z = z; HalfX = halfX; HalfY = halfY; HalfZ = halfZ;
        }

        /// <summary>
        /// A mailbox: the post, the box and the address plate above it. Wider than the mesh so a
        /// mailbox is easy to pick while walking past.
        /// </summary>
        public static AimBox Mailbox(WorldMap map, DestinationDef dest) =>
            new AimBox(dest.MailboxX, map.HeightAt(dest.MailboxX, dest.MailboxZ) + 1.0f, dest.MailboxZ, 0.5f, 1.0f, 0.5f);

        /// <summary>A chest of the map, a little wider than its body.</summary>
        public static AimBox Chest(WorldMap map, MapChestDef chest) =>
            new AimBox(chest.X, map.HeightAt(chest.X, chest.Z) + 0.45f, chest.Z, 0.6f, 0.5f, 0.6f);

        /// <summary>A parked vehicle: a bike from wheel to wheel whichever way it points, saddle and bars included.</summary>
        public static AimBox Vehicle(WorldMap map, float x, float z) =>
            new AimBox(x, map.HeightAt(x, z) + 0.55f, z, 0.85f, 0.6f, 0.85f);

        /// <summary>A death bag on the ground, wider than the satchel so it is easy to pick in a fight.</summary>
        public static AimBox Bag(WorldMap map, float x, float z) =>
            new AimBox(x, map.HeightAt(x, z) + 0.3f, z, 0.5f, 0.4f, 0.5f);

        /// <summary>
        /// A resource node: a tree's trunk up to where its crown begins, a bush, a boulder. A little
        /// wider than what is drawn, as everything else that is aimed at.
        /// </summary>
        public static AimBox Node(WorldMap map, in Prop prop)
        {
            float ground = map.HeightAt(prop.X, prop.Z), s = prop.Scale;
            switch (prop.Kind)
            {
                case PropKind.Tree: return new AimBox(prop.X, ground + 1.5f, prop.Z, 0.6f, 1.5f, 0.6f);
                case PropKind.Boulder: return new AimBox(prop.X, ground + 0.35f * s, prop.Z, 0.8f * s, 0.45f * s, 0.8f * s);
                case PropKind.SandPile: return new AimBox(prop.X, ground + 0.25f * s, prop.Z, 0.85f * s, 0.35f * s, 0.85f * s);
                default: return new AimBox(prop.X, ground + 0.45f * s, prop.Z, 0.65f * s, 0.5f * s, 0.65f * s);
            }
        }

        /// <summary>
        /// Something the carriers built: a wall the length of its tile along the way it runs and
        /// a hand thicker than it is, a chest a little wider than its body.
        /// </summary>
        public static AimBox Built(WorldMap map, Construct built)
        {
            float ground = map.HeightAt(built.X, built.Z), h = built.Def.Height * 0.5f;
            if (built.Def.Length > 1)
            {
                // A ramp: the box over all of its tiles.
                var (lx, lz) = built.TileAt(built.Def.Length - 1);
                float cx = (built.X + (lx + 0.5f) * SimConstants.TileSize) * 0.5f, cz = (built.Z + (lz + 0.5f) * SimConstants.TileSize) * 0.5f;
                float half = built.Def.Length * SimConstants.TileSize * 0.5f - 0.1f, side = built.Def.Radius + 0.15f;
                return (built.Facing & 1) == 0 ? new AimBox(cx, ground + h, cz, half, h + 0.1f, side) : new AimBox(cx, ground + h, cz, side, h + 0.1f, half);
            }
            if (!built.Def.Joins) return new AimBox(built.X, ground + h, built.Z, built.Def.Radius + 0.15f, h + 0.1f, built.Def.Radius + 0.15f);
            float along = SimConstants.TileSize * 0.5f, across = built.Def.Radius + 0.1f;
            return built.AlongZ
                ? new AimBox(built.X, ground + h, built.Z, across, h, along)
                : new AimBox(built.X, ground + h, built.Z, along, h, across);
        }

        /// <summary>The Intake counter with its sign.</summary>
        public static AimBox Intake(WorldMap map)
        {
            var po = map.PostOffice;
            return new AimBox(po.IntakeX, map.HeightAt(po.IntakeX, po.IntakeZ) + 1.0f, po.IntakeZ, 1.0f, 1.0f, 1.0f);
        }

        /// <summary>The shop counter, the window behind it and the sign over it: a good deal wider than the counter's body.</summary>
        public static AimBox Shop(WorldMap map)
        {
            var po = map.PostOffice;
            return new AimBox(po.ShopX, map.HeightAt(po.ShopX, po.ShopZ) + 1.25f, po.ShopZ, 1.3f, 1.25f, 1.3f);
        }
    }
}
