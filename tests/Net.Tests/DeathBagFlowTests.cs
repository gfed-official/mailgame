using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The death bag (doc 01 §2.2) as the players see it over the loopback transport: where it lies,
    /// whose it is, how long it has, and what the prompt, the panel title and the feed say of it.
    /// </summary>
    public class DeathBagFlowTests
    {
        private const double Dt = 1.0 / 30.0;

        private sealed class Seat
        {
            public ClientRuntime Client = null!;
            public InteractionController Interaction = null!;
            public readonly EventFeed Feed = new EventFeed();

            public ClientWorld World => Client.World!;
            public IEnumerable<string> FeedLines => Feed.Entries.Select(e => e.Text);
        }

        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly List<Seat> Seats = new List<Seat>();
            private uint _nextMailId = 700000;

            public Session()
            {
                Server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3, QuickStart = true }, Hub);
            }

            public SimWorld Sim => Server.World;

            public Seat Join(string name)
            {
                var seat = new Seat { Client = new ClientRuntime(Shared.Content, Hub.CreateClient(), name) };
                seat.Interaction = new InteractionController(seat.Client);
                seat.Client.EventReceived += e => seat.Feed.OnEvent(seat.Client.World!, e);
                Seats.Add(seat);
                Run(10);
                return seat;
            }

            public void Leave(Seat seat)
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

            /// <summary>One frame at 30 fps for one seat: interaction, then one tick of every end.</summary>
            public void Frame(Seat seat, bool interact)
            {
                seat.Interaction.Update(Dt, interact, false);
                Run(1);
            }

            public Player Truth(Seat seat) => Sim.GetPlayer(seat.World.LocalId)!;

            public void Teleport(Seat seat, float x, float z)
            {
                var p = Truth(seat);
                p.Move.X = x;
                p.Move.Z = z;
                Run(6);
            }

            public uint GiveLetter(Seat seat)
            {
                var kind = Shared.Content.MailKind("letter");
                var item = new MailItem
                {
                    Id = _nextMailId++, Kind = kind.Index, Address = Sim.Map.Destinations[0].Address, Value = (ushort)kind.BaseValue,
                    SpawnShift = 1, DeadlineShift = 1,
                };
                Assert.True(Sim.Give(Truth(seat).Hotbar, ItemStack.OfMail(item)));
                Run(3);
                return item.Id;
            }

            public void Fell(Seat seat)
            {
                Sim.DamagePlayer(Truth(seat), 999f, (byte)Shared.Content.Enemy("barbarian").Index);
                Run(6);
            }

            /// <summary>Stands the seat beside a bag, looking at it.</summary>
            public void WalkUpTo(Seat seat, ClientBag bag)
            {
                Teleport(seat, bag.X + 1.3f, bag.Z);
                ViewRay.Aim(seat.World.Local, bag.X, seat.World.Map.HeightAt(bag.X, bag.Z) + 0.3f, bag.Z,
                    out seat.Client.Input.YawRadians, out seat.Client.Input.PitchRadians);
            }
        }

        private static int Letters(GridContainer container) =>
            container.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Count);

        [Fact]
        public void TheGuestSeesTheHostsBag_TakesTheMailOutOfIt_AndTheBagIsGoneForBoth()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            var bo = s.Join("Bo");
            s.GiveLetter(ada);
            s.Teleport(ada, 50f, 28f);
            Assert.Empty(ada.World.Bags);
            Assert.Equal("", HudText.BagTracker(ada.World));

            s.Fell(ada);
            var truth = Assert.Single(s.Sim.Bags);
            foreach (var seat in new[] { ada, bo })
            {
                var seen = Assert.Single(seat.World.Bags.Values);
                Assert.Equal((truth.Id, ada.World.LocalId, truth.X, truth.Z, truth.DespawnTick),
                    (seen.ContainerId, seen.Owner, seen.X, seen.Z, seen.DespawnTick));
                Assert.InRange(seat.World.BagSecondsLeft(seen), 28.0, 30.0);
            }
            Assert.Equal((50f, 28f), (truth.X, truth.Z));
            Assert.Contains("Your bag lies where you fell: get to it before it is gone", ada.FeedLines);
            Assert.Contains("Ada's bag lies where they fell", bo.FeedLines);
            Assert.StartsWith("Your bag   ·   gone in 00:", HudText.BagTracker(ada.World));
            Assert.Equal("", HudText.BagTracker(bo.World));
            Assert.Equal(0, Letters(ada.World.Hotbar));

            // Down beside their own bag, the host gets no prompt: hands answer nobody who is down.
            s.Frame(ada, interact: false);
            Assert.True(ada.World.Local.Dead);
            Assert.Equal(InteractKind.None, ada.Interaction.Prompt.Kind);

            // The guest walks up. Looking away shows nothing; looking at the bag names its owner and its time.
            var bag = bo.World.Bags.Values.Single();
            s.WalkUpTo(bo, bag);
            float yaw = bo.Client.Input.YawRadians;
            bo.Client.Input.YawRadians = yaw + System.MathF.PI;
            s.Frame(bo, interact: false);
            Assert.Equal(InteractKind.None, bo.Interaction.Prompt.Kind);
            bo.Client.Input.YawRadians = yaw;
            s.Frame(bo, interact: false);
            Assert.Equal(InteractKind.OpenBag, bo.Interaction.Prompt.Kind);
            Assert.StartsWith("Open Ada's bag  [E]   ·   gone in 00:", HudText.Prompt(bo.World, bo.Interaction.Prompt, out _));

            s.Frame(bo, interact: true);
            s.Run(3);
            Assert.Equal(bag.ContainerId, bo.World.OpenContainerId);
            Assert.Equal("Ada's bag", HudText.ContainerTitle(bo.World, bo.World.OpenContainer!));
            s.Frame(bo, interact: false);
            Assert.Equal(InteractKind.CloseBag, bo.Interaction.Prompt.Kind);
            Assert.StartsWith("Close Ada's bag  [E]   ·   gone in 00:", HudText.Prompt(bo.World, bo.Interaction.Prompt, out _));

            // The letter lies in the cell it had in the hotbar; taking it empties the bag, which is then gone.
            var letter = Assert.Single(bo.World.OpenContainer!.Entries);
            Assert.NotEqual(0u, bo.Client.QuickMove(bag.ContainerId, letter.EntryId));
            Assert.True(s.RunUntil(() => bo.World.Bags.Count == 0 && ada.World.Bags.Count == 0, 2));
            Assert.Empty(s.Sim.Bags);
            Assert.Equal(0u, bo.World.OpenContainerId);
            Assert.Equal(1, Letters(bo.World.Hotbar));
            Assert.Contains("Ada's bag has been picked up", bo.FeedLines);
            Assert.Contains("Your bag has been picked up", ada.FeedLines);
            Assert.Equal("", HudText.BagTracker(ada.World));
            s.Frame(bo, interact: false);
            Assert.Equal(InteractKind.None, bo.Interaction.Prompt.Kind);
        }

        [Fact]
        public void ABagNobodyFetches_CountsDown_ThenTheMailIsBackInTheIntakeAndTheRestInItsOwnersPockets()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            uint letterId = s.GiveLetter(ada);
            var stone = Shared.Content.Item("stone");
            Assert.True(s.Sim.Give(s.Truth(ada).Inventory, ItemStack.OfItem(stone, 3)));
            s.Teleport(ada, 50f, 28f);
            s.Fell(ada);
            var bag = Assert.Single(ada.World.Bags.Values);
            Assert.Same(bag, ada.World.LocalBag);
            Assert.Equal(0, ada.World.Inventory.EntryCount);

            // The countdown runs while they are down and after they are back on their feet.
            double before = ada.World.BagSecondsLeft(bag);
            Assert.True(s.RunUntil(() => !ada.World.Local.Dead, 12));
            double after = ada.World.BagSecondsLeft(bag);
            Assert.InRange(before - after, 9.0, 11.0);
            Assert.StartsWith("Your bag   ·   gone in 00:", HudText.BagTracker(ada.World));

            Assert.True(s.RunUntil(() => ada.World.Bags.Count == 0, 25));
            Assert.Contains("Your bag is gone: 1 item of mail went back to the Intake", ada.FeedLines);
            Assert.True(ada.Feed.Entries.Single(e => e.Text.StartsWith("Your bag is gone")).Bad);
            Assert.Equal("", HudText.BagTracker(ada.World));
            Assert.Contains(s.Sim.Intake.Entries, e => e.Stack.Kind == StackKind.Mail && e.Stack.Mail.Any(m => m.Id == letterId));
            s.Run(3);
            var back = Assert.Single(ada.World.Inventory.Entries);
            Assert.Equal((StackKind.Item, stone.Index, 3), (back.Stack.Kind, back.Stack.DefIndex, back.Stack.Count));
            Assert.Equal(0, Letters(ada.World.Hotbar));
        }

        [Fact]
        public void WhoJoinsLaterSeesTheBagAlreadyLying_AndOnceItsOwnerHasLeftItIsNobodys()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            s.GiveLetter(ada);
            s.Teleport(ada, 50f, 28f);
            s.Fell(ada);
            var truth = Assert.Single(s.Sim.Bags);
            EntityId owner = ada.World.LocalId;

            var bo = s.Join("Bo");
            var seen = Assert.Single(bo.World.Bags.Values);
            Assert.Equal((truth.Id, owner, truth.X, truth.Z, truth.DespawnTick), (seen.ContainerId, seen.Owner, seen.X, seen.Z, seen.DespawnTick));
            Assert.Equal("Ada's bag", HudText.BagName(bo.World, seen.Owner));
            Assert.Equal("", HudText.BagTracker(bo.World));

            s.Leave(ada);
            Assert.Equal(EntityId.None, seen.Owner);
            Assert.Equal(EntityId.None, Assert.Single(s.Sim.Bags).Owner);
            Assert.Equal("an unclaimed bag", HudText.BagName(bo.World, seen.Owner));

            // Whoever takes the seat is not told the bag is theirs.
            var cy = s.Join("Cy");
            Assert.Equal(owner, cy.World.LocalId);
            Assert.Equal(EntityId.None, Assert.Single(cy.World.Bags.Values).Owner);
            Assert.Null(cy.World.LocalBag);
            Assert.Equal("", HudText.BagTracker(cy.World));

            s.WalkUpTo(bo, seen);
            s.Frame(bo, interact: false);
            Assert.Equal(InteractKind.OpenBag, bo.Interaction.Prompt.Kind);
            Assert.StartsWith("Open an unclaimed bag  [E]", HudText.Prompt(bo.World, bo.Interaction.Prompt, out _));
            s.Frame(bo, interact: true);
            s.Run(3);
            Assert.Equal(seen.ContainerId, bo.World.OpenContainerId);
            Assert.Equal("An unclaimed bag", HudText.ContainerTitle(bo.World, bo.World.OpenContainer!));

            // Nobody takes it: the mail goes back, and nobody is told their pockets were filled.
            Assert.True(s.RunUntil(() => bo.World.Bags.Count == 0, 32));
            Assert.Equal(0u, bo.World.OpenContainerId);
            Assert.Contains("An unclaimed bag is gone: 1 item of mail went back to the Intake", bo.FeedLines);
        }
    }
}
