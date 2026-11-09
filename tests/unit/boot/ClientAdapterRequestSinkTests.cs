using System.Buffers.Binary;
using System.Runtime.InteropServices;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Game.Boot;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Tests.Unit.Boot;

/// <summary>
/// WP-F regression: the host client's request sink sent the op's in-memory payload as its wire bytes. <c>DeliverMatching</c>'s wire
/// order (mailbox, hold start, claim[, selected entry]) differs from its struct (mailbox, selected entry, hold start, claim), so every
/// hand delivery reached the host with the claim shifted by one field and was refused <c>out_of_range</c>.
/// </summary>
public class ClientAdapterRequestSinkTests
{
    [Fact]
    public void TrySend_DeliverMatching_TravelsInItsWireLayout_AndDecodesToTheSamePayload()
    {
        // Arrange
        var transport = new RecordingTransport();
        var adapter = new ClientAdapter(transport, new PeerHandle(1), new EventCodecRegistry(ReadOnlySpan<EventDescriptor>.Empty, []));
        var codecs = new OpCodecRegistry([DeliveryDescriptors.DeliverMatching], DeliveryCodecs.CreateOps());
        var sink = new ClientAdapterRequestSink(adapter, codecs);
        var sent = new DeliverMatchingPayload { MailboxId = 77, SelectedEntryId = 0, HoldStartTick = 556, Claim = new ClaimedPosition(76_048, 1_000, 37_220) };
        var op = new Op { Kind = DeliveryOpKinds.DeliverMatching };
        op.SetPayload(sent);

        // Act
        bool accepted = sink.TrySend(in op);

        // Assert: the request envelope's payload decodes, through the host's codec, to what the client built.
        Assert.True(accepted);
        byte[] envelope = Assert.Single(transport.Sent);
        var decoded = default(OpPayload);
        Assert.True(new DeliverMatchingCodec().TryDecode(envelope.AsSpan(RequestEnvelope.HeaderSize), ref decoded, out byte length));
        Span<byte> bytes = decoded;
        DeliverMatchingPayload got = MemoryMarshal.Read<DeliverMatchingPayload>(bytes[..length]);
        Assert.Equal((sent.MailboxId, sent.SelectedEntryId, sent.HoldStartTick), (got.MailboxId, got.SelectedEntryId, got.HoldStartTick));
        Assert.Equal((sent.Claim.X, sent.Claim.Y, sent.Claim.Z), (got.Claim.X, got.Claim.Y, got.Claim.Z));
    }

    [Fact]
    public void TrySend_TakeAll_TravelsAsTheAdrNineByteLayout_AndDecodesToTheSamePayload()
    {
        // Arrange
        var transport = new RecordingTransport();
        var adapter = new ClientAdapter(transport, new PeerHandle(1), new EventCodecRegistry(ReadOnlySpan<EventDescriptor>.Empty, []));
        var codecs = new OpCodecRegistry([InvOpDescriptors.TakeAll], InventoryCodecs.CreateOps());
        var sink = new ClientAdapterRequestSink(adapter, codecs);
        var sent = new TakeAllPayload { Container = 1, FilterKind = (byte)TakeAllFilter.Street, FilterValue = 0x01020304 };
        var op = new Op { Kind = InvOpKinds.TakeAll };
        op.SetPayload(sent);

        // Act
        bool accepted = sink.TrySend(in op);

        // Assert: ADR-0009 l.88, container u32, filterKind u8, filterValue u32 (9 B), little-endian, no pad.
        Assert.True(accepted);
        byte[] envelope = Assert.Single(transport.Sent);
        ReadOnlySpan<byte> wire = envelope.AsSpan(RequestEnvelope.HeaderSize);
        Assert.Equal(9, wire.Length);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(wire));
        Assert.Equal((byte)TakeAllFilter.Street, wire[4]);
        Assert.Equal(0x01020304u, BinaryPrimitives.ReadUInt32LittleEndian(wire[5..]));

        // Assert: it decodes, through the host's codec, to what the client built.
        var decoded = default(OpPayload);
        Assert.True(codecs.TryGet(InvOpKinds.TakeAll, out _, out IOpCodec? codec));
        Assert.True(codec.TryDecode(wire, ref decoded, out byte length));
        Span<byte> bytes = decoded;
        TakeAllPayload got = MemoryMarshal.Read<TakeAllPayload>(bytes[..length]);
        Assert.Equal((sent.Container, sent.FilterKind, sent.FilterValue), (got.Container, got.FilterKind, got.FilterValue));
    }

    private sealed class RecordingTransport : ITransport
    {
        public List<byte[]> Sent { get; } = [];

        public void Poll(ITransportSink sink)
        {
        }

        public void Send(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> bytes) => Sent.Add(bytes.ToArray());

        public void Disconnect(PeerHandle peer, DisconnectReason reason)
        {
        }
    }
}
