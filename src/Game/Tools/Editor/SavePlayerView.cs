#if TOOLS
using Godot;
using PerformativeMail.Game.Views.Player;

namespace PerformativeMail.Game.Tools.Editor;

/// <summary>
/// Editor-only tool (player-character story 022): saves the greybox <c>PlayerView.tscn</c> from an editor context so its script
/// <c>ext_resource</c> carries the editor's <c>uid="uid://…"</c> and every node its unique id (4.6+ format). Shape:
/// <c>PlayerView (Node3D, PlayerView.cs) → RigOffset (Node3D) → { Body (capsule r 0.3 m, h 1.4 m, feet at the origin), Hat (box on top),
/// Facing (a wedge toward −Z), HeldItemAnchor (Marker3D), HatAnchor (Marker3D) }</c>; no collision shapes, no physics bodies, no
/// textures. Compiles only in editor (<c>TOOLS</c>) builds. Re-running overwrites the scene and keeps its uid:
/// <code>godot --headless --editor --path . --script res://src/Game/Tools/Editor/SavePlayerView.cs</code>
/// </summary>
[Tool]
public partial class SavePlayerView : SceneTree
{
    private const string ScriptPath = "res://src/Game/Views/Player/PlayerView.cs";
    private int _frames;

    /// <inheritdoc />
    public override bool _Process(double delta)
    {
        // Let the editor finish its first filesystem scan (the script's uid known) before saving.
        if (++_frames < 30 || EditorInterface.Singleton.GetResourceFilesystem().IsScanning())
        {
            return false;
        }

        Error result = ResourceSaver.Save(BuildScene(), PlayerPresenter.ScenePath);
        GD.Print($"[player-view] save {PlayerPresenter.ScenePath}: {result}");
        return true;
    }

    private static PackedScene BuildScene()
    {
        var root = new Node3D { Name = "PlayerView" };
        root.SetScript(GD.Load<Script>(ScriptPath));
        var rig = new Node3D { Name = "RigOffset" };
        Add(root, root, rig);
        Add(root, rig, Mesh("Body", new CapsuleMesh { Radius = 0.3f, Height = 1.4f }, new Vector3(0f, 0.7f, 0f), new Color(0.20f, 0.35f, 0.75f)));
        Add(root, rig, Mesh("Hat", new BoxMesh { Size = new Vector3(0.45f, 0.12f, 0.45f) }, new Vector3(0f, 1.46f, 0f), new Color(0.90f, 0.75f, 0.15f)));
        MeshInstance3D facing = Mesh("Facing", new PrismMesh { Size = new Vector3(0.2f, 0.25f, 0.1f) }, new Vector3(0f, 1.15f, -0.32f), new Color(0.95f, 0.30f, 0.20f));
        facing.RotationDegrees = new Vector3(-90f, 0f, 0f);   // the prism's apex points along −Z (forward at yaw 0)
        Add(root, rig, facing);
        Add(root, rig, new Marker3D { Name = "HeldItemAnchor", Position = new Vector3(0f, 1.0f, -0.4f) });
        Add(root, rig, new Marker3D { Name = "HatAnchor", Position = new Vector3(0f, 1.4f, 0f) });
        var scene = new PackedScene();
        scene.Pack(root);
        root.Free();
        return scene;
    }

    private static MeshInstance3D Mesh(string name, Mesh mesh, Vector3 position, Color color) => new()
    {
        Name = name,
        Mesh = mesh,
        Position = position,
        MaterialOverride = new StandardMaterial3D { AlbedoColor = color },
    };

    private static void Add(Node owner, Node parent, Node child)
    {
        parent.AddChild(child);
        child.Owner = owner;
    }
}
#endif
