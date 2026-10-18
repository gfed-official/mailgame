using System.Runtime.InteropServices;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The carrier's claimed feet position, <c>int x, y, z</c> mm, 12 B sequential (ADR-0014 section 5; Player Character P13 layout).
/// Carried in every range-gated payload and latched at press.
/// </summary>
/// <example><code>var claim = new ClaimedPosition(1000, 0, -2500);</code></example>
[StructLayout(LayoutKind.Sequential)]
public readonly struct ClaimedPosition
{
    /// <summary>The size in bytes.</summary>
    public const int SizeBytes = 12;

    /// <summary>X in mm.</summary>
    public readonly int X;

    /// <summary>Y in mm.</summary>
    public readonly int Y;

    /// <summary>Z in mm.</summary>
    public readonly int Z;

    /// <summary>Creates a claim.</summary>
    /// <param name="x">X in mm.</param>
    /// <param name="y">Y in mm.</param>
    /// <param name="z">Z in mm.</param>
    public ClaimedPosition(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}
