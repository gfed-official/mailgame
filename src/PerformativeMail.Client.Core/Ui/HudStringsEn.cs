using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Ui;

/// <summary>The M-1a English string table (H11). Formats use the tokens <c>{glyph} {address} {money} {pieces} {n} {stacks}</c> only.</summary>
public static class HudStringsEn
{
    private static readonly Dictionary<StringId, string> _rows = new()
    {
        [DeliverPromptKeys.VerbHold] = "Hold {glyph} Deliver · {address}",
        [DeliverPromptKeys.VerbToggle] = "Press {glyph} Deliver · {address}",
        [DeliverPromptKeys.VerbArmed] = "Armed — {glyph} cancel · {address}",
        [DeliverPromptKeys.MatchPieces] = "{pieces}",
        [DeliverPromptKeys.MatchStacks] = "{stacks} stacks",
        [DeliverPromptKeys.MatchPay] = "{money}",
        [DeliverPromptKeys.MatchDeadOnly] = "Clears {n} dead letters · {money}",
        [DeliverPromptKeys.MismatchPieceFor] = "Wrong house — piece is for {address}",
        [DeliverPromptKeys.MismatchPostOne] = "Posts {n} · {money}",
        [DeliverPromptKeys.FloorRefusal] = "Can't afford a wrong house ({money})",
        [DeliverPromptKeys.Nothing] = "Nothing for {address}",
        [DeliverPromptKeys.ClosePanel] = "Close panel to deliver",
        [DeliverPromptKeys.DryLand] = "Dry land required to deliver",
        [DeliverPromptKeys.Pending] = "Delivering…",
        [DeliverPromptKeys.Sorting] = "Sorting…",
        [DeliverPromptKeys.MailRejectClosed] = "Closed — opens at Delivery",
        [DeliverPromptKeys.MailRejectNotOnRoute] = "Not on today's route",
    };

    /// <summary>The format string of <paramref name="id"/>, or null when the table has no row.</summary>
    /// <param name="id">A <see cref="DeliverPromptKeys"/> id.</param>
    /// <returns>The format or null.</returns>
    public static string? Format(StringId id) => _rows.GetValueOrDefault(id);

    /// <summary>"MM:SS" for whole seconds left.</summary>
    /// <param name="seconds">Seconds, at least 0.</param>
    /// <returns>The text.</returns>
    public static string TimerText(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";
}
