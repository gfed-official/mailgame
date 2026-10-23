using System.Runtime.InteropServices;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// The local interaction after <see cref="EdgeResolveStep"/> (ADR-0014 sections 3 and 7): the state, the latch and the last
/// cancel with its reason key. Values only, no bool. Written by the press (story 016) and the hold step (story 017); story 018
/// copies it into the <c>InteractionSnapshot</c>.
/// </summary>
/// <example><code>ref readonly InteractionStatus s = ref edgeResolve.Interaction; if (s.State == InteractState.Holding) { int n = s.Latch.HoldTicks; }</code></example>
[StructLayout(LayoutKind.Sequential)]
public struct InteractionStatus
{
    /// <summary>The interaction state.</summary>
    public InteractState State { get; internal set; }

    /// <summary>The latch while <see cref="State"/> is Holding or Channel (a Tap's latch is transient and never stored here).</summary>
    public InteractionLatch Latch { get; internal set; }

    /// <summary>The last cancel or press refusal (<see cref="CancelReason.Unavailable"/>, <see cref="CancelReason.Busy"/>; story 017's cancels).</summary>
    public CancelReason LastCancel { get; internal set; }

    /// <summary>The reason key of <see cref="LastCancel"/> (the unavailable target's, for the prompt pulse); <see cref="StringId.None"/> otherwise.</summary>
    public StringId LastCancelReason { get; internal set; }
}
