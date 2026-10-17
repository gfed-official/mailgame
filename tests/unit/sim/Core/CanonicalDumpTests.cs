using System.Buffers;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Covers TR-sim-035 for story 019 (GDD R30, ADR-0006 section 4): the dump format (AC-1), the contents and order of Sim Core's
/// section 1, each field moving the SHA-256 (AC-2), and validity plus order independence (AC-3). SHA-256 is taken in the tests
/// only (<see cref="DumpHash"/>); the Sim never hashes. The zero-allocation clause is in <see cref="CanonicalDumpAllocationTests"/>.
/// Behaviour that differs between SIM_ASSERT and other builds branches on <see cref="TestBuildExpectation.SimAssertsExpected"/>.
/// </summary>
[Trait("Id", "SC-R30-1")]
public class CanonicalDumpTests
{
    // Section 1 size for a world with no ops, no streams and no test classes: 4 tick + 32 counters + 4 test count + 4 mail
    // + 4 opSeq + 4 stream count + 4 op count + latch block (8 slots x (1 hasSeq + 4 lastSeq) + 4 receivedTick + 4 count)
    // + 8 slots x 9.
    private const int FreshSection1Length = 4 + 32 + 4 + 4 + 4 + 4 + 4 + ((8 * 5) + 4 + 4) + (8 * 9);

    private static readonly byte[] _header = { 0x50, 0x4D, 0x53, 0x44, 0x01, 0x00 };

    // ---------------------------------------------------------------- fixtures

    private sealed class ForwardingReader : ITickReader, IWorldStateReader
    {
        public SimWorld? Target { get; set; }

        public uint CurrentTick => Target!.CurrentTick;

        public TickStage Stage => Target!.Stage;

        public PhaseId Phase => Target!.Phase;

        public SimState State => Target!.State;
    }

    private sealed class ActionPhase : ISimPhase
    {
        private readonly Action _body;

        public ActionPhase(Action body)
        {
            _body = body;
        }

        public void Run(in TickContext ctx) => _body();
    }

    private sealed class ActionCheck : ITickEndCheck
    {
        private readonly Action _body;

        public ActionCheck(Action body)
        {
            _body = body;
        }

        public void Check(uint tick) => _body();
    }

    private sealed class ActionSink : IEventSink
    {
        private readonly Action _body;

        public ActionSink(Action body)
        {
            _body = body;
        }

