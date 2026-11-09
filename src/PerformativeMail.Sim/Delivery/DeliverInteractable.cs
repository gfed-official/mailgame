using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery.Client;
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
/// (<see cref="WritePrompt(in DeliverPromptInput, ref PromptArgs)"/>).
/// </para>
/// <para>
/// Two instances exist. The host's (composition, <see cref="DeliverInteractable(IStatSheet, IMailApi?)"/>) answers
/// <c>IPlayersApi.CheckInteraction</c> on host state only: MD2 rows 2, 4 and 5 through <see cref="DeliverAvailability"/> with the host
/// flag; the framework gate has already refused swim depth, and CLOSED is left to Mail's <c>ConsumeHand</c>, which judges it at
/// <c>holdStartTick</c> under the closing grace (this hook does not see the hold's start), so a graced hold is not refused. The
/// client's (<see cref="DeliverPreviewStep.Interactable"/>) reads the 4b step's published state (stories 003 and 004): availability
/// and the settle, the press payload, the cancels, the request and the prompt.
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
    private readonly IMailApi? _hostMail;
    private readonly DeliverPreviewStep? _client;

    /// <summary>Creates the host's Deliver hooks over <paramref name="stats"/> and, for rows 4 and 5, <paramref name="mail"/>.</summary>
    /// <param name="stats">The stat sheet read for <see cref="Stat.DeliverInsertPm"/>.</param>
    /// <param name="mail">Mail's API for the destination state (none: every row passes).</param>
    /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
    public DeliverInteractable(IStatSheet stats, IMailApi? mail = null)
        : this(stats, mail, null)
    {
    }

    /// <summary>Creates the hooks: the client's when <paramref name="client"/> is set, else the host's.</summary>
    internal DeliverInteractable(IStatSheet stats, IMailApi? mail, DeliverPreviewStep? client)
    {
        ArgumentNullException.ThrowIfNull(stats);
        _stats = stats;
        _hostMail = mail;
        _client = client;
    }

    /// <summary>
    /// MD2's ordered rows (<see cref="DeliverAvailability"/>): on the client, rows 1 to 6 and then the MD4 settle
    /// (<c>deliver.cancel.settle</c>; the press is refused here because <c>OnPress</c> cannot refuse); on the host, LOCKED
    /// (<c>mail.reject.not_on_route</c>) from Mail's current destination facts.
    /// </summary>
    /// <param name="c">The call context.</param>
    /// <param name="reason">The first row's key when false; <see cref="StringId.None"/> when true.</param>
    /// <returns>True when available.</returns>
    public bool IsAvailable(in InteractionContext c, out StringId reason)
    {
        if (_client is not null)
        {
            return _client.IsAvailable(in c, out reason);
        }

        DestState state = DestState.Open;
        if (_hostMail is not null && _hostMail.TryGetDestinationFacts(c.Anchor.Entity, out DestinationFacts f) && f.State == DestState.Locked)
        {
            state = DestState.Locked;
        }

        DeliverRow row = DeliverAvailability.Evaluate(new DeliverAvailabilityFacts { HostFacts = true, State = state });
        reason = DeliverAvailability.Key(row);
        return row == DeliverRow.None;
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

    /// <summary>
    /// MD4's extra cancels on the client, judged on this tick's recompute with the destination state at the hold's start (closing grace):
    /// the branch kind changed (<c>deliver.cancel.prompt_changed</c>), or on a MISDELIVER_ONE hold the selected slot or entry changed
    /// (<c>deliver.cancel.selection_changed</c>). The host never times a hold, so its instance never cancels.
    /// </summary>
    /// <param name="latch">The latch.</param>
    /// <param name="c">The call context.</param>
    /// <param name="reason">The cancel key when true.</param>
    /// <returns>True to cancel.</returns>
    public bool ShouldCancel(in InteractionLatch latch, in InteractionContext c, out StringId reason)
    {
        if (_client is not null)
        {
            return _client.ShouldCancel(in latch, out reason);
        }

        reason = StringId.None;
        return false;
    }

    /// <summary>
    /// The prompt hook: on the client, <see cref="WritePrompt(in DeliverPromptInput, ref PromptArgs)"/> over this tick's published preview,
    /// availability and settle state for <see cref="InteractionContext.Anchor"/>; otherwise the bytes are left untouched.
    /// </summary>
    /// <param name="c">The call context.</param>
    /// <param name="args">The destination bytes.</param>
    public void WritePrompt(in InteractionContext c, ref PromptArgs args)
    {
        if (_client is not null && _client.TryPromptInput(c.Anchor, out DeliverPromptInput input))
        {
            WritePrompt(in input, ref args);
        }
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

    /// <summary>
    /// The one request of a completed hold (MD5): <c>deliver_matching</c> with the 24 B <see cref="DeliverMatchingPayload"/> — the latched
    /// anchor's destination, <c>selectedEntryId</c> only for a MISDELIVER_ONE latch (0 on MATCH), the latched <c>holdStartTick</c> and
    /// <c>Claim = latch.Claimed</c> (feet at press). No duration and no count. The host's instance builds nothing.
    /// </summary>
    /// <param name="latch">The latch.</param>
    /// <param name="c">The call context.</param>
    /// <param name="op">The op to fill.</param>
    /// <returns>True when a request was built.</returns>
    public bool TryBuildRequest(in InteractionLatch latch, in InteractionContext c, ref Op op)
    {
        if (_client is null)
        {
            return false;
        }

        DeliverLatchPayload p = DeliverLatchPayload.Read(in latch.OwnerPayload);
        op.Kind = DeliveryOpKinds.DeliverMatching;
        op.SetPayload(new DeliverMatchingPayload
        {
            MailboxId = latch.Anchor.Entity.Value,
            SelectedEntryId = p.Kind == DeliverBranchKind.MisdeliverOne ? p.SelectedEntry.Value : 0u,
            HoldStartTick = p.HoldStartTick,
            Claim = latch.Claimed,
        });
        return true;
    }

    /// <summary>
    /// Latches the press-tick branch (MD4) into the 16 B owner payload (<see cref="DeliverLatchPayload"/>): kind, selected slot, entry
    /// (MISDELIVER_ONE only), <c>holdStartTick = c.Tick</c> (the <c>IHostClock</c> estimate the press context carries) and the
    /// destination ordinal. The settle refusal is <see cref="IsAvailable"/>'s. The host's instance latches nothing.
    /// </summary>
    /// <param name="c">The call context.</param>
    /// <param name="latch">The latch being built.</param>
    public void OnPress(in InteractionContext c, ref InteractionLatch latch)
    {
        if (_client is not null)
        {
            DeliverLatchPayload p = _client.LatchPayload(in c);
            DeliverLatchPayload.Write(in p, ref latch.OwnerPayload);
        }
    }
}
