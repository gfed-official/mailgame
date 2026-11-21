using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The bike (doc 04 §3.1) as the clients see it over the loopback transport: bought at the
    /// counter, the prompt beside it, the saddle got into with E, the ride predicted by the rider
    /// and seen by the others, the rack opened from the saddle, the jump key off.
    /// </summary>
    public class VehicleFlowTests
    {
        private const double Dt = 1.0 / 30.0;
        private const float StreetZ = 28f;

        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly List<ClientRuntime> Clients = new List<ClientRuntime>();
            public readonly ContentDb Content = Shared.Content;

            public Session()
            {
                Server = new ServerRuntime(Content, new SimSettings { Seed = 3, Raids = false, Draft = false }, Hub);
            }

            public SimWorld Sim => Server.World;

            public ClientRuntime Join(string name)
            {
                var client = new ClientRuntime(Content, Hub.CreateClient(), name);
                Clients.Add(client);
                Run(10);
                return client;
            }

            public void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    foreach (var c in Clients) c.Tick();
                    Server.Tick();
                }
            }

            public Player Truth(ClientRuntime client) => Sim.GetPlayer(client.World!.LocalId)!;
        }

        [Fact]
        public void ABikeIsBought_RiddenAndLeft_AsEverybodySeesIt()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            var bo = s.Join("Bo");
            ClientWorld a = ada.World!, b = bo.World!;
            var interaction = new InteractionController(ada);
            EventFeed adaFeed = new EventFeed(), boFeed = new EventFeed();
            ada.EventReceived += e => adaFeed.OnEvent(a, e);
            bo.EventReceived += e => boFeed.OnEvent(b, e);
            Assert.NotEqual(0u, ada.PressGo());
            s.Run(10);
            Assert.Equal(RunPhase.Prep, s.Sim.Run.Phase);
            s.Sim.Economy.AddLoot(500);

            // At the counter: the card, bought with its key.
            var po = s.Sim.Map.PostOffice;
            var truth = s.Truth(ada);
            truth.Move.X = po.ShopX + 1.5f;
            truth.Move.Z = po.ShopZ;
            s.Run(10);
            Assert.True(ada.OpenShop());
            var card = s.Content.ShopItem("bike");
            int place = a.ShopShelf.ToList().IndexOf(card);
            Assert.True(place >= 0);
            Assert.Equal(OpResult.Ok, a.ShopState(card));
            Assert.Equal("One for each carrier   ·   parked by the counter", HudText.ShopNote(a, card));
            Assert.True(ada.PressShopKey(place));
            s.Run(10);
            var bike = Assert.Single(a.Vehicles.Values);
            Assert.Single(b.Vehicles.Values);
            Assert.False(bike.Ridden);
            Assert.Contains(adaFeed.Entries, e => e.Text == "The Bike stands by the counter   ·   look at it and [E] to ride");
            Assert.Contains(boFeed.Entries, e => e.Text == "Ada bought Bike  -$1.20");
            // Another is there for Bo, and then the team has one each.
            Assert.Equal(OpResult.Ok, a.ShopState(card));
            ada.CloseShop();

            // Beside it, looking at it: "Ride Bike", and E puts Ada into the saddle.
            truth.Move.X = bike.X + 1.6f;
            truth.Move.Z = bike.Z;
            s.Run(10);
            var box = AimBox.Vehicle(a.Map, bike.X, bike.Z);
            ViewRay.Aim(a.Local, box.X, box.Y, box.Z, out ada.Input.YawRadians, out ada.Input.PitchRadians);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.Mount, interaction.Prompt.Kind);
            Assert.Equal("Ride Bike  [E]", HudText.Prompt(a, interaction.Prompt, out _));
            interaction.Update(Dt, false, false, interactTapped: true);
            s.Run(10);
            Assert.True(a.Local.Riding);
            Assert.Same(bike, a.LocalRide);
            Assert.Equal(a.LocalId, b.Vehicles[bike.Id].Rider);
            Assert.Equal(1, b.Players[a.LocalId].Ride);
            Assert.Contains(adaFeed.Entries, e => e.Text.StartsWith("You are on the Bike   ·   [Space] gets off", StringComparison.Ordinal));
            Assert.Contains(boFeed.Entries, e => e.Text == "Ada got on a Bike");

            // Down the street at 8 m/s, as predicted on Ada's end and as the server has it.
            truth.Move.X = 30f;
            truth.Move.Z = StreetZ;
            s.Run(10);
            ada.Input.MoveX = 1f;
            ada.Input.MoveZ = 0f;
            // Every tick the prediction is where the server has her, within what one tick in flight covers.
            float worst = 0f;
            for (int i = 0; i < 90; i++)
            {
                s.Run(1);
                worst = MathF.Max(worst, MathF.Abs(a.Local.X - truth.Move.X));
            }
            Assert.True(worst < 0.6f, $"the prediction was {worst:0.00} m from the server");
            Assert.Equal(8f, a.Local.RideSpeed, 2);
            ada.Input.MoveX = 0f;
            s.Run(100);
            Assert.Equal(0f, a.Local.RideSpeed);
            Assert.Equal(truth.Move.X, a.Local.X, 3);
            Assert.True(truth.Move.X > 45f, $"rode to x {truth.Move.X}");
            // Bo sees her on it, turned the way it points (east is yaw −π/2).
            Assert.Equal(-MathF.PI / 2f, NormalisedYaw(b.Players[a.LocalId].Heading), 1);

            // From the saddle with nothing else in view: the rack, and how to get off.
            ada.Input.PitchRadians = -0.9f;
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.OpenRack, interaction.Prompt.Kind);
            Assert.Equal("Open Bike cargo  [E]   ·   [Space] get off", HudText.Prompt(a, interaction.Prompt, out _));
            interaction.Update(Dt, false, false, interactTapped: true);
            s.Run(10);
            Assert.Equal(bike.CargoContainer, a.OpenContainerId);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.CloseRack, interaction.Prompt.Kind);

            // The jump key: off at once on Ada's end, and the rack shuts.
            ada.Input.Buttons = InputButtons.Jump;
            s.Run(1);
            ada.Input.Buttons = InputButtons.None;
            Assert.False(a.Local.Riding);
            s.Run(10);
            Assert.False(truth.Move.Riding);
            Assert.False(b.Vehicles[bike.Id].Ridden);
            Assert.Equal(0, b.Players[a.LocalId].Ride);
            Assert.Equal(0u, a.OpenContainerId);
            Assert.Contains(adaFeed.Entries, e => e.Text == "You got off the Bike");
            Assert.Equal(truth.Move.X, b.Vehicles[bike.Id].X, 1);

            // Somebody who joins now finds it parked where she left it.
            var cara = s.Join("Cara");
            var c = cara.World!;
            var seen = Assert.Single(c.Vehicles.Values);
            Assert.Equal((bike.Id, false), (seen.Id, seen.Ridden));
            Assert.Equal(b.Vehicles[bike.Id].X, seen.X);
        }

        private static float NormalisedYaw(float yaw)
        {
            while (yaw > MathF.PI) yaw -= 2f * MathF.PI;
            while (yaw < -MathF.PI) yaw += 2f * MathF.PI;
            return yaw;
        }
    }
}
