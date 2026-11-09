using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Delivery;

/// <summary>MD2's availability rows in their order (Manual Delivery MD2): the first matching row wins; <see cref="None"/> is <c>ok</c>.</summary>
public enum DeliverRow : byte
{
    /// <summary>No row applies: available.</summary>
    None = 0,

    /// <summary>Row 1: Panel context (<c>deliver.close_panel</c>; client only).</summary>
    ClosePanel = 1,

    /// <summary>Row 2: swim depth (<c>deliver.dry_land</c>).</summary>
    DryLand = 2,

    /// <summary>Row 3: an own non-optimistic op is pending (<c>deliver.pending</c>; client only).</summary>
    Pending = 3,

    /// <summary>Row 3 while the pending op is a sort (<c>deliver.sorting</c>; client only).</summary>
    Sorting = 4,

    /// <summary>Row 4: the destination is CLOSED (<c>mail.reject.closed</c>).</summary>
    Closed = 5,

    /// <summary>Row 5: the destination is LOCKED (<c>mail.reject.not_on_route</c>).</summary>
    NotOnRoute = 6,

    /// <summary>Row 6: the preview is NOTHING (<c>deliver.nothing</c>; client only).</summary>
    Nothing = 7,
}

/// <summary>
/// The inputs of <see cref="DeliverAvailability.Evaluate"/>, built by the client from its carrier status, its pending requests, the
/// <c>DestinationMirror</c> and this tick's preview, or by the host from host state with <see cref="HostFacts"/> set. Values only.
/// </summary>
/// <example><code>var f = new DeliverAvailabilityFacts { Swimming = status.Swimming, State = dest.State, PreviewNothing = b.Kind == DeliverBranchKind.Nothing };</code></example>
public readonly struct DeliverAvailabilityFacts
{
    /// <summary>Host state: only rows 2, 4 and 5 are evaluated (MD6); the client-only rows 1, 3 and 6 are skipped.</summary>
    public bool HostFacts { get; init; }

    /// <summary>Row 1: the actor is in Panel context.</summary>
    public bool PanelOpen { get; init; }

    /// <summary>Row 2: the actor is at swim depth.</summary>
    public bool Swimming { get; init; }

    /// <summary>Row 3: an own <c>deliver_matching</c>, <c>throw</c>, <c>drop</c> or <c>take_all</c> awaits its reply.</summary>
    public bool Pending { get; init; }

    /// <summary>Row 3: an own <c>sort</c> awaits its reply (shown as <c>deliver.sorting</c> when no other op is pending).</summary>
    public bool SortPending { get; init; }

    /// <summary>Rows 4 and 5: the destination's state.</summary>
    public DestState State { get; init; }

    /// <summary>Row 6: this tick's preview is NOTHING.</summary>
    public bool PreviewNothing { get; init; }
}

/// <summary>
/// MD2's one ordered availability list (TR-delivery-002; ADR-0014 section 1). The client and the host call the same function; the
/// host sets <see cref="DeliverAvailabilityFacts.HostFacts"/>. Row 3 is read from the pending requests the caller passes in, never
/// from a flag kept here. Pure, allocation-free.
/// </summary>
/// <example><code>DeliverRow row = DeliverAvailability.Evaluate(in facts);   // DeliverRow.ClosePanel with the panel open</code></example>
public static class DeliverAvailability
{
    /// <summary>The first MD2 row that applies to <paramref name="f"/>, else <see cref="DeliverRow.None"/>.</summary>
    /// <param name="f">The facts.</param>
    /// <returns>The row.</returns>
    public static DeliverRow Evaluate(in DeliverAvailabilityFacts f)
    {
        bool client = !f.HostFacts;
        if (client && f.PanelOpen)
        {
            return DeliverRow.ClosePanel;
        }

        if (f.Swimming)
        {
            return DeliverRow.DryLand;
        }

        if (client && (f.Pending || f.SortPending))
        {
            return f.Pending ? DeliverRow.Pending : DeliverRow.Sorting;
        }

        if (f.State == DestState.Closed)
        {
            return DeliverRow.Closed;
        }

        if (f.State == DestState.Locked)
        {
            return DeliverRow.NotOnRoute;
        }

        return client && f.PreviewNothing ? DeliverRow.Nothing : DeliverRow.None;
    }

    /// <summary>True for rows 1 to 5 (the rows that reset the settle tracker, MD3).</summary>
    /// <param name="row">The row.</param>
    /// <returns>True when <paramref name="row"/> is one of rows 1 to 5.</returns>
    public static bool BlocksVerb(DeliverRow row) => row is not DeliverRow.None and not DeliverRow.Nothing;

    /// <summary>The reason key of <paramref name="row"/>; <see cref="StringId.None"/> for <see cref="DeliverRow.None"/>.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The <see cref="DeliverPromptKeys"/> id.</returns>
    public static StringId Key(DeliverRow row) => row switch
    {
        DeliverRow.ClosePanel => DeliverPromptKeys.ClosePanel,
        DeliverRow.DryLand => DeliverPromptKeys.DryLand,
        DeliverRow.Pending => DeliverPromptKeys.Pending,
        DeliverRow.Sorting => DeliverPromptKeys.Sorting,
        DeliverRow.Closed => DeliverPromptKeys.MailRejectClosed,
        DeliverRow.NotOnRoute => DeliverPromptKeys.MailRejectNotOnRoute,
        DeliverRow.Nothing => DeliverPromptKeys.Nothing,
        _ => StringId.None,
    };

    /// <summary>The prompt's rows 1 to 3 (<see cref="DeliverPromptInput.Unavailable"/>) for <paramref name="row"/>.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The prompt value; <see cref="DeliverUnavailable.None"/> for rows 4 to 6 (the branch shows those).</returns>
    public static DeliverUnavailable PromptRow(DeliverRow row) => row switch
    {
        DeliverRow.ClosePanel => DeliverUnavailable.ClosePanel,
        DeliverRow.DryLand => DeliverUnavailable.DryLand,
        DeliverRow.Pending => DeliverUnavailable.Pending,
        DeliverRow.Sorting => DeliverUnavailable.Sorting,
        _ => DeliverUnavailable.None,
    };
}
