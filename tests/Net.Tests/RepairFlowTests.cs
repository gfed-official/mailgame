using System;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The Repair Hammer over the loopback transport (doc 05 §4, doc 09 §3 "Repair hammer shows a
    /// target health bar"; design choice 139): held at a hurt wall the HUD's line names it with its
    /// health and cost, held attack mends it on the client as on the server, and the feed says when
    /// it is whole; without a Log the line says what it takes.
    /// </summary>
    public class RepairFlowTests
    {
        private const int Tx = 22, Tz = 15;

        [Fact]
        public void HeldAtAHurtWall_TheLineSaysWhatItTakes_AndHeldAttackMendsIt()
        {
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = false }, hub);
            var ada = new ClientRuntime(content, hub.CreateClient(), "Ada", PlaytestFlags.None, 1);
            var feed = new EventFeed();
            ada.EventReceived += ev => feed.OnEvent(ada.World!, ev);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { ada.Tick(); server.Tick(); }
            }
            Run(10);
            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            var sim = server.World;
            var a = ada.World!;
            Assert.Equal(RunPhase.Prep, a.Global.Phase);
            var truth = sim.GetPlayer(a.LocalId)!;
            var hammer = content.Item("repair_hammer");
            Assert.True(sim.Give(truth.Hotbar, ItemStack.OfItem(hammer, 1)));
            var wall = sim.PutUp(content.Building("wall_wood"), Tx, Tz);
            sim.DamageConstruct(wall, 200f);
            truth.Move.X = wall.CentreX + 1.8f;
            truth.Move.Z = wall.CentreZ;
            Run(20);
            var seen = a.Constructs.At(Tx, Tz)!;
            Assert.Equal(100, seen.Hp);
            int slot = a.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == hammer.Index).X;
            ada.Input.HeldSlot = (byte)slot;
            ada.Input.YawRadians = MathF.Atan2(-(wall.CentreX - a.Local.X), -(wall.CentreZ - a.Local.Z));
            Run(5);

            // No Log in the pockets: the line says so, in red.
            Assert.Same(seen, HudText.RepairTarget(a, slot, out bool po, out var held));
            Assert.False(po);
            Assert.Same(hammer, held);
            Assert.Equal("WOODEN WALL  ·  100 / 300 HP\nMending it takes Log", HudText.RepairPrompt(a, slot, out var tone));
            Assert.Equal(PromptTone.Mismatch, tone);
            Assert.Equal("[LMB] mend, 50 HP a second   ·   [RMB] mail cannon: no Small Package", HudText.RepairWeapons(hammer, 0));

            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item("log"), 1)));
            Run(10);
            Assert.Equal("WOODEN WALL  ·  100 / 300 HP\n[hold LMB] Mend  ·  Log x0.75 a full bar", HudText.RepairPrompt(a, slot, out tone));
            Assert.Equal(PromptTone.Neutral, tone);
            // Doc 09 §3: the HUD's target bar under the crosshair shows a third.
            Assert.Equal(100f / 300f, HudText.RepairTargetHealth(a, slot), 3);

            // Held for five seconds: whole, on the client too, and the feed says so once.
            ada.Input.Buttons = InputButtons.Attack;
            Run(5 * 30);
            ada.Input.Buttons = InputButtons.None;
            Run(5);
            Assert.Equal(300, wall.Hp);
            Assert.Equal(300, a.Constructs.At(Tx, Tz)!.Hp);
            Assert.Single(feed.Entries, e => e.Text == "You mended the Wooden Wall");
            // A mend is no blow: no number rose for it (the one there is the blow that hurt it).
            Assert.All(a.ConstructHits, h => Assert.Equal(200, h.Amount));
            // Whole, it is nothing to mend: no line.
            Assert.Equal("", HudText.RepairPrompt(a, slot, out _));
            Assert.Equal(-1f, HudText.RepairTargetHealth(a, slot));
            Assert.Equal(0, ada.ContainerDesyncs + ada.BadPackets);
        }
    }
}
