using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// Playtest nits 10, 13 and 18 as the players see them: the flags travel in the Hello, so a guest
    /// asks for them the same way the host's own client does.
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

            public ClientRuntime AddClient(string name, PlaytestFlags playtest = PlaytestFlags.None, int startShift = 0)
            {
                var client = new ClientRuntime(Shared.Content, Hub.CreateClient(), name, playtest, startShift);
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
            var guest = s.AddClient("Bo", PlaytestFlags.NoTimer | PlaytestFlags.AtMailbox);
            var hand = new InteractionController(guest);
            s.Step(10);
            var world = guest.World!;
            var server = s.Server.World;
            var truth = server.GetPlayer(world.LocalId)!;

            Assert.Equal(PlaytestFlags.None, server.GetPlayer(host.World!.LocalId)!.Playtest);
            Assert.Equal(PlaytestFlags.NoTimer | PlaytestFlags.AtMailbox, truth.Playtest);
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
        public void AGuestWithShiftNoQuotaAndRaid_StartsTheRunAtThatShift_RaidedAtOnce_AndTheMissedQuotaEndsNothing()
        {
            var s = new Session();
            var host = s.AddClient("Ada");
            var guest = s.AddClient("Bo", PlaytestFlags.NoQuota | PlaytestFlags.RaidNow, 2);
            s.Step(10);
            var world = guest.World!;
            var server = s.Server.World;
            var truth = server.GetPlayer(world.LocalId)!;
            Assert.Equal((PlaytestFlags.NoQuota | PlaytestFlags.RaidNow, (byte)2), (truth.Playtest, truth.PlaytestShift));
            Assert.Contains(s.Log, line => line.StartsWith("Bo joined as ", StringComparison.Ordinal) && line.EndsWith("(playtest: NoQuota, RaidNow, shift 2)", StringComparison.Ordinal));
            Assert.True(world.QuotaWaived && host.World!.QuotaWaived);
            Assert.False(world.Global.ClockStopped);

            // The host starts the run: it is shift 2 for both, with shift 2's quota for two.
            Assert.NotEqual(0u, host.PressGo());
            s.Step(5);
            Assert.StartsWith("Shift 2 / 5   PREP 01:", HudText.ShiftAndPhase(world));
            Assert.StartsWith("Shift 2 / 5   PREP 01:", HudText.ShiftAndPhase(host.World));
            Assert.Equal(QuotaFormula.Quota(Shared.Content.Balance, 2, 2), world.Global.Quota);
            host.PressGo();
            guest.PressGo();
            s.Step(10);
            Assert.Equal((RunPhase.Delivery, (byte)2), (world.Global.Phase, world.Global.Shift));

            // The raid is announced as the shift starts, on both screens, and is there five seconds in.
            Assert.Equal(RaidState.Warning, world.Raid);
            Assert.Equal(RaidState.Warning, host.World.Raid);
            Assert.StartsWith(HudText.RaidWarning + "   ·   00:0", HudText.RaidBanner(world));
            Assert.StartsWith("Raid in 00:0", HudText.RaidTracker(host.World));
            Assert.InRange(world.RaidSecondsLeft, 4.0, 5.0);
            s.Step(5 * 30);
            Assert.Equal(RaidState.Active, world.Raid);
            Assert.Equal(RaidState.Active, host.World.Raid);
            Assert.NotEmpty(world.Enemies);
            Assert.Equal(server.Enemies.Count, host.World.Enemies.Count);
            Assert.Equal(HudText.RaidTracker(world), HudText.RaidTracker(host.World));
            Assert.StartsWith("Raid   ·   ", HudText.RaidTracker(world));
            // The clock of the shift runs as ever.
            Assert.DoesNotContain(HudText.ClockStoppedTag, HudText.ShiftAndPhase(world));
            Assert.StartsWith("Shift 2 / 5   DELIVERY 04:2", HudText.ShiftAndPhase(world));

            // Nobody delivers: the quota is missed, and the run goes on to shift 3 all the same.
            while (server.Run.Phase == RunPhase.Delivery) s.Step(30);
            s.Step(10);
            Assert.Equal(RunPhase.Payday, world.Global.Phase);
            Assert.NotNull(world.LastSummary);
            Assert.False(world.LastSummary!.QuotaMet);
            Assert.StartsWith($"Quota missed — {HudText.QuotaWaivedTag}   ·   shift 3 in 00:", HudText.PhaseHint(world));
            Assert.Equal(HudText.PhaseHint(world), HudText.PhaseHint(host.World));
            while (server.Run.Phase == RunPhase.Payday) s.Step(30);
            s.Step(10);
            Assert.Equal((RunPhase.Prep, (byte)3, RunOutcome.None), (world.Global.Phase, world.Global.Shift, world.Global.Outcome));
            Assert.StartsWith("Shift 3 / 5   PREP ", HudText.ShiftAndPhase(host.World));
            Assert.Equal(RaidState.None, world.Raid);
            Assert.Empty(world.Enemies);

            // The one who asked leaves: the next missed quota ends the run, and no raid opens shift 3.
            guest.Disconnect();
            s.Clients.Remove(guest);
            s.Step(15);
            Assert.False(host.World.QuotaWaived);
            host.PressGo();
            s.Step(10);
            Assert.Equal(RunPhase.Delivery, host.World.Global.Phase);
            Assert.Equal(RaidState.None, host.World.Raid);
            Assert.Equal(server.Run.PhaseEndTick - 90u * 30u, server.RaidStartTick);
            while (server.Run.Phase == RunPhase.Delivery) s.Step(30);
            s.Step(10);
            Assert.StartsWith("Quota missed — results in 00:", HudText.PhaseHint(host.World));
            while (server.Run.Phase == RunPhase.Payday) s.Step(30);
            s.Step(10);
            Assert.Equal("RESULTS   RUN OVER", HudText.ShiftAndPhase(host.World));
            Assert.Equal(0, host.ContainerDesyncs + host.BadPackets + guest.BadPackets);
        }

        [Fact]
        public void AGuestWithPackagesAndRaid_HasTheCannonLoadedWhenTheRaidersCome_AndFiresItThroughItsOwnInput()
        {
            // Nit 18: the mail cannon can be tried in a raid without a trip to the Intake first.
            var s = new Session();
            var host = s.AddClient("Ada");
            var guest = s.AddClient("Bo", PlaytestFlags.Packages | PlaytestFlags.RaidNow);
            s.Step(10);
            var world = guest.World!;
            var server = s.Server.World;
            var truth = server.GetPlayer(world.LocalId)!;
            Assert.Contains(s.Log, line => line.StartsWith("Bo joined as ", StringComparison.Ordinal) && line.EndsWith("(playtest: RaidNow, Packages)", StringComparison.Ordinal));
            Assert.Equal((0, 0), (guest.CannonAmmo, host.CannonAmmo));

            host.PressGo();
            s.Step(5);
            host.PressGo();
            guest.PressGo();
            s.Step(10);
            Assert.Equal(RunPhase.Delivery, world.Global.Phase);
            Assert.Equal((PlaytestMask.PackageCount, 0), (guest.CannonAmmo, host.World!.Hotbar.EntryCount + host.World.Inventory.EntryCount));
            Assert.Equal(RaidState.Warning, world.Raid);
            Assert.Equal("[LMB] hammer   ·   [RMB] mail cannon x4", HudText.Weapons(guest.CannonAmmo));
            Assert.Equal("[LMB] hammer   ·   [RMB] mail cannon: no Small Package", HudText.Weapons(host.CannonAmmo));

            // The raiders are on the street; one package down it, by the guest's own trigger.
            s.Step(5 * 30);
            while (server.Enemies.Count == 0) s.Step(1);
            var raider = server.Enemies[0];
            truth.Move.X = raider.X - 8f;
            truth.Move.Z = raider.Z;
            s.Step(10);
            float hp = raider.Hp;
            guest.Input.YawRadians = -MathF.PI / 2f;
            guest.Input.Buttons = InputButtons.AltAttack;
            s.Step(2);
            guest.Input.Buttons = InputButtons.None;
            s.Step(30);
            Assert.Equal(1, guest.LocalCannonShots);
            Assert.Equal(PlaytestMask.PackageCount - 1, guest.CannonAmmo);
            Assert.True(raider.Hp < hp || !server.Enemies.Contains(raider), "the guest's package missed a raider eight metres down the street");
            Assert.Equal(0, guest.ContainerDesyncs + guest.BadPackets + host.BadPackets);
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
