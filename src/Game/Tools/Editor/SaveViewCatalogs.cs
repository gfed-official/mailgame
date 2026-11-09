#if TOOLS
using Godot;
using PerformativeMail.Game.Views;

namespace PerformativeMail.Game.Tools.Editor;

/// <summary>
/// Editor-only tool (godot-host-shell story 008, ADR-0019 section 5): saves the placeholder view scene and the three M-1a view catalogs
/// from an editor context, so every <c>ext_resource</c> carries the <c>uid="uid://…"</c> the editor writes (spike S4-2: a runtime save
/// stores paths only). Game code never saves a catalog; this file compiles only in editor (<c>TOOLS</c>) builds, so no export carries
/// it. Idempotent: an existing scene or catalog is loaded and re-saved, keeping its entries and uid. Run twice on a fresh checkout (the
/// first run saves the scene; the editor scan of the second run knows its uid before the catalogs reference it):
/// <code>godot --headless --editor --path . --script res://src/Game/Tools/Editor/SaveViewCatalogs.cs</code>
/// </summary>
[Tool]
public partial class SaveViewCatalogs : SceneTree
{
    private const string PlaceholderPath = "res://src/Game/Views/Placeholder/PlaceholderView.tscn";
    private static readonly string[] _catalogPaths = [.. Enum.GetValues<Client.Core.Presentation.DefTableId>().Select(ViewCatalogResolver.CatalogPath)];
    private int _frames;

    /// <inheritdoc />
    public override bool _Process(double delta)
    {
        // Let the editor finish its first filesystem scan (uids of existing files known) before saving.
        if (++_frames < 30 || EditorInterface.Singleton.GetResourceFilesystem().IsScanning())
        {
            return false;
        }

        GD.Print($"[catalogs] editor hint={Engine.IsEditorHint()}");
        if (!ResourceLoader.Exists(PlaceholderPath))
        {
            Report(PlaceholderPath, ResourceSaver.Save(BuildPlaceholder(), PlaceholderPath));
            GD.Print("[catalogs] placeholder saved; run the tool again to save the catalogs against its uid");
            return true;
        }

        PackedScene placeholder = GD.Load<PackedScene>(PlaceholderPath);
        foreach (string path in _catalogPaths)
        {
            ViewCatalog catalog = ResourceLoader.Exists(path) ? GD.Load<ViewCatalog>(path) : new ViewCatalog();
            catalog.Placeholder = placeholder;
            Report(path, ResourceSaver.Save(catalog, path));
        }

        return true;
    }

    private static void Report(string path, Error result) => GD.Print($"[catalogs] save {path}: {result}");

    // A Node3D root with a 0.4 m magenta unshaded box; no collider (views never collide).
    private static PackedScene BuildPlaceholder()
    {
        var root = new Node3D { Name = "PlaceholderView" };
        var mesh = new MeshInstance3D
        {
            Name = "Box",
            Mesh = new BoxMesh { Size = new Vector3(0.4f, 0.4f, 0.4f) },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1f, 0f, 1f),
            },
        };
        root.AddChild(mesh);
        mesh.Owner = root;
        var scene = new PackedScene();
        scene.Pack(root);
        root.Free();
        return scene;
    }
}
#endif
