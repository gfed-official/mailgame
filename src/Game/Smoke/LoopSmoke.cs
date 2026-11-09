using Godot;
using PerformativeMail.Client.Core.Throwing;
using PerformativeMail.Client.Core.Ui.Throwing;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Economy;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.ThrowingCatching;
using PerformativeMail.Sim.World;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// The playable loop over the real boot (WP-F), driven only by key and mouse events through the production input adapter: walk to the
/// Intake console, see it focused with the [E] prompt, press E (the Intake's Open, then take_all and the hotbar staging), see mail in the
/// own-panel mirror with a hotbar slot selected; walk to the mailbox of a carried letter, hold E and see the wallet change; walk in front
/// of the selected letter's mailbox, hold the right mouse button, pitch until the aim preview turns Accept, release, and see a projectile
/// launched and the wallet change again. Every step fails loudly when it stays idle (no focus, no prompt, no pickup, no wallet change,
/// no arc, no flight).
/// </summary>
[SmokeScenario("loop-pickup-deliver-throw")]
public sealed class LoopSmoke : SmokeScenario
{
    private const int PrepTicks = 60;
    private const int StandOffMm = 1500;
    private const int ThrowStandOffMm = 4000;
    private const int MoveToleranceMm = 150;
    private const int StreetCentreZMm = 32_000;
    private const ushort FaceNorth = 0;
    private const ushort FaceSouth = 32768;
    private const int MinAimWallMs = 400;

