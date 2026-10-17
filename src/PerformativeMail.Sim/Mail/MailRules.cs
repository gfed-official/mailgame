using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The delivery rule (Mail R5, TR-mail-005, ADR-0017 sections 4 and 5; ADR-0016 section 3): a static, pure, allocation-free
/// function over two small facts structs, compiled into the client for the prompt and arc colour. Its signature takes no
/// wallet, RNG, clock, registry, mirror or world; there is no second copy of the rule.
/// </summary>
/// <example>
/// <code>
/// registry.TryGetDestinationFacts(dest, out DestinationFacts d);
/// DeliverEval e = MailRules.Evaluate(in d, in unit);   // Reject(Closed) | Reject(NotOnRoute) | Reject(WontFit) | Accept | Misdeliver
/// </code>
/// </example>
public static class MailRules
{
    /// <summary>The most predicate checks one <see cref="Evaluate"/> call makes (M-PERF-1a).</summary>
    public const int MaxPredicateChecks = 6;

#if SIM_ASSERT
    /// <summary>
    /// Instrumented build only (absent in Release): runs the same rule as <see cref="Evaluate"/> and reports how many predicate
    /// checks it made. The count is returned, not stored, because the Sim holds no mutable static state (ADR-0003 section 4).
    /// </summary>
    /// <param name="d">The destination facts.</param>
    /// <param name="u">The piece facts.</param>
    /// <param name="checks">The number of predicate checks made.</param>
    /// <returns>The result, identical to <see cref="Evaluate"/>.</returns>
    internal static DeliverEval EvaluateCounted(in DestinationFacts d, in UnitFacts u, out int checks)
    {
        int count = 0;
        DeliverEval e = EvaluateCore(in d, in u, ref count);
        checks = count;
        return e;
    }
#endif

    /// <summary>
    /// Decides a delivery, checking in order: CLOSED gives Reject Closed; LOCKED gives Reject NotOnRoute; a non-mail
    /// category gives Reject WontFit; a kind outside the destination type's accepts gives Reject WontFit; a matching address
    /// gives Accept; anything else is Misdeliver. The address match compares the unit when
    /// <see cref="DestinationFacts.RequiresUnit"/> is set and ignores it otherwise.
    /// </summary>
    /// <param name="d">The destination facts (state already includes any grace window).</param>
    /// <param name="u">The piece facts.</param>
    /// <returns>The result.</returns>
    public static DeliverEval Evaluate(in DestinationFacts d, in UnitFacts u)
    {
        int unused = 0;
        return EvaluateCore(in d, in u, ref unused);
    }

    // The one copy of the rule; the instrumented entry and the public entry both call it. Inlined so the Release entry
    // carries no stack slot for the (compiled-out) counter.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static DeliverEval EvaluateCore(in DestinationFacts d, in UnitFacts u, ref int checks)
    {
        CountCheck(ref checks);
        if (d.State == DestState.Closed)
        {
            return DeliverEval.Reject(MailRejectReason.Closed);
        }

        CountCheck(ref checks);
        if (d.State == DestState.Locked)
        {
            return DeliverEval.Reject(MailRejectReason.NotOnRoute);
        }

        CountCheck(ref checks);
        if (u.Category != ItemCategory.Mail)
        {
            return DeliverEval.Reject(MailRejectReason.WontFit);
        }

        CountCheck(ref checks);
        if (u.Kind.Value >= 64 || ((d.AcceptsMask >> u.Kind.Value) & 1UL) == 0UL)
        {
            return DeliverEval.Reject(MailRejectReason.WontFit);
        }

        CountCheck(ref checks);
        uint unitMask = d.RequiresUnit ? 0u : (uint)AddressId.MaxUnit;
        if ((u.Address.Value & ~unitMask) == (d.Address.Value & ~unitMask))
        {
            return DeliverEval.Accept();
        }

        return DeliverEval.Misdeliver();
    }

    [System.Diagnostics.Conditional("SIM_ASSERT")]
    private static void CountCheck(ref int checks)
    {
        checks++;
    }
}
