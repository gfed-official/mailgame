using System.Collections.Immutable;
using System.Text;
using PerformativeMail.Sim.Content.Parsing;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Sources;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>A source whose manifest lists paths it cannot read (the CV-GEN-10 case).</summary>
internal sealed class ListedButMissingSource : IContentSource
{
    private readonly ImmutableArray<string> _paths;

    public ListedButMissingSource(params string[] paths) => _paths = ImmutableArray.Create(paths);

    public ImmutableArray<string> ListManifest() => _paths;

    public bool TryRead(string path, out ReadOnlyMemory<byte> bytes)
    {
        bytes = default;
        return false;
    }
}

/// <summary>
/// Violation and valid-edge fixtures of the stage 1 syntax rules CV-GEN-04, CV-GEN-10 and CV-GEN-16 (content-data story 017).
/// Each violation walks a small file and must produce exactly one diagnostic of the rule's id; the valid edge of
/// <c>nesting_too_deep</c> is nesting exactly 32 deep. Appended to <see cref="RuleFixtureCatalogue.Real"/>.
/// </summary>
/// <example>
/// <code>
/// ValidationResult r = SyntaxRuleFixtures.Walk("[{\"id\":1,\"id\":2}]", TopLevelShape.Array);   // one CV-GEN-04
/// </code>
/// </example>
internal static class SyntaxRuleFixtures
{
    /// <summary>Walks <paramref name="text"/> (UTF-8) and wraps the findings as a validation result.</summary>
    public static ValidationResult Walk(string text, TopLevelShape shape) =>
        Walk(Encoding.UTF8.GetBytes(text), shape);

    /// <summary>Walks <paramref name="bytes"/> and wraps the findings as a validation result.</summary>
    public static ValidationResult Walk(byte[] bytes, TopLevelShape shape) =>
        ValidationResult.FromDiagnostics(SyntaxWalker.Walk("defs/fixture.jsonc", bytes, shape).Diagnostics);

    /// <summary><paramref name="depth"/> nested empty arrays.</summary>
    public static string Nested(int depth) => new string('[', depth) + new string(']', depth);

    private static RuleFixture Violation(string rule, string clause, Func<ValidationResult> produce) =>
        new(rule, clause, FixtureKind.Violation, produce, ImmutableArray<string>.Empty);

    private static RuleFixture Edge(string rule, string clause, Func<ValidationResult> produce) =>
        new(rule, clause, FixtureKind.ValidEdge, produce, ImmutableArray<string>.Empty);

    /// <summary>All the story 017 fixtures.</summary>
    public static ImmutableArray<RuleFixture> All() => ImmutableArray.Create(
        Violation(GeneralRuleIds.DuplicateKey, "duplicate_key",
            () => Walk("[{\"id\":\"a\",\"id\":\"b\"}]", TopLevelShape.Array)),
        Violation(GeneralRuleIds.ManifestFile, "listed_file_missing",
            () => ValidationResult.FromDiagnostics(
                SyntaxStage.Run(new ListedButMissingSource("defs/gone.jsonc"), _ => TopLevelShape.Array).Diagnostics)),
        Violation(GeneralRuleIds.SyntaxShape, "invalid_utf8",
            () => Walk(new byte[] { (byte)'[', (byte)'"', 0xFF, (byte)'"', (byte)']' }, TopLevelShape.Array)),
        Violation(GeneralRuleIds.SyntaxShape, "bom_not_at_start",
            () => Walk(new byte[] { (byte)'[', 0xEF, 0xBB, 0xBF, (byte)']' }, TopLevelShape.Array)),
        Violation(GeneralRuleIds.SyntaxShape, "no_json_value",
            () => Walk("// nothing here\n", TopLevelShape.Array)),
        Violation(GeneralRuleIds.SyntaxShape, "nesting_too_deep",
            () => Walk(Nested(SyntaxWalker.MaxDepth + 1), TopLevelShape.Array)),
        Edge(GeneralRuleIds.SyntaxShape, "nesting_too_deep",
            () => Walk(Nested(SyntaxWalker.MaxDepth), TopLevelShape.Array)),
        Violation(GeneralRuleIds.SyntaxShape, "top_level_shape",
            () => Walk("{}", TopLevelShape.Array)),
        Violation(GeneralRuleIds.SyntaxShape, "invalid_json",
            () => Walk("[+5]", TopLevelShape.Array)));
}