    private readonly FakeClock _clock = new();
    private ClientLoop _client = null!;
    private CarrierView _carrier = null!;
    private WalletMirror _wallet = null!;
    private FlightMirrorConsumer _flights = null!;
    private IInteractionQuery _interaction = null!;
    private IAimArcSource _aim = null!;
    private IWorldQuery _world = null!;
    private GameLoop _loop = null!;

    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override double TimeoutSeconds => 120;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop) =>
        loop.UseBootOptionsForSmoke(new BootOptions
        {
            Clock = _clock,
            FixtureProfile = SimProfile.M1aGreybox.WithShiftStub(new ShiftStubSettings { PrepTicks = PrepTicks }),
        });

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        BootSession? session = ctx.Loop.Session;
        Expect(ctx, "client loop composed at boot step 5", session?.Loop is not null, true);
        Expect(ctx, "router interaction port bound", ctx.Loop.InputBoot?.Ports.Describe().Contains("interaction=bound", StringComparison.Ordinal) ?? false, true);
    }

    /// <inheritdoc />
    public override SmokeStatus Poll(SmokeContext ctx)
    {
        _loop = ctx.Loop;
        if (_loop.InputBoot?.Adapter is not { } adapter || !adapter.IsInsideTree())
        {
            return SmokeStatus.Running;
        }

        if (_loop.Session is not { Loop: { } client, LocalPlayer: { } local, Mirrors: { } mirrors } session)
        {
            ctx.Fail("no booted session with the client loop");
            return SmokeStatus.Done;
        }

        _client = client;
        _carrier = session.Carrier.View;
        _wallet = mirrors.Wallet;
        _flights = mirrors.Flights;
        _world = session.World;
        _interaction = client.Interaction.Publisher;
        _aim = client.Aim;
        _loop._Process(0);
        for (int i = 0; i < 20 && !local.IsKnown; i++)
        {
            Tick();
        }

        for (int i = 0; i < PrepTicks + 60 && mirrors.Run.Phase != ShiftPhase.Delivery; i++)
        {
            Tick();
        }

        if (!Expect(ctx, "the shift reached Delivery", mirrors.Run.Phase, ShiftPhase.Delivery))
        {
            return SmokeStatus.Done;
        }

        if (Pickup(ctx) && HandDeliver(ctx))
        {
            Throw(ctx);
        }

        return SmokeStatus.Done;
    }

    private bool Pickup(SmokeContext ctx)
    {
        Vec3Mm intake = _world.PostOffice.IntakeAnchor;
        MoveTo(intake.X, intake.Z + StandOffMm);
        AnchorRef console = WaitFocus(Verb.Open);
        if (!Expect(ctx, $"the Intake console is focused (feet {_carrier.Current.X},{_carrier.Current.Z}, anchor {intake.X},{intake.Z})", console.IsNone, false))
        {
            return false;
        }

        Expect(ctx, "the HUD prompt shows [E] Take mail", PromptText(), "[E] Take mail");

        // The first batch reaches the Intake a few seconds into Delivery (F2 jitter): an empty Intake answers no_target, so press again.
        bool picked = false;
        for (int attempt = 0; attempt < 8 && !picked; attempt++)
        {
            Press(Key.E, 2);
            picked = TickUntil(45, () => _client.Pickup.Pickups > 0 && _client.Panel.SelectedIndex > 0);
        }

        Expect(ctx, $"E took the Intake's mail (take_all reason {_client.Pickup.LastTakeAllReason})", _client.Pickup.Pickups, 1);
        return Expect(ctx, $"a letter is staged on the hotbar and selected (slot {_client.Panel.SelectedIndex})", picked && _client.Panel.TryGet(out _), true);
    }

    private bool HandDeliver(SmokeContext ctx)
    {
        EntryIdOrHeld(out AddressId address);
        if (!TryLot(address, out LotRecord lot))
        {
            ctx.Fail($"no lot carries the letter's address {address.Value}");
            return false;
        }

        GoInFront(lot, StandOffMm);
        AnchorRef box = WaitFocus(Verb.Deliver);
        if (!Expect(ctx, $"the mailbox of {address.Value} is focused", box.Entity.Value, lot.Destination.Value))
        {
            return false;
        }

        Expect(ctx, "the HUD prompt offers the delivery", PromptText().Contains("Deliver", StringComparison.Ordinal), true);
        long before = _wallet.BalanceCents;
        SmokeInput.Key(Key.E, true);
        bool paid = TickUntil(90, () => _wallet.BalanceCents != before);
        SmokeInput.Key(Key.E, false);
        Tick();
        return Expect(ctx, $"hand delivery changed the wallet ({before} → {_wallet.BalanceCents} ¢)", paid, true);
    }

    private void Throw(SmokeContext ctx)
    {
        if (!_client.Panel.TryGet(out SelectedEntryFacts held))
        {
            ctx.Fail("no letter left in hand to throw");
            return;
        }

        if (!TryLot(held.Unit.Address, out LotRecord lot))
        {
            ctx.Fail($"no lot carries the held letter's address {held.Unit.Address.Value}");
            return;
        }

        GoInFront(lot, ThrowStandOffMm);
        long before = _wallet.BalanceCents;
        uint released = _client.Release?.Released ?? 0;
        SmokeInput.Mouse(MouseButton.Right, true);
        TickUntil(10, () => _aim.Aiming);
        int arc = _aim.PointCount;
        bool accept = false;
        for (int i = 0; i < 80 && !accept; i++)
        {
            SmokeInput.MouseMotion(0f, 6f);   // pitch down a little each frame until the arc ends in the mailbox
            Tick();
            accept = _aim.Aiming && _aim.End == AimEnd.Accept;
            arc = Math.Max(arc, _aim.PointCount);
        }

        Expect(ctx, $"RMB shows an aim arc ({arc} points)", arc > 1, true);
        Expect(ctx, "the arc ends in the letter's mailbox (Accept)", accept, true);
        // The router judges the release on its own millisecond clock (wall time): hold at least throwMinAimMs before letting go.
        System.Threading.Thread.Sleep(MinAimWallMs);
        SmokeInput.Mouse(MouseButton.Right, false);
        bool launched = TickUntil(10, () => (_client.Release?.Released ?? 0) > released);
        Expect(ctx, "releasing RMB sent the throw", launched, true);
        int flying = 0;
        bool paid = TickUntil(120, () =>
        {
            flying = Math.Max(flying, _flights.Flights.Count);
            return _wallet.BalanceCents != before;
        });
        Expect(ctx, $"a projectile was in flight (max {flying})", flying > 0, true);
        Expect(ctx, $"the thrown letter landed and changed the wallet ({before} → {_wallet.BalanceCents} ¢)", paid, true);
    }

    // The address to hand-deliver: a player_inventory letter when there is one, else the held one.
    private void EntryIdOrHeld(out AddressId address)
    {
        EntryId bag = _client.Panel.FirstInventoryMail();
        address = !bag.IsNone ? _client.Panel.AddressOf(bag)
            : _client.Panel.TryGet(out SelectedEntryFacts held) ? held.Unit.Address : AddressId.None;
    }

    private bool TryLot(AddressId address, out LotRecord lot)
    {
        foreach (LotRecord l in _world.Lots)
        {
            if (l.Address == address)
            {
                lot = l;
                return true;
            }
        }

        lot = default;
        return false;
    }

    // Walks (look yaw north) to stand off the lot's mailbox on the street side, then faces it.
    private void GoInFront(in LotRecord lot, int offMm)
    {
        Turn(FaceNorth);
        bool north = lot.Anchor.Z < StreetCentreZMm;
        MoveTo(lot.Anchor.X, lot.Anchor.Z + (north ? offMm : -offMm));
        Turn(north ? FaceNorth : FaceSouth);
    }

    // Steers with W/A/S/D while the look yaw is 0 (W = −Z, D = +X) until the feet are within tolerance.
    private void MoveTo(int x, int z)
    {
        for (int i = 0; i < 900; i++)
        {
            int dx = x - _carrier.Current.X;
            int dz = z - _carrier.Current.Z;
            Hold(Key.D, dx > MoveToleranceMm);
            Hold(Key.A, dx < -MoveToleranceMm);
            Hold(Key.S, dz > MoveToleranceMm);
            Hold(Key.W, dz < -MoveToleranceMm);
            if (Math.Abs(dx) <= MoveToleranceMm && Math.Abs(dz) <= MoveToleranceMm)
            {
                break;
            }

            Tick();
        }

        foreach (Key k in new[] { Key.W, Key.A, Key.S, Key.D })
        {
            Hold(k, false);
        }

        for (int i = 0; i < 6; i++)
        {
            Tick();
        }
    }

    // Turns the look with mouse motion until it is within 1° of the yaw.
    private void Turn(ushort yaw)
    {
        for (int i = 0; i < 60; i++)
        {
            int diff = unchecked((short)(ushort)(yaw - _carrier.LookYaw));
            if (Math.Abs(diff) < 182)
            {
                return;
            }

            SmokeInput.MouseMotion(-Math.Clamp(diff / 20f, -400f, 400f), 0f);
            Tick();
        }
    }

    private AnchorRef WaitFocus(Verb verb)
    {
        TickUntil(30, () => !_interaction.Current.Focused.IsNone && _interaction.Current.Verb == verb && _interaction.Current.Available != 0);
        return _interaction.Current.Verb == verb ? _interaction.Current.Focused : AnchorRef.None;
    }

    private string PromptText()
    {
        Tick();
        return _loop.GetNodeOrNull<Control>("Hud/Persistent/DeliverPromptView") is { Visible: true } view
            ? string.Join(" ", view.FindChildren("*", nameof(Label), true, false).OfType<Label>().Where(l => l.Visible && l.Text.Length > 0).Select(l => l.Text))
            : string.Empty;
    }

    private void Press(Key key, int ticks)
    {
        SmokeInput.Key(key, true);
        for (int i = 0; i < ticks; i++)
        {
            Tick();
        }

        SmokeInput.Key(key, false);
        Tick();
    }

    private readonly HashSet<Key> _down = [];

    private void Hold(Key key, bool down)
    {
        if (down == _down.Contains(key))
        {
            return;
        }

        SmokeInput.Key(key, down);
        _ = down ? _down.Add(key) : _down.Remove(key);
    }

    private bool TickUntil(int ticks, Func<bool> done)
    {
        for (int i = 0; i < ticks; i++)
        {
            if (done())
            {
                return true;
            }

            Tick();
        }

        return done();
    }

    private void Tick()
    {
        _clock.Advance(FakeClock.TickCounts);
        _loop._Process(0);
    }
}
