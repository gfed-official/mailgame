using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Ops;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/004 (TR-inventory-006, TR-inventory-025; ADR-0009 sections 1, 2 and 4; ADR-0007 sections 1, 4 and 5): the M-1a
/// inventory op kinds and their padding-free payloads (story pad-rule table), the reserved M1 values, the <see cref="InvReason"/> snapshot,
/// the descriptors' reason declarations and the 24 B <see cref="InvReplyExtra"/>. The malformed-op behaviour (H-NET-5, H-R7-4) is in
/// <see cref="InvMalformedOpTests"/>.
/// </summary>
public class InvOpKindTests
{
    private const int InventoryPayloadCeiling = 32;

    public static TheoryData<ushort, int, Type?> SizeTable => new()
    {
        { (ushort)InvOpKinds.Pick, 8, typeof(PickPayload) },
        { (ushort)InvOpKinds.Move, 24, typeof(MovePayload) },
        { (ushort)InvOpKinds.Merge, 16, typeof(MergePayload) },
        { (ushort)InvOpKinds.QuickMove, 12, typeof(QuickMovePayload) },
        { (ushort)InvOpKinds.Return, 0, null },
        { (ushort)InvOpKinds.Drop, 4, typeof(DropPayload) },
        { (ushort)InvOpKinds.Pickup, 20, typeof(PickupPayload) },
        { (ushort)InvOpKinds.Open, 16, typeof(OpenPayload) },
        { (ushort)InvOpKinds.Close, 4, typeof(ClosePayload) },
    };

    public static TheoryData<ushort> AllKinds => new(InvOpDescriptors.All.Select(d => (ushort)d.Kind));

    private static OpDescriptor DescriptorOf(ushort kind) => InvOpDescriptors.All.Single(d => d.Kind == (OpKind)kind);

    // ------------------------------------------------------------------ AC 1: kinds and payloads

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void EveryDescriptor_PassesTheSharedDescriptorCheck_AndFitsThe32ByteCeiling(ushort kind)
    {
        // Arrange
        OpDescriptor d = DescriptorOf(kind);

        // Act
        DescriptorAssert.Check(d, OpKindBlock.Inventory);

        // Assert
        Assert.True(d.PayloadLength <= InventoryPayloadCeiling, $"{d.Kind} is {d.PayloadLength} B");
        Assert.True(d.PayloadLength <= DescriptorAssert.MaxPayloadBytes);
        Assert.False(d.SystemOnly);
    }

    [Theory]
    [MemberData(nameof(SizeTable))]
    public void EveryDescriptor_MatchesTheStorySizeTable_FieldSumSizeOfAndPayloadLength(ushort kind, int size, Type? payload)
    {
        // Arrange
        OpDescriptor d = DescriptorOf(kind);

        // Act
        int sizeOf = payload is null ? 0 : DescriptorAssert.SizeOf(payload);
        int fieldSum = payload is null ? 0 : DescriptorAssert.SumOfFieldSizes(payload);

        // Assert
        Assert.Equal(payload, d.PayloadType);
        Assert.Equal(size, d.PayloadLength);
        Assert.Equal(size, sizeOf);
        Assert.Equal(size, fieldSum);
        Assert.Equal(0, size % 4);
    }

    [Fact]
    public void Return_IsPayloadFree_LengthZeroAndNoPayloadType()
    {
        // Act
        OpDescriptor d = InvOpDescriptors.Return;

        // Assert: the explicit exemption (Unsafe.SizeOf of an empty struct is 1, so Return has no payload type at all).
        Assert.Equal(InvOpKinds.Return, d.Kind);
        Assert.Equal(0, d.PayloadLength);
        Assert.Null(d.PayloadType);
    }

