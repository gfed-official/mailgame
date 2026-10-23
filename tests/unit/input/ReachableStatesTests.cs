using System.Collections.Immutable;
using System.Text;
using PerformativeMail.Client.Core.Input;
using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 004, AC H-R2-8: the reachable-state set produced from the R2 "Reachable states" rule data equals the
/// committed golden list <c>tests/unit/input/ReachableStates.golden</c> (one canonical line per reachable id, ascending);
/// each of the seven clauses rejects its own fixture; and removing a clause changes the set and fails the golden comparison.
/// <para>
/// Regenerating the golden file is an explicit opt-in: run with <c>PM_UPDATE_GOLDEN=1</c>. Without it the file is only read.
/// The canonical line is formatted here, not by <see cref="RouterState.ToDebugString()"/>, so a debug-overlay text change
/// cannot silently invalidate the golden file.
/// </para>
/// </summary>
[Trait("Id", "H-R2-8")]
public class ReachableStatesTests
{
    private const string UpdateGoldenVariable = "PM_UPDATE_GOLDEN";
    private const string GoldenRelativePath = "tests/unit/input/ReachableStates.golden";

    private static ushort Pack(
        CaptureLayer capture = CaptureLayer.None,
        OverlayLayer overlay = OverlayLayer.None,
        BodyLayer body = BodyLayer.OnFoot,
        bool holding = false,
        bool aiming = false,
        SubmenuLayer submenu = SubmenuLayer.None) =>
        RouterStateInfo.Pack(capture, overlay, body, holding, aiming, submenu);

    /// <summary>Canonical golden line: <c>0042 capture=None overlay=Panel body=OnFoot holding=0 aiming=0 submenu=None</c>.</summary>
    private static string CanonicalLine(ushort id)
    {
        Assert.True(RouterStateInfo.Unpack(id, out CaptureLayer c, out OverlayLayer o, out BodyLayer b, out bool h, out bool a, out SubmenuLayer s));
        return $"{id:D4} capture={c} overlay={o} body={b} holding={(h ? 1 : 0)} aiming={(a ? 1 : 0)} submenu={s}";
    }

    private static string[] CanonicalLines(ReachableSet set)
    {
        var lines = new string[set.Count];
        ReadOnlySpan<ushort> ids = set.Ids;
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = CanonicalLine(ids[i]);
        }

