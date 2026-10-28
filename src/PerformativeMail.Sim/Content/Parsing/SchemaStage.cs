using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// The loader's stages 2 and 3 over the M-1a def tables. Constructing it runs <see cref="ContentSchema.SelfCheck()"/> over every
/// table (TD-090) and throws on a table defect, so a bad row is reported once as a code defect rather than as a content error on
/// every token. Story 019's orchestrator maps each path to its def type and calls <see cref="Walk"/> per file, then
/// <see cref="Build"/> once. Immutable after construction, so thread-safe.
/// </summary>
/// <example>
/// <code>
/// var stage = new SchemaStage();
/// SchemaResult items = stage.Walk(SyntaxWalker.Walk("items/core.jsonc", bytes, TopLevelShape.Array), DefType.Item);
/// BuiltDefs defs = stage.Build(new[] { items });
/// </code>
/// </example>
public sealed class SchemaStage
{
    private readonly ImmutableArray<SchemaTable> _tables;

    /// <summary>Creates the stage over <see cref="ContentSchema.Tables"/> after checking them.</summary>
    /// <exception cref="InvalidOperationException">A table fails <see cref="ContentSchema.SelfCheck()"/>.</exception>
    public SchemaStage()
        : this(ContentSchema.Tables)
    {
    }

    /// <summary>Creates the stage over <paramref name="tables"/> after checking them (a seam for the self-check test).</summary>
    /// <param name="tables">The def tables.</param>
    /// <exception cref="InvalidOperationException">A table fails the self-check; the message lists every issue.</exception>
    internal SchemaStage(ImmutableArray<SchemaTable> tables)
    {
        // "Loader construction" for the TD-090 SelfCheck is this constructor (Mail Dev default, pending Evan (TD-176)); story 019's
        // ContentLoader must construct SchemaStage.
        ImmutableArray<FieldSpecIssue> issues = ContentSchema.SelfCheck(tables);
        if (!issues.IsEmpty)
        {
            throw new InvalidOperationException(
                "The content field tables fail their self-check (a code defect): " +
                string.Join("; ", issues.Select(i => i.FieldName + " " + i.RuleId)));
        }

        _tables = tables;
    }

    /// <summary>Walks one def file (stage 2) with the table of <paramref name="type"/>.</summary>
    /// <param name="file">The file's stage 1 result.</param>
    /// <param name="type">The def type the file holds.</param>
    /// <returns>The file's defs and findings.</returns>
    /// <exception cref="ArgumentException"><paramref name="type"/> has no table.</exception>
    public SchemaResult Walk(SyntaxResult file, DefType type)
    {
        foreach (SchemaTable table in _tables)
        {
            if (table.Type == type)
            {
                return SchemaWalker.Walk(file, table);
            }
        }

        throw new ArgumentException("No field table for the def type.", nameof(type));
    }

    /// <summary>Builds the def records of every walked file (stage 3, <see cref="Builder"/>).</summary>
    /// <param name="files">The stage 2 results, in manifest order.</param>
    /// <returns>The records, references, failed inputs and findings.</returns>
    public BuiltDefs Build(IReadOnlyList<SchemaResult> files) => Builder.Build(files);
}
