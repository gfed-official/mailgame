using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>The fixed 24-byte extra area of a <see cref="Reply"/>. Access it through <see cref="Reply.ExtraAs{T}"/> and <see cref="Reply.SetExtra{T}"/>.</summary>
[InlineArray(Size)]
public struct ReplyExtra
{
    /// <summary>Extra size in bytes.</summary>
    public const int Size = 24;

#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
    private byte _e0;
#pragma warning restore CS0169
}

/// <summary>
/// A handler's answer to one <see cref="Op"/> (ADR-0007 section 5, TR-sim-020): 36 bytes, <c>Pack = 1</c>,
/// padding-free (three explicit zero pad bytes). A handler returns exactly one; Sim Core appends it to the
/// event log after every event the handler raised (Story 020). <c>Ok</c> if and only if <see cref="Reason"/> is 0;
/// a partial result is <c>Ok</c> with details in <see cref="Extra"/>. Sim Core never dedupes on <see cref="ReqId"/>.
/// Sim Core reasons 1 to 63 come only from <see cref="CoreReason"/>; system reasons start at 64. Plain value,
/// not thread-safe.
/// </summary>
/// <example>
/// <code>
/// Reply Apply(in Op op, in TickContext ctx) =>
///     CanDo(op) ? Reply.Ok(in op) : Reply.Reject(in op, CoreReason.BadRequest);
/// </code>
/// </example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Reply
{
    /// <summary>Total size in bytes (asserted by a test).</summary>
    public const int SizeBytes = 36;

    /// <summary>The request id copied from the op.</summary>
    public uint ReqId;

    /// <summary>The kind copied from the op.</summary>
    public OpKind Kind;

    /// <summary>0 for success; otherwise a <see cref="CoreReason"/> (1 to 63) or a system reason (64 and up).</summary>
    public ushort Reason;

    /// <summary>Coarse outcome; <see cref="ReplyResult.Ok"/> exactly when <see cref="Reason"/> is 0.</summary>
    public ReplyResult Result;

    /// <summary>Explicit padding; always zero.</summary>
    public byte Pad0;

    /// <summary>Explicit padding; always zero.</summary>
    public byte Pad1;

    /// <summary>Explicit padding; always zero.</summary>
    public byte Pad2;

    /// <summary>Result details; bytes not written by <see cref="SetExtra{T}"/> are zero.</summary>
    public ReplyExtra Extra;

    /// <summary>True exactly when <see cref="Reason"/> is 0.</summary>
    public readonly bool IsOk => Reason == 0;

    /// <summary>Builds the success reply for an op: <c>ReqId</c> and <c>Kind</c> copied, reason 0.</summary>
    /// <param name="op">The op being answered.</param>
    /// <returns>An <c>Ok</c> reply with a zero <see cref="Extra"/>.</returns>
    /// <example>
    /// <code>
    /// return Reply.Ok(in op);
    /// </code>
    /// </example>
    public static Reply Ok(in Op op)
    {
        var reply = default(Reply);
        reply.ReqId = op.ReqId;
        reply.Kind = op.Kind;
        reply.Reason = 0;
        reply.Result = ReplyResult.Ok;
        return reply;
    }

    /// <summary>Builds the success reply for an op with result details in <see cref="Extra"/>.</summary>
    /// <typeparam name="T">A padding-free <c>unmanaged</c> struct of at most 24 bytes.</typeparam>
    /// <param name="op">The op being answered.</param>
    /// <param name="extra">The details to store (see <see cref="SetExtra{T}"/> for the over-size behaviour).</param>
    /// <returns>An <c>Ok</c> reply carrying <paramref name="extra"/>.</returns>
    /// <example>
    /// <code>
    /// return Reply.Ok(in op, new PartialInfo { Moved = 3 });
    /// </code>
    /// </example>
    public static Reply Ok<T>(in Op op, in T extra)
        where T : unmanaged
    {
        Reply reply = Ok(in op);
        reply.SetExtra(in extra);
        return reply;
    }

    /// <summary>
    /// Builds a refusal for an op. <paramref name="reason"/> must be a <see cref="CoreReason"/> from 1 to
    /// <see cref="CoreReason.BadRequest"/> or a system reason of <see cref="CoreReason.FirstSystemReason"/> and up.
    /// Reason 0 and the reserved range 9 to 63 assert under SIM_ASSERT; in Release both become
    /// <see cref="CoreReason.BadRequest"/> so that <c>Ok &lt;=&gt; Reason == 0</c> always holds and no reserved code escapes.
    /// </summary>
    /// <param name="op">The op being answered.</param>
    /// <param name="reason">A <see cref="CoreReason"/> (1 to 63) or a system reason declared in the descriptor (64 and up).</param>
    /// <returns>A <c>Rejected</c> reply with a zero <see cref="Extra"/>.</returns>
    /// <example>
    /// <code>
    /// return Reply.Reject(in op, CoreReason.UnknownActor);
    /// </code>
    /// </example>
    public static Reply Reject(in Op op, ushort reason)
    {
        SimAssert.That(reason != 0, SimAssertCode.ReplyRejectZeroReason);
        SimAssert.That(reason <= CoreReason.BadRequest || reason >= CoreReason.FirstSystemReason, SimAssertCode.ReplyRejectReservedReason);
        bool legal = reason != 0 && (reason <= CoreReason.BadRequest || reason >= CoreReason.FirstSystemReason);
        var reply = default(Reply);
        reply.ReqId = op.ReqId;
        reply.Kind = op.Kind;
        reply.Reason = legal ? reason : CoreReason.BadRequest;
        reply.Result = ReplyResult.Rejected;
        return reply;
    }

    /// <summary>
    /// Zero-fills <see cref="Extra"/> and writes <paramref name="value"/> unaligned at offset 0. A type over 24 bytes
    /// asserts under SIM_ASSERT; in Release <see cref="Extra"/> is left zero.
    /// <para>
    /// <paramref name="value"/> is copied before <see cref="Extra"/> is cleared, so it may alias this reply's own bytes.
    /// This method mutates the reply it is called on: call it on a <c>ref</c> local or a variable, never on a by-value
    /// copy such as <c>Reply.Ok(in op).SetExtra(...)</c> (use <see cref="Ok{T}"/> instead) or a <c>foreach</c> variable.
    /// </para>
    /// </summary>
    /// <typeparam name="T">A padding-free <c>unmanaged</c> struct of at most 24 bytes.</typeparam>
    /// <param name="value">The details to store.</param>
    /// <example>
    /// <code>
    /// var reply = Reply.Ok(in op);
    /// reply.SetExtra(new PartialInfo { Moved = 3 });
    /// </code>
    /// </example>
    public void SetExtra<T>(in T value)
        where T : unmanaged
    {
        T copy = value;
        int size = Unsafe.SizeOf<T>();
        SimAssert.That(size <= ReplyExtra.Size, SimAssertCode.ReplyExtraTooLarge);
        Extra = default;
        if (size > ReplyExtra.Size)
        {
            return;
        }

        Span<byte> span = Extra;
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(span), copy);
    }

    /// <summary>
    /// Reads <see cref="Extra"/> as <typeparamref name="T"/> (unaligned, no allocation). A type over 24 bytes
    /// asserts under SIM_ASSERT; in Release it returns <c>default</c>.
    /// </summary>
    /// <typeparam name="T">A padding-free <c>unmanaged</c> struct of at most 24 bytes.</typeparam>
    /// <returns>The details value.</returns>
    /// <example>
    /// <code>
    /// PartialInfo info = reply.ExtraAs&lt;PartialInfo&gt;();
    /// </code>
    /// </example>
    public readonly T ExtraAs<T>()
        where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        SimAssert.That(size <= ReplyExtra.Size, SimAssertCode.ReplyExtraRead);
        if (size > ReplyExtra.Size)
        {
            return default;
        }

        ReadOnlySpan<byte> span = Extra;
        return MemoryMarshal.Read<T>(span);
    }
}
