using System.Collections.Immutable;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Delivery;

/// <summary>One Deliver prompt key: its id and its ASCII spelling (Manual Delivery GDD Localisation keys, l.432).</summary>
/// <param name="Id">The key's id in <see cref="StringNamespace.Reason"/>.</param>
/// <param name="Spelling">The key as the GDD spells it.</param>
/// <example><code>DeliverPromptKey k = DeliverPromptKeys.All[0];   // (Reason:16, "deliver.verb.hold")</code></example>
public readonly record struct DeliverPromptKey(StringId Id, string Spelling);

/// <summary>
/// The prompt keys <see cref="DeliverInteractable"/> writes (manual-delivery story 010 AC-4; ADR-0004 section 6 erratum 2026-10-23), in the
/// <see cref="FrameworkReasons"/> shape: one <see cref="StringId"/> per key in <see cref="StringNamespace.Reason"/> plus its spelling.
/// Indices 0 to 15 are the framework's <c>reason.*</c> keys (<c>PlayerReasons</c>); Deliver's take 16 upward in the order of the GDD's
/// Localisation keys line (the <c>a11y.deliver.*</c> variants are story 012's, deferred), then the two <c>mail.reject.*</c> keys MD2 rows 4
/// and 5 reuse. Append-only: never renumber one. The string table that spells them for players stays with TD-119; no key holds a money
/// sign (money is a <c>{money}</c> argument).
/// </summary>
/// <example><code>StringId key = DeliverPromptKeys.MatchPieces;   // (Reason, 19), "deliver.match.pieces"</code></example>
public static class DeliverPromptKeys
{
    /// <summary>The first index Deliver uses (0 to 15 are the framework's).</summary>
    public const ushort FirstIndex = 16;

    /// <summary>The spelling of <see cref="VerbHold"/>.</summary>
    public const string VerbHoldKey = "deliver.verb.hold";

    /// <summary><c>deliver.verb.hold</c>: the hold verb line ({glyph, address}).</summary>
    public static readonly StringId VerbHold = new(StringNamespace.Reason, FirstIndex + 0);

    /// <summary>The spelling of <see cref="VerbToggle"/>.</summary>
    public const string VerbToggleKey = "deliver.verb.toggle";

    /// <summary><c>deliver.verb.toggle</c>: the toggle verb line ({glyph, address}).</summary>
    public static readonly StringId VerbToggle = new(StringNamespace.Reason, FirstIndex + 1);

    /// <summary>The spelling of <see cref="VerbArmed"/>.</summary>
    public const string VerbArmedKey = "deliver.verb.armed";

    /// <summary><c>deliver.verb.armed</c>: the armed toggle line ({glyph, address}).</summary>
    public static readonly StringId VerbArmed = new(StringNamespace.Reason, FirstIndex + 2);

    /// <summary>The spelling of <see cref="MatchPieces"/>.</summary>
    public const string MatchPiecesKey = "deliver.match.pieces";

    /// <summary><c>deliver.match.pieces</c>: MATCH: the pieces line ({pieces}, plural).</summary>
    public static readonly StringId MatchPieces = new(StringNamespace.Reason, FirstIndex + 3);

    /// <summary>The spelling of <see cref="MatchStacks"/>.</summary>
    public const string MatchStacksKey = "deliver.match.stacks";

    /// <summary><c>deliver.match.stacks</c>: MATCH: the stacks line ({stacks}; omitted when k &lt; 2).</summary>
    public static readonly StringId MatchStacks = new(StringNamespace.Reason, FirstIndex + 4);

    /// <summary>The spelling of <see cref="MatchKind"/>.</summary>
    public const string MatchKindKey = "deliver.match.kind";

    /// <summary><c>deliver.match.kind</c>: MATCH: a kind chip ({kind, count}).</summary>
    public static readonly StringId MatchKind = new(StringNamespace.Reason, FirstIndex + 5);

    /// <summary>The spelling of <see cref="MatchPay"/>.</summary>
    public const string MatchPayKey = "deliver.match.pay";

    /// <summary><c>deliver.match.pay</c>: MATCH: the predicted pay ({money}).</summary>
    public static readonly StringId MatchPay = new(StringNamespace.Reason, FirstIndex + 6);

    /// <summary>The spelling of <see cref="MatchLate"/>.</summary>
    public const string MatchLateKey = "deliver.match.late";

    /// <summary><c>deliver.match.late</c>: MATCH: the late units ({n}).</summary>
    public static readonly StringId MatchLate = new(StringNamespace.Reason, FirstIndex + 7);

