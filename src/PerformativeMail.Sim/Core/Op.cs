using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>The fixed 48-byte payload area of an <see cref="Op"/>. Access it through <see cref="Op.PayloadAs{T}"/> and <see cref="Op.SetPayload{T}"/>.</summary>
[InlineArray(Size)]
public struct OpPayload
{
    /// <summary>Payload size in bytes.</summary>
    public const int Size = 48;

#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
    private byte _e0;
#pragma warning restore CS0169
}

/// <summary>
/// One queued request: a 64-byte blittable value (ADR-0007 section 1, TR-sim-015). A 16-byte header
/// (<see cref="OpSeq"/>, <see cref="Actor"/>, <see cref="Kind"/>, <see cref="PayloadLength"/>, a zero
/// <see cref="Reserved"/> byte, <see cref="ReqId"/>) plus a 48-byte payload. Each system owns an
/// <c>unmanaged</c>, padding-free payload struct (at most 48 bytes, no references, no <c>Int128</c>) and reads it
/// with <see cref="PayloadAs{T}"/> / writes it with <see cref="SetPayload{T}"/>; both are unaligned because the
/// payload sits at offset 16 (4-byte aligned only). Unused payload bytes and <see cref="Reserved"/> are zero.
/// The actor is only ever read from <see cref="Actor"/>, never from a payload. Not thread-safe (plain value).
/// </summary>
/// <example>
/// <code>
/// var op = new Op { Kind = OpKind.TeleportAck, ReqId = 5 };
/// op.SetPayload(new MyAckPayload { Token = 9 });
/// MyAckPayload p = op.PayloadAs&lt;MyAckPayload&gt;();   // Token == 9
/// </code>
/// </example>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Op
{
    /// <summary>Total size in bytes (asserted by a test).</summary>
    public const int SizeBytes = 64;

    /// <summary>Global arrival order, stamped by Sim Core when the op is enqueued (Story 014).</summary>
    public uint OpSeq;

    /// <summary>Who issued the op, set by Sim Core from the enqueue parameter (never read from the payload).</summary>
    public ActorId Actor;

    /// <summary>What the op asks for.</summary>
    public OpKind Kind;

    /// <summary>Number of meaningful payload bytes; equals the descriptor's <c>PayloadLength</c>.</summary>
    public byte PayloadLength;

    /// <summary>Reserved; always zero.</summary>
    public byte Reserved;

    /// <summary>Caller-chosen request id echoed in the <see cref="Reply"/>. Sim Core never dedupes on it.</summary>
    public uint ReqId;

    /// <summary>The payload bytes; bytes at and after <see cref="PayloadLength"/> are zero.</summary>
    public OpPayload Payload;

    /// <summary>
    /// Reads the payload as <typeparamref name="T"/> (unaligned, no allocation). <c>Unsafe.SizeOf&lt;T&gt;()</c> must
    /// equal <see cref="PayloadLength"/>. Asserts under SIM_ASSERT otherwise; in Release a type of at most 48 bytes
    /// reads its leading bytes (zero past the stored length) and a larger type returns <c>default</c>.
    /// </summary>
    /// <typeparam name="T">The system's padding-free <c>unmanaged</c> payload struct.</typeparam>
    /// <returns>The payload value.</returns>
    /// <example>
    /// <code>
    /// var join = op.PayloadAs&lt;JoinPayload&gt;();
    /// </code>
    /// </example>
    public readonly T PayloadAs<T>()
        where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        SimAssert.That(size == PayloadLength, SimAssertCode.OpPayloadLength);
        if (size > OpPayload.Size)
        {
            return default;
        }

        ReadOnlySpan<byte> span = Payload;
        return MemoryMarshal.Read<T>(span);
    }

    /// <summary>
    /// Zero-fills the payload, writes <paramref name="value"/> unaligned at offset 0 and sets
    /// <see cref="PayloadLength"/> to its size. A type over 48 bytes asserts under SIM_ASSERT; in Release the
    /// payload is left zero and <see cref="PayloadLength"/> is 0 (the drain then refuses the op as <c>unknown_kind</c>).
    /// <para>
    /// <paramref name="value"/> is copied before the payload is cleared, so it may alias this op's own payload bytes.
    /// This method mutates the op it is called on: call it on a <c>ref</c> local, an array element or a field, never on
    /// a by-value copy (a <c>foreach</c> variable, a returned <see cref="Op"/> or a lambda capture), or the write is lost.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The system's padding-free <c>unmanaged</c> payload struct.</typeparam>
    /// <param name="value">The payload to store.</param>
    /// <example>
    /// <code>
    /// op.SetPayload(new JoinPayload { Name = nameId });
    /// </code>
    /// </example>
    public void SetPayload<T>(in T value)
        where T : unmanaged
    {
        T copy = value;
        int size = Unsafe.SizeOf<T>();
        SimAssert.That(size <= OpPayload.Size, SimAssertCode.OpPayloadTooLarge);
        Payload = default;
        if (size > OpPayload.Size)
        {
            PayloadLength = 0;
            return;
        }

        Span<byte> span = Payload;
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(span), copy);
        PayloadLength = (byte)size;
    }
}
