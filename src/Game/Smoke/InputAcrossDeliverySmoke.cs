using Godot;
using PerformativeMail.Client.Core.Ui.Throwing;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Input across the Prep → Delivery hand-off (WP-G item 3; playtest of f145219: "W/E/clicks dead while the clock ran"), over the real boot
/// and the production input adapter: during Prep walk, press E at the Intake (an Open with nothing to take yet), click both mouse
/// buttons and hold E at a closed mailbox; hold S through the tick the shift turns to Delivery; then, in Delivery, W still moves the
/// carrier, the mouse still turns the look, E at the Intake still takes the first batch and the right button still raises the aim
/// with the letter in hand.
/// </summary>
[SmokeScenario("input-across-delivery")]
public sealed class InputAcrossDeliverySmoke : SmokeScenario
{
    private const int PrepTicks = 600;
    private const int StandOffMm = 1500;

    private readonly FakeClock _clock = new();

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
    public override void Start(SmokeContext ctx) =>
        Expect(ctx, "client loop composed at boot step 5", ctx.Loop.Session?.Loop is not null, true);

    /// <inheritdoc />
    public override SmokeStatus Poll(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        if (loop.InputBoot?.Adapter is not { } adapter || !adapter.IsInsideTree())
        {
            return SmokeStatus.Running;
        }

        if (loop.Session is not { LocalPlayer: { } local, Mirrors: { } mirrors, Loop: { } client } session)
        {
            ctx.Fail("no booted session");
            return SmokeStatus.Done;
        }

        var drive = new SmokeDriver(_clock, loop, session.Carrier.View);
        CarrierView carrier = drive.Carrier;
        loop._Process(0);
        drive.TickUntil(20, () => local.IsKnown);

        // Prep: walk, E at the Intake, both mouse buttons, a held E at a closed mailbox.
        Expect(ctx, "in Prep at the start", mirrors.Run.Phase, ShiftPhase.Prep);
        Expect(ctx, "Prep: W walks", Walks(drive, Key.W), true);
        Vec3Mm intake = session.World.PostOffice.IntakeAnchor;
        drive.MoveTo(intake.X, intake.Z + StandOffMm);
        drive.Press(Key.E, 2);
        drive.TickN(10);
        drive.Click(MouseButton.Left);
        drive.Click(MouseButton.Right);
        LotRecord lot = session.World.Lots[0];
        drive.MoveTo(lot.Anchor.X, lot.Anchor.Z + StandOffMm);
        drive.Press(Key.E, 20);
        if (!Expect(ctx, "still in Prep before the hand-off", mirrors.Run.Phase, ShiftPhase.Prep))
        {
            return SmokeStatus.Done;
        }

        // The hand-off with S held through it (toward the street centre), from ten ticks before it.
        drive.TickUntil(PrepTicks, () => session.CurrentTick + 10 >= PrepTicks);
        drive.Hold(Key.S, true);
        int z0 = carrier.Current.Z;
        drive.TickUntil(PrepTicks, () => mirrors.Run.Phase == ShiftPhase.Delivery);
        int zAtHandOff = carrier.Current.Z;
        drive.TickN(15);
        drive.Hold(Key.S, false);
        if (!Expect(ctx, "the shift reached Delivery", mirrors.Run.Phase, ShiftPhase.Delivery))
        {
            return SmokeStatus.Done;
        }

        Expect(ctx, $"a key held through the hand-off keeps moving the carrier (z {z0} → {zAtHandOff} → {carrier.Current.Z})", carrier.Current.Z > zAtHandOff + 500, true);

        // Delivery: walking, looking, E at the Intake, the right button.
        Expect(ctx, "Delivery: W walks", Walks(drive, Key.W), true);
        ushort yaw = carrier.LookYaw;
        SmokeInput.MouseMotion(40f, 0f);
        drive.TickN(3);
        Expect(ctx, $"Delivery: the mouse turns the look (yaw {yaw} → {carrier.LookYaw})", carrier.LookYaw != yaw, true);
        drive.Turn(0);
        drive.MoveTo(intake.X, intake.Z + StandOffMm);
        bool picked = false;
        for (int attempt = 0; attempt < 8 && !picked; attempt++)
        {
            drive.Press(Key.E, 2);
            picked = drive.TickUntil(45, () => client.Pickup.Pickups > 0 && client.Panel.SelectedIndex > 0);
        }

        Expect(ctx, $"Delivery: E at the Intake takes the mail (take_all reason {client.Pickup.LastTakeAllReason})", picked, true);
        drive.TickN(6);   // the aim gate reads the published preview's refusal: let the hotbar selection reach it first
        IAimArcSource aim = client.Aim;
        SmokeInput.Mouse(MouseButton.Right, true);
        bool aiming = drive.TickUntil(10, () => aim.Aiming && aim.PointCount > 1);
        Expect(ctx, $"Delivery: the right button raises the aim with the letter in hand ({aim.PointCount} points)", aiming, true);
        drive.Press(Key.Escape, 1);
        SmokeInput.Mouse(MouseButton.Right, false);
        drive.Tick();
        return SmokeStatus.Done;
    }

    // Holds the key for 20 ticks; true when the feet moved more than half a metre.
    private static bool Walks(SmokeDriver drive, Key key)
    {
        int x = drive.Carrier.Current.X;
        int z = drive.Carrier.Current.Z;
        drive.Hold(key, true);
        drive.TickN(20);
        drive.Hold(key, false);
        drive.TickN(6);
        long dx = drive.Carrier.Current.X - x;
        long dz = drive.Carrier.Current.Z - z;
        return (dx * dx) + (dz * dz) > 500L * 500L;
    }
}
