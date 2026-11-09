using Godot;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Views.Terrain;

/// <summary>
/// The map edge made readable (WP-G, greybox): the Sim already stops a carrier <c>CarrierBounds.InsetMm</c> inside
/// <see cref="IWorldQuery.Bounds"/> (ADR-0013 phase 7) and Throwing treats the four sides as vertical solids, but the ground mesh simply
/// ended there, so the street ends showed sky below the horizon. This adds a <c>Fence</c> box on each side of the bounds (the invisible
/// wall, now visible) and a <c>Skirt</c> of flat ground outside them (lawn on three sides, sea on the beach side) out to
/// <see cref="SkirtM"/>. Visual only: no colliders, nothing the Sim or the camera boom reads.
/// </summary>
/// <example><code>terrain.AddChild(MapEdgeBuilder.Build(world));</code></example>
public static class MapEdgeBuilder
{
    /// <summary>How far the skirt reaches past the bounds, metres.</summary>
    public const float SkirtM = 400f;

    /// <summary>Fence height above the west edge's ground, metres.</summary>
    public const float FenceHeightM = 1.2f;

    /// <summary>Fence thickness, metres (straddling the bounds line).</summary>
    public const float FenceThicknessM = 0.2f;

    private static readonly Color _fence = new(0.45f, 0.33f, 0.22f);
    private static readonly Color _lawn = new(0.49f, 0.66f, 0.40f);
    private static readonly Color _sea = new(0.36f, 0.50f, 0.60f);

    /// <summary>Builds the <c>MapEdge</c> node (<c>Fence/West|East|North|South</c>, <c>Skirt/West|East|North|South</c>).</summary>
    /// <param name="world">The world tables.</param>
    /// <returns>The node.</returns>
    public static Node3D Build(IWorldQuery world)
    {
        ArgumentNullException.ThrowIfNull(world);
        MapBounds b = world.Bounds;
        float minX = b.MinXMm / 1000f;
        float minZ = b.MinZMm / 1000f;
        float maxX = b.MaxXMm / 1000f;
        float maxZ = b.MaxZMm / 1000f;

        // The fence runs from below the lowest corner (the sea floor at the beach end) to FenceHeightM above the west edge's ground.
        float ground = world.TerrainHeightMm(b.MinXMm, (b.MinZMm + b.MaxZMm) / 2) / 1000f;
        float bottom = (world.MinCornerHeightMm() / 1000f) - 0.5f;
        float top = ground + FenceHeightM;
        float midY = (bottom + top) / 2f;
        float h = top - bottom;
        float t = FenceThicknessM;

        var edge = new Node3D { Name = "MapEdge" };
        var fence = new Node3D { Name = "Fence" };
        edge.AddChild(fence);
        fence.AddChild(Box("West", new Vector3(minX, midY, (minZ + maxZ) / 2f), new Vector3(t, h, maxZ - minZ + t), _fence));
        fence.AddChild(Box("East", new Vector3(maxX, midY, (minZ + maxZ) / 2f), new Vector3(t, h, maxZ - minZ + t), _fence));
        fence.AddChild(Box("North", new Vector3((minX + maxX) / 2f, midY, minZ), new Vector3(maxX - minX + t, h, t), _fence));
        fence.AddChild(Box("South", new Vector3((minX + maxX) / 2f, midY, maxZ), new Vector3(maxX - minX + t, h, t), _fence));

        // Lawn at the west edge's ground height on the west, north and south (the corners included); sea level past the east edge, where
        // the M-1a beach runs into the sea. A hair below the ground so the map's own mesh wins where they meet.
        float lawnY = ground - 0.02f;
        float seaY = world.TerrainHeightMm(b.MaxXMm, (b.MinZMm + b.MaxZMm) / 2) < 0 ? -0.02f : lawnY;
        Color eastColor = seaY < lawnY ? _sea : _lawn;
        float s = SkirtM;
        var skirt = new Node3D { Name = "Skirt" };
        edge.AddChild(skirt);
        skirt.AddChild(Plane("West", minX - s, minZ - s, minX, maxZ + s, lawnY, _lawn));
        skirt.AddChild(Plane("North", minX, minZ - s, maxX, minZ, lawnY, _lawn));
        skirt.AddChild(Plane("South", minX, maxZ, maxX, maxZ + s, lawnY, _lawn));
        skirt.AddChild(Plane("East", maxX, minZ - s, maxX + s, maxZ + s, seaY, eastColor));
        return edge;
    }

    private static int MinCornerHeightMm(this IWorldQuery w)
    {
        int best = int.MaxValue;
        for (int tz = 0; tz < w.TilesZ; tz++)
        {
            for (int tx = 0; tx < w.TilesX; tx++)
            {
                w.GetCorners(tx, tz, out HeightCorners c);
                best = Math.Min(best, Math.Min(Math.Min(c.H00, c.H10), Math.Min(c.H01, c.H11)));
            }
        }

        return best;
    }

    private static MeshInstance3D Box(string name, Vector3 centre, Vector3 size, Color color) => new()
    {
        Name = name,
        Position = centre,
        Mesh = new BoxMesh { Size = size },
        MaterialOverride = new StandardMaterial3D { AlbedoColor = color },
    };

    private static MeshInstance3D Plane(string name, float x0, float z0, float x1, float z1, float y, Color color) => new()
    {
        Name = name,
        Position = new Vector3((x0 + x1) / 2f, y, (z0 + z1) / 2f),
        Mesh = new PlaneMesh { Size = new Vector2(x1 - x0, z1 - z0) },
        MaterialOverride = new StandardMaterial3D { AlbedoColor = color },
    };
}
