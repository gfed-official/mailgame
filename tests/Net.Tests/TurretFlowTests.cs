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
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The turret operated by a carrier over the loopback transport (doc 05 §4 "Player-operated:
    /// 2× fire rate and manual aim"; design choice 125): the prompt beside it, E takes the
    /// controls, the carrier stands while they hold them, attack fires the gun where they look and
    /// swings no hammer, the other client sees who is at it, and E lets go.
    /// </summary>
    public class TurretFlowTests
    {
        private const double Dt = 1.0 / 30.0;
        private const int Tx = 22, Tz = 15;

        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly List<ClientRuntime> Clients = new List<ClientRuntime>();
            public readonly ContentDb Content = Shared.Content;

            public Session()
            {
                Server = new ServerRuntime(Content, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = false }, Hub);
            }

            public SimWorld Sim => Server.World;

            public ClientRuntime Join(string name, int startShift = 0)
            {
                var client = new ClientRuntime(Content, Hub.CreateClient(), name, PlaytestFlags.None, startShift);
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
        public void TheControlsAreTakenWithE_TheGunFiresWhereTheCarrierLooks_AndEveryoneSeesWhoIsAtIt()
        {
            var s = new Session();
            var ada = s.Join("Ada", startShift: 3);
            var bo = s.Join("Bo");
            ClientWorld a = ada.World!, b = bo.World!;
            var interaction = new InteractionController(ada);
            var feed = new EventFeed();
            ada.EventReceived += e => feed.OnEvent(a, e);
            Assert.NotEqual(0u, ada.PressGo());
            s.Run(10);
            Assert.Equal((RunPhase.Prep, 3), (s.Sim.Run.Phase, s.Sim.Run.Shift));
            var truth = s.Truth(ada);
            Assert.True(s.Sim.Grant(s.Content.ShopItem("bp_turret"), truth.Id));
            Assert.True(s.Sim.Give(truth.Inventory, ItemStack.OfItem(s.Content.Item("iron_ingot"), 6)));
            Assert.True(s.Sim.Give(truth.Inventory, ItemStack.OfItem(s.Content.Item("stone"), 2)));
            truth.Move.X = BuildRules.TileCentre(Tx);
            truth.Move.Z = BuildRules.TileCentre(Tz) + 2.6f;
            s.Run(10);
            Assert.NotEqual(0u, ada.PlaceConstruct(s.Content.Building("turret").Recipe, Tx, Tz, 0));
            s.Run(10);
            var turret = a.Constructs.At(Tx, Tz)!;
            Assert.True(turret.Def.IsTurret);

            // Beside it, looking at it: the prompt offers the controls.
            truth.Move.X = turret.CentreX + 2f;
            truth.Move.Z = turret.CentreZ;
            s.Run(20);
            var box = AimBox.Built(a.Map, turret);
            ViewRay.Aim(a.Local, box.X, box.Y, box.Z, out ada.Input.YawRadians, out ada.Input.PitchRadians);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.OperateTurret, interaction.Prompt.Kind);
            Assert.Equal("TURRET\n[E] Operate  ·  by hand, 2× the rate", HudText.Prompt(a, interaction.Prompt, out _));
            interaction.Update(Dt, false, false, interactTapped: true);
            s.Run(10);
            Assert.Same(turret, a.LocalTurret);
            Assert.Equal(a.LocalId, b.TurretOperators[Tz * b.Map.Cols + Tx]);
            Assert.Same(s.Sim.Constructs.At(Tx, Tz), truth.Turret);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.LeaveTurret, interaction.Prompt.Kind);
            Assert.Equal("TURRET  ·  at the controls\n[LMB] Fire where you look\n[E] Let go", HudText.Prompt(a, interaction.Prompt, out _));
            Assert.Equal("[LMB] turret, a shot every 0.25 s, 18 m", HudText.TurretWeapons(turret.Def));

            // At the controls the carrier stands, whatever the keys say.
            float x0 = truth.Move.X, z0 = truth.Move.Z;
            ada.Input.MoveX = 1f;
            s.Run(15);
            ada.Input.MoveX = 0f;
            Assert.Equal((x0, z0), (truth.Move.X, truth.Move.Z));
            Assert.Equal((x0, z0), (a.Local.X, a.Local.Z));

            // A raider east; the carrier looks at it and holds attack.
            var raider = s.Sim.SpawnEnemy(s.Content.Enemy("barbarian"), turret.CentreX + 12f, turret.CentreZ)!;
            int swings = ada.LocalHammerSwings, shotsBefore = s.Sim.TurretShots;
            for (int i = 0; i < 20; i++)
            {
                ada.Input.YawRadians = MathF.Atan2(-(raider.X - turret.CentreX), -(raider.Z - turret.CentreZ));
                ada.Input.Buttons = InputButtons.Attack;
                s.Run(1);
            }
            ada.Input.Buttons = InputButtons.None;
            s.Run(5);
            Assert.InRange(s.Sim.TurretShots - shotsBefore, 2, 4);
            Assert.True(raider.Hp < s.Content.Enemy("barbarian").Hp);
            Assert.Equal(swings, ada.LocalHammerSwings);
            Assert.True(a.TurretShots.ContainsKey(Tz * a.Map.Cols + Tx));
            Assert.True(b.TurretShots.ContainsKey(Tz * b.Map.Cols + Tx));

            // A second carrier is told somebody is at it.
            Assert.Equal("Somebody is at its controls", HudText.OperateRefused(OpResult.Manned));

            // E lets go: the gun is its own again, and the carrier walks.
            interaction.Update(Dt, false, false, interactTapped: true);
            s.Run(10);
            Assert.Null(a.LocalTurret);
            Assert.Null(truth.Turret);
            Assert.Empty(b.TurretOperators);
            ada.Input.MoveX = 1f;
            s.Run(10);
            ada.Input.MoveX = 0f;
            Assert.True(truth.Move.X > x0);
        }
    }
}
