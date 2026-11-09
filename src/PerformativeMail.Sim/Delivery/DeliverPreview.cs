using System.Diagnostics;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Delivery;

/// <summary>
/// The deliver preview (Manual Delivery MD3, F1; TR-delivery-003, TR-delivery-013; ADR-0016 section 3): a pure, allocation-free
/// function over facts that the ClientLayer runs every focused tick with no cache. It scans the panel through
/// <see cref="DeliverScan"/>, decides each entry with Mail's <see cref="MailRules.Evaluate"/> and prices it with Mail's
/// <see cref="MailPay"/> functions (one F6 rounding per unit, <c>thrownPm</c> 1000); it never copies a rule or a rounding.
/// </summary>
/// <example>
/// <code>
/// var scratch = new DeliverPreviewScratch();
/// DeliverBranch b = DeliverPreview.Preview(in dest, panel, selectedSlot: 3, currentShift: 3, balance, stats, wallet, floor, ref scratch);
/// </code>
/// </example>
public static class DeliverPreview
{
    /// <summary>A hand delivery never gets Cannon Arm (F1 <c>thrownPm</c>).</summary>
    private const int HandThrownPm = 1000;

    /// <summary>
    /// Computes the branch, checking in MD3 order (first match wins): REJECT when <paramref name="d"/> is CLOSED or LOCKED (Evaluate's
    /// mailbox-level steps; no panel scan); MATCH when any scanned entry evaluates to Accept; MISDELIVER_ONE when the selected hotbar
    /// slot holds mail evaluating to Misdeliver (or mail of a kind the type does not accept, flagged <see cref="DeliverBranch.WontFit"/>);
    /// otherwise NOTHING. The selected entry is scanned like any other, so a selected entry that is the only match is MATCH.
    /// </summary>
    /// <param name="d">The destination facts, with its state at the current tick (or a hold's start tick).</param>
    /// <param name="panel">The player's own panel (displayed mirror on the client, authoritative on the host).</param>
    /// <param name="selectedSlot">The selected hotbar slot (0 is hands).</param>
    /// <param name="currentShift">The client's current shift (F5 timeliness input).</param>
    /// <param name="balance">The Mail balance (<c>deadLetterShifts</c>, <c>misdeliveryPenaltyRatio</c>, <c>minPenaltyCents</c>).</param>
    /// <param name="stats">The stat sheet (<c>MailValueMultPm</c>, <c>LateValueRatioPm</c>, <c>MisdeliveryPenaltyMultPm</c>).</param>
    /// <param name="wallet">The replicated team wallet.</param>
    /// <param name="walletFloor">The lowest balance a misdelivery may reach (landing on it is allowed).</param>
    /// <param name="s">The caller's scratch; test builds count evaluations and scanned entries in it.</param>
    /// <returns>The branch and its payload.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="panel"/>, <paramref name="balance"/> or <paramref name="stats"/> is null.</exception>
    public static DeliverBranch Preview(
        in DestinationFacts d,
        IPanelView panel,
        int selectedSlot,
        int currentShift,
        MailBalance balance,
        IStatSheet stats,
        Cents wallet,
        Cents walletFloor,
        ref DeliverPreviewScratch s)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(balance);
        ArgumentNullException.ThrowIfNull(stats);
        s.CountEvaluation();
        DeliverBranch b = default;

        // REJECT: Evaluate's first two steps decide on the destination alone, so a unit-less probe reads them.
        UnitFacts none = default;
        MailRejectReason gate = MailRules.Evaluate(in d, in none).Reason;
        if (gate == MailRejectReason.Closed || gate == MailRejectReason.NotOnRoute)
        {
            b.Kind = DeliverBranchKind.Reject;
            b.RejectReason = gate;
            return b;
        }