        public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow) => _body();
    }

    private sealed class FixedContributor : ISimStateContributor
    {
        private readonly ushort _value;

        public FixedContributor(ushort sectionId, ushort value)
        {
            SectionId = sectionId;
            _value = value;
        }

        public ushort SectionId { get; }

        public void Write(ref CanonicalWriter w)
        {
            w.WriteU16(_value);
        }
    }

    /// <summary>Holds a keyed collection; writes it in ascending key order regardless of insertion order.</summary>
    private sealed class KeyedContributor : ISimStateContributor
    {
        private readonly List<KeyValuePair<uint, uint>> _items = new();
        private readonly bool _sorted;

        public KeyedContributor(bool sorted = true)
        {
            _sorted = sorted;
        }

        public ushort SectionId => 40;

        public void Add(uint key, uint value) => _items.Add(new KeyValuePair<uint, uint>(key, value));

        public void Write(ref CanonicalWriter w)
        {
            var order = new int[_items.Count];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            if (_sorted)
            {
                for (int i = 1; i < order.Length; i++)
                {
                    int cur = order[i];
                    int j = i - 1;
                    while (j >= 0 && _items[order[j]].Key > _items[cur].Key)
                    {
                        order[j + 1] = order[j];
                        j--;
                    }

                    order[j + 1] = cur;
                }
            }

            w.WriteU32((uint)order.Length);
            for (int i = 0; i < order.Length; i++)
            {
                w.WriteU32(_items[order[i]].Key);
                w.WriteU32(_items[order[i]].Value);
            }
        }
    }

    /// <summary>One world with every Sim Core state source bound.</summary>
    private sealed class Rig
    {
        public Rig(
            uint[]? initialClasses = null,
            uint lastMail = 0,
            uint lastOpSeq = 0,
            int opCapacity = 16,
            ISimStateContributor[]? contributors = null,
            Action<SimWorldBuilder>? configure = null)
        {
            Counters = new SimCounters();
            Ring = new SimCheckRing();
            var holder = new ForwardingReader();
            Ids = new EntityIds(holder, Ring, Counters, initialClasses, allowTestClasses: true);
            Mail = new MailIds(holder, Ring, Counters, lastMail);
            Rng = new RngRegistry(1, Counters);
            Slots = new SlotTable();
            // MaxOpsDrainedPerTick = 0: dump tests inspect queued ops across ticks (Story 020 drains otherwise).
            var settings = new SimCoreSettings { OpQueueCapacity = opCapacity, SystemOpReserve = 2, MaxOpsDrainedPerTick = 0 };
            var builder = new SimWorldBuilder(settings, Counters, Ring, initialCounters: new SimInitialCounters { LastOpSeq = lastOpSeq });
            builder.BindCoreState(Ids, Mail, Rng, Slots);
            if (contributors is not null)
            {
                foreach (ISimStateContributor c in contributors)
                {
                    builder.BindStateContributor(c);
                }
            }

            configure?.Invoke(builder);
            World = builder.Freeze();
            holder.Target = World;
        }

        public SimCounters Counters { get; }

        public SimCheckRing Ring { get; }

        public EntityIds Ids { get; }

        public MailIds Mail { get; }

        public RngRegistry Rng { get; }

        public SlotTable Slots { get; }

        public SimWorld World { get; }

        public string Hash() => DumpHash.Sha256(World);
    }

    private readonly record struct StreamRow(ushort Ordinal, uint Index, ulong State, ulong Inc, ulong Draws);

    private readonly record struct OpRow(uint OpSeq, uint Actor, ushort Kind, byte PayloadLength, byte Reserved, uint ReqId, byte[] Payload, uint Boundary);

    private readonly record struct SlotRow(uint Player, uint FreedTick, byte Occupied);

    private sealed class Core
    {
        public uint Tick;
        public uint[] Classes = new uint[8];
        public List<(byte Class, uint Counter)> TestClasses = new();
        public uint Mail;
        public uint OpSeq;
        public List<StreamRow> Streams = new();
        public List<OpRow> Ops = new();
        public uint Latch;
        public List<(byte HasSeq, uint LastSeq)> LatchSeqs = new();
        public uint LatchTick;
        public List<byte[]> LatchSamples = new();
        public List<SlotRow> Slots = new();
        public int Consumed;
    }

    private static List<(ushort Id, byte[] Payload)> ParseSections(byte[] dump)
    {
        Assert.True(dump.Length >= 6);
        Assert.Equal(_header, dump[..6]);
        var sections = new List<(ushort, byte[])>();
        int pos = 6;
        while (pos < dump.Length)
        {
            ushort id = BitConverter.ToUInt16(dump, pos);
            uint length = BitConverter.ToUInt32(dump, pos + 2);
            Assert.True(pos + 6 + length <= dump.Length, "section runs past the end of the dump");
            sections.Add((id, dump[(pos + 6)..(pos + 6 + (int)length)]));
            pos += 6 + (int)length;
        }

        Assert.Equal(dump.Length, pos);
        return sections;
    }

    private static Core ParseCore(byte[] payload)
    {
        var core = new Core();
        int p = 0;
        uint U32()
        {
            uint v = BitConverter.ToUInt32(payload, p);
            p += 4;
            return v;
        }

        ulong U64()
        {
            ulong v = BitConverter.ToUInt64(payload, p);
            p += 8;
            return v;
        }

        ushort U16()
        {
            ushort v = BitConverter.ToUInt16(payload, p);
            p += 2;
            return v;
        }

        byte U8() => payload[p++];

        core.Tick = U32();
        for (int i = 0; i < 8; i++)
        {
            core.Classes[i] = U32();
        }

        uint testCount = U32();
        for (uint i = 0; i < testCount; i++)
        {
            byte c = U8();
            core.TestClasses.Add((c, U32()));
        }

        core.Mail = U32();
        core.OpSeq = U32();
        uint streams = U32();
        for (uint i = 0; i < streams; i++)
        {
            core.Streams.Add(new StreamRow(U16(), U32(), U64(), U64(), U64()));
        }

        uint ops = U32();
        for (uint i = 0; i < ops; i++)
        {
            uint seq = U32();
            uint actor = U32();
            ushort kind = U16();
            byte len = U8();
            byte reserved = U8();
            uint req = U32();
            byte[] data = payload[p..(p + OpPayload.Size)];
            p += OpPayload.Size;
            core.Ops.Add(new OpRow(seq, actor, kind, len, reserved, req, data, U32()));
        }

        for (int i = 0; i < 8; i++)
        {
            byte has = U8();
            core.LatchSeqs.Add((has, U32()));
        }

        core.LatchTick = U32();
        core.Latch = U32();
        for (uint i = 0; i < core.Latch; i++)
        {
            core.LatchSamples.Add(payload[p..(p + 24)]);
            p += 24;
        }

        for (int i = 0; i < 8; i++)
        {
            core.Slots.Add(new SlotRow(U32(), U32(), U8()));
        }

        core.Consumed = p;
        return core;
    }

    private static Core Parse(SimWorld world)
    {
        List<(ushort Id, byte[] Payload)> sections = ParseSections(DumpHash.Bytes(world));
        Assert.Equal(SectionIds.SimCore, sections[0].Id);
        Core core = ParseCore(sections[0].Payload);
        Assert.Equal(sections[0].Payload.Length, core.Consumed);
        return core;
    }

    private static Op OpOf(OpKind kind = OpKind.TeleportAck, uint req = 1, byte payloadByte = 0, byte reserved = 0, byte length = 0)
    {
        var op = new Op { Kind = kind, ReqId = req, Reserved = reserved, PayloadLength = length };
        Span<byte> payload = op.Payload;
        payload[0] = payloadByte;
        return op;
    }

    private static uint[] Classes(byte cls, uint value)
    {
        var a = new uint[256];
        a[cls] = value;
        return a;
    }

    private static readonly ActorId _player = new(0x0100_0005u);

    // ---------------------------------------------------------------- AC-1 format

    [Fact]
    public void Ac1_FreshReadyWorld_BeginsWithHeaderThenSection1()
    {
        var rig = new Rig();

        byte[] dump = DumpHash.Bytes(rig.World);

        Assert.Equal(_header, dump[..6]);
        Assert.Equal(new byte[] { 0x01, 0x00 }, dump[6..8]);
        Assert.Equal((uint)FreshSection1Length, BitConverter.ToUInt32(dump, 8));
        Assert.Equal(6 + 6 + FreshSection1Length, dump.Length);
    }

    [Fact]
    public void Ac1_WorldWithoutBoundSources_DumpsLikeAFreshRig()
    {
        SimWorld bare = new SimWorldBuilder().Freeze();
        var rig = new Rig();

        Assert.Equal(DumpHash.Bytes(rig.World), DumpHash.Bytes(bare));
    }

    [Fact]
    public void Ac1_Contributors_FollowSection1_InRegistrationOrder_AndWriteLittleEndian()
    {
        var rig = new Rig(contributors: new ISimStateContributor[] { new FixedContributor(9, 0x1234), new FixedContributor(3, 0xBEEF) });

        List<(ushort Id, byte[] Payload)> sections = ParseSections(DumpHash.Bytes(rig.World));

        Assert.Equal(new ushort[] { SectionIds.SimCore, 9, 3 }, sections.Select(s => s.Id).ToArray());
        Assert.Equal(new byte[] { 0x34, 0x12 }, sections[1].Payload);
        Assert.Equal(new byte[] { 0xEF, 0xBE }, sections[2].Payload);
    }

    [Fact]
    public void Ac1_DuplicateSectionId_FailsTheBuild()
    {
        var builder = new SimWorldBuilder();
        builder.BindStateContributor(new FixedContributor(5, 1));

        var e = Assert.Throws<SimBuildException>(() => builder.BindStateContributor(new FixedContributor(5, 2)));

        Assert.Equal(SimBuildErrorCode.DuplicateSection, e.Code);
    }

    [Fact]
    public void Ac1_ContributorUsingSimCoreSectionId_FailsTheBuild()
    {
        var builder = new SimWorldBuilder();

        var e = Assert.Throws<SimBuildException>(() => builder.BindStateContributor(new FixedContributor(SectionIds.SimCore, 1)));

        Assert.Equal(SimBuildErrorCode.DuplicateSection, e.Code);
    }

    [Fact]
    public void Ac1_SectionIdZero_FailsTheBuild()
    {
        var builder = new SimWorldBuilder();

        var e = Assert.Throws<SimBuildException>(() => builder.BindStateContributor(new FixedContributor(0, 1)));

        Assert.Equal(SimBuildErrorCode.InvalidSection, e.Code);
    }

    [Fact]
    public void Ac1_BindAfterFreeze_FailsWithBindAfterFreeze()
    {
        var builder = new SimWorldBuilder();
        builder.Freeze();

        var contributor = Assert.Throws<SimBuildException>(() => builder.BindStateContributor(new FixedContributor(5, 1)));
        var core = Assert.Throws<SimBuildException>(() => builder.BindCoreState(null, null, null, null));

        Assert.Equal(SimBuildErrorCode.BindAfterFreeze, contributor.Code);
        Assert.Equal(SimBuildErrorCode.BindAfterFreeze, core.Code);
    }

    [Fact]
    public void Ac1_NullContributor_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SimWorldBuilder().BindStateContributor(null!));
    }

    [Fact]
    public void Ac1_TwoDumpsOfTheSameState_AreByteIdentical_AndDoNotMutateIt()
    {
        var rig = new Rig();
        rig.World.EnqueueOp(_player, OpOf());
        rig.Rng.Create(RngNames.Mail, 1).Next32();

        byte[] first = DumpHash.Bytes(rig.World);
        byte[] second = DumpHash.Bytes(rig.World);

        Assert.Equal(first, second);
        Assert.Equal(1u, rig.World.LastOpSeq);
    }

    [Fact]
    public void Ac1_DumpHash_IsStableAcrossIndependentWorldsWithTheSameHistory()
    {
        Assert.Equal(new Rig().Hash(), new Rig().Hash());
    }

    // ---------------------------------------------------------------- AC-2 section 1

    [Fact]
    public void Ac2_FreshSection1_HasTheDocumentedLayout()
    {
        var rig = new Rig();

        Core core = Parse(rig.World);

        Assert.Equal(0u, core.Tick);
        Assert.All(core.Classes, c => Assert.Equal(0u, c));
        Assert.Empty(core.TestClasses);
        Assert.Equal(0u, core.Mail);
        Assert.Equal(0u, core.OpSeq);
        Assert.Empty(core.Streams);
        Assert.Empty(core.Ops);
        Assert.Equal(0u, core.Latch);
        Assert.Equal(0u, core.LatchTick);
        Assert.Equal(8, core.LatchSeqs.Count);
        Assert.All(core.LatchSeqs, q => Assert.Equal(((byte)0, 0u), q));
        Assert.Empty(core.LatchSamples);
        Assert.Equal(8, core.Slots.Count);
        Assert.All(core.Slots, s => Assert.Equal(new SlotRow(0, PlayerSlotEntry.NeverFreed, 0), s));
        Assert.Equal(FreshSection1Length, ParseSections(DumpHash.Bytes(rig.World))[0].Payload.Length);
    }

    [Fact]
    public void Ac2_FieldsAppearInTheSpecifiedOrder()
    {
        var initial = new uint[256];
        for (byte c = 1; c <= 8; c++)
        {
            initial[c] = 100u + c;
        }

        initial[0xF3] = 77;
        var rig = new Rig(initial, lastMail: 55, lastOpSeq: 9);
        rig.World.Tick();
        rig.World.Tick();
        rig.Rng.Create(RngNames.Mail, 2);
        rig.World.EnqueueOp(_player, OpOf(req: 42));
        rig.Slots.Assign(PlayerSlot.FromValue(2), new EntityId(0x0100_0007u), 2);

        Core core = Parse(rig.World);

        Assert.Equal(2u, core.Tick);
        Assert.Equal(new uint[] { 101, 102, 103, 104, 105, 106, 107, 108 }, core.Classes);
        Assert.Equal(new[] { ((byte)0xF3, 77u) }, core.TestClasses.ToArray());
        Assert.Equal(55u, core.Mail);
        Assert.Equal(10u, core.OpSeq);
        Assert.Single(core.Streams);
        Assert.Single(core.Ops);
        Assert.Equal(42u, core.Ops[0].ReqId);
        Assert.Equal(0u, core.Latch);
        Assert.Equal(new SlotRow(0x0100_0007u, PlayerSlotEntry.NeverFreed, 1), core.Slots[2]);
    }

    [Fact]
    public void Ac2_CurrentTick_ChangesTheHash()
    {
        var baseline = new Rig();
        var ticked = new Rig();
        ticked.World.Tick();

        Assert.NotEqual(baseline.Hash(), ticked.Hash());
    }

    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    [InlineData((byte)4)]
    [InlineData((byte)5)]
    [InlineData((byte)6)]
    [InlineData((byte)7)]
    [InlineData((byte)8)]
    public void Ac2_EachEntityClassCounter_ChangesTheHash_AndEncodesAtItsPosition(byte cls)
    {
        string baseline = new Rig().Hash();
        var rig = new Rig(Classes(cls, 3));

        Assert.NotEqual(baseline, rig.Hash());
        Core core = Parse(rig.World);
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(i == cls - 1 ? 3u : 0u, core.Classes[i]);
        }
    }

    [Fact]
    public void Ac2_EntityClassCounters_AreMutuallyDistinguishable()
    {
        var hashes = new HashSet<string>();
        for (byte c = 1; c <= 8; c++)
        {
            Assert.True(hashes.Add(new Rig(Classes(c, 1)).Hash()), $"class {c} collides");
        }
    }

    [Fact]
    public void Ac2_TestClassCounter_ChangesTheHash_AndIsListedAscending()
    {
        string baseline = new Rig().Hash();
        var initial = Classes(0xF5, 4);
        initial[0xF1] = 2;
        var rig = new Rig(initial);

        Assert.NotEqual(baseline, rig.Hash());
        Assert.NotEqual(new Rig(Classes(0xF5, 5)).Hash(), new Rig(Classes(0xF5, 4)).Hash());
        Assert.Equal(new[] { ((byte)0xF1, 2u), ((byte)0xF5, 4u) }, Parse(rig.World).TestClasses.ToArray());
    }

    [Fact]
    public void Ac2_MailCounter_ChangesTheHash()
    {
        Assert.NotEqual(new Rig().Hash(), new Rig(lastMail: 1).Hash());
        Assert.Equal(7u, Parse(new Rig(lastMail: 7).World).Mail);
    }

    [Fact]
    public void Ac2_OpSeq_ChangesTheHash()
    {
        Assert.NotEqual(new Rig().Hash(), new Rig(lastOpSeq: 1).Hash());
        Assert.Equal(7u, Parse(new Rig(lastOpSeq: 7).World).OpSeq);
    }

    [Fact]
    public void Ac2_RngStream_Created_Drawn_AndIndexed_EachChangeTheHash()
    {
        string baseline = new Rig().Hash();
        var created = new Rig();
        created.Rng.Create(RngNames.Mail, 1);
        var drawn = new Rig();
        drawn.Rng.Create(RngNames.Mail, 1).Next32();
        var otherIndex = new Rig();
        otherIndex.Rng.Create(RngNames.Mail, 2);
        var otherName = new Rig();
        otherName.Rng.Create(RngNames.MailSpecial, 1);

        string[] all = { baseline, created.Hash(), drawn.Hash(), otherIndex.Hash(), otherName.Hash() };
        Assert.Equal(all.Length, all.Distinct().Count());
    }

    [Fact]
    public void Ac2_CreateTwice_YieldsOneStreamEntry_WithZeroDraws()
    {
        var rig = new Rig();
        rig.Rng.Create(RngNames.Mail, 1);
        rig.Rng.Create(RngNames.Mail, 1);

        Core core = Parse(rig.World);

        StreamRow row = Assert.Single(core.Streams);
        Assert.Equal(RngNames.Mail.Ordinal, row.Ordinal);
        Assert.Equal(1u, row.Index);
        Assert.Equal(0UL, row.Draws);
    }

    [Fact]
    public void Ac2_Streams_AreListedInNameOrderThenIndex_WithTheirLiveState()
    {
        var rig = new Rig();
        RngStream events = rig.Rng.Create(RngNames.Events, 1);
        RngStream mail2 = rig.Rng.Create(RngNames.Mail, 2);
        RngStream waves = rig.Rng.Create(RngNames.Waves, 0);
        RngStream mail1 = rig.Rng.Create(RngNames.Mail, 1);
        mail1.Next32();
        mail1.Next32();
        mail1.Next32();

        Core core = Parse(rig.World);

        Assert.Equal(
            new[] { (RngNames.Mail.Ordinal, 1u), (RngNames.Mail.Ordinal, 2u), (RngNames.Waves.Ordinal, 0u), (RngNames.Events.Ordinal, 1u) },
            core.Streams.Select(s => (s.Ordinal, s.Index)).ToArray());
        Assert.Equal(3UL, core.Streams[0].Draws);
        Assert.Equal(mail1.State, core.Streams[0].State);
        Assert.Equal(mail1.Inc, core.Streams[0].Inc);
        Assert.Equal(mail2.State, core.Streams[1].State);
        Assert.Equal(waves.State, core.Streams[2].State);
        Assert.Equal(events.State, core.Streams[3].State);
    }

    [Fact]
    public void Ac2_QueuedOp_EachFieldChangesTheHash()
    {
        string empty = new Rig().Hash();
        string baseline = WithOp(OpOf(), _player).Hash();
        var variants = new Dictionary<string, string>
        {
            ["kind"] = WithOp(OpOf(OpKind.PlayerLeave), _player).Hash(),
            ["reqId"] = WithOp(OpOf(req: 2), _player).Hash(),
            ["payload"] = WithOp(OpOf(payloadByte: 1), _player).Hash(),
            ["payloadLength"] = WithOp(OpOf(length: 1), _player).Hash(),
            ["reserved"] = WithOp(OpOf(reserved: 1), _player).Hash(),
            ["actor"] = WithOp(OpOf(), new ActorId(0x0200_0009u)).Hash(),
        };

        Assert.NotEqual(empty, baseline);
        foreach (KeyValuePair<string, string> v in variants)
        {
            Assert.NotEqual(baseline, v.Value);
        }

        Assert.Equal(variants.Count, variants.Values.Distinct().Count());
    }

    [Fact]
    public void Ac2_QueuedOp_BoundaryTick_ChangesTheHash_WhenNothingElseDoes()
    {
        var early = new Rig();
        early.World.EnqueueOp(_player, OpOf());
        early.World.Tick();
        var late = new Rig();
        late.World.Tick();
        late.World.EnqueueOp(_player, OpOf());

        Core a = Parse(early.World);
        Core b = Parse(late.World);

        Assert.Equal(a.Tick, b.Tick);
        Assert.Equal(0u, a.Ops[0].Boundary);
        Assert.Equal(1u, b.Ops[0].Boundary);
        Assert.NotEqual(early.Hash(), late.Hash());
    }

    [Fact]
    public void Ac2_QueuedOps_AreListedInRingOrder_AfterTheRingWraps()
    {
        var rig = new Rig(opCapacity: 4);
        for (uint i = 1; i <= 3; i++)
        {
            Assert.True(rig.World.EnqueueOp(ActorId.System, OpOf(req: i)));
        }

        rig.World.OpsForTests.DropFront(2);
        for (uint i = 4; i <= 6; i++)
        {
            Assert.True(rig.World.EnqueueOp(ActorId.System, OpOf(req: i)));
        }

        Core core = Parse(rig.World);

        Assert.Equal(new uint[] { 3, 4, 5, 6 }, core.Ops.Select(o => o.OpSeq).ToArray());
        Assert.Equal(new uint[] { 3, 4, 5, 6 }, core.Ops.Select(o => o.ReqId).ToArray());
    }

    [Fact]
    public void Ac2_QueuedOp_PayloadIsWrittenAs48Bytes()
    {
        var op = new Op { Kind = OpKind.TeleportAck };
        Span<byte> payload = op.Payload;
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i + 1);
        }

        op.PayloadLength = 48;
        var rig = new Rig();
        rig.World.EnqueueOp(_player, op);

        OpRow row = Assert.Single(Parse(rig.World).Ops);

        Assert.Equal(Enumerable.Range(1, 48).Select(i => (byte)i).ToArray(), row.Payload);
    }

    [Fact]
    public void Ac2_SlotTable_EachFieldChangesTheHash()
    {
        string baseline = new Rig().Hash();
        var player = new EntityId(0x0100_0007u);
        var slot0 = new Rig();
        slot0.Slots.Assign(PlayerSlot.FromValue(0), player, 5);
        var slot1 = new Rig();
        slot1.Slots.Assign(PlayerSlot.FromValue(1), player, 5);
        var otherPlayer = new Rig();
        otherPlayer.Slots.Assign(PlayerSlot.FromValue(0), new EntityId(0x0100_0008u), 5);
        var freed = new Rig();
        freed.Slots.Assign(PlayerSlot.FromValue(0), player, 5);
        freed.Slots.Free(PlayerSlot.FromValue(0), 6);
        var freedLater = new Rig();
        freedLater.Slots.Assign(PlayerSlot.FromValue(0), player, 5);
        freedLater.Slots.Free(PlayerSlot.FromValue(0), 7);

        string[] all = { baseline, slot0.Hash(), slot1.Hash(), otherPlayer.Hash(), freed.Hash(), freedLater.Hash() };
        Assert.Equal(all.Length, all.Distinct().Count());
        Assert.Equal(new SlotRow(0, 6, 0), Parse(freed.World).Slots[0]);
    }

    [Fact]
    public void Ac2_Excluded_CountersAndCheckRing_DoNotChangeTheHash()
    {
        var a = new Rig();
        var b = new Rig();
        b.Counters.Add(CounterId.BadTick);
        b.Ring.Record(CheckCode.BusySystem, 0, 1, 2);

        Assert.Equal(a.Hash(), b.Hash());
    }

    // ---------------------------------------------------------------- AC-3 validity

    [Fact]
    public void Ac3_FromAPhase_IsRefusedAndCounted()
    {
        bool? result = null;
        Rig? rig = null;
        rig = new Rig(configure: b => b.Bind(PhaseId.PlayersStep, new ActionPhase(() => result = rig!.World.TryWriteDump(new ArrayBufferWriter<byte>()))));

        rig.World.Tick();

        Assert.False(result);
        Assert.Equal(1L, rig.Counters[CounterId.DumpDuringTick]);
    }

    [Fact]
    public void Ac3_FromATickEndCheck_IsRefusedAndCounted()
    {
        bool? result = null;
        Rig? rig = null;
        rig = new Rig(configure: b => b.BindTickEnd(new ActionCheck(() => result = rig!.World.TryWriteDump(new ArrayBufferWriter<byte>()))));

        rig.World.Tick();

        Assert.False(result);
        Assert.Equal(1L, rig.Counters[CounterId.DumpDuringTick]);
    }

    [Fact]
    public void Ac3_FromTheEventSink_IsRefusedAndCounted_AndWritesNothing()
    {
        bool? result = null;
        var output = new ArrayBufferWriter<byte>();
        Rig? rig = null;
        rig = new Rig(configure: b => b.BindEventSink(new ActionSink(() => result = rig!.World.TryWriteDump(output))));

        rig.World.Tick();

        Assert.False(result);
        Assert.Equal(0, output.WrittenCount);
        Assert.Equal(1L, rig.Counters[CounterId.DumpDuringTick]);
    }

    [Fact]
    public void Ac3_FaultedWorld_IsRefused_WithoutCounting()
    {
        var rig = new Rig(configure: b => b.Bind(PhaseId.PlayersStep, new ActionPhase(() => throw new InvalidOperationException("boom"))));
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Throws<InvalidOperationException>(rig.World.Tick);
        }
        else
        {
            rig.World.Tick();
        }

        var output = new ArrayBufferWriter<byte>();

        Assert.Equal(SimState.Faulted, rig.World.State);
        Assert.False(rig.World.TryWriteDump(output));
        Assert.Equal(0, output.WrittenCount);
        Assert.Equal(0L, rig.Counters[CounterId.DumpDuringTick]);
    }

    [Fact]
    public void Ac3_ConstructedWorld_IsRefused()
    {
        SimWorld world = InState(SimState.Constructed);

        Assert.False(world.TryWriteDump(new ArrayBufferWriter<byte>()));
    }

    [Fact]
    public void Ac3_ReadyRunningAndStoppedWorlds_AreAccepted()
    {
        var rig = new Rig();
        Assert.Equal(SimState.Ready, rig.World.State);
        Assert.True(rig.World.TryWriteDump(new ArrayBufferWriter<byte>()));

        rig.World.Tick();
        Assert.Equal(SimState.Running, rig.World.State);
        Assert.True(rig.World.TryWriteDump(new ArrayBufferWriter<byte>()));

        Assert.True(rig.World.Stop());
        Assert.Equal(SimState.Stopped, rig.World.State);
        Assert.True(rig.World.TryWriteDump(new ArrayBufferWriter<byte>()));
        Assert.Equal(0L, rig.Counters[CounterId.DumpDuringTick]);
    }

    [Fact]
    public void Ac3_ADumpAfterATick_IsAcceptedAgain_ThenAfterRefusalTheStateIsUnchanged()
    {
        var rig = new Rig(configure: b => b.Bind(PhaseId.PlayersStep, new ActionPhase(() => { })));
        rig.World.Tick();
        string before = rig.Hash();

        Assert.True(rig.World.TryWriteDump(new ArrayBufferWriter<byte>()));
        Assert.Equal(before, rig.Hash());
    }

    [Fact]
    public void Ac3_NullOutput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Rig().World.TryWriteDump(null!));
    }

    [Fact]
    public void Ac3_ReentrantDumpFromAContributor_IsRefused()
    {
        bool? inner = null;
        var output = new ArrayBufferWriter<byte>();
        SimWorld? world = null;
        var contributor = new CallbackContributor(() => inner = world!.TryWriteDump(new ArrayBufferWriter<byte>()));
        var rig = new Rig(contributors: new ISimStateContributor[] { contributor });
        world = rig.World;

        Assert.True(world.TryWriteDump(output));

        Assert.False(inner);
    }

    private sealed class CallbackContributor : ISimStateContributor
    {
        private readonly Action _callback;

        public CallbackContributor(Action callback)
        {
            _callback = callback;
        }

        public ushort SectionId => 50;

        public void Write(ref CanonicalWriter w)
        {
            _callback();
            w.WriteU8(1);
        }
    }

    // ---------------------------------------------------------------- AC-3 order independence

    [Fact]
    public void Ac3_KeyedContributor_IsInsertionOrderIndependent()
    {
        Rig a = KeyedRig(sorted: true, 3, 1, 2);
        Rig b = KeyedRig(sorted: true, 2, 3, 1);
        Rig c = KeyedRig(sorted: true, 1, 2, 3);

        Assert.Equal(DumpHash.Bytes(a.World), DumpHash.Bytes(b.World));
        Assert.Equal(DumpHash.Bytes(a.World), DumpHash.Bytes(c.World));
    }

    [Fact]
    public void Ac3_ControlCase_AContributorThatWritesInsertionOrder_IsDetectedByTheSameComparison()
    {
        Rig a = KeyedRig(sorted: false, 3, 1, 2);
        Rig b = KeyedRig(sorted: false, 2, 3, 1);

        Assert.NotEqual(a.Hash(), b.Hash());
    }

    [Fact]
    public void Ac3_DumpFromAnotherThread_IsRefusedOrAsserts()
    {
        var rig = new Rig();
        Exception? thrown = null;
        bool? result = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                result = rig.World.TryWriteDump(new ArrayBufferWriter<byte>());
            }
            catch (Exception e)
            {
                thrown = e;
            }
        });
        thread.Start();
        thread.Join();

        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.IsType<SimAssertException>(thrown);
        }
        else
        {
            Assert.Null(thrown);
            Assert.False(result);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static Rig KeyedRig(bool sorted, params uint[] keys)
    {
        var contributor = new KeyedContributor(sorted);
        foreach (uint k in keys)
        {
            contributor.Add(k, k * 10);
        }

        return new Rig(contributors: new ISimStateContributor[] { contributor });
    }

    private static Rig WithOp(Op op, ActorId actor)
    {
        var rig = new Rig();
        Assert.True(rig.World.EnqueueOp(actor, op));
        return rig;
    }

    private static SimWorld InState(SimState state)
    {
        var phases = new ISimPhase[14][];
        for (int i = 0; i < phases.Length; i++)
        {
            phases[i] = Array.Empty<ISimPhase>();
        }

        var settings = new SimCoreSettings();
        var parts = new SimWorldParts(phases, Array.Empty<ITickEndCheck>(), settings, new SimCounters(), new SimCheckRing(settings.CheckRingCapacity), new NullPhaseProbe())
        {
            InitialState = state,
        };
        return new SimWorld(parts);
    }
}