    [Fact]
    public void KindValues_RegisteredAndReserved_AreDistinctAndInsideTheInventoryBlock()
    {
        // Arrange
        OpKind[] registered = InvOpDescriptors.All.Select(d => d.Kind).ToArray();
        OpKind[] reserved = InvOpKinds.Reserved.ToArray();
        OpKind[] declared = typeof(InvOpKinds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(OpKind)).Select(f => (OpKind)f.GetRawConstantValue()!).ToArray();

        // Act
        OpKind[] all = registered.Concat(reserved).ToArray();

        // Assert
        Assert.Equal(9, registered.Length);
        Assert.Equal(5, reserved.Length);
        Assert.Equal(all.Length, all.Distinct().Count());
        Assert.Equal(declared.Order(), all.Order());
        Assert.All(all, k => Assert.True(OpKindBlock.Inventory.Contains(k), $"0x{(ushort)k:X4}"));
        Assert.All(all, k => Assert.False(Enum.IsDefined(k), "collides with a Sim Core OpKind member"));
        Assert.DoesNotContain((OpKind)0x00F0, all); // the value Sim Core tests use as "unregistered"
        Assert.Equal(registered.Order(), registered);
        Assert.Equal(
            new[] { InvOpKinds.MoveGroup, InvOpKinds.Split, InvOpKinds.QuickMoveGroup, InvOpKinds.TakeAll, InvOpKinds.Sort }.Order(),
            reserved.Order());
    }

    [Theory]
    [InlineData((ushort)InvOpKinds.Move)]
    [InlineData((ushort)InvOpKinds.Merge)]
    [InlineData((ushort)InvOpKinds.QuickMove)]
    [InlineData((ushort)InvOpKinds.Pickup)]
    [InlineData((ushort)InvOpKinds.Return)]
    public void BlockAcquiringKinds_DeclareCapacityExhausted85(ushort kind)
    {
        // Act
        OpDescriptor d = DescriptorOf(kind);

        // Assert
        Assert.Contains(new ReasonDef(85, "capacity_exhausted"), d.Reasons);
    }

    [Theory]
    [InlineData((ushort)InvOpKinds.Pick)]
    [InlineData((ushort)InvOpKinds.Drop)]
    [InlineData((ushort)InvOpKinds.Open)]
    [InlineData((ushort)InvOpKinds.Close)]
    public void NonAcquiringKinds_DoNotDeclareCapacityExhausted(ushort kind)
    {
        // Act
        OpDescriptor d = DescriptorOf(kind);

        // Assert
        Assert.DoesNotContain(d.Reasons, r => r.Code == 85);
    }

    [Fact]
    public void MovePayload_PutsTheU32sFirst_ThenTheNarrowFields_ThenThePad()
    {
        // Arrange
        (string Field, int Offset)[] expected =
        {
            ("From", 0), ("EntryId", 4), ("To", 8), ("BaseVersion", 12), ("X", 16), ("Y", 18), ("Rotated", 20), ("Count", 21),
            ("Pad0", 22), ("Pad1", 23),
        };

        // Act
        int[] actual = expected.Select(e => (int)Marshal.OffsetOf<MovePayload>(e.Field)).ToArray();

        // Assert
        Assert.Equal(expected.Select(e => e.Offset), actual);
    }

    [Fact]
    public void PaddedPayloads_WrittenThroughOp_HaveDataBytesThenZeroPadBytesAtTheTableOffsets()
    {
        // Arrange
        var move = new MovePayload { From = uint.MaxValue, EntryId = uint.MaxValue, To = uint.MaxValue, BaseVersion = uint.MaxValue, X = -1, Y = -1, Rotated = 0xFF, Count = 0xFF };
        var quick = new QuickMovePayload { From = uint.MaxValue, EntryId = uint.MaxValue, Count = 0xFF };
        var moveOp = new Op { Kind = InvOpKinds.Move };
        var quickOp = new Op { Kind = InvOpKinds.QuickMove };

        // Act
        moveOp.SetPayload(in move);
        quickOp.SetPayload(in quick);
        ReadOnlySpan<byte> moveBytes = moveOp.Payload;
        ReadOnlySpan<byte> quickBytes = quickOp.Payload;

        // Assert
        Assert.Equal(24, moveOp.PayloadLength);
        Assert.Equal(12, quickOp.PayloadLength);
        Assert.True(moveBytes.Slice(0, 22).ToArray().All(b => b == 0xFF));
        Assert.Equal(new byte[2], moveBytes.Slice(22, 2).ToArray());
        Assert.True(quickBytes.Slice(0, 9).ToArray().All(b => b == 0xFF));
        Assert.Equal(new byte[3], quickBytes.Slice(9, 3).ToArray());
        MovePayload readMove = moveOp.PayloadAs<MovePayload>();
        Assert.Equal(0, readMove.Pad0 | readMove.Pad1);
    }