    /// <summary>The spelling of <see cref="MatchDeadOnly"/>.</summary>
    public const string MatchDeadOnlyKey = "deliver.match.dead_only";

    /// <summary><c>deliver.match.dead_only</c>: MATCH: only dead letters, "clears" ({n}).</summary>
    public static readonly StringId MatchDeadOnly = new(StringNamespace.Reason, FirstIndex + 8);

    /// <summary>The spelling of <see cref="KeepRaised"/>.</summary>
    public const string KeepRaisedKey = "deliver.keep_raised";

    /// <summary><c>deliver.keep_raised</c>: the raised-piece note ({address}).</summary>
    public static readonly StringId KeepRaised = new(StringNamespace.Reason, FirstIndex + 9);

    /// <summary>The spelling of <see cref="MismatchPieceFor"/>.</summary>
    public const string MismatchPieceForKey = "deliver.mismatch.piece_for";

    /// <summary><c>deliver.mismatch.piece_for</c>: MISDELIVER_ONE: the piece's address line ({address}).</summary>
    public static readonly StringId MismatchPieceFor = new(StringNamespace.Reason, FirstIndex + 10);

    /// <summary>The spelling of <see cref="MismatchPostOne"/>.</summary>
    public const string MismatchPostOneKey = "deliver.mismatch.post_one";

    /// <summary><c>deliver.mismatch.post_one</c>: MISDELIVER_ONE: the penalty line ({money}).</summary>
    public static readonly StringId MismatchPostOne = new(StringNamespace.Reason, FirstIndex + 11);

    /// <summary>The spelling of <see cref="MismatchCursed"/>.</summary>
    public const string MismatchCursedKey = "deliver.mismatch.cursed";

    /// <summary><c>deliver.mismatch.cursed</c>: MISDELIVER_ONE: the Cursed chip tooltip.</summary>
    public static readonly StringId MismatchCursed = new(StringNamespace.Reason, FirstIndex + 12);

    /// <summary>The spelling of <see cref="FloorRefusal"/>.</summary>
    public const string FloorRefusalKey = "deliver.floor_refusal";

    /// <summary><c>deliver.floor_refusal</c>: MISDELIVER_ONE: the wallet-floor refusal line ({money}).</summary>
    public static readonly StringId FloorRefusal = new(StringNamespace.Reason, FirstIndex + 13);

    /// <summary>The spelling of <see cref="Nothing"/>.</summary>
    public const string NothingKey = "deliver.nothing";

    /// <summary><c>deliver.nothing</c>: NOTHING (MD2 row 6).</summary>
    public static readonly StringId Nothing = new(StringNamespace.Reason, FirstIndex + 14);

    /// <summary>The spelling of <see cref="ClosePanel"/>.</summary>
    public const string ClosePanelKey = "deliver.close_panel";

    /// <summary><c>deliver.close_panel</c>: MD2 row 1: the panel is open.</summary>
    public static readonly StringId ClosePanel = new(StringNamespace.Reason, FirstIndex + 15);

    /// <summary>The spelling of <see cref="DryLand"/>.</summary>
    public const string DryLandKey = "deliver.dry_land";

    /// <summary><c>deliver.dry_land</c>: MD2 row 2: swim depth.</summary>
    public static readonly StringId DryLand = new(StringNamespace.Reason, FirstIndex + 16);

    /// <summary>The spelling of <see cref="Pending"/>.</summary>
    public const string PendingKey = "deliver.pending";

    /// <summary><c>deliver.pending</c>: MD2 row 3: an own non-optimistic op is pending.</summary>
    public static readonly StringId Pending = new(StringNamespace.Reason, FirstIndex + 17);

    /// <summary>The spelling of <see cref="Sorting"/>.</summary>
    public const string SortingKey = "deliver.sorting";

    /// <summary><c>deliver.sorting</c>: MD2 row 3 while the pending op is a sort.</summary>
    public static readonly StringId Sorting = new(StringNamespace.Reason, FirstIndex + 18);

    /// <summary>The spelling of <see cref="ReplyTooFar"/>.</summary>
    public const string ReplyTooFarKey = "deliver.reply.too_far";

    /// <summary><c>deliver.reply.too_far</c>: a reply failure: out of range.</summary>
    public static readonly StringId ReplyTooFar = new(StringNamespace.Reason, FirstIndex + 19);

