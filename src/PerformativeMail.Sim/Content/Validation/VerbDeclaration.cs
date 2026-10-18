using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// The interaction verbs one content definition declares, as CV-PLR-13 (PCM-P10-1, input-controls H-R4-5) reads them. At M-1a no
/// def type carries a verb field (player-character story 012 adds no JSON field: that is a schema change the owner must approve),
/// so built-in content and the production pipeline pass no declarations and the rule is exercised by fixtures. When a def type
/// later carries verbs, the loader fills <see cref="ContentValidationInput.VerbDeclarations"/> from that field and the rule body
/// is unchanged. <see cref="Verbs"/> holds the raw snake_case spellings as written in data (not parsed), so an unknown spelling
/// such as <c>revive</c> can be named in the finding. Immutable.
/// </summary>
/// <param name="Def">The def that declares the verbs (the finding is attributed to it).</param>
/// <param name="Location">Where the declaration is written.</param>
/// <param name="Verbs">The declared spellings, in data order; empty when the def declares the field but lists nothing.</param>
/// <example>
/// <code>
/// var v = new VerbDeclaration(new DefRef(DefType.DestinationType, "house"),
///     ContentValidationInput.LocationOf("destination_types", "house"), ImmutableArray.Create("deliver"));
/// </code>
/// </example>
public sealed record VerbDeclaration(DefRef Def, SourceLocation Location, ImmutableArray<string> Verbs);
