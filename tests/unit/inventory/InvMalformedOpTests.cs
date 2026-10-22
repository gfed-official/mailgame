using System.Buffers;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/004, GDD H-NET-5 (Sim half) and H-R7-4 (malformed half); TR-inventory-025, TR-inventory-006; ADR-0009 section 2;
/// ADR-0007 sections 3 and 5. Until story 019 binds the real handlers, every inventory kind the system does not bind itself (all but
/// <c>Pick</c> and <c>Move</c>, story 007, <c>Merge</c>, story 008, <c>QuickMove</c> and <c>Return</c>, story 010, and <c>Close</c>,
/// story 011, whose real handlers run here and are covered by <c>PickMoveTests</c>, <c>MoveSplitMergeTests</c>,
/// <c>QuickMoveReturnTests</c> and <c>OpenSetTests</c>) is bound (with its real descriptor) to a TEST-ONLY <see cref="InvValidateOnlyStub"/>
/// whose whole body is the shared production steps (<see cref="InvRequestCheck.CheckRequest"/>, then — no <c>not_open</c> step exists
/// for those kinds yet — <see cref="InvRequestCheck.ResolveEntries"/>) and which never mutates; ops go through the real phase-0 drain of
/// a composed world holding a real <see cref="InventorySystem"/> (with the world's slot view, so the real handlers resolve the actor's
/// slot) and live and removed entries. The rig's live entries are allocated but placed in no container, so the real <c>Pick</c>,
/// <c>Move</c>, <c>Merge</c> and <c>QuickMove</c> answer a well-formed op on one with <c>stale_entry</c>, and the real <c>Return</c> answers the
/// rig's empty cursor with <c>cursor_empty</c>. Out-of-range values that a narrow payload field cannot represent
/// (<c>count = -1</c>, <c>x = 70000</c>) are checked at the helper's widened-<c>int</c> entry points (<see cref="InvRequestCheck.CheckCount"/>,
/// <see cref="InvRequestCheck.CheckCoordinate"/>) that the codec decode boundary will call; byte-level decode itself is the
/// network-adapter-loopback epic's.
/// </summary>
public class InvMalformedOpTests
{
    private const uint RecordedSeed = 0x5EED_0004u;
    private const int FuzzOps = 10_000;
    private const ulong GoldenFuzzHash = 0x7285DA452711B85CUL; // pinned 2026-10-20 from the recorded seed

    // ------------------------------------------------------------------ H-R7-4 (malformed half)

    [Theory]
    [Trait("Id", "H-R7-4")]
    [InlineData(-1, CoreReason.BadRequest)]
    [InlineData(256, CoreReason.BadRequest)]
    [InlineData(int.MinValue, CoreReason.BadRequest)]
    [InlineData(int.MaxValue, CoreReason.BadRequest)]
    [InlineData(0, 0)]
    [InlineData(255, 0)]
    public void CheckCount_DecodedValue_OutsideU8IsBadRequest(int decoded, int expected)
    {
        // Act
        ushort reason = InvRequestCheck.CheckCount(decoded);

        // Assert
        Assert.Equal(expected, reason);
    }

    [Theory]
    [Trait("Id", "H-R7-4")]
    [InlineData(70_000, CoreReason.BadRequest)]
    [InlineData(32_768, CoreReason.BadRequest)]
    [InlineData(-32_769, CoreReason.BadRequest)]
    [InlineData(int.MinValue, CoreReason.BadRequest)]
    [InlineData(32_767, 0)]
    [InlineData(-32_768, 0)]
    [InlineData(0, 0)]
    public void CheckCoordinate_DecodedValue_OutsideI16IsBadRequest(int decoded, int expected)
    {
        // Act
        ushort reason = InvRequestCheck.CheckCoordinate(decoded);

        // Assert
        Assert.Equal(expected, reason);
    }

    [Fact]
    [Trait("Id", "H-R7-4")]
    public void Move_EntryIdAtOrAboveTheHighWater_IsBadRequest_AndChangesNothing()
    {
        // Arrange
        var rig = new InvOpRig();
        uint firstUnissued = rig.Inventory.Entries.LastId + 1;

        // Act + Assert
        rig.AssertSingleReply(rig.MoveOp(firstUnissued), rig.PlayerA, CoreReason.BadRequest);
        rig.AssertSingleReply(rig.MoveOp(EntryPool.MaxRealId), rig.PlayerA, CoreReason.BadRequest);
    }

    [Fact]
    [Trait("Id", "H-R7-4")]
    public void Move_AlreadyRemovedEntryId_IsStaleEntry_AndChangesNothing()
    {
        // Arrange
        var rig = new InvOpRig();

        // Act + Assert
        Assert.All(rig.RemovedIds, id => rig.AssertSingleReply(rig.MoveOp(id), rig.PlayerA, (ushort)InvReason.StaleEntry));
    }