    /// <summary>The spelling of <see cref="ReplyTimeout"/>.</summary>
    public const string ReplyTimeoutKey = "deliver.reply.timeout";

    /// <summary><c>deliver.reply.timeout</c>: a reply failure: the request timed out.</summary>
    public static readonly StringId ReplyTimeout = new(StringNamespace.Reason, FirstIndex + 20);

    /// <summary>The spelling of <see cref="CancelSelectionChanged"/>.</summary>
    public const string CancelSelectionChangedKey = "deliver.cancel.selection_changed";

    /// <summary><c>deliver.cancel.selection_changed</c>: a hold cancel: the selection changed.</summary>
    public static readonly StringId CancelSelectionChanged = new(StringNamespace.Reason, FirstIndex + 21);

    /// <summary>The spelling of <see cref="CancelPromptChanged"/>.</summary>
    public const string CancelPromptChangedKey = "deliver.cancel.prompt_changed";

    /// <summary><c>deliver.cancel.prompt_changed</c>: a hold cancel: the prompt changed.</summary>
    public static readonly StringId CancelPromptChanged = new(StringNamespace.Reason, FirstIndex + 22);

    /// <summary>The spelling of <see cref="CancelSettle"/>.</summary>
    public const string CancelSettleKey = "deliver.cancel.settle";

    /// <summary><c>deliver.cancel.settle</c>: a hold cancel: the settle refused the press.</summary>
    public static readonly StringId CancelSettle = new(StringNamespace.Reason, FirstIndex + 23);

    /// <summary>The spelling of <see cref="MailRejectClosed"/>.</summary>
    public const string MailRejectClosedKey = "mail.reject.closed";

    /// <summary><c>mail.reject.closed</c>: MD2 row 4 / REJECT: the destination is CLOSED (Mail &amp; Destinations' key).</summary>
    public static readonly StringId MailRejectClosed = new(StringNamespace.Reason, FirstIndex + 24);

    /// <summary>The spelling of <see cref="MailRejectNotOnRoute"/>.</summary>
    public const string MailRejectNotOnRouteKey = "mail.reject.not_on_route";

    /// <summary><c>mail.reject.not_on_route</c>: MD2 row 5 / REJECT: the destination is LOCKED (Mail &amp; Destinations' key).</summary>
    public static readonly StringId MailRejectNotOnRoute = new(StringNamespace.Reason, FirstIndex + 25);

    /// <summary>Every key, in index order.</summary>
    public static ImmutableArray<DeliverPromptKey> All { get; } = ImmutableArray.Create(
        new DeliverPromptKey(VerbHold, VerbHoldKey),
        new DeliverPromptKey(VerbToggle, VerbToggleKey),
        new DeliverPromptKey(VerbArmed, VerbArmedKey),
        new DeliverPromptKey(MatchPieces, MatchPiecesKey),
        new DeliverPromptKey(MatchStacks, MatchStacksKey),
        new DeliverPromptKey(MatchKind, MatchKindKey),
        new DeliverPromptKey(MatchPay, MatchPayKey),
        new DeliverPromptKey(MatchLate, MatchLateKey),
        new DeliverPromptKey(MatchDeadOnly, MatchDeadOnlyKey),
        new DeliverPromptKey(KeepRaised, KeepRaisedKey),
        new DeliverPromptKey(MismatchPieceFor, MismatchPieceForKey),
        new DeliverPromptKey(MismatchPostOne, MismatchPostOneKey),
        new DeliverPromptKey(MismatchCursed, MismatchCursedKey),
        new DeliverPromptKey(FloorRefusal, FloorRefusalKey),
        new DeliverPromptKey(Nothing, NothingKey),
        new DeliverPromptKey(ClosePanel, ClosePanelKey),
        new DeliverPromptKey(DryLand, DryLandKey),
        new DeliverPromptKey(Pending, PendingKey),
        new DeliverPromptKey(Sorting, SortingKey),
        new DeliverPromptKey(ReplyTooFar, ReplyTooFarKey),
        new DeliverPromptKey(ReplyTimeout, ReplyTimeoutKey),
        new DeliverPromptKey(CancelSelectionChanged, CancelSelectionChangedKey),
        new DeliverPromptKey(CancelPromptChanged, CancelPromptChangedKey),
        new DeliverPromptKey(CancelSettle, CancelSettleKey),
        new DeliverPromptKey(MailRejectClosed, MailRejectClosedKey),
        new DeliverPromptKey(MailRejectNotOnRoute, MailRejectNotOnRouteKey));
}
