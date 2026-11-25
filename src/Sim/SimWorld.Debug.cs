using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// The debug world (content/maps/debug_world.json, <c>--debug-world</c>; design choice 119): a
    /// map whose <see cref="WorldMap.Debug"/> block puts every construct up and running as each run
    /// starts — belts, sorters, inserters, storage, pipes, a depot with its truck and driver, a port
    /// with its boat and captain, a Business Dock and an Oil Pump — feeds the Intake one piece of
    /// mail at a time for the addresses in turn, and turns the debug toggles on: nobody is hurt,
    /// the wallet stays full, every blueprint is owned, building takes no materials, the clock
    /// stands and no raid comes unless a player asks for one. <see cref="Showcase"/> watches the
    /// mail go through each station.
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>Nobody of the team takes damage (the debug world's god mode).</summary>
        public bool GodMode => Map.Debug is { GodMode: true };
        /// <summary>The wallet stays full and building takes no materials (the debug world).</summary>
        public bool Unlimited => Map.Debug is { Unlimited: true };
        /// <summary>What the debug world's stations have shown since the run began; null on any other map.</summary>
        public ShowcaseProbe? Showcase { get; private set; }

        private uint _debugMailTick;
        private int _debugMailNext;
        private readonly List<int> _debugMailKinds = new List<int>();
        /// <summary>The turret range's practice raiders: the one each target has up, and when the next may come.</summary>
        private readonly List<(EntityId Up, uint Next)> _debugTargets = new List<(EntityId, uint)>();

        /// <summary>At the end of <see cref="SetUpRun"/>: the debug world's showcase, as a carrier would have built it, and its crews hired.</summary>
        private void BuildShowcase()
        {
            var debug = Map.Debug;
            if (debug == null) return;
            Showcase = new ShowcaseProbe(debug, PumpedCans);
            _debugMailTick = 0;
            _debugMailNext = 0;
            _debugMailKinds.Clear();
            _debugTargets.Clear();
            foreach (var t in debug.Targets)
            {
                if (Content.Enemies.All(e => e.Id != t.Enemy)) throw new InvalidOperationException($"the debug world's target names no enemy '{t.Enemy}'");
                _debugTargets.Add((EntityId.None, 0));
            }
            if (debug.Unlimited)
                foreach (var item in Content.ShopItems)
                    if (item.Kind == ShopKind.Blueprint) Grant(item, EntityId.None);
            foreach (var c in debug.Constructs)
            {
                var def = FindBuilding(c.Building) ?? throw new InvalidOperationException($"the debug world names no building '{c.Building}'");
                var verdict = BuildRules.CheckPlacement(Map, Constructs, def, c.X, c.Z, c.Rotation);
                if (verdict != OpResult.Ok) throw new InvalidOperationException($"the debug world's {def.Name} at ({c.X}, {c.Z}) turned {c.Rotation}: {verdict}");
                var built = Erect(def, c.X, c.Z, c.Rotation, EntityId.None);
                if (c.Filters.Count == 0) continue;
                var filters = new List<MailFilter>(built.Filters);
                for (int i = 0; i < c.Filters.Count && i < filters.Count; i++) filters[i] = ParseFilter(c.Filters[i]);
                Constructs.SetFilters(built, filters);
                Events.Add(Configured(built, EntityId.None));
            }
            foreach (var v in debug.Vehicles)
            {
                VehicleDef? def = null;
                foreach (var d in Content.Vehicles) if (d.Id == v.Vehicle) def = d;
                if (def == null) throw new InvalidOperationException($"the debug world names no vehicle '{v.Vehicle}'");
                var bay = Constructs.At(v.BayX, v.BayZ);
                if (bay == null || !bay.Def.HasConsole) throw new InvalidOperationException($"the debug world's {def.Name} has no depot or port at ({v.BayX}, {v.BayZ})");
                Bay(bay, out float x, out float z, out float heading);
                var vehicle = AddVehicleAt(def, x, z, heading);
                if (v.FuelSeconds >= 0f && def.Fuelled)
                {
                    vehicle.Fuel = MathF.Min(def.TankSeconds, v.FuelSeconds);
                    Events.Add(new VehicleFuelEvent { Vehicle = vehicle.Id, Seconds = vehicle.Fuel });
                }
            }
            foreach (var (x, z) in debug.Hires)
            {
                var at = Constructs.At(x, z);
                if (at == null || !at.Def.HasConsole) throw new InvalidOperationException($"the debug world hires a crew at ({x}, {z}), where no depot or port stands");
                var d = new DepotDriver { Depot = at, State = DriverState.Waiting, LoadSeen = -1 };
                _drivers[at] = d;
                Tell(d, EntityId.None);
            }
        }

        /// <summary>A target whose raider is gone stands another up once its seconds have passed (the turret range, raids or not).</summary>
        private void StepDebugTargets(DebugWorldDef debug)
        {
            for (int i = 0; i < _debugTargets.Count && i < debug.Targets.Count; i++)
            {
                var (up, next) = _debugTargets[i];
                if (!up.IsNone)
                {
                    if (GetEnemy(up) != null) continue;
                    _debugTargets[i] = (EntityId.None, Tick + Math.Max(1u, SimConstants.SecondsToTicks(debug.Targets[i].EverySeconds)));
                    continue;
                }
                if (Tick < next) continue;
                var t = debug.Targets[i];
                var def = Content.Enemies.First(e => e.Id == t.Enemy);
                var raider = SpawnEnemy(def, BuildRules.TileCentre(t.X), BuildRules.TileCentre(t.Z));
                if (raider != null) _debugTargets[i] = (raider.Id, 0);
            }
        }

        private BuildingDef? FindBuilding(string id)
        {
            foreach (var b in Content.Buildings) if (b.Id == id) return b;
            return null;
        }

        /// <summary>"street:NAME", "district:N", "kind:ID", "address:NUMBER STREET", or "" for none.</summary>
        private MailFilter ParseFilter(string text)
        {
            if (text.Length == 0) return default;
            int colon = text.IndexOf(':');
            string kind = colon < 0 ? text : text.Substring(0, colon), value = colon < 0 ? "" : text.Substring(colon + 1);
            switch (kind)
            {
                case "street":
                    foreach (var s in Map.Streets) if (s.Name == value) return new MailFilter(FilterKind.Street, s.Id);
                    break;
                case "district":
                    if (int.TryParse(value, out int district)) return new MailFilter(FilterKind.District, district);
                    break;
                case "kind":
                    foreach (var k in Content.MailKinds) if (k.Id == value) return new MailFilter(FilterKind.Kind, k.Index);
                    break;
                case "address":
                    foreach (var d in Map.Destinations) if (d.Label == value) return new MailFilter(FilterKind.Address, d.Id);
                    break;
            }
            throw new InvalidOperationException($"the debug world's filter \"{text}\" names nothing of the map or the content");
        }

        /// <summary>
        /// In Delivery: one piece into the Intake every <see cref="DebugWorldDef.MailEverySeconds"/>,
        /// for the open destinations in turn, each in turn of the kinds it takes that are out by the
        /// shift; and the wallet topped up. Instead of the spawner's batches.
        /// </summary>
        private void StepDebugWorld()
        {
            var debug = Map.Debug!;
            if (debug.Unlimited) Economy.TopUp(PlaytestMask.MaxWallet);
            if (Run.Phase != RunPhase.Delivery) return;
            StepDebugTargets(debug);
            if (Map.Destinations.Count == 0 || Tick < _debugMailTick) return;
            _debugMailTick = Tick + Math.Max(1u, SimConstants.SecondsToTicks(debug.MailEverySeconds));
            if (_debugMailKinds.Count < Map.Destinations.Count)
            {
                _debugMailKinds.Clear();
                for (int i = 0; i < Map.Destinations.Count; i++) _debugMailKinds.Add(0);
            }
            for (int tries = 0; tries < Map.Destinations.Count; tries++)
            {
                int at = _debugMailNext++ % Map.Destinations.Count;
                var dest = Map.Destinations[at];
                if (Map.OpensAt(dest) > Run.Shift) continue;
                var kinds = Content.MailKinds;
                for (int k = 0; k < kinds.Count; k++)
                {
                    var kind = kinds[(_debugMailKinds[at] + k) % kinds.Count];
                    if (kind.UnlockShift > Run.Shift || !DeliveryRules.KindAccepted(kind, dest.Type)) continue;
                    _debugMailKinds[at] = (kind.Index + 1) % kinds.Count;
                    Spawner.SpawnOne(kind, dest, Run.Shift);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// What the debug world's stations have shown (design choice 119): for each, whether each of
    /// its proofs has come true since the run began — mail onto a belt, a machine's delivery, a can
    /// pumped. The smoke and the tests read it; the sim fills it from its own events every tick.
    /// </summary>
    public sealed class ShowcaseProbe
    {
        public IReadOnlyList<ShowcaseStation> Stations { get; }
        private readonly int[][] _counts;
        private readonly int _pumpedAtStart;

        public ShowcaseProbe(DebugWorldDef debug, int pumpedAtStart)
        {
            Stations = debug.Stations;
            _pumpedAtStart = pumpedAtStart;
            _counts = new int[Stations.Count][];
            for (int i = 0; i < Stations.Count; i++) _counts[i] = new int[Stations[i].Proofs.Count];
        }

        /// <summary>How many times the proof has come true.</summary>
        public int Count(int station, int proof) => _counts[station][proof];

        public bool StationDone(int station)
        {
            foreach (int n in _counts[station]) if (n == 0) return false;
            return true;
        }

        public bool AllDone
        {
            get
            {
                for (int i = 0; i < Stations.Count; i++) if (!StationDone(i)) return false;
                return true;
            }
        }

        /// <summary>"Small Port: a captain delivery; …" for what has not come true yet; empty when all has.</summary>
        public string Missing()
        {
            var parts = new List<string>();
            for (int i = 0; i < Stations.Count; i++)
                for (int p = 0; p < Stations[i].Proofs.Count; p++)
                    if (_counts[i][p] == 0) parts.Add($"{Stations[i].Name}: {Stations[i].Proofs[p]}");
            return string.Join("; ", parts);
        }

        internal void Observe(SimWorld world, List<SimEvent> events, int from)
        {
            int cols = world.Map.Cols;
            for (int i = 0; i < Stations.Count; i++)
            {
                var proofs = Stations[i].Proofs;
                for (int p = 0; p < proofs.Count; p++)
                {
                    var proof = proofs[p];
                    switch (proof.Kind)
                    {
                        case ShowcaseProofKind.Fired:
                            for (int e = from; e < events.Count; e++)
                                if (events[e] is TurretFiredEvent f && f.X == proof.X && f.Y == proof.Z) _counts[i][p]++;
                            break;
                        case ShowcaseProofKind.Pumped:
                            _counts[i][p] = world.PumpedCans - _pumpedAtStart;
                            break;
                        case ShowcaseProofKind.Belt:
                        {
                            var seg = world.Belts.SegmentAt(proof.X, proof.Z);
                            if (seg == null) break;
                            for (int e = from; e < events.Count; e++)
                            {
                                int head = events[e] switch
                                {
                                    BeltItemAddedEvent a => a.Y * cols + a.X,
                                    BeltItemMovedEvent m => m.Y * cols + m.X,
                                    _ => -1,
                                };
                                if (head == seg.Head) _counts[i][p]++;
                            }
                            break;
                        }
                        case ShowcaseProofKind.Delivered:
                            for (int e = from; e < events.Count; e++)
                            {
                                if (events[e] is not DeliveredEvent d || !d.Player.IsNone) continue;
                                if (!string.Equals(d.Machine.ToString(), proof.Machine, StringComparison.OrdinalIgnoreCase)) continue;
                                if (proof.To.Length > 0 && world.Map.GetDestination(d.DestId)?.Label != proof.To) continue;
                                _counts[i][p]++;
                            }
                            break;
                    }
                }
            }
        }
    }
}
