using System.Collections.Immutable;
using System.Reflection;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 001, "Settings binding" (TR-movement-020): <see cref="PlayerSettings"/> is built once from content-data's
/// <see cref="PlayerBalance"/>, exposes every key the later stories use, equals the GDD defaults for the built-in M-1a content,
/// and a key missing from the balance table is a build error, never a default.
/// </summary>
public class PlayerSettingsTests
{
    /// <summary>The GDD Tuning Knobs defaults, written out as independent literals (not read from content).</summary>
    private static PlayerSettings GddDefaults() => new()
    {
            WalkMmPerSec = 5000,
            SprintMmPerSec = 7500,
            SwimMmPerSec = 2000,
            TicksAccel = 4,
            TicksDecel = 3,
            AirControlPm = 500,
            TurnDegPerSec = 720,
            G = 24_000,
            JumpHeightMm = 1200,
            ExtraJumpPm = 900,
            CoyoteTicks = 4,
            JumpBufferTicks = 4,
            StepUpMm = 400,
            CapsuleRadiusMm = 300,
            CapsuleHeightMm = 1400,
            SwimEnterMm = 900,
            SwimExitMm = 700,
            MaxWadeSlowPm = 200,
            HaulOutMm = 1200,
            HaulOutTicks = 15,
            RangeMm = 2500,
            IncumbentSlackMm = 400,
            FocusConePm = 900,
            NearMm = 800,
            NearAxisMm = 150,
            WDist = 400,
            WAng = 600,
            StickyPm = 60,
            FocusDwellTicks = 4,
            MinHoldTicks = 6,
            RangeSlackMm = 500,
            RangeHistoryTicks = 9,
            PositionStaleTicks = 30,
            ClaimToleranceMm = 1500,
            SupportSpeedCapMm = 12_000,
            PushCapMm = 60,
            PushSharePm = 500,
            PushDeadMm = 10,
            GhostAfterTicks = 45,
            BuffCapPm = 400,
            MaxExtraJumps = 1,
            RespawnMinMs = 1000,
            RespawnMs = 10_000,
            TerminalMmS = 20_000,
            SlopeMaxPm = 700,
            SubStepMm = 300,
            MaxSubStepsPerTick = 4,
            SlideIterations = 3,
            ChestMm = 1000,
            MaxAnchors = 4096,
            MaxCarrierCandidates = 64,
    };

    public static TheoryData<string> BuildCriticalProperties() => new()
    {
        nameof(PlayerSettings.TerminalMmS), nameof(PlayerSettings.SubStepMm), nameof(PlayerSettings.MaxSubStepsPerTick),
        nameof(PlayerSettings.SlideIterations), nameof(PlayerSettings.MaxAnchors), nameof(PlayerSettings.MaxCarrierCandidates),
        nameof(PlayerSettings.CapsuleRadiusMm), nameof(PlayerSettings.CapsuleHeightMm), nameof(PlayerSettings.TicksAccel),
        nameof(PlayerSettings.TicksDecel), nameof(PlayerSettings.G),
    };

    [Fact]
    [Trait("Id", "PCM-P1-1")]
    public void FromBalance_BuiltInM1aContent_EqualsEveryGddDefault()
    {
        // Act
        PlayerSettings settings = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player);

