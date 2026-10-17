using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// Declaration of one <see cref="EventKind"/> (ADR-0008 sections 1 and 3):
/// <c>(Kind, Class, PayloadLength, Coalescible, ResyncScope, WireCodecId, WireSize)</c>, plus the payload's CLR type
/// so the shared descriptor test can prove it is padding-free. Built once at composition and bound with
/// <see cref="SimWorldBuilder.BindEventKind"/>; immutable. <see cref="Coalescible"/> and <see cref="ResyncScope"/> are
/// carried for the later coalescing and overflow stories and are not read by Sim Core yet.
/// </summary>
/// <param name="Kind">The kind being declared.</param>
/// <param name="Class">Who the kind is delivered to.</param>
/// <param name="PayloadLength">Payload size in bytes (0 to 48).</param>
/// <param name="Coalescible">True when the owner may rewrite an entry of this kind within one call.</param>
/// <param name="ResyncScope">Resync scope bit index (0 to 63) a dropped entry of this kind sets.</param>
/// <param name="WireCodecId">Network codec id (assigned by the network epic).</param>
/// <param name="WireSize">Encoded wire size in bytes.</param>
/// <example>
/// <code>
/// var d = EventDescriptor.For&lt;MovedPayload&gt;(EventKind.OpReply, ReplicationClass.Replicated, false, 3, 1, 12);
/// builder.BindEventKind(d);
/// </code>
/// </example>
[ImmutableTable] // SC-ARCH-1 static-state rule: get-only/init-only registration record, shared as a static
public sealed record EventDescriptor(
    EventKind Kind,
    ReplicationClass Class,
    byte PayloadLength,
    bool Coalescible,
    byte ResyncScope,
    ushort WireCodecId,
    byte WireSize)
{
    /// <summary>The payload struct type, or <c>null</c> for a payload-free kind. Test metadata: lets the descriptor test check padding. Not part of the wire contract.</summary>
    public Type? PayloadType { get; init; }

    /// <summary>Creates a descriptor whose length and type come from <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The kind's <c>unmanaged</c>, padding-free payload struct (at most 48 bytes).</typeparam>
    /// <param name="kind">The kind being declared.</param>
    /// <param name="replicationClass">Who the kind is delivered to.</param>
    /// <param name="coalescible">True when the owner may rewrite an entry within one call.</param>
    /// <param name="resyncScope">Resync scope bit index (0 to 63).</param>
    /// <param name="wireCodecId">Network codec id.</param>
    /// <param name="wireSize">Encoded wire size in bytes.</param>
    /// <returns>The descriptor; validity (padding, block, scope) is checked by the shared descriptor test and, for the basics, at bind.</returns>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is larger than 48 bytes.</exception>
    /// <example>
    /// <code>
    /// EventDescriptor d = EventDescriptor.For&lt;Reply&gt;(EventKind.OpReply, ReplicationClass.OwnerOnly, false, 0, 0, 36);
    /// </code>
    /// </example>
    public static EventDescriptor For<T>(
        EventKind kind,
        ReplicationClass replicationClass,
        bool coalescible,
        byte resyncScope,
        ushort wireCodecId,
        byte wireSize)
        where T : unmanaged
    {
        if (Unsafe.SizeOf<T>() > EventPayload.Size)
        {
            throw new ArgumentException($"Payload type {typeof(T).Name} is {Unsafe.SizeOf<T>()} B; the payload limit is {EventPayload.Size} B.");
        }

        return new EventDescriptor(kind, replicationClass, (byte)Unsafe.SizeOf<T>(), coalescible, resyncScope, wireCodecId, wireSize)
        {
            PayloadType = typeof(T),
        };
    }
}

/// <summary>The descriptors Sim Core declares for its own <see cref="EventKind"/>s. <see cref="SimWorldBuilder"/> binds them by default.</summary>
/// <example>
/// <code>
/// EventDescriptor reply = CoreEventDescriptors.OpReply;
/// </code>
/// </example>
public static class CoreEventDescriptors
{
    /// <summary>
    /// <see cref="EventKind.OpReply"/>: <see cref="ReplicationClass.OwnerOnly"/> to the actor's slot (audience 0 for a
    /// reply to <c>System</c>), payload <see cref="Reply"/> (36 B), not coalescible. The resync scope is a placeholder 0:
    /// ADR-0008 section 6 sets a dropped reply's scope from the op kind it answers (Story 016). The codec id is 0 until
    /// the network epic assigns one.
    /// </summary>
    public static EventDescriptor OpReply { get; } =
        EventDescriptor.For<Reply>(EventKind.OpReply, ReplicationClass.OwnerOnly, coalescible: false, resyncScope: 0, wireCodecId: 0, wireSize: Reply.SizeBytes);
}