    [Fact]
    public void Move_BadRequestPrecedesStaleEntry_WhenARemovedIdCarriesANonZeroPad()
    {
        // Arrange
        var rig = new InvOpRig();
        Op op = rig.MoveOp(rig.RemovedIds[0], pad0: 1);

        // Act + Assert
        rig.AssertSingleReply(op, rig.PlayerA, CoreReason.BadRequest);
    }

    [Theory]
    [InlineData("Move", 22, (byte)0x01)]
    [InlineData("Move", 23, (byte)0x80)]
    [InlineData("QuickMove", 9, (byte)0x01)]
    [InlineData("QuickMove", 10, (byte)0x80)]
    [InlineData("QuickMove", 11, (byte)0xFF)]
    public void PaddedPayload_NonZeroByteInEachPadPosition_IsBadRequest_OnAnOtherwiseValidOp(string kind, int offset, byte value)
    {
        // Arrange: a well-formed op on a live entry (Ok through the stub; stale_entry through the real Move, the entry being
        // unplaced), then one pad byte set in place.
        var rig = new InvOpRig();
        Op op = KindOp(rig, kind, rig.LiveIds[0], rig.LiveIds[1]);
        Assert.Equal(0, op.Payload[offset]);
        op.Payload[offset] = value;

        // Act + Assert
        rig.AssertSingleReply(op, rig.PlayerA, CoreReason.BadRequest);
    }

    [Fact]
    public void Move_RotatedOnASlotsLayout_IsBadRequest_ButOnAGridPassesTheRequestCheck()
    {
        // Arrange
        var rig = new InvOpRig();
        Op ontoHotbar = rig.MoveOp(rig.LiveIds[0], to: rig.HotbarA.Value, rotated: 1);
        Op ontoGrid = rig.MoveOp(rig.LiveIds[0], to: rig.InventoryA.Value, rotated: 1);
        Op rotatedTwo = rig.MoveOp(rig.LiveIds[0], to: rig.InventoryA.Value, rotated: 2);

        // Act + Assert: past bad_request the real Move answers stale_entry (the live entry is in no container)
        rig.AssertSingleReply(ontoHotbar, rig.PlayerA, CoreReason.BadRequest);
        rig.AssertSingleReply(ontoGrid, rig.PlayerA, (ushort)InvReason.StaleEntry);
        rig.AssertSingleReply(rotatedTwo, rig.PlayerA, CoreReason.BadRequest);
    }

    [Fact]
    public void Move_TempIdOfAnotherSlot_IsBadRequest_OwnUnmappedTempIdIsStaleEntry()
    {
        // Arrange
        var rig = new InvOpRig();
        uint tempOfB = EntryId.Temp(PlayerSlot.FromValue(1), 5).Value;
        uint tempOfA = EntryId.Temp(PlayerSlot.FromValue(0), 5).Value;

        // Act + Assert
        rig.AssertSingleReply(rig.MoveOp(tempOfB), rig.PlayerA, CoreReason.BadRequest);
        rig.AssertSingleReply(rig.MoveOp(tempOfA), rig.PlayerA, (ushort)InvReason.StaleEntry);
        rig.AssertSingleReply(rig.MoveOp(tempOfA), ActorId.System, CoreReason.BadRequest);
        rig.AssertSingleReply(rig.MoveOp(0x8080_0001u), rig.PlayerA, CoreReason.BadRequest); // bit 23 set: malformed temp
    }

