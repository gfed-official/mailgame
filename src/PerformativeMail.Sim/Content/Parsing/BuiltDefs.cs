using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// What stage 3 (<see cref="Builder"/>) produced from the stage 2 results of every def file: the def records of each type in
/// ordinal <c>DefIndex</c> order (empty for a type that failed stages 1 to 3), every by-id reference as a
/// <see cref="PendingReference"/>, the failed inputs (a type's table key when the type was not built) and the findings of
/// stages 1 to 3. Hand it to <see cref="ContentPipeline.Build(Balance.BalanceTable, BuiltDefs, RuleRegistry)"/>. Immutable.
/// </summary>
/// <param name="Containers">The container defs.</param>
/// <param name="Items">The item defs.</param>
/// <param name="MailKinds">The mail kind defs.</param>
/// <param name="DestinationTypes">The destination type defs.</param>
/// <param name="MailMixes">The mail mix defs.</param>
/// <param name="References">Every by-id reference, resolved (or reported as CV-GEN-01) at stage 4.</param>
/// <param name="Failed">The table keys of the types that were not built, or were built without their duplicate ids.</param>
/// <param name="Diagnostics">The findings of stages 1 and 2 (from the files) and stage 3 (CV-GEN-01 duplicates, CV-GEN-09).</param>
/// <example>
/// <code>
/// BuiltDefs defs = Builder.Build(files);
/// ContentBuildResult r = ContentPipeline.Build(balance, defs, RuleRegistry.CreateDefault());
/// </code>
/// </example>
public sealed record BuiltDefs(
    ImmutableArray<ContainerDef> Containers,
    ImmutableArray<ItemDef> Items,
    ImmutableArray<MailKindDef> MailKinds,
    ImmutableArray<DestinationTypeDef> DestinationTypes,
    ImmutableArray<MailMixDef> MailMixes,
    ImmutableArray<PendingReference> References,
    ImmutableArray<FailedInput> Failed,
    ImmutableArray<ContentDiagnostic> Diagnostics);
