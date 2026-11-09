using Godot;
using PerformativeMail.Client.Core.Ui.Throwing;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Game.Views.Throwing;

/// <summary>
/// The aim arc and end marker (throwing-catching 018, greybox): one custom-drawn <c>Control</c> that projects the published preview
/// polyline through the camera each frame and draws the quads from <see cref="RibbonGeometry"/> with <c>DrawPrimitive</c> (never a line call).
/// </summary>
public partial class AimRibbonView : Control, IPresentedView
{
    private const int MaxPoints = 151;
    private const int MaxQuads = 1024;
    private static readonly Color _green = new(0.239f, 0.749f, 0.353f);
    private static readonly Color _red = new(0.843f, 0.149f, 0.239f);
    private static readonly Color _chalk = new(0.957f, 0.945f, 0.918f);

    private readonly IAimArcSource _source;
    private readonly System.Func<Camera3D?> _camera;
    private readonly Px[] _screen = new Px[MaxPoints];
    private readonly bool[] _valid = new bool[MaxPoints];
    private readonly Quad[] _quads = new Quad[MaxQuads];
    private readonly Vector2[] _poly = new Vector2[4];
    private readonly Color[] _colors = new Color[4];
    private int _quadCount;
    private int _markerCount;
    private AimEnd _end;

    /// <summary>Creates the view.</summary>
    /// <param name="source">The aim preview port.</param>
    /// <param name="camera">Resolves the current gameplay camera.</param>
    public AimRibbonView(IAimArcSource source, System.Func<Camera3D?> camera)
    {
        _source = source;
        _camera = camera;
        Name = "AimRibbonView";
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Quads (stroke and end marker) the last <see cref="Present"/> queued for drawing.</summary>
    public int DrawnQuads => _quadCount + _markerCount;

    /// <inheritdoc />
    public void Present(int alphaPermille)
    {
        _quadCount = 0;
        _markerCount = 0;
        Camera3D? cam = _camera();
        if (_source.Aiming && cam != null)
        {
            int n = System.Math.Min(_source.PointCount, MaxPoints);
            int last = -1;
            for (int i = 0; i < n; i++)
            {
                Vec3Mm p = _source.Point(i);
                var w = new Vector3(p.X / 1000f, p.Y / 1000f, p.Z / 1000f);
                bool ok = !cam.IsPositionBehind(w);
                _valid[i] = ok;
                if (ok)
                {
                    Vector2 s = cam.UnprojectPosition(w);
                    _screen[i] = new Px((int)s.X, (int)s.Y);
                    last = i;
                }
            }

            _end = _source.End;
            _quadCount = RibbonGeometry.BuildStroke(_screen.AsSpan(0, n), _valid.AsSpan(0, n), _end, _quads.AsSpan(0, MaxQuads - 4));
            if (last == n - 1)
            {
                _markerCount = RibbonGeometry.BuildMarker(_end, _screen[last], _quads.AsSpan(_quadCount, 4));
            }
        }

        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        Color c = _end switch
        {
            AimEnd.Accept => _green,
            AimEnd.Misdeliver => _red,
            _ => _chalk,
        };
        System.Array.Fill(_colors, c);
        for (int i = 0; i < _quadCount + _markerCount; i++)
        {
            Quad q = _quads[i];
            _poly[0] = new Vector2(q.A.X, q.A.Y);
            _poly[1] = new Vector2(q.B.X, q.B.Y);
            _poly[2] = new Vector2(q.C.X, q.C.Y);
            _poly[3] = new Vector2(q.D.X, q.D.Y);
            DrawPrimitive(_poly, _colors, null!);
        }
    }
}