    [Fact]
    public void NoPayload_CarriesAnIdentityOrDependsOnField()
    {
        // Arrange
        string[] forbidden = { "actor", "player", "slot", "dependson", "reqid" };

        // Act
        string[] names = InvOpDescriptors.All.Where(d => d.PayloadType is not null)
            .SelectMany(d => d.PayloadType!.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .Select(f => f.Name.ToLowerInvariant()).ToArray();

        // Assert
        Assert.NotEmpty(names);
        Assert.DoesNotContain(names, n => forbidden.Any(n.Contains));
    }

    // ------------------------------------------------------------------ AC 2: reason codes

    private static readonly (string Name, ushort Value, string Snake)[] _adrReasons =
    {
        ("NotOpen", 64, "not_open"), ("OutOfRange", 65, "out_of_range"), ("PositionStale", 66, "position_stale"),
        ("StaleEntry", 67, "stale_entry"), ("StaleLayout", 68, "stale_layout"), ("CursorBusy", 69, "cursor_busy"),
        ("CursorEmpty", 70, "cursor_empty"), ("Blocked", 71, "blocked"), ("OutOfBounds", 72, "out_of_bounds"),
        ("NotAllowed", 73, "not_allowed"), ("NotCarryable", 74, "not_carryable"), ("Swimming", 75, "swimming"),
        ("StackFull", 76, "stack_full"), ("NoRoom", 77, "no_room"), ("BadCount", 78, "bad_count"),
        ("Unsplittable", 79, "unsplittable"), ("ReservedSlot", 80, "reserved_slot"), ("SortFailed", 81, "sort_failed"),
        ("ContainerGone", 82, "container_gone"), ("NoTarget", 83, "no_target"), ("WalletFloor", 84, "wallet_floor"),
        ("CapacityExhausted", 85, "capacity_exhausted"),
    };

    [Fact]
    public void InvReason_NameValuePairs_EqualTheAdr0009ListExactly()
    {
        // Act
        (string, ushort)[] actual = Enum.GetValues<InvReason>().Select(r => (r.ToString(), (ushort)r)).ToArray();

        // Assert: fails on any rename, renumber, addition, removal or reuse.
        Assert.Equal(_adrReasons.Select(r => (r.Name, r.Value)), actual);
        Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(InvReason)));
        Assert.Equal(actual.Length, actual.Select(a => a.Item2).Distinct().Count());
    }

    [Fact]
    public void BadRequest_IsSimCoresGlobalCode8_AndNotAnInventoryValue()
    {
        // Assert
        Assert.Equal(8, InvReasons.BadRequest);
        Assert.Equal(CoreReason.BadRequest, InvReasons.BadRequest);
        Assert.False(Enum.IsDefined(typeof(InvReason), (ushort)8));
    }

    [Fact]
    public void ReasonNames_AndEveryDescriptorReasonDef_UseTheAdrSnakeCaseNames()
    {
        // Arrange
        Dictionary<ushort, string> snake = _adrReasons.ToDictionary(r => r.Value, r => r.Snake);

        // Act
        ReasonDef[] declared = InvOpDescriptors.All.SelectMany(d => d.Reasons).ToArray();

        // Assert
        Assert.All(_adrReasons, r => Assert.Equal(r.Snake, InvReasons.Name((InvReason)r.Value)));
        Assert.All(declared, r => Assert.Equal(snake[r.Code], r.Name));
        Assert.All(InvOpDescriptors.All, d => Assert.Contains(d.Reasons, r => r.Code >= 64));
    }

    // ------------------------------------------------------------------ AC 3: InvReplyExtra

    [Fact]
    public void InvReplyExtra_Is24Bytes_ExplicitLayout_EqualToReplyExtra()
    {
        // Assert
        Assert.Equal(24, Unsafe.SizeOf<InvReplyExtra>());
        Assert.Equal(ReplyExtra.Size, Unsafe.SizeOf<InvReplyExtra>());
        Assert.Equal(24, Marshal.SizeOf<InvReplyExtra>());
        Assert.True(typeof(InvReplyExtra).IsExplicitLayout);
        Assert.Equal(24, DescriptorAssert.SumOfFieldSizes(typeof(InvReplyExtra)));
    }

    [Theory]
    [InlineData("ResultVersionA", 0)]
    [InlineData("ResultVersionB", 4)]
    [InlineData("ByActorId", 8)]
    [InlineData("RequestedUnits", 12)]
    [InlineData("GotUnits", 14)]
    [InlineData("RequestedEntries", 16)]
    [InlineData("GotEntries", 18)]
    [InlineData("SkippedEntries", 20)]
    [InlineData("CauseAndFlags", 22)]
    [InlineData("SkipReason", 23)]
    public void InvReplyExtra_FieldOffset_IsTheAdrOffset(string field, int offset)
    {
        // Act
        FieldInfo info = typeof(InvReplyExtra).GetField(field)!;

        // Assert
        Assert.Equal(offset, info.GetCustomAttribute<FieldOffsetAttribute>()!.Value);
        Assert.Equal(offset, (int)Marshal.OffsetOf<InvReplyExtra>(field));
        Assert.Equal(10, typeof(InvReplyExtra).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length);
    }

    [Theory]
    [InlineData(65_536, 65_535)]
    [InlineData(65_535, 65_535)]
    [InlineData(12, 12)]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    public void Saturate_ClampsCountsToTheU16Range(int value, int expected)
    {
        // Act
        ushort actual = InvReplyExtra.Saturate(value);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SetUnitsAndSetEntries_SaturateEveryCount()
    {
        // Arrange
        var extra = default(InvReplyExtra);

        // Act
        extra.SetUnits(70_000, 3);
        extra.SetEntries(-5, 100_000, 7);

        // Assert
        Assert.Equal((ushort)65_535, extra.RequestedUnits);
        Assert.Equal((ushort)3, extra.GotUnits);
        Assert.Equal((ushort)0, extra.RequestedEntries);
        Assert.Equal((ushort)65_535, extra.GotEntries);
        Assert.Equal((ushort)7, extra.SkippedEntries);
    }

    [Fact]
    public void InvReplyExtra_RoundTripsThroughReplyExtra_AtTheAdrByteOffsets()
    {
        // Arrange
        var op = new Op { Kind = InvOpKinds.Move, ReqId = 77 };
        var extra = new InvReplyExtra
        {
            ResultVersionA = 0x0403_0201, ResultVersionB = 0x0807_0605, ByActorId = 0x0C0B_0A09, RequestedUnits = 0x0E0D, GotUnits = 0x100F,
            RequestedEntries = 0x1211, GotEntries = 0x1413, SkippedEntries = 0x1615, CauseAndFlags = 0x17, SkipReason = 0x18,
        };

        // Act
        Reply reply = Reply.Ok(in op, in extra);
        InvReplyExtra back = reply.ExtraAs<InvReplyExtra>();
        ReadOnlySpan<byte> raw = reply.Extra;

        // Assert
        Assert.Equal(extra, back);
        Assert.Equal(Enumerable.Range(1, 24).Select(i => (byte)i), raw.ToArray()); // little-endian, field-by-field at the ADR offsets
        Assert.True(reply.IsOk);
    }
}
