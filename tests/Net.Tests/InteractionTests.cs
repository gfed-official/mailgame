using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>The human path through the loop: InteractionController + HUD text over a real loopback session.</summary>
    public class InteractionTests
    {
        private const double Dt = 1.0 / 30.0;

        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly ClientRuntime Client;
            public readonly InteractionController Interaction;
            public readonly EventFeed Feed = new EventFeed();
            private uint _nextMailId = 100000;

            public Session()
            {
                Server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3, QuickStart = true }, Hub);
                Client = new ClientRuntime(Shared.Content, Hub.CreateClient(), "Ada");
                Interaction = new InteractionController(Client);
                Client.EventReceived += e => Feed.OnEvent(Client.World!, e);
                Client.RequestCompleted += (_, _) => Requests++;
                Run(10);
            }

            /// <summary>Requests the server has answered.</summary>
            public int Requests { get; private set; }

            public Player Player => Server.World.Players[0];
            public ClientWorld World => Client.World!;

            public void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    Client.Tick();
                    Server.Tick();
                }
            }

            /// <summary>One frame at 30 fps: interaction, then one tick of both ends.</summary>
            public void Frame(bool interact, bool deliverAll = false)
            {
                Interaction.Update(Dt, interact, deliverAll);
                Run(1);
            }

            public void Teleport(float x, float z)
            {
                Player.Move.X = x;
                Player.Move.Z = z;
                Run(6);
            }

            public void Give(GridContainer container, string kindId, DestinationDef dest, int count = 1)
            {
                var kind = Shared.Content.MailKind(kindId);
                for (int i = 0; i < count; i++)
                {
                    var item = new MailItem
                    {
                        Id = _nextMailId++, Kind = kind.Index, Address = dest.Address, Value = (ushort)kind.BaseValue,
                        SpawnShift = 1, DeadlineShift = 1,
                    };
                    Assert.True(Server.World.Give(container, ItemStack.OfMail(item)));
                }
                Run(3);
            }
        }

        [Fact]
        public void IntakeOpensAndClosesOnInteractPress()
        {
            var s = new Session();
            var po = s.World.Map.PostOffice;
            s.Teleport(30f, 27f); // out on the street
            s.Frame(interact: false);
            Assert.Equal(InteractKind.None, s.Interaction.Prompt.Kind);

            s.Teleport(po.IntakeX + 1f, po.IntakeZ);
            s.Frame(interact: false);
            Assert.Equal(InteractKind.OpenIntake, s.Interaction.Prompt.Kind);
            Assert.Equal("Open Intake  [E]", HudText.Prompt(s.World, s.Interaction.Prompt, out _));

            s.Frame(interact: true);
            s.Run(3);
            Assert.Equal(s.World.IntakeContainerId, s.World.OpenContainerId);
            Assert.NotNull(s.World.OpenContainer);

            // Still held: no second request. Release and press again closes.
            s.Frame(interact: true);
            Assert.Equal(InteractKind.CloseIntake, s.Interaction.Prompt.Kind);
            s.Run(3);
            Assert.Equal(s.World.IntakeContainerId, s.World.OpenContainerId);
            s.Frame(interact: false);
            s.Frame(interact: true);
            s.Run(3);
            Assert.Equal(0u, s.World.OpenContainerId);
        }

        [Fact]
        public void ATapShorterThanAFrameStillOpensTheIntake()
        {
            // The fa44a16 playtest: a very fast E tap is pressed and released between two frames, so
            // the key is never seen held; only the input event tells.
            var s = new Session();
            var po = s.World.Map.PostOffice;
            s.Teleport(po.IntakeX + 1f, po.IntakeZ);
            s.Frame(interact: false);
            Assert.Equal(InteractKind.OpenIntake, s.Interaction.Prompt.Kind);

            s.Interaction.Update(Dt, interactHeld: false, deliverAllPressed: false, interactTapped: true);
            s.Run(4);
            Assert.Equal(s.World.IntakeContainerId, s.World.OpenContainerId);

            // An event and the held key in the same frame are one press, not two.
            s.Interaction.Update(Dt, interactHeld: true, deliverAllPressed: false, interactTapped: true);
            s.Run(4);
            Assert.Equal(0u, s.World.OpenContainerId);
            for (int i = 0; i < 12; i++) s.Frame(interact: true);
            Assert.Equal(0u, s.World.OpenContainerId);
        }

        [Fact]
        public void ATapWhileTheLastRequestIsPendingIsNotLost()
        {
            var s = new Session();
            var po = s.World.Map.PostOffice;
            s.Teleport(po.IntakeX + 1f, po.IntakeZ);
            s.Frame(interact: true);  // open: the request is on its way
            Assert.Equal(0u, s.World.OpenContainerId);
            s.Interaction.Update(Dt, interactHeld: false, deliverAllPressed: false); // released
            s.Interaction.Update(Dt, interactHeld: true, deliverAllPressed: false);  // tapped again before the answer
            for (int i = 0; i < 6; i++) s.Frame(interact: false);
            // Both taps counted: opened, then closed again.
            Assert.Equal(0u, s.World.OpenContainerId);
            Assert.Equal(InteractKind.OpenIntake, s.Interaction.Prompt.Kind);
            Assert.Equal(2, s.Requests);

            // A press with nothing to act on does not linger: it is forgotten after a moment.
            s.Teleport(30f, 27f);
            s.Frame(interact: true);
            for (int i = 0; i < 10; i++) s.Frame(interact: false);
            s.Teleport(po.IntakeX + 1f, po.IntakeZ);
            for (int i = 0; i < 6; i++) s.Frame(interact: false);
            Assert.Equal(0u, s.World.OpenContainerId);
            Assert.Equal(2, s.Requests);
        }

        [Fact]
        public void TakeFromIntakeThenHoldToDeliver_PaysTheWallet()
        {
            var s = new Session();
            var po = s.World.Map.PostOffice;
            s.Run(100); // the first batch lands within the 3 s jitter
            s.Teleport(po.IntakeX + 1f, po.IntakeZ);
            s.Frame(interact: true);
            s.Run(3);
            var intake = s.World.OpenContainer!;

            GridEntry? letters = null;
            ushort letter = Shared.Content.MailKind("letter").Index;
            foreach (var e in intake.Entries)
                if (e.Stack.DefIndex == letter && (letters == null || e.EntryId < letters.EntryId)) letters = e;
            Assert.NotNull(letters);
            int value = 0;
            foreach (var m in letters!.Stack.Mail) value += m.Value;
            var dest = s.World.Map.FindDestination(letters.Stack.Address)!;

            s.Client.QuickMove(intake.Id, letters.EntryId);
            s.Run(3);
            var carried = s.World.Hotbar.EntryAt(1, 0);
            Assert.NotNull(carried);

            s.Teleport(dest.MailboxX + 0.5f, dest.MailboxZ);
            Assert.Equal(0u, s.World.OpenContainerId); // the server closed the Intake when we left

            // Hands selected: the prompt names the mailbox but there is nothing to deliver.
            s.Frame(interact: true);
            Assert.Equal(InteractKind.Deliver, s.Interaction.Prompt.Kind);
            Assert.Null(s.Interaction.Prompt.Stack);
            Assert.Equal(1, s.Interaction.Prompt.MatchingStacks);
            s.Frame(interact: false);

            s.Client.Input.HeldSlot = 1;
            s.Frame(interact: false);
            var prompt = s.Interaction.Prompt;
            Assert.Same(carried, prompt.Stack);
            Assert.True(prompt.AddressMatches);
            string text = HudText.Prompt(s.World, prompt, out var tone);
            Assert.Equal(PromptTone.Match, tone);
            Assert.Contains(dest.Label, text);
            Assert.StartsWith(HudText.MatchWord, text);

            // A short tap does nothing; the full 0.4 s hold delivers.
            for (int i = 0; i < 6; i++) s.Frame(interact: true);
            Assert.InRange(s.Interaction.Prompt.HoldProgress, 0.3f, 0.7f);
            s.Frame(interact: false);
            s.Run(5);
            Assert.Equal(0, s.Server.World.Economy.Wallet);

            for (int i = 0; i < 14; i++) s.Frame(interact: true);
            s.Frame(interact: false);
            s.Run(15);
            Assert.Equal(value, s.Server.World.Economy.Wallet);
            Assert.Equal(value, s.World.Global.Wallet);
            Assert.Null(s.World.Hotbar.EntryAt(1, 0));
            Assert.Equal($"Wallet {HudText.Money(value)}", HudText.Wallet(s.World.Global));
            Assert.Contains(s.Feed.Entries, e => e.Text.StartsWith("You delivered") && e.Text.Contains(dest.Label) && !e.Bad);
            Assert.Equal(0, s.Client.ContainerDesyncs);
        }

        [Fact]
        public void WrongMailboxShowsTheMismatchAndCharges()
        {
            var s = new Session();
            var map = s.World.Map;
            var right = map.Destinations[2];
            var wrong = map.Destinations[3];
            s.Give(s.Player.Hotbar, "small", right);
            s.Teleport(wrong.MailboxX + 0.5f, wrong.MailboxZ);
            s.Client.Input.HeldSlot = 1;
            s.Frame(interact: false);

            string text = HudText.Prompt(s.World, s.Interaction.Prompt, out var tone);
            Assert.Equal(PromptTone.Mismatch, tone);
            Assert.StartsWith(HudText.MismatchWord, text);
            Assert.Contains(right.Label, text);
            Assert.Contains(wrong.Label, text);

            for (int i = 0; i < 14; i++) s.Frame(interact: true);
            s.Frame(interact: false);
            s.Run(15);
            Assert.Equal(-15, s.Server.World.Economy.Wallet); // 30 ¢ × 0.5
            Assert.Equal(-15, s.World.Global.Wallet);
            Assert.Contains(s.Feed.Entries, e => e.Bad && e.Text.StartsWith("You misdelivered 1 Small Package"));
        }

        [Fact]
        public void PromptNeedsTheViewOnTheMailbox()
        {
            var s = new Session();
            var dest = s.World.Map.Destinations[2];
            s.Teleport(dest.MailboxX + 1.5f, dest.MailboxZ);

            // Looking straight at it from the eyes.
            ViewRay.Aim(s.World.Local, dest.MailboxX, 1.0f, dest.MailboxZ, out s.Client.Input.YawRadians, out s.Client.Input.PitchRadians);
            s.Frame(interact: false);
            Assert.Equal(InteractKind.Deliver, s.Interaction.Prompt.Kind);
            Assert.Same(dest, s.Interaction.Prompt.Destination);

            // Back turned: in range, but not in view.
            s.Client.Input.YawRadians += System.MathF.PI;
            s.Frame(interact: false);
            Assert.Equal(InteractKind.None, s.Interaction.Prompt.Kind);

            // Facing it but looking at the sky.
            s.Client.Input.YawRadians -= System.MathF.PI;
            s.Client.Input.PitchRadians = 1.2f;
            s.Frame(interact: false);
            Assert.Equal(InteractKind.None, s.Interaction.Prompt.Kind);
        }

        [Fact]
        public void ViewRayHitsWhatItIsAimedAt()
        {
            var feet = new PlayerMoveState { X = 10f, Y = 0f, Z = 10f };
            var box = new AimBox(12f, 1f, 7f, 0.5f, 1f, 0.5f);
            ViewRay.Aim(feet, box.X, box.Y, box.Z, out float yaw, out float pitch);
            var ray = ViewRay.FromPlayer(feet, yaw, pitch);
            Assert.Equal(1.6f, ray.Y, 3);
            Assert.InRange(ray.Hit(box), 2.5f, 3.7f);
            Assert.Equal(-1f, new ViewRay(ray.X, ray.Y, ray.Z, yaw + 1.5f, pitch).Hit(box));
            // Yaw 0 looks along −Z, positive pitch looks up.
            var ahead = new ViewRay(0f, 0f, 0f, 0f, 0f);
            Assert.Equal(-1f, ahead.DirZ, 5);
            Assert.True(new ViewRay(0f, 0f, 0f, 0f, 0.5f).DirY > 0f);
        }

        [Fact]
        public void HotbarAssistSelectsPickedUpMailAndTheNextStackAfterADelivery()
        {
            var s = new Session();
            var map = s.World.Map;
            var assist = new HotbarAssist();
            assist.Update(s.Client);
            Assert.Equal(0, s.Client.Input.HeldSlot); // hands

            s.Give(s.Player.Hotbar, "letter", map.Destinations[1]);
            s.Give(s.Player.Hotbar, "letter", map.Destinations[2]);
            assist.Update(s.Client);
            Assert.Equal(1, s.Client.Input.HeldSlot);

            // Choosing hands by key sticks while nothing changes.
            s.Client.Input.HeldSlot = 0;
            assist.Update(s.Client);
            assist.Update(s.Client);
            Assert.Equal(0, s.Client.Input.HeldSlot);

            // Deliver the held stack: the next one is selected.
            s.Client.Input.HeldSlot = 1;
            assist.Update(s.Client);
            var held = s.World.Hotbar.EntryAt(1, 0)!;
            var dest = map.FindDestination(held.Stack.Address)!;
            s.Teleport(dest.MailboxX + 0.5f, dest.MailboxZ);
            s.Client.Deliver(s.World.Hotbar.Id, held.EntryId, dest.Id);
            s.Run(10);
            Assert.Null(s.World.Hotbar.EntryAt(1, 0));
            assist.Update(s.Client);
            Assert.Equal(2, s.Client.Input.HeldSlot);

            // Deliver the last one: no emptied slot stays selected, the hands are.
            held = s.World.Hotbar.EntryAt(2, 0)!;
            dest = map.FindDestination(held.Stack.Address)!;
            s.Teleport(dest.MailboxX + 0.5f, dest.MailboxZ);
            s.Client.Deliver(s.World.Hotbar.Id, held.EntryId, dest.Id);
            s.Run(10);
            Assert.Equal(0, s.World.Hotbar.EntryCount);
            assist.Update(s.Client);
            Assert.Equal(0, s.Client.Input.HeldSlot);
        }

        [Fact]
        public void DeliverAllMatchingEmptiesEveryMatchingStack()
        {
            var s = new Session();
            var map = s.World.Map;
            var here = map.Destinations[4];
            var elsewhere = map.Destinations[5];
            s.Give(s.Player.Hotbar, "letter", here, count: 3);
            s.Give(s.Player.Inventory, "medium", here);
            s.Give(s.Player.Hotbar, "letter", elsewhere, count: 2);
            s.Teleport(here.MailboxX, here.MailboxZ + 0.5f);

            s.Frame(interact: false, deliverAll: true);
            Assert.Equal(2, s.Interaction.Prompt.MatchingStacks);
            for (int i = 0; i < 45; i++) s.Frame(interact: false);
            s.Run(10);

            Assert.Equal(3 * 8 + 70, s.Server.World.Economy.Wallet);
            Assert.Equal(0, s.Interaction.Prompt.MatchingStacks);
            Assert.Equal(1, s.World.Hotbar.EntryCount);
            Assert.Equal(0, s.World.Inventory.EntryCount);
            Assert.Equal(0, s.Server.World.Economy.TotalMisdeliveries);
            // Three letters went in one by one but read as one feed line.
            Assert.Contains(s.Feed.Entries, e => e.Text.Contains("delivered 3 Letters"));
        }

        [Fact]
        public void HudTextFormats()
        {
            Assert.Equal("$18.20", HudText.Money(1820));
            Assert.Equal("$0.05", HudText.Money(5));
            Assert.Equal("-$1.50", HudText.Money(-150));
            Assert.Equal("03:58", HudText.Clock(237.2));
            Assert.Equal("00:00", HudText.Clock(-1));
            Assert.Equal("1 Letter", HudText.CountAndKind("Letter", 1));
            Assert.Equal("12 Letters", HudText.CountAndKind("Letter", 12));

            var s = new Session();
            Assert.StartsWith("Shift 1 / 5   DELIVERY 0", HudText.ShiftAndPhase(s.World));
            Assert.Equal("Quota $0.00 / $6.00", HudText.Quota(s.World.Global));
        }

        [Fact]
        public void FeedKeepsFiveEntriesForSixSeconds()
        {
            var feed = new EventFeed();
            for (int i = 0; i < 7; i++) feed.Add($"line {i}");
            Assert.Equal(EventFeed.MaxEntries, feed.Entries.Count);
            Assert.Equal("line 2", feed.Entries[0].Text);
            feed.Update(5.9);
            Assert.Equal(5, feed.Entries.Count);
            feed.Update(0.2);
            Assert.Empty(feed.Entries);
        }
    }
}
