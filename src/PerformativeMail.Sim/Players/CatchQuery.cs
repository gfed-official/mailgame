using System.Runtime.InteropServices;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The input of <see cref="ICatchPredictor.Evaluate"/>. No ADR defines it; D1 — Mail Lead default, pending Evan (TD-171). Values
/// only. PROVISIONAL shape: the throwing-catching epic owns it.
/// </summary>
/// <example><code>var q = new CatchQuery { Tick = ctx.ClientTick, Self = self, Chest = chest, Presented = presented };</code></example>
[StructLayout(LayoutKind.Sequential)]
public readonly struct CatchQuery
{
    /// <summary>The client tick of the press.</summary>
    public uint Tick { get; init; }

    /// <summary>The local carrier's slot (its own flights are never caught).</summary>
    public PlayerSlot Self { get; init; }

    /// <summary>The carrier's chest, mm.</summary>
    public Vec3Mm Chest { get; init; }

    /// <summary>The edge's presented target, or <see cref="AnchorRef.None"/>.</summary>
    public AnchorRef Presented { get; init; }
}