        // Display-only figures inside documented ranges: nothing to count, so the sink drops (as the geometry helpers do).
        var sink = default(SimCounterSink);
        int lateValueRatioPm = 0;
        long pay = 0;
        foreach (PanelEntryFacts e in DeliverScan.Enumerate(panel))
        {
            s.CountScanned();
            UnitFacts u = UnitOf(in e, e.Run(0));
            if (MailRules.Evaluate(in d, in u).Result != EvalResult.Accept)
            {
                continue;
            }

            if (b.K == 0)
            {
                lateValueRatioPm = MailPay.LateValueRatioPm(stats);
            }

            b.AddMatch(e.EntryId);
            b.N += e.Units;
            if (e.KindKey < 64)
            {
                b.KindsPresent |= 1UL << e.KindKey;
            }

            for (int r = 0; r < e.RunCount; r++)
            {
                PanelHintRun run = e.Run(r);
                UnitFacts unit = UnitOf(in e, run);
                int t = MailPay.Timeliness(currentShift, run.DeadlineShift, lateValueRatioPm, balance.DeadLetterShifts);
                pay += run.Count * MailPay.UnitPay(in unit, t, HandThrownPm, stats, null, ref sink).Value;
                MailPay.Overdue(currentShift, run.DeadlineShift, balance.DeadLetterShifts, out bool late, out bool dead);
                b.LateUnits += late ? run.Count : 0;
                b.DeadUnits += dead ? run.Count : 0;
            }
        }

        if (b.K > 0)
        {
            b.Kind = DeliverBranchKind.Match;
            b.PredictedPay = Cents.FromCents(pay, ref sink);
            b.DeadOnly = b.DeadUnits == b.N;
            return b;
        }

        b.Kind = DeliverBranchKind.Nothing;
        if ((uint)selectedSlot >= (uint)panel.EntryCount(PanelSection.Hotbar))
        {
            return b;
        }

        PanelEntryFacts selected = panel.Entry(PanelSection.Hotbar, selectedSlot);
        if (!selected.IsEntry || selected.Category != ItemCategory.Mail)
        {
            return b;
        }

        UnitFacts oldest = UnitOf(in selected, selected.Run(0));
        DeliverEval eval = MailRules.Evaluate(in d, in oldest);
        bool wontFit = eval.Result == EvalResult.Reject && eval.Reason == MailRejectReason.WontFit;
        if (eval.Result != EvalResult.Misdeliver && !wontFit)
        {
            return b;
        }

        b.Kind = DeliverBranchKind.MisdeliverOne;
        b.SelectedEntry = selected.EntryId;
        b.SelectedAddress = selected.Address;
        b.Cursed = (selected.Flags & (byte)MailFlags.Cursed) != 0;
        b.WontFit = wontFit;
        if (!wontFit)
        {
            Cents penalty = MailPay.UnitPenalty(in oldest, balance.MisdeliveryPenaltyRatio, balance.MinPenaltyCents, stats, ref sink);
            b.PredictedPenalty = penalty;
            b.FloorRefusal = !MailPay.WalletGuard(wallet, penalty, walletFloor);
        }

        return b;
    }

    // The unit Evaluate and F6/F7 read: the entry's kind, category, address and flags with one run's value and deadline.
    private static UnitFacts UnitOf(in PanelEntryFacts e, PanelHintRun run) =>
        new UnitFacts(DefIndex.FromValue(e.KindKey), e.Category, e.Address, run.UnitSpawnValue, run.DeadlineShift, (MailFlags)e.Flags);
}

/// <summary>
/// The caller-owned scratch of <see cref="DeliverPreview.Preview"/> (the Sim holds no mutable static state, ADR-0003 section 4). In
/// test builds (<c>SIM_ASSERT</c>) it counts preview evaluations and scanned entries, MD3's test seams; elsewhere both stay 0.
/// </summary>
/// <example>
/// <code>
/// var scratch = new DeliverPreviewScratch();
/// _ = DeliverPreview.Preview(in dest, panel, 1, 3, balance, stats, wallet, floor, ref scratch);   // scratch.Evaluations == 1 in Debug
/// </code>
/// </example>
public struct DeliverPreviewScratch
{
    /// <summary>Preview evaluations so far (test builds only).</summary>
    public int Evaluations { get; private set; }

    /// <summary>Panel entries scanned so far (test builds only).</summary>
    public int ScannedEntries { get; private set; }

    [Conditional("SIM_ASSERT")]
    internal void CountEvaluation() => Evaluations++;

    [Conditional("SIM_ASSERT")]
    internal void CountScanned() => ScannedEntries++;
}
