using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// The hold counter at <see cref="PreviewSubStep.HoldAdvance"/> (4e) (ADR-0014 section 4; ADR-0016 section 2; GDD P11, P12;
/// input-controls F2, G1; TR-movement-011, TR-movement-012). It runs after <see cref="EdgeResolveStep"/> (4d), which has already
/// applied this tick's press, release and cancel edges (Evan approved 2026-10-29 (TD-186 item 1)); this step reads no edge. While
/// <see cref="InteractState.Holding"/>, on every held tick up to and including the completion tick, in this order:
/// <list type="number">
/// <item>The cancels, first match wins: the latched anchor not registered in the current context
/// (<see cref="CancelReason.Unregistered"/>); chest-to-anchor distance² strictly greater than
/// <c>(RangeMm + incumbentSlackMm)²</c> in <see langword="long"/>, from the current feet (<see cref="CancelReason.OutOfRange"/>,
/// H-F2-1: 2900 mm keeps, 2901 mm cancels); the owner's <c>IsAvailable</c> false (<see cref="CancelReason.Unavailable"/> with its
/// key); the owner's <c>ShouldCancel</c> (<see cref="CancelReason.OwnerCancel"/>, its key unchanged); a context change since the
/// previous tick (<see cref="InteractionGate.ContextCancelReason"/>: Panel, Mounted, Dead, Swim, Knockdown). Walking and anything
/// else that leaves the status unchanged never cancel.</item>
/// <item>On tick <c>PressTick + HoldTicks</c> (<c>heldTicks = tick − PressTick</c>): <see cref="IInteractable.TryBuildRequest"/> with
/// the latch (its <c>HoldTicks</c>, <c>OwnerPayload</c> and <c>Claimed</c>) and one <see cref="IRequestSink.TrySend"/>, then
/// <see cref="InteractState.Complete"/>, which never sends again (G6) and waits for 4d's release edge. A refused send is
/// <see cref="CancelReason.Busy"/>, Idle and no retry (Evan approved 2026-10-29 (TD-186 item 2)).</item>
/// </list>
/// A <see cref="InteractState.Channel"/> ends only on a context change (a Catch channel sends <c>CatchArm{up}</c>). The latch is
/// never re-targeted to the focus and the catch predictor is never read, so a latched hold ignores a catch. After the state is
/// settled, <see cref="FocusState.Latched"/> is set exactly while the state is Holding or Channel. Counts
/// <see cref="CounterId.HoldsCompleted"/> and <see cref="CounterId.HoldsCancelled"/> (Evan approved 2026-10-29 (TD-186 item 4)).
/// The host never times a hold and no request carries a duration. Reads only its constructor ports, never <c>SimWorld</c>.
/// Allocation free; not thread-safe.
/// </summary>
/// <example>
/// <code>
/// var hold = new HoldAdvanceStep(edgeResolve, focus, source, players.Anchors, settings, inputSettings, catchArm, requests, layerCounters, self);
/// builder.BindPreview(PreviewSubStep.HoldAdvance, hold);
/// </code>
/// </example>
public sealed class HoldAdvanceStep : IClientStep
{
    private readonly EdgeResolveStep _press;
    private readonly FocusStep _focus;
    private readonly ICarrierFocusSource _source;
    private readonly AnchorRegistry _anchors;
    private readonly PlayerSettings _settings;
    private readonly IClientInputSettings _input;
    private readonly ICatchArmSender _catchArm;
    private readonly IRequestSink _requests;
    private readonly SimCounters _counters;
    private readonly PlayerSlot _self;
    private readonly HoldSettings _hold;
    private CarrierStatus _previous;

