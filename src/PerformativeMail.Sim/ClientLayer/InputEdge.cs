using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PerformativeMail.Sim.ClientLayer;

/// <summary>Whether an <see cref="InputEdge"/> is a press, a release or a cancel. Values are explicit and append-only (ADR-0012 section 4).</summary>
public enum EdgeKind : byte
{
    /// <summary>The action went down.</summary>
    Press = 1,

    /// <summary>The action went up.</summary>
    Release = 2,

    /// <summary>The router cancelled the action (for example a state change). Never dropped by the latch.</summary>
    Cancel = 3,
}

/// <summary>
/// One ordered input edge: exactly 16 bytes, integer fields only, no references (ADR-0012 section 4). The
/// <c>Interact</c> press carries the presented target in <see cref="TargetEntity"/>, <see cref="TargetSlot"/> and
/// <see cref="PresentedSeq"/>; other edges leave them 0.
/// </summary>
/// <example>
/// <code>
/// var jump = new InputEdge(FrameAction.Jump, EdgeKind.Press, arrivalSeq: 17, routerStateId: 1);
/// var interact = new InputEdge(FrameAction.Interact, EdgeKind.Press, 18, 1, targetEntity: 0x0200_0001u, targetSlot: 2, presentedSeq: 9);
/// </code>
/// </example>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public readonly struct InputEdge
{
    [FieldOffset(0)]
    private readonly FrameAction _action;
    [FieldOffset(1)]
    private readonly EdgeKind _kind;
    [FieldOffset(2)]
    private readonly EdgeReason _reason;
    [FieldOffset(3)]
    private readonly byte _deviceClass;
    [FieldOffset(4)]
    private readonly ushort _routerStateId;
    [FieldOffset(6)]
    private readonly ushort _arrivalSeq;
    [FieldOffset(8)]
    private readonly uint _targetEntity;
    [FieldOffset(12)]
    private readonly byte _targetSlot;
    [FieldOffset(13)]
    private readonly byte _reserved;
    [FieldOffset(14)]
    private readonly ushort _presentedSeq;

    /// <summary>Creates an edge.</summary>
    /// <param name="action">The frame action.</param>
    /// <param name="kind">Press, release or cancel.</param>
    /// <param name="arrivalSeq">The router's wrapping arrival sequence number; edges are delivered in arrival order and compared with <c>SerialU16</c>.</param>
    /// <param name="routerStateId">The router state the edge was gated against at event time.</param>
    /// <param name="reason">Why the router produced the edge.</param>
    /// <param name="deviceClass">The router's device class code (opaque to the Sim).</param>
    /// <param name="targetEntity">The presented target's entity id value (0 for none).</param>
    /// <param name="targetSlot">The presented target's slot.</param>
    /// <param name="presentedSeq">The presented-snapshot sequence the target came from.</param>
    public InputEdge(
        FrameAction action,
        EdgeKind kind,
        ushort arrivalSeq,
        ushort routerStateId,
        EdgeReason reason = EdgeReason.None,
        byte deviceClass = 0,
        uint targetEntity = 0,
        byte targetSlot = 0,
        ushort presentedSeq = 0)
    {
        _action = action;
        _kind = kind;
        _reason = reason;
        _deviceClass = deviceClass;
        _routerStateId = routerStateId;
        _arrivalSeq = arrivalSeq;
        _targetEntity = targetEntity;
        _targetSlot = targetSlot;
        _reserved = 0;
        _presentedSeq = presentedSeq;
    }

    /// <summary>The frame action; its id is the bit index in <see cref="InputFrame.HeldBits"/>.</summary>
    public FrameAction Action => _action;

    /// <summary>Press, release or cancel.</summary>
    public EdgeKind Kind => _kind;

    /// <summary>Why the router produced the edge.</summary>
    public EdgeReason Reason => _reason;

    /// <summary>The router's device class code (opaque to the Sim).</summary>
    public byte DeviceClass => _deviceClass;

    /// <summary>The router state the edge was gated against at event time.</summary>
    public ushort RouterStateId => _routerStateId;

    /// <summary>The router's wrapping arrival sequence number (compare with <c>SerialU16</c>, never numerically).</summary>
    public ushort ArrivalSeq => _arrivalSeq;

    /// <summary>The presented target's entity id value, or 0.</summary>
    public uint TargetEntity => _targetEntity;

    /// <summary>The presented target's slot.</summary>
    public byte TargetSlot => _targetSlot;

    /// <summary>The presented-snapshot sequence the target came from.</summary>
    public ushort PresentedSeq => _presentedSeq;
}

/// <summary>Fixed-capacity inline storage for <see cref="InputEdge"/>s (no heap, no <c>List&lt;T&gt;</c> on the tick path).</summary>
[InlineArray(InputFrame.MaxEdges)]
public struct InputEdgeBuffer
{
#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
#pragma warning disable IDE0051 // Remove unused private members
    private InputEdge _element;
#pragma warning restore IDE0051
#pragma warning restore CS0169
}