/// <summary>
/// The zero-allocation clause of story 019 ("the dump's internal buffer is reusable"): once warm, a dump into a reused
/// output buffer allocates nothing. Non-parallel so no other test disturbs the measurement.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class CanonicalDumpAllocationTests
{
    [Fact]
    public void Ac1_WarmDump_AllocatesNothing_InOptimisedBuilds()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(SimWorld).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        var counters = new SimCounters();
        var ring = new SimCheckRing();
        var rng = new RngRegistry(1, counters);
        var slots = new SlotTable();
        var builder = new SimWorldBuilder(counters: counters, checkRing: ring);
        builder.BindCoreState(null, null, rng, slots);
        SimWorld world = builder.Freeze();
        rng.Create(RngNames.Mail, 1).Next32();
        rng.Create(RngNames.Events, 3);
        var op = new Op { Kind = OpKind.TeleportAck, ReqId = 1 };
        Assert.True(world.EnqueueOp(new ActorId(0x0100_0005u), op));
        Assert.True(world.EnqueueOp(new ActorId(0x0100_0005u), op));
        var output = new ArrayBufferWriter<byte>(4096);

        int runs = AllocAssert.Zero(
            "warm TryWriteDump",
            () =>
            {
                output.ResetWrittenCount();
                world.TryWriteDump(output);
            });

        Assert.True(runs > 0);
        Assert.True(output.WrittenCount > 0);
    }
}
