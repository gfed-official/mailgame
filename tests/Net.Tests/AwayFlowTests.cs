using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Net;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The disconnect grace (doc 06 §6) over the loopback transport: who loses their connection
    /// during a run stands away for two minutes, and the same account is the same player again.
    /// </summary>
    public class AwayFlowTests
    {
        private const ulong AdaAccount = 0xADA, BoAccount = 0xB0;

        private sealed class Seat
        {
            public ClientRuntime Client = null!;
            public readonly EventFeed Feed = new EventFeed();

            public ClientWorld World => Client.World!;
            public IEnumerable<string> FeedLines => Feed.Entries.Select(e => e.Text);
        }

        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly List<Seat> Seats = new List<Seat>();
            public readonly List<string> Log = new List<string>();
            private uint _nextMailId = 710000;

            public Session(bool quickStart = true)
            {
                Server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3, QuickStart = quickStart, Draft = false }, Hub) { Log = Log.Add };
            }

            public SimWorld Sim => Server.World;

            public Seat Join(string name, ulong account, PlaytestFlags playtest = PlaytestFlags.None, int startShift = 0)
            {
                var seat = new Seat { Client = new ClientRuntime(Shared.Content, Hub.CreateClient(), name, playtest, startShift, account) };
                seat.Client.EventReceived += e => seat.Feed.OnEvent(seat.Client.World!, e);
                Seats.Add(seat);
                Run(10);
                return seat;
            }

            /// <summary>The seat's connection is gone: nothing says goodbye.</summary>
            public void Drop(Seat seat)
            {
                seat.Client.Disconnect();
                Seats.Remove(seat);
                Run(10);
            }

            public void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    foreach (var seat in Seats) seat.Client.Tick();
                    Server.Tick();
                }
            }

            public bool RunUntil(System.Func<bool> done, int maxSeconds)
            {
                for (int i = 0; i < maxSeconds * 30 && !done(); i++) Run(1);
                return done();
            }

            public Player Truth(EntityId id) => Sim.GetPlayer(id)!;

            public void Teleport(EntityId id, float x, float z)
            {
                var p = Truth(id);
                p.Move.X = x;
                p.Move.Z = z;
                Run(6);
            }

            public uint GiveLetter(EntityId id)
            {
                var kind = Shared.Content.MailKind("letter");
                var item = new MailItem
                {
                    Id = _nextMailId++, Kind = kind.Index, Address = Sim.Map.Destinations[0].Address, Value = (ushort)kind.BaseValue,
                    SpawnShift = 1, DeadlineShift = 1,
                };
                Assert.True(Sim.Give(Truth(id).Hotbar, ItemStack.OfMail(item)));
                Run(3);
                return item.Id;
            }

            /// <summary>The seat puts one letter worth the whole quota into its mailbox.</summary>
            public void MeetTheQuota(Seat seat)
            {
                var home = Sim.Map.Destinations[0];
                var kind = Shared.Content.MailKind("letter");
                var item = new MailItem
                {
                    Id = _nextMailId++, Kind = kind.Index, Address = home.Address, Value = (ushort)Sim.Run.Quota,
                    SpawnShift = (byte)Sim.Run.Shift, DeadlineShift = (byte)Sim.Run.Shift,
                };
                var truth = Truth(seat.World.LocalId);
                Assert.True(Sim.Give(truth.Hotbar, ItemStack.OfMail(item)));
                Teleport(truth.Id, home.MailboxX, home.MailboxZ);
                Run(15);
                var entry = truth.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
                Assert.NotEqual(0u, seat.Client.Deliver(truth.Hotbar.Id, entry.EntryId, home.Id));
                Run(10);
                Assert.True(Sim.Economy.ShiftEarnings >= Sim.Run.Quota);
            }

            /// <summary>Lobby → Prep → Delivery with everybody's go key.</summary>
            public void StartDelivery()
            {
                var host = Seats.Single(s => s.World.IsHost);
                Assert.NotEqual(0u, host.Client.PressGo());
                Run(10);
                foreach (var seat in Seats) Assert.NotEqual(0u, seat.Client.PressGo());
                Run(10);
                Assert.Equal(RunPhase.Delivery, Sim.Run.Phase);
            }
        }

        /// <summary>A connection whose Welcome is overtaken: it arrives after what was sent behind it, once it is let go.</summary>
        private sealed class LateWelcome : IClientTransport, IClientTransportHandler
        {
            private readonly IClientTransport _inner;
            private IClientTransportHandler _handler = null!;
            private byte[]? _welcome;
            private bool _held;
            public bool Hold = true;
            public int Overtaken;

            public LateWelcome(IClientTransport inner) { _inner = inner; }

            public void Poll(IClientTransportHandler handler)
            {
                _handler = handler;
                _inner.Poll(this);
                if (_welcome == null || Hold) return;
                handler.OnData(NetChannel.Bulk, _welcome);
                _welcome = null;
            }

            public void Send(NetChannel channel, byte[] data) => _inner.Send(channel, data);
            public void Close() => _inner.Close();
            public void OnConnected() => _handler.OnConnected();
            public void OnDisconnected() => _handler.OnDisconnected();

            public void OnData(NetChannel channel, byte[] data)
            {
                if ((MsgType)data[0] == MsgType.Welcome && !_held)
                {
                    _welcome = data;
                    _held = true;
                    return;
                }
                if (_welcome != null && (MsgType)data[0] == MsgType.Events) Overtaken++;
                _handler.OnData(channel, data);
            }
        }

        [Fact]
        public void WhatOvertakesTheWelcomeIsNotLost()
        {
            var s = new Session();
            var ada = s.Join("Ada", AdaAccount);
            var bo = s.Join("Bo", BoAccount);
            EntityId id = bo.World.LocalId;
            s.Drop(bo);

            var transport = new LateWelcome(s.Hub.CreateClient());
            var back = new Seat { Client = new ClientRuntime(Shared.Content, transport, "Bo", account: BoAccount) };
            back.Client.EventReceived += e => back.Feed.OnEvent(back.Client.World!, e);
            s.Seats.Add(back);
            s.Run(2);
            Assert.Equal(ClientState.Joining, back.Client.State);
            // Told while the Welcome is still under way: loot falls, and a letter lands in their pocket.
            var giant = s.Sim.SpawnEnemy(Shared.Content.Enemy("giant"), 90f, 28f)!;
            s.Sim.DamageEnemy(giant, 100000f, null);
            uint letter = s.GiveLetter(id);
            Assert.Equal(ClientState.Joining, back.Client.State);
            Assert.True(transport.Overtaken > 0);

            transport.Hold = false;
            s.Run(10);
            Assert.Equal(ClientState.Joined, back.Client.State);
            Assert.Equal(id, back.World.LocalId);
            Assert.Single(back.World.Loot);
            Assert.True(HasMail(back.World.Hotbar, letter));
            Assert.Equal(s.Truth(id).Hotbar.ComputeHash(), back.World.Hotbar.ComputeHash());
            Assert.False(ada.World.Players[id].Away);
            Assert.Equal(0, back.Client.BadPackets + back.Client.ContainerDesyncs);
        }

        private static bool HasMail(GridContainer container, uint mailId) =>
            container.Entries.Any(e => e.Stack.Kind == StackKind.Mail && e.Stack.Mail.Any(m => m.Id == mailId));

        [Fact]
        public void AGuestWhoDropsMidShift_StandsAwayOnTheHostsScreen_AndComesBackAsThemself()
        {
            var s = new Session();
            var ada = s.Join("Ada", AdaAccount);
            var bo = s.Join("Bo", BoAccount);
            Assert.Equal(RunPhase.Delivery, s.Sim.Run.Phase);
            EntityId id = bo.World.LocalId;
            Assert.Equal("Player#2", id.ToString());
            s.Teleport(id, 50f, 28f);
            uint letter = s.GiveLetter(id);
            Assert.True(HasMail(bo.World.Hotbar, letter));
            var seen = ada.World.Players[id];
            Assert.Equal("Bo", HudText.NameTag(seen));

            s.Drop(bo);
            Assert.Equal(2, s.Sim.Players.Count);
            Assert.True(s.Truth(id).Away);
            Assert.Contains("Bo lost the connection: 120 s to come back as Player#2", s.Log);
            Assert.True(seen.Away);
            Assert.Same(seen, ada.World.Players[id]);
            Assert.Equal("Bo (away)", HudText.NameTag(seen));
            Assert.Equal(1, ada.World.PresentCount);
            Assert.Contains("Bo lost the connection — 02:00 to come back", ada.FeedLines);
            Assert.DoesNotContain("Bo left", ada.FeedLines);

            // Half a minute later, from a new connection, under whatever name: the account is what counts.
            s.Run(30 * 30);
            var back = s.Join("Bo again", BoAccount);
            Assert.Equal(ClientState.Joined, back.Client.State);
            Assert.Equal(id, back.World.LocalId);
            Assert.Equal(2, s.Sim.Players.Count);
            Assert.False(s.Truth(id).Away);
            Assert.Contains("Bo is back as Player#2", s.Log);
            Assert.Equal("Bo", back.World.Players[id].Name);
            Assert.True(HasMail(back.World.Hotbar, letter));
            Assert.Equal(s.Truth(id).Hotbar.ComputeHash(), back.World.Hotbar.ComputeHash());
            Assert.InRange(System.MathF.Abs(back.World.Local.X - 50f) + System.MathF.Abs(back.World.Local.Z - 28f), 0f, 0.05f);
            Assert.Equal(RunPhase.Delivery, back.World.Global.Phase);

            Assert.Same(seen, ada.World.Players[id]);
            Assert.False(seen.Away);
            Assert.Equal("Bo", HudText.NameTag(seen));
            Assert.Equal(2, ada.World.PresentCount);
            Assert.Contains("Bo is back", ada.FeedLines);
            Assert.DoesNotContain("Bo is back", back.FeedLines);
            Assert.DoesNotContain(ada.FeedLines, line => line.Contains("joined") && line.Contains("again"));

            // They walk on from where they stood, and the server and their screen agree on where to.
            back.Client.Input.MoveX = 1f;
            s.Run(30);
            back.Client.Input.MoveX = 0f;
            s.Run(30);
            var truth = s.Truth(id);
            Assert.True(truth.Move.X > 52f);
            Assert.InRange(System.MathF.Abs(back.World.Local.X - truth.Move.X) + System.MathF.Abs(back.World.Local.Z - truth.Move.Z), 0f, 0.05f);
            Assert.Equal(0, ada.Client.BadPackets + back.Client.BadPackets + ada.Client.ContainerDesyncs + back.Client.ContainerDesyncs);
        }

        [Fact]
        public void TheSeatIsOnlyForItsAccount_AStrangerIsStillTurnedAwayMidShift()
        {
            var s = new Session(quickStart: false);
            var ada = s.Join("Ada", AdaAccount);
            var bo = s.Join("Bo", BoAccount);
            s.StartDelivery();
            EntityId id = bo.World.LocalId;
            s.Drop(bo);
            Assert.True(s.Truth(id).Away);

            var stranger = s.Join("Cy", 0xC1);
            Assert.Equal(ClientState.Rejected, stranger.Client.State);
            Assert.Equal(RejectReason.RunInProgress, stranger.Client.RejectReason);
            s.Seats.Remove(stranger);
            // No account is nobody's account.
            var nameless = s.Join("Bo", 0);
            Assert.Equal(RejectReason.RunInProgress, nameless.Client.RejectReason);
            s.Seats.Remove(nameless);
            // The host's account is not away.
            var twin = s.Join("Ada", AdaAccount);
            Assert.Equal(RejectReason.RunInProgress, twin.Client.RejectReason);
            s.Seats.Remove(twin);
            Assert.Equal(2, s.Sim.Players.Count);
            Assert.True(s.Truth(id).Away);

            var back = s.Join("Bo", BoAccount);
            Assert.Equal(ClientState.Joined, back.Client.State);
            Assert.Equal(id, back.World.LocalId);
            Assert.Equal(RunPhase.Delivery, back.World.Global.Phase);
            Assert.False(ada.World.Players[id].Away);
            // Once they are back the account opens nothing more.
            var second = s.Join("Bo", BoAccount);
            Assert.Equal(RejectReason.RunInProgress, second.Client.RejectReason);
        }

        [Fact]
        public void NotBackInTwoMinutes_TheHostReadsThatTheyDidNotComeBack_AndTheirPocketsAreABag()
        {
            var s = new Session();
            var ada = s.Join("Ada", AdaAccount);
            var bo = s.Join("Bo", BoAccount);
            EntityId id = bo.World.LocalId;
            s.Teleport(id, 50f, 28f);
            s.GiveLetter(id);
            s.Drop(bo);

            s.Run(119 * 30);
            Assert.True(ada.World.Players[id].Away);
            Assert.Empty(ada.World.Bags);
            s.Run(30);
            Assert.False(ada.World.Players.ContainsKey(id));
            Assert.Single(s.Sim.Players);
            Assert.Contains("Bo's bag lies where they stood", ada.FeedLines);
            Assert.Contains("Bo did not come back", ada.FeedLines);
            Assert.DoesNotContain("Bo left", ada.FeedLines);
            Assert.Contains("Bo did not come back", s.Log);
            var bag = Assert.Single(ada.World.Bags.Values);
            Assert.Equal((50f, 28f, EntityId.None), (bag.X, bag.Z, bag.Owner));
            Assert.Equal("an unclaimed bag", HudText.BagName(ada.World, bag.Owner));

            // The account is a stranger's now: on this server, which lets anybody in, they are a new player with empty pockets.
            var late = s.Join("Bo", BoAccount);
            Assert.Equal(ClientState.Joined, late.Client.State);
            Assert.DoesNotContain(late.World.Hotbar.Entries, e => e.Stack.Kind == StackKind.Mail);
            Assert.Contains("Bo joined", ada.FeedLines);
        }

        [Fact]
        public void InTheLobbyALostConnectionIsALeave()
        {
            var s = new Session(quickStart: false);
            var ada = s.Join("Ada", AdaAccount);
            var bo = s.Join("Bo", BoAccount);
            EntityId id = bo.World.LocalId;
            s.Drop(bo);
            Assert.Single(s.Sim.Players);
            Assert.False(ada.World.Players.ContainsKey(id));
            Assert.Contains("Bo left", ada.FeedLines);
            Assert.Contains("Bo left", s.Log);
            Assert.DoesNotContain(ada.FeedLines, line => line.Contains("lost the connection"));
        }

        [Fact]
        public void PrepDoesNotWaitForWhoIsAway_AndTheHintCountsWhoIsHere()
        {
            var s = new Session(quickStart: false);
            var ada = s.Join("Ada", AdaAccount);
            var bo = s.Join("Bo", BoAccount);
            var cy = s.Join("Cy", 0xC1);
            s.StartDelivery();
            EntityId id = cy.World.LocalId;
            // The shift is over before the grace is: Cy is still away in the Payday and the Prep.
            s.MeetTheQuota(ada);
            Assert.True(s.RunUntil(() => s.Sim.Run.Phase == RunPhase.Delivery && s.Sim.Run.PhaseEndTick - s.Sim.Tick < 30 * 30, 241));
            s.Drop(cy);
            Assert.True(s.RunUntil(() => s.Sim.Run.Phase == RunPhase.Prep, 60));
            s.Run(10);
            Assert.True(ada.World.Players[id].Away);
            Assert.Equal(2, ada.World.PresentCount);
            Assert.Equal(QuotaFormula.Quota(Shared.Content.Balance, 2, 2), ada.World.Global.Quota);

            ada.Client.PressGo();
            s.Run(10);
            Assert.Equal(RunPhase.Prep, s.Sim.Run.Phase);
            Assert.StartsWith("Ready 1 / 2 ", HudText.PhaseHint(ada.World));
            bo.Client.PressGo();
            s.Run(10);
            Assert.Equal(RunPhase.Delivery, s.Sim.Run.Phase);
            Assert.Equal(2, s.Sim.Run.PlayerCount);
        }

        [Fact]
        public void WhoComesBackIntoARaid_KnowsOfItFromTheWelcome()
        {
            var s = new Session();
            var ada = s.Join("Ada", AdaAccount, PlaytestFlags.RaidNow, startShift: 2);
            var bo = s.Join("Bo", BoAccount);
            EntityId id = bo.World.LocalId;
            s.Teleport(ada.World.LocalId, 20f, 28f);
            s.Teleport(id, 30f, 28f);
            // Bo is down when the connection goes; a raider's loot falls while they are away.
            s.Sim.DamagePlayer(s.Truth(id), 999f, (byte)Shared.Content.Enemy("barbarian").Index);
            s.Run(6);
            Assert.True(bo.World.Local.Dead);
            s.Drop(bo);
            Assert.True(s.RunUntil(() => s.Sim.Raid != RaidState.None, 10));
            var giant = s.Sim.SpawnEnemy(Shared.Content.Enemy("giant"), 90f, 28f)!;
            s.Sim.DamageEnemy(giant, 100000f, null);
            var bundle = Assert.Single(s.Sim.Loot);
            s.Run(5);

            var back = s.Join("Bo", BoAccount);
            Assert.Equal(id, back.World.LocalId);
            Assert.Equal(s.Sim.Raid, back.World.Raid);
            Assert.NotEqual(RaidState.None, back.World.Raid);
            Assert.Equal(s.Sim.RaidTick, back.World.RaidTick);
            var loot = Assert.Single(back.World.Loot.Values);
            Assert.Equal((bundle.X, bundle.Z, bundle.Coins), (loot.X, loot.Z, (int)loot.Coins));
            // Still down, with the time to standing up that the server has.
            var truth = s.Truth(id);
            if (truth.Dead)
            {
                Assert.True(back.World.Local.Dead);
                Assert.Equal(truth.RespawnTick, back.World.LocalRespawnTick);
                Assert.InRange(back.World.RespawnSecondsLeft, 0.1, 10.0);
            }
            Assert.True(truth.Dead, "the respawn came before the reconnect: the test checks nothing");

            // On their feet they are protected like anybody, and the screen knows until when.
            Assert.True(s.RunUntil(() => !s.Truth(id).Dead, 11));
            s.Run(5);
            Assert.False(back.World.Local.Dead);
            Assert.Equal(truth.ProtectedUntilTick, back.World.LocalProtectedUntilTick);
        }

        [Fact]
        public void BackOnProtectedFeet_TheWelcomeSaysUntilWhen()
        {
            var s = new Session();
            var ada = s.Join("Ada", AdaAccount);
            var bo = s.Join("Bo", BoAccount);
            EntityId id = bo.World.LocalId;
            s.Sim.DamagePlayer(s.Truth(id), 999f, (byte)Shared.Content.Enemy("barbarian").Index);
            s.Run(6);
            s.Drop(bo);
            // They stand up at the pad while away, and their five seconds start.
            Assert.True(s.RunUntil(() => !s.Truth(id).Dead, 11));
            var truth = s.Truth(id);
            Assert.True(truth.Away);
            s.Run(30);

            var back = s.Join("Bo", BoAccount);
            Assert.False(back.World.Local.Dead);
            Assert.Equal(truth.ProtectedUntilTick, back.World.LocalProtectedUntilTick);
            Assert.True(back.World.LocalProtected);
            Assert.InRange(back.World.ProtectedSecondsLeft, 2.0, 4.5);
            Assert.NotNull(ada.World);
        }

        [Fact]
        public void BackInThePayday_TheyGetTheSummaryTheyMissed()
        {
            var s = new Session(quickStart: false);
            var ada = s.Join("Ada", AdaAccount);
            var bo = s.Join("Bo", BoAccount);
            s.StartDelivery();
            EntityId id = bo.World.LocalId;
            s.MeetTheQuota(ada);
            Assert.True(s.RunUntil(() => s.Sim.Run.Phase == RunPhase.Delivery && s.Sim.Run.PhaseEndTick - s.Sim.Tick < 10 * 30, 241));
            s.Drop(bo);
            Assert.True(s.RunUntil(() => s.Sim.Run.Phase == RunPhase.Payday, 11));
            s.Run(30);

            var back = s.Join("Bo", BoAccount);
            Assert.Equal(id, back.World.LocalId);
            Assert.Equal(RunPhase.Payday, back.World.Global.Phase);
            Assert.NotNull(back.World.LastSummary);
            Assert.Equal((1, true), (back.World.LastSummary!.Shift, back.World.LastSummary.QuotaMet));
            Assert.Contains(back.FeedLines, line => line.StartsWith("Shift 1 is over: quota met"));
            // Nobody else is told twice.
            Assert.Single(ada.FeedLines, line => line.StartsWith("Shift 1 is over"));
        }
    }
}
