using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Client.UI;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.App
{
    /// <summary>
    /// Build mode in the smoke's run (doc 09 §2.7): in the Prep of the round of the nodes, with
    /// the Logs the tree gave, the host puts Build mode up, finds the ghost green on open ground
    /// and red out of reach, builds a Wooden Wall, takes it down with the held button, builds a
    /// Chest and opens it with E, takes that down and builds the wall again to leave it standing
    /// for the run — the ghost, the prompt, the recipe bar, the pockets, the feed and what is drawn
    /// in the world are right on both ends. In the Lobby nothing built is left.
    /// </summary>
    public partial class SmokeDriver
    {
        private enum BuildStep { Start, Ghost, Wall, Red, TakeDown, Chest, Open, Close, ChestDown, Again, Done }

        private const int BuildStepFrames = 240;
        private const float BuildStandOff = 3.0f;

        private BuildStep _buildStep;
        private int _buildFrames, _buildVisits, _buildTx, _buildTz, _buildLogs;
        private float _buildX, _buildZ;

        private RecipeDef BuildRecipe(string building) => _session.Client.World!.Content.Recipes.First(r => r.Building.Id == building);

        private int CarriedLogs(ClientWorld world)
        {
            var log = world.Content.Item("log");
            return Units(world.Hotbar, log) + Units(world.Inventory, log);
        }

        /// <summary>
        /// A tile of open ground near the Post Office with the tile before it open too and room to
        /// stand <see cref="BuildStandOff"/> west of it; the host is put there, facing it.
        /// </summary>
        /// <summary>
        /// Stands the host west of the open tile nearest the spawn pad, or nearest
        /// (<paramref name="nearX"/>, <paramref name="nearZ"/>) among those <paramref name="accept"/> takes.
        /// </summary>
        private bool StandAtBuildSite(float nearX = float.NaN, float nearZ = float.NaN, Func<int, int, bool>? accept = null)
        {
            var world = _session.Client.World!;
            var map = world.Map;
            var po = map.PostOffice;
            var player = _session.Server!.World.GetPlayer(world.LocalId)!;
            if (float.IsNaN(nearX)) (nearX, nearZ) = (po.SpawnPadX, po.SpawnPadZ);
            float best = float.MaxValue;
            for (int tz = 0; tz < map.Rows; tz++)
            {
                for (int tx = 1; tx < map.Cols; tx++)
                {
                    if (BuildRules.CheckTile(map, world.Constructs, tx, tz) != OpResult.Ok || BuildRules.CheckTile(map, world.Constructs, tx - 1, tz) != OpResult.Ok) continue;
                    float x = BuildRules.TileCentre(tx) - BuildStandOff, z = BuildRules.TileCentre(tz);
                    if (!FreeGround(map, x, z, -1) || (accept != null && !accept(tx, tz))) continue;
                    float dx = x - nearX, dz = z - nearZ, d = dx * dx + dz * dz;
                    if (d >= best) continue;
                    best = d;
                    _buildTx = tx;
                    _buildTz = tz;
                    _buildX = x;
                    _buildZ = z;
                }
            }
            if (best == float.MaxValue) return false;
            player.Move.X = _buildX;
            player.Move.Z = _buildZ;
            return true;
        }

        private void NextBuildStep(BuildStep step)
        {
            _buildStep = step;
            _buildFrames = 0;
        }

        /// <summary>The view on the middle of a tile of the ground.</summary>
        private void AimAtTile(int tx, int tz)
        {
            var client = _session.Client;
            ViewRay.Aim(client.World!.Local, BuildRules.TileCentre(tx), 0f, BuildRules.TileCentre(tz), out client.Input.YawRadians, out client.Input.PitchRadians);
        }

        private void AimAtBuilt(Construct built)
        {
            var client = _session.Client;
            var box = AimBox.Built(client.World!.Map, built);
            ViewRay.Aim(client.World.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
        }

        /// <summary>A frame of Prep. Returns true while the building goes on: the shop and the go key wait for it.</summary>
        private bool StepBuild()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var view = _screen.Constructs;
            var build = _session.Build;

            if (_buildStep == BuildStep.Start)
            {
                if (_buildVisits > 0 || _harvestVisits == 0 || world.Local.Dead || client.PendingRequestCount > 0) return false;
                if (CarriedLogs(world) < BuildRecipe("chest").Inputs[0].Count)
                {
                    Fail($"the round of the nodes left {CarriedLogs(world)} Logs, too few to build with");
                    return false;
                }
                if (!StandAtBuildSite())
                {
                    Fail("no open ground near the Post Office to build on");
                    return false;
                }
                _session.FixedTicksPerFrame = 1;
                _buildLogs = CarriedLogs(world);
                Check(build.Toggle() && build.Active, $"B in Prep did not put Build mode up ({build.Refused})");
                Check(build.PressKey(0) && build.Recipe!.Building.Id == "wall_wood", "key 1 in Build mode is not the Wooden Wall");
                // Turned across the view, so that the wall shows its face.
                Check(build.Rotate() && build.Rotation == 1, $"R turned the wall to {build.Rotation}");
                NextBuildStep(BuildStep.Ghost);
                return true;
            }

            if (++_buildFrames > BuildStepFrames)
            {
                Fail($"Build mode stopped at {_buildStep}: ghost {build.Ghost.Aim} at ({build.Ghost.Tx}, {build.Ghost.Tz}) {build.Ghost.State}, prompt \"{hud.PromptText}\", "
                    + $"{world.Constructs.All.Count} built here and {server.Constructs.All.Count} on the server, {CarriedLogs(world)} Logs, {client.PendingRequestCount} requests pending");
                return false;
            }
            bool arrived = MathF.Abs(world.Local.X - _buildX) < 0.3f && MathF.Abs(world.Local.Z - _buildZ) < 0.3f;
            var built = world.Constructs.At(_buildTx, _buildTz);
            bool place = false, remove = false;
            // The prompt of the world goes on under Build mode, as the input layer has it every frame.
            if (_buildStep != BuildStep.Open) _session.Interaction.Update(1.0 / 30.0, false, false);

            switch (_buildStep)
            {
                case BuildStep.Ghost:
                case BuildStep.Again:
                {
                    AimAtTile(_buildTx, _buildTz);
                    if (!arrived || _buildFrames < 4) break;
                    build.Update(1.0 / 30.0, false, false);
                    hud.Refresh();
                    view.Refresh();
                    var g = build.Ghost;
                    if (g.Aim != BuildAim.Place || g.Tx != _buildTx || g.Tz != _buildTz || g.State != OpResult.Ok) break;
                    string want = HudText.BuildPrompt(world, g, out var tone);
                    Check(hud.PromptText == want && want.StartsWith("Build Wooden Wall", StringComparison.Ordinal) && tone == PromptTone.Match && hud.PromptMark == PromptTone.Match,
                        $"the green ghost's prompt is \"{hud.PromptText}\" ({hud.PromptMark}), expected \"{want}\"");
                    Check(hud.RecipeBarText.Split('\n').Length == world.Content.RecipeKeys.Count && hud.RecipeShown == 0 && hud.HeldText == HudText.BuildKeys,
                        $"the recipe bar reads \"{hud.RecipeBarText}\" ({hud.RecipeShown}), over \"{hud.HeldText}\"");
                    // The bar stands where the hotbar does, clear of the health bars and the trackers, inside the view.
                    var bar = hud.RecipeBar.GetGlobalRect();
                    var seen = GetViewport().GetVisibleRect();
                    Check(seen.Encloses(bar) && bar.Position.X > Hud.SafeMargin + 260f && bar.End.X < seen.End.X - Hud.SafeMargin - 330f,
                        $"the recipe bar at {bar} runs into the health bars or the trackers of a {seen.Size} view");
                    // Its rows, as many as the recipes take, stay inside it, under the keys line and over the bottom margin.
                    var keys = hud.HeldRect;
                    foreach (var child in hud.RecipeBar.GetChildren())
                    {
                        var card = ((Control)child).GetGlobalRect();
                        Check(bar.Encloses(card) && card.End.Y <= seen.End.Y - Hud.SafeMargin + 0.5f && !card.Intersects(keys),
                            $"the recipe card \"{((Label)child).Text}\" at {card} leaves the bar at {bar} or runs into the keys line at {keys}");
                    }
                    Check(keys.Position.Y >= hud.FeedBottom - 0.5f, $"the keys line at {keys} runs into the feed, which ends at {hud.FeedBottom}");
                    // Every recipe on every step of the wheel fits its card whole (none is cut short with "…").
                    var cardLabel = (Label)hud.RecipeBar.GetChild(0);
                    var cardFont = cardLabel.GetThemeFont("font");
                    int cardSize = cardLabel.GetThemeFontSize("font_size");
                    var recipeKeys = world.Content.RecipeKeys;
                    for (int k = 0; k < recipeKeys.Count; k++)
                        foreach (var r in recipeKeys[k])
                        {
                            string text = HudText.RecipeCard(r, k, recipeKeys[k]);
                            float width = cardFont.GetStringSize(text, HorizontalAlignment.Left, -1, cardSize).X;
                            Check(width <= Hud.RecipeCardWidth, $"the recipe card \"{text}\" is {width:0} px, wider than its {Hud.RecipeCardWidth:0}");
                        }
                    // Its words, drawn in the middle of the screen, stay over the bar: clear of the trackers to its right.
                    float keysMid = keys.Position.X + keys.Size.X * 0.5f, keysHalf = hud.HeldTextWidth * 0.5f;
                    Check(keysMid - keysHalf >= bar.Position.X - 0.5f && keysMid + keysHalf <= bar.End.X + 0.5f,
                        $"the keys line's words run from x {keysMid - keysHalf:0} to {keysMid + keysHalf:0}, past the recipe bar at {bar.Position.X:0}..{bar.End.X:0}");
                    var at = new Vector3(BuildRules.TileCentre(_buildTx), 0f, BuildRules.TileCentre(_buildTz));
                    Check(view.GhostShown && view.GhostGreen && view.GhostPosition.DistanceTo(at) < 0.01f, $"the ghost is drawn {(view.GhostShown ? view.GhostGreen ? "green" : "red" : "nowhere")} at {view.GhostPosition}, the tile at {at}");
                    Shot(_buildStep == BuildStep.Ghost ? "build-ghost" : "build-again");
                    place = true;
                    NextBuildStep(_buildStep == BuildStep.Ghost ? BuildStep.Wall : BuildStep.Done);
                    break;
                }

                case BuildStep.Wall:
                    if (built == null || server.Constructs.At(_buildTx, _buildTz) == null || client.PendingRequestCount > 0) break;
                    view.Refresh();
                    Check(built.Def.Id == "wall_wood" && built.Rotation == 1 && CarriedLogs(world) == _buildLogs - 3, $"built a {built.Def.Id} turned {built.Rotation}, {_buildLogs} Logs became {CarriedLogs(world)}");
                    Check(view.Drawn == 1, $"{view.Drawn} constructs drawn with one built");
                    Check(_session.Feed.Entries.Any(e => e.Text == "You built a Wooden Wall"), "the feed does not say \"You built a Wooden Wall\"");
                    Shot("build-wall");
                    NextBuildStep(BuildStep.Red);
                    break;

                case BuildStep.Red:
                {
                    // Along the ground well past the hands' reach, beside the wall: the ghost goes, red, and says why.
                    AimAtTile(_buildTx - 1, world.Map.InBounds(_buildTx - 1, _buildTz + 5) ? _buildTz + 5 : _buildTz - 5);
                    build.Update(1.0 / 30.0, false, false);
                    hud.Refresh();
                    view.Refresh();
                    var g = build.Ghost;
                    if (g.Aim != BuildAim.Place || _buildFrames < 4) break;
                    Check(g.State != OpResult.Ok && view.GhostShown && !view.GhostGreen && hud.PromptMark == PromptTone.Mismatch && hud.PromptText == HudText.BuildPrompt(world, g, out _),
                        $"the ghost out of reach is {g.State}, drawn {(view.GhostGreen ? "green" : "red")}, prompt \"{hud.PromptText}\"");
                    Shot("build-red");
                    NextBuildStep(BuildStep.TakeDown);
                    break;
                }

                case BuildStep.TakeDown:
                case BuildStep.ChestDown:
                    if (built != null)
                    {
                        AimAtBuilt(built);
                        if (_buildFrames < 3) break;
                        remove = true;
                        if (_buildFrames == 3)
                        {
                            build.Update(1.0 / 30.0, false, false);
                            hud.Refresh();
                            Check(build.Ghost.Aim == BuildAim.Construct && build.Ghost.State == OpResult.Ok && hud.PromptText.StartsWith($"Take down {built.Def.Name}", StringComparison.Ordinal),
                                $"looking at the {built.Def.Name} the prompt is \"{hud.PromptText}\" ({build.Ghost.Aim} {build.Ghost.State})");
                        }
                        if (hud.HoldShown > 0f) Shot($"build-takedown-{built.Def.Id}");
                        break;
                    }
                    if (server.Constructs.At(_buildTx, _buildTz) != null || client.PendingRequestCount > 0) break;
                    view.Refresh();
                    Check(CarriedLogs(world) == _buildLogs && view.Drawn == 0, $"taken down in Prep: {CarriedLogs(world)} of {_buildLogs} Logs back, {view.Drawn} drawn");
                    if (_buildStep == BuildStep.TakeDown)
                    {
                        Check(build.PressKey(1) && build.Recipe!.Building.Id == "chest", "key 2 in Build mode is not the Chest");
                        NextBuildStep(BuildStep.Chest);
                    }
                    else
                    {
                        // Back off the tile, to where the first wall was built from.
                        build.PressKey(0);
                        _buildX = BuildRules.TileCentre(_buildTx) - BuildStandOff;
                        var player = server.GetPlayer(world.LocalId)!;
                        player.Move.X = _buildX;
                        player.Move.Z = _buildZ;
                        NextBuildStep(BuildStep.Again);
                    }
                    break;

                case BuildStep.Chest:
                    AimAtTile(_buildTx, _buildTz);
                    if (built == null)
                    {
                        place = _buildFrames == 4;
                        break;
                    }
                    if (server.Constructs.At(_buildTx, _buildTz) == null || client.PendingRequestCount > 0) break;
                    Check(built.Def.Id == "chest" && built.ContainerId != 0 && CarriedLogs(world) == _buildLogs - 4, $"built a {built.Def.Id} (container {built.ContainerId}) for {_buildLogs - CarriedLogs(world)} Logs");
                    NextBuildStep(BuildStep.Open);
                    break;

                case BuildStep.Open:
                {
                    // E on the chest that was built opens it, as one of the map's: a step closer, into the hands' reach.
                    if (_buildFrames == 1)
                    {
                        _buildX = BuildRules.TileCentre(_buildTx) - 1.4f;
                        var player = server.GetPlayer(world.LocalId)!;
                        player.Move.X = _buildX;
                        player.Move.Z = _buildZ;
                    }
                    AimAtBuilt(built!);
                    _session.Interaction.Update(1.0 / 30.0, false, false, interactTapped: arrived && _buildFrames % 20 == 10);
                    var prompt = _session.Interaction.Prompt;
                    if (world.OpenContainerId != built!.ContainerId) break;
                    hud.Refresh();
                    Check(prompt.Kind == InteractKind.CloseChest && prompt.Built == built && _screen.Inventory.Visible, $"the built chest is open, the prompt {prompt.Kind}, the panel {(_screen.Inventory.Visible ? "up" : "down")}");
                    Shot("build-chest-open");
                    client.CloseContainer();
                    NextBuildStep(BuildStep.Close);
                    break;
                }

                case BuildStep.Close:
                    if (world.OpenContainerId != 0 || client.PendingRequestCount > 0) break;
                    NextBuildStep(BuildStep.ChestDown);
                    break;

                case BuildStep.Done:
                    if (built == null || server.Constructs.At(_buildTx, _buildTz) == null || client.PendingRequestCount > 0) break;
                    Check(!build.Toggle() && !build.Active, "B did not put Build mode down");
                    hud.Refresh();
                    view.Refresh();
                    Check(hud.RecipeBarText.Length == 0 && hud.RecipeShown == -1 && !view.GhostShown, $"with Build mode down the bar reads \"{hud.RecipeBarText}\" and the ghost is {(view.GhostShown ? "up" : "down")}");
                    foreach (char c in FontCheck.MissingGlyphs(hud))
                        if (_missingGlyphs.IndexOf(c) < 0) _missingGlyphs += c;
                    GD.Print($"[smoke] build: a Wooden Wall and a Chest built at ({_buildTx}, {_buildTz}), opened, taken down for every Log, the wall left standing; {CarriedLogs(world)} Logs carried");
                    _buildVisits++;
                    _buildStep = BuildStep.Start;
                    _session.FixedTicksPerFrame = RunOutTicksPerFrame;
                    return false;
            }
            if (_buildStep != BuildStep.Open && _buildStep != BuildStep.Close) build.Update(1.0 / 30.0, place, remove);
            return true;
        }

        /// <summary>After the run: something was built once, and in the Lobby nothing built is left on either end.</summary>
        private void VerifyBuild()
        {
            var world = _session.Client.World!;
            var server = _session.Server!.World;
            var view = _screen.Constructs;
            view.Refresh();
            Check(_buildVisits == 1, $"the smoke built {_buildVisits} times");
            Check(world.Constructs.All.Count == 0 && server.Constructs.All.Count == 0 && view.Drawn == 0,
                $"in the Lobby {world.Constructs.All.Count} constructs stand here, {server.Constructs.All.Count} on the server, {view.Drawn} drawn");
            Check(!_session.Build.Active, "Build mode is still up in the Lobby");
        }
    }
}
