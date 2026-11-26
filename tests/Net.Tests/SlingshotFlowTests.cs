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

        [Fact]
        public void BehindLatency_AStoneLoosedAtTheRaiderWhereTheClientDrawsIt_Hits()
        {
            // Doc 06 §4.3, design choice 145: a raider crossing the line at 7.5 m/s is drawn by the
            // client some ticks behind where the server has it; the server rewinds it there.
            var hub = new LoopbackHub { DelayPolls = 1 };
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = false }, hub);
            var ada = new ClientRuntime(content, hub.CreateClient(), "Ada", PlaytestFlags.None, 1);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { ada.Tick(); server.Tick(); }
            }
            Run(10);
            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            var sim = server.World;
            var a = ada.World!;
            Assert.Equal(RunPhase.Delivery, a.Global.Phase);
            var truth = sim.GetPlayer(a.LocalId)!;
            truth.MaxHp = truth.Hp = 100000f;
            var sling = content.Item("slingshot");
            Assert.True(sim.Give(truth.Hotbar, ItemStack.OfItem(sling, 1)));
            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item("stone"), 1)));
            Run(10);
            ada.Input.HeldSlot = (byte)a.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == sling.Index).X;
            float lineZ = truth.Move.Z, x = truth.Move.X - 10f;
            ada.Input.YawRadians = MathF.PI / 2f; // west, along −X
            var raider = sim.SpawnEnemy(content.Enemy("barbarian"), x, lineZ - 6f)!;
            raider.Speed = raider.BaseSpeed = 0f;
            bool fired = false;
            float firedAt = 0f;
            for (int k = 0; k < 48 && !fired; k++)
            {
                raider.X = x;
                raider.Z = lineZ - 6f + 0.25f * k;
                ada.Input.Buttons = InputButtons.None;
                float dz = float.NaN;
                if (a.Enemies.TryGetValue((ushort)raider.Id.Value, out var drawn)) drawn.Interpolate(a.ServerTick + 1.0 - ClientRuntime.EnemyInterpolationDelayTicks, out _, out dz, out _);
                if (MathF.Abs(dz - lineZ) < 0.15f)
                {
                    ada.Input.Buttons = InputButtons.Attack;
                    fired = true;
                    firedAt = raider.Z;
                }
                Run(1);
            }
            Assert.True(fired);
            Run(10);
            // Where the server had it as the Stone flew, it was a metre and more past the line.
            Assert.True(firedAt - lineZ > raider.Radius + 0.3f, $"the raider was {firedAt - lineZ:0.00} m past the line");
            Assert.InRange(sim.LastRewindTicks, 4, SimWorld.MaxRewindTicks);
            Assert.Equal(raider.Def.Hp - 12f, raider.Hp);
        }
    }
}
