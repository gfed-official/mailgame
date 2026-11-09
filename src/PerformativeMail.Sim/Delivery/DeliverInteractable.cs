using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Delivery;

/// <summary>
/// The Deliver verb's <see cref="IInteractable"/> (Manual Delivery GDD MD1, F2, MD9; TR-delivery-001, TR-delivery-015; ADR-0014
/// sections 1 and 4). One instance serves every Deliver anchor (<see cref="DeliverAnchors.RegisterAll"/>); the hooks tell the
/// anchors apart by <see cref="InteractionContext.Anchor"/>. Every hook is pure: it reads its inputs and the injected stat sheet,
/// mutates nothing, raises nothing and allocates nothing. <see cref="InteractionContext"/> carries no stat sheet, so the sheet comes
/// in through the constructor.
/// <para>
/// Story 001 fills <see cref="GetHoldTicks"/>: the shared <c>holdEff</c> (<see cref="Hold.HoldTicks(int, int, int, in HoldSettings)"/>)
/// with the actor's <see cref="Stat.DeliverInsertPm"/>, read on every call and never cached. Story 010 adds the prompt writer
/// (<see cref="WritePrompt(in DeliverPromptInput, ref PromptArgs)"/>); the <see cref="IInteractable"/> hook calls it once story 003
/// supplies this tick's preview and availability. The other hooks return fixed values until stories 003 (<see cref="IsAvailable"/>)
/// and 004 (<see cref="OnPress"/>, <see cref="ShouldCancel"/>, <see cref="TryBuildRequest"/>) fill them.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var impl = new DeliverInteractable(statSheet);
/// RegisterResult r = DeliverAnchors.RegisterAll(players, registry, content, impl);
/// </code>
/// </example>
internal sealed class DeliverInteractable : IInteractable
{
    private readonly IStatSheet _stats;

    /// <summary>Creates the shared Deliver hooks over <paramref name="stats"/>.</summary>
    /// <param name="stats">The stat sheet read for <see cref="Stat.DeliverInsertPm"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
    public DeliverInteractable(IStatSheet stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        _stats = stats;
    }

    /// <summary>
    /// Available, with no reason key, until story 003 adds MD2's ordered rows. Story 005's host handler runs
    /// <c>IPlayersApi.CheckInteraction</c>, which asks this hook after the framework gate (swim depth is the gate's
    /// <c>reason.dry_land</c>), so an always-false stub would refuse every <c>deliver_matching</c>; CLOSED and LOCKED are judged by Mail
    /// at the hold's start inside <c>ConsumeHand</c> meanwhile.
    /// </summary>
    /// <param name="c">The call context.</param>
    /// <param name="reason"><see cref="StringId.None"/>.</param>
    /// <returns>True.</returns>
    public bool IsAvailable(in InteractionContext c, out StringId reason)
    {
        reason = StringId.None;
        return true;
    }

    /// <summary>
    /// The Deliver hold length (F2): <c>Hold.HoldTicks(holdBaseMs, insertPm, holdSettingMs, settings)</c> with
    /// <c>insertPm</c> the actor's personal <see cref="Stat.DeliverInsertPm"/> (the StatDef clamps it to 0 to 750; the shared
    /// function clamps again defensively). A pure function of the context and the sheet's current value.
    /// </summary>
    /// <param name="latch">The latch (unused: the length depends only on the anchor's base, the setting and the stat).</param>
    /// <param name="c">The call context.</param>
    /// <returns>The hold length in ticks.</returns>
    /// <example><code>int ticks = impl.GetHoldTicks(in latch, in c);   // 6 for base 400 at 400 ms with insertPm 500</code></example>
    public int GetHoldTicks(in InteractionLatch latch, in InteractionContext c)
    {
        int insertPm = _stats.Read(Stat.DeliverInsertPm, StatSheet.NoQualifier, c.Actor);
        HoldSettings settings = c.HoldSettings;   // a property read is a copy; Hold.HoldTicks takes it by in
        return Hold.HoldTicks(c.HoldBaseMs, insertPm, c.HoldSettingMs, in settings);
    }

    /// <summary>Never cancels until story 004 defines the cancel rule.</summary>
    /// <param name="latch">The latch.</param>
    /// <param name="c">The call context.</param>
    /// <param name="reason"><see cref="StringId.None"/>.</param>
    /// <returns>False.</returns>
    public bool ShouldCancel(in InteractionLatch latch, in InteractionContext c, out StringId reason)
    {
        reason = StringId.None;
        return false;
    }

    /// <summary>
    /// Writes nothing yet: <see cref="InteractionContext"/> carries no preview, panel or availability, so this hook calls
    /// <see cref="WritePrompt(in DeliverPromptInput, ref PromptArgs)"/> once story 003's preview step supplies the input.
    /// </summary>
    /// <param name="c">The call context.</param>
    /// <param name="args">The destination bytes (left untouched).</param>
    public void WritePrompt(in InteractionContext c, ref PromptArgs args)
    {
    }

