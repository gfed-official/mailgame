using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// Sim Core's own dump section, <see cref="SectionIds.SimCore"/> (ADR-0006 section 4), always first. Layout, in order, all
/// little-endian, every field written one by one:
/// <list type="number">
/// <item><description><c>CurrentTick</c> u32.</description></item>
/// <item><description>Entity id counters (last issued, 0 = none) for classes 0x01 to 0x08, ascending: 8 x u32.</description></item>
/// <item><description>Test-class counters: u32 count, then (class u8, counter u32) for each non-zero class 0xF0 to 0xFF ascending.</description></item>
/// <item><description>Mail id counter (last issued) u32.</description></item>
/// <item><description><c>opSeq</c> counter (last issued) u32.</description></item>
/// <item><description>Live RNG streams: u32 count, then per stream <c>nameOrdinal</c> u16, <c>index</c> u32, <c>state</c> u64, <c>inc</c> u64, <c>draws</c> u64, in <see cref="RngNames"/> ordinal order then index. Never-created streams are absent.</description></item>
/// <item><description>Queued ops: u32 count, then per op in ring (<c>opSeq</c>) order <c>OpSeq</c> u32, <c>Actor</c> u32, <c>Kind</c> u16, <c>PayloadLength</c> u8, <c>Reserved</c> u8, <c>ReqId</c> u32, 48 payload bytes, boundary tick u32.</description></item>
/// <item><description>Transform-sample latch block (Story 021): per slot 0 to 7 in order, <c>hasSeq</c> u8 then last accepted <c>Seq</c> u32 (8 x 5 bytes); the latch's <c>ReceivedTick</c> u32; sample count u32; then per sample in latch order (slot, then seq) its fields via <see cref="TransformSample.WriteCanonical"/>: <c>Seq</c> u32, <c>PosXMm</c> i32, <c>PosYMm</c> i32, <c>PosZMm</c> i32, <c>YawBam</c> u16, <c>PitchBam</c> u16, <c>Slot</c> u8, <c>Flags</c> u8, <c>Pad0</c> u8, <c>Pad1</c> u8. Samples still pending in the ring between ticks are NOT dumped: the recorder captures a sample at the drain, so replay reproduces it, and a dump then depends only on drained state.</description></item>
/// <item><description>Slot table: 8 entries in slot order, each player id u32, freed tick u32, occupied u8.</description></item>
/// </list>
/// Excluded: counters, the check ring, perf data, caches and the event log (empty between ticks).
/// </summary>
internal sealed class SimCoreStateContributor : ISimStateContributor
{
    private const byte FirstClass = (byte)EntityClass.Player;
    private const byte LastClass = (byte)EntityClass.Prop;

    private readonly SimWorld _world;
    private readonly SimCoreStateSources? _sources;
    private readonly RngStream[] _rngScratch;

    public SimCoreStateContributor(SimWorld world, SimCoreStateSources? sources)
    {
        _world = world;
        _sources = sources;
        _rngScratch = new RngStream[sources?.Rng?.HandleCount ?? 0];
    }

    public ushort SectionId => SectionIds.SimCore;

    public void Write(ref CanonicalWriter w)
    {
        w.WriteU32(_world.CurrentTick);
        WriteEntityCounters(ref w);
        w.WriteU32(_sources?.Mail?.Peek() ?? 0u);
        w.WriteU32(_world.LastOpSeq);
        WriteRngStreams(ref w);
        WriteQueuedOps(ref w);
        WriteSampleLatch(ref w);
        WriteSlotTable(ref w);
    }

    private void WriteEntityCounters(ref CanonicalWriter w)
    {
        EntityIds? ids = _sources?.Ids;
        for (int c = FirstClass; c <= LastClass; c++)
        {
            w.WriteU32(ids?.Peek((EntityClass)c) ?? 0u);
        }

        uint testClasses = 0;
        for (int c = (int)EntityClass.TestFirst; c <= (int)EntityClass.TestLast; c++)
        {
            if ((ids?.Peek((EntityClass)c) ?? 0u) != 0u)
            {
                testClasses++;
            }
        }

        w.WriteU32(testClasses);
        for (int c = (int)EntityClass.TestFirst; c <= (int)EntityClass.TestLast; c++)
        {
            uint last = ids?.Peek((EntityClass)c) ?? 0u;
            if (last != 0u)
            {
                w.WriteU8((byte)c);
                w.WriteU32(last);
            }
        }
    }

    private void WriteRngStreams(ref CanonicalWriter w)
    {
        RngRegistry? rng = _sources?.Rng;
        int live = rng is null ? 0 : rng.CopyLive(_rngScratch);

        // CopyLive yields RngNames ordinal order then ascending index, the order the format requires.
        w.WriteU32((uint)live);
        for (int i = 0; i < live; i++)
        {
            RngStream stream = _rngScratch[i];
            w.WriteU16(stream.Name.Ordinal);
            w.WriteU32(stream.Index);
            w.WriteU64(stream.State);
            w.WriteU64(stream.Inc);
            w.WriteU64(stream.Draws);
        }

        Array.Clear(_rngScratch, 0, live);
    }

    private void WriteQueuedOps(ref CanonicalWriter w)
    {
        OpQueueView ops = _world.Ops;
        w.WriteU32((uint)ops.Count);
        for (int i = 0; i < ops.Count; i++)
        {
            ref readonly Op op = ref ops.PeekAt(i);
            w.WriteU32(op.OpSeq);
            w.WriteU32(op.Actor.Value);
            w.WriteU16((ushort)op.Kind);
            w.WriteU8(op.PayloadLength);
            w.WriteU8(op.Reserved);
            w.WriteU32(op.ReqId);
            ReadOnlySpan<byte> payload = op.Payload;
            w.WriteBytes(payload);
            w.WriteU32(ops.BoundaryTick(i));
        }
    }

    // Transform-sample latch block, see the class remarks for the layout.
    private void WriteSampleLatch(ref CanonicalWriter w)
    {
        ISampleLatch latch = _world.Samples;
        SampleLatch state = _world.SampleLatchState;
        for (int s = 0; s < PlayerSlot.Count; s++)
        {
            w.WriteBool(state.HasSeq(s));
            w.WriteU32(state.LastSeqRaw(s));
        }

        w.WriteU32(latch.ReceivedTick);
        w.WriteU32((uint)latch.Count);
        for (int i = 0; i < latch.Count; i++)
        {
            latch[i].WriteCanonical(ref w);
        }
    }

    private void WriteSlotTable(ref CanonicalWriter w)
    {
        SlotTable? slots = _sources?.Slots;
        for (byte s = 0; s < PlayerSlot.Count; s++)
        {
            PlayerSlotEntry e = slots is null
                ? new PlayerSlotEntry(EntityId.None, PlayerSlotEntry.NeverFreed, occupied: false)
                : slots.Entry(PlayerSlot.FromValue(s));
            w.WriteU32(e.PlayerId.Value);
            w.WriteU32(e.FreedTick);
            w.WriteBool(e.Occupied);
        }
    }
}
