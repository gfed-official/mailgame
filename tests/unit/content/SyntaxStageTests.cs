using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using PerformativeMail.Sim.Content.Parsing;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Sources;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 017 (TR-content-001/003/021; GDD CDD-PARSE-1 syntax half, CDD-PARSE-2 duplicate-key half,
/// CDD-PARSE-3 CV-GEN-16 half, CDD-PARSE-4 parser half): the content sources, the stage 1 syntax walk and the
/// BannedSymbols additions.
/// </summary>
public class SyntaxStageTests
{
    private const string Path = "defs/items.jsonc";

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));

    private static SyntaxResult Walk(string text, TopLevelShape shape = TopLevelShape.Array) =>
        SyntaxWalker.Walk(Path, Utf8(text), shape);

    private static SyntaxResult Walk(byte[] bytes, TopLevelShape shape = TopLevelShape.Array) =>
        SyntaxWalker.Walk(Path, bytes, shape);

    private static string[] Stream(SyntaxResult r)
    {
        var list = new List<string>();
        foreach (SyntaxToken t in r.Tokens)
        {
            list.Add(t.Kind + ":" + Encoding.UTF8.GetString(r.Raw(t)));
        }

        return list.ToArray();
    }

    private static ContentDiagnostic Single(SyntaxResult r, string ruleId)
    {
        Assert.Single(r.Diagnostics);
        ContentDiagnostic d = r.Diagnostics[0];
        Assert.Equal(ruleId, d.RuleId);
        Assert.Equal(RuleSeverity.Error, d.Severity);
        Assert.True(d.Def.IsNone);
        return d;
    }

    private static void AssertAt(ContentDiagnostic d, int line, int column)
    {
        Assert.Equal(Path, d.Location.Path);
        Assert.Equal(line, d.Location.Line);
        Assert.Equal(column, d.Location.Column);
    }

    // ------------------------------------------------------------ AC1 (CDD-PARSE-1, syntax half)

    private const string BaseDef = "[\n  {\"id\": \"a\", \"n\": 1.5, \"tags\": [\"x\", \"y\"], \"on\": true, \"none\": null}\n]\n";

    [Fact]
    [Trait("Id", "CDD-PARSE-1")]
    public void OneDefFileSavedFiveWays_GivesTheSameTokenStream()
    {
        string withComments =
            "// header\n[\n  /* the item */ {\"id\": \"a\", // why\n \"n\": 1.5, \"tags\": [\"x\", \"y\"], \"on\": true, \"none\": null}\n]\n";
        string withTrailingCommas =
            "[\n  {\"id\": \"a\", \"n\": 1.5, \"tags\": [\"x\", \"y\",], \"on\": true, \"none\": null,},\n]\n";
        byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8(BaseDef)).ToArray();

        // BOM + CRLF + comments + trailing commas together, with a comment on the last line and no trailing newline.
        string combinedText =
            "// header\r\n[\r\n  /* the item */ {\"id\": \"a\", // why\r\n \"n\": 1.5, \"tags\": [\"x\", \"y\",], \"on\": true, \"none\": null,},\r\n]\r\n// the end";
        byte[] combined = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8(combinedText)).ToArray();

        SyntaxResult lf = Walk(BaseDef);
        SyntaxResult[] others =
        {
            Walk(BaseDef.Replace("\n", "\r\n", StringComparison.Ordinal)),
            Walk(bom),
            Walk(withComments),
            Walk(withTrailingCommas),
            Walk(combined),
        };

        Assert.True(lf.Ok);
        Assert.Empty(lf.Diagnostics);
        string[] expected = Stream(lf);
        foreach (SyntaxResult other in others)
        {
            Assert.True(other.Ok);
            Assert.Empty(other.Diagnostics);
            Assert.Equal(expected, Stream(other));
        }
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-1")]
    public void TokenTable_RecordsKindAndRawBytesOfEveryToken()
    {
        SyntaxResult r = Walk("[{\"id\" : \"a\\u0062\", \"n\":-1.50, \"ok\":true, \"x\":null, \"f\":false}]");

        Assert.Equal(
            new[]
            {
                "StartArray:[", "StartObject:{", "PropertyName:\"id\"", "String:\"a\\u0062\"", "PropertyName:\"n\"",
                "Number:-1.50", "PropertyName:\"ok\"", "True:true", "PropertyName:\"x\"", "Null:null", "PropertyName:\"f\"",
                "False:false", "EndObject:}", "EndArray:]",
            },
            Stream(r));
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-1")]
    public void TokenOffsets_AreIntoTheOriginalFileEvenWithABom()
    {
        byte[] bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8("[1]")).ToArray();

        SyntaxResult r = Walk(bytes);

        Assert.Equal(3, r.PayloadOffset);
        Assert.Equal(3, r.Tokens[0].Start);
        Assert.Equal(4, r.Tokens[1].Start);
        Assert.Equal(Utf8("[1]"), r.Payload.ToArray());
        Assert.Equal(JsonTokenType.Number, r.Tokens[1].Kind);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-1")]
    public void ListedButMissingFile_IsCvGen10_AndNotReadAsEmpty()
    {
        SyntaxStageResult r = SyntaxStage.Run(new ListedButMissingSource("defs/gone.jsonc"), _ => TopLevelShape.Array);

        ContentDiagnostic d = Assert.Single(r.Diagnostics);
        Assert.Equal(GeneralRuleIds.ManifestFile, d.RuleId);
        Assert.Equal("defs/gone.jsonc", d.Value);
        Assert.Equal("listed file present", d.Allowed);
        Assert.Equal("defs/gone.jsonc", d.Location.Path);
        Assert.Empty(r.Files);
        Assert.False(r.Ok);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-1")]
    public void ZeroByteFile_IsCvGen16_NoJsonValue()
    {
        SyntaxResult r = Walk(Array.Empty<byte>());

        AssertAt(Single(r, GeneralRuleIds.SyntaxShape), 1, 1);
        Assert.Equal("one JSON value at the top level", r.Diagnostics[0].Allowed);
        Assert.False(r.Ok);
        Assert.Empty(r.Tokens);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-1")]
    public void EmptyFile_IsReadAsPresentWithZeroBytes_ThroughTheStage()
    {
        var source = new InMemorySource(("defs/empty.jsonc", Array.Empty<byte>()));

        SyntaxStageResult r = SyntaxStage.Run(source, _ => TopLevelShape.Array);

        Assert.Single(r.Files);
        Assert.Equal(GeneralRuleIds.SyntaxShape, Assert.Single(r.Diagnostics).RuleId);
    }

    // ------------------------------------------------------------ AC2 (CDD-PARSE-3, CV-GEN-16 half)

    [Theory]
    [Trait("Id", "CDD-PARSE-3")]
    [InlineData("5B 22 FF 22 5D", 1, 3)]
    [InlineData("5B C0 80 5D", 1, 2)]
    [InlineData("5B E2 82 5D", 1, 2)]
    [InlineData("5B ED A0 80 5D", 1, 2)]
    [InlineData("5B F4 90 80 80 5D", 1, 2)]
    [InlineData("5B 80 5D", 1, 2)]
    [InlineData("5B 0A 22 C3 28 22 5D", 2, 2)]
    [InlineData("EF BB BF 5B FF 5D", 1, 5)]
    public void InvalidUtf8_IsCvGen16_AtTheFirstBadByte(string hex, int line, int column)
    {
        SyntaxResult r = Walk(Hex(hex));

        ContentDiagnostic d = Single(r, GeneralRuleIds.SyntaxShape);
        AssertAt(d, line, column);
        Assert.Equal("well-formed UTF-8", d.Allowed);
    }

    [Theory]
    [Trait("Id", "CDD-PARSE-3")]
    [InlineData("5B 22 C3 A9 22 5D")]
    [InlineData("5B 22 ED 9F BF 22 5D")]
    [InlineData("5B 22 F0 9F 98 80 22 5D")]
    [InlineData("5B 22 F4 8F BF BF 22 5D")]
    public void ValidMultiByteUtf8_IsAccepted(string hex)
    {
        Assert.True(Walk(Hex(hex)).Ok);
    }

    [Theory]
    [Trait("Id", "CDD-PARSE-3")]
    [InlineData("5B EF BB BF 5D", 1, 2)]
    [InlineData("EF BB BF EF BB BF 5B 5D", 1, 4)]
    [InlineData("5B 22 EF BB BF 22 5D", 1, 3)]
    public void BomNotAtOffsetZero_IsCvGen16_AtTheBom(string hex, int line, int column)
    {
        ContentDiagnostic d = Single(Walk(Hex(hex)), GeneralRuleIds.SyntaxShape);

        AssertAt(d, line, column);
        Assert.Equal("U+FEFF", d.Value);
    }

    [Theory]
    [Trait("Id", "CDD-PARSE-3")]
    [InlineData("// only a comment\n", 2, 1)]
    [InlineData("/* only a comment */", 1, 21)]
    [InlineData("\n\n  // a\n  /* b */\n", 5, 1)]
    [InlineData("   ", 1, 4)]
    public void CommentOnlyOrBlankFile_IsCvGen16_NoJsonValue_AtEndOfFile(string text, int line, int column)
    {
        SyntaxResult r = Walk(text);

        ContentDiagnostic d = Single(r, GeneralRuleIds.SyntaxShape);
        AssertAt(d, line, column);
        Assert.Equal("end of file", d.Value);
        Assert.Equal("one JSON value at the top level", d.Allowed);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-3")]
    public void BomOnlyFile_IsCvGen16_NoJsonValue()
    {
        SyntaxResult r = Walk(new byte[] { 0xEF, 0xBB, 0xBF });

        ContentDiagnostic d = Single(r, GeneralRuleIds.SyntaxShape);
        AssertAt(d, 1, 4);
        Assert.Equal("end of file", d.Value);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-3")]
    public void Nesting32_Passes_And33_IsCvGen16_AtTheOpeningBracket()
    {
        string ok = SyntaxRuleFixtures.Nested(32);
        string bad = SyntaxRuleFixtures.Nested(33);

        Assert.True(Walk(ok).Ok);
        ContentDiagnostic d = Single(Walk(bad), GeneralRuleIds.SyntaxShape);
        AssertAt(d, 1, 33);
        Assert.Equal("depth 33", d.Value);
        Assert.Equal("at most 32 levels of nesting", d.Allowed);
    }

    private static string ObjectNest(int depth, string unit = "{\"a\":") =>
        string.Concat(Enumerable.Repeat(unit, depth - 1)) + "{}" + new string('}', depth - 1);

    private static string MixedNest(int pairs) =>
        string.Concat(Enumerable.Repeat("[{\"a\":", pairs)) + "1" + string.Concat(Enumerable.Repeat("}]", pairs));

    [Fact]
    [Trait("Id", "CDD-PARSE-3")]
    public void Nesting33_ThroughMixedNesting_IsCvGen16_AtThe33rdBracket()
    {
        // '[' then 16 x '{"a":[' = 1 + 32 levels; the last '[' (offset 96) is the 33rd level.
        string bad = "[" + string.Concat(Enumerable.Repeat("{\"a\":[", 16)) + new string(']', 16) + string.Concat(Enumerable.Repeat("}", 16));

        ContentDiagnostic d = Single(Walk(bad), GeneralRuleIds.SyntaxShape);

        AssertAt(d, 1, 97);
        Assert.Equal("depth 33", d.Value);
        Assert.Equal("at most 32 levels of nesting", d.Allowed);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-3")]
    public void Nesting32_ThroughObjectsAndMixedNesting_Passes()
    {
        Assert.True(Walk(ObjectNest(32), TopLevelShape.Object).Ok);
        Assert.True(Walk(MixedNest(16)).Ok);   // [{"a":[{"a": ... exactly 32 levels
        Assert.True(Walk(ObjectNest(32, "{\"a\": /* c */ "), TopLevelShape.Object).Ok);
    }

    [Theory]
    [Trait("Id", "CDD-PARSE-3")]
    [InlineData("{\"a\":", 161)]            // 32 units of 5 bytes, then the 33rd '{' at byte 160
    [InlineData("{\"a\": /* c */ ", 449)]   // 32 units of 14 bytes, reached after ':' and a comment
    public void Nesting33_ThroughObjects_IsCvGen16_AtThe33rdBrace(string unit, int column)
    {
        ContentDiagnostic d = Single(Walk(ObjectNest(33, unit), TopLevelShape.Object), GeneralRuleIds.SyntaxShape);

        AssertAt(d, 1, column);
        Assert.Equal("depth 33", d.Value);
        Assert.Equal("at most 32 levels of nesting", d.Allowed);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-3")]
    public void Nesting33_ThroughObjects_AfterALineComment_IsReportedOnTheLineOfTheBrace()
    {
        // Each unit ends its line, so the 33rd '{' is the first byte of line 33.
        ContentDiagnostic d = Single(Walk(ObjectNest(33, "{\"a\": // c\n"), TopLevelShape.Object), GeneralRuleIds.SyntaxShape);

        AssertAt(d, 33, 1);
        Assert.Equal("depth 33", d.Value);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-3")]
    public void AStrayCommaBeforeTheBracketAtDepth32_IsAnInvalidJsonError_NotNesting()
    {
        SyntaxResult r = Walk(new string('[', 32) + ",[");

        ContentDiagnostic d = Single(r, GeneralRuleIds.SyntaxShape);
        AssertAt(d, 1, 33);
        Assert.Equal("','", d.Value);
    }

    [Theory]
    [Trait("Id", "CDD-PARSE-3")]
    [InlineData("[{\"\\ud800\":1}]", 3)]
    [InlineData("[{\"\\udc00\":1}]", 3)]
    [InlineData("[{\"a\":1,\"\\ud800\":2,\"\\ud800\":3}]", 9)]
    [InlineData("[{\"\\ud83d\\ud83d\":1}]", 3)]
    public void AnUnpairedSurrogateEscapeInAKey_IsOneCvGen16_AtTheKey_AndEndsTheFile(string text, int column)
    {
        SyntaxResult r = Walk(text);

        AssertAt(Single(r, GeneralRuleIds.SyntaxShape), 1, column);
        Assert.False(r.Ok);
        Assert.Empty(r.Tokens);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-3")]
    public void APairedSurrogateEscapeInAKey_IsAccepted_AndComparesByItsUnescapedBytes()
    {
        Assert.True(Walk("[{\"\\ud83d\\ude00\":1}]").Ok);
        ContentDiagnostic d = Single(Walk("[{\"\\ud83d\\ude00\":1,\"\U0001F600\":2}]"), GeneralRuleIds.DuplicateKey);
        Assert.Equal("\U0001F600", d.Value);
    }

    [Theory]
    [Trait("Id", "CDD-PARSE-3")]
    [InlineData("[{\"a\":\"\\ud800\"}]")]
    [InlineData("[\"\\udc00\"]")]
    public void AnUnpairedSurrogateEscapeInAValue_IsNotUnescapedByStage1_SoItIsAccepted(string text)
    {
        // Values stay raw in the token table; the schema stage decodes strings (story 018) and owns this case.
        Assert.True(Walk(text).Ok);
    }

    [Theory]
    [Trait("Id", "CDD-PARSE-3")]
    [InlineData("{}", TopLevelShape.Array, 1, 1)]
    [InlineData("5", TopLevelShape.Array, 1, 1)]
    [InlineData("\"s\"", TopLevelShape.Array, 1, 1)]
    [InlineData("  // c\n  null", TopLevelShape.Array, 2, 3)]
    [InlineData("[]", TopLevelShape.Object, 1, 1)]
    [InlineData("[{}]", TopLevelShape.Object, 1, 1)]
    public void WrongTopLevelShape_IsCvGen16_AtTheFirstToken(string text, TopLevelShape expected, int line, int column)
    {
        ContentDiagnostic d = Single(Walk(text, expected), GeneralRuleIds.SyntaxShape);

        AssertAt(d, line, column);
    }

    [Theory]
    [Trait("Id", "CDD-PARSE-3")]
    [InlineData("5.")]
    [InlineData("+5")]
    [InlineData("[5.]")]
    [InlineData("[+5]")]
    [InlineData("[1e]")]
    [InlineData("[01]")]
    [InlineData("[tru]")]
    [InlineData("[\"a\"")]
    [InlineData("[1,")]
    [InlineData("[1 2]")]
    [InlineData("[] []")]
    [InlineData("[]x")]
    [InlineData("[{'a':1}]")]
    [InlineData("[{\"a\" 1}]")]
    [InlineData("[ /* open")]
    public void InvalidJson_IsExactlyOneCvGen16(string text)
    {
        SyntaxResult r = Walk(text);

        Single(r, GeneralRuleIds.SyntaxShape);
        Assert.False(r.Ok);
        Assert.Empty(r.Tokens);
    }

    [Theory]
    [Trait("Id", "CDD-PARSE-3")]
    [InlineData("[+5]", TopLevelShape.Array, 1, 2)]
    [InlineData("[\n  1,\n  @\n]", TopLevelShape.Array, 3, 3)]
    [InlineData("{\"a\":1,,}", TopLevelShape.Object, 1, 8)]
    [InlineData("[] x", TopLevelShape.Array, 1, 4)]
    public void InvalidJson_IsReportedAtTheFailingByte(string text, TopLevelShape shape, int line, int column)
    {
        AssertAt(Single(Walk(text, shape), GeneralRuleIds.SyntaxShape), line, column);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-3")]
    public void InvalidJson_AfterABom_IsReportedInOriginalFileBytes()
    {
        byte[] bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8("[+5]")).ToArray();

        AssertAt(Single(Walk(bytes), GeneralRuleIds.SyntaxShape), 1, 5);
    }

    [Theory]
    [Trait("Id", "CDD-PARSE-3")]
    [InlineData("[]", TopLevelShape.Array, 2)]
    [InlineData("{}", TopLevelShape.Object, 2)]
    [InlineData("[1,]", TopLevelShape.Array, 3)]
    [InlineData("{\"a\":1,}", TopLevelShape.Object, 4)]
    [InlineData("[ ] // end", TopLevelShape.Array, 2)]
    public void ValidFiles_AreAccepted(string text, TopLevelShape shape, int tokens)
    {
        SyntaxResult r = Walk(text, shape);

        Assert.True(r.Ok);
        Assert.Empty(r.Diagnostics);
        Assert.Equal(tokens, r.Tokens.Length);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-3")]
    public void AnEmptyArray_IsAValidEmptyFile()
    {
        SyntaxResult r = Walk("[]");

        Assert.True(r.Ok);
        Assert.Equal(new[] { "StartArray:[", "EndArray:]" }, Stream(r));
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-3")]
    public void ASyntaxErrorEndsOnlyThatFile_AndTheRestAreStillParsed()
    {
        var source = InMemorySource.FromText(
            ("defs/a_bad.jsonc", "[1,,]"),
            ("defs/b_good.jsonc", "[{\"id\":\"x\"}]"),
            ("balance/c_good.jsonc", "{\"k\":1}"),
            ("defs/d_bad.jsonc", "{}"));

        SyntaxStageResult r = SyntaxStage.Run(
            source, p => p.StartsWith("balance/", StringComparison.Ordinal) ? TopLevelShape.Object : TopLevelShape.Array);

        Assert.Equal(4, r.Files.Length);
        Assert.Equal("balance/c_good.jsonc", r.Files[0].Path);
        Assert.True(r.Files[0].Ok);
        Assert.False(r.Files[1].Ok);
        Assert.True(r.Files[2].Ok);
        Assert.Equal(6, r.Files[2].Tokens.Length);
        Assert.False(r.Files[3].Ok);
        Assert.Equal(2, r.Diagnostics.Length);
        Assert.Equal("defs/a_bad.jsonc", r.Diagnostics[0].Location.Path);
        Assert.Equal("defs/d_bad.jsonc", r.Diagnostics[1].Location.Path);
        Assert.False(r.Ok);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-3")]
    public void TheStage_ReportsAMissingFileAndStillParsesTheOthers()
    {
        var source = new MixedSource();

        SyntaxStageResult r = SyntaxStage.Run(source, _ => TopLevelShape.Array);

        Assert.Single(r.Files);
        Assert.Equal(new[] { GeneralRuleIds.ManifestFile }, r.Diagnostics.Select(d => d.RuleId).ToArray());
        Assert.Equal("defs/a_missing.jsonc", r.Diagnostics[0].Value);
    }

    private sealed class MixedSource : IContentSource
    {
        public ImmutableArray<string> ListManifest() => ImmutableArray.Create("defs/a_missing.jsonc", "defs/b.jsonc");

        public bool TryRead(string path, out ReadOnlyMemory<byte> bytes)
        {
            bytes = Utf8("[]");
            return string.Equals(path, "defs/b.jsonc", StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------ line endings (CR, CRLF, mixed)

    [Theory]
    [Trait("Id", "CDD-PARSE-3")]
    [InlineData("[\n  {\"a\": 1},\n  {\"a\": @}\n]\n", 3, 9)]
    [InlineData("[\r\n  {\"a\": 1},\r\n  {\"a\": @}\r\n]\r\n", 3, 9)]
    [InlineData("[\r\n  {\"a\": 1},\n  {\"a\":\r @}\r\n]\n", 3, 10)]
    [InlineData("[\r  {\"a\": 1},\n  {\"a\": @}\n]\n", 2, 9)]
    public void SyntaxErrorOnALaterLine_HasTheSameLineForLfCrlfAndMixed_AndALoneCrIsNotALineBreak(string text, int line, int column)
    {
        AssertAt(Single(Walk(text), GeneralRuleIds.SyntaxShape), line, column);
    }

    [Theory]
    [Trait("Id", "CDD-PARSE-2")]
    [InlineData("[\n  /* one\n     two */ {\"a\":1, \"a\":2}\n]", 3, 21)]
    [InlineData("[\r\n  /* one\r\n     two */ {\"a\":1, \"a\":2}\r\n]", 3, 21)]
    [InlineData("[\r\n  /* one\r\n     two */ {\"a\":1,\r \"a\":2}\n]", 3, 22)]
    [InlineData("[\r  /* one\r\n     two */ {\"a\":1, \"a\":2}\n]", 2, 21)]
    public void DuplicateKeyAfterAMultiLineComment_HasTheSameLineForLfCrlfAndMixed(string text, int line, int column)
    {
        AssertAt(Single(Walk(text), GeneralRuleIds.DuplicateKey), line, column);
    }

    // ------------------------------------------------------------ nested and large key sets

    [Theory]
    [Trait("Id", "CDD-PARSE-2")]
    [InlineData("[{\"a\":{\"b\":1},\"a\":2}]", 1, 15, "a")]
    [InlineData("[{\"x\":[{\"k\":1,\"k\":2}]}]", 1, 15, "k")]
    public void DuplicateKey_AroundNestedObjects_IsOneCvGen04_AtTheRepeatedKey(string text, int line, int column, string key)
    {
        ContentDiagnostic d = Single(Walk(text), GeneralRuleIds.DuplicateKey);

        AssertAt(d, line, column);
        Assert.Equal(key, d.Value);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-2")]
    public void DuplicateKey_AtTheEndOfAnObjectWithMoreThan256KeyBytes_IsFound()
    {
        var text = new StringBuilder("[{");
        for (int i = 0; i < 60; i++)
        {
            text.Append("\"k").Append(i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)).Append("\":1,");
        }

        text.Append("\"k000\":2}]");
        ContentDiagnostic d = Single(Walk(text.ToString()), GeneralRuleIds.DuplicateKey);
        Assert.Equal("k000", d.Value);

        string longKey = new string('x', 300);
        ContentDiagnostic longDup = Single(Walk("[{\"" + longKey + "\":1,\"y\":2,\"" + longKey + "\":3}]"), GeneralRuleIds.DuplicateKey);
        Assert.Equal(longKey, longDup.Value);
    }

    [Theory]
    [Trait("Id", "CDD-PARSE-2")]
    [InlineData("[{\"a\":{\"a\":1},\"b\":{\"a\":2}}]", TopLevelShape.Array)]
    [InlineData("[{\"a\":1,\"o\":{\"z\":1}},{\"a\":2,\"o\":{\"z\":2}}]", TopLevelShape.Array)]
    [InlineData("{\"o\":{\"x\":1},\"x\":2}", TopLevelShape.Object)]
    [InlineData("{\"o\":{\"x\":1,\"p\":{\"x\":2}},\"x\":3,\"q\":{\"x\":4}}", TopLevelShape.Object)]
    public void AKeyReusedInANestedOrSiblingObject_IsNotADuplicate(string text, TopLevelShape shape)
    {
        SyntaxResult r = Walk(text, shape);

        Assert.True(r.Ok);
        Assert.Empty(r.Diagnostics);
    }

    [Theory]
    [Trait("Id", "CDD-PARSE-3")]
    [InlineData("5.", 1, 3, "end of file")]       // an incomplete number is reported where the file ends
    [InlineData("+5", 1, 1, "'+'")]
    [InlineData("[1,", 1, 3, "','")]             // the reader fails at the dangling comma, not at the end of file
    [InlineData("[\"a\"", 1, 5, "end of file")]   // unterminated array
    [InlineData("[01]", 1, 3, "'1'")]            // the digit after the leading zero
    [InlineData("[] []", 1, 4, "'['")]           // content after the single top-level value
    [InlineData("[{'a':1}]", 1, 3, "'\''")]      // a single quote where a key must start
    public void InvalidJson_PositionsArePinned(string text, int line, int column, string value)
    {
        ContentDiagnostic d = Single(Walk(text), GeneralRuleIds.SyntaxShape);

        AssertAt(d, line, column);
        Assert.Equal(value, d.Value);
    }

    // ------------------------------------------------------------ AC3 (CDD-PARSE-2, duplicate-key half)

    [Fact]
    [Trait("Id", "CDD-PARSE-2")]
    public void DuplicateKey_IsOneCvGen04_AtTheDuplicateKeysOwnLineAndColumn()
    {
        // Line 7 is: TAB {"id":"a",  "id":"b"}   (the second "id" opens at byte column 14; the TAB counts 1).
        string text = "[\n// 2\n// 3\n// 4\n// 5\n// 6\n\t{\"id\":\"a\",  \"id\":\"b\"}\n]\n";

        SyntaxResult r = Walk(text);

        ContentDiagnostic d = Single(r, GeneralRuleIds.DuplicateKey);
        Assert.Equal("defs/items.jsonc:7:14", d.Location.ToString());
        Assert.Equal("id", d.Value);
        Assert.Equal("each key once per object", d.Allowed);
        Assert.False(r.Ok);
        Assert.Equal(8, r.Tokens.Length);   // the walk continued to the end of the file
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-2")]
    public void DuplicateKey_ColumnIsInUtf8Bytes_SoAMultiByteCharShiftsIt()
    {
        // [ "é" , { "k" : 1 , "k" ...  with é = 2 bytes: the duplicate "k" opens at byte column 16 (15 with a plain 'e').
        ContentDiagnostic d = Single(Walk("[\"\u00e9\", {\"k\":1, \"k\":2}]"), GeneralRuleIds.DuplicateKey);
        ContentDiagnostic plain = Single(Walk("[\"e\", {\"k\":1, \"k\":2}]"), GeneralRuleIds.DuplicateKey);

        AssertAt(d, 1, 16);
        AssertAt(plain, 1, 15);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-2")]
    public void DuplicateKey_CrlfLinesStillCountByLf()
    {
        ContentDiagnostic d = Single(Walk("[\r\n{\"a\":1,\r\n \"a\":2}\r\n]"), GeneralRuleIds.DuplicateKey);

        AssertAt(d, 3, 2);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-2")]
    public void SeveralDuplicateKeys_GiveSeveralCvGen04_AndTheWalkContinues()
    {
        SyntaxResult r = Walk("[{\"a\":1,\"a\":2,\"b\":3,\"b\":4},{\"a\":5,\"a\":6}]");

        Assert.Equal(3, r.Diagnostics.Length);
        Assert.All(r.Diagnostics, d => Assert.Equal(GeneralRuleIds.DuplicateKey, d.RuleId));
        Assert.Equal(new[] { "a", "b", "a" }, r.Diagnostics.Select(d => d.Value).ToArray());
        Assert.Equal(JsonTokenType.EndArray, r.Tokens[^1].Kind);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-2")]
    public void DuplicateKeys_AreCompared_ByUnescapedName()
    {
        ContentDiagnostic d = Single(Walk("[{\"a\":1,\"\\u0061\":2}]"), GeneralRuleIds.DuplicateKey);

        Assert.Equal("a", d.Value);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-2")]
    public void TheSameKey_InSiblingAndNestedObjects_IsNotADuplicate()
    {
        SyntaxResult r = Walk("[{\"id\":1},{\"id\":2,\"x\":{\"id\":3,\"y\":{\"id\":4}},\"z\":[{\"id\":5}]},{\"id\":6}]");

        Assert.True(r.Ok);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-2")]
    public void SchemaAndSuppressKeys_AreOrdinaryAcceptedKeys()
    {
        Assert.True(Walk("{\"$schema\":\"x\",\"suppress\":[{\"rule\":\"CV-X-01\",\"why\":\"y\"}],\"a\":1}", TopLevelShape.Object).Ok);
        Assert.True(Walk("[{\"$schema\":\"x\",\"suppress\":[],\"id\":\"a\"}]").Ok);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-2")]
    public void DuplicateKey_ThenASyntaxError_ReportsBoth()
    {
        SyntaxResult r = Walk("[{\"a\":1,\"a\":2,]");

        Assert.Equal(new[] { GeneralRuleIds.DuplicateKey, GeneralRuleIds.SyntaxShape }, r.Diagnostics.Select(d => d.RuleId).ToArray());
        Assert.Empty(r.Tokens);
    }

    // ------------------------------------------------------------ TextPosition

    [Fact]
    [Trait("Id", "CDD-PARSE-2")]
    public void TextPosition_MatchesSourceLocationFromUtf8Offset_AtEveryOffset()
    {
        byte[] file = Utf8("\u00ef\u00bb\n[\r\n\t\"\u00e9\u20ac\"\n\n  x\n");
        var positions = new TextPosition(Path, file);

        for (int i = 0; i <= file.Length; i++)
        {
            Assert.Equal(SourceLocation.FromUtf8Offset(Path, file, i), positions.At(i));
        }

        Assert.Equal(6, positions.LineCount);
        Assert.Equal(file.Length, positions.At(file.Length + 50).Column + positions.LineStart(5) - 1);
    }

    // ------------------------------------------------------------ AC4 (sources, architecture)

    [Fact]
    [Trait("Id", "CD2")]
    public void DirectorySource_ListsOnlyJsoncFiles_SlashSeparated_OrdinalSorted()
    {
        string root = NewTempDirectory();
        try
        {
            Write(root, "a.jsonc", "[]");
            Write(root, "B.jsonc", "[]");
            Write(root, "items-x.jsonc", "[]");
            Write(root, "items/zz.jsonc", "[]");
            Write(root, "items/deep/q.jsonc", "[]");
            Write(root, "notes.json", "[]");
            Write(root, "items/readme.txt", "x");
            Write(root, "items/a.jsonc.bak", "x");
            Write(root, "empty.jsonc", string.Empty);

            var source = new DirectorySource(root);

            Assert.Equal(
                new[] { "B.jsonc", "a.jsonc", "empty.jsonc", "items-x.jsonc", "items/deep/q.jsonc", "items/zz.jsonc" },
                source.ListManifest().ToArray());
            Assert.True(source.TryRead("items/zz.jsonc", out ReadOnlyMemory<byte> bytes));
            Assert.Equal(Utf8("[]"), bytes.ToArray());
            Assert.True(source.TryRead("empty.jsonc", out ReadOnlyMemory<byte> empty));
            Assert.Equal(0, empty.Length);
            Assert.False(source.TryRead("missing.jsonc", out ReadOnlyMemory<byte> none));
            Assert.Equal(0, none.Length);
            Assert.False(source.TryRead("items/nope/x.jsonc", out _));
            Assert.False(source.TryRead("../outside.jsonc", out _));
            Assert.False(source.TryRead(string.Empty, out _));
            Assert.False(source.TryRead("items", out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Id", "CD2")]
    public void DirectorySource_OverAMissingRoot_ListsNothing()
    {
        var source = new DirectorySource(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pm-no-such-" + Guid.NewGuid().ToString("N")));

        Assert.True(source.ListManifest().IsEmpty);
        Assert.False(source.TryRead("a.jsonc", out _));
    }

    [Fact]
    [Trait("Id", "CD2")]
    public void DirectorySource_FeedsTheStage_EndToEnd()
    {
        string root = NewTempDirectory();
        try
        {
            Write(root, "defs/items.jsonc", "[{\"id\":\"a\",\"id\":\"b\"}]");
            Write(root, "defs/ok.jsonc", "[]");

            SyntaxStageResult r = SyntaxStage.Run(new DirectorySource(root), _ => TopLevelShape.Array);

            Assert.Equal(2, r.Files.Length);
            ContentDiagnostic d = Assert.Single(r.Diagnostics);
            Assert.Equal("defs/items.jsonc", d.Location.Path);
            Assert.Equal(GeneralRuleIds.DuplicateKey, d.RuleId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Id", "CD2")]
    public void InMemorySource_ServesFixtureBytes_ByPath()
    {
        var source = new InMemorySource(
            ("z.jsonc", Utf8("[1]")), ("a/b.jsonc", Utf8("[]")), ("B.jsonc", Array.Empty<byte>()), ("a-b.jsonc", Utf8("{}")));

        Assert.Equal(new[] { "B.jsonc", "a-b.jsonc", "a/b.jsonc", "z.jsonc" }, source.ListManifest().ToArray());
        Assert.True(source.TryRead("z.jsonc", out ReadOnlyMemory<byte> bytes));
        Assert.Equal(Utf8("[1]"), bytes.ToArray());
        Assert.True(source.TryRead("B.jsonc", out ReadOnlyMemory<byte> empty));
        Assert.Equal(0, empty.Length);
        Assert.False(source.TryRead("b.jsonc", out _));
        Assert.False(source.TryRead("missing.jsonc", out _));
        Assert.Throws<ArgumentException>(() => new InMemorySource(("a.jsonc", Utf8("[]")), ("a.jsonc", Utf8("[]"))));
    }

    [Fact]
    [Trait("Id", "CD2")]
    public void NoSimFile_ContainsTheGameResourceScheme_OrReferencesGodot()
    {
        string scheme = "res:" + "//";
        string simRoot = System.IO.Path.Combine(FindRepoRoot(), "src", "PerformativeMail.Sim");
        int scanned = 0;
        foreach (string file in Directory.EnumerateFiles(simRoot, "*.cs", SearchOption.AllDirectories))
        {
            string normalised = file.Replace('\\', '/');
            if (normalised.Contains("/obj/", StringComparison.Ordinal) || normalised.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            Assert.False(text.Contains(scheme, StringComparison.Ordinal), file + " contains the game resource scheme");
            Assert.False(text.Contains("using Godot", StringComparison.Ordinal), file + " references Godot");
            scanned++;
        }

        Assert.True(scanned > 100, "the scan found only " + scanned + " files");
    }

    [Fact]
    [Trait("Id", "CD2")]
    public void DirectorySource_TryRead_RefusesRootedPathsAndPathsThatEscapeTheRoot()
    {
        string root = NewTempDirectory();
        string evil = root + "-evil";
        try
        {
            Write(root, "a.jsonc", "[]");
            Write(evil, "a.jsonc", "[]");
            var source = new DirectorySource(root);

            Assert.True(source.TryRead("a.jsonc", out _));
            Assert.False(source.TryRead(System.IO.Path.Combine(root, "a.jsonc"), out _));   // rooted, though the file exists
            Assert.False(source.TryRead("/etc/hostname", out _));
            Assert.False(source.TryRead("../" + System.IO.Path.GetFileName(root) + "-evil/a.jsonc", out _));   // sibling folder sharing the root's name prefix

            // Pinned current behaviour: a ".." that stays inside the root is normalised, so this reads root/a.jsonc.
            Assert.True(source.TryRead("items/../a.jsonc", out ReadOnlyMemory<byte> inside));
            Assert.Equal(Utf8("[]"), inside.ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(evil, recursive: true);
        }
    }

    // ------------------------------------------------------------ AC5 (CDD-PARSE-4, parser half)

    private static readonly string[] _newBans =
    {
        "T:System.Text.Json.JsonSerializer",
        "T:System.Text.Json.JsonDocument",
        "M:System.Text.Json.Utf8JsonReader.GetDouble",
        "M:System.Text.Json.Utf8JsonReader.GetDecimal",
        "M:System.Text.Json.Utf8JsonReader.GetSingle",
        "M:System.Double.Parse(System.String)",
        "M:System.Double.Parse(System.String,System.Globalization.NumberStyles)",
        "M:System.Double.Parse(System.String,System.IFormatProvider)",
        "M:System.Double.Parse(System.String,System.Globalization.NumberStyles,System.IFormatProvider)",
        "M:System.Double.Parse(System.ReadOnlySpan{System.Char},System.Globalization.NumberStyles,System.IFormatProvider)",
        "M:System.Double.Parse(System.ReadOnlySpan{System.Char},System.IFormatProvider)",
        "M:System.Double.Parse(System.ReadOnlySpan{System.Byte},System.Globalization.NumberStyles,System.IFormatProvider)",
        "M:System.Double.Parse(System.ReadOnlySpan{System.Byte},System.IFormatProvider)",
        "M:System.String.Compare(System.String,System.String)",
        "M:System.String.Compare(System.String,System.String,System.Boolean)",
        "M:System.String.CompareTo(System.String)",
        "M:System.String.CompareTo(System.Object)",
        "M:System.String.StartsWith(System.String)",
        "M:System.String.EndsWith(System.String)",
        "M:System.String.ToLower",
        "M:System.String.ToUpper",
        "M:System.String.ToLower(System.Globalization.CultureInfo)",
        "M:System.String.ToUpper(System.Globalization.CultureInfo)",
    };

    [Fact]
    [Trait("Id", "CDD-PARSE-4")]
    public void BannedSymbols_ListsTheParserBans_EachWithAFixtureThatCoversIt()
    {
        string root = FindRepoRoot();
        string[] lines = File.ReadAllLines(System.IO.Path.Combine(root, "src", "PerformativeMail.Sim", "BannedSymbols.txt"));
        var fixtureText = new StringBuilder();
        foreach (string f in Directory.EnumerateFiles(System.IO.Path.Combine(root, "tests", "fixtures", "analyzers"), "*.cs"))
        {
            fixtureText.Append(File.ReadAllText(f));
        }

        string fixtures = fixtureText.ToString();
        foreach (string id in _newBans)
        {
            Assert.True(Array.Exists(lines, l => l.StartsWith(id + ";", StringComparison.Ordinal)), "BannedSymbols.txt lacks " + id);
            Assert.True(fixtures.Contains("// covers: " + id + "\n", StringComparison.Ordinal) ||
                        fixtures.Contains("// covers: " + id + "\r\n", StringComparison.Ordinal), "no analyzer fixture covers " + id);
        }
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-4")]
    public void SimCsproj_SetsInvariantGlobalization()
    {
        string csproj = File.ReadAllText(System.IO.Path.Combine(FindRepoRoot(), "src", "PerformativeMail.Sim", "PerformativeMail.Sim.csproj"));

        Assert.Contains("<InvariantGlobalization>true</InvariantGlobalization>", csproj, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ helpers

    private static string NewTempDirectory()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pm-syntax-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(string root, string relative, string text)
    {
        string full = System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "PerformativeMail.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("PerformativeMail.sln not found above " + AppContext.BaseDirectory);
    }
}
