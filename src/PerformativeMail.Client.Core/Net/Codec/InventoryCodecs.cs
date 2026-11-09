using System.Runtime.InteropServices;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Players.Ops;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// Wire codecs for the client's Intake pickup and own-panel mirror (WP-F): the ops <c>Open</c>, <c>HotbarSelect</c>, <c>QuickMove</c>,
/// <c>TakeAll</c> and <c>Close</c>, and the targeted container records <c>Inserted</c>, <c>Removed</c>, <c>Merged</c>, <c>Moved</c>,
/// <c>SplitFrom</c> and <c>Version</c> (TD-140's kinds 0x0080-0x0085). Every one is fixed length: the Sim payload's bytes as they are
/// (little-endian, as <see cref="FixedLengthEventCodec"/>). The ids come from the Sim's descriptors.
/// </summary>
/// <example><code>var session = new HostSession(args, clock, [.. InventoryCodecs.CreateOps()], [.. InventoryCodecs.CreateEvents()]);</code></example>
public static class InventoryCodecs
{
    /// <summary>Creates the op codecs.</summary>
    /// <returns>A new array of the five op codecs.</returns>
    public static IOpCodec[] CreateOps() => new IOpCodec[]
    {
        new FixedLengthOpCodec<OpenPayload>(OpenOp.CodecId, new OpenPayload { Container = 1, ClaimedX = int.MinValue, ClaimedY = 1000, ClaimedZ = int.MaxValue }),
        new FixedLengthOpCodec<HotbarSelectPayload>(HotbarSelectOp.CodecId, new HotbarSelectPayload { Index = 7 }),
        new FixedLengthOpCodec<QuickMovePayload>(InvOpDescriptors.QuickMoveCodecId, new QuickMovePayload { From = 3, EntryId = 0x8000_0001u, Count = 255 }),
        new FixedLengthOpCodec<TakeAllPayload>(InvOpDescriptors.TakeAllCodecId, new TakeAllPayload { Container = 1, FilterKind = (byte)TakeAllFilter.Kind, FilterValue = uint.MaxValue }),
        new FixedLengthOpCodec<ClosePayload>(InvOpDescriptors.CloseCodecId, new ClosePayload { Container = uint.MaxValue }),
    };

    /// <summary>Creates the event codecs (a continuation chunk travels in its kind's codec).</summary>
    /// <returns>A new array of the six container-record codecs.</returns>
    public static IEventCodec[] CreateEvents() => new IEventCodec[]
    {
        new FixedLengthEventCodec(InvEventDescriptors.InsertedCodecId, Marshal.SizeOf<InvInsertedPayload>()),
        new FixedLengthEventCodec(InvEventDescriptors.RemovedCodecId, Marshal.SizeOf<InvRemovedPayload>()),
        new FixedLengthEventCodec(InvEventDescriptors.MergedCodecId, Marshal.SizeOf<InvMergedPayload>()),
        new FixedLengthEventCodec(InvEventDescriptors.MovedCodecId, Marshal.SizeOf<InvMovedPayload>()),
        new FixedLengthEventCodec(InvEventDescriptors.SplitFromCodecId, Marshal.SizeOf<InvSplitFromPayload>()),
        new FixedLengthEventCodec(InvEventDescriptors.VersionCodecId, Marshal.SizeOf<InvVersionPayload>()),
    };
}

/// <summary>
/// An op codec that sends the Sim payload <typeparamref name="T"/> as its raw bytes (its size is the wire size; every byte pattern is in
/// wire range, so the handler's checks judge the values). Decode refuses any other length. One sample: <paramref name="sample"/>.
/// </summary>
/// <typeparam name="T">The payload struct (Pack = 1, padding-free).</typeparam>
/// <param name="codecId">The descriptor's wire codec id.</param>
/// <param name="sample">A payload for the round-trip tests.</param>
/// <example><code>IOpCodec c = new FixedLengthOpCodec&lt;ClosePayload&gt;(16, new ClosePayload { Container = 1 });</code></example>
public sealed class FixedLengthOpCodec<T>(ushort codecId, T sample) : IOpCodec
    where T : unmanaged
{
    private static readonly int _size = Marshal.SizeOf<T>();

    /// <inheritdoc />
    public ushort CodecId { get; } = codecId;

    /// <inheritdoc />
    public byte WireSize => (byte)_size;

    /// <inheritdoc />
    public int SampleCount => 1;

    /// <inheritdoc />
    public bool TryDecode(ReadOnlySpan<byte> payload, ref OpPayload dst, out byte payloadLength)
    {
        payloadLength = 0;
        if (payload.Length != _size)
        {
            return false;
        }

        Span<byte> target = dst;
        target.Clear();
        payload.CopyTo(target);
        payloadLength = (byte)_size;
        return true;
    }

    /// <inheritdoc />
    public int Encode(in Op src, Span<byte> dst)
    {
        if (src.PayloadLength != _size || dst.Length < _size)
        {
            return -1;
        }

        Op copy = src;
        ReadOnlySpan<byte> payload = copy.Payload;
        payload[.._size].CopyTo(dst);
        return _size;
    }

    /// <inheritdoc />
    public Op GetSample(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(index, 0);
        var op = default(Op);
        op.SetPayload(sample);
        return op;
    }
}