        // Assert
        Assert.Equal(GddDefaults(), settings);
        Assert.Equal((5000, 7500, 2000), (settings.WalkMmPerSec, settings.SprintMmPerSec, settings.SwimMmPerSec));
        Assert.Equal((24_000, 1200, 300, 1400), (settings.G, settings.JumpHeightMm, settings.CapsuleRadiusMm, settings.CapsuleHeightMm));
    }

    [Fact]
    public void FromBalance_ListsEveryKeyTheLaterStoriesUse()
    {
        // Arrange: the key list of the story's Settings-binding acceptance criterion, as PlayerSettings property names.
        string[] required =
        {
            "WalkMmPerSec", "SprintMmPerSec", "SwimMmPerSec", "BuffCapPm", "TicksAccel", "TicksDecel", "AirControlPm", "TurnDegPerSec", "G",
            "JumpHeightMm", "ExtraJumpPm", "TerminalMmS", "CoyoteTicks", "JumpBufferTicks", "StepUpMm", "SubStepMm", "MaxSubStepsPerTick",
            "SlideIterations", "SlopeMaxPm", "CapsuleRadiusMm", "CapsuleHeightMm", "SwimEnterMm", "SwimExitMm", "MaxWadeSlowPm",
            "HaulOutMm", "HaulOutTicks", "ChestMm", "RangeMm", "IncumbentSlackMm", "FocusConePm", "NearMm", "NearAxisMm", "WDist", "WAng",
            "StickyPm", "FocusDwellTicks", "MinHoldTicks", "RangeSlackMm", "PositionStaleTicks", "ClaimToleranceMm", "MaxAnchors",
            "MaxCarrierCandidates",
        };

        // Act
        HashSet<string> have = typeof(PlayerSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(int)).Select(p => p.Name).ToHashSet();

        // Assert
        Assert.All(required, n => Assert.Contains(n, have));
    }

    [Fact]
    public void Settings_AreAnIntOnlyMirrorOfPlayerBalance_WithTheSameValues()
    {
        // Arrange
        PlayerBalance balance = BuiltInContent.CreateM1a().Player;
        PlayerSettings settings = PlayerSettings.FromBalance(balance);
        PropertyInfo[] balanceProps = typeof(PlayerBalance).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(int)).ToArray();
        PropertyInfo[] settingProps = typeof(PlayerSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType == typeof(PlayerSettings) && p.Name != "EqualityContract").ToArray();

        // Assert: no key is dropped, none is invented, every property is an int and every value is copied.
        Assert.Equal(balanceProps.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal), settingProps.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(settingProps, p => Assert.Equal(typeof(int), p.PropertyType));
        foreach (PropertyInfo bp in balanceProps)
        {
            Assert.Equal(bp.GetValue(balance), typeof(PlayerSettings).GetProperty(bp.Name)!.GetValue(settings));
        }
    }

    [Fact]
    public void EveryPlayerBalanceKey_IsABalanceRow_AndEveryRowHasAProperty()
    {
        // Arrange
        string[] rows = BalanceKeys.Player.Rows.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        string[] props = typeof(PlayerSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType == typeof(PlayerSettings) && p.Name != "EqualityContract")
            .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]).OrderBy(k => k, StringComparer.Ordinal).ToArray();

        // Assert: the camel-cased property names are exactly the content keys (WDist -> wDist is the one lower-case-first case).
        Assert.Equal(rows, props);
    }

    [Fact]
    public void FromBalance_NullBalance_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PlayerSettings.FromBalance(null!));
    }

    [Theory]
    [MemberData(nameof(BuildCriticalProperties))]
    public void FromBalance_ADivisorCapacityOrCapsuleBelowOne_IsABuildError(string property)
    {
        // Arrange: start from the built-in record and zero one value through the init-only property.
        PlayerBalance good = BuiltInContent.CreateM1a().Player;
        PlayerBalance bad = property switch
        {
            nameof(PlayerSettings.TerminalMmS) => good with { TerminalMmS = 0 },
            nameof(PlayerSettings.SubStepMm) => good with { SubStepMm = 0 },
            nameof(PlayerSettings.MaxSubStepsPerTick) => good with { MaxSubStepsPerTick = 0 },
            nameof(PlayerSettings.SlideIterations) => good with { SlideIterations = 0 },
            nameof(PlayerSettings.MaxAnchors) => good with { MaxAnchors = 0 },
            nameof(PlayerSettings.MaxCarrierCandidates) => good with { MaxCarrierCandidates = 0 },
            nameof(PlayerSettings.CapsuleRadiusMm) => good with { CapsuleRadiusMm = 0 },
            nameof(PlayerSettings.CapsuleHeightMm) => good with { CapsuleHeightMm = 0 },
            nameof(PlayerSettings.TicksAccel) => good with { TicksAccel = 0 },
            nameof(PlayerSettings.TicksDecel) => good with { TicksDecel = 0 },
            _ => good with { G = 0 },
        };

        // Act
        SimBuildException ex = Assert.Throws<SimBuildException>(() => PlayerSettings.FromBalance(bad));

        // Assert
        Assert.Equal(SimBuildErrorCode.PlayerSettingsInvalid, ex.Code);
        Assert.Contains(char.ToLowerInvariant(property[0]) + property[1..], ex.Message);
    }

    [Fact]
    public void MissingPlayerKey_IsABuildError_NotADefault()
    {
        Assert.NotEmpty(BalanceKeys.Player.Rows);
        foreach (BalanceRow playerRow in BalanceKeys.Player.Rows)
        {
            // Arrange: a registry and entry set without one player row.
            string name = "player." + playerRow.Key;
            ImmutableArray<BalanceRow> rows = BalanceKeys.CreateRegistry().Rows.Where(r => r.Owner + "." + r.Key != name).ToImmutableArray();
            BalanceRegistry registry = BalanceKeys.Compose(rows);
            ImmutableArray<BalanceEntry> entries = BalanceKeys.CreateDefaultEntries().Where(e => e.Name != name).ToImmutableArray();
            BalanceBuildResult built = BalanceTable.Build(registry, entries);
            Assert.True(built.Table is not null, $"{name}: the table did not build");

            // Act + Assert: the handles cannot be resolved, so no PlayerBalance (and no PlayerSettings) comes out of the table.
            Exception? resolve = Record.Exception(() => BalanceKeys.Player.Resolve(registry));
            Assert.True(resolve?.GetType() == typeof(ArgumentException), $"{name}: Resolve threw {resolve?.GetType().Name ?? "nothing"}, not ArgumentException");
            Exception? fromTable = Record.Exception(() => PlayerBalance.FromTable(built.Table!));
            Assert.True(fromTable?.GetType() == typeof(ArgumentException), $"{name}: FromTable threw {fromTable?.GetType().Name ?? "nothing"}, not ArgumentException");
        }
    }
}
