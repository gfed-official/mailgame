using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Vehicles;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// The vehicles of the run (doc 04 §3.1, doc 06 §3 "vehicle movement: S with driver
    /// prediction"). A bought vehicle stands parked by the counter; who is beside it gets into the
    /// saddle with a request, and from then on it is part of the rider's movement state, stepped
    /// by <see cref="PlayerMovement.Step"/> on the server and in the rider's prediction alike.
    /// The jump key gets off; so does going down or losing the connection. The cargo grid rides
    /// with it and is the rider's to open. What is bought is the team's until the run ends.
    /// </summary>
    public sealed partial class SimWorld
    {
        private readonly List<Vehicle> _vehicles = new List<Vehicle>();

        public IReadOnlyList<Vehicle> Vehicles => _vehicles;

        public Vehicle? GetVehicle(EntityId id)
        {
            foreach (var v in _vehicles) if (v.Id == id) return v;
            return null;
        }

        /// <summary>The vehicle the player sits on, null on foot.</summary>
        public Vehicle? RiddenBy(EntityId player)
        {
            if (player.IsNone) return null;
            foreach (var v in _vehicles) if (v.Rider == player) return v;
            return null;
        }

        /// <summary>
        /// Puts a vehicle down parked (tests, kits; the shop's purchase goes through here). Null if
        /// there is no free spot by the counter.
        /// </summary>
        public Vehicle? AddVehicle(VehicleDef def, EntityId buyer)
        {
            if (!FindParking(out float x, out float z)) return null;
            return AddVehicleAt(def, x, z, Map.PostOffice.SpawnYaw, buyer);
        }

        public Vehicle AddVehicleAt(VehicleDef def, float x, float z, float heading, EntityId buyer = default)
        {
            var cargo = CreateContainer(def.Cargo.Id, EntityId.None, x, z);
            var v = new Vehicle(_entityIds.Allocate(EntityClass.Vehicle), def, cargo.Container, x, z, heading);
            cargo.Vehicle = v;
            _vehicles.Add(v);
            Events.Add(new VehicleAddedEvent
            {
                Vehicle = v.Id, Def = def.Index, X = x, Z = z, Heading = heading, CargoContainer = cargo.Container.Id, Buyer = buyer,
            });
            if (def.Fuelled) Events.Add(new VehicleFuelEvent { Vehicle = v.Id, Seconds = v.Fuel });
            return v;
        }

        /// <summary>
        /// The free tile nearest the counter (the spawn pad if the map has no shop) to park a new
        /// vehicle on: open walkable ground with nothing on it — no prop, nothing built, no other
        /// vehicle — and clear of the counter, the Intake and the pad's seats.
        /// </summary>
        private bool FindParking(out float x, out float z)
        {
            var po = Map.PostOffice;
            float ox = po.HasShop ? po.ShopX : po.SpawnPadX, oz = po.HasShop ? po.ShopZ : po.SpawnPadZ;
            const float tile = SimConstants.TileSize;
            int otx = (int)MathF.Floor(ox / tile), otz = (int)MathF.Floor(oz / tile);
            float best = float.MaxValue;
            x = z = 0f;
            for (int ring = 0; ring <= 6; ring++)
            {
                for (int tz = otz - ring; tz <= otz + ring; tz++)
                {
                    for (int tx = otx - ring; tx <= otx + ring; tx++)
                    {
                        if (Math.Max(Math.Abs(tx - otx), Math.Abs(tz - otz)) != ring) continue;
                        if (!ParkingFree(tx, tz)) continue;
                        float cx = (tx + 0.5f) * tile, cz = (tz + 0.5f) * tile;
                        float d = (cx - ox) * (cx - ox) + (cz - oz) * (cz - oz);
                        if (d < best) { best = d; x = cx; z = cz; }
                    }
                }
                if (best < float.MaxValue) return true;
            }
            return false;
        }

        private bool ParkingFree(int tx, int tz)
        {
            if (!Map.InBounds(tx, tz) || Map.IsSolid(tx, tz) || Map.IsNavBlocked(tx, tz)) return false;
            var props = Map.SolidPropsAt(tx, tz);
            if (props != null && props.Length > 0) return false;
            if (Constructs.At(tx, tz) != null) return false;
            const float tile = SimConstants.TileSize;
            float cx = (tx + 0.5f) * tile, cz = (tz + 0.5f) * tile;
            var po = Map.PostOffice;
            if (Near(cx, cz, po.IntakeX, po.IntakeZ, 1.6f)) return false;
            if (po.HasShop && Near(cx, cz, po.ShopX, po.ShopZ, 1.6f)) return false;
            for (int seat = 0; seat < PostOfficeDef.Seats; seat++)
            {
                po.Seat(seat, out float sx, out float sz);
                if (Near(cx, cz, sx, sz, 1.2f)) return false;
            }
            foreach (var v in _vehicles)
                if (Near(cx, cz, v.X, v.Z, 1.0f)) return false;
            return true;
        }

        private static bool Near(float ax, float az, float bx, float bz, float r) => (ax - bx) * (ax - bx) + (az - bz) * (az - bz) < r * r;

        private OpResult MountOp(Player player, in SimRequest r)
        {
            if (player.Dead) return OpResult.Dead;
            if (player.Move.Riding || RiddenBy(player.Id) != null) return OpResult.AlreadyRiding;
            var v = GetVehicle(new EntityId(r.EntryId));
            if (v == null) return OpResult.BadRequest;
            if (v.Ridden) return OpResult.Ridden;
            if (!player.WasWithin(v.X, v.Z, _serverInteractRange + v.Def.BodyOffset, _rewindTicks)) return OpResult.OutOfRange;
            if (v.Def.Fuelled && v.Fuel <= 0f) return OpResult.NoFuel;

            CloseExternal(player, notify: true);
            v.Rider = player.Id;
            Vehicle.Forward(v.Heading, out float hx, out float hz);
            player.Move.X = v.X;
            player.Move.Z = v.Z;
            player.Move.Y = Map.HeightAt(v.X, v.Z);
            player.Move.VelY = 0f;
            player.Move.Grounded = true;
            player.Move.Ride = (byte)(v.Def.Index + 1);
            player.Move.RideSpeed = 0f;
            player.Move.HeadX = hx;
            player.Move.HeadZ = hz;
            Events.Add(new VehicleMountedEvent { Vehicle = v.Id, Player = player.Id });
            TakeOver(v, player);
            return OpResult.Ok;
        }

        /// <summary>
        /// After the players have moved: a ridden vehicle is where its rider is. Whoever is off —
        /// by the jump key in their step, gone down, away, put somewhere by the server, or gone —
        /// leaves it parked where it last was.
        /// </summary>
        private void StepVehicles()
        {
            foreach (var v in _vehicles)
            {
                if (!v.Ridden) continue;
                var rider = GetPlayer(v.Rider);
                if (rider != null && rider.Move.Ride == v.Def.Index + 1 && !rider.Dead && !rider.Away)
                {
                    v.X = rider.Move.X;
                    v.Z = rider.Move.Z;
                    v.Heading = Vehicle.YawOf(rider.Move.HeadX, rider.Move.HeadZ, v.Heading);
                    MoveCargo(v);
                    if (!v.Def.Fuelled) continue;
                    // Doc 04 §3.1: it burns while it rolls; dry, it stops and its driver gets down.
                    if (rider.Move.RideSpeed > 0.1f) v.Fuel = MathF.Max(0f, v.Fuel - SimConstants.TickDt);
                    if (v.Fuel <= 0f)
                    {
                        Events.Add(new VehicleFuelEvent { Vehicle = v.Id, Seconds = 0f });
                        Park(v, rider);
                    }
                    else if (Tick % SimConstants.TickRate == 0) Events.Add(new VehicleFuelEvent { Vehicle = v.Id, Seconds = v.Fuel });
                    continue;
                }
                Park(v, rider);
            }
        }

        /// <summary>Who joins hears how much fuel each vehicle that burns it has.</summary>
        public void SendVehicleFuel(EntityId player)
        {
            foreach (var v in _vehicles)
                if (v.Def.Fuelled) Events.Add(new VehicleFuelEvent { Audience = player, Vehicle = v.Id, Seconds = v.Fuel });
        }

        /// <summary>Doc 04 §3.1: an Oil Can out of the pockets (the hand's first) into the tank of a vehicle beside the carrier.</summary>
        private OpResult RefuelOp(Player player, in SimRequest r)
        {
            if (player.Dead) return OpResult.Dead;
            var v = GetVehicle(new EntityId(r.EntryId));
            if (v == null || !v.Def.Fuelled) return OpResult.BadRequest;
            if (!player.WasWithin(v.X, v.Z, _serverInteractRange + v.Def.BodyOffset, _rewindTicks)) return OpResult.OutOfRange;
            if (v.Fuel + v.Def.FuelSecondsPerCan > v.Def.TankSeconds + 0.5f) return OpResult.TankFull;
            var can = Content.Item("oil_can");
            foreach (var pocket in new[] { player.Hotbar, player.Inventory })
            {
                foreach (var e in pocket.Entries)
                {
                    if (e.Stack.Kind != StackKind.Item || e.Stack.DefIndex != can.Index) continue;
                    if (e.Stack.ItemCount > 1) { e.Stack.ItemCount--; pocket.MarkDirty(e.EntryId); }
                    else pocket.Remove(e.EntryId);
                    CommitContainer(pocket.Id);
                    v.Fuel = MathF.Min(v.Def.TankSeconds, v.Fuel + v.Def.FuelSecondsPerCan);
                    Events.Add(new VehicleFuelEvent { Vehicle = v.Id, Seconds = v.Fuel });
                    return OpResult.Ok;
                }
            }
            return OpResult.NoOilCan;
        }

        /// <summary>A vehicle standing parked whose rear (its loading face) is on the tile; null for none.</summary>
        public Vehicle? LoadingFaceOn(int tx, int tz)
        {
            foreach (var v in _vehicles)
            {
                if (!v.Def.LoadingFace || v.Ridden || v.NpcDriving) continue;
                Vehicle.Rear(v.Def, v.X, v.Z, v.Heading, out float rx, out float rz);
                if ((int)MathF.Floor(rx / SimConstants.TileSize) == tx && (int)MathF.Floor(rz / SimConstants.TileSize) == tz) return v;
            }
            return null;
        }

        private void Park(Vehicle v, Player? rider)
        {
            if (rider != null && rider.Move.Ride != 0)
            {
                rider.Move.Ride = 0;
                rider.Move.RideSpeed = 0f;
            }
            var by = v.Rider;
            v.Rider = EntityId.None;
            SnapIntoBay(v);
            MoveCargo(v);
            Events.Add(new VehicleParkedEvent { Vehicle = v.Id, X = v.X, Z = v.Z, Heading = v.Heading, Player = by });
            HandBack(v);
        }

        private void MoveCargo(Vehicle v)
        {
            if (!_containers.TryGetValue(v.Cargo.Id, out var info)) return;
            info.X = v.X;
            info.Z = v.Z;
        }

        /// <summary>Takes a vehicle away (the smoke, tests): its rider set down, the mail in its cargo back to the Intake.</summary>
        public bool RemoveVehicle(Vehicle v)
        {
            if (!_vehicles.Contains(v)) return false;
            var rider = GetPlayer(v.Rider);
            if (rider != null) { rider.Move.Ride = 0; rider.Move.RideSpeed = 0f; }
            foreach (var p in _players)
                if (p.OpenContainer == v.Cargo.Id) CloseExternal(p, notify: true);
            foreach (var e in v.Cargo.Entries)
                if (e.Stack.Kind == StackKind.Mail)
                    foreach (var mail in e.Stack.Mail) Spawner.ReturnToIntake(mail);
            CommitContainer(Intake.Id);
            _containers.Remove(v.Cargo.Id);
            _vehicles.Remove(v);
            Events.Add(new VehicleRemovedEvent { Vehicle = v.Id });
            return true;
        }

        /// <summary>A run ends or begins: no vehicle is left, nor what was in its cargo (the run's mail is gone with the run).</summary>
        private void ClearVehicles()
        {
            if (_vehicles.Count == 0) return;
            foreach (var v in _vehicles)
            {
                var rider = GetPlayer(v.Rider);
                if (rider != null) { rider.Move.Ride = 0; rider.Move.RideSpeed = 0f; }
                foreach (var p in _players)
                    if (p.OpenContainer == v.Cargo.Id) CloseExternal(p, notify: true);
                _containers.Remove(v.Cargo.Id);
            }
            _vehicles.Clear();
            Events.Add(new VehiclesClearedEvent());
        }
    }
}