    [Fact]
    [Trait("Id", "H-R7-4")]
    public void Move_DecodedCountMinusOneAndX70000_AreNotRepresentableInThePayload_SoTheWidenedEntryPointsAreTheGate()
    {
        // Arrange: what a codec that skipped the widened checks would store after narrowing.
        var rig = new InvOpRig();
        int decodedCount = -1;
        int decodedX = 70_000;
        Op narrowed = rig.MoveOp(rig.LiveIds[0], x: unchecked((short)decodedX), count: unchecked((byte)decodedCount));

        // Act
        ushort countGate = InvRequestCheck.CheckCount(decodedCount);
        ushort coordinateGate = InvRequestCheck.CheckCoordinate(decodedX);

        // Assert: the decoded values are bad_request at the entry points; once narrowed (255, 4464) they are representable and in range,
        // so the handler cannot see them as malformed (it passes bad_request and answers stale_entry for the unplaced entry) — this
        // test pins that boundary rather than claiming otherwise.
        Assert.Equal(CoreReason.BadRequest, countGate);
        Assert.Equal(CoreReason.BadRequest, coordinateGate);
        rig.AssertSingleReply(narrowed, rig.PlayerA, (ushort)InvReason.StaleEntry);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0xFFFF_FFFFu)]
    public void Move_ContainerZeroOrAboveTheContainerHighWater_IsBadRequest(uint container)
    {
        // Arrange
        var rig = new InvOpRig();

        // Act + Assert
        rig.AssertSingleReply(rig.MoveOp(rig.LiveIds[0], from: container), rig.PlayerA, CoreReason.BadRequest);
        rig.AssertSingleReply(rig.MoveOp(rig.LiveIds[0], to: container == 0 ? 0 : rig.Inventory.Containers.NextId), rig.PlayerA, CoreReason.BadRequest);
    }

    // ------------------------------------------------------------------ H-NET-5 (Sim half)

    [Fact]
    [Trait("Id", "H-NET-5-sim")]
    public void TenThousandOutOfRangeOps_FromARecordedSeed_ThroughTheDrain_AllBadRequest_StateUnchanged_NoException()
    {
        // Arrange
        var rig = new InvOpRig();
        var gen = new FaultGenerator(RecordedSeed, rig);
        var issued = new Dictionary<uint, OpKind>(FuzzOps);
        byte[] before = rig.Snapshot();
        int perTick = rig.Host.Content.Settings.Core.MaxOpsDrainedPerTick;
        int ticksBefore = rig.Sink.Ticks.Count;

        // Act
        Exception? escaped = Record.Exception(() =>
        {
            uint req = 1_000_000;
            while (issued.Count < FuzzOps)
            {
                for (int i = 0; i < perTick && issued.Count < FuzzOps; i++)
                {
                    req++;
                    (Op op, ActorId actor) = gen.Next(req);
                    Assert.True(rig.Host.World.EnqueueOp(actor, in op), "enqueue refused");
                    issued.Add(req, op.Kind);
                }

                rig.Host.World.Tick();
            }
        });
        Reply[] replies = rig.RepliesSince(ticksBefore);

        // Assert
        Assert.Null(escaped);
        Assert.Equal(FuzzOps, replies.Length);
        Assert.All(replies, r =>
        {
            Assert.Equal(issued[r.ReqId], r.Kind);
            Assert.Equal(ReplyResult.Rejected, r.Result);
            Assert.Equal(CoreReason.BadRequest, r.Reason);
        });
        Assert.Equal(FuzzOps, replies.Select(r => r.ReqId).Distinct().Count());
        // Return is payload-free: a structurally valid Return has no field only the Sim can judge, so the generator issues none (its one
        // bad_request, a non-zero payload length, is CheckRequest_ReturnWithANonZeroPayloadLength_IsBadRequest).
        foreach (OpKind bound in InventoryWithStubOps.BoundBySystem.Where(k => k != InvOpKinds.Return))
        {
            Assert.True(issued.Values.Count(k => k == bound) > 0, $"generator issued no {bound} op");
        }

        // Every op reached a handler: the stubbed kinds the stub's helper, Pick, Move, Merge, QuickMove, Return and Close their real handler (bad_request above).
        // A handler's reply carries its call scope's CallSeq; a Sim Core pre-check reject would carry 0.
        int boundOps = issued.Values.Count(k => Array.IndexOf(InventoryWithStubOps.BoundBySystem, k) >= 0);
        Assert.Equal(FuzzOps - boundOps, rig.StubCalls);
        Assert.Equal(boundOps, rig.HandlerCallsSince(ticksBefore, InventoryWithStubOps.BoundBySystem));
        Assert.Equal(FuzzOps, rig.HandlerCallsSince(ticksBefore, issued.Values.Distinct().ToArray()));
        Assert.True(gen.KindsSeen >= 8, $"generator covered only {gen.KindsSeen} kinds");
        Assert.Equal(before, rig.Snapshot());
        Assert.NotEqual(SimState.Faulted, rig.Host.World.State);
    }

    [Fact]
    [Trait("Id", "H-NET-5-sim")]
    public void FaultGenerator_RecordedSeed_First256Ops_MatchThePinnedGoldenHash()
    {
        // Arrange
        var rig = new InvOpRig();
        var gen = new FaultGenerator(RecordedSeed, rig);
        var bytes = new List<byte>();

        // Act: FNV-1a 64 over (kind u16, length u8, actor u32, 48 payload bytes) of each op.
        for (uint i = 0; i < 256; i++)
        {
            (Op op, ActorId actor) = gen.Next(i);
            bytes.AddRange(BitConverter.GetBytes((ushort)op.Kind));
            bytes.Add(op.PayloadLength);
            bytes.AddRange(BitConverter.GetBytes(actor.Value));
            ReadOnlySpan<byte> payload = op.Payload;
            bytes.AddRange(payload.ToArray());
        }

        ulong hash = Fnv1a64Hash.Hash(bytes.ToArray());

        // Assert: a change to the generator or the seed changes the recorded op stream; re-pin only on purpose.
        Assert.Equal(GoldenFuzzHash, hash);
    }

    // ------------------------------------------------------------------ per-kind pass / stale_entry through the drain

    // Pick, Move, Merge, QuickMove, Return and Close are not here: InventorySystem binds their real handlers (stories 007, 008, 010 and
    // 011), whose valid cases are in PickMoveTests, MoveSplitMergeTests, QuickMoveReturnTests and OpenSetTests (the rig's unplaced live
    // entry is stale_entry to the real Pick, Move, Merge and QuickMove, QuickMoveReturnTests.QuickMove_OnTheStubRigsUnplacedLiveEntry_IsStaleEntry;
    // the rig's empty cursor is cursor_empty to the real Return, QuickMoveReturnTests.Return_OnTheStubRigsEmptyCursor_IsCursorEmpty).
    public static TheoryData<string> ValidCases => new() { "Drop", "Pickup", "PickupDefaultEntry", "Open" };

    public static TheoryData<string> StaleCases => new() { "Pick", "Move", "Merge", "MergeTarget", "QuickMove", "Drop", "Pickup" };

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void ValidOp_PerKind_IsOkThroughTheDrainAndStub_AndChangesNothing(string kind)
    {
        // Arrange
        var rig = new InvOpRig();
        Op op = KindOp(rig, kind, rig.LiveIds[0], rig.LiveIds[1]);

        // Act + Assert
        rig.AssertSingleReply(op, rig.PlayerA, CoreReason.Ok);
    }

    [Theory]
    [MemberData(nameof(StaleCases))]
    public void RemovedEntryId_PerEntryNamingKind_IsStaleEntry_AndChangesNothing(string kind)
    {
        // Arrange: MergeTarget keeps EntryId live and removes only TargetEntryId.
        var rig = new InvOpRig();
        Op op = kind == "MergeTarget"
            ? KindOp(rig, "Merge", rig.LiveIds[0], rig.RemovedIds[0])
            : KindOp(rig, kind, rig.RemovedIds[0], rig.LiveIds[1]);

        // Act + Assert
        rig.AssertSingleReply(op, rig.PlayerA, (ushort)InvReason.StaleEntry);
    }

    // ------------------------------------------------------------------ the CheckRequest / ResolveEntries split and error edges

    [Fact]
    public void CheckRequest_RemovedIdBelowTheHighWater_PassesWithZero_AndOnlyResolveEntriesSaysStaleEntry()
    {
        // Arrange
        var rig = new InvOpRig();
        InvRequestCheck check = rig.Inventory.RequestCheck;
        Op op = rig.MoveOp(rig.RemovedIds[0]);

        // Act
        ushort first = check.CheckRequest(in op, PlayerSlot.FromValue(0));
        ushort second = check.ResolveEntries(in op);

        // Assert: CheckRequest never decides stale_entry, so a handler can run not_open between the two steps.
        Assert.Equal(0, first);
        Assert.Equal((ushort)InvReason.StaleEntry, second);
    }

    [Fact]
    public void CheckRequest_WrongPayloadLengthForTheKind_IsBadRequest_WhileTheDrainRejectsItEarlierAsUnknownKind()
    {
        // Arrange: a Move carrying a 4 B Drop payload.
        var rig = new InvOpRig();
        var op = new Op { Kind = InvOpKinds.Move, ReqId = 3 };
        op.SetPayload(new DropPayload { EntryId = rig.LiveIds[0] });

        // Act
        ushort direct = rig.Inventory.RequestCheck.CheckRequest(in op, PlayerSlot.FromValue(0));
        ushort resolve = rig.Inventory.RequestCheck.ResolveEntries(in op);

        // Assert
        Assert.Equal(CoreReason.BadRequest, direct);
        Assert.Equal(CoreReason.BadRequest, resolve);
        rig.AssertSingleReply(op, rig.PlayerA, CoreReason.UnknownKind);
        Assert.Equal(0, rig.StubCalls);
    }

    [Fact]
    public void CheckRequest_ReturnWithANonZeroPayloadLength_IsBadRequest()
    {
        // Arrange
        var rig = new InvOpRig();
        var op = new Op { Kind = InvOpKinds.Return, ReqId = 4 };
        op.SetPayload(new DropPayload { EntryId = 1 });

        // Act
        ushort reason = rig.Inventory.RequestCheck.CheckRequest(in op, PlayerSlot.FromValue(0));

        // Assert
        Assert.Equal(4, op.PayloadLength);
        Assert.Equal(CoreReason.BadRequest, reason);
    }

    [Theory]
    [InlineData((ushort)0x0001)] // Sim Core PlayerJoin
    [InlineData((ushort)0x0082)] // reserved MoveGroup
    [InlineData((ushort)0x00F0)] // unassigned
    [InlineData((ushort)0x0140)] // Throwing block
    public void CheckRequest_NonInventoryOrReservedKind_IsBadRequest(ushort kind)
    {
        // Arrange
        var rig = new InvOpRig();
        var empty = new Op { Kind = (OpKind)kind, ReqId = 5 };
        var withPayload = new Op { Kind = (OpKind)kind, ReqId = 6 };
        withPayload.SetPayload(new PickPayload { From = rig.InventoryA.Value, EntryId = rig.LiveIds[0] });

        // Act
        ushort a = rig.Inventory.RequestCheck.CheckRequest(in empty, PlayerSlot.FromValue(0));
        ushort b = rig.Inventory.RequestCheck.CheckRequest(in withPayload, PlayerSlot.FromValue(0));

        // Assert
        Assert.Equal(CoreReason.BadRequest, a);
        Assert.Equal(CoreReason.BadRequest, b);
    }

    private static Op KindOp(InvOpRig rig, string kind, uint entry, uint target)
    {
        uint inv = rig.InventoryA.Value;
        var op = new Op { ReqId = 11 };
        switch (kind)
        {
            case "Pick": op.Kind = InvOpKinds.Pick; op.SetPayload(new PickPayload { From = inv, EntryId = entry }); break;
            case "Move": return rig.MoveOp(entry, x: 1, count: 2);
            case "Merge": op.Kind = InvOpKinds.Merge; op.SetPayload(new MergePayload { From = inv, EntryId = entry, To = inv, TargetEntryId = target }); break;
            case "QuickMove": op.Kind = InvOpKinds.QuickMove; op.SetPayload(new QuickMovePayload { From = inv, EntryId = entry, Count = 1 }); break;
            case "Drop": op.Kind = InvOpKinds.Drop; op.SetPayload(new DropPayload { EntryId = entry }); break;
            case "Pickup": op.Kind = InvOpKinds.Pickup; op.SetPayload(new PickupPayload { WorldItemId = 7, EntryId = entry, ClaimedX = 1000, ClaimedZ = -2500 }); break;
            case "PickupDefaultEntry": op.Kind = InvOpKinds.Pickup; op.SetPayload(new PickupPayload { WorldItemId = 7, EntryId = 0 }); break;
            case "Open": op.Kind = InvOpKinds.Open; op.SetPayload(new OpenPayload { Container = rig.IntakeContainer.Value, ClaimedX = 5 }); break;
            default: op.Kind = InvOpKinds.Return; break;
        }

        return op;
    }

    /// <summary>A fixed-seed xorshift32 (ADR-0006 rule 6: no <c>System.Random</c>).</summary>
    private struct XorShift32
    {
        private uint _state;

        public XorShift32(uint seed)
        {
            _state = seed == 0u ? 0x9E37_79B9u : seed;
        }

        public uint Next()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public int Next(int bound) => (int)(Next() % (uint)bound);
    }

    /// <summary>
    /// Generates structurally valid inventory ops (registered kind, exact payload length, zero reserved byte) that each carry at least one
    /// field only the Sim can judge malformed, chosen per kind; the other fields are drawn from valid, removed and extreme-but-representable
    /// values (count 0/255, coordinates at the int16 limits, claimed positions at the int32 limits), so the expected reply is exactly
    /// <c>bad_request</c> for every op.
    /// </summary>
    private sealed class FaultGenerator
    {
        private static readonly OpKind[] _kinds =
        {
            InvOpKinds.Pick, InvOpKinds.Move, InvOpKinds.Merge, InvOpKinds.QuickMove, InvOpKinds.Drop, InvOpKinds.Pickup, InvOpKinds.Open, InvOpKinds.Close,
        };

        private readonly InvOpRig _rig;
        private readonly bool[] _seen = new bool[_kinds.Length];
        private XorShift32 _rng;

        public FaultGenerator(uint seed, InvOpRig rig)
        {
            _rng = new XorShift32(seed);
            _rig = rig;
        }

        public int KindsSeen => _seen.Count(s => s);

        public (Op Op, ActorId Actor) Next(uint req)
        {
            int k = _rng.Next(_kinds.Length);
            _seen[k] = true;
            int actorPick = _rng.Next(5);
            ActorId actor = actorPick == 0 ? ActorId.System : actorPick % 2 == 0 ? _rig.PlayerA : _rig.PlayerB;
            byte actorSlot = actor == _rig.PlayerA ? (byte)0 : (byte)1;
            var op = new Op { Kind = _kinds[k], ReqId = req };
            switch (_kinds[k])
            {
                case InvOpKinds.Pick:
                {
                    var p = new PickPayload { From = AnyContainer(), EntryId = AnyEntry(actorSlot) };
                    if (_rng.Next(2) == 0) { p.From = BadContainer(); } else { p.EntryId = BadEntry(actorSlot, actor); }
                    op.SetPayload(in p);
                    break;
                }

                case InvOpKinds.Move:
                {
                    var p = new MovePayload
                    {
                        From = AnyContainer(), EntryId = AnyEntry(actorSlot), To = AnyContainer(), BaseVersion = _rng.Next(),
                        X = ExtremeShort(), Y = ExtremeShort(), Rotated = 0, Count = ExtremeByte(),
                    };
                    switch (_rng.Next(6))
                    {
                        case 0: p.From = BadContainer(); break;
                        case 1: p.To = BadContainer(); break;
                        case 2: p.EntryId = BadEntry(actorSlot, actor); break;
                        case 3: p.Rotated = 1; p.To = _rng.Next(2) == 0 ? _rig.HotbarA.Value : _rig.CursorB.Value; break; // slots layouts
                        case 4: p.Rotated = (byte)(2 + _rng.Next(254)); break;
                        default: p.Pad0 = (byte)(1 + _rng.Next(255)); p.Pad1 = (byte)_rng.Next(256); break;
                    }

                    op.SetPayload(in p);
                    break;
                }

                case InvOpKinds.Merge:
                {
                    var p = new MergePayload { From = AnyContainer(), EntryId = AnyEntry(actorSlot), To = AnyContainer(), TargetEntryId = AnyEntry(actorSlot) };
                    switch (_rng.Next(4))
                    {
                        case 0: p.From = BadContainer(); break;
                        case 1: p.To = BadContainer(); break;
                        case 2: p.EntryId = BadEntry(actorSlot, actor); break;
                        default: p.TargetEntryId = BadEntry(actorSlot, actor); break;
                    }

                    op.SetPayload(in p);
                    break;
                }

                case InvOpKinds.QuickMove:
                {
                    var p = new QuickMovePayload { From = AnyContainer(), EntryId = AnyEntry(actorSlot), Count = ExtremeByte() };
                    switch (_rng.Next(3))
                    {
                        case 0: p.From = BadContainer(); break;
                        case 1: p.EntryId = BadEntry(actorSlot, actor); break;
                        default: if (_rng.Next(2) == 0) { p.Pad2 = 0x80; } else { p.Pad0 = (byte)(_rng.Next() | 1u); } break;
                    }

                    op.SetPayload(in p);
                    break;
                }

                case InvOpKinds.Drop:
                {
                    var p = new DropPayload { EntryId = BadEntry(actorSlot, actor) };
                    op.SetPayload(in p);
                    break;
                }

                case InvOpKinds.Pickup:
                {
                    var p = new PickupPayload { WorldItemId = 1u + (uint)_rng.Next(1000), EntryId = 0, ClaimedX = int.MinValue, ClaimedY = int.MaxValue, ClaimedZ = unchecked((int)_rng.Next()) };
                    if (_rng.Next(2) == 0) { p.WorldItemId = 0; } else { p.EntryId = BadEntry(actorSlot, actor, allowZero: false); }
                    op.SetPayload(in p);
                    break;
                }

                case InvOpKinds.Open:
                {
                    var p = new OpenPayload { Container = BadContainer(), ClaimedX = int.MaxValue, ClaimedY = int.MinValue, ClaimedZ = unchecked((int)_rng.Next()) };
                    op.SetPayload(in p);
                    break;
                }

                default:
                {
                    var p = new ClosePayload { Container = BadContainer() };
                    op.SetPayload(in p);
                    break;
                }
            }

            return (op, actor);
        }

        private uint AnyContainer() => 1u + (uint)_rng.Next((int)_rig.Inventory.Containers.NextId - 1);

        private uint BadContainer()
        {
            uint high = _rig.Inventory.Containers.NextId;
            return _rng.Next(4) switch
            {
                0 => 0u,
                1 => high,
                2 => uint.MaxValue,
                _ => high + (_rng.Next() % (uint.MaxValue - high)),
            };
        }

        // A valid-namespace id: live, removed, or the actor's own (unmapped) temp id.
        private uint AnyEntry(byte actorSlot) => _rng.Next(3) switch
        {
            0 => _rig.LiveIds[_rng.Next(_rig.LiveIds.Length)],
            1 => _rig.RemovedIds[_rng.Next(_rig.RemovedIds.Length)],
            _ => EntryId.Temp(PlayerSlot.FromValue(actorSlot), _rng.Next() & EntryId.TempCounterMask).Value,
        };

        private uint BadEntry(byte actorSlot, ActorId actor, bool allowZero = true)
        {
            uint last = _rig.Inventory.Entries.LastId;
            int pick = _rng.Next(allowZero ? 5 : 4);
            byte otherSlot = (byte)((actorSlot + 1 + _rng.Next(PlayerSlot.Count - 1)) % PlayerSlot.Count);
            return pick switch
            {
                0 => last + 1 + (_rng.Next() % (EntryPool.MaxRealId - last)), // real, above the high-water
                1 => EntryId.Temp(PlayerSlot.FromValue(actor.IsSystem ? actorSlot : otherSlot), _rng.Next() & EntryId.TempCounterMask).Value, // System has no namespace
                2 => EntryId.TempFlag | ((1u + (_rng.Next() % 0xFFu)) << 23) | (_rng.Next() & 0x7F_FFFFu), // reserved bits 23-30 set
                3 => EntryPool.MaxRealId,
                _ => 0u,
            };
        }

        private short ExtremeShort() => _rng.Next(4) switch { 0 => short.MinValue, 1 => short.MaxValue, 2 => -1, _ => unchecked((short)_rng.Next(65_536)) };

        private byte ExtremeByte() => _rng.Next(3) switch { 0 => 0, 1 => byte.MaxValue, _ => (byte)_rng.Next(256) };
    }
}