        return lines;
    }

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

    private static string GoldenPath() => Path.Combine(FindRepoRoot(), GoldenRelativePath);

    private static string[] ReadGolden()
    {
        string path = GoldenPath();
        Assert.True(File.Exists(path), $"Golden file missing: {path}. Create it deliberately with {UpdateGoldenVariable}=1.");
        var lines = new List<string>();
        foreach (string raw in File.ReadAllText(path).Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines.ToArray();
    }

    /// <summary>Empty when equal; otherwise a message with the count and both set differences (golden-only and actual-only lines).</summary>
    private static string GoldenDiff(string[] golden, string[] actual)
    {
        var goldenSet = new HashSet<string>(golden, StringComparer.Ordinal);
        var actualSet = new HashSet<string>(actual, StringComparer.Ordinal);
        var onlyGolden = golden.Where(l => !actualSet.Contains(l)).ToList();
        var onlyActual = actual.Where(l => !goldenSet.Contains(l)).ToList();
        bool sameOrder = golden.AsSpan().SequenceEqual(actual);
        if (onlyGolden.Count == 0 && onlyActual.Count == 0 && sameOrder)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append("Reachable-state set differs from the golden file: golden has ").Append(golden.Length)
          .Append(" lines, actual has ").Append(actual.Length).Append(sameOrder ? " (order equal).\n" : " (order or content differs).\n");
        sb.Append("In golden but not actual (").Append(onlyGolden.Count).Append("):\n");
        foreach (string l in onlyGolden)
        {
            sb.Append("  - ").Append(l).Append('\n');
        }

        sb.Append("In actual but not golden (").Append(onlyActual.Count).Append("):\n");
        foreach (string l in onlyActual)
        {
            sb.Append("  + ").Append(l).Append('\n');
        }

        sb.Append("If the change is deliberate, regenerate with ").Append(UpdateGoldenVariable).Append("=1 and review the diff.");
        return sb.ToString();
    }

    /// <summary>The seven GDD clauses written as plain branches, independent of the rule data, as a cross-check of that data.</summary>
    private static bool OracleReachable(CaptureLayer c, OverlayLayer o, BodyLayer b, bool h, bool a, SubmenuLayer s)
    {
        if (a && !(b == BodyLayer.OnFoot && o == OverlayLayer.None && !h && c == CaptureLayer.None))
        {
            return false;
        }

        if (s != SubmenuLayer.None && o != OverlayLayer.Panel)
        {
            return false;
        }

        if (h && b != BodyLayer.OnFoot)
        {
            return false;
        }

        if (o == OverlayLayer.Panel && b != BodyLayer.OnFoot)
        {
            return false;
        }

        if (o == OverlayLayer.History && b != BodyLayer.OnFoot && b != BodyLayer.Dead)
        {
            return false;
        }

        if (b == BodyLayer.Dead
            && !((o == OverlayLayer.None || o == OverlayLayer.Draft || o == OverlayLayer.Results || o == OverlayLayer.History) && !h && !a))
        {
            return false;
        }

        bool noAimOverlay = o is OverlayLayer.Map or OverlayLayer.Station or OverlayLayer.Draft or OverlayLayer.Results or OverlayLayer.History;
        return !(noAimOverlay && a);
    }

    // ------------------------------------------------------------ golden

    [Fact]
    public void ReachableSet_EqualsCommittedGoldenFile()
    {
        string[] actual = CanonicalLines(ReachableSet.Default);

        if (Environment.GetEnvironmentVariable(UpdateGoldenVariable) == "1")
        {
            File.WriteAllText(GoldenPath(), string.Join("\n", actual) + "\n");
            return;
        }

        string diff = GoldenDiff(ReadGolden(), actual);

        Assert.True(diff.Length == 0, diff);
    }

    [Fact]
    public void GoldenFile_IsAscendingByIdWithOneLinePerId()
    {
        string[] golden = ReadGolden();

        int previous = -1;
        foreach (string line in golden)
        {
            int id = int.Parse(line.AsSpan(0, 4), System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(id > previous, $"Golden line '{line}' is not strictly after id {previous}");
            Assert.InRange(id, 0, RouterStateInfo.StateCount - 1);
            previous = id;
        }
    }

    [Fact]
    public void GoldenDiff_ReportsBothSetDifferencesWhenLinesDiffer()
    {
        string[] golden = ["0001 a", "0002 b"];
        string[] actual = ["0002 b", "0003 c"];

        string message = GoldenDiff(golden, actual);

        Assert.Contains("golden has 2 lines, actual has 2", message);
        Assert.Contains("- 0001 a", message);
        Assert.Contains("+ 0003 c", message);
        Assert.Contains(UpdateGoldenVariable + "=1", message);
    }

    [Fact]
    public void ReachableSet_MatchesTheIndependentBranchOracleForAll1008Tuples()
    {
        var mismatches = new List<string>();
        for (int i = 0; i < RouterStateInfo.StateCount; i++)
        {
            ushort id = (ushort)i;
            Assert.True(RouterStateInfo.Unpack(id, out CaptureLayer c, out OverlayLayer o, out BodyLayer b, out bool h, out bool a, out SubmenuLayer s));
            if (OracleReachable(c, o, b, h, a, s) != ReachableSet.Default.Contains(id))
            {
                mismatches.Add(CanonicalLine(id));
            }
        }

        Assert.True(mismatches.Count == 0, "Rule data disagrees with the GDD clauses for: " + string.Join("; ", mismatches));
    }

    [Fact]
    public void ReachableSet_IsAnAscendingSubsetOfTheTupleSpaceAndAgreesWithContains()
    {
        ReachableSet set = ReachableSet.Default;

        Assert.InRange(set.Count, 1, RouterStateInfo.StateCount - 1);
        int seen = 0;
        for (int i = 0; i < RouterStateInfo.StateCount; i++)
        {
            bool inIds = seen < set.Count && set.Ids[seen] == i;
            Assert.Equal(inIds, set.Contains((ushort)i));
            if (inIds)
            {
                seen++;
            }
        }

        Assert.Equal(set.Count, seen);
        Assert.False(set.Contains(RouterStateInfo.StateCount));
        Assert.False(set.Contains(ushort.MaxValue));
    }

    // ------------------------------------------------------------ one fixture per clause

    public static TheoryData<int, string, ushort> ClauseFixtures => new()
    {
        { ReachableRules.AimingIndex, "aiming with Panel", Pack(overlay: OverlayLayer.Panel, aiming: true) },
        { ReachableRules.AimingIndex, "aiming while Mounted", Pack(body: BodyLayer.Mounted, aiming: true) },
        { ReachableRules.AimingIndex, "aiming while holding (OnFoot)", Pack(holding: true, aiming: true) },
        { ReachableRules.AimingIndex, "aiming under Pause", Pack(capture: CaptureLayer.Pause, aiming: true) },
        { ReachableRules.SubmenuIndex, "ActionMenu with no overlay", Pack(submenu: SubmenuLayer.ActionMenu) },
        { ReachableRules.SubmenuIndex, "SplitStepper over Draft", Pack(overlay: OverlayLayer.Draft, submenu: SubmenuLayer.SplitStepper) },
        { ReachableRules.HoldingIndex, "holding while Mounted", Pack(body: BodyLayer.Mounted, holding: true) },
        { ReachableRules.HoldingIndex, "holding in Build", Pack(body: BodyLayer.Build, holding: true) },
        { ReachableRules.PanelIndex, "Panel while Mounted", Pack(overlay: OverlayLayer.Panel, body: BodyLayer.Mounted) },
        { ReachableRules.PanelIndex, "Panel while Build", Pack(overlay: OverlayLayer.Panel, body: BodyLayer.Build) },
        { ReachableRules.HistoryIndex, "History while Mounted", Pack(overlay: OverlayLayer.History, body: BodyLayer.Mounted) },
        { ReachableRules.HistoryIndex, "History while Build", Pack(overlay: OverlayLayer.History, body: BodyLayer.Build) },
        { ReachableRules.DeadIndex, "Dead with Map", Pack(overlay: OverlayLayer.Map, body: BodyLayer.Dead) },
        { ReachableRules.DeadIndex, "Dead with Station", Pack(overlay: OverlayLayer.Station, body: BodyLayer.Dead) },
        { ReachableRules.NoAimUnderOverlayIndex, "aiming with Map", Pack(overlay: OverlayLayer.Map, aiming: true) },
        { ReachableRules.NoAimUnderOverlayIndex, "aiming with Results", Pack(overlay: OverlayLayer.Results, aiming: true) },
    };

    [Fact]
    public void FirstViolation_OfAnOutOfRangeIdOrADefaultRuleList_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReachableRules.FirstViolation(ReachableRules.All, RouterStateInfo.StateCount));
        Assert.Throws<ArgumentException>(() => ReachableRules.FirstViolation(default, 0));
    }

    [Theory]
    [MemberData(nameof(ClauseFixtures))]
    public void Clause_RejectsItsFixture(int clauseIndex, string fixture, ushort id)
    {
        ReachabilityRule rule = ReachableRules.All[clauseIndex];

        Assert.True(rule.IsViolatedBy(id), $"Clause '{rule.Name}' should reject: {fixture} ({CanonicalLine(id)})");
        Assert.False(ReachableSet.Default.Contains(id), fixture);
        Assert.False(RouterState.IsReachable(id), fixture);
        Assert.True(ReachableRules.FirstViolation(ReachableRules.All, id) >= 0, fixture);
    }

    [Fact]
    public void ClauseList_HasTheSevenGddClausesInOrderWithNames()
    {
        Assert.Equal(7, ReachableRules.All.Length);
        Assert.Equal(
            [0, 1, 2, 3, 4, 5, 6],
            new[]
            {
                ReachableRules.AimingIndex, ReachableRules.SubmenuIndex, ReachableRules.HoldingIndex, ReachableRules.PanelIndex,
                ReachableRules.HistoryIndex, ReachableRules.DeadIndex, ReachableRules.NoAimUnderOverlayIndex,
            });
        foreach (ReachabilityRule rule in ReachableRules.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Name));
        }
    }

    // ------------------------------------------------------------ mutation

    [Theory]
    [InlineData(ReachableRules.AimingIndex)]
    [InlineData(ReachableRules.SubmenuIndex)]
    [InlineData(ReachableRules.HoldingIndex)]
    [InlineData(ReachableRules.PanelIndex)]
    [InlineData(ReachableRules.HistoryIndex)]
    [InlineData(ReachableRules.DeadIndex)]
    public void RemovingAClause_ChangesTheReachableSetAndFailsTheGoldenComparison(int clauseIndex)
    {
        ImmutableArray<ReachabilityRule> mutated = ReachableRules.All.RemoveAt(clauseIndex);

        ReachableSet set = ReachableSet.Build(mutated);

        Assert.True(set.Count > ReachableSet.Default.Count, $"Removing '{ReachableRules.All[clauseIndex].Name}' should admit more states");
        string diff = GoldenDiff(ReadGolden(), CanonicalLines(set));
        Assert.NotEqual(string.Empty, diff);
        Assert.Contains("In actual but not golden", diff);
    }

    [Fact]
    public void RemovingTheAimingClause_AdmitsExactlyTheClauseFixture()
    {
        ReachableSet mutated = ReachableSet.Build(ReachableRules.All.RemoveAt(ReachableRules.AimingIndex));
        ushort fixture = Pack(overlay: OverlayLayer.Panel, aiming: true);

        Assert.False(ReachableSet.Default.Contains(fixture));
        Assert.True(mutated.Contains(fixture));
    }

    [Fact]
    public void NoAimUnderOverlayClause_IsRedundantWithTheAimingClause()
    {
        // GDD clause 7 ("overlay in {Map, Station, Draft, Results, History} => not aiming") is implied by clause 1
        // ("aiming => no overlay"). It is kept as data because the GDD lists it; removing it must not change the set.
        ReachableSet mutated = ReachableSet.Build(ReachableRules.All.RemoveAt(ReachableRules.NoAimUnderOverlayIndex));

        Assert.Equal(CanonicalLines(ReachableSet.Default), CanonicalLines(mutated));
    }

    [Fact]
    public void EmptyRuleList_MakesEveryOneOf1008StatesReachable()
    {
        ReachableSet set = ReachableSet.Build(ImmutableArray<ReachabilityRule>.Empty);

        Assert.Equal(RouterStateInfo.StateCount, set.Count);
        Assert.False(set.Contains(RouterStateInfo.StateCount));
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        Assert.Equal(CanonicalLines(ReachableSet.Default), CanonicalLines(ReachableSet.Build(ReachableRules.All)));
    }
}
