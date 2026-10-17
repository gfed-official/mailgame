namespace PerformativeMail.Sim.Core;

/// <summary>
/// Applies one kind of <see cref="Op"/> (ADR-0007 section 3). Called by the drain (Story 020) on the Sim tick
/// thread, once per op of <see cref="Kind"/>. A handler validates fully before its first mutation, id allocation,
/// RNG draw or event raise (a reject leaves state untouched), returns exactly one <see cref="Reply"/>, never
/// appends its own reply and never stores or compares <see cref="Op.ReqId"/>.
/// </summary>
/// <example>
/// <code>
/// sealed class LeaveHandler : IOpHandler
/// {
///     public OpKind Kind =&gt; OpKind.PlayerLeave;
///     public Reply Apply(in Op op, in TickContext ctx) =&gt; Reply.Ok(in op);
/// }
/// </code>
/// </example>
public interface IOpHandler
{
    /// <summary>The kind this handler applies.</summary>
    OpKind Kind { get; }

    /// <summary>Applies the op.</summary>
    /// <param name="op">The drained op (actor already pre-checked by Sim Core).</param>
    /// <param name="ctx">The tick and phase being run.</param>
    /// <returns>Exactly one reply.</returns>
    Reply Apply(in Op op, in TickContext ctx);
}
