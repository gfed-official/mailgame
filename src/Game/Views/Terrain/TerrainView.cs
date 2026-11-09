using System.Diagnostics;
using Godot;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Views.Terrain;

/// <summary>
/// The greybox street on <c>World/Terrain</c> (godot-host-shell story 010, ADR-0019 sections 2-3, ADR-0026): a world-build step that boot
/// step 7 runs once, after <see cref="Initialize"/>, from the immutable <see cref="IWorldQuery"/> tables only. It adds the untextured
/// <c>Ground</c> (<see cref="TerrainMeshBuilder"/>, vertex colour as albedo), the translucent <c>Water</c> quad at y = 0 over the map
/// bounds (the Sim's water plane, not the tile hint), one box per <see cref="IWorldQuery.StaticSolids"/> entry under <c>Solids</c>
/// (named by index, extents exactly the solid's box, coloured by <see cref="SolidKindColors"/>), the spawn-pad markers
/// (<see cref="PadMarkerBuilder"/>), the map-edge fence and the ground skirt past it (<see cref="MapEdgeBuilder"/>), one <see cref="DirectionalLight3D"/> and one <see cref="WorldEnvironment"/>, and sets the
/// <see cref="FallbackCamera"/> current at a pose behind the pad, once (the camera rig takes over by writing its own transform).
/// No colliders, no image textures, no per-frame work (not a presented view). Main thread only.
/// </summary>
/// <example><code>// Main.tscn: World/Terrain carries this script; WorldPresenter's subtree walk registers it as a world-build step.</code></example>
public partial class TerrainView : Node3D, IInjectable<PresenterDeps>, IWorldBuildStep
{
    /// <summary>How far behind the pad (away from the Post Office door) the fallback camera sits, metres.</summary>
    public const float FallbackBackM = 6f;

    /// <summary>How far above the pad the fallback camera sits, metres.</summary>
    public const float FallbackUpM = 4f;

    /// <summary>The camera made current at the fallback pose (<c>World/CameraRig/Boom/Camera3D</c>, wired in <c>Main.tscn</c>); null to skip.</summary>
    [Export]
    public Camera3D? FallbackCamera { get; set; }

    /// <inheritdoc />
    public bool IsInitialized { get; private set; }

    /// <summary>Completed <see cref="Build"/> calls (one per boot).</summary>
    public int BuildCount { get; private set; }

    /// <summary>The ground arrays of the last build (smoke parity checks); null before it.</summary>
    public TerrainMeshBuilder.Arrays? GroundArrays { get; private set; }

    /// <inheritdoc />
    public void Initialize(PresenterDeps deps)
    {
        ArgumentNullException.ThrowIfNull(deps);
        IsInitialized = true;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Not initialised yet, or already built.</exception>
    public void Build(IWorldQuery world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!IsInitialized)
        {
            throw new InvalidOperationException("TerrainView is inert until Initialize.");
        }

        if (BuildCount > 0)
        {
            throw new InvalidOperationException("The terrain is built once per boot.");
        }

        long started = Stopwatch.GetTimestamp();
        GroundArrays = TerrainMeshBuilder.BuildArrays(world);
        AddChild(new MeshInstance3D
        {
            Name = "Ground",
            Mesh = TerrainMeshBuilder.Build(GroundArrays),
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true },
        });
        AddChild(BuildWater(world.Bounds));
        AddChild(BuildSolids(world.StaticSolids));
        AddChild(MapEdgeBuilder.Build(world));
        if (PadMarkerBuilder.Build(world) is { } pad)
        {
            AddChild(pad);
        }

        AddChild(new DirectionalLight3D { Name = "Sun", RotationDegrees = new Vector3(-50f, 30f, 0f) });
        AddChild(new WorldEnvironment
        {
            Name = "Environment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.62f, 0.75f, 0.88f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.75f, 0.78f, 0.85f),
                AmbientLightEnergy = 0.5f,
            },
        });
        BuildCount++;
        double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        GD.Print($"[TerrainView] built tiles={world.TilesX}x{world.TilesZ} solids={world.StaticSolids.Length} in {ms:0.0} ms");
        SetFallbackPose(world.PostOffice);
    }

    private static MeshInstance3D BuildWater(MapBounds b)
    {
        float minX = b.MinXMm / 1000f;
        float minZ = b.MinZMm / 1000f;
        float sizeX = (b.MaxXMm - b.MinXMm) / 1000f;
        float sizeZ = (b.MaxZMm - b.MinZMm) / 1000f;
        return new MeshInstance3D
        {
            Name = "Water",
            Position = new Vector3(minX + (sizeX / 2f), 0f, minZ + (sizeZ / 2f)),
            Mesh = new PlaneMesh { Size = new Vector2(sizeX, sizeZ) },
            MaterialOverride = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.45f, 0.58f, 0.66f, 0.6f),
            },
        };
    }

    private static Node3D BuildSolids(ReadOnlySpan<StaticSolid> solids)
    {
        var parent = new Node3D { Name = "Solids" };
        for (int i = 0; i < solids.Length; i++)
        {
            StaticSolid solid = solids[i];
            Vec3Mm min = solid.Box.Min;
            Vec3Mm max = solid.Box.Max;
            var size = new Vector3((max.X - min.X) / 1000f, (max.Y - min.Y) / 1000f, (max.Z - min.Z) / 1000f);
            parent.AddChild(new MeshInstance3D
            {
                Name = solid.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Position = new Vector3((min.X + max.X) / 2000f, (min.Y + max.Y) / 2000f, (min.Z + max.Z) / 2000f),
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = SolidKindColors.For(solid.Kind) },
            });
        }

        return parent;
    }

    // Behind the pad, on the side away from the Post Office door, looking at the pad (and the door beyond it); +Z when they coincide.
    private void SetFallbackPose(PostOfficeRecord po)
    {
        if (FallbackCamera is not { } camera || !IsInstanceValid(camera))
        {
            return;
        }

        var pad = new Vector3(po.PadCentre.X / 1000f, po.PadCentre.Y / 1000f, po.PadCentre.Z / 1000f);
        var door = new Vector3(po.DoorAnchor.X / 1000f, pad.Y, po.DoorAnchor.Z / 1000f);
        Vector3 away = pad - door;
        away = away.LengthSquared() > 1e-6f ? away.Normalized() : Vector3.Back;
        Vector3 eye = pad + (away * FallbackBackM) + (Vector3.Up * FallbackUpM);
        camera.GlobalTransform = new Transform3D(Basis.Identity, eye).LookingAt(pad, Vector3.Up);
        camera.Current = true;
        GD.Print($"[TerrainView] fallback camera pose set eye={eye} target={pad}");
    }
}
