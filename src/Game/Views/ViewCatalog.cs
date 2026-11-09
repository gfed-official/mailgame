using Godot;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Game.Views;

/// <summary>
/// The presentation lookup of one def table (ADR-0019 section 5, CD28, TR-content-027; godot-host-shell story 008): def id strings
/// parallel to the scenes that show them, plus the placeholder for a def with no entry. One editor-saved, uid-bearing <c>.tres</c> per
/// table under <c>res://src/Game/Views/Catalogs/</c>; boot step 8 resolves it to a <see cref="DefIndex"/>-ordered array with
/// <see cref="ViewCatalogResolver.Resolve"/>. Holds scenes and id strings only, never Sim content. Read-only at runtime: never
/// <c>Duplicate</c>d, never saved from game code.
/// </summary>
/// <example>
/// <code>
/// // Adding art (in the editor): append the def id to DefIds and the scene to Scenes at the same index, save, commit the .tres
/// // and the scene's .uid.
/// ResolvedViews items = ViewCatalogResolver.Resolve(catalog, content, DefTableId.Items, release: !OS.IsDebugBuild());
/// </code>
/// </example>
[GlobalClass]
public partial class ViewCatalog : Resource
{
    /// <summary>Content def id strings, parallel to <see cref="Scenes"/>.</summary>
    [Export]
    public string[] DefIds { get; set; } = [];

    /// <summary>The view scene of each <see cref="DefIds"/> entry.</summary>
    [Export]
    public PackedScene[] Scenes { get; set; } = [];

    /// <summary>The scene shown for a def with no entry (<c>res://src/Game/Views/Placeholder/PlaceholderView.tscn</c>).</summary>
    [Export]
    public PackedScene Placeholder { get; set; } = null!;
}
