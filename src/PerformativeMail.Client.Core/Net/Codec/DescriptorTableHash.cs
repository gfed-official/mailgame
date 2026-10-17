using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// The protocol-version check (ADR-0011 section 3): a hash over the frozen world's op and event descriptor tables, so a host and a
/// client built from different descriptors (kinds, payload lengths, <c>SystemOnly</c>, replication class, codec ids, wire sizes) can
/// detect the mismatch. Descriptors are folded in ascending kind order, ops then events, each field as fixed-width little-endian
/// values, together with the table sizes. Reasons and payload CLR types are not part of the wire contract and are not hashed.
/// <para>
/// This is a 64-bit <see cref="SplitMix.Mix64"/> fold: it is deterministic across runs, platforms and builds (no
/// <c>GetHashCode</c>), and it detects accidental divergence; it is not collision-resistant against an adversary. No hand-written
/// SHA-256 exists in the repo and <c>System.Security.Cryptography</c> is forbidden in Client.Core, so the M0 Hello/HelloAck
/// handshake moves the check to SHA-256 (carried next to the content hash).
/// </para>
/// </summary>
/// <example>
/// <code>
/// ulong mine = DescriptorTableHash.Compute(world);
/// bool compatible = mine == hostHashFromHandshake;
/// </code>
/// </example>
public static class DescriptorTableHash
{
    private const ulong Seed = 0x5045524D41494C31UL; // "PERMAIL1": bump the trailing digit when the folded layout changes

    /// <summary>Computes the hash of a frozen world's op and event descriptor tables.</summary>
    /// <param name="world">The frozen world.</param>
    /// <returns>The 64-bit table hash.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    public static ulong Compute(SimWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Compute(world.OpDescriptors, world.EventDescriptors);
    }

    /// <summary>Computes the hash of explicit tables, which must be ascending by kind (as <c>SimWorld</c> exposes them).</summary>
    /// <param name="ops">Op descriptors, ascending by kind.</param>
    /// <param name="events">Event descriptors, ascending by kind.</param>
    /// <returns>The 64-bit table hash.</returns>
    public static ulong Compute(ReadOnlySpan<OpDescriptor> ops, ReadOnlySpan<EventDescriptor> events)
    {
        ulong h = Seed;
        h = Fold(h, (ulong)ops.Length);
        for (int i = 0; i < ops.Length; i++)
        {
            OpDescriptor d = ops[i];
            h = Fold(h, (ushort)d.Kind | ((ulong)d.PayloadLength << 16) | ((d.SystemOnly ? 1UL : 0UL) << 24));
            h = Fold(h, d.WireCodecId | ((ulong)d.WireSize << 16));
        }

        h = Fold(h, (ulong)events.Length);
        for (int i = 0; i < events.Length; i++)
        {
            EventDescriptor d = events[i];
            h = Fold(h, (ushort)d.Kind | ((ulong)(byte)d.Class << 16) | ((ulong)d.PayloadLength << 24) | ((d.Coalescible ? 1UL : 0UL) << 32) | ((ulong)d.ResyncScope << 40));
            h = Fold(h, d.WireCodecId | ((ulong)d.WireSize << 16));
        }

        return h;
    }

    private static ulong Fold(ulong state, ulong word) => SplitMix.Mix64(state ^ word);
}
