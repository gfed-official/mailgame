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

        /// <summary>The Intake counter with its sign.</summary>
        public static AimBox Intake(WorldMap map)
        {
            var po = map.PostOffice;
            return new AimBox(po.IntakeX, map.HeightAt(po.IntakeX, po.IntakeZ) + 1.0f, po.IntakeZ, 1.0f, 1.0f, 1.0f);
        }
    }
}