/// <summary>
/// TEST-ONLY op handler (story grid-inventory/004): the whole body is the shared production steps of an inventory handler's
/// <c>Validate</c> — <see cref="InvRequestCheck.CheckRequest"/> (bad_request), then <see cref="InvRequestCheck.ResolveEntries"/>
/// (stale_entry), with no <c>not_open</c> / P13 step between them because none exists until stories 008-010 — with the actor's slot
/// resolved from <see cref="Op.Actor"/>. It never mutates,
/// so a pass is <c>Ok</c> with nothing changed. Replaced by the real handlers of stories 008-010.
/// </summary>
internal sealed class InvValidateOnlyStub : IOpHandler
{
    private readonly InvRequestCheck _check;
    private readonly ISlotView _slots;

    public InvValidateOnlyStub(OpKind kind, InvRequestCheck check, ISlotView slots)
    {
        Kind = kind;
        _check = check;
        _slots = slots;
    }

    public OpKind Kind { get; }

    public int Calls { get; private set; }

    public Reply Apply(in Op op, in TickContext ctx)
    {
        Calls++;
        PlayerSlot slot = op.Actor.IsSystem ? PlayerSlot.None : _slots.SlotOf(new EntityId(op.Actor.Value));
        ushort reason = _check.CheckRequest(in op, slot);
        if (reason == 0)
        {
            reason = _check.ResolveEntries(in op);
        }

        return reason == 0 ? Reply.Ok(in op) : Reply.Reject(in op, reason);
    }
}

