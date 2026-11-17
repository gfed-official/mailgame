using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// Playtest nit 10 as the players see it: the flags travel in the Hello, so a guest asks for
    /// them the same way the host's own client does.
    /// </summary>
    public class PlaytestFlowTests
    {
        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly List<ClientRuntime> Clients = new List<ClientRuntime>();
            public readonly List<string> Log = new List<string>();

            public Session(bool quickStart = false)
            {
                Server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3, QuickStart = quickStart }, Hub);
                Server.Log = Log.Add;
            }

            public ClientRuntime AddClient(string name, PlaytestFlags playtest = PlaytestFlags.None)
            {
                var client = new ClientRuntime(Shared.Content, Hub.CreateClient(), name, playtest);
                Clients.Add(client);
                return client;
            }

            public void Step(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    foreach (var c in Clients) c.Tick();
                    Server.Tick();
                }
            }
        }

        private static float Distance(ClientWorld world, Sim.World.DestinationDef dest)
        {
            float dx = dest.MailboxX - world.Local.X, dz = dest.MailboxZ - world.Local.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        [Fact]
        public void AGuestWithBothFlags_StandsAtItsMailboxWithMail_AndStopsTheClockForEveryone()
        {
            var s = new Session();
            var host = s.AddClient("Ada");
            var guest = s.AddClient("Bo", PlaytestMask.All);
            var hand = new InteractionController(guest);
            s.Step(10);
            var world = guest.World!;
            var server = s.Server.World;
            var truth = server.GetPlayer(world.LocalId)!;

            Assert.Equal(PlaytestFlags.None, server.GetPlayer(host.World!.LocalId)!.Playtest);
            Assert.Equal(PlaytestMask.All, truth.Playtest);
            Assert.Contains(s.Log, line => line.StartsWith("Bo joined as ", StringComparison.Ordinal) && line.EndsWith("(playtest: NoTimer, AtMailbox)", StringComparison.Ordinal));
            Assert.Contains(s.Log, line => line.StartsWith("Ada joined as ", StringComparison.Ordinal) && !line.Contains("playtest"));

            // In the Lobby: at the box of the guest's seat, looking at it, empty-handed; no clock to stop yet.
            var dest = PlaytestSpot.Mailbox(world.Map, truth.Slot);
            Assert.Same(world.Map.Destinations[1], dest);
            Assert.InRange(Distance(world, dest), 0.8f, 2f);
            Assert.InRange(MathF.Abs(world.Local.X - truth.Move.X) + MathF.Abs(world.Local.Z - truth.Move.Z), 0f, 0.01f);
            hand.Update(1.0 / 30.0, false, false);
            Assert.Equal((InteractKind.Deliver, dest, true), (hand.Prompt.Kind, hand.Prompt.Destination, hand.Prompt.MailboxClosed));
            Assert.Equal(0, world.Hotbar.EntryCount);
            Assert.True(world.Global.ClockStopped && host.World.Global.ClockStopped);
            Assert.False(world.ClockStopped);
            Assert.Equal("LOBBY", HudText.ShiftAndPhase(world));
            // The host sees the guest out on the street, not on the pad.
            Assert.True(host.World.Players[world.LocalId].Interpolate(host.World.ServerTick, out float seenX, out _, out float seenZ, out _));
            Assert.InRange(MathF.Abs(seenX - truth.Move.X) + MathF.Abs(seenZ - truth.Move.Z), 0f, 0.05f);

            // The guest walks off during Prep ...
            Assert.NotEqual(0u, host.PressGo());
            s.Step(5);
            guest.Input.MoveX = 1f;
            s.Step(60);
            guest.Input.MoveX = 0f;
            guest.Input.YawRadians += 2f;
            Assert.True(Distance(world, dest) > 3f);
            host.PressGo();
            guest.PressGo();
            s.Step(10);

            // ... and the shift puts them back: the box in view, its mail in hand, the clock standing.
            Assert.Equal(RunPhase.Delivery, world.Global.Phase);
            Assert.InRange(Distance(world, dest), 0.8f, 2f);
            hand.Update(1.0 / 30.0, false, false);
            Assert.Equal((InteractKind.Deliver, dest, false), (hand.Prompt.Kind, hand.Prompt.Destination, hand.Prompt.MailboxClosed));
            Assert.NotNull(hand.Prompt.Stack);
            Assert.True(hand.Prompt.AddressMatches && hand.Prompt.KindAccepted);
            Assert.Equal(PlaytestSpot.MailCount, hand.Prompt.Stack!.Stack.Count);
            Assert.Equal(0, host.World.Hotbar.EntryCount);

            const string clock = "Shift 1 / 5   DELIVERY 04:00 (stopped)";
            Assert.True(world.ClockStopped && host.World.ClockStopped);
            for (int i = 0; i < 20 * 30; i++)
            {
                s.Step(1);
                Assert.Equal(clock, HudText.ShiftAndPhase(world));
                Assert.Equal(clock, HudText.ShiftAndPhase(host.World));
            }
            Assert.Equal("Playtest: the shift clock is stopped   ·   [G] ends the shift", HudText.PhaseHint(host.World));
            Assert.Equal("Playtest: the shift clock is stopped   ·   Ada ends the shift", HudText.PhaseHint(world));

            // Holding the key is all it takes to deliver.
            for (int i = 0; i < 20; i++)
            {
                hand.Update(1.0 / 30.0, true, false);
                s.Step(1);
            }
            s.Step(10);
            Assert.Equal(PlaytestSpot.MailCount, truth.Deliveries);
            Assert.Equal(0, truth.Misdeliveries);
            Assert.Equal(0, world.Hotbar.EntryCount);
            Assert.True(world.Global.ShiftEarnings > 0);

            // The guest's key does nothing to the shift; the host's ends it.
            Assert.Equal(0u, guest.PressGo());
            s.Step(10);
            Assert.Equal(RunPhase.Delivery, server.Run.Phase);
            Assert.NotEqual(0u, host.PressGo());
            s.Step(10);
            Assert.Equal(RunPhase.Payday, world.Global.Phase);
            Assert.Equal(RunPhase.Payday, host.World.Global.Phase);
            Assert.StartsWith("Shift 1 / 5   PAYDAY 00:", HudText.ShiftAndPhase(world));
            Assert.Equal(0, guest.ContainerDesyncs + host.ContainerDesyncs + guest.BadPackets + host.BadPackets);
        }

        [Fact]
        public void AHostWithNoTimer_GetsItsClockBackWhenAFlaggedGuestIsNotTheOneWhoAsked()
        {
            var s = new Session();
            var host = s.AddClient("Ada", PlaytestFlags.NoTimer);
            var guest = s.AddClient("Bo");
            s.Step(10);
            host.PressGo();
            s.Step(5);
            host.PressGo();
            guest.PressGo();
            s.Step(10);
            var world = guest.World!;
            Assert.Equal(RunPhase.Delivery, world.Global.Phase);
            Assert.True(world.ClockStopped);
            // Nobody is moved or handed mail: the host asked for the clock only.
            Assert.Equal(0, world.Hotbar.EntryCount + host.World!.Hotbar.EntryCount);
            var po = world.Map.PostOffice;
            Assert.InRange(MathF.Abs(host.World.Local.X - po.SpawnPadX) + MathF.Abs(host.World.Local.Z - po.SpawnPadZ), 0f, 6f);

            s.Step(30 * 30);
            Assert.Equal("Shift 1 / 5   DELIVERY 04:00 (stopped)", HudText.ShiftAndPhase(world));

            // The one who asked leaves: the guest is the host now and the clock runs from where it stood.
            host.Disconnect();
            s.Clients.Remove(host);
            s.Step(15);
            Assert.True(world.IsHost);
            Assert.False(world.ClockStopped);
            Assert.Equal("", HudText.PhaseHint(world));
            Assert.Equal(0u, guest.PressGo());
            Assert.InRange(world.PhaseSecondsLeft, 238.0, 240.0);
            s.Step(60 * 30);
            Assert.InRange(world.PhaseSecondsLeft, 178.0, 180.0);
            Assert.StartsWith("Shift 1 / 5   DELIVERY 03:00", HudText.ShiftAndPhase(world));
        }

        [Fact]
        public void AQuickStartHostWithAMailbox_HoldsTheMailFromTheFirstFrame()
        {
            var s = new Session(quickStart: true);
            var client = s.AddClient("Ada", PlaytestFlags.AtMailbox);
            var hand = new InteractionController(client);
            s.Step(10);
            var world = client.World!;
            var dest = world.Map.Destinations[0];
            Assert.Equal(RunPhase.Delivery, world.Global.Phase);
            Assert.False(world.ClockStopped);
            hand.Update(1.0 / 30.0, false, false);
            Assert.Equal((InteractKind.Deliver, dest), (hand.Prompt.Kind, hand.Prompt.Destination));
            Assert.True(hand.Prompt.AddressMatches);
            Assert.Equal(PlaytestSpot.MailCount, world.Hotbar.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Count));
            Assert.StartsWith("Shift 1 / 5   DELIVERY 0", HudText.ShiftAndPhase(world));
            Assert.DoesNotContain(HudText.ClockStoppedTag, HudText.ShiftAndPhase(world));
        }
    }
}
