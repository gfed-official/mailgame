using Godot;
using PerformativeMail.Game.Boot;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.ThrowingCatching;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Hosting;

/// <summary>
/// <c>--playtest-at-mailbox</c> (WP-G, debug builds only): walks the local carrier to the Intake with synthetic W/A/S/D and mouse-look
/// events (the production input adapter and router, as a player would), and once Delivery starts taps E until the first batch is in hand,
/// then walks to the selected letter's mailbox, faces it and frees itself. It reads only client state (the owned carrier's view, the
/// own-panel mirror, the run mirror, the client's world tables). Keep hands off the keyboard until the <c>[Playtest] ready</c> line.
/// Main thread only.
/// </summary>
/// <example><code>loop.AddChild(new PlaytestAutopilot(session));   // GameLoop, after boot, when PlaytestArgs.AtMailbox</code></example>
public sealed partial class PlaytestAutopilot : Node
{
    private const int StandOffMm = 1500;
    private const int ToleranceMm = 400;
    private const ushort FaceNorth = 0;
    private const ushort FaceSouth = 32768;
    private const ulong GiveUpMs = 90_000;
    private const ulong PressEveryMs = 1500;

    private readonly BootSession _session;
    private readonly HashSet<Key> _down = [];
    private Step _step;
    private ulong _startMs;
    private ulong _lastPressMs;
    private Vec3Mm _target;
    private ushort _face;

    /// <summary>Creates the autopilot over the booted session.</summary>
    /// <param name="session">The session.</param>
    public PlaytestAutopilot(BootSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Name = "PlaytestAutopilot";
    }

    private enum Step
    {
        Start,
        ToIntake,
        Take,
        ToMailbox,
        Done,
    }

    /// <inheritdoc />
    public override void _Ready() => _startMs = Time.GetTicksMsec();

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_session.Loop is not { } client || _session.Mirrors is not { } mirrors || _session.LocalPlayer is not { IsKnown: true })
        {
            return;
        }

        ulong now = Time.GetTicksMsec();
        if (now - _startMs > GiveUpMs)
        {
            Finish($"gave up in {_step} after {GiveUpMs / 1000} s (feet {_session.Carrier.View.Current.X},{_session.Carrier.View.Current.Z}, target {_target.X},{_target.Z})");
            return;
        }

        switch (_step)
        {
            case Step.Start:
                Vec3Mm intake = _session.World.PostOffice.IntakeAnchor;
                Go(new Vec3Mm(intake.X, intake.Y, intake.Z + StandOffMm), FaceNorth, Step.ToIntake);
                break;
            case Step.ToIntake when Arrived():
                _step = Step.Take;
                break;
            case Step.Take when client.Pickup.Pickups > 0 && client.Panel.TryGet(out SelectedEntryFacts held):
                if (!TryLot(held.Unit.Address, out LotRecord lot))
                {
                    Finish($"no lot for address {held.Unit.Address.Value}");
                    break;
                }

                bool north = lot.Anchor.Z < M1aStreet.StreetCentreZMm;
                Go(new Vec3Mm(lot.Anchor.X, lot.Anchor.Y, lot.Anchor.Z + (north ? StandOffMm : -StandOffMm)), north ? FaceNorth : FaceSouth, Step.ToMailbox);
                break;
            case Step.Take when mirrors.Run.Phase != ShiftPhase.Delivery:
                break;
            case Step.Take when _down.Contains(Key.E):
                Hold(Key.E, false);
                break;
            case Step.Take when now - _lastPressMs > PressEveryMs:
                // The first batch reaches the Intake a few seconds into Delivery: an empty Intake answers no_target, so tap again.
                _lastPressMs = now;
                Hold(Key.E, true);
                break;
            case Step.ToMailbox when Arrived():
                Finish($"at the mailbox ({_target.X},{_target.Z}) with {client.Panel.Inventory.EntryCount + 1} letter entries, slot {client.Panel.SelectedIndex} selected");
                break;
            default:
                break;
        }
    }

    private void Go(Vec3Mm target, ushort face, Step step)
    {
        _target = target;
        _face = face;
        _step = step;
    }

    // Steers toward the target with W/A/S/D under look yaw 0 (W = -Z, D = +X), then turns to the face yaw; true once both hold.
    private bool Arrived()
    {
        ref readonly var feet = ref _session.Carrier.View.Current;
        ushort yaw = _session.Carrier.View.LookYaw;
        long dx = _target.X - feet.X;
        long dz = _target.Z - feet.Z;
        bool there = Math.Abs(dx) <= ToleranceMm && Math.Abs(dz) <= ToleranceMm;
        ushort want = there ? _face : FaceNorth;
        int diff = unchecked((short)(ushort)(want - yaw));
        if (Math.Abs(diff) >= 182)
        {
            ReleaseKeys();
            var turn = new Vector2(-Math.Clamp(diff / 20f, -400f, 400f), 0f);
            Inject(new InputEventMouseMotion { Relative = turn, ScreenRelative = turn });
            return false;
        }

        Hold(Key.D, !there && dx > ToleranceMm);
        Hold(Key.A, !there && dx < -ToleranceMm);
        Hold(Key.S, !there && dz > ToleranceMm);
        Hold(Key.W, !there && dz < -ToleranceMm);
        return there;
    }

    private bool TryLot(AddressId address, out LotRecord lot)
    {
        foreach (LotRecord l in _session.World.Lots)
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

    private void Finish(string what)
    {
        ReleaseKeys();
        _step = Step.Done;
        GD.Print($"[Playtest] ready: {what}; input is yours");
        QueueFree();
    }

    private void Hold(Key key, bool down)
    {
        if (down == _down.Contains(key))
        {
            return;
        }

        Inject(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = down });
        _ = down ? _down.Add(key) : _down.Remove(key);
    }

    private void ReleaseKeys()
    {
        foreach (Key k in _down.ToArray())
        {
            Hold(k, false);
        }
    }

    private static void Inject(InputEvent e)
    {
        Godot.Input.ParseInputEvent(e);
        Godot.Input.FlushBufferedEvents();
    }
}
