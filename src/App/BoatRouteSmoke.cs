using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.App
{
    /// <summary>
    /// "-- --smoke --world=large --shift=4 --boat-route": doc 11 §4's Motor Boat to the second town on the Large Island,
    /// through the real client (design choice 175). In shift 4's Prep the host builds a Small Port by the Post Office town's
    /// Business Dock and a second one by the far town's, an inserter behind the far port's loading face into a chest; a
    /// Motor Boat in the home bay holds a crate for the far dock and letters for a house of the far town. At the home
    /// port's console (the real panel) the host routes the far dock and the far town's district (the line: "1 dock, 1
    /// other port") and hires the captain by key 7; in Delivery the client follows the boat over the sea to the dock and
    /// into the far port's bay, the feed has the crate delivered, the letters come out into the chest, and the boat is back
    /// in the home bay. Prints "SMOKE OK" and exits 0, or "SMOKE FAIL: …" and exits 1.
    /// </summary>
    public partial class BoatRouteSmoke : Node
    {
        private const int MaxFrames = 60 * 60 * 8;
        private GameSession _session = null!;
        private GameScreen _screen = null!;
        private readonly List<string> _failures = new List<string>();
        private readonly HashSet<string> _said = new HashSet<string>();
        private readonly HashSet<string> _doing = new HashSet<string>();
        private int _step, _frames, _stepFrames;
        private Construct? _home, _far;
        private (int X, int Z) _chest;
        private DestinationDef? _farDock, _house;
        private Sim.Vehicles.Vehicle? _boat;
        private bool _seenAtFarBay, _out;
        private float _worstMirror;

        public void Setup(GameSession session, GameScreen screen)
        {
            _session = session;
            _screen = screen;
        }

        private void Check(bool ok, string failure)
        {
            if (!ok) _failures.Add(failure);
        }

        private void Finish()
        {
            _step = -1;
            if (_failures.Count == 0) GD.Print("SMOKE OK");
            else foreach (string f in _failures) GD.PrintErr("SMOKE FAIL: ", f);
            GetTree().Quit(_failures.Count == 0 ? 0 : 1);
        }

        private void Next()
        {
            _step++;
            _stepFrames = 0;
        }

        private int _prepFrames;
        private uint _generatingTick;

        /// <summary>The Large Island's map, open from the Lobby into shift 4's Prep, as the ec62063 playtest saw it.</summary>
        private void CheckMap()
        {
            var hud = _screen.Hud;
            hud.Refresh();
            Check(_screen.Map.Visible, "the map the host opened in the Lobby is not open");
            // N23: no GENERATING card left on it.
            Check(!hud.Generating.Visible && hud.Generating.Text.Length == 0, $"in Prep the GENERATING card is still up over the map: \"{hud.Generating.Text}\"");
            // N21: no street name on another.
            var names = _screen.Map.StreetLabelRects;
            Check(names.Count > 0, "the Large Island's map writes no street names");
            for (int i = 0; i < names.Count; i++)
                for (int j = i + 1; j < names.Count; j++)
                    Check(!names[i].Intersects(names[j]), $"on the Large Island's map the street names at {names[i]} and {names[j]} lie on each other");
            // N19's "Opens in shift N" stays off them.
            foreach (var opens in _screen.Map.OpensLabelRects)
                foreach (var name in names)
                    Check(!opens.Intersects(name), $"on the Large Island's map an \"Opens in shift\" label at {opens} lies on a street name at {name}");
            // Batch 9's free mail forecast on the map's line in Prep: whole at 1280×720, every district in it.
            bool prep = _session.Client.World?.Global.Phase == RunPhase.Prep;
            Check(!prep || _screen.Map.RouteText.StartsWith("Mail forecast", StringComparison.Ordinal), $"in Prep the map's line reads \"{_screen.Map.RouteText}\", not the mail forecast");
            if (prep)
                Check(_screen.Map.RouteTextFits, $"the map's mail forecast runs off its panel: \"{_screen.Map.RouteText}\"");
            // N22: no plate on the open map.
            var area = _screen.Map.PanelArea();
            foreach (var row in hud.TrackerRects)
                Check(!row.Intersects(area), $"a plate at {row} stands on the open map at {area}");
        }

        private static (int X, int Z)? PortSite(SimWorld sim, BuildingDef port, float nearX, bool roomBehind)
        {
            int cx = (int)(nearX / 2f);
            (int, int)? best = null;
            int bestD = int.MaxValue;
            for (int x = Math.Max(0, cx - 40); x < Math.Min(sim.Map.Cols, cx + 40); x++)
                for (int z = 0; z < sim.Map.Rows; z++)
                {
                    if (Math.Abs(x - cx) >= bestD || BuildRules.CheckPlacement(sim.Map, sim.Constructs, port, x, z, 1) != OpResult.Ok) continue;
                    if (roomBehind && (BuildRules.CheckTile(sim.Map, sim.Constructs, x - 1, z - 1) != OpResult.Ok || BuildRules.CheckTile(sim.Map, sim.Constructs, x - 1, z - 2) != OpResult.Ok)) continue;
                    best = (x, z);
                    bestD = Math.Abs(x - cx);
                }
            return best;
        }

        public override void _Process(double delta)
        {
            if (_step < 0) return;
            var client = _session.Client;
            var world = client.World;
            var sim = _session.Server?.World;
            if (world == null || sim == null) return;
            foreach (var e in _session.Feed.Entries) _said.Add(e.Text);
            _stepFrames++;
            if (++_frames > MaxFrames || _stepFrames > 60 * 60 * 4)
            {
                Check(false, $"the boat route stopped at step {_step} ({world.Global.Phase} of shift {world.Global.Shift})");
                Finish();
                return;
            }
            var player = sim.GetPlayer(world.LocalId)!;
            var content = world.Content;
            var portDef = content.Building("small_port");
            switch (_step)
            {
                case 0:
                    // The Lobby: the host starts the run (at shift 4: the playtest's --shift), with the map open (the ec62063
                    // playtest's N23: the GENERATING card stood on the open map after Prep had begun).
                    if (world.Global.Phase == RunPhase.Lobby && !_screen.Map.Visible) _screen.Map.Toggle();
                    // The map in the Lobby: its plates (N22) and its names (N21) as the playtest saw them.
                    if (_stepFrames == 20 && world.Global.Phase == RunPhase.Lobby) CheckMap();
                    if (world.Global.Phase == RunPhase.Lobby && client.PendingRequestCount == 0 && _stepFrames % 30 == 5 && _stepFrames > 25) client.PressGo();
                    if (sim.Run.Phase == RunPhase.Generating && _generatingTick == 0) _generatingTick = sim.Tick;
                    if (world.Global.Phase != RunPhase.Prep || client.PendingRequestCount > 0) break;
                    if (++_prepFrames < 6) break;
                    // The client's own Ready ends Generating, not its 10 s deadline (N23's long card).
                    Check(_generatingTick > 0 && (long)sim.Run.PhaseStartTick - _generatingTick < Sim.Core.SimConstants.SecondsToTicks(content.Balance.GetDouble("generatingSeconds")) / 2,
                        $"Generating ran from tick {_generatingTick} to {sim.Run.PhaseStartTick}: the client never said it had the world drawn");
                    CheckMap();
                    if (_screen.Map.Visible) _screen.Map.Toggle();
                    Check(world.Global.Shift == 4 && world.Map.Id == LargeIslandGenerator.LargeIsland, $"Prep of shift {world.Global.Shift} on {world.Map.Id}, not shift 4 on the Large Island");
                    _session.FixedTicksPerFrame = 2;
                    sim.Economy.AddLoot(2000);
                    sim.Grant(content.ShopItem("bp_motorboat"), player.Id);
                    foreach (var (item, n) in new[] { ("plank", 40), ("stone", 20), ("iron_ingot", 20), ("log", 20) })
                        Check(sim.Give(player.Inventory, ItemStack.OfItem(content.Item(item), n)), $"no room for {n} {item}");
                    _farDock = world.Map.Destinations.Single(d => d.IsDock && d.Address.District >= 3);
                    Next();
                    break;
                case 1:
                case 2:
                case 3:
                case 4:
                {
                    // The two ports, then the inserter and the chest behind the far one, each sent by the client from beside it.
                    if (client.PendingRequestCount > 0) break;
                    if (_stepFrames == 1)
                    {
                        var nearDock = world.Map.Destinations.Single(d => d.IsDock && d.Address.District <= 2);
                        (int X, int Z) at;
                        BuildingDef def;
                        int rotation = 1;
                        if (_step <= 2)
                        {
                            var site = PortSite(sim, portDef, (_step == 1 ? nearDock : _farDock!).MailboxX, roomBehind: _step == 2);
                            if (site == null) { Check(false, $"no place for a Small Port by {(_step == 1 ? nearDock : _farDock!).Label}"); Finish(); return; }
                            at = site.Value;
                            def = portDef;
                        }
                        else
                        {
                            var (lx, lz) = SimWorld.PortLoadingTile(_far!);
                            at = _step == 3 ? (lx, lz - 1) : (lx, lz - 2);
                            def = content.Building(_step == 3 ? "inserter" : "chest");
                            rotation = _step == 3 ? 3 : 0;
                            if (_step == 4) _chest = at;
                        }
                        player.Move.X = BuildRules.TileCentre(at.X + 1);
                        player.Move.Z = BuildRules.TileCentre(at.Z - 1);
                        _pending = (def, at.X, at.Z, rotation);
                        break;
                    }
                    var (pd, px, pz, pr) = _pending;
                    if (_stepFrames == 12) Check(client.PlaceConstruct(pd.Recipe, px, pz, pr) != 0, $"the {pd.Name} was not sent");
                    if (_stepFrames <= 12 || world.Constructs.At(px, pz)?.Def != pd) break;
                    if (_step == 1) _home = sim.Constructs.At(px, pz);
                    if (_step == 2) _far = sim.Constructs.At(px, pz);
                    Next();
                    break;
                }
                case 5:
                {
                    // The boat in the home bay with the crate and the far town's letters; the host by the home port's console.
                    SimWorld.Bay(_home!, out float bx, out float bz, out float heading);
                    _boat = sim.AddVehicleAt(content.Vehicle("motorboat"), bx, bz, heading);
                    _house = world.Map.Houses.Where(h => h.Address.District is 3 or 4 && Sim.Vehicles.SeaRoute.PortFor(sim.Constructs, _home!, h) == _far)
                        .OrderBy(h => MathF.Abs(h.MailboxX - _farDock!.MailboxX)).First();
                    Check(sim.Give(_boat.Cargo, ItemStack.OfMail(new MailItem { Id = 997000, Kind = content.MailKind("cargo").Index, Address = _farDock!.Address, Value = 600, SpawnShift = 4, DeadlineShift = 5 })), "no room for the crate");
                    for (uint i = 0; i < 3; i++)
                        Check(sim.Give(_boat.Cargo, ItemStack.OfMail(new MailItem { Id = 997001 + i, Kind = content.MailKind("letter").Index, Address = _house.Address, Value = 10, SpawnShift = 4, DeadlineShift = 6 })), "no room for a letter");
                    player.Move.X = _home!.CentreX;
                    player.Move.Z = _home.CentreZ - 2f;
                    Next();
                    break;
                }
                case 6:
                {
                    if (_stepFrames < 15) break;
                    var here = world.Constructs.At(_home!.Tx, _home.Tz)!;
                    if (_stepFrames == 15)
                    {
                        Check(world.FilterChoices(here).Contains(new MailFilter(FilterKind.District, _house!.Address.District)), "the home port's console does not offer the far town's district");
                        Check(client.OpenFilters(here) != 0, "the home port's console was not asked for");
                        break;
                    }
                    var panel = _screen.Inventory.Filters;
                    if (_stepFrames == 30)
                    {
                        Check(client.SetFilter(0, 0, new MailFilter(FilterKind.Address, _farDock!.Id)) != 0, "the far dock's stop was not sent");
                        Check(client.SetFilter(1, 0, new MailFilter(FilterKind.District, _house!.Address.District)) != 0, "the far district's stop was not sent");
                    }
                    if (_stepFrames < 50 || client.PendingRequestCount > 0) break;
                    if (_stepFrames == 50)
                    {
                        Check(panel.IsVisibleInTree() && panel.Text == HudText.FilterText(world), $"the console's panel reads \"{panel.Text}\"");
                        Check(panel.Text.Contains("1 dock, 1 other port   ·   ", StringComparison.Ordinal), $"the console's line does not count the dock and the other port: \"{panel.Text}\"");
                        Check(panel.Text.Contains("3 for it in the boat", StringComparison.Ordinal), $"the console does not count the far town's letters in the boat: \"{panel.Text}\"");
                        Check(client.PressFilterKey(6), "key 7 did nothing at the console");
                        break;
                    }
                    if (world.DriverAt(here) == null) break;
                    Check(client.CloseFilters(), "the console was not up to take down");
                    GD.Print($"[smoke] boat route: two Small Ports on Large Island #{sim.Settings.Seed}, the home port's console routes {_farDock!.Label} and district {_house!.Address.District} (1 dock, 1 other port), the captain hired");
                    _session.FixedTicksPerFrame = 6;
                    Next();
                    break;
                }
                case 7:
                    if (world.Global.Phase == RunPhase.Prep && client.PendingRequestCount == 0 && _stepFrames % 30 == 5) client.PressGo();
                    if (world.Global.Phase == RunPhase.Delivery) Next();
                    break;
                case 8:
                {
                    var d = sim.DriverAt(_home!)!;
                    var shown = world.DriverAt(world.Constructs.At(_home!.Tx, _home.Tz)!);
                    if (shown != null && shown.State != DriverState.Waiting) _doing.Add(HudText.DriverDoing(world, shown));
                    if (d.State != DriverState.Waiting) _out = true;
                    if (world.Vehicles.TryGetValue(_boat!.Id, out var mirror) && _boat.NpcDriving)
                        _worstMirror = MathF.Max(_worstMirror, MathF.Sqrt((mirror.X - _boat.X) * (mirror.X - _boat.X) + (mirror.Z - _boat.Z) * (mirror.Z - _boat.Z)));
                    if (d.State == DriverState.Unloading && d.PortLeg == _far && mirror != null)
                    {
                        SimWorld.Bay(_far!, out float fx, out float fz, out _);
                        if (MathF.Abs(mirror.X - fx) < 0.3f && MathF.Abs(mirror.Z - fz) < 0.3f) _seenAtFarBay = true;
                    }
                    if (!_out || d.State != DriverState.Waiting || client.PendingRequestCount > 0) break;
                    if (_stepFrames < 10_000 && !_said.Contains("The captain is back at the Small Port")) break;
                    var chest = sim.GetContainer(sim.Constructs.At(_chest.X, _chest.Z)!.ContainerId)!;
                    int letters = chest.Entries.Sum(e => e.Stack.Kind == StackKind.Mail ? e.Stack.Mail.Count : 0);
                    Check(_said.Contains("The captain sets out with the boat: 2 stops"), "the feed did not say the captain set out for 2 stops");
                    Check(_said.Any(t => t.StartsWith($"A captain delivered 1 Cargo to {_farDock!.Label}", StringComparison.Ordinal)), $"the feed did not have the crate delivered to {_farDock!.Label}: {string.Join(" | ", _said)}");
                    Check(_said.Contains("The captain is back at the Small Port"), "the feed did not say the captain was back");
                    Check(_doing.Any(s => s.StartsWith("lying at the Small Port by ", StringComparison.Ordinal)), $"the console never said the boat lay at the far port: {string.Join(" | ", _doing)}");
                    Check(_seenAtFarBay, "the client never saw the boat lie in the far port's bay");
                    Check(_worstMirror < 1.5f, $"the client's boat was {_worstMirror:0.00} m off the server's");
                    Check(letters == 3, $"{letters} letters came out into the far town's chest, not 3");
                    Check(sim.InBay(_home!) == _boat, "the boat is not back in the home bay");
                    GD.Print($"[smoke] boat route: the captain sailed the crate to {_farDock!.Label} and lay in the far port's bay, {letters} letters for {_house!.Label} out into the chest by its inserter, the boat back home (the client's boat within {_worstMirror:0.00} m)");
                    Finish();
                    break;
                }
            }
        }

        private (BuildingDef Def, int X, int Z, int Rotation) _pending;
    }
}
