using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// Wire codecs for Manual Delivery's op (network-adapter-loopback story 008; ADR-0011 section 3 and erratum 2026-10-16; ADR-0009 erratum
/// 2026-10-16). The id comes from the Sim's descriptor (<see cref="DeliveryDescriptors.DeliverMatchingCodecId"/>).
/// </summary>
/// <example><code>var session = new HostSession(args, clock, DeliveryCodecs.CreateOps(), null);</code></example>
public static class DeliveryCodecs
{
    /// <summary>Codec id of <see cref="DeliverMatchingCodec"/>.</summary>
    public const ushort DeliverMatchingCodecId = DeliveryDescriptors.DeliverMatchingCodecId;

    /// <summary>Creates Delivery's op codecs.</summary>
    /// <returns>A new array holding the <c>DeliverMatching</c> codec.</returns>
    public static IOpCodec[] CreateOps() => new IOpCodec[] { new DeliverMatchingCodec() };
}

/// <summary>
/// <see cref="DeliveryOpKinds.DeliverMatching"/>: <c>mailboxId</c> u32 · <c>holdStartTick</c> u32 · <c>ClaimedPosition</c> x, y, z i32
/// [· <c>selectedEntryId</c> u32], little-endian, no flags byte: 20 B for MATCH and 24 B for MISDELIVER_ONE (24 / 28 B with the 4 B
/// envelope, transport framing excluded). Presence of <c>selectedEntryId</c> is implied by the length; decode writes the 24 B Sim
/// payload with <c>selectedEntryId</c> 0 for MATCH. A MISDELIVER_ONE payload carrying <c>selectedEntryId</c> 0 decodes as a failure
/// (0 means absent: <c>bad_request</c>). Encode writes the short form exactly when <c>selectedEntryId</c> is 0. Every other value is in
/// wire range; the mailbox, the tick and the claim are the handler's checks.
/// </summary>
/// <example><code>int n = new DeliverMatchingCodec().Encode(in op, buffer);   // 20 (MATCH) or 24 (MISDELIVER_ONE)</code></example>
public sealed class DeliverMatchingCodec : IShortFormOpCodec
{
    private const int MatchSize = 20;
    private const int MisdeliverOneSize = DeliveryDescriptors.DeliverMatchingWireSize;

    /// <inheritdoc />
    public ushort CodecId => DeliveryCodecs.DeliverMatchingCodecId;

    /// <inheritdoc />
    public byte WireSize => MisdeliverOneSize;

    /// <inheritdoc />
    public byte ShortWireSize => MatchSize;

    /// <inheritdoc />
    public int SampleCount => 3;

    /// <inheritdoc />
    public bool TryDecode(ReadOnlySpan<byte> payload, ref OpPayload dst, out byte payloadLength)
    {
        payloadLength = 0;
        if (payload.Length != MatchSize && payload.Length != MisdeliverOneSize)
        {
            return false;
        }

        uint selected = payload.Length == MisdeliverOneSize ? BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(MatchSize)) : 0u;
        if (payload.Length == MisdeliverOneSize && selected == 0)
        {
            return false;
        }

        var value = new DeliverMatchingPayload
        {
            MailboxId = BinaryPrimitives.ReadUInt32LittleEndian(payload),
            SelectedEntryId = selected,
            HoldStartTick = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4)),
            Claim = new ClaimedPosition(
                BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8)),
                BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(12)),
                BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(16))),
        };
        Span<byte> target = dst;
        MemoryMarshal.Write(target, in value);
        payloadLength = (byte)Unsafe.SizeOf<DeliverMatchingPayload>();
        return true;
    }

    /// <inheritdoc />
    public int Encode(in Op src, Span<byte> dst)
    {
        if (src.PayloadLength != Unsafe.SizeOf<DeliverMatchingPayload>())
        {
            return -1;
        }

        DeliverMatchingPayload value = src.PayloadAs<DeliverMatchingPayload>();
        int size = value.SelectedEntryId == 0 ? MatchSize : MisdeliverOneSize;
        if (dst.Length < size)
        {
            return -1;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(dst, value.MailboxId);
        BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(4), value.HoldStartTick);
        BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(8), value.Claim.X);
        BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(12), value.Claim.Y);
        BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(16), value.Claim.Z);
        if (size == MisdeliverOneSize)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(MatchSize), value.SelectedEntryId);
        }

        return size;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The samples are MISDELIVER_ONE requests (the descriptor's <c>WireSize</c>, as the shared round-trip Theory frames them): the
    /// minimum and maximum field values and a mixed-sign one. The MATCH form is covered by <c>DeliverMatchingCodecTests</c>.
    /// </remarks>
    public Op GetSample(int index)
    {
        var op = default(Op);
        switch (index)
        {
            case 0:
                op.SetPayload(new DeliverMatchingPayload
                {
                    MailboxId = 0, SelectedEntryId = 1, HoldStartTick = 0, Claim = new ClaimedPosition(int.MinValue, int.MinValue, int.MinValue),
                });
                break;
            case 1:
                op.SetPayload(new DeliverMatchingPayload
                {
                    MailboxId = 0x01020304, SelectedEntryId = 60, HoldStartTick = 1_000, Claim = new ClaimedPosition(1000, -250, -2500),
                });
                break;
            case 2:
                op.SetPayload(new DeliverMatchingPayload
                {
                    MailboxId = uint.MaxValue, SelectedEntryId = uint.MaxValue, HoldStartTick = uint.MaxValue,
                    Claim = new ClaimedPosition(int.MaxValue, int.MaxValue, int.MaxValue),
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        return op;
    }
}
