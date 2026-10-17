using System.Collections.Immutable;

namespace PerformativeMail.Client.Core.Input;

/// <summary>How an action without a pad binding is reached on the pad (H-FX-3a).</summary>
/// <example><code>var path = new PadPathEntry("hotbar_slot_1", PadPathKind.ViaAction, "hotbar_step");</code></example>
public enum PadPathKind : byte
{
    /// <summary>Not set. Never valid.</summary>
    None = 0,

    /// <summary>Through another action's pad binding (the hotbar digits through Hotbar step).</summary>
    ViaAction = 1,

    /// <summary>Through the action menu (Y-menu verbs).</summary>
    ActionMenu = 2,
}

/// <summary>One declared pad path: the action (string id), how it is reached and, for <see cref="PadPathKind.ViaAction"/>, through which action.</summary>
/// <param name="ActionId">The string id of the action that has no pad binding.</param>
/// <param name="Kind">How it is reached on the pad.</param>
/// <param name="ViaActionId">The string id of the action whose pad binding reaches it (ViaAction only), else null.</param>
/// <example><code>var path = new PadPathEntry("hotbar_slot_3", PadPathKind.ViaAction, "hotbar_step");</code></example>
public readonly record struct PadPathEntry(string ActionId, PadPathKind Kind, string? ViaActionId = null);

/// <summary>
/// Pad coverage as data (GDD H-FX-3a): every registered action has a pad binding, or a declared <see cref="PadPathEntry"/>,
/// or is on the explicit <see cref="Exemptions"/> list (Chat, no pad text entry in v1, OQ-IC-5; Push-to-talk; Read prompt).
/// An exemption matches by exact string id only, so an exempt-but-unregistered id never hides a missing pad binding on a
/// registered action. Run by <see cref="ActionMapValidator.Validate(ActionMap, ReachableSet, PadPathTable)"/>. Immutable;
/// thread-safe.
/// </summary>
/// <example>
/// <code>
/// PadPathTable table = PadPathTable.M1a;
/// bool exempt = table.IsExempt("chat");                                           // true
/// bool hasPath = table.TryGetPath("hotbar_slot_1", out PadPathEntry path);       // via hotbar_step
/// </code>
/// </example>
public sealed class PadPathTable
{
    /// <summary>The v1 exemption list: exactly Chat, Push-to-talk and Read prompt (none registered at M-1a).</summary>
    public static readonly ImmutableArray<string> V1Exemptions = ["chat", "push_to_talk", "read_prompt"];

    /// <summary>The M-1a table: the eight hotbar digits reach the pad through Hotbar step (D-pad left / right); the v1 exemptions.</summary>
    public static readonly PadPathTable M1a = new(
        [
            new("hotbar_slot_1", PadPathKind.ViaAction, "hotbar_step"),
            new("hotbar_slot_2", PadPathKind.ViaAction, "hotbar_step"),
            new("hotbar_slot_3", PadPathKind.ViaAction, "hotbar_step"),
            new("hotbar_slot_4", PadPathKind.ViaAction, "hotbar_step"),
            new("hotbar_slot_5", PadPathKind.ViaAction, "hotbar_step"),
            new("hotbar_slot_6", PadPathKind.ViaAction, "hotbar_step"),
            new("hotbar_slot_7", PadPathKind.ViaAction, "hotbar_step"),
            new("hotbar_slot_8", PadPathKind.ViaAction, "hotbar_step"),
        ],
        V1Exemptions);

    /// <summary>Creates a table.</summary>
    /// <param name="paths">The declared pad paths.</param>
    /// <param name="exemptions">The string ids exempt from pad coverage.</param>
    /// <example><code>var table = new PadPathTable([], PadPathTable.V1Exemptions);</code></example>
    public PadPathTable(ImmutableArray<PadPathEntry> paths, ImmutableArray<string> exemptions)
    {
        Paths = paths.IsDefault ? [] : paths;
        Exemptions = exemptions.IsDefault ? [] : exemptions;
    }

    /// <summary>The declared pad paths.</summary>
    public ImmutableArray<PadPathEntry> Paths { get; }

    /// <summary>The exempt string ids.</summary>
    public ImmutableArray<string> Exemptions { get; }

    /// <summary>True when <paramref name="actionId"/> is on the exemption list (ordinal).</summary>
    /// <param name="actionId">A string id.</param>
    /// <returns>Whether it is exempt.</returns>
    /// <example><code>bool exempt = PadPathTable.M1a.IsExempt("push_to_talk");</code></example>
    public bool IsExempt(string actionId)
    {
        foreach (string e in Exemptions)
        {
            if (string.Equals(e, actionId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The declared pad path of <paramref name="actionId"/>.</summary>
    /// <param name="actionId">A string id.</param>
    /// <param name="path">The path when declared.</param>
    /// <returns>Whether a path is declared.</returns>
    /// <example><code>bool has = PadPathTable.M1a.TryGetPath("hotbar_slot_2", out PadPathEntry p);</code></example>
    public bool TryGetPath(string actionId, out PadPathEntry path)
    {
        foreach (PadPathEntry p in Paths)
        {
            if (string.Equals(p.ActionId, actionId, StringComparison.Ordinal))
            {
                path = p;
                return true;
            }
        }

        path = default;
        return false;
    }

    /// <summary>True when the action has a bound gamepad slot.</summary>
    /// <param name="def">The action.</param>
    /// <returns>Whether either pad slot is bound.</returns>
    /// <example><code>bool pad = PadPathTable.HasPadBinding(map.Get(ActionId.Jump)!);</code></example>
    public static bool HasPadBinding(ActionDef def) =>
        IsBound(def.PadPrimary) || IsBound(def.PadSecondary);

    /// <summary>Adds a <see cref="ValidationCodes.PadPath"/> error for every registered action that has no pad binding, no valid declared path and no exemption.</summary>
    /// <param name="map">The map.</param>
    /// <param name="report">The report to add to.</param>
    /// <example><code>PadPathTable.M1a.Check(map, report);</code></example>
    public void Check(ActionMap map, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(report);
        foreach (ActionDef d in map.All)
        {
            if (HasPadBinding(d) || IsExempt(d.StringId))
            {
                continue;
            }

            string? why = null;
            if (!TryGetPath(d.StringId, out PadPathEntry path))
            {
                why = "no gamepad binding, no declared pad path and not exempt";
            }
            else if (path.Kind == PadPathKind.ViaAction)
            {
                ActionDef? via = path.ViaActionId is null ? null : map.Resolve(path.ViaActionId);
                if (via is null || !HasPadBinding(via))
                {
                    why = $"the declared pad path goes through '{path.ViaActionId}', which is not registered or has no gamepad binding";
                }
            }
            else if (path.Kind != PadPathKind.ActionMenu)
            {
                why = "the declared pad path has no kind";
            }

            if (why is not null)
            {
                report.Add(new ValidationEntry(d.StringId, ValidationFields.PadPrimary, why) { Code = ValidationCodes.PadPath });
            }
        }
    }

    private static bool IsBound(InputBinding b) => !b.IsMissing && !b.IsUnbound;
}
