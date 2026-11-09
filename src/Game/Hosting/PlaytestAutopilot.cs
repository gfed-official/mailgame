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
/// then walks to the selected letter's mailbox, faces it and frees itself. With <c>--playtest-park-next</c> (WP-I) it stays: whenever the
/// selected hotbar letter is for another house (a throw or a delivery staged the next one, or a hotbar key chose it), it walks to that
/// letter's mailbox again and prints <c>[Playtest] ready</c> once more, so a playtest throws and delivers letter after letter without
/// walking. It reads only client state (the owned carrier's view, the own-panel mirror, the run mirror, the client's world tables). Keep
/// hands off the keyboard until each <c>[Playtest] ready</c> line. Main thread only.
/// </summary>
/// <example><code>loop.AddChild(new PlaytestAutopilot(session, parkNext: false));   // GameLoop, after boot, when PlaytestArgs.AtMailbox</code></example>
public sealed partial class PlaytestAutopilot : Node
{
    private const int StandOffMm = 1500;
    private const int ToleranceMm = 400;
    private const ushort FaceNorth = 0;
    private const ushort FaceSouth = 32768;
    private const ulong GiveUpMs = 90_000;
    private const ulong PressEveryMs = 1500;

    private readonly BootSession _session;
    private readonly bool _parkNext;
    private readonly HashSet<Key> _down = [];
    private Step _step;
    private ulong _startMs;
    private ulong _lastPressMs;
    private Vec3Mm _target;
    private ushort _face;
    private AddressId _parked;

    /// <summary>Creates the autopilot over the booted session.</summary>
    /// <param name="session">The session.</param>
    /// <param name="parkNext">Stay on and re-park at each next letter's mailbox (<c>--playtest-park-next</c>).</param>
    public PlaytestAutopilot(BootSession session, bool parkNext)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _parkNext = parkNext;
        Name = "PlaytestAutopilot";
    }

    private enum Step
    {
        Start,
        ToIntake,
        Take,
        ToMailbox,
        Parked,
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
        if (_step != Step.Parked && now - _startMs > GiveUpMs)
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
                ToMailboxOf(held.Unit.Address);
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
            case Step.Parked when client.Panel.TryGet(out SelectedEntryFacts next) && next.IsMail && next.Unit.Address != _parked:
                _startMs = now;
                ToMailboxOf(next.Unit.Address);
                break;
            default:
                break;
        }
    }

    // Walks to the mailbox of the address: 1.5 m in front of it on the street side, facing it.
    private void ToMailboxOf(AddressId address)
    {
        _parked = address;
        if (!TryLot(address, out LotRecord lot))
        {
            Finish($"no lot for address {address.Value}");
            return;
        }

        bool north = lot.Anchor.Z < M1aStreet.StreetCentreZMm;
        Go(new Vec3Mm(lot.Anchor.X, lot.Anchor.Y, lot.Anchor.Z + (north ? StandOffMm : -StandOffMm)), north ? FaceNorth : FaceSouth, Step.ToMailbox);
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
        GD.Print($"[Playtest] ready: {what}; input is yours");
        if (_parkNext)
        {
            _step = Step.Parked;
            return;
        }

        _step = Step.Done;
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
