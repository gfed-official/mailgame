using Godot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Views.Terrain;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.World;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// godot-host-shell story 010 over the real boot. <c>World/Terrain</c> was built once. Its <c>Ground</c> mesh has six vertices per
/// tile, and every tile's corner vertices equal <see cref="IWorldQuery.GetCorners"/> / 1000 (±1 mm). The ground material uses vertex
/// colour with no texture. One translucent <c>Water</c> quad sits at y = 0 over the map bounds. <c>Solids</c> holds one box per
/// <see cref="IWorldQuery.StaticSolids"/> entry, named by index, with the solid's extents and centre. The pad markers, one light and
/// one environment exist. Nothing under the terrain collides. The fallback camera is current and looks at the pad.
/// </summary>
[SmokeScenario("terrain-build")]
public sealed class TerrainBuildSmoke : SmokeScenario
{
    private const float MmTolerance = 0.001f;

    private PresenterProbeView _probe = null!;

    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop)
    {
        _probe = new PresenterProbeView { Tag = "terrain" };
        loop.GetNode("World").AddChild(_probe);
    }

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        if (_probe.Deps is not { } deps)
        {
            ctx.Fail("probe has no deps");
            return;
        }

        IWorldQuery world = deps.World;
        var terrain = ctx.Loop.GetNode<TerrainView>("World/Terrain");
        Expect(ctx, "terrain built once at boot step 7", terrain.BuildCount, 1);
        CheckGround(ctx, terrain, world);
        CheckWater(ctx, terrain, world.Bounds);
        CheckSolids(ctx, terrain, world.StaticSolids);
        Expect(ctx, "pad ring + slots + door + Intake markers", terrain.GetNodeOrNull("Pad")?.GetChildCount() ?? -1, world.PostOffice.PadSlots + 3);
        Expect(ctx, "one DirectionalLight3D", terrain.FindChildren("*", nameof(DirectionalLight3D), true, false).Count, 1);
        Expect(ctx, "one WorldEnvironment", terrain.FindChildren("*", nameof(WorldEnvironment), true, false).Count, 1);
        Expect(ctx, "no collider under World/Terrain", terrain.FindChildren("*", nameof(CollisionObject3D), true, false).Count, 0);

        var camera = ctx.Loop.GetNode<Camera3D>("World/CameraRig/Boom/Camera3D");
        PostOfficeRecord po = world.PostOffice;
        var pad = new Vector3(po.PadCentre.X / 1000f, po.PadCentre.Y / 1000f, po.PadCentre.Z / 1000f);
        Expect(ctx, "fallback camera is current", camera.Current, true);
        Expect(ctx, "fallback camera looks at the pad", (pad - camera.GlobalPosition).Normalized().Dot(-camera.GlobalBasis.Z) > 0.999f, true);
        Expect(ctx, "fallback camera 4 m above the pad", Mathf.IsEqualApprox(camera.GlobalPosition.Y - pad.Y, TerrainView.FallbackUpM), true);
    }

    private static void CheckGround(SmokeContext ctx, TerrainView terrain, IWorldQuery world)
    {
        var ground = terrain.GetNodeOrNull<MeshInstance3D>("Ground");
        if (ground?.Mesh is not ArrayMesh mesh || terrain.GroundArrays is not { } arrays)
        {
            ctx.Fail("no Ground ArrayMesh");
            return;
        }

        int tiles = world.TilesX * world.TilesZ;
        Expect(ctx, "ground surfaces", mesh.GetSurfaceCount(), 1);
        Expect(ctx, "ground vertices = tiles × 6", mesh.SurfaceGetArrayLen(0), tiles * TerrainMeshBuilder.VerticesPerTile);
        // Read the vertices back from the mesh; the dummy renderer may keep no surface data, then the built arrays stand in.
        Vector3[] vertices = mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        bool fromMesh = vertices.Length == tiles * TerrainMeshBuilder.VerticesPerTile;
        ctx.Pass($"corner parity read from {(fromMesh ? "the ArrayMesh surface" : "the built arrays (no surface data headless)")}");
        if (!fromMesh)
        {
            vertices = arrays.Vertices;
        }

        int mismatches = 0;
        int up = 0;
        for (int tz = 0; tz < world.TilesZ; tz++)
        {
            for (int tx = 0; tx < world.TilesX; tx++)
            {
                world.GetCorners(tx, tz, out HeightCorners h);
                int v = ((tz * world.TilesX) + tx) * TerrainMeshBuilder.VerticesPerTile;

                // Triangles (00, 10, 11) and (00, 11, 01).
                mismatches += Off(vertices[v], h.H00) + Off(vertices[v + 1], h.H10) + Off(vertices[v + 2], h.H11)
                    + Off(vertices[v + 3], h.H00) + Off(vertices[v + 4], h.H11) + Off(vertices[v + 5], h.H01);
                up += arrays.Normals[v].Y > 0f && arrays.Normals[v + 3].Y > 0f ? 1 : 0;
            }
        }

        Expect(ctx, "corner vertices off the Sim's corner heights by > 1 mm", mismatches, 0);
        Expect(ctx, "tiles whose normals face up (lit from above)", up, tiles);
        MapBounds b = world.Bounds;
        Aabb box = mesh.GetAabb();
        bool inside = box.Position.X * 1000 >= b.MinXMm - 1 && box.End.X * 1000 <= b.MaxXMm + 1
            && box.Position.Z * 1000 >= b.MinZMm - 1 && box.End.Z * 1000 <= b.MaxZMm + 1;
        Expect(ctx, $"ground AABB {box} within the map bounds", inside, true);
        var material = ground.MaterialOverride as StandardMaterial3D;
        Expect(ctx, "ground material: vertex colour as albedo, no texture", material is { VertexColorUseAsAlbedo: true, AlbedoTexture: null }, true);
    }

    private static void CheckWater(SmokeContext ctx, TerrainView terrain, MapBounds b)
    {
        var water = terrain.GetNodeOrNull<MeshInstance3D>("Water");
        if (water?.Mesh is not PlaneMesh plane)
        {
            ctx.Fail("no Water plane");
            return;
        }

        Aabb box = water.GetAabb();
        box.Position += water.Position;
        bool spans = Mathf.IsEqualApprox(box.Position.X * 1000, b.MinXMm, 1f) && Mathf.IsEqualApprox(box.End.X * 1000, b.MaxXMm, 1f)
            && Mathf.IsEqualApprox(box.Position.Z * 1000, b.MinZMm, 1f) && Mathf.IsEqualApprox(box.End.Z * 1000, b.MaxZMm, 1f);
        Expect(ctx, "water at y = 0", water.Position.Y, 0f);
        Expect(ctx, $"water quad {plane.Size} spans the map bounds", spans, true);
        Expect(ctx, "water is translucent", water.MaterialOverride is StandardMaterial3D { Transparency: BaseMaterial3D.TransparencyEnum.Alpha }, true);
    }

    private static void CheckSolids(SmokeContext ctx, TerrainView terrain, ReadOnlySpan<StaticSolid> solids)
    {
        Node? parent = terrain.GetNodeOrNull("Solids");
        Expect(ctx, "boxes under World/Terrain/Solids = StaticSolids.Length", parent?.GetChildCount() ?? -1, solids.Length);
        int wrong = 0;
        for (int i = 0; i < solids.Length; i++)
        {
            StaticSolid s = solids[i];
            var box = parent?.GetNodeOrNull<MeshInstance3D>(s.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var size = new Vector3(s.Box.Max.X - s.Box.Min.X, s.Box.Max.Y - s.Box.Min.Y, s.Box.Max.Z - s.Box.Min.Z) / 1000f;
            var centre = new Vector3(s.Box.Min.X + s.Box.Max.X, s.Box.Min.Y + s.Box.Max.Y, s.Box.Min.Z + s.Box.Max.Z) / 2000f;
            if (box?.Mesh is not BoxMesh mesh || !mesh.Size.IsEqualApprox(size) || !box.Position.IsEqualApprox(centre))
            {
                wrong++;
            }
        }

        Expect(ctx, "solid boxes not matching their solid's extents and centre", wrong, 0);
    }

    private static int Off(Vector3 vertex, int heightMm) => Mathf.Abs(vertex.Y - (heightMm / 1000f)) <= MmTolerance ? 0 : 1;
}
