using PerformativeMail.Client.Core.Ui.Throwing;
using PerformativeMail.Game.Boot;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.ThrowingCatching;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.Boot;

/// <summary>
/// WP-F's playable loop through the real boot steps 5-6 and the loopback session, with the router's output written straight into the
/// client latch (the Godot router and adapter are the loop smoke's): E at the Intake puts mail in the own-panel mirror (the inventory
/// panel's source and <c>ClientPorts.Panel</c>) with a letter selected on the hotbar; holding E at that mail's mailbox pays; a release
/// aimed until the preview turns Accept lands in the mailbox and pays.
/// </summary>
public class ClientLoopBootTests
{
    private const int StandOffMm = 1500;
    private const int ThrowStandOffMm = 4000;
    private const int StreetCentreZMm = 32_000;

    [Fact]
    public void Loop_EAtTheIntake_ThenHoldEAtTheMailbox_PicksUpIntoThePanel_AndPays()
    {
        // Arrange
        var loop = new LoopDriver();

        // Act: pickup
        bool picked = loop.Pickup();

        // Assert: mail in the own-panel mirror, a letter selected on the hotbar, the mirror is the client's panel port.
        Assert.True(picked);
        Assert.True(loop.Client.Panel.TryGet(out SelectedEntryFacts held));
        Assert.True(held.IsMail && loop.Client.Panel.SelectedIndex > 0);
        Assert.Same(loop.Client.Panel, loop.Session.Panel);

        // Act: hand delivery at the selected letter's mailbox
        long before = loop.Wallet;
        loop.GoInFront(held.Unit.Address, StandOffMm);
        loop.HoldInteract(Verb.Deliver, 60, () => loop.Wallet != before);

        // Assert
        Assert.True(loop.Wallet > before, $"wallet {before} -> {loop.Wallet}");
    }

    [Fact]
    public void Loop_ReleaseAimedIntoTheHeldLettersMailbox_LaunchesItAndPays()
    {
        // Arrange: picked up, standing 4 m in front of the held letter's mailbox.
        var loop = new LoopDriver();
        Assert.True(loop.Pickup());
        Assert.True(loop.Client.Panel.TryGet(out SelectedEntryFacts held));
        loop.GoInFront(held.Unit.Address, ThrowStandOffMm);
        long before = loop.Wallet;

        // Act: aim, pitching down until the arc ends in the mailbox, then commit the release.
        bool accept = loop.AimUntil(() => loop.Client.Aim.End == AimEnd.Accept);
        ThrowSendResult sent = loop.Client.Release!.Commit(loop.Session.Session.Client.ClientTick);
        loop.Idle(90, () => loop.Wallet != before);

        // Assert
        Assert.True(accept);
        Assert.Equal(ThrowSendResult.Sent, sent);
        Assert.True(loop.Wallet > before, $"wallet {before} -> {loop.Wallet}");
    }

    // Boots the session (steps 1-9 over a fake clock, Prep cut short) and drives it frame by frame through the client latch.
    private sealed class LoopDriver
    {
        private readonly FakeBootClock _clock;
        private InputState _state = new() { FocusCosMinPm = 635 };
        private ushort _seq;

        public LoopDriver()
        {
            var h = new BootHarness();
            h.Context.Options = new BootOptions
            {
                Clock = h.Context.Options.Clock,
                FixtureProfile = SimProfile.M1aGreybox.WithShiftStub(new ShiftStubSettings { PrepTicks = 30 }),
            };
            Assert.True(new BootSequence(h.Plan()).Run(h.Context));
            Session = h.Context.Session!;
            Client = Session.Loop!;
            _clock = (FakeBootClock)Session.Clock;
            Session.Driver.Start(_clock.Timestamp);
            Idle(200, () => Session.Mirrors!.Run.Phase == ShiftPhase.Delivery && Session.LocalPlayer!.IsKnown);
            Assert.Equal(ShiftPhase.Delivery, Session.Mirrors!.Run.Phase);
        }

        public BootSession Session { get; }

        public ClientLoop Client { get; }

