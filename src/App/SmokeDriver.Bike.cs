using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.App
{
    /// <summary>
    /// The bike in the smoke's run (doc 04 §3.1): in the Prep after the Gate and the Alarm Post,
    /// the host buys a Bike with its card's key, finds it parked by the counter and drawn,
    /// looks at it ("Ride Bike  [E]") and taps interact, rides it down the street at its road
    /// speed as the server has them, opens its rack from the saddle, and gets off with the jump
    /// key; the bike stands where they got off, and is gone with the run.
    /// </summary>
    public partial class SmokeDriver
    {
        private int _bikeStep, _bikeFrames, _bikeVisits;
        /// <summary>The Land kit's bike stood by the counter already: the card is refused and that one is ridden.</summary>
        private bool _kitBike;
        private float _bikeTopSpeed;
        private Action? _bikeTick;

        private bool StepBike()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var card = world.Content.ShopItem("bike");
            var player = server.GetPlayer(world.LocalId)!;
            var po = world.Map.PostOffice;

            if (_bikeStep == 0)
            {
                if (_bikeVisits > 0 || _defenceVisits == 0 || world.Local.Dead || client.PendingRequestCount > 0) return false;
                // The run's money is the bot's to earn; the bike is bought with what the counter asks, topped up if need be.
                if (server.Economy.Wallet < card.Price) server.Economy.AddLoot(card.Price - server.Economy.Wallet);
                _session.FixedTicksPerFrame = 1;
                // The bot shuts whatever is open outside Delivery: the rack is the host's own to open and shut here.
                client.TickStarting -= _botTick;
                player.Move.X = po.ShopX + ShopStandOff;
                player.Move.Z = po.ShopZ;
                _bikeStep = 1;
                _bikeFrames = 0;
                return true;
            }
            if (++_bikeFrames > BuildStepFrames * 2)
            {
                Fail($"the bike visit stopped at step {_bikeStep}: {Bikes(world).Count()} bikes here, {server.Vehicles.Count(v => v.Def.Id == "bike")} on the server, riding {world.Local.Riding}, "
                    + $"prompt {_session.Interaction.Prompt.Kind} \"{hud.PromptText}\", host at {world.Local.X:0.00}, {world.Local.Z:0.00}");
                return false;
            }
            bool tap = false;
            bool next = false;
            switch (_bikeStep)
            {
                case 1:
                {
                    float dx = world.Local.X - player.Move.X, dz = world.Local.Z - player.Move.Z;
                    if (dx * dx + dz * dz > 0.1f || _bikeFrames < 4) break;
                    if (world.Global.Wallet != server.Economy.Wallet) break;
                    if (!world.ShopOpen) { client.OpenShop(); break; }
                    int place = world.ShopShelf.ToList().IndexOf(card);
                    _kitBike = Bikes(world).Count() == 1;
                    if (_kitBike)
                    {
                        // One carrier, and the team's kit gave them a bike: the card says so and sells none.
                        Check(world.ShopState(card) == Sim.Inventory.OpResult.OnePerCarrier, $"with the kit's bike by the counter the Bike's card says \"{HudText.ShopNote(world, card)}\"");
                    }
                    else
                    {
                        Check(place >= 0 && world.ShopState(card) == Sim.Inventory.OpResult.Ok, $"the Bike's card is at {place}: \"{HudText.ShopNote(world, card)}\"");
                        Check(client.PressShopKey(place), $"the key of the Bike's card {HudText.CardKey(place)} was not the shop's");
                    }
                    next = true;
                    break;
                }
                case 2:
                {
                    if (Bikes(world).Count() != 1 || server.Vehicles.Count(v => v.Def.Id == "bike") != 1 || client.PendingRequestCount > 0) break;
                    var bought = Bikes(world).First();
                    // The kit's bike was announced in shift 1 (the kit's visit checks that line).
                    Check(_kitBike || _session.Feed.Entries.Any(e => e.Text == HudText.VehicleAdded(bought.Def)), "the feed does not say where the bought bike stands");
                    Check(_screen.Vehicles.Shown == 1 + Others(world) && _screen.Vehicles.RiddenShown == 0, $"{_screen.Vehicles.Shown} bikes drawn for one parked");
                    client.CloseShop();
                    Shot("bike-bought");
                    // Beside it, on open ground (not in a gate or on a strip someone built there).
                    foreach (var (sx, sz) in new[] { (1.6f, 0f), (-1.6f, 0f), (0f, 1.6f), (0f, -1.6f) })
                    {
                        float x = bought.X + sx, z = bought.Z + sz;
                        int tx = (int)MathF.Floor(x / 2f), tz = (int)MathF.Floor(z / 2f);
                        if (world.Map.IsNavBlocked(tx, tz) || world.Constructs.At(tx, tz) != null) continue;
                        player.Move.X = x;
                        player.Move.Z = z;
                        break;
                    }
                    next = true;
                    break;
                }
                case 3:
                {
                    var bike = Bikes(world).First();
                    float dx = world.Local.X - player.Move.X, dz = world.Local.Z - player.Move.Z;
                    if (dx * dx + dz * dz > 0.1f || _bikeFrames < 4) break;
                    var box = AimBox.Vehicle(world.Map, bike.X, bike.Z);
                    ViewRay.Aim(world.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                    _session.Interaction.Update(1.0 / 30.0, false, false);
                    hud.Refresh();
                    // A few frames under the new aim, so that the picture is of it.
                    if (_session.Interaction.Prompt.Kind != InteractKind.Mount || _bikeFrames < 8) break;
                    Check(hud.PromptText == "BIKE\n[E] Ride", $"beside the bike the prompt reads \"{hud.PromptText}\"");
                    Shot("bike-prompt");
                    tap = true;
                    next = true;
                    break;
                }
                case 4:
                {
                    if (!world.Local.Riding || !player.Move.Riding || client.PendingRequestCount > 0) break;
                    Check(_screen.Vehicles.RiddenShown == 1, "the ridden bike is not drawn under the host");
                    Check(_session.Feed.Entries.Any(e => e.Text.StartsWith("You are on the Bike", StringComparison.Ordinal)), "the feed does not say the host is on the bike");
                    // Out on the street, east: the rider's own input, predicted, as a player rides.
                    player.Move.X = 20f;
                    player.Move.Z = 28f;
                    _bikeTick = () =>
                    {
                        client.Input.YawRadians = -MathF.PI * 0.5f;
                        client.Input.PitchRadians = 0f;
                        client.Input.MoveX = 1f;
                        client.Input.MoveZ = 0f;
                        client.Input.Buttons = InputButtons.None;
                    };
                    client.TickStarting += _bikeTick;
                    next = true;
                    break;
                }
                case 5:
                {
                    _bikeTopSpeed = MathF.Max(_bikeTopSpeed, player.Move.RideSpeed);
                    if (_bikeFrames == 45) Shot("bike-ride");
                    if (world.Local.X < 60f) break;
                    Check(MathF.Abs(world.Local.X - player.Move.X) < 0.6f, $"riding, the prediction is at x {world.Local.X:0.00} and the server at {player.Move.X:0.00}");
                    Check(_bikeTopSpeed > 7.9f && world.Local.RideSpeed > 7.9f, $"on the street the bike ran {_bikeTopSpeed:0.00} m/s");
                    client.TickStarting -= _bikeTick;
                    _bikeTick = () =>
                    {
                        client.Input.MoveX = 0f;
                        client.Input.MoveZ = 0f;
                        client.Input.Buttons = InputButtons.None;
                    };
                    client.TickStarting += _bikeTick;
                    next = true;
                    break;
                }
                case 6:
                {
                    // Rolled out; from the saddle, eyes on the road, E opens the rack.
                    if (player.Move.RideSpeed > 0f || world.Local.RideSpeed > 0f) break;
                    client.Input.PitchRadians = -0.9f;
                    _session.Interaction.Update(1.0 / 30.0, false, false);
                    hud.Refresh();
                    Check(_session.Interaction.Prompt.Kind == InteractKind.OpenRack && hud.PromptText.EndsWith($"{HudText.GetOffKey} get off", StringComparison.Ordinal),
                        $"in the saddle the prompt is {_session.Interaction.Prompt.Kind} \"{hud.PromptText}\"");
                    tap = true;
                    next = true;
                    break;
                }
                case 7:
                {
                    var bike = world.LocalRide;
                    if (bike == null || world.OpenContainerId != bike.CargoContainer || client.PendingRequestCount > 0) break;
                    Check(_screen.Inventory.ExternalTitle == "Bike cargo", $"the rack's panel is titled \"{_screen.Inventory.ExternalTitle}\"");
                    Shot("bike-rack");
                    // The jump key: off, and the rack shuts.
                    client.TickStarting -= _bikeTick;
                    _bikeTick = null;
                    bool jumped = false;
                    _bikeTick = () =>
                    {
                        client.Input.Buttons = jumped ? InputButtons.None : InputButtons.Jump;
                        jumped = true;
                    };
                    client.TickStarting += _bikeTick;
                    next = true;
                    break;
                }
                case 8:
                {
                    var bike = Bikes(world).First();
                    if (world.Local.Riding || player.Move.Riding || bike.Ridden || client.PendingRequestCount > 0) break;
                    client.TickStarting -= _bikeTick;
                    _bikeTick = null;
                    client.Input.Buttons = InputButtons.None;
                    client.Input.PitchRadians = 0f;
                    Check(world.OpenContainerId == 0, "the rack is still open after getting off");
                    Check(_session.Feed.Entries.Any(e => e.Text == "You got off the Bike"), "the feed does not say the host got off");
                    Check(MathF.Abs(bike.X - player.Move.X) < 1f && bike.X > 55f, $"the bike was left at x {bike.X:0.00}, the host stands at {player.Move.X:0.00}");
                    Check(_screen.Vehicles.Shown == 1 + Others(world) && _screen.Vehicles.RiddenShown == 0, "the bike left on the street is not drawn parked");
                    // A step back from it, looking at it, for the picture.
                    player.Move.X = bike.X + 2.2f;
                    next = true;
                    break;
                }
                case 9:
                {
                    var bike = Bikes(world).First();
                    if (MathF.Abs(world.Local.X - player.Move.X) > 0.1f || _bikeFrames < 4) break;
                    var box = AimBox.Vehicle(world.Map, bike.X, bike.Z);
                    ViewRay.Aim(world.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                    if (_bikeFrames < 6) break;
                    Shot("bike-parked");
                    GD.Print($"[smoke] bike: {(_kitBike ? "the Land kit's" : "bought")}, ridden at {_bikeTopSpeed:0.0} m/s down the street, its rack opened from the saddle, and left parked at x {bike.X:0.0}");
                    _bikeVisits++;
                    _bikeStep = 0;
                    client.TickStarting += _botTick;
                    _session.FixedTicksPerFrame = RunOutTicksPerFrame;
                    return false;
                }
            }
            if (tap) _session.Interaction.Update(1.0 / 30.0, false, false, interactTapped: true);
            if (next)
            {
                _bikeStep++;
                _bikeFrames = 0;
            }
            return true;
        }

        /// <summary>The bikes the client knows of; a truck left standing at a Vehicle Depot is not one.</summary>
        private static System.Collections.Generic.IEnumerable<PerformativeMail.Client.ClientVehicle> Bikes(PerformativeMail.Client.ClientWorld world) =>
            world.Vehicles.Values.Where(v => v.Def.Id == "bike");

        private static int Others(PerformativeMail.Client.ClientWorld world) => world.Vehicles.Count - Bikes(world).Count();

        /// <summary>After the run: the bike was ridden once, and in the Lobby none is left on either end.</summary>
        private void VerifyBike()
        {
            var world = _session.Client.World!;
            var server = _session.Server!.World;
            Check(_bikeVisits == 1, $"the bike was ridden {_bikeVisits} times");
            Check(world.Vehicles.Count == 0 && server.Vehicles.Count == 0 && _screen.Vehicles.Shown == 0,
                $"in the Lobby {world.Vehicles.Count} bikes are known here, {server.Vehicles.Count} on the server, {_screen.Vehicles.Shown} drawn");
        }
    }
}
