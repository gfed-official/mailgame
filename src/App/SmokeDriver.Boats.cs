using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.App
{
    /// <summary>
    /// The sea path's first part in the smoke's run (doc 04 §3.1 and §3.3): in the Prep after the
    /// bike, the server gives the team the Rowboat blueprint (the Sea kit's) and the host the logs
    /// and rope; from the beach the host builds a Pier out over the shallow water and places a
    /// Rowboat beside the quay, both seen on the client and drawn; gets in by its prompt, rows it
    /// out into the basin under their own input as the server has it, where the jump key does
    /// nothing and the hint says why; and by the pier's end gets out onto its deck.
    /// </summary>
    public partial class SmokeDriver
    {
        private int _boatStep, _boatFrames, _boatVisits;
        private float _boatTopSpeed;
        private Action? _boatTick;
        /// <summary>Where the Small Port went: its back row is the quay's second from the water.</summary>
        private int _portX = -1;
        private const int PortZ = 24;

        private bool StepBoat()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var player = server.GetPlayer(world.LocalId)!;
            var pier = world.Content.Recipes.First(r => r.Building.Id == "pier");
            var placed = world.Content.Recipes.First(r => r.Building.Id == "rowboat_build");
            const int pierX = 3, pierZ = 26, boatX = 5, boatZ = 26, south = 1;
            var port = world.Content.Recipes.First(r => r.Building.Id == "small_port");

            if (_boatStep == 0)
            {
                if (_boatVisits > 0 || _bikeVisits == 0 || world.Local.Dead || client.PendingRequestCount > 0) return false;
                _session.FixedTicksPerFrame = 1;
                client.TickStarting -= _botTick;
                server.Grant(world.Content.ShopItem("bp_rowboat"), player.Id);
                Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("log"), 10))
                    && server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("log"), 4))
                    && server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("rope"), 2)), "no room for the pier's and the boat's materials");
                // On the beach, at the quay, looking out over the water.
                player.Move.X = BuildRules.TileCentre(pierX);
                player.Move.Z = 50.8f;
                _boatStep = 1;
                _boatFrames = 0;
                return true;
            }
            if (++_boatFrames > BuildStepFrames * 2)
            {
                Fail($"the boat visit stopped at step {_boatStep}: riding {world.Local.Riding}, prompt {_session.Interaction.Prompt.Kind} \"{hud.PromptText}\", host at {world.Local.X:0.00}, {world.Local.Z:0.00}");
                return false;
            }
            bool next = false, tap = false;
            switch (_boatStep)
            {
                case 1:
                {
                    if (MathF.Abs(world.Local.Z - player.Move.Z) > 0.1f || _boatFrames < 6 || !world.ShopOwned.Contains(world.Content.ShopItem("bp_rowboat"))) break;
                    Check(_session.Feed.Entries.Any(e => e.Text.Contains("Blueprint: Rowboat", StringComparison.Ordinal)), "the feed did not say the team was given the Rowboat blueprint");
                    // The local rules agree with the server's before anything is sent.
                    Check(BuildRules.CheckPlacement(world.Map, world.Constructs, pier.Building, pierX, pierZ, 0) == OpResult.NotFromShore, "a pier turned along the shore was not refused here");
                    Check(BuildRules.CheckPlacement(world.Map, world.Constructs, pier.Building, pierX, pierZ - 3, south) == OpResult.NotOnWater, "a pier on the beach was not refused here");
                    Check(client.PlaceConstruct(pier, pierX, pierZ, south) != 0, "the pier was not sent");
                    next = true;
                    break;
                }
                case 2:
                {
                    if (client.PendingRequestCount > 0 || world.Constructs.At(pierX, pierZ + 2) == null) break;
                    Check(Enumerable.Range(0, 3).All(k => server.Constructs.At(pierX, pierZ + k)?.Def.Id == "pier"), "the pier does not stand on the server's three tiles of water");
                    Check(client.PlaceConstruct(placed, boatX, boatZ, south) != 0, "the rowboat was not sent");
                    next = true;
                    break;
                }
                case 3:
                {
                    var boat = world.Vehicles.Values.FirstOrDefault(v => v.Def.OnWater);
                    if (client.PendingRequestCount > 0 || boat == null || _boatFrames < 4) break;
                    Check(server.Vehicles.Count(v => v.Def.OnWater) == 1 && server.Constructs.At(boatX, boatZ) == null, "the placed rowboat is not one boat on the server, and no construct");
                    Check(_screen.Vehicles.TryGetPosition(boat.Id, out var drawn) && drawn.Y < -0.3f, $"the rowboat is not drawn on the water ({drawn})");
                    // Along the quay to beside it.
                    player.Move.X = boat.X;
                    player.Move.Z = 51.3f;
                    next = true;
                    break;
                }
                case 4:
                {
                    var boat = world.Vehicles.Values.First(v => v.Def.OnWater);
                    if (MathF.Abs(world.Local.X - player.Move.X) > 0.1f || _boatFrames < 4) break;
                    var box = AimBox.Vehicle(world.Map, boat.Def, boat.X, boat.Z, boat.Heading);
                    ViewRay.Aim(world.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                    _session.Interaction.Update(1.0 / 30.0, false, false);
                    hud.Refresh();
                    if (_session.Interaction.Prompt.Kind != InteractKind.Mount || _boatFrames < 8) break;
                    Check(hud.PromptText == "Get into the Rowboat  [E]", $"beside the rowboat the prompt reads \"{hud.PromptText}\"");
                    Shot("rowboat-prompt");
                    tap = true;
                    next = true;
                    break;
                }
                case 5:
                {
                    if (!world.Local.Riding || !player.Move.Riding || client.PendingRequestCount > 0) break;
                    Check(_session.Feed.Entries.Any(e => e.Text.StartsWith("You are in the Rowboat", StringComparison.Ordinal)), "the feed does not say the host is in the rowboat");
                    // Out into the basin, south, by the rower's own input.
                    _boatTick = () =>
                    {
                        client.Input.YawRadians = MathF.PI;
                        client.Input.PitchRadians = -0.2f;
                        client.Input.MoveX = 0f;
                        client.Input.MoveZ = 1f;
                        client.Input.Buttons = InputButtons.None;
                    };
                    client.TickStarting += _boatTick;
                    next = true;
                    break;
                }
                case 6:
                {
                    _boatTopSpeed = MathF.Max(_boatTopSpeed, player.Move.RideSpeed);
                    if (_boatFrames == 40) Shot("rowboat-row");
                    if (world.Local.Z < 60f) break;
                    Check(MathF.Abs(world.Local.Z - player.Move.Z) < 0.3f, $"rowing, the prediction is at z {world.Local.Z:0.00} and the server at {player.Move.Z:0.00}");
                    Check(_boatTopSpeed > 2.9f && _boatTopSpeed < 3.05f, $"the rowboat ran {_boatTopSpeed:0.00} m/s");
                    // Rest on the oars, then the jump key out in the basin.
                    client.TickStarting -= _boatTick;
                    int frame = 0;
                    _boatTick = () =>
                    {
                        client.Input.MoveX = 0f;
                        client.Input.MoveZ = 0f;
                        client.Input.Buttons = ++frame == 60 ? InputButtons.Jump : InputButtons.None;
                    };
                    client.TickStarting += _boatTick;
                    next = true;
                    break;
                }
                case 7:
                {
                    if (_boatFrames < 75) break;
                    var boat = world.LocalRide!;
                    Check(world.Local.Riding && player.Move.Riding, "out on the basin the jump key got the host out of the boat");
                    Check(HudText.GetOff(world, boat) == "row to a pier or the shore to get out", $"out on the basin the hint reads \"{HudText.GetOff(world, boat)}\"");
                    // Beside the pier's end (the server puts the boat there), and out onto its deck.
                    client.TickStarting -= _boatTick;
                    _boatTick = null;
                    player.Move.X = BuildRules.TileCentre(pierX + 1) + 0.3f;
                    player.Move.Z = BuildRules.TileCentre(pierZ + 2);
                    next = true;
                    break;
                }
                case 8:
                {
                    var boat = world.LocalRide!;
                    if (MathF.Abs(world.Local.X - player.Move.X) > 0.1f || _boatFrames < 6) break;
                    Check(HudText.GetOff(world, boat) == $"{HudText.GetOffKey} get out", $"by the pier the hint reads \"{HudText.GetOff(world, boat)}\"");
                    bool jumped = false;
                    _boatTick = () =>
                    {
                        client.Input.Buttons = jumped ? InputButtons.None : InputButtons.Jump;
                        jumped = true;
                    };
                    client.TickStarting += _boatTick;
                    next = true;
                    break;
                }
                case 9:
                {
                    var boat = world.Vehicles.Values.First(v => v.Def.OnWater);
                    if (world.Local.Riding || player.Move.Riding || boat.Ridden || client.PendingRequestCount > 0 || _boatFrames < 4) break;
                    client.TickStarting -= _boatTick;
                    _boatTick = null;
                    client.Input.Buttons = InputButtons.None;
                    int tx = (int)MathF.Floor(player.Move.X / 2f), tz = (int)MathF.Floor(player.Move.Z / 2f);
                    Check(server.Constructs.At(tx, tz)?.Def.Id == "pier", $"out of the boat the host stands at {player.Move.X:0.00}, {player.Move.Z:0.00}, not on the pier");
                    Check(MathF.Abs(world.Local.X - player.Move.X) < 0.1f && MathF.Abs(world.Local.Z - player.Move.Z) < 0.1f, "the client does not stand where the server put the host");
                    Check(world.Map.IsWater((int)MathF.Floor(boat.X / 2f), (int)MathF.Floor(boat.Z / 2f)), "the boat was left off the water");
                    // Turned back to the boat on the water, for the picture.
                    ViewRay.Aim(world.Local, boat.X, -0.3f, boat.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                    next = true;
                    break;
                }
                case 10:
                {
                    if (_boatFrames < 8) break;
                    Shot("pier");
                    GD.Print($"[smoke] boats: a Pier built out from the beach and a Rowboat placed with the Sea kit's blueprint, got into by its prompt, rowed at {_boatTopSpeed:0.0} m/s into the basin (no getting out there) and left from the pier's end");
                    // The Small Port (doc 04 §3.3): the Harbour blueprint and its materials, a spot on the deep quay.
                    server.Grant(world.Content.ShopItem("bp_motorboat"), player.Id);
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("plank"), 16))
                        && server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("stone"), 10))
                        && server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("iron_ingot"), 6))
                        && server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("plank"), 2))
                        && server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("iron_ingot"), 1)), "no room for the port's materials");
                    _portX = -1;
                    for (int x = world.Map.Cols - 1; x >= 2 && _portX < 0; x--)
                        if (BuildRules.CheckPlacement(server.Map, server.Constructs, port.Building, x, PortZ, south) == OpResult.Ok) _portX = x;
                    Check(_portX >= 0, "no room for a Small Port along the deep quay");
                    if (_portX < 0) return false;
                    player.Move.X = BuildRules.TileCentre(_portX - 1);
                    player.Move.Z = BuildRules.TileCentre(PortZ - 2);
                    next = true;
                    break;
                }
                case 11:
                {
                    if (MathF.Abs(world.Local.X - player.Move.X) > 0.1f || _boatFrames < 6 || !world.ShopOwned.Contains(world.Content.ShopItem("bp_motorboat"))) break;
                    Check(client.PlaceConstruct(port, _portX, PortZ, south) != 0, "the port was not sent");
                    next = true;
                    break;
                }
                case 12:
                {
                    if (client.PendingRequestCount > 0 || world.Constructs.At(_portX, PortZ) == null) break;
                    var built = server.Constructs.At(_portX, PortZ);
                    Check(built?.Def.Id == "small_port", "the Small Port does not stand on the server");
                    if (built == null) return false;
                    // A Motor Boat in its bay, a belt from behind the quay into its loading face.
                    Sim.SimWorld.Bay(built, out float bx, out float bz, out float bh);
                    server.AddVehicleAt(world.Content.Vehicle("motorboat"), bx, bz, bh, player.Id);
                    var (lx, lz) = Sim.SimWorld.PortLoadingTile(built);
                    Check(client.PlaceConstruct(world.Content.Recipes.First(r => r.Building.Id == "belt_mk1"), lx, lz - 1, south) != 0, "the belt behind the quay was not sent");
                    next = true;
                    break;
                }
                case 13:
                {
                    var built = server.Constructs.At(_portX, PortZ)!;
                    var (lx, lz) = Sim.SimWorld.PortLoadingTile(built);
                    var seg = server.Belts.SegmentAt(lx, lz - 1);
                    var boat = server.InBay(built);
                    if (client.PendingRequestCount > 0 || seg == null || boat == null || _boatFrames < 4) break;
                    var kind = world.Content.MailKind("letter");
                    var letter = new Sim.Mail.MailItem { Id = 984001, Kind = kind.Index, Address = world.Map.Destinations[0].Address, Value = (ushort)kind.BaseValue, SpawnShift = (byte)server.Run.Shift, DeadlineShift = (byte)(server.Run.Shift + 1) };
                    Check(server.PutOnBelt(seg, letter), "no letter went on the belt behind the quay");
                    next = true;
                    break;
                }
                case 14:
                {
                    var built = server.Constructs.At(_portX, PortZ)!;
                    var boat = server.InBay(built)!;
                    if (boat.Cargo.EntryCount == 0 || !world.Vehicles.ContainsKey(boat.Id)) break;
                    Check(_screen.Vehicles.TryGetPosition(boat.Id, out var drawn) && drawn.Y < -0.3f, "the Motor Boat is not drawn on the water");
                    // Back on the quay's corner, looking over the port to the boat.
                    player.Move.X = BuildRules.TileCentre(_portX + 1) + 0.6f;
                    player.Move.Z = BuildRules.TileCentre(PortZ - 1);
                    ViewRay.Aim(world.Local, boat.X, 0.4f, boat.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                    next = true;
                    break;
                }
                case 15:
                {
                    var built = server.Constructs.At(_portX, PortZ)!;
                    var boat = server.InBay(built)!;
                    if (MathF.Abs(world.Local.X - player.Move.X) > 0.1f || _boatFrames < 10) break;
                    ViewRay.Aim(world.Local, boat.X, 0.4f, boat.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                    if (_boatFrames < 14) break;
                    Shot("small-port");
                    GD.Print($"[smoke] boats: a Small Port built on the deep quay at ({_portX}, {PortZ}), a Motor Boat in its bay loaded from a belt into its loading face");
                    _boatVisits++;
                    _boatStep = 0;
                    client.TickStarting += _botTick;
                    _session.FixedTicksPerFrame = RunOutTicksPerFrame;
                    return false;
                }
            }
            if (tap) _session.Interaction.Update(1.0 / 30.0, false, false, interactTapped: true);
            if (next)
            {
                _boatStep++;
                _boatFrames = 0;
            }
            return true;
        }

        /// <summary>After the run: the boat was rowed once, and in the Lobby no boat and no pier is left.</summary>
        private void VerifyBoat()
        {
            var world = _session.Client.World!;
            var server = _session.Server!.World;
            Check(_boatVisits == 1, $"the rowboat was rowed {_boatVisits} times");
            Check(!world.Vehicles.Values.Any(v => v.Def.OnWater) && !server.Vehicles.Any(v => v.Def.OnWater), "a boat is left in the Lobby");
            Check(!server.Constructs.All.Any(c => c.Def.IsPort || c.Def.Deck) && !world.Constructs.All.Any(c => c.Def.IsPort || c.Def.Deck), "a pier or a port is left in the Lobby");
        }
    }
}
