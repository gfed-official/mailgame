#if TOOLS
using Godot;

namespace SpikeAdr0019;

[Tool]
public partial class SpikePlug : EditorPlugin
{
    public override void _EnterTree()
    {
        string? mode = System.Environment.GetEnvironmentVariable("SPIKE_EDITOR_MODE");
        GD.Print("[plug] EnterTree IsEditorHint=" + Engine.IsEditorHint() + " mode=" + (mode ?? "<none>"));
        if (mode == "makecatalog")
        {
            ViewCatalogSpike c = new()
            {
                DefIds = new[] { "def.a", "def.b" },
                Views = new[] { GD.Load<PackedScene>("res://views/a.tscn"), GD.Load<PackedScene>("res://views/b.tscn") },
            };
            Error e = ResourceSaver.Save(c, "res://catalog_editor.tres");
            GD.Print("[plug] saved catalog_editor.tres " + e);
        }
    }
}
#endif
