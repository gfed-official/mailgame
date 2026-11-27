using System;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The Mail Truck over the loopback transport (doc 04 §3.1): beside it the prompt says its fuel,
    /// a client drives it at 14 m/s as predicted, the tank goes down on both ends, an Oil Can in
    /// the hand refuels it, and one who joins hears how much it has.
    /// </summary>
    public class TruckFlowTests
    {
        private const double Dt = 1.0 / 30.0;

        [Fact]
        public void AClientDrivesTheTruck_ItsFuelGoesDownOnBothEnds_AndAnOilCanFillsIt()
        {
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = false }, hub);
            var client = new ClientRuntime(content, hub.CreateClient(), "Ada", PlaytestFlags.None, 0);
            ClientRuntime? late = null;
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { client.Tick(); late?.Tick(); server.Tick(); }
            }
            Run(10);
            Assert.NotEqual(0u, client.PressGo());
            Run(10);
            var sim = server.World;
            var truth = sim.GetPlayer(client.World!.LocalId)!;
            var truck = sim.AddVehicleAt(content.Vehicle("mail_truck"), 30f, 28f, -MathF.PI / 2f, truth.Id);
            truth.Move.X = 30f;
            truth.Move.Z = 30.6f;
            Run(10);
            var a = client.World;
            var here = a.Vehicles[truck.Id];
            Assert.Equal(300f, here.Fuel);
            var interaction = new InteractionController(client);
            var box = AimBox.Vehicle(a.Map, here.Def, here.X, here.Z, here.Heading);
            ViewRay.Aim(a.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.Mount, interaction.Prompt.Kind);
            Assert.Equal("MAIL TRUCK  ·  fuel 05:00 of 10:00\n[E] Drive", HudText.Prompt(a, interaction.Prompt, out _));
            interaction.Update(Dt, false, false, interactTapped: true);
            Run(10);
            Assert.True(a.Local.Riding);

            // East down the street: 14 m/s, the prediction with the server, the tank going down here as there.
            client.Input.MoveX = 1f;
            client.Input.MoveZ = 0f;
            float worst = 0f;
            for (int i = 0; i < 120; i++)
            {
                Run(1);
                worst = MathF.Max(worst, MathF.Abs(a.Local.X - truth.Move.X));
            }
            Assert.True(worst < 0.9f, $"the prediction was {worst:0.00} m from the server");
            Assert.Equal(14f, a.Local.RideSpeed, 2);
            Assert.InRange(truck.Fuel, 295f, 297f);
            Assert.True(MathF.Abs(here.Fuel - truck.Fuel) < 0.3f, $"fuel {here.Fuel} here, {truck.Fuel} on the server");

            // Off; an Oil Can in the hand, and E at it fills it.
            client.Input.MoveX = 0f;
            Run(90);
            client.Input.Buttons = InputButtons.Jump;
            Run(2);
            client.Input.Buttons = InputButtons.None;
            Run(10);
            Assert.False(a.Local.Riding);
            Assert.True(sim.Give(truth.Hotbar, ItemStack.OfItem(content.Item("oil_can"), 1)));
            Run(5);
            int slot = a.Hotbar.Entries.First(e => e.Stack.Kind == StackKind.Item).X;
            client.Input.HeldSlot = (byte)slot;
            truth.Move.X = truck.X;
            truth.Move.Z = truck.Z + 2.5f;
            Run(10);
            box = AimBox.Vehicle(a.Map, here.Def, here.X, here.Z, here.Heading);
            ViewRay.Aim(a.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.Refuel, interaction.Prompt.Kind);
            Assert.EndsWith("\n[E] Refuel with an Oil Can", HudText.Prompt(a, interaction.Prompt, out _));
            interaction.Update(Dt, false, false, interactTapped: true);
            Run(10);
            // (It burnt on while it rolled out.)
            Assert.InRange(truck.Fuel, 590f, 597f);
            Assert.Equal(truck.Fuel, here.Fuel);

            // One who joins hears it.
            late = new ClientRuntime(content, hub.CreateClient(), "Bo", PlaytestFlags.None, 0);
            Run(15);
            Assert.Equal(truck.Fuel, late.World!.Vehicles[truck.Id].Fuel);
        }

        [Fact]
        public void AQuickTapOfSpace_NeverSeenHeld_GetsTheDriverOff()
        {
            // Playtest f5813b5: a tap shorter than a frame was lost; a 0.3 s hold worked.
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = false }, hub);
            var client = new ClientRuntime(content, hub.CreateClient(), "Ada", PlaytestFlags.None, 0);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { client.Tick(); server.Tick(); }
            }
            Run(10);
            Assert.NotEqual(0u, client.PressGo());
            Run(10);
            var truth = server.World.GetPlayer(client.World!.LocalId)!;
            var truck = server.World.AddVehicleAt(content.Vehicle("mail_truck"), 30f, 28f, -MathF.PI / 2f, truth.Id);
            truth.Move.X = 30f;
            truth.Move.Z = 30.6f;
            Run(10);
            Assert.NotEqual(0u, client.Mount(truck.Id));
            Run(10);
            Assert.True(client.World.Local.Riding);

            // Pressed and let go between two frames: never in Buttons, only in Tapped, for one tick.
            client.Input.Tapped |= InputButtons.Jump;
            Run(1);
            Assert.Equal(InputButtons.None, client.Input.Tapped);
            Run(10);
            Assert.False(client.World.Local.Riding);
            Assert.False(truth.Move.Riding);
        }
    }
}
