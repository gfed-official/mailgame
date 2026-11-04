using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// Publishes the <see cref="InteractionSnapshot"/> at <see cref="PreviewSubStep.HoldAdvance"/> (4e), bound right after
/// <see cref="HoldAdvanceStep"/>, so it reflects this tick's focus (4a), press (4d) and hold (4e) (ADR-0014 section 7; ADR-0016
/// section 2; TD-209 item 9, approved by Evan 2026-11-04 9:23 AM PT: "a separate small step that runs right after the hold step at
/// the end of each tick"). Per tick it builds the snapshot from <c>default</c>:
/// <list type="bullet">
/// <item>Focus: <see cref="FocusState.Focused"/>, its usable flag and reason, the focused anchor's verb and mode, and its owner's
/// <see cref="IInteractable.WritePrompt"/> into a fresh <see cref="PromptArgs"/>, called once, for the focused anchor only, usable
/// or not (TD-209 item 2). Nothing is written when nothing is focused, when the focused ref is not registered in this context (its
/// verb and mode then come from the latch; uncredited Mail Dev default pending Evan, TD-209 B2), or on a Dead tick, which publishes
/// no target and no reason (TD-209 item 6).</item>
/// <item>The too-far target from <see cref="FocusState.NearestBeyond"/>, and this tick's press prediction from
/// <see cref="EdgeResolveStep.LastPrediction"/> (TD-209 item 3).</item>
/// <item>The interaction: state, last cancel, and while Holding the latched anchor, <c>tick − PressTick</c>, the latched length and
/// <see cref="Hold.ProgressPm"/>; Channel keeps the latched anchor only (counts 0; TD-209 B3); Complete keeps the anchor with the ring
/// full (TD-209 item 5). Counts saturate at 32 767.</item>
/// </list>
/// <see cref="InteractionSnapshot.Seq"/> goes up by 1 only when bytes 6 to 83 differ from the previous publish (TD-209 item 7).
/// The two buffers swap per publish, so a reader's <c>ref readonly</c> from the previous tick still reads whole bytes. Reads only its
/// constructor ports, never <c>SimWorld</c>. Allocation free; not thread-safe.
/// </summary>
/// <example>
/// <code>
/// var publisher = new InteractionSnapshotPublisher(focus, press, source, players.Anchors, settings, inputSettings, self);
/// builder.BindPreview(PreviewSubStep.HoldAdvance, hold);
/// builder.BindPreview(PreviewSubStep.HoldAdvance, publisher);
/// IInteractionQuery query = publisher;
/// </code>
/// </example>
public sealed class InteractionSnapshotPublisher : IClientStep, IInteractionQuery
{
    private readonly FocusStep _focus;
    private readonly EdgeResolveStep _press;
    private readonly ICarrierFocusSource _source;
    private readonly AnchorRegistry _anchors;
    private readonly PlayerSettings _settings;
    private readonly IClientInputSettings _input;
    private readonly PlayerSlot _self;
    private readonly HoldSettings _hold;
    private InteractionSnapshot _a;
    private InteractionSnapshot _b;
    private bool _bIsCurrent;

