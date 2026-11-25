using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.App
{
    /// <summary>
    /// Conveyor belts in the smoke's run (doc 04 §2.1, §1.1): in the Prep after the defences the
    /// host is handed five Planks and five Iron Ingots, finds five open tiles in a row that end at
    /// a mailbox, and in Build mode presses on the first, holds across to the last and lets go:
    /// the ghost is five green tiles with "Lay 5 Conveyor Belts", one request lays them, one
    /// segment ends at the mailbox on both ends. The server puts three letters for that house on
    /// the belt; they ride to the end, where the shut mailbox (Prep) holds them, the same on both
    /// ends and drawn. In Delivery the mailbox takes them, and the feed says a belt did.
    /// </summary>
    public partial class SmokeDriver
    {
        private const int BeltLine = 5;
        private int _beltStep, _beltFrames, _beltVisits, _beltDelivered, _pipeHold;
        private int _beltX0, _beltZ0, _beltX1, _beltZ1, _beltDest = -1;
        /// <summary>Interact held at the line (doc 04 §2.1 pushing a belt); where the ride began.</summary>
        private bool _beltPushing;
        private float _rideX, _rideZ;

        /// <summary>A row or column of open tiles whose last one faces a mailbox's tile, the host standing beside its middle.</summary>
        private bool FindBeltSite(ClientWorld world)
        {
            var map = world.Map;
            foreach (var d in map.Destinations)
            {
                int mx = (int)MathF.Floor(d.MailboxX / SimConstants.TileSize), mz = (int)MathF.Floor(d.MailboxZ / SimConstants.TileSize);
                for (int dir = 0; dir < 4; dir++)
                {
                    var (dx, dz) = BeltNetwork.Dirs[dir];
                    // The line runs towards the mailbox: its last tile is next to it.
                    int x1 = mx - dx, z1 = mz - dz, x0 = x1 - dx * (BeltLine - 1), z0 = z1 - dz * (BeltLine - 1);
                    bool open = true;
                    for (int i = 0; i < BeltLine && open; i++)
                        open = BuildRules.CheckTile(map, world.Constructs, x0 + dx * i, z0 + dz * i) == OpResult.Ok;
                    if (!open) continue;
                    // Stand on the middle tile: a belt lies flat, it is laid under the feet.
                    float sx = BuildRules.TileCentre(x0 + dx * (BeltLine / 2)), sz = BuildRules.TileCentre(z0 + dz * (BeltLine / 2));
                    if (!FreeGround(map, sx, sz, -1)) continue;
                    (_beltX0, _beltZ0, _beltX1, _beltZ1, _beltDest) = (x0, z0, x1, z1, d.Id);
                    var player = _session.Server!.World.GetPlayer(world.LocalId)!;
                    player.Move.X = sx;
                    player.Move.Z = sz;
                    return true;
                }
            }
            return false;
        }

        /// <summary>A frame of Prep. Returns true while the belt is being laid.</summary>
        private bool StepBelts()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var view = _screen.Constructs;
            var build = _session.Build;
            var belt = world.Content.Recipes.First(r => r.Building.Id == "belt_mk1");

            if (_beltStep == 0)
            {
                if (_beltVisits > 0 || _defenceVisits == 0 || world.Local.Dead || client.PendingRequestCount > 0) return false;
                _session.FixedTicksPerFrame = 1;
                var player = server.GetPlayer(world.LocalId)!;
                Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("plank"), BeltLine)), "no room for five Planks");
                Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("iron_ingot"), BeltLine)), "no room for five Iron Ingots");
                if (!FindBeltSite(world))
                {
                    Fail("no row of five open tiles ends at a mailbox");
                    return false;
                }
                if (!build.Active) Check(build.Toggle(), $"B did not put Build mode up for the belt ({build.Refused})");
                int key = belt.Key;
                Check(build.PressKey(key) && build.Recipe == belt, $"key {key + 1} in Build mode is not the Conveyor Belt");
                // The wheel steps to the Fast Belt on the same key (doc 09 §2.7), red without its blueprint, and back.
                Check(build.Cycle(1) && build.Recipe?.Building.Id == "belt_mk2", $"the wheel on the belt's key is the {build.Recipe?.Building.Name}, not the Fast Belt");
                hud.Refresh();
                Check(hud.RecipeBarText.Contains("Fast Belt 2/4", StringComparison.Ordinal)
                    && hud.RecipeShown == key && world.RecipeState(build.Recipe!) == OpResult.NeedsBlueprint,
                    $"with the wheel on the Fast Belt the bar reads \"{hud.RecipeBarText}\" ({hud.RecipeShown}), {world.RecipeState(build.Recipe!)}");
                Check(build.Cycle(-1) && build.Recipe == belt, "the wheel back is not the Conveyor Belt");
                _beltStep = 1;
                _beltFrames = 0;
                return true;
            }

            if (++_beltFrames > BuildStepFrames)
            {
                Fail($"the belt stopped at step {_beltStep}: ghost {build.Ghost.Aim} ({build.Ghost.Tx}, {build.Ghost.Tz}) {build.Ghost.State} line {build.Ghost.Line?.Count}, "
                    + $"prompt \"{hud.PromptText}\", {server.Belts.Segments.Count} segments on the server, {server.Belts.ItemCount} items, {client.PendingRequestCount} requests pending");
                return false;
            }
            _session.Interaction.Update(1.0 / 30.0, _beltPushing, false);
            var seg = server.Belts.SegmentAt(_beltX0, _beltZ0);
            switch (_beltStep)
            {
                case 1:
                    // The press on the first tile.
                    AimAtTile(_beltX0, _beltZ0);
                    if (_beltFrames < 6) break;
                    build.Update(1.0 / 30.0, true, false, placeHeld: true);
                    Check(build.Ghost.Line != null && build.Ghost.Line.Count == 1, $"the press began no line ({build.Ghost.Aim} {build.Ghost.State})");
                    _beltStep = 2;
                    _beltFrames = 0;
                    return true;

                case 2:
                {
                    // Held across to the last tile, then let go.
                    AimAtTile(_beltX1, _beltZ1);
                    build.Update(1.0 / 30.0, false, false, placeHeld: true);
                    if (_beltFrames < 4) return true;
                    hud.Refresh();
                    view.Refresh();
                    var g = build.Ghost;
                    string want = $"Lay {BeltLine} Conveyor Belts";
                    Check(g.Line != null && g.Line.Count == BeltLine && g.LineOk == BeltLine && hud.PromptText.StartsWith(want, StringComparison.Ordinal) && hud.PromptMark == PromptTone.Match,
                        $"the drag line is {g.Line?.Count} tiles ({g.LineOk} green), the prompt \"{hud.PromptText}\"");
                    Check(view.GhostShown && view.GhostGreen && view.GhostTiles == BeltLine, $"the drag's ghost is {view.GhostTiles} tiles, {(view.GhostGreen ? "green" : "red")}");
                    Shot("belt-drag");
                    build.Update(1.0 / 30.0, false, false, placeHeld: false);
                    _beltStep = 3;
                    _beltFrames = 0;
                    return true;
                }

                case 3:
                {
                    if (client.PendingRequestCount > 0 || seg == null || world.Belts.SegmentAt(_beltX0, _beltZ0) == null) break;
                    var mine = world.Belts.SegmentAt(_beltX0, _beltZ0)!;
                    Check(seg.Tiles.Count == BeltLine && seg.End == BeltEndKind.Mailbox && seg.DestId == _beltDest && mine.Tiles.Count == BeltLine && mine.End == BeltEndKind.Mailbox,
                        $"the line is a segment of {seg.Tiles.Count} tiles ending at {seg.End} {seg.DestId} on the server, {mine.Tiles.Count} at {mine.End} here");
                    int planks = BuildRules.Have(world.Hotbar, world.Inventory, world.Content.Item("plank"));
                    Check(planks == 0 && _session.Feed.Entries.Any(e => e.Text.Contains("Conveyor Belt", StringComparison.Ordinal)),
                        $"after the line {planks} Planks are left; the feed: {string.Join(" / ", _session.Feed.Entries.Select(e => e.Text))}");
                    Shot("belt-line");
                    _beltStep = 4;
                    _beltFrames = 0;
                    break;
                }

                case 4:
                {
                    // One a third of a second onto the head, two lanes of it.
                    if (_beltFrames % 10 == 1 && _beltFrames < 30)
                    {
                        var dest = world.Map.GetDestination(_beltDest)!;
                        var kind = world.Content.MailKind("letter");
                        uint n = (uint)(_beltFrames / 10);
                        var mail = new MailItem { Id = 990001 + n, Kind = kind.Index, Address = dest.Address, Value = (ushort)kind.BaseValue, SpawnShift = (byte)server.Run.Shift, DeadlineShift = (byte)(server.Run.Shift + 1) };
                        Check(seg != null && server.PutOnBelt(seg, mail), $"no room on the belt for letter {n + 1}");
                    }
                    // 10 m at 2 m/s: they back up at the shut mailbox, the same on both ends.
                    if (_beltFrames < 7 * 30) break;
                    var mine = world.Belts.SegmentAt(_beltX0, _beltZ0)!;
                    string Ids(BeltSegment s) => string.Join(",", s.Items.OrderBy(it => it.Lane).ThenByDescending(it => it.Pos).Select(it => $"{it.Lane}/{it.Mail.Id}"));
                    Check(seg != null && seg.Items.Count == 3 && Ids(seg) == Ids(mine) && seg.Items.Max(it => it.Pos) >= seg.Length - 1e-3f,
                        $"on the belt: server {Ids(seg!)} at the end {seg!.Items.Max(it => it.Pos):0.00} of {seg.Length}, here {Ids(mine)}");
                    _screen.BeltItems.Refresh();
                    Check(_screen.BeltItems.Drawn == 3 && world.BeltMismatches == 0, $"{_screen.BeltItems.Drawn} items drawn on the belt, {world.BeltMismatches} checksums off");
                    // A look along the line at the mail waiting at its end.
                    AimAtTile(_beltX1, _beltZ1);
                    Shot("belt-mail");
                    GD.Print($"[smoke] belts: a line of {BeltLine} Conveyor Belts dragged from ({_beltX0}, {_beltZ0}) to ({_beltX1}, {_beltZ1}) at the mailbox of {world.Map.GetDestination(_beltDest)!.Label}, three letters ridden to its end");
                    _beltStep = 40;
                    _beltFrames = 0;
                    return true;
                }

                case 40:
                {
                    // Pushed (doc 04 §2.1): beside the line's second tile, interact held at it.
                    int tx = _beltX0 + Math.Sign(_beltX1 - _beltX0), tz = _beltZ0 + Math.Sign(_beltZ1 - _beltZ0);
                    if (_beltFrames == 1)
                    {
                        var player = server.GetPlayer(world.LocalId)!;
                        // Off the line's side the player stands on.
                        bool alongX = _beltZ0 == _beltZ1;
                        float side = alongX ? MathF.Sign(player.Move.Z - BuildRules.TileCentre(tz)) : MathF.Sign(player.Move.X - BuildRules.TileCentre(tx));
                        if (side == 0f) side = 1f;
                        player.Move.X = BuildRules.TileCentre(tx) + (alongX ? 0f : 1.4f * side);
                        player.Move.Z = BuildRules.TileCentre(tz) + (alongX ? 1.4f * side : 0f);
                    }
                    AimAtTile(tx, tz);
                    if (_beltFrames < 8) break;
                    var prompt = _session.Interaction.Prompt;
                    if (!_beltPushing)
                    {
                        Check(prompt.Kind == InteractKind.PushBelt && HudText.Prompt(world, prompt, out _).StartsWith("Push the Conveyor Belt", StringComparison.Ordinal),
                            $"beside the belt the prompt is {prompt.Kind} \"{HudText.Prompt(world, prompt, out _)}\"");
                        _beltPushing = true;
                        break;
                    }
                    if (_beltFrames < 20) break;
                    var mine = world.Belts.SegmentAt(_beltX0, _beltZ0)!;
                    Check(prompt.Pushing && seg != null && seg.PushTicks > 0 && mine.PushTicks > 0,
                        $"held at the belt: pushing {prompt.Pushing}, {seg?.PushTicks} ticks of push on the server, {mine.PushTicks} here");
                    Shot("belt-push");
                    _beltPushing = false;
                    // On the head of the line: carried along it.
                    var truth = server.GetPlayer(world.LocalId)!;
                    truth.Move.X = BuildRules.TileCentre(_beltX0);
                    truth.Move.Z = BuildRules.TileCentre(_beltZ0);
                    _beltStep = 41;
                    _beltFrames = 0;
                    return true;
                }

                case 41:
                {
                    if (_beltFrames == 10)
                    {
                        _rideX = world.Local.X;
                        _rideZ = world.Local.Z;
                    }
                    if (_beltFrames < 40) break;
                    var truth = server.GetPlayer(world.LocalId)!;
                    float along = (world.Local.X - _rideX) * Math.Sign(_beltX1 - _beltX0) + (world.Local.Z - _rideZ) * Math.Sign(_beltZ1 - _beltZ0);
                    float floor = world.Map.HeightAt(world.Local.X, world.Local.Z) + BeltNetwork.SurfaceHeight;
                    Check(along > 1.6f && along < 2.4f && MathF.Abs(world.Local.Y - floor) < 0.01f && MathF.Abs(world.Local.X - truth.Move.X) + MathF.Abs(world.Local.Z - truth.Move.Z) < 0.3f,
                        $"on the belt the host went {along:0.00} m along it in a second at y {world.Local.Y:0.00} (its top {floor:0.00}), the server has them {truth.Move.X - world.Local.X:0.00}, {truth.Move.Z - world.Local.Z:0.00} off");
                    GD.Print($"[smoke] belt push and ride: the line pushed to 3 m/s while held, and the host carried {along:0.00} m along it in a second, standing on it");
                    // Off it again, beside its head.
                    truth.Move.X = BuildRules.TileCentre(_beltX0) + (_beltZ0 == _beltZ1 ? 0f : 1.4f);
                    truth.Move.Z = BuildRules.TileCentre(_beltZ0) + (_beltZ0 == _beltZ1 ? 1.4f : 0f);
                    _beltStep = 5;
                    _beltFrames = 0;
                    return true;
                }

                case 5:
                {
                    // The Intake's outfeed (doc 04 §2.6): two belts dragged north from the tile north of it.
                    var belts = world.Belts;
                    int ox = belts.OutfeedX, oz = belts.OutfeedZ;
                    if (_beltFrames == 1)
                    {
                        var player = server.GetPlayer(world.LocalId)!;
                        Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("plank"), 2)), "no room for two Planks");
                        Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("iron_ingot"), 2)), "no room for two Iron Ingots");
                        Check(BuildRules.CheckTile(world.Map, world.Constructs, ox, oz) == OpResult.Ok && BuildRules.CheckTile(world.Map, world.Constructs, ox, oz - 1) == OpResult.Ok,
                            $"the outfeed's tiles ({ox}, {oz}) and ({ox}, {oz - 1}) cannot take a belt");
                        // Beside the counter, south-east of the outfeed.
                        player.Move.X = BuildRules.TileCentre(ox + 1);
                        player.Move.Z = BuildRules.TileCentre(oz + 1);
                        _outfeedIntake = IntakeMail(server.Intake);
                    }
                    AimAtTile(ox, oz);
                    if (_beltFrames < 8) break;
                    build.Update(1.0 / 30.0, true, false, placeHeld: true);
                    _beltStep = 6;
                    _beltFrames = 0;
                    return true;
                }

                case 6:
                {
                    var belts = world.Belts;
                    AimAtTile(belts.OutfeedX, belts.OutfeedZ - 1);
                    build.Update(1.0 / 30.0, false, false, placeHeld: true);
                    if (_beltFrames < 4) return true;
                    hud.Refresh();
                    var g = build.Ghost;
                    Check(g.Line != null && g.Line.Count == 2 && g.LineOk == 2 && g.Rotation == BeltNetwork.OutfeedRotation && hud.PromptText.Contains("fed by the Intake", StringComparison.Ordinal),
                        $"the outfeed's drag is {g.Line?.Count} tiles ({g.LineOk} green) turned {g.Rotation}, the prompt \"{hud.PromptText}\"");
                    Shot("outfeed-drag");
                    build.Update(1.0 / 30.0, false, false, placeHeld: false);
                    // Mail for the outfeed to give, whatever the Intake has already.
                    var dest = world.Map.GetDestination(_beltDest)!;
                    var kind = world.Content.MailKind("letter");
                    for (uint i = 0; i < 2; i++)
                        Check(server.Give(server.Intake, ItemStack.OfMail(new MailItem { Id = 990011 + i, Kind = kind.Index, Address = dest.Address, Value = (ushort)kind.BaseValue, SpawnShift = (byte)server.Run.Shift, DeadlineShift = (byte)(server.Run.Shift + 1) })),
                            "no room in the Intake for a letter");
                    _outfeedIntake += 2;
                    _beltStep = 7;
                    _beltFrames = 0;
                    return true;
                }

                case 7:
                {
                    var outfeed = server.Belts.Outfeed;
                    if (_beltFrames < 3 * 30 || outfeed == null || client.PendingRequestCount > 0) break;
                    var mine = world.Belts.Outfeed;
                    string Ids(BeltSegment s) => string.Join(",", s.Items.OrderBy(it => it.Lane).ThenByDescending(it => it.Pos).Select(it => $"{it.Lane}/{it.Mail.Id}"));
                    int taken = _outfeedIntake - IntakeMail(server.Intake);
                    Check(mine != null && outfeed.Tiles.Count == 2 && outfeed.Items.Count >= 2 && taken == outfeed.Items.Count && Ids(outfeed) == Ids(mine)
                        && outfeed.Items.Max(it => it.Mail.Id) < OldestIn(server.Intake) && outfeed.Items.Max(it => it.Pos) >= outfeed.Length - 1e-3f,
                        $"the outfeed's belt holds {outfeed.Items.Count} ({Ids(outfeed)}; here {(mine == null ? "none" : Ids(mine))}), the Intake gave {taken}");
                    _screen.BeltItems.Refresh();
                    Check(_screen.BeltItems.Drawn >= 3 + outfeed.Items.Count, $"{_screen.BeltItems.Drawn} items drawn on the belts");
                    AimAtTile(world.Belts.OutfeedX, world.Belts.OutfeedZ - 1);
                    Shot("outfeed-mail");
                    GD.Print($"[smoke] outfeed: two belts dragged north from the Intake's outfeed at ({world.Belts.OutfeedX}, {world.Belts.OutfeedZ}), {outfeed.Items.Count} pieces of mail out of the Intake onto them, oldest first");
                    // Taken down again: the mail on them goes back into the Intake for the shift.
                    foreach (var tile in outfeed.Tiles)
                    {
                        var built = world.Constructs.At(tile % world.Map.Cols, tile / world.Map.Cols);
                        if (built != null) client.RemoveConstruct(built);
                    }
                    _beltStep = 8;
                    _beltFrames = 0;
                    break;
                }

                case 8:
                {
                    if (client.PendingRequestCount > 0 || server.Belts.Outfeed != null) break;
                    Check(IntakeMail(server.Intake) == _outfeedIntake && world.Belts.Outfeed == null, $"after the outfeed's belts went the Intake holds {IntakeMail(server.Intake)} of {_outfeedIntake}");
                    // A Splitter (doc 04 §2.3) on open ground two tiles off, by its key.
                    var splitter = world.Content.Recipes.First(r => r.Building.Id == "splitter");
                    var player = server.GetPlayer(world.LocalId)!;
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("plank"), 1)), "no room for a Plank");
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("iron_ingot"), 2)), "no room for two Iron Ingots");
                    int hx = (int)MathF.Floor(world.Local.X / SimConstants.TileSize), hz = (int)MathF.Floor(world.Local.Z / SimConstants.TileSize);
                    _splitX = -1;
                    for (int r = 2; r <= 3 && _splitX < 0; r++)
                        for (int dz = -r; dz <= r && _splitX < 0; dz++)
                            for (int dx = -r; dx <= r && _splitX < 0; dx++)
                                if ((Math.Abs(dx) == r || Math.Abs(dz) == r) && BuildRules.CheckTile(world.Map, world.Constructs, hx + dx, hz + dz) == OpResult.Ok)
                                    (_splitX, _splitZ) = (hx + dx, hz + dz);
                    if (_splitX < 0)
                    {
                        Fail("no open tile near the Intake for the Splitter");
                        return false;
                    }
                    int key = splitter.Key;
                    Check(build.PressKey(key) && build.Recipe == splitter, $"key {key + 1} in Build mode is not the Splitter");
                    _beltStep = 9;
                    _beltFrames = 0;
                    break;
                }

                case 9:
                {
                    AimAtTile(_splitX, _splitZ);
                    var built = world.Constructs.At(_splitX, _splitZ);
                    if (built == null)
                    {
                        if (_beltFrames == 6)
                        {
                            build.Update(1.0 / 30.0, false, false);
                            hud.Refresh();
                            Check(build.Ghost.State == OpResult.Ok && hud.PromptText.StartsWith("Build Splitter", StringComparison.Ordinal), $"the Splitter's ghost is {build.Ghost.State}, the prompt \"{hud.PromptText}\"");
                            build.Update(1.0 / 30.0, true, false);
                            return true;
                        }
                        break;
                    }
                    if (client.PendingRequestCount > 0) break;
                    view.Refresh();
                    Check(built.Def.Id == "splitter" && server.Constructs.At(_splitX, _splitZ)?.Def.Id == "splitter" && world.Belts.SegmentAt(_splitX, _splitZ) != null,
                        $"the Splitter at ({_splitX}, {_splitZ}) is a {built.Def.Id}, on the server {server.Constructs.At(_splitX, _splitZ)?.Def.Id}");
                    Shot("splitter");
                    GD.Print($"[smoke] splitter: built at ({_splitX}, {_splitZ}) by its key, a belt segment of its own on both ends");
                    client.RemoveConstruct(built);
                    _beltStep = 10;
                    _beltFrames = 0;
                    break;
                }

                case 10:
                {
                    if (client.PendingRequestCount > 0 || server.Constructs.At(_splitX, _splitZ) != null) break;
                    // A Belt Ramp (doc 04 §2.1), two tiles long, by the wheel on the belt's key.
                    var player = server.GetPlayer(world.LocalId)!;
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("plank"), 5)), "no room for five Planks");
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("iron_ingot"), 3)), "no room for three Iron Ingots");
                    var ramp = world.Content.Building("belt_mk1_ramp");
                    int hx = (int)MathF.Floor(world.Local.X / SimConstants.TileSize), hz = (int)MathF.Floor(world.Local.Z / SimConstants.TileSize);
                    _splitX = -1;
                    for (int r = 2; r <= 3 && _splitX < 0; r++)
                        for (int dz = -r; dz <= r && _splitX < 0; dz++)
                            for (int dx = -r; dx <= r && _splitX < 0; dx++)
                                for (int rot = 0; rot < 4 && _splitX < 0; rot++)
                                    if ((Math.Abs(dx) == r || Math.Abs(dz) == r) && BuildRules.CheckPlacement(world.Map, world.Constructs, ramp, hx + dx, hz + dz, rot) == OpResult.Ok
                                        && BuildRules.InRange(hx + dx + 2 * BeltNetwork.Dirs[rot].Dx, hz + dz + 2 * BeltNetwork.Dirs[rot].Dz, world.Local.X, world.Local.Z, 7f))
                                        (_splitX, _splitZ, _rampRot) = (hx + dx, hz + dz, rot);
                    if (_splitX < 0)
                    {
                        Fail("no two open tiles near the Intake for the Belt Ramp");
                        return false;
                    }
                    var belt0 = world.Content.Recipes.First(r => r.Building.Id == "belt_mk1");
                    Check(build.PressKey(belt0.Key), "the belt's key did not take");
                    for (int i = 0; i < 4 && build.Recipe?.Building != ramp; i++) build.Cycle(1);
                    for (int i = 0; i < 8 && build.Rotation != _rampRot; i++) build.Rotate();
                    Check(build.Recipe?.Building == ramp && build.Rotation == _rampRot, $"the wheel never came to the Belt Ramp ({build.Recipe?.Building.Name}, turned {build.Rotation})");
                    _beltStep = 11;
                    _beltFrames = 0;
                    break;
                }

                case 11:
                {
                    AimAtTile(_splitX, _splitZ);
                    var built = world.Constructs.At(_splitX, _splitZ);
                    if (built == null)
                    {
                        if (_beltFrames == 6)
                        {
                            build.Update(1.0 / 30.0, false, false);
                            hud.Refresh();
                            Check(build.Ghost.State == OpResult.Ok && hud.PromptText.StartsWith("Build Belt Ramp", StringComparison.Ordinal) && hud.PromptText.Contains("up a level", StringComparison.Ordinal),
                                $"the Belt Ramp's ghost is {build.Ghost.State}, the prompt \"{hud.PromptText}\"");
                            build.Update(1.0 / 30.0, true, false);
                            return true;
                        }
                        break;
                    }
                    if (client.PendingRequestCount > 0) break;
                    var (x2, z2) = built.TileAt(1);
                    view.Refresh();
                    Check(built.Def.Id == "belt_mk1_ramp" && world.Constructs.At(x2, z2) == built && server.Constructs.At(x2, z2)?.Def.Id == "belt_mk1_ramp"
                        && world.Belts.SegmentAt(_splitX, _splitZ)?.Tiles.Count == 2,
                        $"the Belt Ramp at ({_splitX}, {_splitZ}) is a {built.Def.Id}, its second tile ({x2}, {z2}) holds {world.Constructs.At(x2, z2)?.Def.Id} here, {server.Constructs.At(x2, z2)?.Def.Id} on the server");
                    AimAtTile(x2, z2);
                    Shot("ramp");
                    GD.Print($"[smoke] ramp: a Belt Ramp built at ({_splitX}, {_splitZ}) on ({x2}, {z2}) too by the wheel on the belt's key, one segment of two tiles");
                    client.RemoveConstruct(built);
                    _beltStep = 12;
                    _beltFrames = 0;
                    break;
                }

                case 12:
                {
                    if (client.PendingRequestCount > 0 || server.Constructs.At(_splitX, _splitZ) != null) break;
                    // An Elevated Belt on the street (doc 04 §1.1, the crossing), standing on it.
                    var map = world.Map;
                    int hx = (int)MathF.Floor(world.Local.X / SimConstants.TileSize), hz = (int)MathF.Floor(world.Local.Z / SimConstants.TileSize);
                    _splitX = -1;
                    for (int r = 0; r <= 12 && _splitX < 0; r++)
                        for (int dz = -r; dz <= r && _splitX < 0; dz++)
                            for (int dx = -r; dx <= r && _splitX < 0; dx++)
                                if ((Math.Abs(dx) == r || Math.Abs(dz) == r) && map.GetTile(hx + dx, hz + dz) == TileType.Street
                                    && map.GetTile(hx + dx + 1, hz + dz) == TileType.Street && BuildRules.CheckTile(map, world.Constructs, hx + dx, hz + dz, onStreet: true) == OpResult.Ok)
                                    (_splitX, _splitZ) = (hx + dx, hz + dz);
                    if (_splitX < 0)
                    {
                        Fail("no street near the Intake for the Elevated Belt");
                        return false;
                    }
                    var player = server.GetPlayer(world.LocalId)!;
                    player.Move.X = BuildRules.TileCentre(_splitX + 1) + 0.5f;
                    player.Move.Z = BuildRules.TileCentre(_splitZ);
                    var elevated = world.Content.Building("belt_mk1_elevated");
                    for (int i = 0; i < 4 && build.Recipe?.Building != elevated; i++) build.Cycle(1);
                    Check(build.Recipe?.Building == elevated, $"the wheel never came to the Elevated Belt ({build.Recipe?.Building.Name})");
                    _beltStep = 13;
                    _beltFrames = 0;
                    break;
                }

                case 13:
                {
                    AimAtTile(_splitX, _splitZ);
                    var built = world.Constructs.At(_splitX, _splitZ);
                    if (built == null)
                    {
                        if (_beltFrames == 10)
                        {
                            build.Update(1.0 / 30.0, false, false);
                            hud.Refresh();
                            Check(build.Ghost.State == OpResult.Ok && hud.PromptText.StartsWith("Build Elevated Belt", StringComparison.Ordinal),
                                $"the Elevated Belt's ghost on the street at ({_splitX}, {_splitZ}) is {build.Ghost.State} ({build.Ghost.Tx}, {build.Ghost.Tz}), the prompt \"{hud.PromptText}\"");
                            build.Update(1.0 / 30.0, true, false, placeHeld: true);
                            build.Update(1.0 / 30.0, false, false, placeHeld: false);
                            return true;
                        }
                        break;
                    }
                    if (client.PendingRequestCount > 0) break;
                    view.Refresh();
                    Check(built.Def.Id == "belt_mk1_elevated" && world.Map.GetTile(_splitX, _splitZ) == TileType.Street && server.Constructs.At(_splitX, _splitZ)?.Def.Id == "belt_mk1_elevated",
                        $"on the street at ({_splitX}, {_splitZ}) stands a {built.Def.Id}, on the server {server.Constructs.At(_splitX, _splitZ)?.Def.Id}");
                    // Looked at from the street beside it: a level up, on its posts.
                    _session.Client.Input.PitchRadians = 0.25f;
                    Shot("elevated");
                    GD.Print($"[smoke] elevated: an Elevated Belt built on the street at ({_splitX}, {_splitZ}) by the wheel, {BeltNetwork.LevelHeight:0.0} m up");
                    client.RemoveConstruct(built);
                    _beltStep = 14;
                    _beltFrames = 0;
                    break;
                }

                case 14:
                {
                    if (client.PendingRequestCount > 0 || server.Constructs.At(_splitX, _splitZ) != null) break;
                    // The Address Sorter (doc 04 §2.4): its blueprint bought at the counter with the card's key.
                    var player = server.GetPlayer(world.LocalId)!;
                    var sorting = world.Content.ShopItem("bp_sorting");
                    server.Economy.AddLoot(sorting.Price);
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("iron_ingot"), 4)), "no room for four Iron Ingots");
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("stone"), 6)), "no room for six Stones");
                    if (build.Active) build.Toggle();
                    player.Move.X = world.Map.PostOffice.ShopX + ShopStandOff;
                    player.Move.Z = world.Map.PostOffice.ShopZ;
                    _beltStep = 15;
                    _beltFrames = 0;
                    break;
                }

                case 15:
                {
                    var player = server.GetPlayer(world.LocalId)!;
                    var sorting = world.Content.ShopItem("bp_sorting");
                    float dx = world.Local.X - player.Move.X, dz = world.Local.Z - player.Move.Z;
                    if (dx * dx + dz * dz > 0.1f || _beltFrames < 4 || world.Global.Wallet != server.Economy.Wallet) break;
                    if (!world.ShopOpen) { client.OpenShop(); break; }
                    int place = world.ShopShelf.ToList().IndexOf(sorting);
                    Check(place >= 0 && world.ShopState(sorting) == OpResult.Ok, $"the Sorting blueprint's card is at {place}, {world.ShopState(sorting)}");
                    Check(client.PressShopKey(place), $"the key of the Sorting blueprint's card {HudText.CardKey(place)} was not the shop's");
                    _beltStep = 16;
                    _beltFrames = 0;
                    break;
                }

                case 16:
                {
                    if (!world.ShopOwned.Contains(world.Content.ShopItem("bp_sorting")) || client.PendingRequestCount > 0) break;
                    Check(server.OwnsBlueprint("bp_sorting"), "the server does not have the team owning the Sorting blueprint");
                    client.CloseShop();
                    var sorter = world.Content.Recipes.First(r => r.Building.Id == "address_sorter_mk1");
                    // Four open tiles, the host on open ground just behind its input.
                    var map = world.Map;
                    int hx = (int)MathF.Floor(world.Local.X / SimConstants.TileSize), hz = (int)MathF.Floor(world.Local.Z / SimConstants.TileSize);
                    _splitX = -1;
                    for (int r = 2; r <= 10 && _splitX < 0; r++)
                        for (int dz = -r; dz <= r && _splitX < 0; dz++)
                            for (int dx = -r; dx <= r && _splitX < 0; dx++)
                            {
                                int x = hx + dx, z = hz + dz;
                                if ((Math.Abs(dx) != r && Math.Abs(dz) != r) || BuildRules.CheckPlacement(map, world.Constructs, sorter.Building, x, z, 0) != OpResult.Ok) continue;
                                if (BuildRules.CheckTile(map, world.Constructs, x - 1, z) != OpResult.Ok || !FreeGround(map, BuildRules.TileCentre(x - 1), BuildRules.TileCentre(z), -1)) continue;
                                (_splitX, _splitZ) = (x, z);
                            }
                    if (_splitX < 0)
                    {
                        Fail("no four open tiles near the Post Office for the Address Sorter");
                        return false;
                    }
                    var player = server.GetPlayer(world.LocalId)!;
                    player.Move.X = BuildRules.TileCentre(_splitX - 1) - 0.4f;
                    player.Move.Z = BuildRules.TileCentre(_splitZ);
                    Check(build.Toggle(), $"B did not put Build mode up for the Address Sorter ({build.Refused})");
                    Check(build.PressKey(sorter.Key) && build.Recipe == sorter, $"key {sorter.Key + 1} in Build mode is not the Address Sorter");
                    for (int i = 0; i < 4 && build.Rotation != 0; i++) build.Rotate();
                    _beltStep = 17;
                    _beltFrames = 0;
                    break;
                }

                case 17:
                {
                    AimAtTile(_splitX, _splitZ);
                    var built = world.Constructs.At(_splitX, _splitZ);
                    if (built == null)
                    {
                        if (_beltFrames == 8)
                        {
                            build.Update(1.0 / 30.0, false, false);
                            hud.Refresh();
                            view.Refresh();
                            Check(build.Ghost.State == OpResult.Ok && hud.PromptText.StartsWith("Build Address Sorter", StringComparison.Ordinal) && view.GhostGreen,
                                $"the Address Sorter's ghost at ({_splitX}, {_splitZ}) is {build.Ghost.State} ({build.Ghost.Tx}, {build.Ghost.Tz}), the prompt \"{hud.PromptText}\"");
                            Shot("sorter-ghost");
                            build.Update(1.0 / 30.0, true, false);
                            return true;
                        }
                        break;
                    }
                    if (client.PendingRequestCount > 0) break;
                    view.Refresh();
                    bool four = Enumerable.Range(0, built.Def.TileCount).All(i => world.Constructs.At(built.TileAt(i).Tx, built.TileAt(i).Tz) == built
                        && server.Constructs.At(built.TileAt(i).Tx, built.TileAt(i).Tz)?.Def == built.Def);
                    Check(built.Def.Id == "address_sorter_mk1" && four, $"the Address Sorter at ({_splitX}, {_splitZ}) does not stand on its four tiles on both ends");
                    // A few steps back for its picture, then up to it again.
                    var player = server.GetPlayer(world.LocalId)!;
                    player.Move.X = BuildRules.TileCentre(_splitX - 1) - 4.5f;
                    _beltStep = 21;
                    _beltFrames = 0;
                    break;
                }

                case 21:
                {
                    var built = world.Constructs.At(_splitX, _splitZ)!;
                    var box = AimBox.Built(world.Map, built);
                    ViewRay.Aim(world.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                    if (_beltFrames < 8) break;
                    view.Refresh();
                    Shot("sorter");
                    var player = server.GetPlayer(world.LocalId)!;
                    player.Move.X = BuildRules.TileCentre(_splitX - 1) - 0.4f;
                    _beltStep = 22;
                    _beltFrames = 0;
                    break;
                }

                case 22:
                {
                    AimAtTile(_splitX, _splitZ);
                    if (_beltFrames < 8) break;
                    var built = world.Constructs.At(_splitX, _splitZ)!;
                    // In Build mode the crosshair on it says its filters are on the interact key.
                    build.Update(1.0 / 30.0, false, false);
                    hud.Refresh();
                    Check(build.Ghost.Aim == BuildAim.Construct && hud.PromptText.EndsWith(HudText.FiltersNote(built), StringComparison.Ordinal),
                        $"at the Address Sorter Build mode says \"{hud.PromptText}\"");
                    _session.Interaction.Update(1.0 / 30.0, false, false, interactTapped: true);
                    Check(world.FilterTarget == built, $"E at the Address Sorter did not put its filters up ({_session.Interaction.Prompt.Kind})");
                    _beltStep = 18;
                    _beltFrames = 0;
                    return true;
                }

                case 18:
                {
                    var panel = _screen.Inventory.Filters;
                    if (world.FilterStatus == null || !panel.IsVisibleInTree() || _beltFrames < 3) break;
                    Check(_screen.Mouse.PanelOpen && panel.Text == HudText.FilterText(world) && panel.CardCount == 4,
                        $"the filter panel reads \"{panel.Text}\" ({panel.CardCount} cards), not \"{HudText.FilterText(world)}\"");
                    Check(client.PressFilterKey(0), "key 1 was not the filters'");
                    _beltStep = 19;
                    _beltFrames = 0;
                    break;
                }

                case 19:
                {
                    var built = world.FilterTarget;
                    var truth = server.Constructs.At(_splitX, _splitZ);
                    if (built == null || truth == null || truth.Filter(0, 0).IsEmpty || client.PendingRequestCount > 0 || _beltFrames < 3) break;
                    var panel = _screen.Inventory.Filters;
                    string street = HudText.FilterName(world, built, truth.Filter(0, 0));
                    Check(built.Filter(0, 0) == truth.Filter(0, 0) && truth.Filter(0, 0).Kind == FilterKind.Street && panel.Text.Contains("\n" + street + "\n", StringComparison.Ordinal),
                        $"key 1 set the front to {truth.Filter(0, 0).Kind} {truth.Filter(0, 0).Value} on the server, {built.Filter(0, 0).Kind} here; the panel \"{panel.Text}\"");
                    Shot("sorter-filters");
                    GD.Print($"[smoke] sorter: an Address Sorter built at ({_splitX}, {_splitZ}) on four tiles, its filters up by E, the front set to {street} by key 1");
                    Check(client.CloseFilters(), "the filters were not up to take down");
                    client.RemoveConstruct(built);
                    _beltStep = 20;
                    _beltFrames = 0;
                    break;
                }

                case 20:
                {
                    if (client.PendingRequestCount > 0 || server.Constructs.At(_splitX, _splitZ) != null) break;
                    // An Inserter (doc 04 §2.5) on the outfeed's tile, out of the Intake onto a belt:
                    // the wheel on the sorter's key, turned to face away from the counter.
                    var player = server.GetPlayer(world.LocalId)!;
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("iron_ingot"), 3)), "no room for three Iron Ingots");
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("plank"), 2)), "no room for two Planks");
                    var belts = world.Belts;
                    player.Move.X = BuildRules.TileCentre(belts.OutfeedX + 1);
                    player.Move.Z = BuildRules.TileCentre(belts.OutfeedZ + 1);
                    var inserter = world.Content.Building("inserter");
                    var sorterKey = world.Content.Recipes.First(r => r.Building.Id == "address_sorter_mk1").Key;
                    Check(build.PressKey(sorterKey), "the sorter's key did not take");
                    for (int i = 0; i < 2 && build.Recipe?.Building != inserter; i++) build.Cycle(1);
                    for (int i = 0; i < 4 && build.Rotation != BeltNetwork.OutfeedRotation; i++) build.Rotate();
                    Check(build.Recipe?.Building == inserter && build.Rotation == BeltNetwork.OutfeedRotation, $"the wheel on key {sorterKey + 1} never came to the Inserter ({build.Recipe?.Building.Name}, turned {build.Rotation})");
                    _beltStep = 23;
                    _beltFrames = 0;
                    break;
                }

                case 23:
                {
                    var belts = world.Belts;
                    AimAtTile(belts.OutfeedX, belts.OutfeedZ);
                    if (_beltFrames < 8) break;
                    build.Update(1.0 / 30.0, false, false);
                    hud.Refresh();
                    Check(build.Ghost.State == OpResult.Ok && hud.PromptText.StartsWith("Build Inserter", StringComparison.Ordinal),
                        $"the Inserter's ghost on the outfeed is {build.Ghost.State}, the prompt \"{hud.PromptText}\"");
                    build.Update(1.0 / 30.0, true, false);
                    _beltStep = 24;
                    _beltFrames = 0;
                    return true;
                }

                case 24:
                {
                    var belts = world.Belts;
                    var built = world.Constructs.At(belts.OutfeedX, belts.OutfeedZ);
                    if (built == null || client.PendingRequestCount > 0) break;
                    Check(built.Def.Id == "inserter" && built.Facing == BeltNetwork.OutfeedRotation, $"on the outfeed stands a {built.Def.Id} turned {built.Facing}");
                    Check(client.PlaceConstruct(belt, belts.OutfeedX, belts.OutfeedZ - 1, BeltNetwork.OutfeedRotation) != 0, "the belt beyond the Inserter was not asked for");
                    // Mail for it to take, whatever the Intake has already.
                    var dest = world.Map.GetDestination(_beltDest)!;
                    var kind = world.Content.MailKind("letter");
                    for (uint i = 0; i < 2; i++)
                        Check(server.Give(server.Intake, ItemStack.OfMail(new MailItem { Id = 990021 + i, Kind = kind.Index, Address = dest.Address, Value = (ushort)kind.BaseValue, SpawnShift = (byte)server.Run.Shift, DeadlineShift = (byte)(server.Run.Shift + 1) })),
                            "no room in the Intake for a letter");
                    _beltStep = 25;
                    _beltFrames = 0;
                    break;
                }

                case 25:
                {
                    var belts = world.Belts;
                    var fed = server.Belts.SegmentAt(belts.OutfeedX, belts.OutfeedZ - 1);
                    AimAtTile(belts.OutfeedX, belts.OutfeedZ);
                    view.Animate(1f / 30f);
                    if (fed == null || fed.Items.Count == 0 || view.ArmsSwinging == 0 || _beltFrames < 10) break;
                    var mine = world.Belts.SegmentAt(belts.OutfeedX, belts.OutfeedZ - 1);
                    Check(mine != null && mine.Items.Count == fed.Items.Count && world.InserterSwings.ContainsKey(belts.OutfeedZ * world.Map.Cols + belts.OutfeedX),
                        $"the Inserter put {fed.Items.Count} pieces on the belt, {mine?.Items.Count} of them here");
                    Shot("inserter");
                    GD.Print($"[smoke] inserter: an Inserter on the outfeed by the wheel on key {world.Content.Recipes.First(r => r.Building.Id == "inserter").Key + 1}, {fed.Items.Count} pieces out of the Intake onto a belt, its arm swinging");
                    client.RemoveConstruct(world.Constructs.At(belts.OutfeedX, belts.OutfeedZ - 1)!);
                    _beltStep = 26;
                    _beltFrames = 0;
                    break;
                }

                case 26:
                {
                    var belts = world.Belts;
                    if (client.PendingRequestCount > 0 || server.Constructs.At(belts.OutfeedX, belts.OutfeedZ - 1) != null) break;
                    var built = world.Constructs.At(belts.OutfeedX, belts.OutfeedZ);
                    if (built != null) client.RemoveConstruct(built);
                    _beltStep = 50;
                    _beltFrames = 0;
                    break;
                }

                case 50:
                {
                    var belts = world.Belts;
                    if (client.PendingRequestCount > 0 || server.Constructs.At(belts.OutfeedX, belts.OutfeedZ) != null) break;
                    // A Long Inserter (doc 04 §2.5, bp_sorting) a tile past the outfeed: it reaches over
                    // the empty outfeed tile into the Intake and puts the mail on a belt two tiles on.
                    var player = server.GetPlayer(world.LocalId)!;
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("iron_ingot"), 4)), "no room for four Iron Ingots");
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("plank"), 2)), "no room for two Planks");
                    player.Move.X = BuildRules.TileCentre(belts.OutfeedX + 1);
                    player.Move.Z = BuildRules.TileCentre(belts.OutfeedZ - 1);
                    var longOne = world.Content.Building("inserter_long");
                    var sorterKey = world.Content.Recipes.First(r => r.Building.Id == "address_sorter_mk1").Key;
                    Check(build.PressKey(sorterKey), "the sorter's key did not take");
                    for (int i = 0; i < 3 && build.Recipe?.Building != longOne; i++) build.Cycle(1);
                    for (int i = 0; i < 4 && build.Rotation != BeltNetwork.OutfeedRotation; i++) build.Rotate();
                    Check(build.Recipe?.Building == longOne && build.Rotation == BeltNetwork.OutfeedRotation, $"the wheel on key {sorterKey + 1} never came to the Long Inserter ({build.Recipe?.Building.Name}, turned {build.Rotation})");
                    _beltStep = 51;
                    _beltFrames = 0;
                    break;
                }

                case 51:
                {
                    var belts = world.Belts;
                    AimAtTile(belts.OutfeedX, belts.OutfeedZ - 1);
                    if (_beltFrames < 8) break;
                    build.Update(1.0 / 30.0, false, false);
                    hud.Refresh();
                    Check(build.Ghost.State == OpResult.Ok && hud.PromptText.StartsWith("Build Long Inserter", StringComparison.Ordinal),
                        $"the Long Inserter's ghost past the outfeed is {build.Ghost.State}, the prompt \"{hud.PromptText}\"");
                    build.Update(1.0 / 30.0, true, false);
                    _beltStep = 52;
                    _beltFrames = 0;
                    return true;
                }

                case 52:
                {
                    var belts = world.Belts;
                    var built = world.Constructs.At(belts.OutfeedX, belts.OutfeedZ - 1);
                    if (built == null || client.PendingRequestCount > 0) break;
                    Check(built.Def.Id == "inserter_long" && built.Facing == BeltNetwork.OutfeedRotation, $"past the outfeed stands a {built.Def.Id} turned {built.Facing}");
                    Check(client.PlaceConstruct(belt, belts.OutfeedX, belts.OutfeedZ - 3, BeltNetwork.OutfeedRotation) != 0, "the belt two past the Long Inserter was not asked for");
                    var dest = world.Map.GetDestination(_beltDest)!;
                    var kind = world.Content.MailKind("letter");
                    for (uint i = 0; i < 2; i++)
                        Check(server.Give(server.Intake, ItemStack.OfMail(new MailItem { Id = 990031 + i, Kind = kind.Index, Address = dest.Address, Value = (ushort)kind.BaseValue, SpawnShift = (byte)server.Run.Shift, DeadlineShift = (byte)(server.Run.Shift + 1) })),
                            "no room in the Intake for a letter");
                    _beltStep = 53;
                    _beltFrames = 0;
                    break;
                }

                case 53:
                {
                    var belts = world.Belts;
                    var fed = server.Belts.SegmentAt(belts.OutfeedX, belts.OutfeedZ - 3);
                    AimAtTile(belts.OutfeedX, belts.OutfeedZ - 2);
                    view.Animate(1f / 30f);
                    if (fed == null || fed.Items.Count == 0 || view.ArmsSwinging == 0 || _beltFrames < 10) break;
                    var mine = world.Belts.SegmentAt(belts.OutfeedX, belts.OutfeedZ - 3);
                    Check(mine != null && mine.Items.Count == fed.Items.Count && world.InserterSwings.ContainsKey((belts.OutfeedZ - 1) * world.Map.Cols + belts.OutfeedX),
                        $"the Long Inserter put {fed.Items.Count} pieces on the belt two on, {mine?.Items.Count} of them here");
                    float reach = view.GrabReach(belts.OutfeedX, belts.OutfeedZ - 1);
                    Check(MathF.Abs(reach - PerformativeMail.Client.Presentation.ConstructsView.ArmLength(2)) < 1e-3f && reach > SimConstants.TileSize, $"the Long Inserter's grab is {reach:0.00} m out");
                    Shot("long-inserter");
                    GD.Print($"[smoke] long inserter: a Long Inserter past the outfeed by the wheel, over the empty outfeed tile out of the Intake, {fed.Items.Count} pieces onto a belt two tiles on, its grab {reach:0.00} m out");
                    client.RemoveConstruct(world.Constructs.At(belts.OutfeedX, belts.OutfeedZ - 3)!);
                    _beltStep = 54;
                    _beltFrames = 0;
                    break;
                }

                case 54:
                {
                    var belts = world.Belts;
                    if (client.PendingRequestCount > 0 || server.Constructs.At(belts.OutfeedX, belts.OutfeedZ - 3) != null) break;
                    var built = world.Constructs.At(belts.OutfeedX, belts.OutfeedZ - 1);
                    if (built != null) client.RemoveConstruct(built);
                    _beltStep = 55;
                    _beltFrames = 0;
                    break;
                }

                case 55:
                {
                    if (client.PendingRequestCount > 0 || server.Constructs.At(world.Belts.OutfeedX, world.Belts.OutfeedZ - 1) != null) break;
                    _beltStep = 60;
                    _beltFrames = 0;
                    break;
                }

                case 60:
                {
                    // Pneumatic pipes (doc 04 §2.2, bp_pipes): the blueprint is sold from shift 3, so
                    // the server grants it here. A Pipe Inlet on the outfeed by the wheel on the pipes'
                    // key, pipes up the yard to a junction and west to an outlet.
                    var player = server.GetPlayer(world.LocalId)!;
                    Check(server.Grant(world.Content.ShopItem("bp_pipes"), player.Id), "the team owned the Pneumatics blueprint already");
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("iron_ingot"), 14)), "no room for fourteen Iron Ingots");
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("glass"), 6)), "no room for six Glass");
                    var belts = world.Belts;
                    player.Move.X = BuildRules.TileCentre(belts.OutfeedX - 1);
                    player.Move.Z = BuildRules.TileCentre(belts.OutfeedZ - 3);
                    _beltStep = 61;
                    _beltFrames = 0;
                    break;
                }

                case 61:
                {
                    var inlet = world.Content.Building("pipe_inlet");
                    if (!world.ShopOwned.Contains(world.Content.ShopItem("bp_pipes")) || _beltFrames < 3) break;
                    var pipeKey = world.Content.Building("pipe").Recipe.Key;
                    Check(build.PressKey(pipeKey), "the pipes' key did not take");
                    for (int i = 0; i < 5 && build.Recipe?.Building != inlet; i++) build.Cycle(1);
                    for (int i = 0; i < 4 && build.Rotation != BeltNetwork.OutfeedRotation; i++) build.Rotate();
                    Check(build.Recipe?.Building == inlet && build.Rotation == BeltNetwork.OutfeedRotation, $"the wheel on key {pipeKey + 1} never came to the Pipe Inlet ({build.Recipe?.Building.Name}, turned {build.Rotation})");
                    _beltStep = 62;
                    _beltFrames = 0;
                    break;
                }

                case 62:
                {
                    var belts = world.Belts;
                    AimAtTile(belts.OutfeedX, belts.OutfeedZ);
                    if (_beltFrames < 8) break;
                    build.Update(1.0 / 30.0, false, false);
                    hud.Refresh();
                    Check(build.Ghost.State == OpResult.Ok && hud.PromptText.StartsWith("Build Pipe Inlet", StringComparison.Ordinal),
                        $"the Pipe Inlet's ghost on the outfeed is {build.Ghost.State}, the prompt \"{hud.PromptText}\"");
                    build.Update(1.0 / 30.0, true, false);
                    _beltStep = 63;
                    _beltFrames = 0;
                    return true;
                }

                case 63:
                {
                    var belts = world.Belts;
                    int ox = belts.OutfeedX, oz = belts.OutfeedZ;
                    var built = world.Constructs.At(ox, oz);
                    if (built == null || client.PendingRequestCount > 0) break;
                    Check(built.Def.Id == "pipe_inlet" && built.Facing == BeltNetwork.OutfeedRotation, $"on the outfeed stands a {built.Def.Id} turned {built.Facing}");
                    Sim.Content.RecipeDef Of(string id) => world.Content.Building(id).Recipe;
                    for (int z = oz - 1; z >= oz - 3; z--) Check(client.PlaceConstruct(Of("pipe"), ox, z, BeltNetwork.OutfeedRotation) != 0, $"the pipe on ({ox}, {z}) was not asked for");
                    Check(client.PlaceConstruct(Of("pipe_junction"), ox, oz - 4, 0) != 0, "the junction was not asked for");
                    Check(client.PlaceConstruct(Of("pipe"), ox - 1, oz - 4, 2) != 0, "the pipe west was not asked for");
                    Check(client.PlaceConstruct(Of("pipe_outlet"), ox - 2, oz - 4, 2) != 0, "the outlet was not asked for");
                    var dest = world.Map.GetDestination(_beltDest)!;
                    var kind = world.Content.MailKind("letter");
                    for (uint i = 0; i < 2; i++)
                        Check(server.Give(server.Intake, ItemStack.OfMail(new MailItem { Id = 990041 + i, Kind = kind.Index, Address = dest.Address, Value = (ushort)kind.BaseValue, SpawnShift = (byte)server.Run.Shift, DeadlineShift = (byte)(server.Run.Shift + 1) })),
                            "no room in the Intake for a letter");
                    _beltStep = 64;
                    _beltFrames = 0;
                    break;
                }

                case 64:
                {
                    var belts = world.Belts;
                    int ox = belts.OutfeedX, oz = belts.OutfeedZ;
                    // Back on the open row north of the Post Office, Build mode down, looking down the pipes into the yard.
                    if (_beltFrames == 1)
                    {
                        build.Leave();
                        var player = server.GetPlayer(world.LocalId)!;
                        player.Move.X = BuildRules.TileCentre(ox + 3);
                        player.Move.Z = BuildRules.TileCentre(oz - 5);
                    }
                    AimAtTile(ox - 1, oz - 3);
                    _screen.PipeCapsules.Refresh();
                    if (client.PendingRequestCount > 0 || world.Pipes.Capsules.Count == 0 || _screen.PipeCapsules.Drawn == 0 || _beltFrames < 6) break;
                    Check(server.Pipes.Capsules.Count >= world.Pipes.Capsules.Count - 1 && world.PipeMismatches == 0,
                        $"{world.Pipes.Capsules.Count} capsules here, {server.Pipes.Capsules.Count} on the server, {world.PipeMismatches} without a way");
                    foreach (var cap in world.Pipes.Capsules)
                        Check(cap.Path[0] == oz * world.Map.Cols + ox && cap.Outlet == (oz - 4) * world.Map.Cols + ox - 2, $"a capsule from tile {cap.Path[0]} to {cap.Outlet}");
                    Shot("pipes");
                    GD.Print($"[smoke] pipes: a Pipe Inlet on the outfeed by the wheel on key {world.Content.Building("pipe").Recipe.Key + 1}, {world.Pipes.Capsules.Count} capsules drawn on their way to the outlet");
                    _beltStep = 65;
                    _beltFrames = 0;
                    break;
                }

                case 65:
                {
                    var belts = world.Belts;
                    int ox = belts.OutfeedX, oz = belts.OutfeedZ;
                    var outlet = server.Pipes.OutletAt((oz - 4) * world.Map.Cols + ox - 2);
                    if (outlet == null || outlet.Queue.Count < 2 || world.Pipes.Capsules.Count < 2) break;
                    // Both wait in the outlet (nothing stands in front of it), drawn at its end.
                    // Back in reach of every piece, then take them down.
                    if (++_pipeHold == 1)
                    {
                        var player = server.GetPlayer(world.LocalId)!;
                        player.Move.X = BuildRules.TileCentre(ox - 1);
                        player.Move.Z = BuildRules.TileCentre(oz - 3);
                    }
                    if (_pipeHold < 4) break;
                    GD.Print($"[smoke] pipes: both letters at the outlet, {outlet.Queue.Count} in its queue; the pipes taken down");
                    foreach (var (x, z) in new[] { (ox, oz), (ox, oz - 1), (ox, oz - 2), (ox, oz - 3), (ox, oz - 4), (ox - 1, oz - 4), (ox - 2, oz - 4) })
                        if (world.Constructs.At(x, z) is { } piece) client.RemoveConstruct(piece);
                    _beltStep = 66;
                    _beltFrames = 0;
                    break;
                }

                case 66:
                {
                    var belts = world.Belts;
                    if (client.PendingRequestCount > 0 || server.Pipes.Inlets.GetEnumerator().MoveNext() || server.Pipes.ItemCount > 0) break;
                    Check(world.Pipes.Capsules.Count == 0 && server.Intake.Entries.Any(e => e.Stack.Kind == StackKind.Mail && e.Stack.Mail.Any(m => m.Id == 990041)),
                        $"{world.Pipes.Capsules.Count} capsules left here; the letters are not back in the Intake");
                    _beltStep = 70;
                    _beltFrames = 0;
                    break;
                }

                case 70:
                {
                    // The Mail Truck (doc 04 §3.1): the server grants the Motor Pool and parks one with
                    // its rear (the loading face) at the end of two belts out of the outfeed.
                    var belts = world.Belts;
                    int ox = belts.OutfeedX, oz = belts.OutfeedZ;
                    if (client.PendingRequestCount > 0 || _beltFrames < 2) break;
                    var player = server.GetPlayer(world.LocalId)!;
                    Check(server.Grant(world.Content.ShopItem("bp_truck"), player.Id), "the team owned the Motor Pool blueprint already");
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("iron_ingot"), 2)), "no room for two Iron Ingots");
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("plank"), 2)), "no room for two Planks");
                    Check(client.PlaceConstruct(belt, ox, oz, BeltNetwork.OutfeedRotation) != 0 && client.PlaceConstruct(belt, ox, oz - 1, BeltNetwork.OutfeedRotation) != 0,
                        "the belts out of the outfeed were not asked for");
                    var truckDef = world.Content.Vehicle("mail_truck");
                    _pipeHold = 0;
                    var truck = server.AddVehicleAt(truckDef, BuildRules.TileCentre(ox), BuildRules.TileCentre(oz - 2) - truckDef.Length * 0.5f, 0f, player.Id);
                    Check(server.LoadingFaceOn(ox, oz - 2) == truck, "the truck's rear is not on the tile the belts end into");
                    _beltStep = 71;
                    _beltFrames = 0;
                    break;
                }

                case 71:
                {
                    var truck = server.Vehicles.FirstOrDefault(v => v.Def.Id == "mail_truck");
                    var here = world.Vehicles.Values.FirstOrDefault(v => v.Def.Id == "mail_truck");
                    if (truck == null || here == null || client.PendingRequestCount > 0 || !truck.Cargo.Entries.Any()) break;
                    // From behind it, looking at it: "Drive the Mail Truck", with its fuel.
                    if (++_pipeHold < 20)
                    {
                        var player = server.GetPlayer(world.LocalId)!;
                        // Behind it and to its right: the roll-up door at its rear, the belts running into it.
                        player.Move.X = here!.X + 1.0f;
                        player.Move.Z = here.Z + 3.3f;
                        break;
                    }
                    var box = AimBox.Vehicle(world.Map, here!.Def, here.X, here.Z, here.Heading);
                    ViewRay.Aim(world.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                    _session.Interaction.Update(1.0 / 30.0, false, false);
                    hud.Refresh();
                    if (_session.Interaction.Prompt.Kind != InteractKind.Mount || _beltFrames < 30) break;
                    Check(hud.PromptText == $"Drive the Mail Truck  [E]   ·   fuel 05:00 of 10:00", $"beside the truck the prompt reads \"{hud.PromptText}\"");
                    Check(_screen.Vehicles.Shown >= 1 && world.BeltMismatches == 0, $"{_screen.Vehicles.Shown} vehicles drawn, {world.BeltMismatches} checksums off");
                    _beltStep = 75;
                    _beltFrames = 0;
                    break;
                }

                case 75:
                {
                    // For the picture: from the open ground east of the yard, the truck and the belts into its rear.
                    var player = server.GetPlayer(world.LocalId)!;
                    var here = world.Vehicles.Values.First(v => v.Def.Id == "mail_truck");
                    var truck = server.Vehicles.First(v => v.Def.Id == "mail_truck");
                    if (_beltFrames == 1)
                    {
                        player.Move.X = BuildRules.TileCentre(10);
                        player.Move.Z = BuildRules.TileCentre(7) + 0.5f;
                    }
                    ViewRay.Aim(world.Local, here.X, 1.2f, here.Z + 0.8f, out client.Input.YawRadians, out client.Input.PitchRadians);
                    if (_beltFrames < 20) break;
                    Shot("truck");
                    int loaded = truck!.Cargo.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Mail.Count);
                    GD.Print($"[smoke] truck: a Mail Truck parked at the end of two belts out of the outfeed, {loaded} pieces loaded at its rear; \"Drive the Mail Truck\" with its fuel beside it");
                    player.Move.X = here.X + 1.0f;
                    player.Move.Z = here.Z + 3.3f;
                    _beltStep = 76;
                    _beltFrames = 0;
                    break;
                }

                case 76:
                {
                    var here = world.Vehicles.Values.First(v => v.Def.Id == "mail_truck");
                    float dx = world.Local.X - here.X - 1.0f, dz = world.Local.Z - here.Z - 3.3f;
                    if (dx * dx + dz * dz > 0.1f || _beltFrames < 4) break;
                    Check(client.Mount(here.Id) != 0, "the truck's mount was not asked for");
                    _beltStep = 72;
                    _beltFrames = 0;
                    break;
                }

                case 72:
                {
                    var player = server.GetPlayer(world.LocalId)!;
                    if (!world.Local.Riding || !player.Move.Riding || client.PendingRequestCount > 0) break;
                    Check(world.LocalRide?.Def.Id == "mail_truck", "the host rides something else than the truck");
                    // Playtest 346eb3e: the eyes are in the cab, ahead of the cargo box, not inside it.
                    _screen.Camera.Sync();
                    var cab = _screen.Camera.GlobalPosition - _screen.Players.LocalView!.GlobalPosition;
                    float ahead = cab.X * world.Local.HeadX + cab.Z * world.Local.HeadZ;
                    Check(Mathf.Abs(ahead - FirstPersonCamera.CabEyeForward) < 0.05f && Mathf.Abs(cab.Y - FirstPersonCamera.CabEyeHeight) < 0.05f,
                        $"at the truck's wheel the eyes are {ahead:0.00} m ahead and {cab.Y:0.00} m up, not in the cab");
                    // Off again, and the truck, the belts and what it held go.
                    var truck = server.Vehicles.First(v => v.Def.Id == "mail_truck");
                    Check(server.RemoveVehicle(truck), "the truck was not taken away");
                    _beltStep = 73;
                    _beltFrames = 0;
                    break;
                }

                case 73:
                {
                    // On foot again (the server's word reached here): the belts go.
                    if (world.Vehicles.Values.Any(v => v.Def.Id == "mail_truck") || world.Local.Riding || _beltFrames < 4) break;
                    var belts = world.Belts;
                    foreach (var (x, z) in new[] { (belts.OutfeedX, belts.OutfeedZ), (belts.OutfeedX, belts.OutfeedZ - 1) })
                        if (world.Constructs.At(x, z) is { } piece) client.RemoveConstruct(piece);
                    _beltStep = 74;
                    _beltFrames = 0;
                    break;
                }

                case 74:
                {
                    var belts = world.Belts;
                    if (client.PendingRequestCount > 0 || world.Local.Riding || server.Constructs.At(belts.OutfeedX, belts.OutfeedZ) != null
                        || server.Constructs.At(belts.OutfeedX, belts.OutfeedZ - 1) != null) break;
                    if (!build.Active) Check(build.Toggle(), $"B did not put Build mode up again ({build.Refused})");
                    _beltStep = 27;
                    _beltFrames = 0;
                    break;
                }

                case 27:
                {
                    if (client.PendingRequestCount > 0 || server.Constructs.At(world.Belts.OutfeedX, world.Belts.OutfeedZ) != null) break;
                    // A Depot (doc 04 §2.6) by the wheel on the chest's key, on four open tiles.
                    var player = server.GetPlayer(world.LocalId)!;
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("stone"), 8)), "no room for eight Stones");
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("iron_ingot"), 4)), "no room for four Iron Ingots");
                    var depot = world.Content.Building("depot");
                    var map = world.Map;
                    int hx = (int)MathF.Floor(world.Local.X / SimConstants.TileSize), hz = (int)MathF.Floor(world.Local.Z / SimConstants.TileSize);
                    _splitX = -1;
                    for (int r = 2; r <= 10 && _splitX < 0; r++)
                        for (int dz = -r; dz <= r && _splitX < 0; dz++)
                            for (int dx = -r; dx <= r && _splitX < 0; dx++)
                            {
                                int x = hx + dx, z = hz + dz;
                                if ((Math.Abs(dx) != r && Math.Abs(dz) != r) || BuildRules.CheckPlacement(map, world.Constructs, depot, x, z, 0) != OpResult.Ok) continue;
                                if (BuildRules.CheckTile(map, world.Constructs, x - 1, z) != OpResult.Ok || !FreeGround(map, BuildRules.TileCentre(x - 1), BuildRules.TileCentre(z), -1)) continue;
                                (_splitX, _splitZ) = (x, z);
                            }
                    if (_splitX < 0)
                    {
                        Fail("no four open tiles near the Post Office for the Depot");
                        return false;
                    }
                    player.Move.X = BuildRules.TileCentre(_splitX - 1) - 0.4f;
                    player.Move.Z = BuildRules.TileCentre(_splitZ);
                    var chestKey = world.Content.Recipes.First(r => r.Building.Id == "chest").Key;
                    Check(build.PressKey(chestKey), "the chest's key did not take");
                    for (int i = 0; i < 2 && build.Recipe?.Building != depot; i++) build.Cycle(1);
                    for (int i = 0; i < 4 && build.Rotation != 0; i++) build.Rotate();
                    Check(build.Recipe?.Building == depot, $"the wheel on key {chestKey + 1} never came to the Depot ({build.Recipe?.Building.Name})");
                    _beltStep = 28;
                    _beltFrames = 0;
                    break;
                }

                case 28:
                {
                    AimAtTile(_splitX, _splitZ);
                    var built = world.Constructs.At(_splitX, _splitZ);
                    if (built == null)
                    {
                        if (_beltFrames == 8)
                        {
                            build.Update(1.0 / 30.0, false, false);
                            hud.Refresh();
                            Check(build.Ghost.State == OpResult.Ok && hud.PromptText.StartsWith("Build Depot", StringComparison.Ordinal),
                                $"the Depot's ghost at ({_splitX}, {_splitZ}) is {build.Ghost.State}, the prompt \"{hud.PromptText}\"");
                            build.Update(1.0 / 30.0, true, false);
                            return true;
                        }
                        break;
                    }
                    if (client.PendingRequestCount > 0) break;
                    Check(built.Def.Id == "depot" && world.Constructs.At(_splitX + 1, _splitZ + 1) == built && built.ContainerId != 0,
                        $"the Depot at ({_splitX}, {_splitZ}) is a {built.Def.Id} on {(world.Constructs.At(_splitX + 1, _splitZ + 1) == built ? "four" : "fewer")} tiles");
                    // Out of Build mode, E opens it like a chest.
                    Check(build.Toggle() == false, "B did not put Build mode down at the Depot");
                    var box = AimBox.Built(world.Map, built);
                    ViewRay.Aim(world.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                    _beltStep = 29;
                    _beltFrames = 0;
                    break;
                }

                case 29:
                {
                    var built = world.Constructs.At(_splitX, _splitZ)!;
                    var aim = AimBox.Built(world.Map, built);
                    ViewRay.Aim(world.Local, aim.X, aim.Y, aim.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                    // The delivery bot shuts whatever is open outside Delivery: it stands aside while the Depot is.
                    if (!_botAside)
                    {
                        client.TickStarting -= _botTick;
                        _botAside = true;
                    }
                    // A tap of E while it says "Open Depot", again if the first was early.
                    if (_beltFrames % 20 == 4 && world.OpenContainerId == 0 && _session.Interaction.Prompt.Kind == InteractKind.OpenChest)
                    {
                        _session.Interaction.Update(1.0 / 30.0, false, false, interactTapped: true);
                        break;
                    }
                    if (world.OpenContainerId != built.ContainerId || !_screen.Inventory.IsVisibleInTree() || _beltFrames < 8) { _depotShown = 0; break; }
                    // The panel lays itself out over a frame or two after it comes up.
                    if (++_depotShown < 4) break;
                    var grid = world.OpenContainer;
                    Check(grid != null && grid.Def.Id == "depot" && _screen.Inventory.ExternalTitle == "Depot" && _screen.Inventory.ExternalGrid.IsVisibleInTree(),
                        $"E at the Depot opened {grid?.Def.Id}, titled \"{_screen.Inventory.ExternalTitle}\"");
                    var seen = GetViewport().GetVisibleRect();
                    Check(seen.Encloses(_screen.Inventory.ExternalGrid.GetGlobalRect()), $"the Depot's grid at {_screen.Inventory.ExternalGrid.GetGlobalRect()} leaves the view");
                    Shot("depot-open");
                    client.CloseContainer();
                    _beltStep = 30;
                    _beltFrames = 0;
                    break;
                }

                case 30:
                {
                    var built = world.Constructs.At(_splitX, _splitZ)!;
                    if (client.PendingRequestCount > 0 || world.OpenContainerId != 0) break;
                    // Shut on both ends: the bot comes back.
                    if (_botAside)
                    {
                        client.TickStarting += _botTick;
                        _botAside = false;
                    }
                    var player = server.GetPlayer(world.LocalId)!;
                    player.Move.X = BuildRules.TileCentre(_splitX - 1) - 4.5f;
                    if (_beltFrames < 10) break;
                    var box = AimBox.Built(world.Map, built);
                    ViewRay.Aim(world.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                    if (_beltFrames < 16) break;
                    view.Refresh();
                    Shot("depot");
                    GD.Print($"[smoke] depot: a Depot built at ({_splitX}, {_splitZ}) on four tiles by the wheel on the chest's key, opened by E, its 16 x 10 grid on screen");
                    player.Move.X = BuildRules.TileCentre(_splitX - 1) - 0.4f;
                    _beltStep = 31;
                    _beltFrames = 0;
                    break;
                }

                case 31:
                {
                    if (_beltFrames < 8) break;
                    var built = world.Constructs.At(_splitX, _splitZ);
                    if (built != null && client.PendingRequestCount == 0 && _beltFrames == 8) client.RemoveConstruct(built);
                    if (client.PendingRequestCount > 0 || server.Constructs.At(_splitX, _splitZ) != null) break;
                    _beltStep = 33;
                    _beltFrames = 0;
                    break;
                }

                case 33:
                {
                    // The Vehicle Depot (doc 04 §3.2, bp_truck, granted with the truck): the wheel on the
                    // chest's key, on nine open tiles.
                    if (_beltFrames < 2) break;
                    var player = server.GetPlayer(world.LocalId)!;
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("plank"), 12)), "no room for twelve Planks");
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("stone"), 8)), "no room for eight Stones");
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("iron_ingot"), 4)), "no room for four Iron Ingots");
                    var vd = world.Content.Building("vehicle_depot");
                    var map = world.Map;
                    int hx = (int)MathF.Floor(world.Local.X / SimConstants.TileSize), hz = (int)MathF.Floor(world.Local.Z / SimConstants.TileSize);
                    _splitX = -1;
                    for (int r = 2; r <= 12 && _splitX < 0; r++)
                        for (int dz = -r; dz <= r && _splitX < 0; dz++)
                            for (int dx = -r; dx <= r && _splitX < 0; dx++)
                            {
                                int x = hx + dx, z = hz + dz;
                                if ((Math.Abs(dx) != r && Math.Abs(dz) != r) || BuildRules.CheckPlacement(map, world.Constructs, vd, x, z, 0) != OpResult.Ok) continue;
                                if (BuildRules.CheckTile(map, world.Constructs, x - 1, z) != OpResult.Ok || !FreeGround(map, BuildRules.TileCentre(x - 1), BuildRules.TileCentre(z), -1)) continue;
                                (_splitX, _splitZ) = (x, z);
                            }
                    if (_splitX < 0)
                    {
                        Fail("no nine open tiles near the Post Office for the Vehicle Depot");
                        return false;
                    }
                    player.Move.X = BuildRules.TileCentre(_splitX - 1) - 0.4f;
                    player.Move.Z = BuildRules.TileCentre(_splitZ);
                    if (!build.Active) Check(build.Toggle(), $"B did not put Build mode up for the Vehicle Depot ({build.Refused})");
                    var chestKey = world.Content.Recipes.First(r => r.Building.Id == "chest").Key;
                    Check(build.PressKey(chestKey), "the chest's key did not take");
                    for (int i = 0; i < 3 && build.Recipe?.Building != vd; i++) build.Cycle(1);
                    for (int i = 0; i < 4 && build.Rotation != 0; i++) build.Rotate();
                    Check(build.Recipe?.Building == vd, $"the wheel on key {chestKey + 1} never came to the Vehicle Depot ({build.Recipe?.Building.Name})");
                    _beltStep = 34;
                    _beltFrames = 0;
                    break;
                }

                case 34:
                {
                    AimAtTile(_splitX, _splitZ);
                    var built = world.Constructs.At(_splitX, _splitZ);
                    if (built == null)
                    {
                        if (_beltFrames == 8)
                        {
                            build.Update(1.0 / 30.0, false, false);
                            hud.Refresh();
                            Check(build.Ghost.State == OpResult.Ok && hud.PromptText.StartsWith("Build Vehicle Depot", StringComparison.Ordinal),
                                $"the Vehicle Depot's ghost at ({_splitX}, {_splitZ}) is {build.Ghost.State}, the prompt \"{hud.PromptText}\"");
                            Shot("vehicle-depot-ghost");
                            build.Update(1.0 / 30.0, true, false);
                            return true;
                        }
                        break;
                    }
                    if (client.PendingRequestCount > 0) break;
                    Check(built.Def.Id == "vehicle_depot" && world.Constructs.At(_splitX + 2, _splitZ + 2) == built,
                        $"the Vehicle Depot at ({_splitX}, {_splitZ}) is a {built.Def.Id}, not on nine tiles");
                    Check(build.Toggle() == false, "B did not put Build mode down at the Vehicle Depot");
                    // A truck in its bay, two letters in it for the first house.
                    var truth = server.Constructs.At(_splitX, _splitZ)!;
                    SimWorld.Bay(truth, out float bx, out float bz, out float heading);
                    var truck = server.AddVehicleAt(world.Content.Vehicle("mail_truck"), bx, bz, heading, server.GetPlayer(world.LocalId)!.Id);
                    var first = world.Map.Destinations[0];
                    for (uint i = 0; i < 2; i++)
                        Check(server.Give(truck.Cargo, ItemStack.OfMail(new MailItem { Id = 990301 + i, Kind = 1, Address = first.Address, Value = 8, SpawnShift = 1, DeadlineShift = 3 })), "no room in the truck");
                    Check(server.InBay(truth) == truck, "the truck put into the bay is not in it");
                    _beltStep = 35;
                    _beltFrames = 0;
                    break;
                }

                case 35:
                {
                    var built = world.Constructs.At(_splitX, _splitZ)!;
                    var box = AimBox.Built(world.Map, built);
                    var player = server.GetPlayer(world.LocalId)!;
                    // Before the console, facing it from outside the depot.
                    SimWorld.Console(built, out float kx, out float kz);
                    if (_beltFrames == 1) { player.Move.X = kx; player.Move.Z = kz - 2.2f; }
                    ViewRay.Aim(world.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                    if (_beltFrames < 10) break;
                    if (!_botAside)
                    {
                        client.TickStarting -= _botTick;
                        _botAside = true;
                    }
                    _session.Interaction.Update(1.0 / 30.0, false, false);
                    hud.Refresh();
                    if (world.FilterTarget == null)
                    {
                        if (_beltFrames == 12)
                            Check(_session.Interaction.Prompt.Kind == InteractKind.Configure && hud.PromptText == "Open the route console of the Vehicle Depot  [E]",
                                $"at the console the prompt reads \"{hud.PromptText}\" ({_session.Interaction.Prompt.Kind})");
                        if (_beltFrames % 10 == 2) _session.Interaction.Update(1.0 / 30.0, false, false, interactTapped: true);
                        break;
                    }
                    var panel = _screen.Inventory.Filters;
                    if (world.FilterStatus == null || !panel.IsVisibleInTree() || _beltFrames < 20) break;
                    Check(panel.Text == HudText.FilterText(world) && panel.CardCount == 7,
                        $"the route console reads \"{panel.Text}\" ({panel.CardCount} cards), not \"{HudText.FilterText(world)}\"");
                    Check(panel.Text.StartsWith("VEHICLE DEPOT   ·   route console\nNo stops yet", StringComparison.Ordinal), $"the console begins \"{panel.Text}\"");
                    Check(client.PressFilterKey(0), "key 1 was not the console's");
                    _beltStep = 36;
                    _beltFrames = 0;
                    break;
                }

                case 36:
                {
                    var built = world.FilterTarget;
                    var truth = server.Constructs.At(_splitX, _splitZ);
                    if (built == null || truth == null || truth.Filter(0, 0).IsEmpty || client.PendingRequestCount > 0 || _beltFrames < 3) break;
                    var panel = _screen.Inventory.Filters;
                    if (world.FilterStatus == null || world.FilterStatus.Matches.Count == 0 || world.FilterStatus.Matches[0] == 0) break;
                    var first = world.Map.Destinations[0];
                    Check(truth.Filter(0, 0) == new MailFilter(FilterKind.Address, first.Id) && built.Filter(0, 0) == truth.Filter(0, 0),
                        $"key 1 set stop 1 to {truth.Filter(0, 0).Kind} {truth.Filter(0, 0).Value}, not the first house");
                    Check(panel.Text == HudText.FilterText(world) && panel.Text.Contains($"[1]  Stop 1   ·   {first.Label}\n2 for it in the truck", StringComparison.Ordinal)
                        && panel.Text.Contains("1 house   ·   ", StringComparison.Ordinal),
                        $"the console after key 1 reads \"{panel.Text}\"");
                    var seen = GetViewport().GetVisibleRect();
                    for (int c = 0; c < panel.CardCount; c++)
                        Check(seen.Encloses(panel.CardAt(c).GetGlobalRect()), $"the console's card {c + 1} at {panel.CardAt(c).GetGlobalRect()} leaves the view");
                    Shot("vehicle-depot-console");
                    GD.Print($"[smoke] vehicle depot: built at ({_splitX}, {_splitZ}) on nine tiles by the wheel on the chest's key, a truck in its bay, the route console up by E, stop 1 set to {first.Label} by key 1 with the truck's 2 letters for it");
                    // Its driver (doc 04 §3.2), for hire from shift 2 solo: this is shift 2's Prep, key 7 hires one.
                    Check(panel.Text.Contains("[7]  Driver   ·   hire for this shift, 150 g\ndrives the route at 60 % of your speed", StringComparison.Ordinal), $"the console's driver card reads \"{panel.Text}\"");
                    Check(client.PressFilterKey(6), "key 7 was not the console's");
                    _beltStep = 38;
                    _beltFrames = 0;
                    break;
                }

                case 38:
                {
                    if (client.PendingRequestCount > 0 || _beltFrames < 3) break;
                    var panel = _screen.Inventory.Filters;
                    if (world.FilterTarget == null || world.DriverAt(world.FilterTarget) == null) break;
                    Check(_session.Feed.Entries.Any(e => e.Text == "You hired a driver for the Vehicle Depot   ·   150 g"), "the feed did not say the driver was hired");
                    Check(server.Drivers.Count(d => d.State == DriverState.Waiting) == 1, "no driver waits at the depot on the server");
                    Check(panel.Text.Contains("[7]  Driver   ·   hired for this shift   ·   0 delivered\nwaiting for a truck with mail for the route, in Delivery", StringComparison.Ordinal),
                        $"after key 7 the console reads \"{panel.Text}\"");
                    GD.Print("[smoke] vehicle depot: key 7 hired its driver for shift 2 (150 g); the depot and the truck stay for Delivery");
                    Check(client.CloseFilters(), "the console was not up to take down");
                    _beltStep = 37;
                    _beltFrames = 0;
                    break;
                }

                case 37:
                {
                    var built = world.Constructs.At(_splitX, _splitZ);
                    if (built == null)
                    {
                        if (client.PendingRequestCount > 0 || server.Constructs.At(_splitX, _splitZ) != null) break;
                        _beltStep = 32;
                        _beltFrames = 0;
                        break;
                    }
                    var player = server.GetPlayer(world.LocalId)!;
                    if (_botAside)
                    {
                        client.TickStarting += _botTick;
                        _botAside = false;
                    }
                    // For the picture: back from its open side.
                    if (_beltFrames == 1) { player.Move.X = built.CentreX - 9f; player.Move.Z = built.CentreZ + 4f; }
                    ViewRay.Aim(world.Local, built.CentreX, 1.4f, built.CentreZ, out client.Input.YawRadians, out client.Input.PitchRadians);
                    if (_beltFrames < 16) break;
                    view.Refresh();
                    Shot("vehicle-depot");
                    // The depot, its truck and its driver stay: in Delivery the driver takes the two letters to the first house.
                    player.Move.X = BuildRules.TileCentre(_splitX - 1) - 0.4f;
                    player.Move.Z = BuildRules.TileCentre(_splitZ);
                    _depotX = _splitX;
                    _depotZ = _splitZ;
                    _beltStep = 32;
                    _beltFrames = 0;
                    break;
                }

                case 32:
                    Check(!build.Active, "Build mode is still up after the belt");
                    _beltVisits++;
                    _beltStep = 0;
                    _session.FixedTicksPerFrame = RunOutTicksPerFrame;
                    return false;
            }
            if (_beltStep >= 3) build.Update(1.0 / 30.0, false, false);
            return true;
        }

        /// <summary>After the run: the belt was laid, and in Delivery the mailbox took its three letters.</summary>
        private void VerifyBelts()
        {
            Check(_beltVisits == 1, $"the smoke laid {_beltVisits} belts");
            Check(_beltDelivered >= 3, $"{_beltDelivered} letters went from a belt into a mailbox");
            Check(_session.Feed.Entries.Any(e => e.Text.StartsWith("A belt delivered", StringComparison.Ordinal)) || _beltFeedSeen,
                "the feed never said \"A belt delivered\"");
            var world = _session.Client.World!;
            Check(world.Belts.ItemCount == 0 && _session.Server!.World.Belts.ItemCount == 0, "mail is left on the belts in the Lobby");
            Check(_driverFollowed, "the driver's truck was never seen on the road here where the server had it");
            Check(_driverDelivered >= 2, $"the depot's driver delivered {_driverDelivered} pieces, not the truck's two letters");
            Check(!world.Drivers.Values.Any(d => d.State != DriverState.None) && !_session.Server!.World.Drivers.Any(), "a driver is left in the Lobby");
        }

        private bool _beltFeedSeen;
        private int _outfeedIntake, _splitX, _splitZ, _rampRot, _depotShown;
        private bool _botAside;
        /// <summary>The Vehicle Depot left standing for Delivery, and what its driver did there.</summary>
        private int _depotX = -1, _depotZ = -1, _driverDelivered;
        private bool _driverFollowed;

        /// <summary>
        /// In the run-out: while the depot's driver has its truck on the road, it is drawn where the
        /// server has it (the snapshots' word) with the driver aboard.
        /// </summary>
        private void TrackDriver()
        {
            if (_depotX < 0 || _driverFollowed) return;
            var server = _session.Server!.World;
            var world = _session.Client.World!;
            var d = server.Drivers.FirstOrDefault(x => x.State == DriverState.Out && x.Truck != null);
            if (d == null || !world.Vehicles.TryGetValue(d.Truck!.Id, out var here) || !here.NpcDriving) return;
            float dx = here.X - d.Truck.X, dz = here.Z - d.Truck.Z;
            if (dx * dx + dz * dz > 4f) return;
            Check(here.NpcAboard && _screen.Vehicles.TryGetPosition(here.Id, out _), "the driver's truck is not drawn with its driver");
            _driverFollowed = true;
        }

        /// <summary>The lowest mail id in a container: the oldest piece there (uint.MaxValue for none).</summary>
        private static uint OldestIn(Sim.Inventory.GridContainer c)
        {
            uint oldest = uint.MaxValue;
            foreach (var e in c.Entries)
                if (e.Stack.Kind == StackKind.Mail)
                    foreach (var m in e.Stack.Mail) oldest = Math.Min(oldest, m.Id);
            return oldest;
        }

        private static int IntakeMail(Sim.Inventory.GridContainer intake) => intake.Entries.Sum(e => e.Stack.Kind == StackKind.Mail ? e.Stack.Count : 0);
    }
}
