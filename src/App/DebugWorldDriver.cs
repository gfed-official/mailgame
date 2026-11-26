using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PerformativeMail.Client.InputHandling;
using PerformativeMail.Client.UI;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.App
{
    /// <summary>
    /// <c>--debug-world</c> with <c>--smoke</c> or <c>--shots=DIR</c> (design choice 119). With
    /// <c>--smoke</c> it checks the showcase in the real host: the run in Delivery of shift 5 with
    /// the clock stopped and the wallet full; fast-forwarded, the mail goes through every station
    /// (each station's proofs, as the server's <see cref="ShowcaseProbe"/> sees them) while the
    /// client mirrors every construct, vehicle and crew and draws mail on the belts and in the
    /// pipes; the carrier takes no damage and builds out of empty pockets; each bookmark key puts
    /// the free camera at its station with the station in view; the free camera flies while the
    /// carrier stands; the time scale keys run the clock at 0.25×, 1× and 4×. With
    /// <c>--shots</c> it saves a picture at every bookmark (in a window; headless there is
    /// nothing to draw). Then "SMOKE OK" (or "SHOTS OK") and quit.
    /// </summary>
    public partial class DebugWorldDriver : Node
    {
        public string? ShotDir;
        public bool Smoke;

        /// <summary>
        /// The share of a grid of rays through the picture that meet a house (its walls and roof as a
        /// box) before they have come half the way to <paramref name="target"/>.
        /// </summary>
        private static float HiddenByHouses(Client.ClientWorld world, Camera3D camera, Vector3 target)
        {
            var eye = camera.GlobalPosition;
            float far = eye.DistanceTo(target) * 0.5f;
            var size = camera.GetViewport().GetVisibleRect().Size;
            var boxes = world.Map.Houses.Select(h =>
            {
                var b = h.Building;
                float w = b.W * SimConstants.TileSize, d = b.H * SimConstants.TileSize;
                float ridge = Client.Presentation.HouseBuilder.RidgeHeight(Mathf.Max(w, d));
                return new Aabb(new Vector3(b.CenterX - w * 0.5f - 0.6f, 0f, b.CenterZ - d * 0.5f - 0.6f), new Vector3(w + 1.2f, ridge + 0.3f, d + 1.2f));
            }).ToList();
            int rays = 0, hit = 0;
            for (int i = 0; i < 12; i++)
                for (int j = 0; j < 7; j++)
                {
                    var at = new Vector2((i + 0.5f) / 12f * size.X, (j + 0.5f) / 7f * size.Y);
                    var dir = camera.ProjectRayNormal(at);
                    rays++;
                    if (boxes.Any(box => box.IntersectsSegment(eye, eye + dir * far))) hit++;
                }
            return (float)hit / rays;
        }

        private enum Stage { Boot, Flow, Toggles, Ruin, Console, RouteEdit, Map, Turret, Bookmarks, Fly, Scale, Done }
        private const int FastTicks = 30;
        /// <summary>Sim seconds the flow may take before the smoke gives up on a station.</summary>
        private const int FlowSeconds = 180;
        /// <summary>Frames a bookmark is given to draw (labels settle, the far views fill in) before its picture.</summary>
        private const int SettleFrames = 12;

        private GameSession _session = null!;
        private GameScreen _screen = null!;
        private readonly List<string> _failures = new List<string>();
        private Stage _stage;
        private int _frames, _bookmark, _scaleStep;
        private uint _scaleTick0;
        private double _scaleTime;
        private float _flyX, _flyZ, _carrierX, _carrierZ;
        private readonly double[] _scaleRatios = new double[3];

        private Sim.SimWorld Server => _session.Server!.World;

        public void Setup(GameSession session, GameScreen screen)
        {
            _session = session;
            _screen = screen;
            GD.Print($"[debug] {(Smoke ? "smoke" : "shots")} of the debug world{(ShotDir != null ? $", pictures into {ShotDir}" : "")}");
        }

        private void Check(bool ok, string failure)
        {
            if (!ok) _failures.Add(failure);
        }

        private void Next(Stage stage)
        {
            _stage = stage;
            _frames = 0;
        }

        public override void _Process(double delta)
        {
            if (_stage == Stage.Done) return;
            _frames++;
            var client = _session.Client;
            var world = client.World!;
            var debug = _screen.Debug;
            switch (_stage)
            {
                case Stage.Boot:
                {
                    if (_frames < 3) return;
                    var hud = _screen.Hud;
                    hud.Refresh();
                    Check(debug != null, "the debug world has no debug keys");
                    Check(world.Map.Id == "debug_world", $"the map is {world.Map.Id}");
                    Check(world.Global.Phase == RunPhase.Delivery && world.Global.Shift == 5, $"the run is in {world.Global.Phase} of shift {world.Global.Shift}, not Delivery of shift 5");
                    Check(world.Global.ClockStopped && hud.PhaseText.Contains("(stopped)", StringComparison.Ordinal), $"the clock runs: \"{hud.PhaseText}\"");
                    Check(world.Global.Wallet >= PlaytestMask.MaxWallet, $"the wallet holds {world.Global.Wallet} g");
                    Check(debug == null || debug.LineText.StartsWith("DEBUG WORLD · god mode · unlimited · clock stopped", StringComparison.Ordinal), $"the debug line reads \"{debug?.LineText}\"");
                    Check(Server.Constructs.All.Count == world.Map.Debug!.Constructs.Count, $"{Server.Constructs.All.Count} constructs stand of the {world.Map.Debug.Constructs.Count} of the showcase");
                    if (!Smoke) { Next(Stage.Flow); return; }
                    string missing = FontCheck.MissingGlyphs(hud) + FontCheck.MissingGlyphs(this);
                    Check(missing.Length == 0, $"no glyph for \"{missing}\"");
                    _session.FixedTicksPerFrame = FastTicks;
                    Next(Stage.Flow);
                    return;
                }
                case Stage.Flow:
                {
                    var probe = Server.Showcase!;
                    // The pictures want mail on the belts: the shots wait for the flow too, in real time at 4x.
                    if (!Smoke && _frames == 1) debug!.SetScale(4.0);
                    if (!probe.AllDone && Server.Tick < FlowSeconds * SimConstants.TickRate) return;
                    for (int i = 0; i < probe.Stations.Count; i++)
                        GD.Print($"[debug] {probe.Stations[i].Name}: {string.Join(", ", probe.Stations[i].Proofs.Select((p, k) => $"{p} x{probe.Count(i, k)}"))}");
                    _session.FixedTicksPerFrame = 0;
                    if (!Smoke) { debug!.SetScale(1.0); Next(Stage.Bookmarks); return; }
                    Check(probe.AllDone, $"after {Server.Tick / SimConstants.TickRate} s no mail went through {probe.Missing()}");
                    Check(world.Constructs.All.Count == Server.Constructs.All.Count, $"the client has {world.Constructs.All.Count} constructs, the server {Server.Constructs.All.Count}");
                    Check(world.Vehicles.Count == Server.Vehicles.Count, $"the client has {world.Vehicles.Count} vehicles, the server {Server.Vehicles.Count}");
                    Check(world.Drivers.Values.Count(d => d.State != DriverState.None) == 2, "the client does not know of both crews");
                    Check(client.ContainerDesyncs == 0 && client.BadPackets == 0, $"{client.ContainerDesyncs} container desyncs, {client.BadPackets} bad packets");
                    Check(_screen.BeltItems.Drawn > 0, "no mail is drawn on the belts");
                    Check(_screen.Constructs.PumpsNodding > 0, "the Oil Pump does not nod");
                    Check(_screen.Constructs.TurretShotsDrawn > 0 && world.TurretShots.Count > 0, $"the turret's shots are not drawn ({_screen.Constructs.TurretShotsDrawn} taken up, {world.TurretShots.Count} told)");
                    Check(Server.Economy.ShiftMisdeliveries == 0, $"{Server.Economy.ShiftMisdeliveries} misdeliveries");
                    Next(Stage.Toggles);
                    return;
                }
                case Stage.Toggles:
                {
                    // God mode: a blow that would down anybody leaves the carrier whole, on the HUD too.
                    var truth = Server.GetPlayer(world.LocalId)!;
                    if (_frames == 1)
                    {
                        Server.DamagePlayer(truth, 1000f);
                        Check(!truth.Dead && truth.Hp == truth.MaxHp, $"god mode: the carrier is at {truth.Hp} HP");
                        // Unlimited: a Stone Wall (behind its blueprint) out of empty pockets, by the real request.
                        foreach (var box in new[] { truth.Hotbar, truth.Inventory })
                            foreach (uint id in box.Entries.Select(e => e.EntryId).ToList()) box.Remove(id);
                        truth.Move.X = BuildRules.TileCentre(10);
                        truth.Move.Z = BuildRules.TileCentre(17);
                        return;
                    }
                    if (_frames < 6) return;
                    if (_frames < 120 && !ClientAt(world, BuildRules.TileCentre(10), BuildRules.TileCentre(17))) return;
                    if (_wallAsked == 0)
                    {
                        _wallAsked = _frames;
                        var wall = world.Content.Recipes.First(r => r.Building.Id == "wall_stone");
                        Check(world.RecipeState(wall) == OpResult.Ok, $"the Stone Wall out of empty pockets is {world.RecipeState(wall)}");
                        Check(client.PlaceConstruct(wall, 10, 15, 0) != 0, "the Stone Wall was not asked for");
                        return;
                    }
                    if (_frames < _wallAsked + 8) return;
                    Check(Server.Constructs.At(10, 15)?.Def.Id == "wall_stone", "the Stone Wall out of empty pockets was not built");
                    Check(world.Constructs.At(10, 15)?.Def.Id == "wall_stone", "the client does not see the Stone Wall");
                    Check(world.LocalHp >= truth.MaxHp - 0.01f, $"god mode: the client has the carrier at {world.LocalHp} HP");
                    Check(Server.Raid == Sim.Combat.RaidState.None && Server.Enemies.Count == 0, "a raid came to the debug world");
                    GD.Print($"[smoke] debug world: every station ran ({Server.Tick / SimConstants.TickRate} s), no damage, a Stone Wall out of empty pockets, no raid");
                    Next(Stage.Ruin);
                    return;
                }
                case Stage.Ruin:
                {
                    // Doc 05 §5: the Stone Wall broken leaves its ruin, drawn; in Delivery the prompt clears it away.
                    var interaction = _session.Interaction;
                    if (_frames == 1)
                    {
                        Server.DamageConstruct(Server.Constructs.At(10, 15)!, 100000f);
                        var carrier = Server.GetPlayer(world.LocalId)!;
                        carrier.Move.X = BuildRules.TileCentre(10) + 0.4f;
                        carrier.Move.Z = BuildRules.TileCentre(16);
                        return;
                    }
                    if (_frames < 8) return;
                    // Until the client sees its carrier beside the ruin (two seconds at most).
                    if (_frames == 8 && ++_ruinWait < 120 && !ClientAt(world, BuildRules.TileCentre(10) + 0.4f, BuildRules.TileCentre(16))) { _frames = 7; return; }
                    var ruin = world.Constructs.RuinAt(10, 15);
                    if (_frames == 8)
                    {
                        Check(ruin != null && world.Constructs.At(10, 15) == null && Server.Constructs.RuinAt(10, 15) != null, "the broken Stone Wall left no ruin on the client");
                        Check(_screen.Constructs.RuinsDrawn == 1, $"{_screen.Constructs.RuinsDrawn} ruins drawn");
                        if (ruin == null) { Next(Stage.Console); return; }
                        var box = Client.AimBox.Built(world.Map, ruin);
                        Client.ViewRay.Aim(world.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                        interaction.Update(1.0 / 30.0, false, false);
                        _screen.Hud.Refresh();
                        Check(interaction.Prompt.Kind == Client.InteractKind.ClearRuin, $"at the ruin the prompt is {interaction.Prompt.Kind}");
                        Check(_screen.Hud.PromptText.StartsWith("Clear the ruin of the Stone Wall  [E]", StringComparison.Ordinal), $"at the ruin the prompt reads \"{_screen.Hud.PromptText}\"");
                        interaction.Update(1.0 / 30.0, false, false, interactTapped: true);
                        return;
                    }
                    if (ruin != null && _frames < 70) return;
                    Check(ruin == null && Server.Constructs.RuinAt(10, 15) == null, "E did not clear the ruin away");
                    Check(world.Constructs.At(10, 15) == null, "something stands where the ruin was cleared");
                    GD.Print("[smoke] debug world: the broken Stone Wall left its ruin, drawn, and E cleared it away");
                    Next(Stage.Console);
                    return;
                }
                case Stage.Console:
                    StepConsole(world);
                    return;
                case Stage.RouteEdit:
                    StepRouteEdit(world);
                    return;
                case Stage.Map:
                    StepMap(world);
                    return;
                case Stage.Turret:
                    StepTurret(world);
                    return;
                case Stage.Bookmarks:
                {
                    var stations = world.Map.Debug!.Stations;
                    if (_bookmark >= stations.Count)
                    {
                        debug!.HandleKey(Key.F12);
                        Check(!_screen.Camera.FreeLook, "F12 did not give the view back to the carrier");
                        if (Smoke) GD.Print($"[smoke] debug world: {stations.Count} bookmarks, each station in view");
                        _bookmark = 0;
                        Next(Smoke ? Stage.Fly : Stage.Done);
                        if (!Smoke) Finish();
                        return;
                    }
                    if (_frames == 1)
                    {
                        Check(debug!.HandleKey(DebugWorldController.BookmarkKeys[_bookmark]), $"{DebugWorldController.BookmarkKeys[_bookmark]} is no bookmark key");
                        return;
                    }
                    if (_frames == 2)
                    {
                        var s = stations[_bookmark];
                        var camera = _screen.Camera;
                        var eye = new Vector3(s.EyeX, s.EyeY, s.EyeZ);
                        var target = new Vector3(s.TargetX, s.TargetY, s.TargetZ);
                        Check(camera.FreeLook && camera.GlobalPosition.DistanceTo(eye) < 0.05f, $"{s.Name}: the camera is at {camera.GlobalPosition}, not {eye}");
                        var forward = -camera.GlobalTransform.Basis.Z;
                        Check(forward.Dot((target - eye).Normalized()) > 0.999f, $"{s.Name}: the camera does not look at its target");
                        Check(camera.IsPositionInFrustum(target), $"{s.Name}: its target is out of view");
                        // Playtest f5813b5: a house's roof right by the eye hid half of a station's picture.
                        float hidden = HiddenByHouses(world, camera, target);
                        if (Smoke) GD.Print($"[smoke] debug world: {s.Name}: {hidden:P0} of the view hidden by houses");
                        Check(hidden < 0.1f, $"{s.Name}: {hidden:P0} of the view is hidden by a house nearer than its station");
                        Check(debug!.LineText.Contains(s.Name, StringComparison.Ordinal), $"{s.Name}: the debug line reads \"{debug.LineText}\"");
                    }
                    if (_frames < SettleFrames) return;
                    // Playtest f5813b5: the turret range's picture is taken as a tracer flies at a raider in view.
                    if (stations[_bookmark].Proofs.Any(p => p.Kind == Sim.World.ShowcaseProofKind.Fired))
                    {
                        bool shooting = _screen.Constructs.TracersShown > 0 && world.Enemies.Values.Any(e =>
                            _screen.Camera.IsPositionInFrustum(new Vector3(e.X, world.Map.HeightAt(e.X, e.Z) + 1f, e.Z)));
                        if (!shooting && _frames < SettleFrames + 900) return;
                        Check(shooting, $"{stations[_bookmark].Name}: no tracer at a raider in view in {_frames} frames");
                        if (Smoke && shooting) GD.Print($"[smoke] debug world: {stations[_bookmark].Name}: a tracer at a raider in view");
                    }
                    Shot($"{_bookmark + 1:00}-{Slug(stations[_bookmark].Name)}");
                    _bookmark++;
                    _frames = 0;
                    return;
                }
                case Stage.Fly:
                {
                    var truth = Server.GetPlayer(world.LocalId)!;
                    if (_frames == 1)
                    {
                        debug!.HandleKey(Key.Quoteleft);
                        Check(_screen.Camera.FreeLook, "the backquote did not free the camera");
                        _flyX = _screen.Camera.FreePosition.X;
                        _flyZ = _screen.Camera.FreePosition.Z;
                        _carrierX = truth.Move.X;
                        _carrierZ = truth.Move.Z;
                        return;
                    }
                    if (_frames <= 31)
                    {
                        // A second of W, as the keys would fly it.
                        debug!.Fly(new Vector2(0f, -1f), 0f, false, 1.0 / 30.0);
                        return;
                    }
                    float flown = new Vector2(_screen.Camera.FreePosition.X - _flyX, _screen.Camera.FreePosition.Z - _flyZ).Length();
                    float walked = new Vector2(truth.Move.X - _carrierX, truth.Move.Z - _carrierZ).Length();
                    Check(flown > 6f && flown < 10f, $"a second of the free camera flew {flown:0.0} m");
                    Check(walked < 0.05f, $"the carrier moved {walked:0.00} m under the free camera");
                    debug!.HandleKey(Key.Quoteleft);
                    Check(!_screen.Camera.FreeLook, "the backquote did not give the view back");
                    GD.Print($"[smoke] debug world: the free camera flew {flown:0.0} m, the carrier stood");
                    Next(Stage.Scale);
                    return;
                }
                case Stage.Scale:
                {
                    // Each scale for half a second of real time: the server's ticks over the frames' time.
                    if (_frames == 1)
                    {
                        debug!.HandleKey(_scaleStep == 0 ? Key.Comma : _scaleStep == 1 ? Key.Period : Key.Slash);
                        _scaleTick0 = Server.Tick;
                        _scaleTime = 0;
                        return;
                    }
                    _scaleTime += delta;
                    if (_scaleTime < 0.5 || _frames < 15) return;
                    double want = DebugWorldController.Scales[_scaleStep];
                    Check(_session.TimeScale == want, $"the time scale is {_session.TimeScale}, not {want}");
                    _scaleRatios[_scaleStep] = (Server.Tick - _scaleTick0) / (_scaleTime * SimConstants.TickRate);
                    if (++_scaleStep < 3) { _frames = 0; return; }
                    debug!.HandleKey(Key.Period);
                    // Wide bounds: the frames of a loaded machine are uneven, and the server catches up five ticks at most.
                    Check(_scaleRatios[0] > 0.1 && _scaleRatios[0] < 0.45, $"at 0.25x the clock ran at {_scaleRatios[0]:0.00}x");
                    Check(_scaleRatios[1] > 0.6 && _scaleRatios[1] < 1.4, $"at 1x the clock ran at {_scaleRatios[1]:0.00}x");
                    Check(_scaleRatios[2] > 2.0, $"at 4x the clock ran at {_scaleRatios[2]:0.00}x");
                    GD.Print(FormattableString.Invariant($"[smoke] debug world: time scale 0.25x/1x/4x ran at {_scaleRatios[0]:0.00}/{_scaleRatios[1]:0.00}/{_scaleRatios[2]:0.00}"));
                    Finish();
                    return;
                }
            }
        }

        private int _wallAsked;

        /// <summary>The client sees its carrier where the server put them.</summary>
        private static bool ClientAt(Client.ClientWorld world, float x, float z) =>
            new Vector2(world.Local.X - x, world.Local.Z - z).Length() < 0.3f;

        private float _consoleX, _consoleZ;
        private int _wait, _ruinWait;
        private PlayerInputController? _keys;
        private int _tap;
        private static readonly Key[] Taps = { Key.Escape, Key.Tab, Key.E };

        /// <summary>
        /// Playtest 346eb3e: the Vehicle Depot's Route Console comes down on a tap of Esc, Tab or E
        /// shorter than a frame, through the real input controller (a key pressed and let go
        /// between two frames; only the event shows it).
        /// </summary>
        private void StepConsole(Client.ClientWorld world)
        {
            var client = _session.Client;
            var depot = Server.Constructs.All.First(c => c.Def.IsVehicleDepot);
            if (_frames == 1 && _tap == 0)
            {
                Sim.SimWorld.Console(depot, out float cx, out float cz);
                float ox = cx - depot.CentreX, oz = cz - depot.CentreZ, len = Mathf.Max(0.01f, Mathf.Sqrt(ox * ox + oz * oz));
                var truth = Server.GetPlayer(world.LocalId)!;
                truth.Move.X = _consoleX = cx + ox / len * 1.2f;
                truth.Move.Z = _consoleZ = cz + oz / len * 1.2f;
                _keys = new PlayerInputController { Name = "SmokeKeys" };
                _keys.Setup(client, _session.Interaction, _screen.Mouse, _session.Build);
                _keys.Cancelled += _screen.Cancel;
                _keys.InventoryToggled += () => { };
                AddChild(_keys);
                return;
            }
            if (_frames < 10) return;
            // Until the client sees its carrier at the console (two seconds at most).
            if (_tap == 0 && _frames == 10 && ++_wait < 120 && !ClientAt(world, _consoleX, _consoleZ)) { _frames = 9; return; }
            var key = Taps[_tap];
            if (_frames == 10)
            {
                Check(client.OpenFilters(world.Constructs.At(depot.Tx, depot.Tz)!) != 0, $"the Route Console did not come up for the {key} tap");
                return;
            }
            // The screen marks the panel open a frame later, as it does in play.
            if (_frames == 13)
            {
                Check(world.FilterTarget != null && _screen.Mouse.PanelOpen, "the Route Console is not up");
                _keys!._UnhandledInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
                _keys._UnhandledInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
                _keys.Sample(1.0 / 30.0);
                return;
            }
            if (_frames < 16) return;
            Check(world.FilterTarget == null, $"a tap of {key} left the Route Console up");
            if (++_tap < Taps.Length) { _frames = 9; return; }
            _keys!.QueueFree();
            _keys = null;
            GD.Print("[smoke] debug world: the Route Console came down on a tap of Esc, Tab and E");
            Next(Stage.RouteEdit);
        }

        /// <summary>
        /// The route editor (doc 09 §2.6): with the depot's console up, M puts the map over it; a
        /// click on a house makes it the next stop, a drag of that stop onto the first swaps them
        /// over, a right click takes one off — on the server too — the console's cards wait
        /// under it, and Esc takes both down.
        /// </summary>
        private void StepRouteEdit(Client.ClientWorld world)
        {
            var client = _session.Client;
            var map = _screen.Map;
            var depot = world.Constructs.All.First(c => c.Def.IsVehicleDepot);
            var anvil = world.Map.Destinations.First(d => d.Label == "1 Anvil Close");
            var beacon = world.Map.Streets.First(s => s.Name == "Beacon Road");
            Sim.World.Construct Truth() => Server.Constructs.At(depot.Tx, depot.Tz)!;
            switch (_frames)
            {
                case 1:
                    Check(client.OpenFilters(depot) != 0, "the depot's console did not come up for the route editor");
                    return;
                case 3:
                    map.Toggle();
                    return;
                case 6:
                    Check(map.Editing && map.TitleText.StartsWith("ROUTE · Vehicle Depot", StringComparison.Ordinal), $"the map over the console is no route editor: \"{map.TitleText}\"");
                    Check(!_screen.Inventory.Visible && _screen.Mouse.PanelOpen, "the console's cards show over the route editor, or the pointer is not free");
                    Check(map.RouteText.Contains("round trip", StringComparison.Ordinal), $"the route editor's line reads \"{map.RouteText}\"");
                    var at = map.ScreenAt(anvil.MailboxX, anvil.MailboxZ);
                    map.Click(at, MouseButton.Left, true);
                    map.Click(at, MouseButton.Left, false);
                    return;
                case 16:
                {
                    Check(Truth().Filter(1, 0) == new Sim.Automation.MailFilter(Sim.Automation.FilterKind.Address, anvil.Id), $"a click on 1 Anvil Close made stop 2 {Truth().Filter(1, 0)}");
                    Shot("route-editor");
                    // Stop 2 dragged onto stop 1.
                    var two = map.StopMarkers.FirstOrDefault(m => m.Stop == 1).At;
                    var one = map.StopMarkers.FirstOrDefault(m => m.Stop == 0).At;
                    Check(map.StopMarkers.Count == 2, $"the route editor marks {map.StopMarkers.Count} stops");
                    map.Click(two, MouseButton.Left, true);
                    map.Click(one, MouseButton.Left, false);
                    return;
                }
                case 26:
                {
                    Check(Truth().Filter(0, 0) == new Sim.Automation.MailFilter(Sim.Automation.FilterKind.Address, anvil.Id)
                        && Truth().Filter(1, 0) == new Sim.Automation.MailFilter(Sim.Automation.FilterKind.Street, beacon.Id), "dragging stop 2 onto stop 1 did not swap them");
                    var first = map.StopMarkers.FirstOrDefault(m => m.Stop == 0).At;
                    map.Click(first, MouseButton.Right, true);
                    return;
                }
                case 36:
                {
                    Check(Truth().Filter(0, 0) == new Sim.Automation.MailFilter(Sim.Automation.FilterKind.Street, beacon.Id) && Truth().Filter(1, 0) == Sim.Automation.MailFilter.Empty,
                        "a right click on stop 1 did not take it off and move the next up");
                    // Esc: the console and the editor go down together.
                    _keys = new PlayerInputController { Name = "SmokeEditKeys" };
                    _keys.Setup(client, _session.Interaction, _screen.Mouse, _session.Build);
                    _keys.Cancelled += _screen.Cancel;
                    AddChild(_keys);
                    _keys._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.Escape, Keycode = Key.Escape, Pressed = true });
                    return;
                }
                case 38:
                    Check(world.FilterTarget == null && !map.Visible, "Esc left the console or the route editor up");
                    _keys!.QueueFree();
                    _keys = null;
                    GD.Print("[smoke] route editor: a house clicked in as stop 2, dragged onto stop 1, taken off by a right click, the server agreeing; Esc took it down");
                    Next(Stage.Map);
                    return;
            }
        }

        /// <summary>
        /// The map's layers and chips (doc 09 §2.6): every belt and pipe drawn as a line, the
        /// machines, both vehicles, both crews' routes; the chips by their keys through the real
        /// input controller take the routes and the mail away and bring them back.
        /// </summary>
        private void StepMap(Client.ClientWorld world)
        {
            var map = _screen.Map;
            if (_frames == 1)
            {
                if (!map.Visible) map.Toggle();
                _keys = new PlayerInputController { Name = "SmokeMapKeys" };
                _keys.Setup(_session.Client, _session.Interaction, _screen.Mouse, _session.Build);
                _keys.MapOpen = () => map.Visible;
                _keys.MapChipPressed += map.Layers.Toggle;
                AddChild(_keys);
                return;
            }
            if (_frames == 4)
            {
                int belts = world.Constructs.All.Count(c => Sim.Automation.BeltNetwork.IsBelt(c) || c.Def.Behaviour == Sim.Content.BuildingBehaviour.Pipe);
                Check(map.BeltsDrawn == belts, $"the map drew {map.BeltsDrawn} belts and pipes of {belts}");
                Check(map.MachinesDrawn == world.Constructs.All.Count - belts, $"the map drew {map.MachinesDrawn} machines");
                Check(map.VehiclesDrawn == world.Vehicles.Count && map.VehiclesDrawn >= 2, $"the map drew {map.VehiclesDrawn} vehicles of {world.Vehicles.Count}");
                Check(map.CrewsDrawn == 2, $"the map drew {map.CrewsDrawn} crews' routes");
                Check(map.ChipsText == "[Z] Mail on   [X] Routes on   [V] Resources off", $"the chips read \"{map.ChipsText}\"");
                Shot("map-layers");
                return;
            }
            if (_frames == 5)
            {
                foreach (var key in new[] { Key.X, Key.Z })
                {
                    _keys!._UnhandledInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
                    _keys._UnhandledInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
                }
                return;
            }
            if (_frames == 8)
            {
                Check(map.CrewsDrawn == 0 && map.MailHousesDrawn == 0, $"with Routes and Mail off the map drew {map.CrewsDrawn} routes and {map.MailHousesDrawn} houses");
                Check(map.ChipsText.StartsWith("[Z] Mail off   [X] Routes off", StringComparison.Ordinal), $"the chips read \"{map.ChipsText}\"");
                map.Layers.Set(Client.MapChip.Mail, true);
                map.Layers.Set(Client.MapChip.Routes, true);
                return;
            }
            if (_frames < 11) return;
            Check(map.CrewsDrawn == 2, "the routes did not come back with their chip");
            string missing = FontCheck.MissingGlyphs(map);
            Check(missing.Length == 0, $"the map has no glyph for \"{missing}\"");
            map.Toggle();
            _keys!.QueueFree();
            _keys = null;
            GD.Print($"[smoke] debug world: the map's layers ({map.BeltsDrawn} belts and pipes, {map.MachinesDrawn} machines, {map.VehiclesDrawn} vehicles, {map.CrewsDrawn} crews' routes), the chips by Z and X");
            Next(Stage.Turret);
        }

        private int _turretStep, _turretShots0, _turretDrawn0, _turretAt;
        private uint _turretTick0;
        private float _turretX, _turretZ;

        /// <summary>
        /// The turret operated by hand (doc 05 §4; design choice 125): beside the range's turret the
        /// prompt offers its controls, E takes them, the HUD's weapons line and the gun follow, a
        /// second of attack held fires it at twice its rate where the carrier looks — at the
        /// practice raider — and E lets go.
        /// </summary>
        private void StepTurret(Client.ClientWorld world)
        {
            var client = _session.Client;
            var interaction = _session.Interaction;
            var turret = world.Constructs.All.First(c => c.Def.IsTurret);
            var truth = Server.GetPlayer(world.LocalId)!;
            switch (_turretStep)
            {
                case 0:
                    _turretX = turret.CentreX + 1.6f;
                    _turretZ = turret.CentreZ;
                    truth.Move.X = _turretX;
                    truth.Move.Z = _turretZ;
                    _turretStep++;
                    return;
                case 1:
                    if (_frames < 120 && !ClientAt(world, _turretX, _turretZ)) return;
                    var box = Client.AimBox.Built(world.Map, turret);
                    Client.ViewRay.Aim(world.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                    interaction.Update(1.0 / 30.0, false, false);
                    _screen.Hud.Refresh();
                    Check(interaction.Prompt.Kind == Client.InteractKind.OperateTurret, $"beside the turret the prompt is {interaction.Prompt.Kind}");
                    Check(_screen.Hud.PromptText.StartsWith("Operate the Turret  [E]", StringComparison.Ordinal), $"beside the turret the prompt reads \"{_screen.Hud.PromptText}\"");
                    interaction.Update(1.0 / 30.0, false, false, interactTapped: true);
                    _turretAt = _frames;
                    _turretStep++;
                    return;
                case 2:
                    if (world.LocalTurret == null && _frames < _turretAt + 60) return;
                    Check(world.LocalTurret == turret && truth.Turret != null, "E did not give the carrier the turret's controls");
                    _turretShots0 = Server.TurretShots;
                    _turretTick0 = Server.Tick;
                    _turretDrawn0 = _screen.Constructs.TurretShotsDrawn;
                    _turretAt = _frames;
                    _turretStep++;
                    return;
                case 3:
                {
                    // A second of attack, the gun on the practice raider (or the range's spot, between raiders).
                    float tx = BuildRules.TileCentre(57), tz = BuildRules.TileCentre(2);
                    foreach (var e in world.Enemies.Values) { tx = e.X; tz = e.Z; }
                    client.Input.YawRadians = MathF.Atan2(-(tx - world.Local.X), -(tz - world.Local.Z));
                    client.Input.PitchRadians = 0f;
                    client.Input.Buttons = InputButtons.Attack;
                    if (Server.Tick < _turretTick0 + SimConstants.TickRate) return;
                    client.Input.Buttons = InputButtons.None;
                    _screen.Hud.Refresh();
                    int shots = Server.TurretShots - _turretShots0;
                    Check(shots >= 3, $"a second at the turret's controls fired {shots} shots");
                    Check(_screen.Constructs.TurretShotsDrawn > _turretDrawn0, "the operated turret's shots are not drawn");
                    Check(_screen.Constructs.TurretsManned == 1, $"{_screen.Constructs.TurretsManned} turrets drawn manned");
                    Check(_screen.Hud.WeaponsText == Client.HudText.TurretWeapons(turret.Def), $"at the turret the weapons line reads \"{_screen.Hud.WeaponsText}\"");
                    Check(new Vector2(truth.Move.X - _turretX, truth.Move.Z - _turretZ).Length() < 0.01f, "the carrier moved at the turret's controls");
                    interaction.Update(1.0 / 30.0, false, false);
                    Check(interaction.Prompt.Kind == Client.InteractKind.LeaveTurret, $"at the controls the prompt is {interaction.Prompt.Kind}");
                    interaction.Update(1.0 / 30.0, false, false, interactTapped: true);
                    GD.Print($"[smoke] debug world: the turret operated by hand, {shots} shots in a second, let go with E");
                    _turretAt = _frames;
                    _turretStep++;
                    return;
                }
                default:
                    if (world.LocalTurret != null && _frames < _turretAt + 60) return;
                    Check(world.LocalTurret == null && truth.Turret == null, "E did not let go of the turret");
                    Next(Stage.Bookmarks);
                    return;
            }
        }

        private static string Slug(string name) => new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

        private void Shot(string name)
        {
            if (ShotDir == null || DisplayServer.GetName() == "headless") return;
            DirAccess.MakeDirRecursiveAbsolute(ShotDir);
            Action? save = null;
            save = () =>
            {
                RenderingServer.FramePostDraw -= save;
                GetViewport().GetTexture().GetImage().SavePng($"{ShotDir}/debug-{name}.png");
            };
            RenderingServer.FramePostDraw += save;
        }

        private void Finish()
        {
            _stage = Stage.Done;
            string ok = Smoke ? "SMOKE OK" : "SHOTS OK";
            if (!Smoke && ShotDir != null && DisplayServer.GetName() == "headless") GD.Print("[debug] headless: nothing is drawn, so no pictures were saved (run it under xvfb-run)");
            if (_failures.Count == 0) GD.Print(ok);
            else
                foreach (string failure in _failures) GD.PrintErr(Smoke ? "SMOKE FAIL: " : "SHOTS FAIL: ", failure);
            // A frame more, so the last picture is saved.
            GetTree().CreateTimer(0.2).Timeout += () => GetTree().Quit(_failures.Count == 0 ? 0 : 1);
        }
    }
}
