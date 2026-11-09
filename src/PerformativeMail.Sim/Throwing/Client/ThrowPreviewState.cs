using System.Runtime.InteropServices;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// What the aim preview (<see cref="ThrowPreviewStep"/>, sub-step 4c) publishes each client tick (GDD T3; ADR-0016 sections 2 and 3,
/// ADR-0015 section 8; throwing-catching story 004). Presentation, the label (stories 005, 019), the ribbon (018) and the input router
/// read it and never recompute it. Flags are bytes (0 or 1), not <c>bool</c>; values only. The polyline and the hat pins are spans on
/// <see cref="IThrowPreviewRead"/>.
/// </summary>
/// <example><code>ref readonly ThrowPreviewState s = ref preview.Current; bool green = s.Active == 1 &amp;&amp; s.EndClass == EndClass.Accept;</code></example>
[StructLayout(LayoutKind.Sequential)]
public struct ThrowPreviewState
{
    /// <summary>Bumped on every change to the published state; unchanged when an unchanged cache key re-runs nothing.</summary>
    public uint Seq { get; internal set; }

    /// <summary>1 while aiming a throwable selected entry (an arc is published), else 0.</summary>
    public byte Active { get; internal set; }

    /// <summary>Why nothing can be thrown; published whether aiming or not.</summary>
    public NotThrowable NotThrowable { get; internal set; }

    /// <summary>The arc's end class; <see cref="EndClass.None"/> while inactive.</summary>
    public EndClass EndClass { get; internal set; }

    /// <summary><c>Evaluate</c>'s reason for <see cref="EndClass.Reject"/>, else <see cref="MailRejectReason.None"/>.</summary>
    public MailRejectReason RejectReason { get; internal set; }

    /// <summary>1 when the selected entry's id or count differs from the entry latched at aim start, or it vanished.</summary>
    public byte EntryStale { get; internal set; }

    /// <summary>The selected hotbar index.</summary>
    public byte HotbarIndex { get; internal set; }

    /// <summary>The latched unit count while aiming (what the thrower sees), else the selected entry's.</summary>
    public byte Units { get; internal set; }

    /// <summary>1 when F1's chest probe moved the origin to the chest point.</summary>
    public byte OriginFromChest { get; internal set; }

    /// <summary>The throwing hand used (+1 or −1).</summary>
    public sbyte Side { get; internal set; }

    /// <summary>The latched entry id while aiming, else the selected entry's (none when empty).</summary>
    public EntryId Entry { get; internal set; }

    /// <summary>Direction x: exactly the integer <c>LaunchMath.Compute</c> consumed for the arc (and <c>ThrowRelease</c> sends).</summary>
    public short DirX { get; internal set; }

    /// <summary>Direction y.</summary>
    public short DirY { get; internal set; }

    /// <summary>Direction z.</summary>
    public short DirZ { get; internal set; }

    /// <summary>The predicted feet the arc launched from (the position a release claims).</summary>
    public Vec3Mm Feet { get; internal set; }

    /// <summary>The arc's end point: the contact point, or <c>p(maxFlightTicks)</c> on timeout.</summary>
    public Vec3Mm EndPoint { get; internal set; }

    /// <summary>The contact's receiver key (the destination's <c>EntityId.Value</c> for a mailbox); 0 for none, ground or water.</summary>
    public uint EndKey { get; internal set; }

    /// <summary>The flight age, ticks, of the end point.</summary>
    public ushort EndAge { get; internal set; }

    /// <summary>The polyline length: <see cref="EndAge"/> + 1 while active, else 0.</summary>
    public ushort PointCount { get; internal set; }

    /// <summary>The client tick the current end class began.</summary>
    public uint ClassSinceTick { get; internal set; }

    /// <summary>The client tick the class last became <see cref="EndClass.Misdeliver"/>; 0 when it is not Misdeliver.</summary>
    public uint RedSinceTick { get; internal set; }
}

/// <summary>
/// Read access to the aim preview's published state (the ADR-0016 <c>IDeliverPreviewRead</c> pattern; throwing-catching story 004).
/// </summary>
/// <example><code>ReadOnlySpan&lt;Vec3Mm&gt; arc = preview.Polyline;</code></example>
public interface IThrowPreviewRead
{
    /// <summary>The state published by the last client tick.</summary>
    ref readonly ThrowPreviewState Current { get; }

    /// <summary>The arc, <c>Ballistic.PositionAt(l, k)</c> for <c>k = 0 … EndAge</c>, the contact point last (<see cref="ThrowPreviewState.PointCount"/> points).</summary>
    ReadOnlySpan<Vec3Mm> Polyline { get; }

    /// <summary>Teammate hat pins (M0): always empty at M-1a.</summary>
    ReadOnlySpan<Vec3Mm> HatPins { get; }
}
