using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Hashing;

namespace PerformativeMail.Sim.Tests.Unit.Presentation;

/// <summary>
/// godot-host-shell story 008 AC-1 (CD28, CDD-SCHEMA-7, ADR-0019 section 5): <see cref="ViewMapping.Map{T}"/> over the def ids
/// <c>axe, fiber, log</c> (DefIndex order) with string stand-ins for scenes. Every row also checks row (f): the result is in DefIndex
/// order and exactly the def count long.
/// </summary>
public class ViewMappingTests
{
    private static readonly string[] _defs = ["axe", "fiber", "log"];

    [Theory]
    [Trait("Id", "CDD-SCHEMA-7")]
    [InlineData("a every def has an entry", new[] { "log", "axe", "fiber" }, new[] { "L", "A", "F" }, false, new[] { "A", "F", "L" }, new string[0])]
    [InlineData("b missing def, debug", new[] { "axe", "log" }, new[] { "A", "L" }, false, new[] { "A", "PH", "L" }, new[] { "warn MissingView fiber" })]
    [InlineData("b missing def, release", new[] { "axe", "log" }, new[] { "A", "L" }, true, new[] { "A", "PH", "L" }, new[] { "error MissingView fiber" })]
    [InlineData("c catalog id with no def", new[] { "axe", "fiber", "log", "saw" }, new[] { "A", "F", "L", "S" }, false, new[] { "A", "F", "L" }, new[] { "warn UnknownDef saw" })]
    [InlineData("d length mismatch, debug", new[] { "axe", "fiber" }, new[] { "A" }, false, new[] { "PH", "PH", "PH" }, new[] { "error LengthMismatch 2/1" })]
    [InlineData("e duplicate catalog id", new[] { "axe", "axe", "fiber", "log" }, new[] { "A", "A2", "F", "L" }, false, new[] { "A", "F", "L" }, new[] { "error DuplicateId axe" })]
    public void Map_CatalogRows_GiveDefIndexOrderedViewsAndFindings(
        string row, string[] catalogIds, string[] catalogViews, bool release, string[] expectedViews, string[] expectedFindings)
    {
        ViewMap<string> map = ViewMapping.Map<string>(_defs, catalogIds, catalogViews, "PH", release);

        Assert.True(expectedViews.SequenceEqual(map.ByDefIndex), $"{row}: views {string.Join(",", map.ByDefIndex)}");
        Assert.Equal(expectedFindings, map.Findings.Select(f => $"{(f.IsError ? "error" : "warn")} {f.Kind} {f.Id}"));
    }

    [Fact]
    [Trait("Id", "CDD-SCHEMA-7")]
    public void Map_M1aContentTwice_EqualResultsPlaceholderEverywhereAndContentHashUnchanged()
    {
        ContentSet set = BuiltInContent.CreateM1a();
        Hash256 before = ContentHash.Compute(set);

        ViewMap<string> first = ViewMapping.Map<string>(ViewMapping.DefIds(set, DefTableId.MailKinds), [], [], "PH", release: false);
        ViewMap<string> second = ViewMapping.Map<string>(ViewMapping.DefIds(set, DefTableId.MailKinds), [], [], "PH", release: false);

        Assert.Equal(first.ByDefIndex, second.ByDefIndex);
        Assert.Equal(first.Findings, second.Findings);
        Assert.Equal(["large", "letter", "medium", "postcard", "small"], first.Findings.Select(f => f.Id));
        Assert.All(first.ByDefIndex, v => Assert.Equal("PH", v));
        Assert.Equal(before, ContentHash.Compute(set));
    }
}
