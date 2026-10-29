using System.Collections.Immutable;
using System.Globalization;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// The stage 3 id checks that run before F4 assigns <c>DefIndex</c>es.
/// <list type="bullet">
/// <item>CV-GEN-01: an id twice in one def type is one Error naming every location (<c>#N</c> is the def's position in the
/// input list). Every def with that id is dropped; the type's other defs are kept. The type is also reported as a failed
/// input (its table key), so the rules that read that table are skipped and counted instead of reporting secondary
/// findings on shifted indices.</item>
/// <item>CV-GEN-09: more than 65 534 defs in one type is one Error. The count is the raw input count, duplicates included.
/// The type's table is left empty and the type is reported as a failed input.</item>
/// </list>
/// Empty ids are NOT handled here: CV-GEN-02 is a loader schema rule (story 018) and <see cref="ContentPipeline.Build"/>
/// still rejects them, except in an over-limit type, where the assigner decides on the count alone and never looks at
/// the ids. Stateless; load-time only (allocates).
/// </summary>
/// <example>
/// <code>
/// DefTable&lt;ItemDef&gt; t = ContentIdChecks.Prepare(DefType.Item, "items", ContentInputKeys.Items, items, sev, sev, found, failed);
/// </code>
/// </example>
internal static class ContentIdChecks
{
    /// <summary>Checks one def type and places the surviving defs by ordinal <c>DefIndex</c>.</summary>
    /// <param name="type">The def type.</param>
    /// <param name="folder">The def folder (for locations).</param>
    /// <param name="inputKey">The type's input key (<see cref="ContentInputKeys"/>).</param>
    /// <param name="defs">The defs, any order.</param>
    /// <param name="duplicateSeverity">The CV-GEN-01 row's severity.</param>
    /// <param name="tooManySeverity">The CV-GEN-09 row's severity.</param>
    /// <param name="found">Receives the findings.</param>
    /// <param name="failed">Receives the type's key when the type has duplicates or too many defs.</param>
    /// <param name="locations">
    /// Loaded defs' real locations (TD-178 item 1), used for the CV-GEN-01 and CV-GEN-09 findings in place of
    /// <c>builtin/&lt;folder&gt;</c>; null for built-in content.
    /// </param>
    /// <exception cref="ArgumentException">A surviving id is empty in a type within the limit (CV-GEN-02 is the loader's).</exception>
    internal static DefTable<T> Prepare<T>(
        DefType type,
        string folder,
        string inputKey,
        ImmutableArray<T> defs,
        RuleSeverity duplicateSeverity,
        RuleSeverity tooManySeverity,
        List<ContentDiagnostic> found,
        List<FailedInput> failed,
        IReadOnlyDictionary<DefRef, SourceLocation>? locations = null)
        where T : IDef
    {
        ImmutableArray<T> list = defs.IsDefault ? ImmutableArray<T>.Empty : defs;
        string path = "builtin/" + folder;
        ImmutableArray<T> kept = DropDuplicates(type, folder, list, duplicateSeverity, found, locations, out bool hadDuplicates);
        bool tooMany = list.Length > DefIdAssigner.MaxDefsPerType;
        if (hadDuplicates || tooMany)
        {
            failed.Add(new FailedInput(inputKey, DefRef.None, path));
        }

        if (!tooMany)
        {
            return ContentPipeline.Sorted(kept);
        }

        SourceLocation where = SourceLocation.BuiltIn(path);
        if (locations is not null && list.Length > 0 && locations.TryGetValue(new DefRef(type, list[0].Id), out SourceLocation first))
        {
            where = first;
        }

        found.Add(TooManyFinding(list, where, tooManySeverity));
        return new DefTable<T>(ImmutableArray<T>.Empty);
    }

    /// <summary>
    /// The CV-GEN-09 finding. The assigner's count-only verdict (story 001) is the single source of the limit; it returns
    /// TooManyDefs before it sorts or inspects an id.
    /// </summary>
    private static ContentDiagnostic TooManyFinding<T>(ImmutableArray<T> list, SourceLocation where, RuleSeverity severity)
        where T : IDef
    {
        string[] ids = new string[list.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            ids[i] = list[i].Id;
        }

        DefAssignment verdict = DefIdAssigner.Assign(ids);
        return new ContentDiagnostic(
            GeneralRuleIds.TooManyDefs,
            severity,
            where,
            DefRef.None,
            verdict.Count.ToString(CultureInfo.InvariantCulture) + " defs",
            "at most " + DefIdAssigner.MaxDefsPerType.ToString(CultureInfo.InvariantCulture) + " defs per type");
    }

    private static ImmutableArray<T> DropDuplicates<T>(
        DefType type,
        string folder,
        ImmutableArray<T> list,
        RuleSeverity severity,
        List<ContentDiagnostic> found,
        IReadOnlyDictionary<DefRef, SourceLocation>? locations,
        out bool hadDuplicates)
        where T : IDef
    {
        Dictionary<string, List<int>> dupes = FindDuplicates(list, out List<string> order);
        hadDuplicates = order.Count > 0;
        if (!hadDuplicates)
        {
            return list;
        }

        foreach (string id in order)
        {
            ContentDiagnostic finding = DuplicateFinding(type, folder, id, dupes[id], severity);
            found.Add(locations is not null && locations.TryGetValue(finding.Def, out SourceLocation real) ? finding with { Location = real } : finding);
        }

        ImmutableArray<T>.Builder survivors = ImmutableArray.CreateBuilder<T>(list.Length);
        foreach (T def in list)
        {
            if (string.IsNullOrEmpty(def.Id) || !dupes.ContainsKey(def.Id))
            {
                survivors.Add(def);
            }
        }

        return survivors.ToImmutable();
    }

    /// <summary>Maps each repeated id to its input positions; <paramref name="order"/> lists those ids by the position of their second occurrence.</summary>
    private static Dictionary<string, List<int>> FindDuplicates<T>(ImmutableArray<T> list, out List<string> order)
        where T : IDef
    {
        var first = new Dictionary<string, int>(StringComparer.Ordinal);
        var dupes = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        order = new List<string>();
        for (int i = 0; i < list.Length; i++)
        {
            string id = list[i].Id;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            if (!first.TryGetValue(id, out int at))
            {
                first[id] = i;
            }
            else if (dupes.TryGetValue(id, out List<int>? positions))
            {
                positions.Add(i);
            }
            else
            {
                dupes[id] = new List<int> { at, i };
                order.Add(id);
            }
        }

        return dupes;
    }

    private static ContentDiagnostic DuplicateFinding(DefType type, string folder, string id, List<int> positions, RuleSeverity severity)
    {
        string basePath = ContentValidationInput.LocationOf(folder, id).Path;
        var where = new string[positions.Count];
        for (int k = 0; k < where.Length; k++)
        {
            where[k] = basePath + "#" + positions[k].ToString(CultureInfo.InvariantCulture);
        }

        return new ContentDiagnostic(
            GeneralRuleIds.IdsAndReferences,
            severity,
            SourceLocation.BuiltIn(where[0]),
            new DefRef(type, id),
            "id '" + id + "' defined at " + string.Join(" and ", where),
            "an id unique within its def type (neither def is kept)");
    }
}
