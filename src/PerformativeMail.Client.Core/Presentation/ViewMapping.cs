using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Defs;

namespace PerformativeMail.Client.Core.Presentation;

/// <summary>The def tables that have a view catalog at M-1a (ADR-0019 section 5; godot-host-shell story 008).</summary>
public enum DefTableId : byte
{
    /// <summary><see cref="ContentSet.Items"/>, catalog <c>items.tres</c>.</summary>
    Items = 0,

    /// <summary><see cref="ContentSet.MailKinds"/>, catalog <c>mail_kinds.tres</c>.</summary>
    MailKinds = 1,

    /// <summary><see cref="ContentSet.DestinationTypes"/>, catalog <c>destination_types.tres</c>.</summary>
    DestinationTypes = 2,
}

/// <summary>What a view-catalog resolution found wrong with one entry or with the catalog.</summary>
public enum ViewFindingKind : byte
{
    /// <summary>A def has no catalog entry (or a null scene); it gets the placeholder.</summary>
    MissingView = 0,

    /// <summary>A catalog id names no def; the entry is ignored.</summary>
    UnknownDef = 1,

    /// <summary><c>DefIds</c> and <c>Scenes</c> differ in length; nothing is mapped. An error in every build.</summary>
    LengthMismatch = 2,

    /// <summary>A catalog id appears twice; the first entry is kept. An error in every build.</summary>
    DuplicateId = 3,

    /// <summary>The catalog has no placeholder. An error in every build.</summary>
    NoPlaceholder = 4,
}

/// <summary>One finding of <see cref="ViewMapping.Map{T}"/>.</summary>
/// <param name="Kind">What was found.</param>
/// <param name="IsError">True when it fails boot (content error); false for a debug warning.</param>
/// <param name="Id">The def or catalog id concerned; for <see cref="ViewFindingKind.LengthMismatch"/> <c>ids/scenes</c> counts; empty for <see cref="ViewFindingKind.NoPlaceholder"/>.</param>
public readonly record struct ViewFinding(ViewFindingKind Kind, bool IsError, string Id);

/// <summary>The result of <see cref="ViewMapping.Map{T}"/>: one view per <c>DefIndex</c> and the findings, in catalog then def order.</summary>
/// <typeparam name="T">The view type (<c>PackedScene</c> in the game).</typeparam>
/// <param name="ByDefIndex">Element <c>i</c> is the view of the def with ordinal <c>i</c>; exactly the def count long.</param>
/// <param name="Findings">Every finding.</param>
/// <param name="Mapped">How many defs got a catalog entry (the rest hold the placeholder).</param>
public sealed record ViewMap<T>(IReadOnlyList<T?> ByDefIndex, IReadOnlyList<ViewFinding> Findings, int Mapped)
    where T : class
{
    /// <summary>Findings that fail boot.</summary>
    public int Errors => Findings.Count(f => f.IsError);

    /// <summary>Findings that only warn.</summary>
    public int Warnings => Findings.Count(f => !f.IsError);
}

/// <summary>
/// The pure half of boot step 8 (CD28, ADR-0019 section 5, TR-content-027; godot-host-shell story 008): maps a view catalog's parallel
/// id/view arrays onto a def table's <c>DefIndex</c> order. A def with no entry gets the placeholder and a <see cref="ViewFindingKind.MissingView"/>
/// warning, an error with <c>release</c>; a catalog id with no def is ignored with an <see cref="ViewFindingKind.UnknownDef"/> warning (an error
/// with <c>release</c>, as ADR-0019 section 5 reads "missing or unknown entries"); a length mismatch, a duplicate id or a missing placeholder is an
/// error in every build. Reads the content set only; its hash is unaffected (CDD-SCHEMA-7). Godot-free.
/// </summary>
/// <example>
/// <code>
/// ViewMap&lt;PackedScene&gt; map = ViewMapping.Map(ViewMapping.DefIds(set, DefTableId.Items), catalog.DefIds, catalog.Scenes, catalog.Placeholder, release: false);
/// </code>
/// </example>
public static class ViewMapping
{
    /// <summary>The ids of <paramref name="table"/> in <c>DefIndex</c> order (ordinal-sorted, content-data 001).</summary>
    /// <param name="set">The content set.</param>
    /// <param name="table">The table.</param>
    /// <returns>A fresh array of the def ids.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="table"/> is not a <see cref="DefTableId"/>.</exception>
    public static string[] DefIds(ContentSet set, DefTableId table)
    {
        ArgumentNullException.ThrowIfNull(set);
        return table switch
        {
            DefTableId.Items => Ids(set.Items),
            DefTableId.MailKinds => Ids(set.MailKinds),
            DefTableId.DestinationTypes => Ids(set.DestinationTypes),
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Unknown def table."),
        };
    }

