using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Hashing;
using PerformativeMail.Sim.Content.Parsing;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Sources;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Tests.Integration.Content;

/// <summary>
/// Content-data story 019 (TR-content-020, TR-content-004, TR-content-013 file half): <see cref="ContentLoader.Load"/> over the
/// shipped <c>content/</c> files equals <see cref="BuiltInContent.CreateM1a"/> byte for byte, gates stages on a failed file,
/// hashes loaded values (not bytes), reads balance files and skips <c>content/presentation/</c>. Fixtures are the shipped
/// files with one textual edit each, served by <see cref="InMemorySource"/>.
/// </summary>
public class ContentLoaderTests
{
    private const string Suppress = "\"suppress\": [{ \"rule\": \"CV-GRD-06\", \"why\": \"a waiver that matches no finding\" }], ";

    // ------------------------------------------------------------ helpers

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PerformativeMail.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("PerformativeMail.sln not found above " + AppContext.BaseDirectory);
    }

    private static string ContentRoot => Path.Combine(FindRepoRoot(), "content");

    /// <summary>The shipped files as (path, text).</summary>
    private static Dictionary<string, string> Shipped()
    {
        var source = new DirectorySource(ContentRoot);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in source.ListManifest())
        {
            Assert.True(source.TryRead(path, out ReadOnlyMemory<byte> bytes));
            files[path] = Encoding.UTF8.GetString(bytes.Span);
        }

        return files;
    }

    /// <summary>A copy of <paramref name="files"/> with one occurrence-checked replacement in <paramref name="path"/>.</summary>
    private static Dictionary<string, string> Edit(Dictionary<string, string> files, string path, string old, string replacement)
    {
        Assert.Contains(old, files[path], StringComparison.Ordinal);
        return new Dictionary<string, string>(files, StringComparer.Ordinal) { [path] = files[path].Replace(old, replacement, StringComparison.Ordinal) };
    }

    private static Dictionary<string, string> With(Dictionary<string, string> files, string path, string text) =>
        new(files, StringComparer.Ordinal) { [path] = text };

    private static ContentLoadResult Load(Dictionary<string, string> files, Func<string, byte[]>? encode = null) =>
        new ContentLoader().Load(new InMemorySource(files.Select(f => (f.Key, (encode ?? Encoding.UTF8.GetBytes)(f.Value))).ToArray()));

    private static Hash256 HashOf(Dictionary<string, string> files, Func<string, byte[]>? encode = null)
    {
        ContentLoadResult r = Load(files, encode);
        Assert.True(r.Set is not null, string.Join("\n", r.Diagnostics.Select(d => d.Format(RuleRegistry.CreateDefault()))));
        return ContentHash.Compute(r.Set);
    }

    private static byte[] Canonical(ContentSet set)
    {
        var buffer = new ArrayBufferWriter<byte>();
        ContentHash.WriteCanonical(set, buffer);
        return buffer.WrittenSpan.ToArray();
    }

    // ------------------------------------------------------------ shipped files load (epic DoD equality test)

    [Fact]
    [Trait("Id", "CDD-LOAD-1")]
    public void Load_ShippedFiles_HasNoFindings_AndTheCanonicalBytesOfBuiltInContent()
    {
        ContentLoadResult r = new ContentLoader().Load(new DirectorySource(ContentRoot));

        Assert.Empty(r.Diagnostics);
        Assert.NotNull(r.Set);
        Assert.Equal(Canonical(BuiltInContent.CreateM1a()), Canonical(r.Set));
        Assert.False(File.Exists(Path.Combine(ContentRoot, "manifest.txt")));   // generated, never committed (ADR-0010 section 1)
    }

    // ------------------------------------------------------------ stage gating (CDD-LOAD-3)

    [Fact]
    [Trait("Id", "CDD-LOAD-3")]
    public void Load_SyntaxErrorInOneFile_WithholdsTheSet_SkipsItsRules_AndStillBuildsTheCleanFile()
    {
        // mail/kinds.jsonc: letter's maxStack 300 is a CV-GRD-01 input, and ",," is a stage 1 syntax error.
        Dictionary<string, string> files = Edit(Shipped(), "mail/kinds.jsonc", "\"maxStack\": 20,", "\"maxStack\": 300,,");
        files = Edit(files, "items/core.jsonc", "{ \"id\": \"axe\", ", "{ \"id\": \"axe\", " + Suppress);

        ContentLoadResult r = Load(files);

        Assert.Null(r.Set);
        Assert.Contains(r.Diagnostics, d => d.RuleId == GeneralRuleIds.SyntaxShape && d.Location.Path == "mail/kinds.jsonc");
        Assert.Contains(r.Validation.Skipped, p => p.RuleId == GridRuleIds.MaxStackWorstCase && p.BlockingFile == "mail/kinds.jsonc");
        Assert.DoesNotContain(r.Diagnostics, d => d.RuleId == GridRuleIds.MaxStackWorstCase);
        Assert.DoesNotContain(r.Validation.Skipped, p => p.BlockingFile == "items/core.jsonc");
        Assert.Equal(1, r.Validation.UnmatchedSuppressCount);   // the clean items file was parsed, built and its suppress reached stage 5
    }

    // ------------------------------------------------------------ loaded defs carry real locations and suppress (TD-178 item 1)

    [Fact]
    [Trait("Id", "CDD-LOAD-3")]
    public void Load_StageFiveFindingOnALoadedDef_PointsAtItsFile_AndItsSuppressReachesTheRunner()
    {
        Dictionary<string, string> files = Edit(Shipped(), "mail/kinds.jsonc", "\"maxStack\": 20,", "\"maxStack\": 300,");
        files = Edit(files, "items/core.jsonc", "{ \"id\": \"axe\", ", "{ \"id\": \"axe\", " + Suppress);

        ContentLoadResult r = Load(files);

        ContentDiagnostic grd01 = Assert.Single(r.Diagnostics, d => d.RuleId == GridRuleIds.MaxStackWorstCase);
        Assert.Equal(("mail/kinds.jsonc", 9, 3), (grd01.Location.Path, grd01.Location.Line, grd01.Location.Column));   // letter's '{'
        Assert.Equal(1, r.Validation.UnmatchedSuppressCount);
    }

    // ------------------------------------------------------------ hash invariance (CDD-PARSE-1, CDD-HASH-1)

    [Fact]
    [Trait("Id", "CDD-PARSE-1")]
    [Trait("Id", "CDD-HASH-1")]
    public void ContentHash_IgnoresTheFileForm_AndChangesWithLoadedValues()
    {
        Dictionary<string, string> fixture = Edit(Shipped(), "items/core.jsonc", "\"maxStack\": 1, ", "\"maxStack\": 1, \"sellPrice\": 400, ");
        Hash256 baseline = HashOf(fixture);
        static byte[] WithBom(string text) => new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        Dictionary<string, string> Every(Func<string, string> f) => fixture.ToDictionary(p => p.Key, p => f(p.Value), StringComparer.Ordinal);

        Assert.Equal(baseline, HashOf(Every(t => t.Replace("\n", "\r\n", StringComparison.Ordinal))));
        Assert.Equal(baseline, HashOf(fixture, WithBom));
        Assert.Equal(baseline, HashOf(Every(t => t.Replace("\n", "\n  // a comment\n", StringComparison.Ordinal).Replace(": ", " :\t  ", StringComparison.Ordinal))));
        Assert.Equal(baseline, HashOf(Edit(fixture, "items/core.jsonc", "\"id\": \"axe\", \"name\": \"item.axe\",", "\"name\": \"item.axe\", \"id\": \"axe\",")));
        Assert.Equal(baseline, HashOf(Edit(fixture, "mail/mix.jsonc", "{ \"letter\": 0.6, \"small\": 0.3, \"medium\": 0.1 }", "{ \"medium\": 0.1, \"small\": 0.3, \"letter\": 0.6 }")));
        Assert.Equal(baseline, HashOf(Edit(fixture, "mail/destinations.jsonc", "[\"large\", \"letter\", \"medium\", \"postcard\", \"small\"]", "[\"small\", \"postcard\", \"medium\", \"letter\", \"large\"]")));

        Assert.NotEqual(baseline, HashOf(Edit(fixture, "items/core.jsonc", "\"sellPrice\": 400,", "\"sellPrice\": 401,")));
        Assert.NotEqual(baseline, HashOf(Edit(fixture, "mail/kinds.jsonc", "\"baseValue\": 8,", "\"baseValue\": 9,")));
        Assert.NotEqual(baseline, HashOf(Edit(fixture, "balance/sim.jsonc", "\"value\": 300,", "\"value\": 301,")));
        Assert.NotEqual(baseline, HashOf(Edit(fixture, "items/core.jsonc", "\"sellPrice\": 400,", "\"sellPrice\": 400, \"buyPrice\": 0,")));
    }

    // ------------------------------------------------------------ balance files (CDD-SCHEMA-5, file form)

    [Theory]
    [Trait("Id", "CDD-SCHEMA-5")]
    [InlineData("unregistered", GeneralRuleIds.BalanceKey, "balance/sim.jsonc")]
    [InlineData("wrongOwner", GeneralRuleIds.BalanceKey, "balance/player.jsonc")]
    [InlineData("twoFiles", GeneralRuleIds.BalanceKey, "balance/player.jsonc")]
    [InlineData("outsideSafe", GeneralRuleIds.BalanceOutsideSafe, "balance/sim.jsonc")]
    [InlineData("noValue", GeneralRuleIds.RequiredFieldMissing, "balance/sim.jsonc")]
    [InlineData("textValue", GeneralRuleIds.NumberFormat, "balance/sim.jsonc")]
    [InlineData("listValue", GeneralRuleIds.NumberFormat, "balance/sim.jsonc")]
    [InlineData("safeOfOne", GeneralRuleIds.ValueOutOfRange, "balance/sim.jsonc")]
    [InlineData("unknownFile", GeneralRuleIds.SyntaxShape, "misc/notes.jsonc")]
    public void Load_BalanceFiles_GiveOneFindingPerMistake(string what, string rule, string path)
    {
        Dictionary<string, string> shipped = Shipped();
        const string Late = "  \"lateValueRatio\": { \"value\": 0.5, \"safe\": [0.25, 0.75] },\n";
        const string Warmup = "\"warmupTicks\": { \"value\": 300, \"safe\": [150, 900] }";
        Dictionary<string, string> files = what switch
        {
            "unregistered" => Edit(shipped, "balance/sim.jsonc", "{\n", "{\n  \"noSuchKey\": 1,\n"),
            "wrongOwner" => Edit(Edit(shipped, "balance/mail.jsonc", Late, string.Empty), path, "{\n", "{\n" + Late),
            "twoFiles" => Edit(shipped, path, "{\n", "{\n" + Late),
            "outsideSafe" => Edit(shipped, "balance/sim.jsonc", "\"value\": 300,", "\"value\": 901,"),
            "noValue" => Edit(shipped, path, Warmup, "\"warmupTicks\": { \"safe\": [150, 900] }"),
            "textValue" => Edit(shipped, path, Warmup, "\"warmupTicks\": { \"value\": \"300\" }"),
            "listValue" => Edit(shipped, path, Warmup, "\"warmupTicks\": []"),
            "safeOfOne" => Edit(shipped, path, Warmup, "\"warmupTicks\": { \"value\": 300, \"safe\": [150] }"),
            _ => With(shipped, path, "[]"),
        };

        ContentLoadResult r = Load(files);

        ContentDiagnostic d = Assert.Single(r.Diagnostics);   // one finding: no CV-GEN-05 cascade for the key
        Assert.Equal((rule, path), (d.RuleId, d.Location.Path));
        Assert.Equal(rule == GeneralRuleIds.BalanceOutsideSafe, r.Set is not null);   // CV-GEN-20 is a Warning: the set still builds
    }

    // ------------------------------------------------------------ presentation exclusion (CDD-SCHEMA-1)

    [Theory]
    [Trait("Id", "CDD-SCHEMA-1")]
    [InlineData("{ \"toastSeconds\": 3 }")]
    [InlineData("{ \"toastSeconds\": 4 }")]
    [InlineData("not json at all")]
    public void Load_PresentationFiles_AreNeitherLoadedNorHashed(string hud)
    {
        Dictionary<string, string> files = With(Shipped(), "presentation/hud.tuning.jsonc", hud);

        ContentLoadResult r = Load(files);

        Assert.Empty(r.Diagnostics);
        Assert.Equal(ContentHash.Compute(BuiltInContent.CreateM1a()), ContentHash.Compute(r.Set!));
    }

    /// <summary>Architecture: the FieldSpec sets are exactly the five Sim def types; no player data (profile, settings, bindings).</summary>
    [Fact]
    [Trait("Id", "CDD-SCHEMA-1")]
    public void FieldSpecSets_HoldOnlySimDefTypes_AndNoPlayerData()
    {
        string[] playerData = { "profile", "setting", "binding", "meta", "input" };
        static IEnumerable<string> Names(ImmutableArray<FieldSpec> rows) =>
            rows.SelectMany(r => new[] { r.Name }.Concat(r.Children.IsDefault ? Enumerable.Empty<string>() : Names(r.Children)));

        Assert.Equal(
            new[] { "ItemDef", "ContainerDef", "MailKindDef", "DestinationTypeDef", "MailMixDef" },
            ContentSchema.Tables.Select(t => t.TypeName));
        foreach (SchemaTable table in ContentSchema.Tables)
        {
            Assert.True(typeof(IDef).IsAssignableFrom(typeof(IDef).Assembly.GetType("PerformativeMail.Sim.Content." + table.TypeName)), table.TypeName);
            foreach (string name in Names(table.Rows))
            {
                Assert.DoesNotContain(playerData, word => name.Contains(word, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
