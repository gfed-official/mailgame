using Godot;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Views.Terrain;

/// <summary>The flat greybox colour of a static solid by its <see cref="SolidKind"/> (godot-host-shell story 010); no textures.</summary>
/// <example><code>var material = new StandardMaterial3D { AlbedoColor = SolidKindColors.For(solid.Kind) };</code></example>
public static class SolidKindColors
{
    /// <summary>The colour of a solid kind (an unknown kind is magenta, like the placeholder view).</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The colour.</returns>
    public static Color For(SolidKind kind) => kind switch
    {
        SolidKind.House => new Color(0.82f, 0.74f, 0.60f),
        SolidKind.Roof => new Color(0.55f, 0.22f, 0.18f),
        SolidKind.PostOffice => new Color(0.25f, 0.40f, 0.70f),
        SolidKind.Prop => new Color(0.50f, 0.50f, 0.50f),
        _ => new Color(1f, 0f, 1f),
    };
}
