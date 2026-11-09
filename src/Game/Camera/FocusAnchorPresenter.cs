using Godot;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Game.Hud;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Game.Views;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Game.Camera;

/// <summary>
/// Builds the HUD's <see cref="FocusFeed"/> once per frame (player-character story 026; ADR-0014 section 7, ADR-0019 section 2). Boot
/// registers it on the World presenter after the subtree walk, so it runs after <see cref="CameraRig"/> has placed the camera and
/// before the Hud presenter reads <see cref="Current"/>. From the published <see cref="IInteractionQuery"/> snapshot it:
/// <list type="bullet">
/// <item>projects the focused anchor's point (<see cref="IAnchorPoints"/>, else the centre of the entity view's bounds) with
/// <c>Camera3D.UnprojectPosition</c>; an anchor behind the camera (<c>IsPositionBehind</c>) is not visible;</item>
/// <item>bounds it by the projected corners of the entity view's world-space visual bounds (the <c>GetAabb()</c> of every
/// <see cref="VisualInstance3D"/> under the view, from <see cref="EntityViewRegistry"/>), or of a <see cref="FallbackCubeM"/> cube
/// around the point when the entity has no view, clamped to the viewport;</item>
/// <item>copies verb, mode, availability and reason, prompt args, state, progress and last cancel unchanged, and the snapshot's
/// <c>Seq</c> as <see cref="FocusFeed.PresentedSeq"/>.</item>
/// </list>
/// With no snapshot published (no client producer yet) the feed stays <c>default</c>: not visible, <c>PresentedSeq</c> 0. Reads only the
/// published states and Godot nodes, never the Sim. Main thread only.
/// </summary>
/// <example><code>var focus = new FocusAnchorPresenter(rigCamera, deps); deps.Published.Add&lt;IHudFocusFeed&gt;(focus); world.Register(focus);</code></example>
public sealed class FocusAnchorPresenter : IPresentedView, IHudFocusFeed
{
    /// <summary>The edge of the bounds cube used when the focused entity has no view, metres.</summary>
    public const float FallbackCubeM = 0.5f;

    private readonly Camera3D _camera;
    private readonly EntityViewRegistry _entities;
    private readonly IInteractionQuery? _query;
    private readonly IAnchorPoints? _points;
    private FocusFeed _feed;

    /// <summary>Creates the presenter over the rig's camera.</summary>
    /// <param name="camera">The camera the feed projects through (<c>World/CameraRig/Boom/Camera3D</c>).</param>
    /// <param name="deps">The presenters' deps (published states and the entity-view registry).</param>
    public FocusAnchorPresenter(Camera3D camera, PresenterDeps deps)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(deps);
        _camera = camera;
        _entities = deps.Entities;
        deps.Published.TryGet(out _query);
        deps.Published.TryGet(out _points);
    }

    /// <summary>True when an <see cref="IInteractionQuery"/> was published at construction.</summary>
    public bool HasSnapshot => _query is not null;

    /// <inheritdoc />
    public ref readonly FocusFeed Current => ref _feed;

    /// <inheritdoc />
    public void Present(int alphaPermille)
    {
        if (_query is null)
        {
            return;
        }

        ref readonly InteractionSnapshot s = ref _query.Current;
        bool visible = false;
        Vector2 pos = default;
        Rect2 bounds = default;
        if (!s.Focused.IsNone && GodotObject.IsInstanceValid(_camera) && _camera.IsInsideTree()
            && TryPoint(s.Focused, out Vector3 point) && !_camera.IsPositionBehind(point))
        {
            pos = _camera.UnprojectPosition(point);
            bounds = Bounds(s.Focused.Entity, point, pos);
            visible = true;
        }

        _feed = new FocusFeed
        {
            Visible = visible,
            Kind = visible ? FocusFeedKind.Focused : FocusFeedKind.None,
            Focused = s.Focused,
            Verb = s.Verb,
            Mode = s.Mode,
            Available = s.Available != 0,
            Reason = s.Reason,
            PromptArgs = s.PromptArgs,
            State = s.State,
            ProgressPm = s.ProgressPm,
            LastCancel = s.LastCancel,
            ScreenPos = pos,
            ScreenBounds = bounds,
            PresentedSeq = s.Seq,
        };
    }

    private static bool TryVisualBounds(Node node, ref Aabb box, ref bool found)
    {
        if (node is VisualInstance3D visual)
        {
            Aabb world = visual.GlobalTransform * visual.GetAabb();
            box = found ? box.Merge(world) : world;
            found = true;
        }

        int count = node.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            TryVisualBounds(node.GetChild(i), ref box, ref found);
        }

        return found;
    }

    private bool TryPoint(AnchorRef anchor, out Vector3 point)
    {
        if (_points is not null && _points.TryGetPoint(anchor, out Vec3Mm p))
        {
            point = new Vector3(p.X / 1000f, p.Y / 1000f, p.Z / 1000f);
            return true;
        }

        Aabb box = default;
        bool found = false;
        if (_entities.TryGet(anchor.Entity, out Node3D view))
        {
            point = TryVisualBounds(view, ref box, ref found) ? box.GetCenter() : view.GlobalPosition;
            return true;
        }

        point = default;
        return false;
    }

    private Rect2 Bounds(EntityId entity, Vector3 point, Vector2 pos)
    {
        Aabb box = default;
        bool found = false;
        if (!(_entities.TryGet(entity, out Node3D view) && TryVisualBounds(view, ref box, ref found)))
        {
            var half = new Vector3(FallbackCubeM / 2f, FallbackCubeM / 2f, FallbackCubeM / 2f);
            box = new Aabb(point - half, half * 2f);
        }

        var rect = new Rect2(pos, Vector2.Zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = box.GetEndpoint(i);
            if (!_camera.IsPositionBehind(corner))
            {
                rect = rect.Expand(_camera.UnprojectPosition(corner));
            }
        }

        return rect.Intersection(_camera.GetViewport().GetVisibleRect());
    }
}
