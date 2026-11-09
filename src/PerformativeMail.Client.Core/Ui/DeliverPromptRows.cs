using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Ui;

/// <summary>The verdict mark of line 1; chosen from the Sim's branch only.</summary>
public enum PromptMark : byte
{
    /// <summary>No mark.</summary>
    None = 0,

    /// <summary>MATCH.</summary>
    Match = 1,

    /// <summary>MISDELIVER_ONE.</summary>
    Mismatch = 2,
}

/// <summary>Up to three text lines and the mark; empty lines are not shown.</summary>
public readonly record struct PromptRows(PromptMark Mark, bool Dim, string Line1, string Line2, string Line3);

/// <summary>Builds the Deliver prompt's rows from the decoded prompt (manual-delivery UI Requirements). Godot-free; call only on <see cref="HudDirty.Prompt"/>.</summary>
public static class DeliverPromptRows
{
    /// <summary>No rows: nothing to show.</summary>
    public static readonly PromptRows Empty = new(PromptMark.None, false, string.Empty, string.Empty, string.Empty);

    /// <summary>Builds the rows.</summary>
    /// <param name="p">The prompt.</param>
    /// <param name="money">The one money formatter.</param>
    /// <param name="text">Address text source.</param>
    /// <param name="glyph">The binding glyph, e.g. "[E]".</param>
    /// <param name="reasons">The framework reason ids (non-Deliver verbs).</param>
    /// <returns>The rows.</returns>
    public static PromptRows Build(in DeliverPrompt p, IMoneyFormatter money, IHudText text, string glyph, FrameworkReasons reasons)
    {
        if (!p.Shown)
        {
            return Empty;
        }

        string address = Address(p.Address, text);
        string verb = Fmt(p.State == InteractState.ToggleArmed ? DeliverPromptKeys.VerbArmed : DeliverPromptKeys.VerbHold, glyph, address, string.Empty, 0, string.Empty);
        switch (p.Branch)
        {
            case DeliverPromptBranch.Match:
                bool deadOnly = p.PredictedPay == 0;
                string line2 = deadOnly
                    ? Fmt(DeliverPromptKeys.MatchDeadOnly, glyph, address, money.Format(0, MoneySign.Delta), p.Units, string.Empty)
                    : Fmt(DeliverPromptKeys.MatchPieces, glyph, address, string.Empty, p.Units, p.Units == 1 ? "1 piece" : p.Units + " pieces")
                      + " · " + Fmt(DeliverPromptKeys.MatchPay, glyph, address, money.Format(p.PredictedPay, MoneySign.Delta), 0, string.Empty);
                return new PromptRows(PromptMark.Match, false, "✓ " + verb, line2, string.Empty);
            case DeliverPromptBranch.MisdeliverOne:
                // The args carry the piece's address only; the mailbox address is not written for this branch (flagged), so line 1 has none.
                string penalty = money.Format(-p.PredictedPenalty, MoneySign.Delta);
                string v = Fmt(DeliverPromptKeys.VerbHold, glyph, string.Empty, string.Empty, 0, string.Empty).TrimEnd(' ', '·');
                return new PromptRows(
                    PromptMark.Mismatch,
                    false,
                    "✗ " + v,
                    Fmt(DeliverPromptKeys.MismatchPieceFor, glyph, address, string.Empty, 0, string.Empty),
                    (p.Flags & DeliverPromptFlags.FloorRefusal) != 0
                        ? Fmt(DeliverPromptKeys.FloorRefusal, glyph, address, penalty, 0, string.Empty)
                        : Fmt(DeliverPromptKeys.MismatchPostOne, glyph, address, penalty, 1, string.Empty));
            case DeliverPromptBranch.Nothing:
                return new PromptRows(PromptMark.None, true, Fmt(DeliverPromptKeys.Nothing, glyph, address, string.Empty, 0, string.Empty), string.Empty, string.Empty);
            case DeliverPromptBranch.Reject:
            case DeliverPromptBranch.Unavailable:
                return new PromptRows(PromptMark.None, false, Fmt(p.Key, glyph, address, string.Empty, 0, string.Empty), string.Empty, string.Empty);
            default:
                string reason = p.Key == reasons.ClosePanel ? Fmt(DeliverPromptKeys.ClosePanel, glyph, address, string.Empty, 0, string.Empty)
                    : p.Key == reasons.DryLand ? Fmt(DeliverPromptKeys.DryLand, glyph, address, string.Empty, 0, string.Empty)
                    : string.Empty;
                return new PromptRows(PromptMark.None, false, reason, string.Empty, string.Empty);
        }
    }

    private static string Address(StringId id, IHudText text) => !id.IsNone && text.TryGetDisplayText(id, out string t) ? t : "?";

    private static string Fmt(StringId key, string glyph, string address, string money, int n, string pieces) =>
        (HudStringsEn.Format(key) ?? string.Empty)
            .Replace("{glyph}", glyph)
            .Replace("{address}", address)
            .Replace("{money}", money)
            .Replace("{n}", n.ToString())
            .Replace("{pieces}", pieces);
}
