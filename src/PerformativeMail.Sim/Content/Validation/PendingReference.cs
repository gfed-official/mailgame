namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// A by-id reference from one def to another def, still to be resolved (CV-GEN-01, stage 4). The defs themselves hold
/// <c>DefIndex</c> references, which the factory (or the M0 loader) has already turned into ordinals; a loader that reads
/// ids from data passes the raw by-id references here so that a missing id, and an id of the wrong def type, are found
/// and named. A wrong-type reference counts as unresolved (GDD "Ids &amp; enums"). Immutable.
/// </summary>
/// <param name="From">The def that holds the reference (the finding is attributed to it).</param>
/// <param name="Where">Where the reference is written.</param>
/// <param name="Field">The referring field, for the message (for example <c>accepts[2]</c>).</param>
/// <param name="Expected">The def type the field must refer to.</param>
/// <param name="TargetId">The id that was written.</param>
/// <example>
/// <code>
/// var r = new PendingReference(new DefRef(DefType.DestinationType, "house"), SourceLocation.BuiltIn("builtin/destination_types/house"),
///     "accepts[0]", DefType.MailKind, "letter");
/// </code>
/// </example>
public sealed record PendingReference(DefRef From, SourceLocation Where, string Field, DefType Expected, string TargetId);
