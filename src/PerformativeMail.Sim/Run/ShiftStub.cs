// M-1a STUB — replaced by Run Structure at M1 (no GDD)
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Run.Events;

namespace PerformativeMail.Sim.Run;

/// <summary>The stub's phases. Values are dumped, so append only.</summary>
public enum ShiftPhase : byte
{
    /// <summary>Before Delivery: deliveries closed.</summary>
    Prep = 0,

    /// <summary>Deliveries open.</summary>
    Delivery = 1,

    /// <summary>After Delivery: deliveries closed for good (no Payday or Results at M-1a).</summary>
    Closed = 2,
}

/// <summary>
/// The M-1a Run Structure stand-in (mail-destinations story 009; ADR-0017 section 3, Q-ARCH-11): Prep, then Delivery, then Closed.
/// Binds phase 12 only. On a transition it calls <see cref="IMailApi.SetDeliveriesState"/> in phase 12 of the tick before, so the new
/// state is effective from the next tick's phase 0, and raises one <see cref="RunEventKinds.DeliveriesState"/>. Reads no clock (ticks
/// only) and never requests a stop. Its section is <see cref="SectionIds.Run"/>. Sim tick thread only.
/// </summary>
/// <example><code>new ShiftStub(mailApi, runControl, context.Events, settings)</code></example>
[SimSystem(SystemId.RunStructure)]
public sealed class ShiftStub : ISimSystem, ISimPhase, ISimStateContributor
{
    private readonly IMailApi _mail;
    private readonly EventLogFacade _events;
    private readonly ulong _mask;
    private readonly uint _deliveryTicks;

    // Held because the orchestrator owns the stop port; the greybox never stops, so it is not called.
    private readonly IRunControl _runControl;

    private ShiftPhase _phase = ShiftPhase.Prep;
    private uint _phaseEndTick;
    private bool _open;
    private uint _closeTick;
    private uint _deadlineTick;

    /// <summary>Creates the stub, in Prep.</summary>
    /// <param name="mail">Mail's API (downward call).</param>
    /// <param name="runControl">The stop port (unused at M-1a).</param>
    /// <param name="events">The world's event-log writer facade.</param>
    /// <param name="settings">The profile's greybox settings.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ShiftStub(IMailApi mail, IRunControl runControl, EventLogFacade events, ShiftStubSettings settings)
    {
        ArgumentNullException.ThrowIfNull(mail);
        ArgumentNullException.ThrowIfNull(runControl);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(settings);
        _mail = mail;
        _runControl = runControl;
        _events = events;
        _mask = settings.UnlockedMask;
        _deliveryTicks = checked((uint)(settings.DeliverySeconds * TickRate.Hz));
        _phaseEndTick = checked((uint)settings.PrepTicks);
    }

    /// <inheritdoc />
    public ushort SectionId => SectionIds.Run;

    /// <inheritdoc />
    public void Bind(SimWorldBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Bind(PhaseId.RunStep, this);
        builder.BindEventKind(RunEventDescriptors.DeliveriesState);
        builder.BindStateContributor(this);
    }

    /// <inheritdoc />
    public void Run(in TickContext ctx)
    {
        uint next = ctx.Tick + 1;
        if (_phase == ShiftPhase.Closed || next < _phaseEndTick)
        {
            return;
        }

        if (_phase == ShiftPhase.Prep)
        {
            _phase = ShiftPhase.Delivery;
            _open = true;
            _closeTick = next + _deliveryTicks;
            _deadlineTick = _closeTick;
            _phaseEndTick = _closeTick;
        }
        else
        {
            _phase = ShiftPhase.Closed;
            _open = false;
        }

        _mail.SetDeliveriesState(_open, _closeTick, _mask);
        var payload = new DeliveriesStatePayload
        {
            CloseTick = _closeTick,
            DeadlineTick = _deadlineTick,
            UnlockedMask = _mask,
            Phase = (byte)_phase,
            Open = _open ? (byte)1 : (byte)0,
        };
        _ = _events.TryRaise(RunEventKinds.DeliveriesState, RunEventDescriptors.AudienceAll, in payload);
    }

    /// <summary>The dump section: <c>phase u8, phaseEndTick u32, open u8, closeTick u32, deadlineTick u32, unlockedMask u64</c>.</summary>
    /// <param name="w">The writer.</param>
    public void Write(ref CanonicalWriter w)
    {
        w.WriteU8((byte)_phase);
        w.WriteU32(_phaseEndTick);
        w.WriteBool(_open);
        w.WriteU32(_closeTick);
        w.WriteU32(_deadlineTick);
        w.WriteU64(_mask);
    }
}
