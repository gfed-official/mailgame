using System;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The Slingshot over the loopback transport (doc 05 §1.2; design choice 143): held, the weapons'
    /// line counts its shots, five a Stone; held attack looses one at the raider ahead, which the
    /// server hurts and every client hears of.
    /// </summary>
    public class SlingshotFlowTests
    {
        [Fact]
        public void Held_TheLineCountsItsShots_AndAShotHurtsTheRaiderAhead()
        {
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = false }, hub);
            var ada = new ClientRuntime(content, hub.CreateClient(), "Ada", PlaytestFlags.None, 1);
            var bob = new ClientRuntime(content, hub.CreateClient(), "Bob", PlaytestFlags.None, 1);
            int stonesHeard = 0;
            bob.EventReceived += ev => { if (ev is SlingshotFiredEvent { Hit: SlingHit.Ground }) stonesHeard++; };
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { ada.Tick(); bob.Tick(); server.Tick(); }
            }
            Run(10);
            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            Assert.NotEqual(0u, ada.PressGo());
            Assert.NotEqual(0u, bob.PressGo());
            Run(10);
            var sim = server.World;
            var a = ada.World!;
            Assert.Equal(RunPhase.Delivery, a.Global.Phase);
            var truth = sim.GetPlayer(a.LocalId)!;
            truth.MaxHp = truth.Hp = 100000f;
            var sling = content.Item("slingshot");
            Assert.True(sim.Give(truth.Hotbar, ItemStack.OfItem(sling, 1)));
            Run(10);
            int slot = a.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == sling.Index).X;
            ada.Input.HeldSlot = (byte)slot;
            Run(5);
            Assert.Equal(0, ada.SlingShots);
            Assert.Equal("[LMB] slingshot: no Stone   ·   [RMB] mail cannon: no Small Package", HudText.SlingWeapons(ada.SlingShots, ada.CannonAmmo));

            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item("stone"), 3)));
            Run(10);
            Assert.Equal(15, ada.SlingShots);
            Assert.Equal("[LMB] slingshot, 15 shots   ·   [RMB] mail cannon: no Small Package", HudText.SlingWeapons(ada.SlingShots, ada.CannonAmmo));

            var raider = sim.SpawnEnemy(content.Enemy("barbarian"), truth.Move.X - 10f, truth.Move.Z)!;
            ada.Input.YawRadians = MathF.Atan2(-(raider.X - a.Local.X), -(raider.Z - a.Local.Z));
            ada.Input.Buttons = InputButtons.Attack;
            Run(2);
            ada.Input.Buttons = InputButtons.None;
            Run(10);
            Assert.Equal(raider.Def.Hp - 12f, raider.Hp);
            Assert.Equal(1, stonesHeard);
            Assert.Equal(1, ada.LocalSlingShots);
            Assert.Equal(4, ada.SlingLoaded);
            Assert.Equal(14, ada.SlingShots);
            Assert.Equal(0, ada.ContainerDesyncs + ada.BadPackets + bob.ContainerDesyncs + bob.BadPackets);
        }
    }
}
