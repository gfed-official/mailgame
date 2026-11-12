using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Client.Bot;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Net;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>The run / shift loop of doc 01 as clients see it over the loopback transport.</summary>
    public class RunFlowTests
    {
        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly List<ClientRuntime> Clients = new List<ClientRuntime>();
            public readonly List<DeliveryBot> Bots = new List<DeliveryBot>();

            public Session(uint seed = 3)
            {
                Server = new ServerRuntime(Shared.Content, new SimSettings { Seed = seed }, Hub);
            }

            public RunState Run => Server.World.Run;

            public ClientRuntime AddClient(string name, bool bot = false)
            {
                var client = new ClientRuntime(Shared.Content, Hub.CreateClient(), name);
                Clients.Add(client);
                if (bot) Bots.Add(new DeliveryBot(client));
                return client;
            }

            public void Step(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    foreach (var b in Bots) b.Tick();
                    foreach (var c in Clients) c.Tick();
                    Server.Tick();
                }
            }

            public bool StepUntil(System.Func<bool> done, int maxSeconds)
            {
                for (int i = 0; i < maxSeconds * 30 && !done(); i++) Step(1);
                return done();
            }
        }

        [Fact]
        public void HostStartsTheRun_AndReadyEndsPrep()
        {
            var s = new Session();
            var host = s.AddClient("Ada");
            var guest = s.AddClient("Bo");
            s.Step(10);

            Assert.Equal(RunPhase.Lobby, host.World!.Global.Phase);
            Assert.False(host.World.PhaseHasClock);
            Assert.True(host.World.IsHost);
            Assert.False(guest.World!.IsHost);
            Assert.Equal(host.World.LocalId, guest.World.Global.Host);

            // In the Lobby the key readies a guest and starts the run for the host.
            Assert.NotEqual(0u, guest.PressGo());
            s.Step(10);
            Assert.True(guest.World.LocalReady);
            Assert.Equal(1, host.World.ReadyCount);
            Assert.Equal(RunPhase.Lobby, s.Run.Phase);

            Assert.NotEqual(0u, host.PressGo());
            s.Step(10);
            Assert.Equal(RunPhase.Prep, guest.World.Global.Phase);
            Assert.True(guest.World.PhaseHasClock);
            Assert.InRange(guest.World.PhaseSecondsLeft, 58.0, 60.0);
            Assert.Equal(0, host.World.ReadyCount);
            Assert.False(guest.World.LocalReady);

            host.PressGo();
            s.Step(10);
            Assert.Equal(1, guest.World.ReadyCount);
            Assert.Equal(RunPhase.Prep, s.Run.Phase);
            guest.PressGo();
            s.Step(10);
            Assert.Equal(RunPhase.Delivery, host.World.Global.Phase);
            Assert.Equal(RunPhase.Delivery, guest.World.Global.Phase);
            Assert.Equal(QuotaFormula.Quota(Shared.Content.Balance, 1, 2), guest.World.Global.Quota);
            Assert.Equal(0, guest.World.ReadyCount);

            // The key does nothing during Delivery.
            Assert.Equal(0u, host.PressGo());
        }

        [Fact]
        public void HudFollowsTheRun_FromLobbyToResults()
        {
            var s = new Session();
            var client = s.AddClient("Ada");
            var interaction = new InteractionController(client);
            var feed = new EventFeed();
            client.EventReceived += e => feed.OnEvent(client.World!, e);
            s.Step(10);
            var world = client.World!;
            var server = s.Server.World;
            var player = server.Players[0];

            Assert.Equal("LOBBY", HudText.ShiftAndPhase(world));
            Assert.Equal("", HudText.Quota(world.Global));
            Assert.Equal("Press [G] to start the run   ·   1 player here", HudText.PhaseHint(world));

            // A letter in hand at its own mailbox: before the shift the mailbox is closed, and holding E sends nothing.
            var dest = world.Map.Destinations[0];
            var kind = Shared.Content.MailKind("letter");
            var item = new Sim.Mail.MailItem { Id = 900001, Kind = kind.Index, Address = dest.Address, Value = (ushort)kind.BaseValue, SpawnShift = 1, DeadlineShift = 1 };
            Assert.True(server.Give(player.Hotbar, Sim.Inventory.ItemStack.OfMail(item)));
            player.Move.X = dest.MailboxX + 0.5f;
            player.Move.Z = dest.MailboxZ;
            s.Step(6);
            client.Input.HeldSlot = 1;
            var box = AimBox.Mailbox(world.Map, dest);
            ViewRay.Aim(world.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);

            void Frames(int n, bool interact)
            {
                for (int i = 0; i < n; i++)
                {
                    interaction.Update(1.0 / 30.0, interact, false);
                    s.Step(1);
                }
            }

            Frames(20, interact: true);
            Assert.True(interaction.Prompt.MailboxClosed);
            Assert.Equal(0f, interaction.Prompt.HoldProgress);
            string closed = HudText.Prompt(world, interaction.Prompt, out var tone);
            Assert.Contains(HudText.ClosedWord, closed);
            Assert.Contains(dest.Label, closed);
            Assert.Equal(PromptTone.Neutral, tone);
            Assert.Equal(0, client.PendingRequestCount);
            Assert.Equal(1, world.Hotbar.EntryCount);
            Frames(1, interact: false);

            client.PressGo();
            s.Step(10);
            Assert.StartsWith("Shift 1 / 5   PREP 01:00", HudText.ShiftAndPhase(world));
            Assert.Equal("Quota $6.00", HudText.Quota(world.Global));
            Assert.Contains("[G] ready (0 / 1)", HudText.PhaseHint(world));
            Assert.Contains(feed.Entries, e => e.Text.StartsWith("Shift 1: get ready"));

            client.PressGo();
            s.Step(10);
            Assert.StartsWith("Shift 1 / 5   DELIVERY 0", HudText.ShiftAndPhase(world));
            Assert.Equal("Quota $0.00 / $6.00", HudText.Quota(world.Global));
            Assert.Equal("", HudText.PhaseHint(world));
            Assert.Contains(feed.Entries, e => e.Text == "Shift 1 has started: deliver $6.00 of mail");

            // Now the same hold delivers.
            Frames(16, interact: true);
            Frames(10, interact: false);
            Assert.False(interaction.Prompt.MailboxClosed);
            Assert.Equal(kind.BaseValue, server.Economy.Wallet);

            Assert.True(s.StepUntil(() => world.Global.Phase == RunPhase.Payday, 241));
            var summary = HudText.SummaryLines(world, world.LastSummary!);
            Assert.Equal("SHIFT 1 — QUOTA MISSED", summary[0]);
            Assert.Equal($"Earned {HudText.Money(kind.BaseValue)} of $6.00", summary[1]);
            Assert.Equal("Delivered 1 Letter", summary[2]);
            Assert.Equal("Misdelivered 0   ·   Late 0", summary[3]);
            // 10 % of one letter rounds down to nothing, so there is no bonus line.
            Assert.Equal("Most deliveries: Ada (1)", summary[4]);
            Assert.Equal(5, summary.Count);
            Assert.StartsWith("Shift 1 / 5   PAYDAY 00:", HudText.ShiftAndPhase(world));
            Assert.StartsWith("Quota missed — results in 00:", HudText.PhaseHint(world));
            Assert.Contains(feed.Entries, e => e.Bad && e.Text.StartsWith("Shift 1 is over: quota missed"));

            Assert.True(s.StepUntil(() => world.Global.Phase == RunPhase.Results, 21));
            Assert.Equal("RESULTS   RUN OVER", HudText.ShiftAndPhase(world));
            Assert.Equal("", HudText.Quota(world.Global));
            Assert.Equal("Press [G] to return to the lobby", HudText.PhaseHint(world));
            var results = HudText.ResultsLines(world, world.Results!);
            Assert.Equal("RUN OVER — quota missed on shift 1", results[0]);
            Assert.StartsWith("Shifts completed 0 / 5   ·   Score ", results[1]);
            Assert.Equal("Delivered 1   ·   Misdelivered 0", results[2]);
            Assert.StartsWith("Ada:   1 delivered, 0 misdelivered", results[3]);
            Assert.EndsWith("+5 XP", results[3]);

            client.PressGo();
            s.Step(10);
            Assert.Equal("LOBBY", HudText.ShiftAndPhase(world));
            Assert.Null(world.Results);
            Assert.Null(world.LastSummary);
            Assert.Equal(0, world.Global.Wallet);
        }

        [Fact]
        public void GuestsSeeWhoTheyAreWaitingFor()
        {
            var s = new Session();
            s.AddClient("Ada");
            var guest = s.AddClient("Bo");
            s.Step(10);
            Assert.Equal("Waiting for Ada to start the run   ·   [G] ready", HudText.PhaseHint(guest.World!));
            guest.PressGo();
            s.Step(10);
            Assert.Equal("Ready — waiting for Ada to start the run   ·   [G] not ready", HudText.PhaseHint(guest.World!));
            Assert.Equal("Press [G] to start the run   ·   2 players here, 1 ready", HudText.PhaseHint(s.Clients[0].World!));
        }

        [Fact]
        public void JoiningMidShiftIsRejected_AndPossibleAgainInPrep()
        {
            var s = new Session();
            var host = s.AddClient("Ada");
            s.Step(5);
            host.PressGo();
            s.Step(5);
            var inPrep = s.AddClient("Bo");
            s.Step(5);
            Assert.Equal(ClientState.Joined, inPrep.State);

            host.PressGo();
            inPrep.PressGo();
            s.Step(10);
            Assert.Equal(RunPhase.Delivery, s.Run.Phase);
            var late = s.AddClient("Cy");
            s.Step(5);
            Assert.Equal(ClientState.Rejected, late.State);
            Assert.Equal(RejectReason.RunInProgress, late.RejectReason);
            Assert.Equal(2, s.Server.World.Players.Count);
        }

        [Fact]
        public void BotPlaysAWholeRun_EveryPhaseReachesTheClient()
        {
            var s = new Session();
            var client = s.AddClient("Bot", bot: true);
            var phases = new List<RunPhase>();
            var summaries = new List<ShiftSummary>();
            RunResults? results = null;
            client.EventReceived += e =>
            {
                switch (e)
                {
                    case PhaseChangedEvent p: phases.Add(p.Phase); break;
                    case ShiftSummaryEvent x: summaries.Add(x.Summary); break;
                    case RunResultsEvent x: results = x.Results; break;
                }
            };

            // The bot starts the run as host, readies up in Prep, delivers, and continues after Results.
            Assert.True(s.StepUntil(() => phases.Count(p => p == RunPhase.Lobby) == 1, 30 * 60), $"run did not finish; saw {string.Join(", ", phases)}");
            Assert.NotNull(results);
            Assert.NotEmpty(summaries);
            Assert.Equal(new[] { RunPhase.Prep, RunPhase.Delivery, RunPhase.Payday }, phases.Take(3));
            Assert.Equal(new[] { RunPhase.Results, RunPhase.Lobby }, phases.Skip(phases.Count - 2));

            var first = summaries[0];
            Assert.Equal(1, first.Shift);
            Assert.Equal(600, first.Quota);
            Assert.True(first.Deliveries >= 20, $"the bot delivered only {first.Deliveries} items in shift 1 (earned {first.Earned})");
            Assert.Equal(client.World!.LocalId, first.Mvp);
            Assert.Equal(0, s.Bots[0].RequestsRejected);

            // The results the client was sent match the server's books for the run.
            Assert.Equal(summaries.Count(x => x.QuotaMet), results!.ShiftsCompleted);
            Assert.Equal(summaries.Sum(x => x.Earned + x.Bonus), results.TotalEarned);
            Assert.Equal(summaries.Sum(x => x.Deliveries), results.Deliveries);
            var row = Assert.Single(results.Players);
            Assert.Equal(client.World.LocalId, row.Player);
            Assert.Equal(QuotaFormula.Xp(Shared.Content.Balance, results.ShiftsCompleted, results.Outcome == RunOutcome.Victory, results.Deliveries, 1), row.Xp);

            // A lone bot working flat out clears the first quota (and loses the run on a later, steeper one).
            string book = string.Join(" | ", summaries.Select(x => $"{x.Earned}/{x.Quota}"));
            Assert.True(first.QuotaMet, $"shift 1 missed: {book}");
            Assert.True(results.ShiftsCompleted >= 1, book);
            Assert.Equal(2, summaries[1].Shift);
            Assert.Equal(results.ShiftsCompleted == 5 ? RunOutcome.Victory : RunOutcome.QuotaMissed, results.Outcome);

            // Back in the Lobby everything is reset, and the bot starts the next run by itself.
            s.Step(15);
            Assert.Null(client.World.Results);
            Assert.Null(client.World.LastSummary);
            Assert.True(s.StepUntil(() => s.Run.Phase == RunPhase.Delivery, 10));
            Assert.Equal(2, s.Run.RunNumber);
            Assert.Equal(0, client.ContainerDesyncs);
            Assert.Equal(0, client.BadPackets);
        }
    }
}
