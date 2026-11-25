using System;
using System.Collections.Generic;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.Vehicles;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client
{
    /// <summary>The map's filter chips (doc 09 §2.6): what it highlights on top of what it always shows.</summary>
    public enum MapChip : byte { Mail = 0, Routes = 1, Resources = 2 }

    /// <summary>How a belt tile is drawn on the map.</summary>
    public enum MapBeltKind : byte { Belt, Fast, Raised, Pipe }

    /// <summary>A belt or pipe tile as a short line on the map, from where mail comes in to where it goes out.</summary>
    public readonly struct MapBelt
    {
        public readonly float X0, Z0, X1, Z1;
        public readonly MapBeltKind Kind;
        public MapBelt(float x0, float z0, float x1, float z1, MapBeltKind kind) { X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; Kind = kind; }
    }

    /// <summary>The way a hired crew drives or sails, round its route's stops and home.</summary>
    public sealed class MapCrewRoute
    {
        public Construct Home = null!;
        public bool Captain;
        public readonly List<(float X, float Z)> Points = new List<(float X, float Z)>();
        public readonly List<DestinationDef> Stops = new List<DestinationDef>();
        /// <summary>The house it is on its way to or at now; null while at home.</summary>
        public DestinationDef? Now;
    }

    /// <summary>
    /// The map's layers and filter chips (doc 09 §2.6; design choice 121), worked out from the
    /// client's mirror of the world while the map is up: the belts as lines the way they run and
    /// the pipes, the other constructs, the vehicles, the hired crews' routes (Routes), the
    /// resource nodes not yet spent (Resources), the raiders an Alarm Post reveals, and the houses
    /// with mail waiting for them in the Intake, the storage or one's own pockets (Mail). The
    /// panel draws it; the counts are what the smoke and the tests read.
    /// </summary>
    public sealed class MapLayers
    {
        /// <summary>The chips' keys while the map is up (doc 09 has none): Z, X and V.</summary>
        public static readonly string[] ChipKeys = { "Z", "X", "V" };
        public static readonly string[] ChipNames = { "Mail", "Routes", "Resources" };
        /// <summary>How often the crews' routes are planned again while the map is up.</summary>
        private const double RouteSeconds = 1.0;

        private readonly bool[] _on = { true, true, false };
        private double _routeAge = double.MaxValue;

        public bool On(MapChip chip) => _on[(int)chip];

        public void Toggle(MapChip chip) => _on[(int)chip] = !_on[(int)chip];

        public void Set(MapChip chip, bool on) => _on[(int)chip] = on;

        public readonly List<MapBelt> Belts = new List<MapBelt>();
        /// <summary>Constructs other than belts and pipes.</summary>
        public readonly List<Construct> Machines = new List<Construct>();
        public readonly List<ClientVehicle> Vehicles = new List<ClientVehicle>();
        /// <summary>Mail chip: pieces waiting for each house (destination id), in storage and the pockets.</summary>
        public readonly Dictionary<ushort, int> MailHouses = new Dictionary<ushort, int>();
        /// <summary>Routes chip: the hired crews' ways.</summary>
        public readonly List<MapCrewRoute> Crews = new List<MapCrewRoute>();
        /// <summary>Resources chip: nodes with something left, and what they give.</summary>
        public readonly List<(float X, float Z, NodeDef Node)> Nodes = new List<(float, float, NodeDef)>();
        /// <summary>Raiders standing in an Alarm Post's reach.</summary>
        public readonly List<(float X, float Z)> Raiders = new List<(float, float)>();

        /// <summary>Works the layers out anew; the crews' routes once a second.</summary>
        public void Update(ClientWorld world, double dt)
        {
            Belts.Clear();
            Machines.Clear();
            const float tile = Sim.Core.SimConstants.TileSize;
            foreach (var c in world.Constructs.All)
            {
                bool pipe = c.Def.Behaviour == BuildingBehaviour.Pipe;
                if (!BeltNetwork.IsBelt(c) && !pipe) { Machines.Add(c); continue; }
                var (dx, dz) = BeltNetwork.Dirs[c.Facing];
                float half = c.Def.Length * tile * 0.5f;
                float cx = (c.Tx + 0.5f) * tile + dx * (c.Def.Length - 1) * tile * 0.5f, cz = (c.Tz + 0.5f) * tile + dz * (c.Def.Length - 1) * tile * 0.5f;
                var kind = pipe ? MapBeltKind.Pipe : c.Def.Level > 0 || c.Def.Ramp ? MapBeltKind.Raised : c.Def.Speed > 2.5f ? MapBeltKind.Fast : MapBeltKind.Belt;
                Belts.Add(new MapBelt(cx - dx * half, cz - dz * half, cx + dx * half, cz + dz * half, kind));
            }

            Vehicles.Clear();
            Vehicles.AddRange(world.Vehicles.Values);

            MailHouses.Clear();
            if (On(MapChip.Mail))
            {
                foreach (var kv in world.MailWaiting) MailHouses[kv.Key] = kv.Value;
                if (world.HasLocal)
                    foreach (var box in new[] { world.Hotbar, world.Inventory })
                        foreach (var e in box.Entries)
                        {
                            if (e.Stack.Kind != StackKind.Mail || world.Map.FindDestination(e.Stack.Address) is not { } d) continue;
                            MailHouses[d.Id] = (MailHouses.TryGetValue(d.Id, out int n) ? n : 0) + e.Stack.Count;
                        }
            }

            Nodes.Clear();
            if (On(MapChip.Resources))
                for (int i = 0; i < world.Map.Props.Count; i++)
                {
                    var def = world.Nodes.Def(i);
                    if (def == null || world.Nodes.Depleted(i)) continue;
                    Nodes.Add((world.Map.Props[i].X, world.Map.Props[i].Z, def));
                }

            Raiders.Clear();
            if (world.Constructs.All.Count > 0)
                foreach (var e in world.Enemies.Values)
                    if (RaidRules.Revealed(world.Constructs, e.X, e.Z)) Raiders.Add((e.X, e.Z));

            _routeAge += dt;
            if (!On(MapChip.Routes)) { Crews.Clear(); _routeAge = double.MaxValue; }
            else if (_routeAge >= RouteSeconds)
            {
                _routeAge = 0;
                PlanCrews(world);
            }
            else
                foreach (var crew in Crews)
                    crew.Now = world.DriverAt(crew.Home) is { House: var h } d && d.State is DriverState.Out or DriverState.Unloading && h != DriverEvent.NoHouse
                        ? world.Map.GetDestination(h) : null;
        }

        /// <summary>Forgets the planned routes: the next <see cref="Update"/> plans them at once.</summary>
        public void Replan() => _routeAge = double.MaxValue;

        private readonly List<(float X, float Z)> _leg = new List<(float X, float Z)>();

        private void PlanCrews(ClientWorld world)
        {
            Crews.Clear();
            foreach (var home in world.Constructs.All)
            {
                if (!home.Def.HasConsole) continue;
                var driver = world.DriverAt(home);
                if (driver == null) continue;
                var crew = Plan(world, home, home.Filters);
                if (crew == null) continue;
                crew.Now = driver.State is DriverState.Out or DriverState.Unloading && driver.House != DriverEvent.NoHouse ? world.Map.GetDestination(driver.House) : null;
                Crews.Add(crew);
            }
        }

        /// <summary>The route editor's console (doc 09 §2.6): its route as the console shows its stops now, hired or not; null for none.</summary>
        public MapCrewRoute? Edited { get; private set; }

        /// <summary>Plans the route of the console being edited, every frame while it is (a click shows at once).</summary>
        public void UpdateEdited(ClientWorld world, Construct? console)
        {
            Edited = null;
            if (console == null) return;
            var stops = new List<MailFilter>();
            for (int i = 0; i < console.Def.RouteStops; i++) stops.Add(world.ShownFilter(console, i, 0));
            Edited = Plan(world, console, stops) ?? new MapCrewRoute { Home = console, Captain = console.Def.IsPort };
        }

        /// <summary>A crew's way from its home's bay round the houses of the stops and back; null with no house to call at.</summary>
        private MapCrewRoute? Plan(ClientWorld world, Construct home, IReadOnlyList<MailFilter> stops)
        {
            var map = world.Map;
            float reach = world.Content.Balance.GetFloat("dockZoneReach");
            float radius = 0.5f;
            foreach (var v in world.Content.Vehicles) if (v.OnWater && v.LoadingFace) radius = v.Radius;
            var crew = new MapCrewRoute { Home = home, Captain = home.Def.IsPort };
            SimWorld.Bay(home, out float bx, out float bz, out _);
            Func<DestinationDef, bool> wanted = crew.Captain ? d => WaterNav.Berth(map, world.Constructs, d, reach, out _, out _) : d => !d.IsDock;
            RoutePlan.Houses(map, stops, bx, bz, wanted, crew.Stops);
            if (crew.Stops.Count == 0) return null;
            float x = bx, z = bz;
            crew.Points.Add((x, z));
            foreach (var d in crew.Stops)
            {
                float tx = d.MailboxX, tz = d.MailboxZ;
                if (crew.Captain) WaterNav.Berth(map, world.Constructs, d, reach, out tx, out tz);
                Leg(x, z, tx, tz);
                x = tx;
                z = tz;
            }
            Leg(x, z, bx, bz);
            return crew;

            void Leg(float fx, float fz, float gx, float gz)
            {
                float metres = crew.Captain ? WaterNav.Route(map, world.Constructs, fx, fz, gx, gz, radius, _leg) : map.Routes.Route(fx, fz, gx, gz, _leg);
                if (metres < 0f || _leg.Count == 0) { crew.Points.Add((gx, gz)); return; }
                for (int i = 1; i < _leg.Count; i++) crew.Points.Add(_leg[i]);
            }
        }

        /// <summary>The chips as the panel writes them: "[Z] Mail on   [X] Routes on   [V] Resources off".</summary>
        public string ChipsText()
        {
            var parts = new string[ChipNames.Length];
            for (int i = 0; i < parts.Length; i++) parts[i] = $"[{ChipKeys[i]}] {ChipNames[i]} {(_on[i] ? "on" : "off")}";
            return string.Join("   ", parts);
        }
    }
}
