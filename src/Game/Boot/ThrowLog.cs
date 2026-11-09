using System.Globalization;
using PerformativeMail.Client.Core.Inventory;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// The own throws' log lines (WP-H), so a live run's <c>game.log</c> tells input from logic: <c>[Throw] aim-start</c> on every Throw
/// press the router checks (with the selected slot, entry and whether it is throwable), <c>[Throw] release</c> on every commit (with the
/// sender's result), <c>[Throw] result</c> when the host refuses the release (<c>rejected</c> and the reason) or the own flight resolves
/// (<c>accepted</c>, <c>misdelivered</c> or <c>rejected</c> at a mailbox, else <c>landed</c>, and the terminal), and
/// <c>[Throw] outcome</c> for Mail's verdict on an own thrown landing: the units consumed and the cents paid or charged (a misdelivery
/// consumes the units and charges the penalty, GDD throwing-catching T6; nothing comes back to the hotbar, WP-I). Reads only client
/// state: the preview, the own-panel mirror and the drained records and replies. A mirror part (<c>ThrowLaunched</c>,
/// <c>FlightResolved</c>, <c>DeliveryOutcome</c>, replies); main thread only. Allocates per line, only on those player actions.
/// </summary>
/// <param name="panel">The own-panel mirror (the selected slot).</param>
/// <param name="ownSlot">This client's player slot.</param>
/// <param name="write">The printer (<c>GD.Print</c> in the game); null logs nothing but still counts.</param>
/// <example><code>var log = new ThrowLog(panel, PlayerSlot.FromValue(0), GD.Print); builder.BindMirrorPart(ThrowLog.Kinds, log);</code></example>
public sealed class ThrowLog(OwnPanelMirror panel, PlayerSlot ownSlot, Action<string>? write) : IMirrorPart
{
    private const int MaxOwnFlights = 16;

    private readonly OwnPanelMirror _panel = panel ?? throw new ArgumentNullException(nameof(panel));
    private readonly ushort[] _own = new ushort[MaxOwnFlights];
    private int _ownCount;

    /// <summary>The kinds this part applies.</summary>
    public static EventKindSet Kinds => EventKindSet.Of(ThrowEventKinds.ThrowLaunched, ThrowEventKinds.FlightResolved, MailEventKinds.DeliveryOutcome);

    /// <summary>Own flights a mailbox accepted.</summary>
    public int Accepted { get; private set; }

    /// <summary>The last line written.</summary>
    public string Last { get; private set; } = string.Empty;

    /// <summary>A Throw press the router checks before aiming.</summary>
    /// <param name="s">The published preview.</param>
    public void AimPress(in ThrowPreviewState s) => Write(
        $"aim-start slot={_panel.SelectedIndex} entry={s.Entry.Value} units={s.Units} "
        + (s.NotThrowable == NotThrowable.None ? "throwable" : $"not_throwable={s.NotThrowable}"));

    /// <summary>A Throw commit.</summary>
    /// <param name="result">The sender's result.</param>
    /// <param name="s">The preview the release was built from.</param>
    public void Release(ThrowSendResult result, in ThrowPreviewState s) =>
        Write($"release {result} slot={s.HotbarIndex} entry={s.Entry.Value} end={s.EndClass}");

    /// <inheritdoc />
    public void Apply(in EventEntry e, ReadOnlySpan<byte> blob)
    {
        if (e.Kind == ThrowEventKinds.ThrowLaunched)
        {
            ThrowLaunched l = e.PayloadAs<ThrowLaunched>();
            if (l.Thrower == ownSlot.Value && _ownCount < MaxOwnFlights)
            {
                _own[_ownCount++] = l.FlightId;
            }

            return;
        }

        if (e.Kind == MailEventKinds.DeliveryOutcome)
        {
            DeliveryOutcomePayload o = e.PayloadAs<DeliveryOutcomePayload>();
            if (o.Actor == ownSlot.Value && o.Via == (byte)DeliveryVia.Throw)
            {
                bool floor = (o.Flags & DeliveryOutcomePayload.FlagStoppedAtFloor) != 0;
                Write($"outcome {((EvalResult)o.Result).ToString().ToLowerInvariant()} units={o.Units} cents={o.Cents}{(floor ? " stopped_at_floor" : string.Empty)}");
            }

            return;
        }

        if (e.Kind != ThrowEventKinds.FlightResolved)
        {
            return;
        }

        FlightResolved r = e.PayloadAs<FlightResolved>();
        int i = Array.IndexOf(_own, r.FlightId, 0, _ownCount);
        if (i < 0)
        {
            return;
        }

        _own[i] = _own[--_ownCount];
        bool accepted = r.Terminal == Terminal.MailboxAccept;
        Accepted += accepted ? 1 : 0;
        string word = r.Terminal switch
        {
            Terminal.MailboxAccept => "accepted",
            Terminal.MailboxMisdeliver => "misdelivered",
            Terminal.MailboxReject => "rejected",
            _ => "landed",
        };
        Write($"result {word} {r.Terminal} flight={r.FlightId}");
    }

    /// <inheritdoc />
    public void ApplyReply(in Reply r)
    {
        if (r.Kind == ThrowOpKinds.ThrowRelease && r.Reason != 0)
        {
            Write($"result rejected {ThrowReasons.Name((ThrowReason)r.Reason)} reason={r.Reason}");
        }
    }

    private void Write(string line)
    {
        Last = string.Create(CultureInfo.InvariantCulture, $"[Throw] {line}");
        write?.Invoke(Last);
    }
}
