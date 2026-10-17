namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// One mail kind's input to F3 (<see cref="KindCounts"/>): its base value, its value share this shift and the fallback
/// rule for a kind with no unlocked accepting destination (GDD R1: the share folds into <c>fallbackKind</c>). Built by the
/// caller from <c>MailKindDef</c> / <c>MailShare</c> (the caller resolves "has an unlocked accepting destination" from the
/// destination registry); <see cref="FallbackIndex"/> indexes the same span the struct lives in. Immutable, 0 B.
/// </summary>
/// <param name="BaseValueCents">The kind's <c>baseValue</c> in cents (sets the descending processing order and E_k).</param>
/// <param name="SharePm">The kind's value share this shift in permille (0 to 1000).</param>
/// <param name="FallbackIndex">Index (in the shares span) of the kind its share folds into, or -1 for none.</param>
/// <param name="HasAcceptingDestination">False when no unlocked destination accepts this kind: its share folds into the fallback and it gets no units.</param>
/// <example>
/// <code>
/// var letter = new KindShare(8, 600, -1, true);
/// var cargo = new KindShare(600, 100, FallbackIndex: 4, HasAcceptingDestination: false);
/// </code>
/// </example>
public readonly record struct KindShare(long BaseValueCents, int SharePm, int FallbackIndex, bool HasAcceptingDestination);
