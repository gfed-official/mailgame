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
        /// <summary>Spent stamina: 0 fresh, 1 exhausted (so that a default state is a rested carrier).</summary>
        public float Fatigue;
        /// <summary>Ran the stamina out: no sprinting until part of it is back.</summary>
        public bool Winded;
        /// <summary>Down until the respawn (doc 05 §1.1); a dead carrier does not move.</summary>
        public bool Dead;

        public float Stamina => 1f - Fatigue;
    }

    /// <summary>Movement tunables (doc 11 / balance.json).</summary>
    public readonly struct MoveConstants
    {
        public readonly float WalkSpeed, SprintSpeed, JumpHeight, Gravity, Radius;
        /// <summary>Fatigue gained per tick of sprinting and lost per tick of anything else.</summary>
        public readonly float FatiguePerTick, RecoveryPerTick;
        /// <summary>A winded carrier may sprint again once this much stamina is back.</summary>
        public readonly float RecoverFraction;

        public MoveConstants(float walk, float sprint, float jumpHeight, float gravity, float radius,
            float staminaSeconds = 0f, float staminaRegenSeconds = 0f, float recoverFraction = 0.2f)
        {
            WalkSpeed = walk; SprintSpeed = sprint; JumpHeight = jumpHeight; Gravity = gravity; Radius = radius;
            FatiguePerTick = staminaSeconds > 0f ? SimConstants.TickDt / staminaSeconds : 0f;
            RecoveryPerTick = staminaRegenSeconds > 0f ? SimConstants.TickDt / staminaRegenSeconds : 1f;
            RecoverFraction = recoverFraction;
        }

        public static MoveConstants From(Balance b) => new MoveConstants(
            b.GetFloat("walkSpeed"), b.GetFloat("sprintSpeed"), b.GetFloat("jumpHeight"),
            b.GetFloat("gravity"), b.GetFloat("playerRadius"),
            b.GetFloat("staminaSeconds"), b.GetFloat("staminaRegenSeconds"), b.GetFloat("staminaRecoverFraction"));
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
            if (s.Dead) mx = mz = lenSq = 0f;

            // Sprinting spends stamina; out of it, a carrier walks until a part is back (the video's blue bar).
            bool sprint = input.Has(InputButtons.Sprint) && lenSq > 0f && !s.Winded;
            if (sprint)
            {
                s.Fatigue += c.FatiguePerTick;
                if (s.Fatigue >= 1f) { s.Fatigue = 1f; s.Winded = true; }
            }
            else
            {
                s.Fatigue = MathF.Max(0f, s.Fatigue - c.RecoveryPerTick);
                if (s.Fatigue <= 1f - c.RecoverFraction) s.Winded = false;
            }
            float speed = (sprint ? c.SprintSpeed : c.WalkSpeed) * speedMult;
            s.X += mx * speed * dt;
            s.Z += mz * speed * dt;
            ResolveCollisions(ref s.X, ref s.Z, s.Y, map, c.Radius);

            if (s.Grounded && input.Has(InputButtons.Jump) && !s.Dead)
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

        /// <summary>
        /// Pushes a circle out of every solid tile and solid prop it overlaps. Map edges are solid.
        /// A prop lower than the walker's feet (<paramref name="feetY"/>) is passed over: fences
        /// can be jumped. Carriers and enemies both walk with this.
        /// </summary>
        public static void ResolveCollisions(ref float x, ref float z, float feetY, WorldMap map, float r)
        {
            const float tile = SimConstants.TileSize;
            for (int iter = 0; iter < 2; iter++)
            {
                bool pushed = false;
                int minTx = (int)MathF.Floor((x - r) / tile), maxTx = (int)MathF.Floor((x + r) / tile);
                int minTz = (int)MathF.Floor((z - r) / tile), maxTz = (int)MathF.Floor((z + r) / tile);
                for (int tz = minTz; tz <= maxTz; tz++)
                {
                    for (int tx = minTx; tx <= maxTx; tx++)
                    {
                        if (!map.IsSolid(tx, tz)) continue;
                        float x0 = tx * tile, z0 = tz * tile, x1 = x0 + tile, z1 = z0 + tile;
                        float cx = Math.Clamp(x, x0, x1);
                        float cz = Math.Clamp(z, z0, z1);
                        float dx = x - cx, dz = z - cz;
                        float distSq = dx * dx + dz * dz;
                        if (distSq >= r * r) continue;
                        if (distSq > 1e-12f)
                        {
                            float dist = MathF.Sqrt(distSq);
                            float push = (r - dist) / dist;
                            x += dx * push;
                            z += dz * push;
                        }
                        else
                        {
                            // Centre is inside the tile: leave through the nearest face.
                            float left = x - x0, right = x1 - x, up = z - z0, down = z1 - z;
                            float min = Math.Min(Math.Min(left, right), Math.Min(up, down));
                            if (min == left) x = x0 - r;
                            else if (min == right) x = x1 + r;
                            else if (min == up) z = z0 - r;
                            else z = z1 + r;
                        }
                        pushed = true;
                    }
                }

                var props = map.SolidPropsAt((int)MathF.Floor(x / tile), (int)MathF.Floor(z / tile));
                if (props != null)
                {
                    foreach (int index in props)
                    {
                        var prop = map.Props[index];
                        if (feetY >= prop.Height) continue;
                        float reach = prop.Radius + r;
                        float dist = prop.Distance(x, z, out float nearX, out float nearZ);
                        if (dist >= reach) continue;
                        if (dist > 1e-5f)
                        {
                            float push = (reach - dist) / dist;
                            x += (x - nearX) * push;
                            z += (z - nearZ) * push;
                        }
                        else x = nearX + reach;
                        pushed = true;
                    }
                }
                if (!pushed) break;
            }
        }
    }
}