/// <summary>
/// TEST-ONLY system: the real <see cref="InventorySystem"/> plus a <see cref="InvValidateOnlyStub"/> for every M-1a inventory descriptor
/// the system does not bind itself (<see cref="BoundBySystem"/>; binding a second handler throws <c>DuplicateOpHandler</c>).
/// </summary>
internal sealed class InventoryWithStubOps : ISimSystem
{
    /// <summary>The kinds <see cref="InventorySystem.Bind"/> binds a production handler for (story 007: <c>Pick</c>, <c>Move</c>; story 008: <c>Merge</c>; story 010: <c>QuickMove</c>, <c>Return</c>; story 011: <c>Close</c>).</summary>
    public static readonly OpKind[] BoundBySystem = { InvOpKinds.Pick, InvOpKinds.Move, InvOpKinds.Merge, InvOpKinds.QuickMove, InvOpKinds.Return, InvOpKinds.Close };

    private readonly ISlotView _slots;

    public InventoryWithStubOps(InventorySystem inner, ISlotView slots)
    {
        Inner = inner;
        _slots = slots;
    }

    public InventorySystem Inner { get; }

    public List<InvValidateOnlyStub> Stubs { get; } = new();

    public void Bind(SimWorldBuilder builder)
    {
        Inner.Bind(builder);
        foreach (OpDescriptor d in InvOpDescriptors.All)
        {
            if (Array.IndexOf(BoundBySystem, d.Kind) >= 0)
            {
                continue;
            }

            var stub = new InvValidateOnlyStub(d.Kind, Inner.RequestCheck, _slots);
            Stubs.Add(stub);
            builder.BindOp(d, stub);
        }
    }
}

