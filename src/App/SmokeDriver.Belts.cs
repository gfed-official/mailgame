using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
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
        private int _beltStep, _beltFrames, _beltVisits, _beltDelivered;
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
                    if (client.PendingRequestCount > 0 || server.Constructs.At(_splitX, _splitZ) != null) break;
                    Check(build.Toggle() == false && !build.Active, "B did not put Build mode down after the belt");
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
        }

        private bool _beltFeedSeen;
        private int _outfeedIntake, _splitX, _splitZ, _rampRot;

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
