using Godot;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Views.Terrain;

/// <summary>
/// The ground's vertex arrays from <see cref="IWorldQuery.GetCorners"/> (godot-host-shell story 010, ADR-0026 section 2): one tile is
/// two triangles split along its (0,0)–(S,S) diagonal, with six vertices of its own so the tile's colour (<see cref="TileColor"/>, by
/// <see cref="IWorldQuery.Tile"/> flags) and each triangle's normal stay flat. Corner heights are the Sim's int mm divided by 1000 at
/// this boundary only; tile (tx, tz) spans x ∈ [tx·S, (tx+1)·S], z ∈ [tz·S, (tz+1)·S] like the Sim's height field. Triangles wind
/// clockwise seen from +Y (Godot's front face), so the ground is lit from above. Pure array building; only <see cref="Build"/>'s caller
/// touches the engine. Visual only: never a collider, never sampled for gameplay.
/// </summary>
/// <example><code>TerrainMeshBuilder.Arrays a = TerrainMeshBuilder.BuildArrays(world); ArrayMesh mesh = TerrainMeshBuilder.Build(a);</code></example>
public static class TerrainMeshBuilder
{
    /// <summary>Vertices per tile (two triangles, unshared).</summary>
    public const int VerticesPerTile = 6;

    /// <summary>The built arrays.</summary>
    /// <param name="Vertices">Positions, metres.</param>
    /// <param name="Normals">Per-triangle (flat) normals.</param>
    /// <param name="Colors">Per-tile (flat) colours.</param>
    public sealed record Arrays(Vector3[] Vertices, Vector3[] Normals, Color[] Colors);

    /// <summary>Builds the arrays for every tile of the map.</summary>
    /// <param name="world">The world tables.</param>
    /// <returns>The arrays, <c>TilesX × TilesZ × 6</c> vertices.</returns>
    public static Arrays BuildArrays(IWorldQuery world)
    {
        ArgumentNullException.ThrowIfNull(world);
        int tiles = world.TilesX * world.TilesZ;
        var vertices = new Vector3[tiles * VerticesPerTile];
        var normals = new Vector3[vertices.Length];
        var colors = new Color[vertices.Length];
        float s = world.TileSizeMm / 1000f;
        int v = 0;
        for (int tz = 0; tz < world.TilesZ; tz++)
        {
            for (int tx = 0; tx < world.TilesX; tx++)
            {
                world.GetCorners(tx, tz, out HeightCorners h);
                float x0 = tx * s;
                float z0 = tz * s;
                var c00 = new Vector3(x0, h.H00 / 1000f, z0);
                var c10 = new Vector3(x0 + s, h.H10 / 1000f, z0);
                var c01 = new Vector3(x0, h.H01 / 1000f, z0 + s);
                var c11 = new Vector3(x0 + s, h.H11 / 1000f, z0 + s);
                Color color = TileColor(world.Tile(tx, tz));
                v = Triangle(vertices, normals, colors, v, c00, c10, c11, color);
                v = Triangle(vertices, normals, colors, v, c00, c11, c01, color);
            }
        }

        return new Arrays(vertices, normals, colors);
    }

    /// <summary>Assembles the one-surface ground mesh.</summary>
    /// <param name="arrays">The arrays from <see cref="BuildArrays"/>.</param>
    /// <returns>The mesh; its material is the caller's.</returns>
    public static ArrayMesh Build(Arrays arrays)
    {
        ArgumentNullException.ThrowIfNull(arrays);
        var surface = new Godot.Collections.Array();
        surface.Resize((int)Mesh.ArrayType.Max);
        surface[(int)Mesh.ArrayType.Vertex] = arrays.Vertices;
        surface[(int)Mesh.ArrayType.Normal] = arrays.Normals;
        surface[(int)Mesh.ArrayType.Color] = arrays.Colors;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surface);
        return mesh;
    }

    /// <summary>The untextured greybox colour of a tile, by the strongest flag: blocked, road, lot, water hint, walkable, else bare.</summary>
    /// <param name="flags">The tile's flags.</param>
    /// <returns>The colour.</returns>
    public static Color TileColor(TileFlags flags) =>
        (flags & TileFlags.Blocked) != 0 ? new Color(0.30f, 0.29f, 0.28f)
        : (flags & TileFlags.Road) != 0 ? new Color(0.42f, 0.42f, 0.45f)
        : (flags & TileFlags.Lot) != 0 ? new Color(0.36f, 0.58f, 0.30f)
        : (flags & TileFlags.Water) != 0 ? new Color(0.80f, 0.74f, 0.55f)
        : (flags & TileFlags.Walkable) != 0 ? new Color(0.50f, 0.66f, 0.38f)
        : new Color(0.55f, 0.50f, 0.42f);

    // Godot's front face is clockwise; for clockwise (a, b, c) seen from +Y the outward normal is (c - a) × (b - a).
    private static int Triangle(Vector3[] vertices, Vector3[] normals, Color[] colors, int v, Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        Vector3 normal = (c - a).Cross(b - a).Normalized();
        vertices[v] = a;
        vertices[v + 1] = b;
        vertices[v + 2] = c;
        for (int i = 0; i < 3; i++)
        {
            normals[v + i] = normal;
            colors[v + i] = color;
        }

        return v + 3;
    }
}