/// <summary>
/// A composed M-1a world (real content, real <see cref="InventorySystem"/>, Sim Core's own <c>PlayerJoin</c>) with players in slots 0 and 1,
/// six allocated (never placed) entries of which ids 2 and 5 are removed again (so the pool's high-water is 6), and the inventory kinds
/// other than <c>Pick</c>, <c>Move</c>, <c>Merge</c>, <c>QuickMove</c>, <c>Return</c> and <c>Close</c> bound to stubs. The system gets the world's slot view (the real handlers
/// resolve the actor's slot through it).
/// </summary>
internal sealed class InvOpRig
{
    private readonly InventoryWithStubOps _system;

    public InvOpRig()
    {
        Sink = new CaptureSink();
        var profile = SimProfile.ForTest(
            "inventory-op-stubs",
            null,
            new SimSystemEntry(
                SystemId.GridInventory,
                ctx => new InventoryWithStubOps(
                    new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, InventorySettings.FromBalance(ctx.Content.Inventory), slots: ctx.Slots), ctx.Slots)));
        Host = SimComposition.Build(new SimBuildArgs(profile, BuiltInContent.CreateM1a(), 42) { EventSink = Sink });
        _system = (InventoryWithStubOps)Host.Systems.Single();
        Join(0, 1);
        Join(1, 2);
        PlayerA = ActorId.FromEntity(Host.Services.Slots.Entry(PlayerSlot.FromValue(0)).PlayerId);
        PlayerB = ActorId.FromEntity(Host.Services.Slots.Entry(PlayerSlot.FromValue(1)).PlayerId);
        HotbarA = Inventory.HotbarOf(PlayerSlot.FromValue(0));
        InventoryA = Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        CursorB = Inventory.CursorOf(PlayerSlot.FromValue(1));

