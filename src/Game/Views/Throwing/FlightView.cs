using Godot;
using PerformativeMail.Client.Core.Ui.Throwing;
using PerformativeMail.Game.Presentation;

namespace PerformativeMail.Game.Views.Throwing;

/// <summary>
/// In-flight parcels (throwing-catching 020, greybox): one <see cref="MultiMeshInstance3D"/> with <see cref="FlightPlacements.Cap"/>
/// instances allocated once; each frame writes the placements and the visible count. No tags, shadow or trail.
/// </summary>
public partial class FlightView : Node3D, IPresentedView
{
    private readonly IFlightSource _source;
    private readonly FlightPlacement[] _slots = new FlightPlacement[FlightPlacements.Cap];
    private readonly MultiMesh _multi;

    /// <summary>Creates the view and preallocates the MultiMesh.</summary>
    /// <param name="source">The flight mirror port.</param>
    public FlightView(IFlightSource source)
    {
        _source = source;
        Name = "Flights";
        var material = new StandardMaterial3D { AlbedoColor = new Color(0.72f, 0.55f, 0.33f) };
        _multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.2f, 0.25f), Material = material },
            InstanceCount = FlightPlacements.Cap,
            VisibleInstanceCount = 0,
        };
        AddChild(new MultiMeshInstance3D
        {
            Name = "ParcelTier1",
            Multimesh = _multi,
            CustomAabb = new Aabb(new Vector3(-1000, -100, -1000), new Vector3(2000, 400, 2000)),
        });
    }

    /// <inheritdoc />
    public void Present(int alphaPermille)
    {
        int n = FlightPlacements.Fill(_source, _slots);
        for (int i = 0; i < n; i++)
        {
            FlightPlacement p = _slots[i];
            _multi.SetInstanceTransform(i, new Transform3D(new Basis(Vector3.Up, p.SpinTurnsE5 * (Mathf.Tau / 100000f)), new Vector3(p.XMm, p.YMm, p.ZMm) / 1000f));
        }

        _multi.VisibleInstanceCount = n;
    }
}
