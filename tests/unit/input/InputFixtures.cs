using System.Collections.Immutable;
using PerformativeMail.Client.Core.Input;
using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>Shared fixtures for the story 006 validator tests: the built-in definitions with one row edited, and named state ids.</summary>
internal static class InputFixtures
{
    public static readonly ushort WorldId = RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.None, BodyLayer.OnFoot, false, false, SubmenuLayer.None);

    public static readonly ushort WorldAimingId = RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.None, BodyLayer.OnFoot, false, true, SubmenuLayer.None);

    public static readonly ushort HoldingClosedId = RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.None, BodyLayer.OnFoot, true, false, SubmenuLayer.None);

    public static readonly ushort PanelId = RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.OnFoot, false, false, SubmenuLayer.None);

    public static readonly ushort HoldingOpenId = RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.OnFoot, true, false, SubmenuLayer.None);

    public static readonly ushort TextEntryWorldId = RouterStateInfo.Pack(CaptureLayer.TextEntry, OverlayLayer.None, BodyLayer.OnFoot, false, false, SubmenuLayer.None);

    /// <summary>A Chat-like Tap action for fixtures (not an M-1a action): live in Panel and Holding-open.</summary>
    public static ActionDef ChatLike(InputBinding kbm, string id = "chat_like") => new()
    {
        Id = 30,
        StringId = id,
        Grammar = Grammar.Tap,
        Delivery = Delivery.Immediate,
        LiveIn = [LivePredicate.Any with { CaptureMask = CaptureMask.None, OverlayMask = OverlayMask.Panel }],
        KbmPrimary = kbm,
        KbmSecondary = InputBinding.Unbound,
        PadPrimary = InputBinding.Unbound,
        PadSecondary = InputBinding.Unbound,
        OwnerGdd = "design/gdd/input-controls.md",
        Reserved = [],
        Aliases = [],
    };

    /// <summary>The built-in definitions with the row of <paramref name="id"/> replaced by <paramref name="edit"/>, plus <paramref name="extra"/>.</summary>
    public static ImmutableArray<ActionDef> Edited(ushort id, Func<ActionDef, ActionDef> edit, params ActionDef[] extra)
    {
        ImmutableArray<ActionDef> defs = BuiltInActionMap.Definitions();
        var b = ImmutableArray.CreateBuilder<ActionDef>(defs.Length + extra.Length);
        foreach (ActionDef d in defs)
        {
            b.Add(d.Id == id ? edit(d) : d);
        }

        b.AddRange(extra);
        return b.ToImmutable();
    }

    /// <summary>
    /// Loads <paramref name="defs"/> through the full <see cref="ActionMap.TryLoad(IReadOnlyList{ActionDef}, out ActionMap, out ValidationReport)"/>
    /// and returns its report; asserts that the field checks passed, so the report is the reachable-state pass's.
    /// </summary>
    public static ValidationReport Validate(IReadOnlyList<ActionDef> defs, PadPathTable? padPaths = null)
    {
        bool loaded = ActionMap.TryLoad(defs, ReachableSet.Default, padPaths ?? PadPathTable.M1a, out ActionMap? map, out ValidationReport report);
        Assert.DoesNotContain(report.Errors, e => e.Code == ValidationCodes.Field);
        Assert.Equal(report.IsValid, loaded);
        Assert.Equal(loaded, map is not null);
        return report;
    }

    /// <summary>The errors with <paramref name="code"/>.</summary>
    public static List<ValidationEntry> ErrorsWith(this ValidationReport report, string code) =>
        report.Errors.Where(e => e.Code == code).ToList();
}
