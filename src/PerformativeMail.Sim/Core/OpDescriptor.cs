using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// Registration record for one <see cref="OpKind"/> (ADR-0007 section 4):
/// <c>(Kind, PayloadLength, SystemOnly, WireCodecId, WireSize, Reasons)</c> plus the payload's CLR type so the
/// descriptor test can prove it is padding-free. Built once at composition; immutable. The descriptor-table
/// hash (protocol check) is computed by the network adapter from these fields.
/// </summary>
/// <example>
/// <code>
/// var d = OpDescriptor.For&lt;TeleportAckPayload&gt;(
///     OpKind.TeleportAck, systemOnly: false, wireCodecId: 4, wireSize: 4,
///     new ReasonDef(64, "stale_token"));
/// </code>
/// </example>
[ImmutableTable] // SC-ARCH-1 static-state rule: get-only/init-only registration record, shared as a static
public sealed class OpDescriptor
{
    /// <summary>Creates a descriptor from explicit fields. Prefer <see cref="For{T}"/>, which derives the length and type.</summary>
    /// <param name="kind">The op kind being registered.</param>
    /// <param name="payloadLength">Payload size in bytes (0 to 48).</param>
    /// <param name="systemOnly">True when only the <c>System</c> actor may issue the kind.</param>
    /// <param name="wireCodecId">Network codec id (assigned by the network epic).</param>
    /// <param name="wireSize">Encoded wire size in bytes.</param>
    /// <param name="reasons">System reason codes (64 and up) the handler may return; copied, so later changes to the array are not seen.</param>
    /// <param name="payloadType">The payload struct type, or <c>null</c> for a payload-free kind.</param>
    /// <remarks>The fields are stored as given: the shared descriptor test, not this constructor, validates length, padding, block and reasons.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="reasons"/> is null.</exception>
    public OpDescriptor(
        OpKind kind,
        byte payloadLength,
        bool systemOnly,
        ushort wireCodecId,
        byte wireSize,
        ReasonDef[] reasons,
        Type? payloadType = null)
    {
        Kind = kind;
        PayloadLength = payloadLength;
        SystemOnly = systemOnly;
        WireCodecId = wireCodecId;
        WireSize = wireSize;
        ArgumentNullException.ThrowIfNull(reasons);
        Reasons = ImmutableArray.Create(reasons);
        PayloadType = payloadType;
    }

    /// <summary>The op kind being registered.</summary>
    public OpKind Kind { get; }

    /// <summary>Payload size in bytes; must equal <c>Unsafe.SizeOf</c> of <see cref="PayloadType"/>.</summary>
    public byte PayloadLength { get; }

    /// <summary>True when only the <c>System</c> actor may issue the kind (others get <c>unknown_actor</c>).</summary>
    public bool SystemOnly { get; }

    /// <summary>Network codec id (assigned by the network epic).</summary>
    public ushort WireCodecId { get; }

    /// <summary>Encoded wire size in bytes.</summary>
    public byte WireSize { get; }

    /// <summary>System reason codes (64 and up) the handler may return; empty when it only uses <see cref="CoreReason"/>.</summary>
    public ImmutableArray<ReasonDef> Reasons { get; }

    /// <summary>The payload struct type, or <c>null</c> for a payload-free kind.</summary>
    public Type? PayloadType { get; }

    /// <summary>Creates a descriptor whose length and type come from <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The kind's <c>unmanaged</c>, padding-free payload struct (at most 48 bytes).</typeparam>
    /// <param name="kind">The op kind being registered.</param>
    /// <param name="systemOnly">True when only the <c>System</c> actor may issue the kind.</param>
    /// <param name="wireCodecId">Network codec id.</param>
    /// <param name="wireSize">Encoded wire size in bytes.</param>
    /// <param name="reasons">System reason codes (64 and up).</param>
    /// <returns>The descriptor; validity (padding, blocks, reasons) is checked by the shared descriptor test.</returns>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is larger than 48 bytes.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="reasons"/> is null.</exception>
    public static OpDescriptor For<T>(OpKind kind, bool systemOnly, ushort wireCodecId, byte wireSize, params ReasonDef[] reasons)
        where T : unmanaged
    {
        if (Unsafe.SizeOf<T>() > OpPayload.Size)
        {
            throw new ArgumentException($"Payload type {typeof(T).Name} is {Unsafe.SizeOf<T>()} B; the payload limit is {OpPayload.Size} B.");
        }

        return new OpDescriptor(kind, (byte)Unsafe.SizeOf<T>(), systemOnly, wireCodecId, wireSize, reasons, typeof(T));
    }

    /// <summary>Creates a descriptor for a payload-free kind (length 0, no type).</summary>
    /// <param name="kind">The op kind being registered.</param>
    /// <param name="systemOnly">True when only the <c>System</c> actor may issue the kind.</param>
    /// <param name="wireCodecId">Network codec id.</param>
    /// <param name="wireSize">Encoded wire size in bytes.</param>
    /// <param name="reasons">System reason codes (64 and up).</param>
    /// <returns>The descriptor.</returns>
    public static OpDescriptor Empty(OpKind kind, bool systemOnly, ushort wireCodecId, byte wireSize, params ReasonDef[] reasons)
    {
        return new OpDescriptor(kind, 0, systemOnly, wireCodecId, wireSize, reasons);
    }
}
