using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Defs;
using PerformativeMail.Sim.Content.Parsing;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// The five M-1a def tables in <see cref="DefType"/> order, and their startup self-check (TD-090). The content loader
/// (<see cref="SchemaStage"/>) calls <see cref="SelfCheck()"/> when it is constructed, before it reads any file, so a table defect is reported once as a code defect rather than as a
/// content error on every token. Each call builds fresh tables (no static state). Thread-safe.
/// </summary>
/// <example>
/// <code>
/// ImmutableArray&lt;FieldSpecIssue&gt; issues = ContentSchema.SelfCheck();   // empty for the shipped tables
/// ImmutableArray&lt;FieldSpec&gt; items = ContentSchema.Rows(DefType.Item);
/// </code>
/// </example>
public static class ContentSchema
{
    /// <summary>The tables, one per <see cref="DefType"/>, in enum order.</summary>
    public static ImmutableArray<SchemaTable> Tables => ImmutableArray.Create(
        new SchemaTable(DefType.Item, nameof(ItemDef), ItemDefFieldSpec.Rows),
        new SchemaTable(DefType.Container, nameof(ContainerDef), ContainerDefFieldSpec.Rows),
        new SchemaTable(DefType.MailKind, nameof(MailKindDef), MailKindDefFieldSpec.Rows),
        new SchemaTable(DefType.DestinationType, nameof(DestinationTypeDef), DestinationTypeDefFieldSpec.Rows),
        new SchemaTable(DefType.MailMix, nameof(MailMixDef), MailMixDefFieldSpec.Rows));

    /// <summary>The rows of one def type's table.</summary>
    /// <param name="type">The def type.</param>
    /// <returns>The rows in canonical order, or an empty array for a type with no table.</returns>
    /// <example><code>ImmutableArray&lt;FieldSpec&gt; rows = ContentSchema.Rows(DefType.MailKind);</code></example>
    public static ImmutableArray<FieldSpec> Rows(DefType type)
    {
        foreach (SchemaTable table in Tables)
        {
            if (table.Type == type)
            {
                return table.Rows;
            }
        }

        return ImmutableArray<FieldSpec>.Empty;
    }

    /// <summary>Runs <see cref="FieldSpecTable.SelfCheck"/> over every table.</summary>
    /// <returns>The issues, each field name prefixed with its type name (<c>ItemDef.grid</c>); empty when every table is clean.</returns>
    /// <example><code>if (!ContentSchema.SelfCheck().IsEmpty) { /* a code defect: stop before reading content */ }</code></example>
    public static ImmutableArray<FieldSpecIssue> SelfCheck() => SelfCheck(Tables);

    /// <summary>Runs <see cref="FieldSpecTable.SelfCheck"/> over <paramref name="tables"/> (a seam for the loader-construction test).</summary>
    internal static ImmutableArray<FieldSpecIssue> SelfCheck(ImmutableArray<SchemaTable> tables)
    {
        ImmutableArray<FieldSpecIssue>.Builder all = ImmutableArray.CreateBuilder<FieldSpecIssue>();
        foreach (SchemaTable table in tables)
        {
            foreach (FieldSpecIssue issue in FieldSpecTable.SelfCheck(table.Rows))
            {
                all.Add(issue with { FieldName = table.TypeName + "." + issue.FieldName });
            }
        }

        return all.ToImmutable();
    }
}
