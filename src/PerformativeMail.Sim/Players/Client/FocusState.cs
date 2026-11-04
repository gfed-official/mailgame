using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// The local carrier's focus after <see cref="FocusStep"/> (ADR-0014 section 2): the focused anchor and its availability, the
/// dwell state, and the interaction latch. Values only. <see cref="Latched"/> is set and cleared by story 017's hold step at
/// <c>PreviewSubStep.HoldAdvance</c>; everything else is written by <see cref="FocusStep"/> only.
/// </summary>
/// <remarks>Story 018 adds the too-far target and <see cref="FocusedIndex"/>, read by <see cref="InteractionSnapshotPublisher"/>.</remarks>
/// <example><code>ref readonly FocusState f = ref focusStep.State; if (!f.Focused.IsNone &amp;&amp; !f.Available) { StringId why = f.Reason; }</code></example>
public struct FocusState
{
    /// <summary>The focused anchor, or <see cref="AnchorRef.None"/>.</summary>
    public AnchorRef Focused { get; internal set; }

    /// <summary>True when the focused anchor is available (gate ok and the owner's <c>IsAvailable</c> true); false shows it dimmed.</summary>
    public bool Available { get; internal set; }

    /// <summary>Why the focused anchor is unavailable (a gate or owner reason key); <see cref="StringId.None"/> when available or unfocused.</summary>
    public StringId Reason { get; internal set; }

    /// <summary>The challenger currently out-scoring the incumbent, or <see cref="AnchorRef.None"/>.</summary>
    public AnchorRef LeaderRef { get; internal set; }

    /// <summary>Consecutive ticks <see cref="LeaderRef"/> has led; it takes focus when this reaches <c>focusDwellTicks</c>.</summary>
    public int LeadTicks { get; internal set; }

    /// <summary>
    /// True while an interaction holds the focus (a hold or channel in progress, or toggle-armed; ADR-0014 l.71). While set, focus
    /// does not re-evaluate the incumbent and no challenger leads (D2, Approved by Evan 2026-10-22 8:24 PM PT (Mail Lead chat),
    /// TD-164).
    /// </summary>
    public bool Latched { get; internal set; }

    /// <summary>
    /// The nearest anchor that fails only the range test (context, gate, cone and line of sight pass) within
    /// <see cref="FocusStep.NearestBeyondMm"/> of the chest, or <see cref="AnchorRef.None"/>. Searched only on an unlatched tick
    /// that starts with nothing focused; empty on every other tick (TD-209 item 4 and open point (ii)).
    /// </summary>
    public AnchorRef NearestBeyond { get; internal set; }

    /// <summary>The chest distance of <see cref="NearestBeyond"/>, mm (at most <see cref="FocusStep.NearestBeyondMm"/>); 0 when it is empty.</summary>
    public int NearestBeyondRangeMm { get; internal set; }

    // Stored as index + 1 so that default(FocusState) reads -1.
    private int _focusedIndexPlusOne;

    /// <summary>
    /// The registry index of <see cref="Focused"/> in this tick's context, or -1 when nothing is focused or the focused ref is not
    /// registered in this context (only while latched). Valid for the tick <see cref="FocusStep"/> wrote it (TD-209 item 2).
    /// </summary>
    internal int FocusedIndex
    {
        readonly get => _focusedIndexPlusOne - 1;
        set => _focusedIndexPlusOne = value + 1;
    }
}
