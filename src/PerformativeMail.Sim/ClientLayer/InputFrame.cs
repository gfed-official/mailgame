using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// The input of one client tick as the <see cref="InputLatch"/> emits it (ADR-0012 section 4, Input and Controls GDD
/// R11). A blittable struct (no references, integer fields only) passed by <c>in</c>. On the first tick of a batch it
/// carries every edge and step latched since the last emitted frame, in arrival order; on later ticks it carries state
/// only (held bits, move, look). Edge and step storage is inline ([InlineArray]); slots at or beyond
/// <see cref="EdgeCount"/> / <see cref="StepCount"/> are always zero.
/// <para>
/// Consumers must tolerate a Release or Cancel whose Press never arrived: overflow drops presses but never releases or
/// cancels, and releases beyond the 32-edge window carry over to the next first tick (see <see cref="InputLatch"/>).
/// </para>
/// </summary>
/// <example>
/// <code>
/// ref readonly InputFrame f = ref ctx.Input;
/// bool sprint = (f.HeldBits &amp; FrameActions.Bit(FrameAction.Sprint)) != 0;
/// foreach (InputEdge e in f.Edges) { /* resolve in arrival order */ }
/// </code>
/// </example>
public struct InputFrame
{
    /// <summary>Capacity of the inline edge storage (ADR-0012 section 4); a latch holding more coalesces, then drops the newest presses.</summary>
    public const int MaxEdges = 32;

    /// <summary>Capacity of the inline step storage: at most four distinct actions per frame.</summary>
    public const int MaxSteps = 4;

    private InputEdgeBuffer _edges;
    private InputStepBuffer _steps;

    /// <summary>The client tick this frame belongs to (the client tick counter, not the host tick).</summary>
    public uint Tick { get; internal set; }

    /// <summary>Move intent X, Q15 on the unit disc after the deadzone (x right).</summary>
    public short MoveX { get; internal set; }

    /// <summary>Move intent Y, Q15 on the unit disc after the deadzone (y forward).</summary>
    public short MoveY { get; internal set; }

    /// <summary>Absolute look yaw in BAM (65 536 = 360 degrees); never a delta.</summary>
    public ushort LookYaw { get; internal set; }

    /// <summary>Absolute look pitch in signed BAM.</summary>
    public short LookPitch { get; internal set; }

    /// <summary>Focus cone threshold in permille of cosine (from the camera FOV and aspect).</summary>
    public short FocusCosMinPm { get; internal set; }

    /// <summary>The router state id at the time of the latest <c>SetState</c> (see <see cref="RouterStateInfo"/>); informational.</summary>
    public ushort RouterStateId { get; internal set; }

    /// <summary>The frame flags: <c>FirstInBatch</c> from the latch, the rest transported from the router.</summary>
    public InputFrameFlags Flags { get; internal set; }

    /// <summary>Logical held state; bit <c>n</c> is <c>(FrameAction)n</c> (see <see cref="FrameActions.Bit"/>).</summary>
    public ulong HeldBits { get; internal set; }

    /// <summary>Number of edges in this frame, 0 to <see cref="MaxEdges"/>.</summary>
    public byte EdgeCount { get; internal set; }

    /// <summary>Number of steps in this frame, 0 to <see cref="MaxSteps"/>.</summary>
    public byte StepCount { get; internal set; }

    /// <summary>Edges dropped by the overflow policy since the previous emitted first-tick frame (saturates at 65 535); 0 on later ticks.</summary>
    public ushort DroppedEdges { get; internal set; }

    /// <summary>True when this frame is the first tick of its batch (the only one that carries edges and steps).</summary>
    public readonly bool IsFirstTickInBatch => (Flags & InputFrameFlags.FirstInBatch) != 0;

    /// <summary>Returns edge <paramref name="index"/> in arrival order.</summary>
    /// <param name="index">0 to <see cref="EdgeCount"/> - 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the frame's edges.</exception>
    /// <example><code>InputEdge first = frame.GetEdge(0);</code></example>
    public readonly InputEdge GetEdge(int index)
    {
        if (unchecked((uint)index) >= EdgeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _edges[index];
    }

    /// <summary>Returns step <paramref name="index"/>.</summary>
    /// <param name="index">0 to <see cref="StepCount"/> - 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the frame's steps.</exception>
    /// <example><code>InputStep s = frame.GetStep(0);</code></example>
    public readonly InputStep GetStep(int index)
    {
        if (unchecked((uint)index) >= StepCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _steps[index];
    }

    /// <summary>The frame's edges in arrival order, as a span over the inline storage.</summary>
    /// <example><code>foreach (InputEdge e in frame.Edges) { }</code></example>
    [UnscopedRef]
    public readonly ReadOnlySpan<InputEdge> Edges => ((ReadOnlySpan<InputEdge>)_edges)[..EdgeCount];

    /// <summary>The frame's steps, as a span over the inline storage.</summary>
    /// <example><code>foreach (InputStep s in frame.Steps) { }</code></example>
    [UnscopedRef]
    public readonly ReadOnlySpan<InputStep> Steps => ((ReadOnlySpan<InputStep>)_steps)[..StepCount];

    /// <summary>Writable view of the whole edge storage; used only by the latch.</summary>
    [UnscopedRef]
    internal Span<InputEdge> EdgeStorage => _edges;

    /// <summary>Writable view of the whole step storage; used only by the latch.</summary>
    [UnscopedRef]
    internal Span<InputStep> StepStorage => _steps;
}
