namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// One <c>suppress: [{rule, why}]</c> entry on a def (GDD CD22). It is diagnostic metadata only: never a gameplay
/// field and never hashed. <see cref="Rule"/> or <see cref="Why"/> may be null or blank to model a malformed entry,
/// which the runner reports (CV-GEN-05).
/// </summary>
/// <param name="Rule">The canonical id of the Warning rule to waive.</param>
/// <param name="Why">The mandatory reason.</param>
/// <param name="Location">Where the entry is written, used for the diagnostic about it.</param>
/// <example>
/// <code>
/// var waive = new SuppressEntry("CV-GRD-07", "short despawn is intended for this test world", location);
/// </code>
/// </example>
public sealed record SuppressEntry(string? Rule, string? Why, SourceLocation Location);