        public long Wallet => Session.Mirrors!.Wallet.BalanceCents;

        private IWorldQuery World => Session.World;

        public bool Pickup()
        {
            MoveTo(World.PostOffice.IntakeAnchor.X, World.PostOffice.IntakeAnchor.Z + StandOffMm);

            // The first batch reaches the Intake a few seconds into Delivery: press again while take_all answers no_target.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                HoldInteract(Verb.Open, 2, () => false);
                if (Idle(45, () => Client.Pickup.Pickups > 0 && Client.Panel.SelectedIndex > 0))
                {
                    return true;
                }
            }

            return false;
        }

        public void GoInFront(AddressId address, int offMm)
        {
            LotRecord lot = World.Lots.ToArray().Single(l => l.Address == address);
            bool north = lot.Anchor.Z < StreetCentreZMm;
            Look(0);
            MoveTo(lot.Anchor.X, lot.Anchor.Z + (north ? offMm : -offMm));
            Look(north ? (ushort)0 : (ushort)32768);
        }

        // Presses Interact on the focused target of the verb and keeps it held for up to frames, until done.
        public void HoldInteract(Verb verb, int frames, Func<bool> done)
        {
            Assert.True(Idle(30, () => Snapshot.Verb == verb && !Snapshot.Focused.IsNone && Snapshot.Available != 0), $"no {verb} focus");
            AnchorRef target = Snapshot.Focused;
            Write(_state with { HeldBits = FrameActions.Bit(FrameAction.Interact) });
            Session.Session.Client.InputWriter.PushEdge(new InputEdge(FrameAction.Interact, EdgeKind.Press, ++_seq, 1, targetEntity: target.Entity.Value, targetSlot: target.Slot, presentedSeq: Snapshot.Seq));
            Idle(frames, done);
            Write(_state with { HeldBits = 0 });
            Session.Session.Client.InputWriter.PushEdge(new InputEdge(FrameAction.Interact, EdgeKind.Release, ++_seq, 1));
            Idle(1, () => false);
        }

        public bool AimUntil(Func<bool> done)
        {
            Write(_state with { HeldBits = FrameActions.Bit(FrameAction.Aim) });
            for (short pitch = 0; pitch > -9000; pitch -= 150)
            {
                Write(_state with { LookPitch = pitch });
                Idle(1, () => false);
                if (Client.Aim.Aiming && done())
                {
                    return true;
                }
            }

            return false;
        }

        public bool Idle(int frames, Func<bool> done)
        {
            for (int i = 0; i < frames; i++)
            {
                if (done())
                {
                    return true;
                }

                Session.Session.Client.InputWriter.SetState(in _state);
                _clock.Timestamp += FakeBootClock.TicksPerSecond / 30;
                Session.Session.RunFrame();
            }

            return done();
        }

        private ref readonly InteractionSnapshot Snapshot => ref Client.Interaction.Publisher.Current;

        private void Look(ushort yaw)
        {
            Write(_state with { LookYaw = yaw });
            Idle(2, () => false);
        }

        // Steers with the move intent at look yaw 0 (W = −Z, D = +X); at yaw 32768 both axes flip.
        private void MoveTo(int x, int z)
        {
            int flip = _state.LookYaw == 0 ? 1 : -1;
            for (int i = 0; i < 900; i++)
            {
                int dx = x - Session.Carrier.View.Current.X;
                int dz = z - Session.Carrier.View.Current.Z;
                if (Math.Abs(dx) <= 150 && Math.Abs(dz) <= 150)
                {
                    break;
                }

                short mx = (short)(Math.Abs(dx) > 150 ? Math.Sign(dx) * 32767 * flip : 0);
                short my = (short)(Math.Abs(dz) > 150 ? -Math.Sign(dz) * 32767 * flip : 0);
                Write(_state with { MoveX = mx, MoveY = my });
                Idle(1, () => false);
            }

            Write(_state with { MoveX = 0, MoveY = 0 });
            Idle(6, () => false);
        }

        private void Write(InputState s) => _state = s;
    }
}
