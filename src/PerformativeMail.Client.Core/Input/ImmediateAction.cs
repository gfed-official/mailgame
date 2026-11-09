using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>Flag bits of an <see cref="ImmediateAction"/> (story 013; additive to ADR-0012 section 6's field list).</summary>
[Flags]
public enum ImmediateFlags : byte
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>A Pick / place release that places (F7, set by story 010's click-or-drag resolver).</summary>
    PlaceOnRelease = 1,
}

/// <summary>
/// One immediate verb (ADR-0012 section 6, GDD R11): queued by <see cref="InputRouter.Feed"/> at the action's firing edge and
/// handed to the action's <see cref="IImmediateActionSink"/> by <see cref="InputRouter.DispatchImmediates"/>. Carries no
/// payload: the sink builds its request from ClientLayer's published state. A value type with no references.
/// </summary>
/// <example><code>if (a.ActionId == ActionId.Drop &amp;&amp; a.EdgeKind == EdgeKind.Press) { /* build the drop op */ }</code></example>
/// <param name="ActionId">The action that fired.</param>
/// <param name="EdgeKind">The firing edge: <see cref="EdgeKind.Press"/> for a tap.</param>
/// <param name="ArrivalMs">The arrival time of the firing record (the <see cref="IInputClock"/> domain).</param>
/// <param name="RouterStateId">The router state the edge was gated against.</param>
/// <param name="PresentedSeq">The <c>Seq</c> of the prompt presented at the edge; 0 when nothing was presented.</param>
/// <param name="Flags">The flag bits.</param>
public readonly record struct ImmediateAction(
    ushort ActionId,
    EdgeKind EdgeKind,
    long ArrivalMs,
    ushort RouterStateId,
    ushort PresentedSeq,
    ImmediateFlags Flags);
