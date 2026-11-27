using System;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Players;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The Rowboat over the loopback transport (doc 04 §3.1 and §3.3): from the quay the prompt
    /// offers it, a client rows it as predicted on the water only, the key does nothing out in the
    /// basin and the hint says why, and by the shore it gets the carrier out on both ends.
    /// </summary>
    public class BoatFlowTests
    {
        private const double Dt = 1.0 / 30.0;

        [Fact]
        public void AClientRowsTheBoat_AsPredicted_AndGetsOutOnlyByTheShore()
        {
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
            var sim = server.World;
            var truth = sim.GetPlayer(client.World!.LocalId)!;
            var boat = sim.AddVehicleAt(content.Vehicle("rowboat"), 21f, 53.2f, 0f, truth.Id);
            truth.Move.X = 21f;
            truth.Move.Z = 51.2f;
            Run(10);
            var a = client.World;
            var here = a.Vehicles[boat.Id];
            Assert.Equal(34, a.Map.Rows);

            var interaction = new InteractionController(client);
            var box = AimBox.Vehicle(a.Map, here.Def, here.X, here.Z, here.Heading);
            ViewRay.Aim(a.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.Mount, interaction.Prompt.Kind);
            Assert.Equal("ROWBOAT\n[E] Get in", HudText.Prompt(a, interaction.Prompt, out _));
            interaction.Update(Dt, false, false, interactTapped: true);
            Run(10);
            Assert.True(a.Local.Riding);

            // Out into the basin, south: 3 m/s, the prediction with the server.
            client.Input.MoveX = 0f;
            client.Input.MoveZ = 1f;
            float worst = 0f;
            for (int i = 0; i < 120; i++)
            {
                Run(1);
                worst = MathF.Max(worst, MathF.Abs(a.Local.Z - truth.Move.Z));
            }
            Assert.True(worst < 0.3f, $"the prediction was {worst:0.00} m from the server");
            Assert.Equal(3f, a.Local.RideSpeed, 2);
            // Into the sea wall at the far side: it stays on the water.
            Run(150);
            Assert.InRange(truth.Move.Z, 64f, 67.6f);
            client.Input.MoveZ = 0f;
            Run(90);
            Assert.Equal("row to a pier or the shore to get out", HudText.GetOff(a, here));
            client.Input.Buttons = InputButtons.Jump;
            Run(2);
            client.Input.Buttons = InputButtons.None;
            Run(10);
            Assert.True(a.Local.Riding && truth.Move.Riding);

            // Back to the quay, and out onto it.
            client.Input.MoveZ = -1f;
            Run(240);
            client.Input.MoveZ = 0f;
            Run(60);
            Assert.Equal("[Space] get out", HudText.GetOff(a, here));
            client.Input.Buttons = InputButtons.Jump;
            Run(2);
            client.Input.Buttons = InputButtons.None;
            Run(10);
            Assert.False(a.Local.Riding);
            Assert.False(truth.Move.Riding);
            Assert.True(truth.Move.Z < 52f, $"got out at z {truth.Move.Z:0.00}, not on the quay");
            Assert.True(MathF.Abs(a.Local.Z - truth.Move.Z) < 0.05f && MathF.Abs(a.Local.X - truth.Move.X) < 0.05f);
            Assert.True(sim.Map.IsWater((int)MathF.Floor(boat.X / 2f), (int)MathF.Floor(boat.Z / 2f)));
        }
    }
}
