using System;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Players
{
    /// <summary>The predicted part of a player: everything <see cref="PlayerMovement.Step"/> reads and writes.</summary>
    public struct PlayerMoveState
    {
        public float X, Y, Z;
        public float VelY;
        public bool Grounded;
        public ushort Yaw;
    }

    /// <summary>Movement tunables (doc 11 / balance.json).</summary>
    public readonly struct MoveConstants
    {
        public readonly float WalkSpeed, SprintSpeed, JumpHeight, Gravity, Radius;

        public MoveConstants(float walk, float sprint, float jumpHeight, float gravity, float radius)
        {
            WalkSpeed = walk; SprintSpeed = sprint; JumpHeight = jumpHeight; Gravity = gravity; Radius = radius;
        }

        public static MoveConstants From(Balance b) => new MoveConstants(
            b.GetFloat("walkSpeed"), b.GetFloat("sprintSpeed"), b.GetFloat("jumpHeight"),
            b.GetFloat("gravity"), b.GetFloat("playerRadius"));
    }

    /// <summary>
    /// Kinematic character step shared by the server and client prediction (doc 07 §5 PlayerMotor).
    /// One call = one 30 Hz tick for one input.
    /// </summary>
    public static class PlayerMovement
    {
        public static void Step(ref PlayerMoveState s, in PlayerInput input, WorldMap map, float speedMult, in MoveConstants c)
        {
            const float dt = SimConstants.TickDt;
            s.Yaw = input.Yaw;

            float mx = input.MoveX / 127f;
            float mz = input.MoveZ / 127f;
            float lenSq = mx * mx + mz * mz;
            if (lenSq > 1f)
            {
                float inv = 1f / MathF.Sqrt(lenSq);
                mx *= inv; mz *= inv;
            }
            float speed = (input.Has(InputButtons.Sprint) ? c.SprintSpeed : c.WalkSpeed) * speedMult;
            s.X += mx * speed * dt;
            s.Z += mz * speed * dt;
            ResolveCollisions(ref s, map, c.Radius);

            if (s.Grounded && input.Has(InputButtons.Jump))
            {
                s.VelY = MathF.Sqrt(2f * c.Gravity * c.JumpHeight);
                s.Grounded = false;
            }
            float ground = map.HeightAt(s.X, s.Z);
            if (!s.Grounded)
            {
                // Exact integration of constant gravity, so the apex really is jumpHeight.
                s.Y += s.VelY * dt - 0.5f * c.Gravity * dt * dt;
                s.VelY -= c.Gravity * dt;
                if (s.Y <= ground)
                {
                    s.Y = ground;
                    s.VelY = 0f;
                    s.Grounded = true;
                }
            }
            else s.Y = ground;
        }

        /// <summary>Pushes a circle out of every solid tile it overlaps. Map edges are solid.</summary>
        private static void ResolveCollisions(ref PlayerMoveState s, WorldMap map, float r)
        {
            const float tile = SimConstants.TileSize;
            for (int iter = 0; iter < 2; iter++)
            {
                bool pushed = false;
                int minTx = (int)MathF.Floor((s.X - r) / tile), maxTx = (int)MathF.Floor((s.X + r) / tile);
                int minTz = (int)MathF.Floor((s.Z - r) / tile), maxTz = (int)MathF.Floor((s.Z + r) / tile);
                for (int tz = minTz; tz <= maxTz; tz++)
                {
                    for (int tx = minTx; tx <= maxTx; tx++)
                    {
                        if (!map.IsSolid(tx, tz)) continue;
                        float x0 = tx * tile, z0 = tz * tile, x1 = x0 + tile, z1 = z0 + tile;
                        float cx = Math.Clamp(s.X, x0, x1);
                        float cz = Math.Clamp(s.Z, z0, z1);
                        float dx = s.X - cx, dz = s.Z - cz;
                        float distSq = dx * dx + dz * dz;
                        if (distSq >= r * r) continue;
                        if (distSq > 1e-12f)
                        {
                            float dist = MathF.Sqrt(distSq);
                            float push = (r - dist) / dist;
                            s.X += dx * push;
                            s.Z += dz * push;
                        }
                        else
                        {
                            // Centre is inside the tile: leave through the nearest face.
                            float left = s.X - x0, right = x1 - s.X, up = s.Z - z0, down = z1 - s.Z;
                            float min = Math.Min(Math.Min(left, right), Math.Min(up, down));
                            if (min == left) s.X = x0 - r;
                            else if (min == right) s.X = x1 + r;
                            else if (min == up) s.Z = z0 - r;
                            else s.Z = z1 + r;
                        }
                        pushed = true;
                    }
                }
                if (!pushed) break;
            }
        }
    }
}