        EntryPool pool = Inventory.Entries;
        var slots = new ushort[6];
        for (int i = 0; i < slots.Length; i++)
        {
            Assert.True(pool.TryAllocate(Host.World.CurrentTick, out slots[i], out _));
        }

        pool.Free(slots[1]);
        pool.Free(slots[4]);
        Inventory.Ledger.Recount(Inventory); // story 017: arranged outside the mutator
        LiveIds = new uint[] { 1, 3, 4, 6 };
        RemovedIds = new uint[] { 2, 5 };
    }

    public SimHost Host { get; }

    public CaptureSink Sink { get; }

    public InventorySystem Inventory => _system.Inner;

    public ActorId PlayerA { get; }

    public ActorId PlayerB { get; }

    public ContainerId HotbarA { get; }

    public ContainerId InventoryA { get; }

    public ContainerId CursorB { get; }

    public ContainerId IntakeContainer => Inventory.IntakeContainer;

    public uint[] LiveIds { get; }

    public uint[] RemovedIds { get; }

    public int StubCalls => _system.Stubs.Sum(s => s.Calls);

    /// <summary>A Move from player A's inventory to its own inventory with in-range defaults.</summary>
    public Op MoveOp(uint entryId, uint? from = null, uint? to = null, short x = 0, byte count = 0, byte rotated = 0, byte pad0 = 0)
    {
        var op = new Op { Kind = InvOpKinds.Move, ReqId = 9 };
        op.SetPayload(new MovePayload
        {
            From = from ?? InventoryA.Value, EntryId = entryId, To = to ?? InventoryA.Value, X = x, Y = 0, Count = count, Rotated = rotated, Pad0 = pad0,
        });
        return op;
    }

    /// <summary>Enqueues one op, ticks, and asserts the reply reason and that the inventory state did not change.</summary>
    public void AssertSingleReply(in Op op, ActorId actor, ushort expectedReason)
    {
        byte[] before = Snapshot();
        int from = Sink.Ticks.Count;
        Assert.True(Host.World.EnqueueOp(actor, in op));
        Host.World.Tick();
        Reply reply = Assert.Single(RepliesSince(from));
        Assert.Equal(expectedReason, reply.Reason);
        Assert.Equal(expectedReason == 0 ? ReplyResult.Ok : ReplyResult.Rejected, reply.Result);
        Assert.Equal(before, Snapshot());
    }

    public Reply[] RepliesSince(int tickIndex) => Sink.Ticks.Skip(tickIndex).SelectMany(t => t)
        .Where(e => e.Kind == EventKind.OpReply).Select(e => e.PayloadAs<Reply>()).ToArray();

    /// <summary>The replies since <paramref name="tickIndex"/> of the given kinds that a handler produced (a non-zero call scope; a Sim Core pre-check reject has none).</summary>
    public int HandlerCallsSince(int tickIndex, OpKind[] kinds) => Sink.Ticks.Skip(tickIndex).SelectMany(t => t)
        .Count(e => e.Kind == EventKind.OpReply && e.CallSeq != 0 && Array.IndexOf(kinds, e.PayloadAs<Reply>().Kind) >= 0);

    /// <summary>The inventory canonical dump section, every container's occupancy counters, the entry pool and the id allocators, as bytes.</summary>
    public byte[] Snapshot()
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new CanonicalWriter(output, new CanonicalBuffer());
        writer.BeginSection(SectionIds.Inventory);
        new InventoryStateContributor(Inventory.Containers).Write(ref writer);
        writer.EndSection();
        var bytes = new List<byte>(output.WrittenSpan.ToArray());
        ContainerTable containers = Inventory.Containers;
        for (int s = 0; s < containers.LiveCount; s++)
        {
            ContainerHeader h = containers[(ushort)s];
            bytes.AddRange(BitConverter.GetBytes(h.FreeCells));
            bytes.AddRange(BitConverter.GetBytes(h.EntryCount));
        }

        EntryPool pool = Inventory.Entries;
        bytes.AddRange(BitConverter.GetBytes(pool.LastId));
        bytes.AddRange(BitConverter.GetBytes(pool.LiveCount));
        for (int s = 0; s < pool.Capacity; s++)
        {
            EntryRecord r = pool[(ushort)s];
            bytes.AddRange(BitConverter.GetBytes(r.Id.Value));
            bytes.Add(r.Count);
            bytes.AddRange(BitConverter.GetBytes(r.ContainerSlot));
        }

        for (int c = 0; c < 256; c++)
        {
            bytes.AddRange(BitConverter.GetBytes(Host.Services.EntityIds.Peek((EntityClass)c)));
        }

        bytes.AddRange(BitConverter.GetBytes(Host.Services.MailIds.Peek()));
        return bytes.ToArray();
    }

    private void Join(byte slot, uint req)
    {
        var op = new Op { Kind = OpKind.PlayerJoin, ReqId = req };
        op.SetPayload(new PlayerJoinPayload { Slot = slot });
        Assert.True(Host.World.EnqueueOp(ActorId.System, in op));
        Host.World.Tick();
    }
}
