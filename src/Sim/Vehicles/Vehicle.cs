using System;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;

namespace PerformativeMail.Sim.Vehicles
{
    /// <summary>
    /// A vehicle of the run (doc 04 §3.1): parked where it was left, or under its rider, whose
    /// movement state carries it (<see cref="Players.PlayerMoveState.Ride"/>).
    /// </summary>
    public sealed class Vehicle
    {
        public EntityId Id { get; }
        public VehicleDef Def { get; }
        public GridContainer Cargo { get; }
        /// <summary>Where it stands, or where its rider was at the end of the last tick.</summary>
        public float X, Z;
        /// <summary>The way it points, as a yaw (0 looks along −Z, as the players' does).</summary>
        public float Heading;
        public EntityId Rider;
        public float Hp;

        public Vehicle(EntityId id, VehicleDef def, GridContainer cargo, float x, float z, float heading)
        {
            Id = id; Def = def; Cargo = cargo; X = x; Z = z; Heading = heading; Hp = def.Hp;
        }

        public bool Ridden => !Rider.IsNone;

        /// <summary>The unit vector on the ground a yaw points along (yaw 0 looks along −Z).</summary>
        public static void Forward(float yaw, out float x, out float z)
        {
            x = -MathF.Sin(yaw);
            z = -MathF.Cos(yaw);
        }

        /// <summary>The yaw a direction on the ground points along; <paramref name="fallback"/> for none.</summary>
        public static float YawOf(float x, float z, float fallback) =>
            x == 0f && z == 0f ? fallback : MathF.Atan2(-x, -z);
    }
}
