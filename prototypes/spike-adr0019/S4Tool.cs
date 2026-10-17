using Godot;

namespace SpikeAdr0019;

public partial class S4Tool : Node
{
    public override void _Ready()
    {
        string mode = Util.ArgValue("--s4") ?? "none";
        GD.Print("[s4] mode=" + mode);
        switch (mode)
        {
            case "makescenes": MakeScenes(); break;
            case "assignuids": AssignUids(); break;
            case "uidinfo": UidInfo(); break;
            case "makecatalog": MakeCatalog(); break;
            case "load": LoadCatalog(); break;
        }
        GetTree().Quit();
    }

    private static void MakeScenes()
    {
        foreach (string n in new[] { "a", "b" })
        {
            Node2D root = new() { Name = "View_" + n };
            Node2D child = new() { Name = "Child" };
            root.AddChild(child);
            child.Owner = root;
            PackedScene ps = new();
            Error e1 = ps.Pack(root);
            Error e2 = ResourceSaver.Save(ps, "res://views/" + n + ".tscn");
            GD.Print("[s4] pack " + e1 + " save " + n + ".tscn " + e2);
            root.Free();
        }
    }

    private static void AssignUids()
    {
        foreach (string n in new[] { "a", "b" })
        {
            string path = "res://views/" + n + ".tscn";
            long id = ResourceUid.CreateId();
            Error e = ResourceSaver.SetUid(path, id);
            GD.Print("[s4] SetUid " + path + " -> " + ResourceUid.IdToText(id) + " " + e);
        }
    }

    private static void UidInfo()
    {
        foreach (string n in new[] { "a", "b", "b_renamed" })
        {
            string path = "res://views/" + n + ".tscn";
            long id = ResourceLoader.GetResourceUid(path);
            string txt = id == ResourceUid.InvalidId ? "<invalid>" : ResourceUid.IdToText(id);
            GD.Print("[s4] uidinfo " + path + " exists=" + ResourceLoader.Exists(path) + " uid=" + txt
                + (id != ResourceUid.InvalidId && ResourceUid.HasId(id) ? " mapsTo=" + ResourceUid.GetIdPath(id) : ""));
        }
    }

    private static void MakeCatalog()
    {
        ViewCatalogSpike c = new()
        {
            DefIds = new[] { "def.a", "def.b" },
            Views = new[] { GD.Load<PackedScene>("res://views/a.tscn"), GD.Load<PackedScene>("res://views/b.tscn") },
        };
        Error e = ResourceSaver.Save(c, "res://catalog.tres");
        GD.Print("[s4] save catalog.tres " + e);
    }

    private static void LoadCatalog()
    {
        ViewCatalogSpike? c = GD.Load<ViewCatalogSpike>(Util.ArgValue("--catalog") ?? "res://catalog.tres");
        if (c == null) { GD.Print("[s4] LOAD catalog FAILED (null)"); return; }
        GD.Print("[s4] catalog loaded: DefIds=" + string.Join(",", c.DefIds) + " Views.Length=" + c.Views.Length);
        for (int i = 0; i < c.Views.Length; i++)
        {
            PackedScene? ps = c.Views[i];
            if (ps == null) { GD.Print("[s4] view[" + i + "] NULL"); continue; }
            Node inst = ps.Instantiate();
            GD.Print("[s4] view[" + i + "] path=" + ps.ResourcePath + " instance=" + inst.Name + " children=" + inst.GetChildCount());
            inst.Free();
        }
    }
}
