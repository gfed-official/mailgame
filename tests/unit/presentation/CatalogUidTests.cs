using System.Text.RegularExpressions;

namespace PerformativeMail.Sim.Tests.Unit.Presentation;

/// <summary>
/// godot-host-shell story 008 AC-2 / ADR-0019 section 5's catalog uid check, as a test instead of <c>tools/ci/check-catalog-uids.sh</c>:
/// every <c>ext_resource</c> line of <c>src/Game/Views/Catalogs/*.tres</c> carries <c>uid="uid://</c> (the editor writes it; a runtime save
/// stores paths only and breaks on rename), and checking zero lines is a failure. A path-only fixture row proves the check fails.
/// </summary>
public partial class CatalogUidTests
{
    [Fact]
    [Trait("Id", "TR-content-027")]
    public void Catalogs_EveryExtResource_CarriesAUid()
    {
        string dir = Path.Combine(FindRepoRoot(), "src", "Game", "Views", "Catalogs");
        string[] catalogs = Directory.GetFiles(dir, "*.tres");

        (int checkedLines, List<string> missing) = Check(catalogs.Select(f => (Path.GetFileName(f), File.ReadAllText(f))));

        Assert.True(checkedLines > 0, $"checked 0 ext_resources in {catalogs.Length} catalogs");
        Assert.Empty(missing);
    }

    [Fact]
    [Trait("Id", "TR-content-027")]
    public void Check_PathOnlyFixture_Fails()
    {
        const string PathOnly = "[gd_resource type=\"Resource\" script_class=\"ViewCatalog\" format=3]\n\n"
            + "[ext_resource type=\"PackedScene\" path=\"res://src/Game/Views/Placeholder/PlaceholderView.tscn\" id=\"1\"]\n";

        (int checkedLines, List<string> missing) = Check([("fixture.tres", PathOnly)]);

        Assert.Equal(1, checkedLines);
        Assert.Equal(["fixture.tres:3"], missing);
    }

    private static (int Checked, List<string> Missing) Check(IEnumerable<(string Name, string Text)> catalogs)
    {
        int checkedLines = 0;
        var missing = new List<string>();
        foreach ((string name, string text) in catalogs)
        {
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("[ext_resource", StringComparison.Ordinal))
                {
                    continue;
                }

                checkedLines++;
                if (!UidAttribute().IsMatch(lines[i]))
                {
                    missing.Add($"{name}:{i + 1}");
                }
            }
        }

        return (checkedLines, missing);
    }

    [GeneratedRegex("\\buid=\"uid://[0-9a-z]+\"")]
    private static partial Regex UidAttribute();

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PerformativeMail.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (PerformativeMail.sln) not found.");
    }
}
