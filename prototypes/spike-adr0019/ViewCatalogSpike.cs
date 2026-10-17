using Godot;

namespace SpikeAdr0019;

[GlobalClass]
public partial class ViewCatalogSpike : Resource
{
    [Export] public string[] DefIds { get; set; } = System.Array.Empty<string>();
    [Export] public PackedScene[] Views { get; set; } = System.Array.Empty<PackedScene>();
}