    /// <summary>
    /// The Deliver prompt as data (manual-delivery story 010; Manual Delivery UI Requirements, MD2, MD3, MD10): the row's key and typed
    /// arguments in the <see cref="DeliverPromptArgs"/> layout. MD2 rows 1 to 3 (<see cref="DeliverPromptInput.Unavailable"/>) win; then
    /// by branch: REJECT <c>mail.reject.closed</c> / <c>mail.reject.not_on_route</c>; MATCH <c>deliver.match.pieces</c> with the units
    /// and the predicted pay; MISDELIVER_ONE <c>deliver.mismatch.piece_for</c> with the selected piece's address, one unit, the predicted
    /// penalty and the floor-refusal and won't-fit flags; NOTHING <c>deliver.nothing</c>. The address argument is the destination's
    /// except on MISDELIVER_ONE; the settling flag is added on any row. Cents are integers, clamped to i32; units saturate at u16. A pure
    /// function of its input: no state, no string, no allocation.
    /// </summary>
    /// <param name="input">The preview branch, availability, settle state and addresses.</param>
    /// <param name="args">The destination bytes (fully overwritten).</param>
    /// <example><code>DeliverInteractable.WritePrompt(in input, ref args);   // a MATCH of 4 letters: (Match, 0, deliver.match.pieces, #17, 4, 32, 0)</code></example>
    internal static void WritePrompt(in DeliverPromptInput input, ref PromptArgs args)
    {
        DeliverBranch b = input.Branch;
        DeliverPromptFlags settling = input.Settling ? DeliverPromptFlags.Settling : DeliverPromptFlags.None;
        DeliverPromptArgs row;
        if (input.Unavailable != DeliverUnavailable.None)
        {
            row = Row(DeliverPromptBranch.Unavailable, settling, UnavailableKey(input.Unavailable), input.DestinationAddress);
        }
        else
        {
            switch (b.Kind)
            {
                case DeliverBranchKind.Reject:
                    StringId key = b.RejectReason == MailRejectReason.NotOnRoute ? DeliverPromptKeys.MailRejectNotOnRoute : DeliverPromptKeys.MailRejectClosed;
                    row = Row(DeliverPromptBranch.Reject, settling, key, input.DestinationAddress);
                    break;
                case DeliverBranchKind.Match:
                    row = Row(DeliverPromptBranch.Match, settling, DeliverPromptKeys.MatchPieces, input.DestinationAddress) with
                    {
                        Units = (ushort)Math.Clamp(b.N, 0, ushort.MaxValue),
                        PayCents = ClampCents(b.PredictedPay),
                    };
                    break;
                case DeliverBranchKind.MisdeliverOne:
                    DeliverPromptFlags flags = settling
                        | (b.FloorRefusal ? DeliverPromptFlags.FloorRefusal : DeliverPromptFlags.None)
                        | (b.WontFit ? DeliverPromptFlags.WontFit : DeliverPromptFlags.None);
                    row = Row(DeliverPromptBranch.MisdeliverOne, flags, DeliverPromptKeys.MismatchPieceFor, input.SelectedAddress) with
                    {
                        Units = 1,
                        PenaltyCents = ClampCents(b.PredictedPenalty),
                    };
                    break;
                default:
                    row = Row(DeliverPromptBranch.Nothing, settling, DeliverPromptKeys.Nothing, input.DestinationAddress);
                    break;
            }
        }

        DeliverPromptArgs.Write(in row, ref args);
    }

    private static DeliverPromptArgs Row(DeliverPromptBranch branch, DeliverPromptFlags flags, StringId key, StringId address) =>
        new(branch, flags, key, address, 0, 0, 0);

    private static StringId UnavailableKey(DeliverUnavailable row) => row switch
    {
        DeliverUnavailable.ClosePanel => DeliverPromptKeys.ClosePanel,
        DeliverUnavailable.DryLand => DeliverPromptKeys.DryLand,
        DeliverUnavailable.Sorting => DeliverPromptKeys.Sorting,
        _ => DeliverPromptKeys.Pending,
    };

    private static int ClampCents(Cents c) => (int)Math.Clamp(c.Value, int.MinValue, int.MaxValue);

    /// <summary>Builds no request until story 004 defines the Deliver op.</summary>
    /// <param name="latch">The latch.</param>
    /// <param name="c">The call context.</param>
    /// <param name="op">The op (left untouched).</param>
    /// <returns>False.</returns>
    public bool TryBuildRequest(in InteractionLatch latch, in InteractionContext c, ref Op op) => false;

    /// <summary>Latches nothing until story 004 defines the press-tick payload.</summary>
    /// <param name="c">The call context.</param>
    /// <param name="latch">The latch being built (left untouched).</param>
    public void OnPress(in InteractionContext c, ref InteractionLatch latch)
    {
    }
}