    /// <summary>Creates the publisher.</summary>
    /// <param name="focus">The 4a step (focus, availability, too-far target).</param>
    /// <param name="press">The 4d step (interaction status and this tick's press prediction).</param>
    /// <param name="source">The local carrier's feet and status.</param>
    /// <param name="anchors">The anchor registry (read only).</param>
    /// <param name="settings">The players settings (<c>ChestMm</c> and the hold settings for the prompt context).</param>
    /// <param name="input">The client input settings (<c>HoldSettingMs</c> for the prompt context).</param>
    /// <param name="self">The local player's slot, for <see cref="InteractionContext.Actor"/>.</param>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    public InteractionSnapshotPublisher(
        FocusStep focus,
        EdgeResolveStep press,
        ICarrierFocusSource source,
        AnchorRegistry anchors,
        PlayerSettings settings,
        IClientInputSettings input,
        PlayerSlot self)
    {
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(press);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(input);
        _focus = focus;
        _press = press;
        _source = source;
        _anchors = anchors;
        _settings = settings;
        _input = input;
        _self = self;
        _hold = HoldSettings.FromPlayerSettings(settings);
    }

    /// <inheritdoc />
    public ref readonly InteractionSnapshot Current => ref _bIsCurrent ? ref _b : ref _a;

    /// <inheritdoc />
    public void Step(in ClientStepContext ctx)
    {
        ref readonly InteractionSnapshot previous = ref Current;
        ref InteractionSnapshot next = ref _bIsCurrent ? ref _a : ref _b;
        next = default;
        next.Tick = ctx.ClientTick;
        WriteFocus(ref next, in ctx);
        WriteInteraction(ref next, ctx.ClientTick);
        CatchPrediction p = _press.LastPrediction;
        next.CatchPredicted = p.Predicted;
        next.CatchFlightId = p.FlightId;
        next.Seq = SameContent(in previous, in next) ? previous.Seq : unchecked((ushort)(previous.Seq + 1));
        _bIsCurrent = !_bIsCurrent;
    }

    private static bool SameContent(in InteractionSnapshot x, in InteractionSnapshot y)
    {
        ReadOnlySpan<byte> a = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in x), 1));
        ReadOnlySpan<byte> b = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in y), 1));
        return a[InteractionSnapshot.ContentOffset..].SequenceEqual(b[InteractionSnapshot.ContentOffset..]);
    }

    private static short Saturate(long ticks) => (short)Math.Clamp(ticks, 0, short.MaxValue);

    private void WriteFocus(ref InteractionSnapshot next, in ClientStepContext ctx)
    {
        ref readonly FocusState f = ref _focus.State;
        if (_source.Status.Body == BodyContext.Dead)
        {
            return;
        }

        next.NearestBeyond = f.NearestBeyond;
        next.NearestBeyondRangeMm = f.NearestBeyondRangeMm;
        if (f.Focused.IsNone)
        {
            return;
        }

        next.Focused = f.Focused;
        next.Available = f.Available ? (byte)1 : (byte)0;
        next.Reason = f.Reason;
        int index = f.FocusedIndex;
        if (index < 0)
        {
            // Only while latched: the focused ref is the latched one, unregistered here (TD-209 item 2; verb and mode: TD-209 B2).
            InteractionLatch latch = _press.Interaction.Latch;
            if (latch.Anchor == f.Focused)
            {
                next.Verb = latch.Verb;
                next.Mode = latch.Mode;
            }

            return;
        }

        ref readonly AnchorDef def = ref _anchors.DefAt(index);
        next.Verb = def.Verb;
        next.Mode = def.Mode;
        var hooks = new InteractionContext
        {
            Tick = ctx.HostClock.IsValid ? ctx.HostClock.EstimatedHostTick : 0u,
            Actor = _self,
            ChestMm = FocusScore.Chest(_source.Feet, _settings.ChestMm),
            HoldSettingMs = _input.HoldSettingMs,
            HoldSettings = _hold,
            IsHost = 0,
        };
        InteractionContext c = PressResolver.ForAnchor(in hooks, in def, _anchors.ContextMaskAt(index));
        PromptArgs args = default;
        _anchors.ImplAt(index).WritePrompt(in c, ref args);
        next.PromptArgs = args;
    }

    private void WriteInteraction(ref InteractionSnapshot next, uint tick)
    {
        ref readonly InteractionStatus s = ref _press.Interaction;
        next.State = s.State;
        next.LastCancel = s.LastCancel;
        InteractionLatch latch = s.Latch;
        switch (s.State)
        {
            case InteractState.Holding:
                next.Latched = latch.Anchor;
                next.HeldTicks = Saturate(unchecked(tick - latch.PressTick));
                next.HoldTicks = Saturate(latch.HoldTicks);
                next.ProgressPm = (ushort)Hold.ProgressPm(next.HeldTicks, next.HoldTicks);
                break;
            case InteractState.Channel:
                next.Latched = latch.Anchor;
                break;
            case InteractState.Complete:
                next.Latched = latch.Anchor;
                next.HoldTicks = Saturate(latch.HoldTicks);
                next.HeldTicks = next.HoldTicks;
                next.ProgressPm = 1000;
                break;
            default:
                break;
        }
    }
}
