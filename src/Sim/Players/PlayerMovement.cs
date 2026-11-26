using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Automation;
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
        /// <summary>
        /// On foot: 0. In the saddle: the ridden vehicle's index in ContentDb.Vehicles + 1 (doc 04
        /// §3.1). Only the server puts a carrier on a vehicle; getting off (the jump key) is part of
        /// the step, so the rider's client predicts it.
        /// </summary>
        public byte Ride;
        /// <summary>How fast the vehicle rolls, m/s, along <see cref="HeadX"/>, <see cref="HeadZ"/>.</summary>
        public float RideSpeed;
        /// <summary>The way the vehicle points, a unit vector on the ground; (0, 0) before it has pointed anywhere.</summary>
        public float HeadX, HeadZ;
        /// <summary>
        /// The boat ridden has run its tank dry (design choice 113): it drifts to a stop and its
        /// skipper stays aboard. Only the server sets it, from the boat's fuel.
        /// </summary>
        public bool Becalmed;

        public float Stamina => 1f - Fatigue;
        public bool Riding => Ride != 0;
    }

    /// <summary>Movement tunables (doc 11 / balance.json).</summary>
    public readonly struct MoveConstants
    {
        public readonly float WalkSpeed, SprintSpeed, JumpHeight, Gravity, Radius;
        /// <summary>Fatigue gained per tick of sprinting and lost per tick of anything else.</summary>
        public readonly float FatiguePerTick, RecoveryPerTick;
        /// <summary>A winded carrier may sprint again once this much stamina is back.</summary>
        public readonly float RecoverFraction;
        /// <summary>What can be ridden, in the order of ContentDb.Vehicles; empty where nothing is.</summary>
        public readonly IReadOnlyList<VehicleDef> Vehicles;

        public MoveConstants(float walk, float sprint, float jumpHeight, float gravity, float radius,
            float staminaSeconds = 0f, float staminaRegenSeconds = 0f, float recoverFraction = 0.2f,
            IReadOnlyList<VehicleDef>? vehicles = null)
        {
            Vehicles = vehicles ?? Array.Empty<VehicleDef>();
            WalkSpeed = walk; SprintSpeed = sprint; JumpHeight = jumpHeight; Gravity = gravity; Radius = radius;
            FatiguePerTick = staminaSeconds > 0f ? SimConstants.TickDt / staminaSeconds : 0f;
            RecoveryPerTick = staminaRegenSeconds > 0f ? SimConstants.TickDt / staminaRegenSeconds : 1f;
            RecoverFraction = recoverFraction;
        }

        public static MoveConstants From(Balance b) => new MoveConstants(
            b.GetFloat("walkSpeed"), b.GetFloat("sprintSpeed"), b.GetFloat("jumpHeight"),
            b.GetFloat("gravity"), b.GetFloat("playerRadius"),
            b.GetFloat("staminaSeconds"), b.GetFloat("staminaRegenSeconds"), b.GetFloat("staminaRecoverFraction"));

        /// <summary>The balance's movement and the content's vehicles: what the server and a client step with.</summary>
        public static MoveConstants From(ContentDb content)
        {
            var b = content.Balance;
            return new MoveConstants(
                b.GetFloat("walkSpeed"), b.GetFloat("sprintSpeed"), b.GetFloat("jumpHeight"),
                b.GetFloat("gravity"), b.GetFloat("playerRadius"),
                b.GetFloat("staminaSeconds"), b.GetFloat("staminaRegenSeconds"), b.GetFloat("staminaRecoverFraction"),
                content.Vehicles);
        }

        /// <summary>The vehicle a state rides, null on foot (or for an index this content does not have).</summary>
        public VehicleDef? RideOf(in PlayerMoveState s) => s.Ride != 0 && s.Ride <= Vehicles.Count ? Vehicles[s.Ride - 1] : null;
    }

    /// <summary>
    /// Kinematic character step shared by the server and client prediction (doc 07 §5 PlayerMotor).
    /// One call = one 30 Hz tick for one input.
    /// </summary>
    public static class PlayerMovement
    {
        public static void Step(ref PlayerMoveState s, in PlayerInput input, WorldMap map, float speedMult, in MoveConstants c,
            ConstructField? constructs = null)
        {
            const float dt = SimConstants.TickDt;
            s.Yaw = input.Yaw;
            if (s.Ride != 0)
            {
                var vehicle = c.RideOf(s);
                // The jump key gets off (a bike does not jump), and so does whoever goes down; off,
                // they stand where the vehicle stood, and this tick's jump is spent on getting off.
                // Out of a boat one steps onto a pier or the shore beside it; on open water the key does nothing.
                bool ashore = true;
                if (vehicle != null && vehicle.OnWater && !s.Dead && input.Has(InputButtons.Jump))
                {
                    ashore = Landing(map, constructs, s.X, s.Z, out float lx, out float lz);
                    if (ashore) { s.X = lx; s.Z = lz; }
                }
                if (vehicle == null || s.Dead || (input.Has(InputButtons.Jump) && ashore))
                {
                    s.Ride = 0;
                    s.RideSpeed = 0f;
                    s.Becalmed = false;
                    var afoot = input;
                    afoot.Buttons &= ~InputButtons.Jump;
                    Step(ref s, afoot, map, speedMult, c, constructs);
                    return;
                }
                StepRide(ref s, input, map, vehicle, c, constructs);
                return;
            }

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
            // Doc 04 §2.1: a carrier standing on a belt is carried, at the belt's rated speed the way
            // it runs (a push speeds the mail, not the ride, so a client predicts it exactly).
            if (s.Grounded && !s.Dead && BeltUnder(constructs, s.X, s.Z) is { } belt)
            {
                var d = BeltNetwork.Dirs[belt.Rotation & 3];
                s.X += d.Dx * belt.Def.Speed * dt;
                s.Z += d.Dz * belt.Def.Speed * dt;
            }
            ResolveCollisions(ref s.X, ref s.Z, s.Y, map, c.Radius, constructs);

            if (s.Grounded && input.Has(InputButtons.Jump) && !s.Dead)
            {
                s.VelY = MathF.Sqrt(2f * c.Gravity * c.JumpHeight);
                s.Grounded = false;
            }
            float ground = Floor(map, constructs, s.X, s.Z);
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

        /// <summary>The belt, splitter or merger on the ground on the tile of a point; null for none.</summary>
        public static Construct? BeltUnder(ConstructField? constructs, float x, float z)
        {
            if (constructs == null) return null;
            var c = constructs.At((int)MathF.Floor(x / SimConstants.TileSize), (int)MathF.Floor(z / SimConstants.TileSize));
            return c != null && c.Def.Rideable ? c : null;
        }

        /// <summary>What a carrier stands on: the ground, or a belt's running surface on it.</summary>
        public static float Floor(WorldMap map, ConstructField? constructs, float x, float z) =>
            map.HeightAt(x, z) + (BeltUnder(constructs, x, z) != null ? BeltNetwork.SurfaceHeight : 0f);

        /// <summary>True on the paved tiles a vehicle runs at its road speed on: the street and the Post Office yard.</summary>
        public static bool OnRoad(WorldMap map, float x, float z)
        {
            var tile = map.GetTile((int)MathF.Floor(x / SimConstants.TileSize), (int)MathF.Floor(z / SimConstants.TileSize));
            return tile == TileType.Street || tile == TileType.Yard;
        }

        /// <summary>
        /// One tick in the saddle (doc 04 §3.1, doc 07 §5 "2.5D arcade model"): the input is where
        /// the rider steers; the front follows it at the vehicle's turn rate and the speed comes up
        /// to the top speed of the ground it rolls on — the road's or the rough's — at its
        /// acceleration. Steering hard against the way it rolls brakes; let go, it rolls out. What
        /// it runs into takes the speed it cannot make good. It carries the load, so what is in
        /// the pockets does not slow it, and the rider's breath comes back as if they stood.
        /// </summary>
        private static void StepRide(ref PlayerMoveState s, in PlayerInput input, WorldMap map, VehicleDef v, in MoveConstants c,
            ConstructField? constructs)
        {
            const float dt = SimConstants.TickDt;
            float mx = input.MoveX / 127f, mz = input.MoveZ / 127f;
            float len = MathF.Sqrt(mx * mx + mz * mz);
            if (len > 1f) { mx /= len; mz /= len; len = 1f; }
            float top = s.Becalmed ? 0f : v.OnWater ? v.WaterSpeed : OnRoad(map, s.X, s.Z) ? v.RoadSpeed : v.OffRoadSpeed;

            if (len > 0.01f)
            {
                float dx = mx / len, dz = mz / len;
                if (s.HeadX == 0f && s.HeadZ == 0f) { s.HeadX = dx; s.HeadZ = dz; }
                float dot = s.HeadX * dx + s.HeadZ * dz;
                if (s.RideSpeed > 0.5f && dot < -0.3f)
                    s.RideSpeed = MathF.Max(0f, s.RideSpeed - v.Braking * dt);
                else
                {
                    if (s.RideSpeed <= 0.5f) { s.HeadX = dx; s.HeadZ = dz; }
                    else
                    {
                        float cross = s.HeadX * dz - s.HeadZ * dx;
                        float angle = Math.Clamp(MathF.Atan2(cross, dot), -v.TurnRate * dt, v.TurnRate * dt);
                        float sin = MathF.Sin(angle), cos = MathF.Cos(angle);
                        float hx = s.HeadX * cos - s.HeadZ * sin, hz = s.HeadX * sin + s.HeadZ * cos;
                        float hl = MathF.Sqrt(hx * hx + hz * hz);
                        s.HeadX = hx / hl; s.HeadZ = hz / hl;
                    }
                    float target = top * len;
                    s.RideSpeed = s.RideSpeed < target
                        ? MathF.Min(target, s.RideSpeed + v.Acceleration * dt)
                        : MathF.Max(target, s.RideSpeed - v.CoastDeceleration * dt);
                }
            }
            else s.RideSpeed = MathF.Max(0f, s.RideSpeed - v.CoastDeceleration * dt);

            if (s.RideSpeed > 0f)
            {
                float x = s.X + s.HeadX * s.RideSpeed * dt, z = s.Z + s.HeadZ * s.RideSpeed * dt;
                ResolveCollisions(ref x, ref z, s.Y, map, v.Radius, constructs, boat: v.OnWater);
                // A long body (a Mail Truck) bumps with its nose and its tail too: each is pushed out, and the middle with it.
                float off = v.BodyOffset;
                if (off > 0f)
                {
                    for (int end = 1; end >= -1; end -= 2)
                    {
                        float ex = x + s.HeadX * off * end, ez = z + s.HeadZ * off * end;
                        float px = ex, pz = ez;
                        ResolveCollisions(ref px, ref pz, s.Y, map, v.Radius, constructs, boat: v.OnWater);
                        x += px - ex;
                        z += pz - ez;
                    }
                }
                float along = ((x - s.X) * s.HeadX + (z - s.Z) * s.HeadZ) / dt;
                s.RideSpeed = Math.Clamp(along, 0f, s.RideSpeed);
                s.X = x; s.Z = z;
            }
            s.Fatigue = MathF.Max(0f, s.Fatigue - c.RecoveryPerTick);
            if (s.Fatigue <= 1f - c.RecoverFraction) s.Winded = false;
            s.VelY = 0f;
            s.Grounded = true;
            s.Y = map.HeightAt(s.X, s.Z);
        }

        /// <summary>True on a tile with a deck built over the water (a Pier): walked on, and in a boat's way.</summary>
        public static bool DeckOn(ConstructField? constructs, int tx, int tz) => constructs?.At(tx, tz) is { Def.Deck: true };

        /// <summary>Where a boat may be: water with nothing built over it, or a Small Port's bay (its middle column, between its pontoons).</summary>
        public static bool BoatGoes(WorldMap map, ConstructField? constructs, int tx, int tz) =>
            map.IsWater(tx, tz) && (constructs?.At(tx, tz) is not { } built || (built.Def.IsPort && built.IndexOf(tx, tz) / built.Def.Length == 1));

        /// <summary>True where a carrier may stand: no house or wall of the map, and over water only on a deck.</summary>
        public static bool Walkable(WorldMap map, ConstructField? constructs, int tx, int tz) =>
            map.InBounds(tx, tz) && !map.IsSolid(tx, tz) && (!map.IsWater(tx, tz) || DeckOn(constructs, tx, tz))
            && !(constructs?.At(tx, tz) is { Def.Solid: true });

        /// <summary>
        /// Where one getting out of a boat at (x, z) steps (doc 04 §3.3 "Rowboat mooring"): the
        /// nearest point of a pier's deck or of the shore within <see cref="LandingReach"/>, a body's
        /// width in from the edge; false on open water. The same on the server and in prediction.
        /// </summary>
        public static bool Landing(WorldMap map, ConstructField? constructs, float x, float z, out float lx, out float lz)
        {
            const float tile = SimConstants.TileSize, inset = 0.45f;
            int ox = (int)MathF.Floor(x / tile), oz = (int)MathF.Floor(z / tile);
            float best = LandingReach * LandingReach;
            bool found = false;
            lx = x; lz = z;
            for (int tz = oz - 2; tz <= oz + 2; tz++)
                for (int tx = ox - 2; tx <= ox + 2; tx++)
                {
                    if (!Walkable(map, constructs, tx, tz) || map.SolidPropsAt(tx, tz) is { Length: > 0 }) continue;
                    float px = Math.Clamp(x, tx * tile + inset, (tx + 1) * tile - inset), pz = Math.Clamp(z, tz * tile + inset, (tz + 1) * tile - inset);
                    float d = (px - x) * (px - x) + (pz - z) * (pz - z);
                    if (d >= best) continue;
                    best = d; lx = px; lz = pz; found = true;
                }
            return found;
        }

        /// <summary>How far from a boat its rower can step ashore.</summary>
        public const float LandingReach = 2.6f;

        /// <summary>
        /// The nearest point of walkable ground or deck to (x, z), however far (design choice 113:
        /// a carrier gone down on the open water, and their bag, wash up there); false on a map
        /// with nowhere to stand.
        /// </summary>
        public static bool Ashore(WorldMap map, ConstructField? constructs, float x, float z, out float lx, out float lz)
        {
            const float tile = SimConstants.TileSize, inset = 0.45f;
            int ox = (int)MathF.Floor(x / tile), oz = (int)MathF.Floor(z / tile);
            float best = float.MaxValue;
            lx = x; lz = z;
            int rings = Math.Max(map.Cols, map.Rows);
            for (int k = 0; k <= rings; k++)
            {
                // Every tile of ring k is at least (k - 1) tiles away: nothing further out can beat what was found.
                float near = Math.Max(0, k - 1) * tile;
                if (best <= near * near) break;
                for (int tz = oz - k; tz <= oz + k; tz++)
                    for (int tx = ox - k; tx <= ox + k; tx++)
                    {
                        if (Math.Max(Math.Abs(tx - ox), Math.Abs(tz - oz)) != k) continue;
                        if (!Walkable(map, constructs, tx, tz) || map.SolidPropsAt(tx, tz) is { Length: > 0 }) continue;
                        float px = Math.Clamp(x, tx * tile + inset, (tx + 1) * tile - inset), pz = Math.Clamp(z, tz * tile + inset, (tz + 1) * tile - inset);
                        float d = (px - x) * (px - x) + (pz - z) * (pz - z);
                        if (d >= best) continue;
                        best = d; lx = px; lz = pz;
                    }
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// Pushes a circle out of every solid tile and solid prop it overlaps. Map edges are solid.
        /// A prop lower than the walker's feet (<paramref name="feetY"/>) is passed over: fences
        /// can be jumped. Carriers and enemies both walk with this; what the carriers have built
        /// (<paramref name="constructs"/>) stands in the way as well, a gate only in a raider's.
        /// </summary>
        public static void ResolveCollisions(ref float x, ref float z, float feetY, WorldMap map, float r,
            ConstructField? constructs = null, bool carrier = true, bool boat = false, bool jumper = false)
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
                        if (!(boat ? !BoatGoes(map, constructs, tx, tz) : map.IsSolid(tx, tz) || (map.IsWater(tx, tz) && !DeckOn(constructs, tx, tz)))) continue;
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
                if (!boat && constructs != null && constructs.Push(ref x, ref z, feetY, r, carrier, jumper)) pushed = true;
                if (!pushed) break;
            }
        }
    }
}
