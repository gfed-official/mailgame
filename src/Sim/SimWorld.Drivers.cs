using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.Vehicles;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>What a Vehicle Depot's NPC driver is doing (doc 04 §3.2).</summary>
    public enum DriverState : byte
    {
        /// <summary>Nobody is hired at the depot.</summary>
        None = 0,
        /// <summary>Hired, at the depot: waiting for a truck in the bay with mail for the route, in Delivery.</summary>
        Waiting = 1,
        /// <summary>Driving the route to its next house.</summary>
        Out = 2,
        /// <summary>Stopped at a house, putting its mail into the mailbox at the box's rate.</summary>
        Unloading = 3,
        /// <summary>The route is done: back to the depot.</summary>
        Returning = 4,
        /// <summary>Raiders near the route (doc 04 §3.2 "NPC drivers flee raids"): back to the depot.</summary>
        Fleeing = 5,
        /// <summary>The tank ran dry on the way; an Oil Can puts it back on the road.</summary>
        Stranded = 6,
        /// <summary>A carrier took the wheel (doc 04 §3.2 "player take-over"): the driver rides along and hands the mail out at the route's houses.</summary>
        Passenger = 7,
    }

    /// <summary>The NPC driver hired at a Vehicle Depot, and the truck they run.</summary>
    public sealed class DepotDriver
    {
        public Construct Depot = null!;
        public DriverState State;
        /// <summary>The state to go back to when the tank is filled again or the carrier at the wheel gets off.</summary>
        public DriverState Resume;
        public Vehicle? Truck;
        /// <summary>The houses of this trip, in order, and the one being driven to or unloaded at.</summary>
        public readonly List<DestinationDef> Houses = new List<DestinationDef>();
        public int HouseAt;
        /// <summary>The way being driven along the streets, and the point next driven at.</summary>
        public readonly List<(float X, float Z)> Path = new List<(float X, float Z)>();
        public int PathAt;
        public uint NextDeliverTick;
        /// <summary>Waiting: mail in the truck when last looked, and the tick it may set out if no more comes.</summary>
        public int LoadSeen = -1;
        public uint DepartTick;
        public int Delivered;
        public bool Hired => State != DriverState.None;
    }

    /// <summary>
    /// The NPC drivers of the Vehicle Depots (doc 04 §3.2, doc 10 §3 npc_driver, doc 11 NPC speed
    /// ratio 0.6). Hired at a depot's console for a shift; in Delivery the driver takes the truck
    /// standing in the bay out when it holds mail for the route and nothing more has been loaded
    /// for a few seconds, drives it along the streets at 60 % of a carrier's speed to the route's
    /// houses that it has mail for, puts that mail into their mailboxes at their insert rate by the
    /// rules a belt goes by (it never guesses), and comes back to the bay. Raiders near the way
    /// send it back early. A carrier may take the wheel at any time; the driver rides along and
    /// hands the mail out wherever the truck stands at a house of the route, and takes over again
    /// where the carrier gets off. At Payday the hire ends and the truck is put back in its bay.
    /// </summary>
    public sealed partial class SimWorld
    {
        private readonly Dictionary<Construct, DepotDriver> _drivers = new Dictionary<Construct, DepotDriver>();
        private readonly List<Construct> _driverScratch = new List<Construct>();

        public IEnumerable<DepotDriver> Drivers => _drivers.Values;

        public DepotDriver? DriverAt(Construct depot) => _drivers.TryGetValue(depot, out var d) ? d : null;

        /// <summary>The driver with a truck (at its wheel or riding along); null for none.</summary>
        public DepotDriver? DriverOf(Vehicle v)
        {
            foreach (var d in _drivers.Values) if (d.Truck == v) return d;
            return null;
        }

        /// <summary>Doc 01 §7 / doc 10 §3: a driver from shift 2 for a solo carrier, shift 3 for a group.</summary>
        public int DriverHireShift => (int)Content.Balance.GetFloat(PresentCount <= 1 ? "npcHireShiftSolo" : "npcHireShiftGroup");

        /// <summary>Doc 10 §3 npc_captain: from shift 3, at a Small Port.</summary>
        public int CaptainHireShift => (int)Content.Balance.GetFloat("npcCaptainHireShift");

        /// <summary>The shift from which the crew of a depot (a driver) or a port (a captain) is for hire.</summary>
        public int HireShift(Construct built) => built.Def.IsPort ? CaptainHireShift : DriverHireShift;

        private OpResult HireOp(Player player, Construct depot)
        {
            if (!depot.Def.HasConsole) return OpResult.BadRequest;
            if (Run.Phase != RunPhase.Prep && Run.Phase != RunPhase.Delivery) return OpResult.WrongPhase;
            if (Run.Shift < HireShift(depot)) return OpResult.ShopLocked;
            if (_drivers.TryGetValue(depot, out var had) && had.Hired) return OpResult.AlreadyHired;
            if (!Economy.Spend(depot.Def.HireCost)) return OpResult.CantAfford;
            var d = had ?? (_drivers[depot] = new DepotDriver { Depot = depot });
            d.State = DriverState.Waiting;
            d.LoadSeen = -1;
            d.Delivered = 0;
            Tell(d, player.Id);
            return OpResult.Ok;
        }

        /// <summary>In the tick, after the vehicles: every driver's part of it.</summary>
        private void StepDrivers()
        {
            if (_drivers.Count == 0) return;
            _driverScratch.Clear();
            foreach (var depot in _drivers.Keys) _driverScratch.Add(depot);
            foreach (var depot in _driverScratch)
            {
                var d = _drivers[depot];
                // Taken down: the driver is gone, the truck stops where it is.
                if (Constructs.At(depot.Tx, depot.Tz) != depot)
                {
                    Dismiss(d);
                    _drivers.Remove(depot);
                    continue;
                }
                if (!d.Hired) continue;
                // The hire is for the shift (doc 10 §3 "per shift"): at Payday the truck is put back in the bay.
                if (Run.Phase != RunPhase.Prep && Run.Phase != RunPhase.Delivery)
                {
                    Dismiss(d, home: true);
                    continue;
                }
                if (d.Truck != null && !_vehicles.Contains(d.Truck))
                {
                    d.Truck = null;
                    SetState(d, DriverState.Waiting);
                }
                switch (d.State)
                {
                    case DriverState.Waiting: StepWaiting(d); break;
                    case DriverState.Out:
                    case DriverState.Returning:
                    case DriverState.Fleeing: StepDriving(d); break;
                    case DriverState.Unloading: StepUnloading(d); break;
                    case DriverState.Stranded:
                        if (d.Truck!.Fuel > 0f) Resume(d);
                        break;
                    case DriverState.Passenger: StepPassenger(d); break;
                }
            }
        }

        private void StepWaiting(DepotDriver d)
        {
            var truck = InBay(d.Depot);
            if (truck == null || !truck.Def.LoadingFace || Run.Phase != RunPhase.Delivery || truck.Fuel <= 0f && truck.Def.Fuelled) { d.LoadSeen = -1; return; }
            int load = MailCount(truck.Cargo);
            // Doc 11 "waits at stops": it sets out once nothing more has come for a few seconds.
            if (load != d.LoadSeen)
            {
                d.LoadSeen = load;
                d.DepartTick = Tick + SimConstants.SecondsToTicks(Content.Balance.GetDouble("npcDepartWaitSeconds"));
                return;
            }
            if (Tick < d.DepartTick) return;
            PlanTrip(d, truck);
            if (d.Houses.Count == 0)
            {
                // Nothing in it for the route yet: look again after the same wait.
                d.LoadSeen = -1;
                return;
            }
            d.Truck = truck;
            truck.NpcDriving = true;
            d.HouseAt = 0;
            if (!PathTo(d, d.Houses[0]))
            {
                truck.NpcDriving = false;
                d.Truck = null;
                d.LoadSeen = -1;
                return;
            }
            SetState(d, DriverState.Out);
        }

        /// <summary>The houses of the route the truck holds mail for that their boxes would take now.</summary>
        private void PlanTrip(DepotDriver d, Vehicle truck)
        {
            var stops = new List<MailFilter>(d.Depot.Filters);
            // A captain goes where a boat can lie by the destination, and sail to (doc 04 §3.3).
            RoutePlan.Houses(Map, stops, truck.X, truck.Z,
                dest => HasMailFor(truck, dest) && (!d.Depot.Def.IsPort || CanSailTo(truck, dest)), d.Houses);
        }

        private readonly List<(float X, float Z)> _sailScratch = new List<(float X, float Z)>();

        private bool CanSailTo(Vehicle boat, DestinationDef dest) =>
            WaterNav.Berth(Map, Constructs, dest, Content.Balance.GetFloat("dockZoneReach"), out float bx, out float bz)
            && WaterNav.Route(Map, Constructs, boat.X, boat.Z, bx, bz, boat.Def.Radius, _sailScratch) >= 0f;

        private bool HasMailFor(Vehicle truck, DestinationDef dest)
        {
            foreach (var e in truck.Cargo.Entries)
            {
                if (e.Stack.Kind != StackKind.Mail || !dest.Takes(e.Stack.Address)) continue;
                foreach (var mail in e.Stack.Mail)
                    if (AutomatedDelivers(dest, mail)) return true;
            }
            return false;
        }

        private static int MailCount(GridContainer c)
        {
            int n = 0;
            foreach (var e in c.Entries) if (e.Stack.Kind == StackKind.Mail) n += e.Stack.Mail.Count;
            return n;
        }

        /// <summary>The way to a house: along the streets to its mailbox, or for a captain over the water to its berth. False if a boat cannot get there.</summary>
        private bool PathTo(DepotDriver d, DestinationDef dest)
        {
            if (!d.Depot.Def.IsPort) return PathTo(d, dest.MailboxX, dest.MailboxZ);
            return WaterNav.Berth(Map, Constructs, dest, Content.Balance.GetFloat("dockZoneReach"), out float bx, out float bz) && PathTo(d, bx, bz);
        }

        private bool PathTo(DepotDriver d, float x, float z)
        {
            var v = d.Truck!;
            d.PathAt = 1;
            if (!d.Depot.Def.IsPort)
            {
                Map.Routes.Route(v.X, v.Z, x, z, d.Path);
                return true;
            }
            if (WaterNav.Route(Map, Constructs, v.X, v.Z, x, z, v.Def.Radius, d.Path) >= 0f) return true;
            // Cut off (a pier built across the way): it stays where it is.
            d.Path.Clear();
            d.Path.Add((v.X, v.Z));
            return false;
        }

        private void PathHome(DepotDriver d)
        {
            Bay(d.Depot, out float bx, out float bz, out _);
            PathTo(d, bx, bz);
        }

        /// <summary>Along the way at 60 % of a carrier's top speed for the ground (doc 04 §3.2), burning fuel; at its end, the next thing.</summary>
        private void StepDriving(DepotDriver d)
        {
            var v = d.Truck!;
            if (v.Def.Fuelled && v.Fuel <= 0f)
            {
                d.Resume = d.State;
                SetState(d, DriverState.Stranded);
                return;
            }
            if (d.State == DriverState.Out && Tick % SimConstants.TickRate == 0 && RaidersNear(d))
            {
                PathHome(d);
                SetState(d, DriverState.Fleeing);
                return;
            }
            float ratio = Content.Balance.GetFloat("npcSpeedRatio");
            float speed = ratio * (v.Def.OnWater ? v.Def.WaterSpeed : PlayerMovement.OnRoad(Map, v.X, v.Z) ? v.Def.RoadSpeed * RoadMult : v.Def.OffRoadSpeed);
            // Doc 10 §6 Street Festival: the crowd lets a driver through at a crawl, as it does a carrier.
            if (!v.Def.OnWater && RoadClosure is var closure && PlayerMovement.Closed(closure.Rect, v.X, v.Z)) speed *= closure.Mult;
            float left = speed * SimConstants.TickDt;
            float reach = d.State == DriverState.Out ? Content.Balance.GetFloat("npcStopReach") : 0.05f;
            float goalX = d.Path[d.Path.Count - 1].X, goalZ = d.Path[d.Path.Count - 1].Z;
            while (left > 0f && d.PathAt < d.Path.Count)
            {
                if (Near(v.X, v.Z, goalX, goalZ, reach)) break;
                var (px, pz) = d.Path[d.PathAt];
                float dx = px - v.X, dz = pz - v.Z, dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist > 1e-4f) v.Heading = Vehicle.YawOf(dx, dz, v.Heading);
                if (dist <= left)
                {
                    v.X = px;
                    v.Z = pz;
                    left -= dist;
                    d.PathAt++;
                }
                else
                {
                    v.X += dx / dist * left;
                    v.Z += dz / dist * left;
                    left = 0f;
                }
            }
            MoveCargo(v);
            if (v.Def.Fuelled)
            {
                v.Fuel = MathF.Max(0f, v.Fuel - SimConstants.TickDt);
                if (Tick % SimConstants.TickRate == 0 || v.Fuel <= 0f) Events.Add(new VehicleFuelEvent { Vehicle = v.Id, Seconds = v.Fuel });
            }
            if (!Near(v.X, v.Z, goalX, goalZ, reach) && d.PathAt < d.Path.Count) return;
            if (d.State == DriverState.Out)
            {
                d.NextDeliverTick = Tick;
                SetState(d, DriverState.Unloading);
                return;
            }
            // Home: into the bay, parked.
            Bay(d.Depot, out v.X, out v.Z, out v.Heading);
            v.NpcDriving = false;
            MoveCargo(v);
            Events.Add(new VehicleParkedEvent { Vehicle = v.Id, X = v.X, Z = v.Z, Heading = v.Heading });
            d.Truck = null;
            d.LoadSeen = -1;
            SetState(d, DriverState.Waiting);
        }

        /// <summary>At a house: one piece a turn of the mailbox's insert rate, then on to the next house that still has mail, or home.</summary>
        private void StepUnloading(DepotDriver d)
        {
            if (Run.Phase != RunPhase.Delivery)
            {
                PathHome(d);
                SetState(d, DriverState.Returning);
                return;
            }
            if (Tick < d.NextDeliverTick) return;
            var house = d.Houses[d.HouseAt];
            if (DeliverOne(d, house)) return;
            // Done here: the next house it still has mail for.
            while (++d.HouseAt < d.Houses.Count)
                if (HasMailFor(d.Truck!, d.Houses[d.HouseAt])) break;
            if (d.HouseAt < d.Houses.Count && PathTo(d, d.Houses[d.HouseAt]))
                SetState(d, DriverState.Out);
            else
            {
                PathHome(d);
                SetState(d, DriverState.Returning);
            }
        }

        /// <summary>One piece out of the truck into a house's box, if it holds one the box takes; the box's rate decides the next turn.</summary>
        private bool DeliverOne(DepotDriver d, DestinationDef house)
        {
            if (!UnloadOne(d.Truck!.Cargo, house, d.Depot.Def.IsPort ? DeliveryMachine.Captain : DeliveryMachine.Driver)) return false;
            d.Delivered++;
            d.NextDeliverTick = Tick + InsertTicks(house);
            return true;
        }

        /// <summary>Ticks between two pieces into a destination: its insert rate.</summary>
        private static uint InsertTicks(DestinationDef dest) => Math.Max(1u, SimConstants.SecondsToTicks(1.0 / Math.Max(0.01, dest.Type.InsertRate)));

        /// <summary>One piece out of a vehicle's cargo into a destination, if it holds one the destination takes by the belts' rules.</summary>
        private bool UnloadOne(GridContainer cargo, DestinationDef dest, DeliveryMachine machine)
        {
            foreach (var e in cargo.Entries)
            {
                if (e.Stack.Kind != StackKind.Mail || !dest.Takes(e.Stack.Address)) continue;
                var list = e.Stack.Mail;
                for (int i = 0; i < list.Count; i++)
                {
                    if (!AutomatedDelivers(dest, list[i])) continue;
                    var mail = list[i];
                    list.RemoveAt(i);
                    if (list.Count == 0) cargo.Remove(e.EntryId);
                    else cargo.MarkDirty(e.EntryId);
                    CommitContainer(cargo.Id);
                    DeliverAutomated(dest, mail, machine);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Doc 04 §3.2 "if enemies are within 20 m of the route": of the truck, or of a house still to come.</summary>
        private bool RaidersNear(DepotDriver d)
        {
            if (_enemies.Count == 0) return false;
            float r = Content.Balance.GetFloat("npcFleeRadius");
            foreach (var e in _enemies)
            {
                if (Near(e.X, e.Z, d.Truck!.X, d.Truck.Z, r)) return true;
                for (int i = d.PathAt; i < d.Path.Count; i++)
                    if (Near(e.X, e.Z, d.Path[i].X, d.Path[i].Z, r)) return true;
                for (int i = d.HouseAt; i < d.Houses.Count; i++)
                    if (Near(e.X, e.Z, d.Houses[i].MailboxX, d.Houses[i].MailboxZ, r)) return true;
            }
            return false;
        }

        /// <summary>A carrier at the wheel: the driver hands the mail out at a house of the route wherever the truck stands still beside its box.</summary>
        private void StepPassenger(DepotDriver d)
        {
            var v = d.Truck!;
            var rider = GetPlayer(v.Rider);
            if (rider == null || rider.Move.RideSpeed > 0.5f || Run.Phase != RunPhase.Delivery || Tick < d.NextDeliverTick) return;
            float reach = Content.Balance.GetFloat("npcStopReach");
            foreach (var house in d.Houses)
                if (Near(v.X, v.Z, house.MailboxX, house.MailboxZ, reach) && DeliverOne(d, house)) return;
        }

        /// <summary>Doc 04 §3.2: a carrier gets in while the driver drives; the driver moves over.</summary>
        private void TakeOver(Vehicle v, Player player)
        {
            var d = DriverOf(v);
            if (d == null || d.State == DriverState.Waiting) return;
            v.NpcDriving = false;
            d.Resume = d.State == DriverState.Stranded ? d.Resume : d.State;
            SetState(d, DriverState.Passenger, player.Id);
        }

        /// <summary>The carrier got off: the driver drives again from where the truck stands, in the same tick.</summary>
        private void HandBack(Vehicle v)
        {
            var d = DriverOf(v);
            if (d == null || d.State != DriverState.Passenger) return;
            // Left in the bay: the driver is home.
            if (InBay(d.Depot) == v)
            {
                d.Truck = null;
                d.LoadSeen = -1;
                SetState(d, DriverState.Waiting);
                return;
            }
            v.NpcDriving = true;
            Resume(d);
        }

        /// <summary>On the road again after a stop: to the house it was going to (or unloading at), or home.</summary>
        private void Resume(DepotDriver d)
        {
            var to = d.Resume;
            if (to == DriverState.Unloading) to = DriverState.Out;
            if (to == DriverState.Out && d.HouseAt < d.Houses.Count)
            {
                while (d.HouseAt < d.Houses.Count && !HasMailFor(d.Truck!, d.Houses[d.HouseAt])) d.HouseAt++;
            }
            if (to == DriverState.Out && d.HouseAt < d.Houses.Count && PathTo(d, d.Houses[d.HouseAt])) { }
            else
            {
                if (to == DriverState.Out) to = DriverState.Returning;
                PathHome(d);
            }
            d.Truck!.NpcDriving = true;
            SetState(d, to);
        }

        /// <summary>The hire ends: the truck stops where it is, or (at Payday) is put back in its bay if that is free.</summary>
        private void Dismiss(DepotDriver d, bool home = false)
        {
            var v = d.Truck;
            if (v != null && _vehicles.Contains(v) && v.NpcDriving)
            {
                v.NpcDriving = false;
                if (home && (InBay(d.Depot) == null) && Constructs.At(d.Depot.Tx, d.Depot.Tz) == d.Depot) Bay(d.Depot, out v.X, out v.Z, out v.Heading);
                MoveCargo(v);
                Events.Add(new VehicleParkedEvent { Vehicle = v.Id, X = v.X, Z = v.Z, Heading = v.Heading });
            }
            d.Truck = null;
            d.Houses.Clear();
            d.Path.Clear();
            if (d.State != DriverState.None) SetState(d, DriverState.None);
        }

        private void ClearDrivers()
        {
            foreach (var v in _vehicles) v.NpcDriving = false;
            _drivers.Clear();
        }

        private void SetState(DepotDriver d, DriverState state, EntityId by = default)
        {
            d.State = state;
            Tell(d, by);
        }

        private void Tell(DepotDriver d, EntityId by, EntityId audience = default)
        {
            var house = d.HouseAt < d.Houses.Count && d.State is DriverState.Out or DriverState.Unloading or DriverState.Passenger or DriverState.Stranded
                ? d.Houses[d.HouseAt] : null;
            Events.Add(new DriverEvent
            {
                Audience = audience, X = (byte)d.Depot.Tx, Y = (byte)d.Depot.Tz, State = d.State, Vehicle = d.Truck?.Id ?? EntityId.None,
                House = house?.Id ?? DriverEvent.NoHouse, Houses = (byte)Math.Min(255, d.Houses.Count), At = (byte)Math.Min(255, d.HouseAt),
                Delivered = (ushort)Math.Min(ushort.MaxValue, d.Delivered), Player = by,
            });
        }

        /// <summary>Who joins hears what every driver is doing.</summary>
        public void SendDrivers(EntityId player)
        {
            foreach (var d in _drivers.Values)
                if (d.Hired) Tell(d, EntityId.None, player);
        }
    }
}