    /// <summary>The table's name in logs and catalog file names (<c>items</c>, <c>mail_kinds</c>, <c>destination_types</c>).</summary>
    /// <param name="table">The table.</param>
    /// <returns>The name.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="table"/> is not a <see cref="DefTableId"/>.</exception>
    public static string TableName(DefTableId table) => table switch
    {
        DefTableId.Items => "items",
        DefTableId.MailKinds => "mail_kinds",
        DefTableId.DestinationTypes => "destination_types",
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Unknown def table."),
    };

    /// <summary>The <c>VIEW</c> log line of one finding, for example <c>VIEW warn missing view for items:axe -&gt; placeholder</c>.</summary>
    /// <param name="table">The table the finding belongs to.</param>
    /// <param name="finding">The finding.</param>
    /// <returns>One line.</returns>
    public static string Describe(DefTableId table, in ViewFinding finding)
    {
        string level = finding.IsError ? "error" : "warn";
        string where = $"{TableName(table)}:{finding.Id}";
        return finding.Kind switch
        {
            ViewFindingKind.MissingView => $"VIEW {level} missing view for {where} -> placeholder",
            ViewFindingKind.UnknownDef => $"VIEW {level} catalog entry {where} names no def -> ignored",
            ViewFindingKind.LengthMismatch => $"VIEW {level} {TableName(table)} catalog DefIds/Scenes length mismatch {finding.Id} -> nothing mapped",
            ViewFindingKind.DuplicateId => $"VIEW {level} duplicate catalog id {where} -> first entry kept",
            _ => $"VIEW {level} {TableName(table)} catalog has no placeholder",
        };
    }

    /// <summary>Maps a catalog onto the def order.</summary>
    /// <typeparam name="T">The view type.</typeparam>
    /// <param name="defIdsInDefIndexOrder">The table's ids, element <c>i</c> being the def with ordinal <c>i</c> (<see cref="DefIds"/>).</param>
    /// <param name="catalogIds">The catalog's <c>DefIds</c>.</param>
    /// <param name="catalogViews">The catalog's views, parallel to <paramref name="catalogIds"/>.</param>
    /// <param name="placeholder">The view of a def with no entry.</param>
    /// <param name="release">True in release exports: missing and unknown entries become errors.</param>
    /// <returns>The per-<c>DefIndex</c> views and the findings.</returns>
    public static ViewMap<T> Map<T>(
        IReadOnlyList<string> defIdsInDefIndexOrder,
        IReadOnlyList<string?> catalogIds,
        IReadOnlyList<T?> catalogViews,
        T? placeholder,
        bool release)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(defIdsInDefIndexOrder);
        ArgumentNullException.ThrowIfNull(catalogIds);
        ArgumentNullException.ThrowIfNull(catalogViews);
        var findings = new List<ViewFinding>();
        var result = new T?[defIdsInDefIndexOrder.Count];
        if (placeholder is null)
        {
            findings.Add(new ViewFinding(ViewFindingKind.NoPlaceholder, true, ""));
        }

        if (catalogIds.Count != catalogViews.Count)
        {
            findings.Add(new ViewFinding(ViewFindingKind.LengthMismatch, true, $"{catalogIds.Count}/{catalogViews.Count}"));
            Array.Fill(result, placeholder);
            return new ViewMap<T>(result, findings, 0);
        }

        var ordinalById = new Dictionary<string, int>(defIdsInDefIndexOrder.Count, StringComparer.Ordinal);
        for (int d = 0; d < defIdsInDefIndexOrder.Count; d++)
        {
            ordinalById[defIdsInDefIndexOrder[d]] = d;
        }

        var seen = new bool[result.Length];
        int mapped = 0;
        for (int i = 0; i < catalogIds.Count; i++)
        {
            string id = catalogIds[i] ?? "";
            if (!ordinalById.TryGetValue(id, out int d))
            {
                findings.Add(new ViewFinding(ViewFindingKind.UnknownDef, release, id));
            }
            else if (seen[d])
            {
                findings.Add(new ViewFinding(ViewFindingKind.DuplicateId, true, id));
            }
            else
            {
                seen[d] = true;
                if (catalogViews[i] is { } view)
                {
                    result[d] = view;
                    mapped++;
                }
            }
        }

        for (int d = 0; d < result.Length; d++)
        {
            if (result[d] is null)
            {
                result[d] = placeholder;
                findings.Add(new ViewFinding(ViewFindingKind.MissingView, release, defIdsInDefIndexOrder[d]));
            }
        }

        return new ViewMap<T>(result, findings, mapped);
    }

    private static string[] Ids<TDef>(DefTable<TDef> table)
        where TDef : IDef
    {
        ReadOnlySpan<TDef> defs = table.AsSpan();
        var ids = new string[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            ids[i] = defs[i].Id;
        }

        return ids;
    }
}
