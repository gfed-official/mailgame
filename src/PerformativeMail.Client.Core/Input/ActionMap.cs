using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The validated, immutable set of <see cref="ActionDef"/>s (GDD R1). Lookup by <see cref="ActionId"/> is an array index;
/// <see cref="Resolve"/> maps a string id or any alias to its def through a dictionary that is used for that lookup only.
/// All ordered iteration goes through <see cref="All"/> (ascending id), never through the dictionary. Every instance is fully
/// validated: <see cref="TryLoad(IReadOnlyList{ActionDef}, out ActionMap, out ValidationReport)"/> runs the field checks and
/// then the reachable-state pass (conflicts, binding shapes, Back-ladder steps, pad paths) and returns no map on any error, so
/// no <see cref="ActionMap"/> can hold a conflict. Immutable; thread-safe.
/// </summary>
/// <example>
/// <code>
/// if (ActionMap.TryLoad(defs, out ActionMap? map, out ValidationReport report))
/// {
///     ActionDef? jump = map.Get(ActionId.Jump);          // array index
///     ActionDef? same = map.Resolve("jump");             // by string id or alias
/// }
/// </code>
/// </example>
public sealed class ActionMap
{
    private readonly ActionDef?[] _byId;
    private readonly Dictionary<string, ActionDef> _byName;

    private ActionMap(ActionDef?[] byId, Dictionary<string, ActionDef> byName, ImmutableArray<ActionDef> all)
    {
        _byId = byId;
        _byName = byName;
        All = all;
        Chords = ChordResolver.Build(all);
    }

    /// <summary>
    /// The per-input binding index and most-specific-chord resolution for this map (ADR-0012 section 2 and 3), built once
    /// here. The conflict validator and the runtime router use this same instance.
    /// </summary>
    public ChordResolver Chords { get; }

    /// <summary>Every action, ascending by <see cref="ActionDef.Id"/>.</summary>
    public ImmutableArray<ActionDef> All { get; }

    /// <summary>The number of actions.</summary>
    public int Count => All.Length;

    /// <summary>
    /// Fully validates <paramref name="defs"/> against <see cref="ReachableSet.Default"/> and <see cref="PadPathTable.M1a"/>
    /// and, when the report has no errors, returns the map. Allocates; load time only.
    /// </summary>
    /// <param name="defs">The definitions, in any order.</param>
    /// <param name="map">The map when valid; null otherwise.</param>
    /// <param name="report">The validation report (errors, warnings, infos).</param>
    /// <returns>True when the map loaded.</returns>
    /// <example><code>bool ok = ActionMap.TryLoad(defs, out ActionMap? map, out ValidationReport report);</code></example>
    public static bool TryLoad(IReadOnlyList<ActionDef?> defs, [NotNullWhen(true)] out ActionMap? map, out ValidationReport report) =>
        TryLoad(defs, ReachableSet.Default, PadPathTable.M1a, out map, out report);

    /// <summary>
    /// Fully validates <paramref name="defs"/>: the field checks, then (if they pass) the reachable-state pass over
    /// <paramref name="reachable"/> with <paramref name="padPaths"/>. Returns the map only when the report has no errors.
    /// </summary>
    /// <param name="defs">The definitions, in any order.</param>
    /// <param name="reachable">The reachable router states.</param>
    /// <param name="padPaths">The pad-path and exemption data.</param>
    /// <param name="map">The map when valid; null otherwise.</param>
    /// <param name="report">The validation report.</param>
    /// <returns>True when the map loaded.</returns>
    /// <example><code>bool ok = ActionMap.TryLoad(defs, ReachableSet.Default, PadPathTable.M1a, out ActionMap? map, out var report);</code></example>
    public static bool TryLoad(IReadOnlyList<ActionDef?> defs, ReachableSet reachable, PadPathTable padPaths, [NotNullWhen(true)] out ActionMap? map, out ValidationReport report)
    {
        report = ActionMapValidator.Validate(defs);
        if (!report.IsValid)
        {
            map = null;
            return false;
        }

        ActionMap candidate = Build(defs);
        ActionMapValidator.AppendReachableChecks(report, candidate, reachable, padPaths);
        map = report.IsValid ? candidate : null;
        return map is not null;
    }

    // Builds the map from defs that passed the field checks; not yet checked across states.
    private static ActionMap Build(IReadOnlyList<ActionDef?> defs)
    {

        int maxId = 0;
        foreach (ActionDef? d in defs)
        {
            maxId = Math.Max(maxId, d!.Id);
        }

        // Placing each def at its id index orders them; no sort, no dictionary iteration.
        var byId = new ActionDef?[maxId + 1];
        foreach (ActionDef? d in defs)
        {
            byId[d!.Id] = d;
        }

        var all = ImmutableArray.CreateBuilder<ActionDef>(defs.Count);
        var byName = new Dictionary<string, ActionDef>(StringComparer.Ordinal);
        foreach (ActionDef? d in byId)
        {
            if (d is null)
            {
                continue;
            }

            all.Add(d);
            byName[d.StringId] = d;
            if (!d.Aliases.IsDefaultOrEmpty)
            {
                foreach (string alias in d.Aliases)
                {
                    byName[alias] = d;
                }
            }
        }

        return new ActionMap(byId, byName, all.MoveToImmutable());
    }

    /// <summary>The def with this numeric id: one array index, 0 B.</summary>
    /// <param name="id">The <see cref="ActionId"/>.</param>
    /// <returns>The def, or null when no action has that id.</returns>
    /// <example><code>ActionDef? move = map.Get(ActionId.Move);</code></example>
    public ActionDef? Get(ushort id) => id < _byId.Length ? _byId[id] : null;

    /// <summary>The def whose string id or alias is <paramref name="idOrAlias"/> (ordinal, case-sensitive).</summary>
    /// <param name="idOrAlias">A string id or an alias.</param>
    /// <returns>The def, or null when unknown.</returns>
    /// <example><code>ActionDef? d = map.Resolve("old_name_of_jump");</code></example>
    public ActionDef? Resolve(string? idOrAlias) =>
        idOrAlias is not null && _byName.TryGetValue(idOrAlias, out ActionDef? def) ? def : null;
}