    /// <summary>Creates the step.</summary>
    /// <param name="press">The 4d step whose <see cref="EdgeResolveStep.Interaction"/> this step advances and ends.</param>
    /// <param name="focus">The 4a step whose <see cref="FocusState.Latched"/> this step writes.</param>
    /// <param name="source">The local carrier's feet and status.</param>
    /// <param name="anchors">The anchor registry (read only).</param>
    /// <param name="settings">The players settings (<c>ChestMm</c>, <c>IncumbentSlackMm</c>, the hold constants).</param>
    /// <param name="input">The client input settings (<c>HoldSettingMs</c> for the hook context only; the hold length is latched).</param>
    /// <param name="catchArm">Throwing's <c>CatchArm</c> sender (<c>NullCatchArmSender</c> at M-1a).</param>
    /// <param name="requests">The ClientLayer's request port.</param>
    /// <param name="counters">The ClientLayer's counters.</param>
    /// <param name="self">The local player's slot, for <see cref="InteractionContext.Actor"/>.</param>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    public HoldAdvanceStep(
        EdgeResolveStep press,
        FocusStep focus,
        ICarrierFocusSource source,
        AnchorRegistry anchors,
        PlayerSettings settings,
        IClientInputSettings input,
        ICatchArmSender catchArm,
        IRequestSink requests,
        SimCounters counters,
        PlayerSlot self)
    {
        ArgumentNullException.ThrowIfNull(press);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(catchArm);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(counters);
        _press = press;
        _focus = focus;
        _source = source;
        _anchors = anchors;
        _settings = settings;
        _input = input;
        _catchArm = catchArm;
        _requests = requests;
        _counters = counters;
        _self = self;
        _hold = HoldSettings.FromPlayerSettings(settings);
    }

    /// <inheritdoc />
    public void Step(in ClientStepContext ctx)
    {
        ref InteractionStatus s = ref _press.Interaction;
        ref readonly CarrierStatus now = ref _source.Status;
        CarrierStatus previous = _previous;
        _previous = now;

        // The press tick's context is the one the press was resolved against, so context cancels compare from the next tick on
        // (a catch armed on the tick swimming starts is kept). Mail Dev default, pending Evan (TD-191 item 5).
        CancelReason context = s.Latch.PressTick == ctx.ClientTick ? CancelReason.None : InteractionGate.ContextCancelReason(in previous, in now);
        switch (s.State)
        {
            case InteractState.Holding:
                AdvanceHold(ref s, context, in now, in ctx);
                break;
            case InteractState.Channel when context != CancelReason.None:
                // A channel has no anchor-side cancels at M-1a: only the context ends it (Mail Dev default, pending Evan (TD-191 item 6)).
                if (s.Latch.Verb == Verb.Catch)
                {
                    _ = _catchArm.Send(down: false);
                }

                End(ref s, context, StringId.None);
                break;
            default:
                break;
        }

        _focus.State.Latched = s.State is InteractState.Holding or InteractState.Channel;
    }

    private void AdvanceHold(ref InteractionStatus s, CancelReason context, in CarrierStatus now, in ClientStepContext ctx)
    {
        InteractionLatch latch = s.Latch;

        // The current context's registration, as at press: entering build mode mid-hold reads as unregistered.
        // Mail Dev default, pending Evan (TD-191 item 7).
        byte mask = now.Body == BodyContext.Build ? AnchorRegistry.ContextBuild : AnchorRegistry.ContextWorld;
        if (!_anchors.TryGet(latch.Anchor, mask, out int index))
        {
            Cancel(ref s, CancelReason.Unregistered, StringId.None);
            return;
        }

        ref readonly AnchorDef def = ref _anchors.DefAt(index);
        Vec3Mm chest = FocusScore.Chest(_source.Feet, _settings.ChestMm);
        long dx = (long)def.Point.X - chest.X;
        long dy = (long)def.Point.Y - chest.Y;
        long dz = (long)def.Point.Z - chest.Z;
        long reach = (long)def.RangeMm + _settings.IncumbentSlackMm;
        if ((dx * dx) + (dy * dy) + (dz * dz) > reach * reach)
        {
            Cancel(ref s, CancelReason.OutOfRange, StringId.None);
            return;
        }

        IInteractable impl = _anchors.ImplAt(index);
        var hooks = new InteractionContext
        {
            Tick = ctx.HostClock.IsValid ? ctx.HostClock.EstimatedHostTick : 0u,
            Actor = _self,
            ChestMm = chest,
            HoldSettingMs = _input.HoldSettingMs,
            HoldSettings = _hold,
            IsHost = 0,
        };
        InteractionContext c = PressResolver.ForAnchor(in hooks, in def, _anchors.ContextMaskAt(index));
        if (!impl.IsAvailable(in c, out StringId reason))
        {
            Cancel(ref s, CancelReason.Unavailable, reason);
            return;
        }

        if (impl.ShouldCancel(in latch, in c, out reason))
        {
            Cancel(ref s, CancelReason.OwnerCancel, reason);
            return;
        }

        if (context != CancelReason.None)
        {
            Cancel(ref s, context, StringId.None);
            return;
        }

        uint held = ctx.ClientTick - latch.PressTick;
        if (held < (long)latch.HoldTicks)
        {
            return;
        }

        // One commit. TryBuildRequest false sends nothing and still completes, as a declined Tap records no cancel.
        // Mail Dev default, pending Evan (TD-191 item 8).
        Op op = default;
        if (impl.TryBuildRequest(in latch, in c, ref op) && !_requests.TrySend(in op))
        {
            Cancel(ref s, CancelReason.Busy, StringId.None);   // Evan approved 2026-10-29 (TD-186 item 2): no retry
            return;
        }

        s.State = InteractState.Complete;
        _counters.Add(CounterId.HoldsCompleted);
    }

    private void Cancel(ref InteractionStatus s, CancelReason reason, StringId key)
    {
        End(ref s, reason, key);
        _counters.Add(CounterId.HoldsCancelled);
    }

    private static void End(ref InteractionStatus s, CancelReason reason, StringId key)
    {
        s.State = InteractState.Idle;
        s.LastCancel = reason;
        s.LastCancelReason = key;
    }
}
