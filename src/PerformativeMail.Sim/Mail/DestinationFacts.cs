using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The facts <c>MailRules.Evaluate</c> reads about a destination (ADR-0017 key interfaces): a small immutable value filled by
/// <see cref="DestinationRegistry.TryGetDestinationFacts"/>. <see cref="State"/> is the raw state (no grace window; story 003).
/// </summary>
/// <example>
/// <code>
/// if (registry.TryGetDestinationFacts(dest, out DestinationFacts f) &amp;&amp; f.State == DestState.Open) { /* deliver */ }
/// </code>
/// </example>
public readonly struct DestinationFacts
{
    /// <summary>The destination entity.</summary>
    public readonly EntityId Id;

    /// <summary>The destination's packed address.</summary>
    public readonly AddressId Address;

    /// <summary>The <c>DestinationTypeDef</c> index.</summary>
    public readonly DefIndex Type;

    /// <summary>The raw delivery state.</summary>
    public readonly DestState State;

    /// <summary>
    /// Bit k is set when the mail kind whose <c>DefIndex.Value</c> is k is accepted by this destination's type (from
    /// <c>DestinationTypeDef.Accepts</c>). Limit: a kind with a <c>Value</c> of 64 or more is never set, so at most 64 mail
    /// kinds can be accepted; M-1a has 5. Carried here so <c>MailRules.Evaluate</c> needs no registry or content access.
    /// </summary>
    public readonly ulong AcceptsMask;

    /// <summary>True when the type needs a unit number to match (<c>DestinationTypeDef.RequiresUnit</c>).</summary>
    public readonly bool RequiresUnit;

    /// <summary>Creates the facts.</summary>
    /// <param name="id">The destination entity.</param>
    /// <param name="address">The packed address.</param>
    /// <param name="type">The destination type index.</param>
    /// <param name="state">The raw state.</param>
    /// <param name="acceptsMask">The accepted-kind bit mask (see <see cref="AcceptsMask"/>).</param>
    /// <param name="requiresUnit">True when the unit number takes part in the address match.</param>
    public DestinationFacts(EntityId id, AddressId address, DefIndex type, DestState state, ulong acceptsMask, bool requiresUnit)
    {
        Id = id;
        Address = address;
        Type = type;
        State = state;
        AcceptsMask = acceptsMask;
        RequiresUnit = requiresUnit;
    }
}
