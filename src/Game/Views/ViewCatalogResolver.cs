using Godot;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.Game.Views;

/// <summary>
/// Boot step 8's resolution of one <see cref="ViewCatalog"/> against the content set (ADR-0019 section 5, TR-content-027; godot-host-shell
/// story 008): the table's def ids in <c>DefIndex</c> order go through <see cref="ViewMapping.Map{T}"/> with the catalog's arrays. The
/// catalog is only read (never mutated, never duplicated) and the content set is untouched. Nothing is instantiated here: a view is
/// produced from a resolved entry only by <c>PackedScene.Instantiate()</c> (<see cref="EntityViewHost"/>, <c>DestinationsHost</c>).
/// </summary>
/// <example><code>ResolvedViews items = ViewCatalogResolver.Resolve(ViewCatalogResolver.Load(DefTableId.Items)!, set, DefTableId.Items, !OS.IsDebugBuild());</code></example>
public static class ViewCatalogResolver
{
    /// <summary>Where the editor-saved catalogs live.</summary>
    public const string CatalogDirectory = "res://src/Game/Views/Catalogs/";

    /// <summary>The catalog file of a table, for example <c>res://src/Game/Views/Catalogs/items.tres</c>.</summary>
    /// <param name="table">The table.</param>
    /// <returns>The resource path.</returns>
    public static string CatalogPath(DefTableId table) => CatalogDirectory + ViewMapping.TableName(table) + ".tres";

    /// <summary>Loads a table's catalog (the shared, cached resource; never duplicated).</summary>
    /// <param name="table">The table.</param>
    /// <returns>The catalog, or null when the file is missing or not a <see cref="ViewCatalog"/>.</returns>
    public static ViewCatalog? Load(DefTableId table)
    {
        string path = CatalogPath(table);
        return ResourceLoader.Exists(path) ? ResourceLoader.Load(path) as ViewCatalog : null;
    }

    /// <summary>Resolves <paramref name="c"/> onto <paramref name="table"/>.</summary>
    /// <param name="c">The catalog.</param>
    /// <param name="set">The content set.</param>
    /// <param name="table">Which def table the catalog covers.</param>
    /// <param name="release">True in release exports (<c>!OS.IsDebugBuild()</c>): missing and unknown entries are errors.</param>
    /// <returns>The per-<c>DefIndex</c> scenes and the findings.</returns>
    public static ResolvedViews Resolve(ViewCatalog c, ContentSet set, DefTableId table, bool release)
    {
        ArgumentNullException.ThrowIfNull(c);
        string[] defIds = ViewMapping.DefIds(set, table);
        ViewMap<PackedScene> map = ViewMapping.Map<PackedScene>(defIds, c.DefIds, c.Scenes, c.Placeholder, release);
        return new ResolvedViews(table, defIds, map);
    }
}
