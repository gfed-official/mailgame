using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Covers TR-sim-015 and TR-sim-020 for story 013 (ADR-0007 sections 1, 4, 5): the <see cref="Op"/> and
/// <see cref="Reply"/> layouts (AC-1), payload round trips (AC-2) and the shared descriptor check with its
/// negative cases (AC-3). No time, I/O, randomness or global state.
/// </summary>
public class OpTypesTests
{
    /// <summary>12-byte padding-free payload (3 x u32).</summary>
    public struct Payload12
    {
        public uint A;
        public uint B;
        public uint C;
    }

    /// <summary>48-byte padding-free payload (6 x u64, 8-aligned).</summary>
    public struct Payload48
    {
        public ulong A;
        public ulong B;
        public ulong C;
        public ulong D;
        public ulong E;
        public ulong F;
    }

    /// <summary>49-byte packed payload: one byte over the limit.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Payload49
    {
        public Payload48 Body;
        public byte Extra;
    }

    /// <summary>8-byte payload whose fields sum to 5: three padding bytes.</summary>
    public struct PaddedPayload
    {
        public byte A;
        public uint B;
    }

    /// <summary>Payload with a nested struct that itself has padding (hidden in the sum unless recursed).</summary>
    public struct NestedPaddedPayload
    {
        public PaddedPayload Inner;
        public uint Tail;
    }

    /// <summary>Payload holding a reference-typed field (not unmanaged; built only to test the helper).</summary>
    public struct RefPayload
    {
        public string Name;
    }

    /// <summary>Payload whose 8-byte field sits at offset 4: forces unaligned access.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct MisalignedPayload
    {
        public uint Lead;
        public ulong Wide;
    }

    /// <summary>256-byte payload: its size truncates to 0 as a byte.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Payload256
    {
        public Payload48 A;
        public Payload48 B;
        public Payload48 C;
        public Payload48 D;
        public Payload48 E;
        public ulong F;
        public ulong G;
        public ulong H;
        public ulong I;
    }

    /// <summary>16-byte payload holding an Int128.</summary>
    public struct Int128Payload
    {
        public Int128 Value;
    }

    /// <summary>Payload with a nested Int128 two levels down.</summary>
    public struct NestedInt128Payload
    {
        public uint Lead;
        public Int128Payload Inner;
    }

    /// <summary>8-byte payload with a float.</summary>
    public struct FloatPayload
    {
        public float Value;
        public uint Tail;
    }

    /// <summary>Payload with a platform-sized integer.</summary>
    public struct NativeIntPayload
    {
        public nint Value;
    }

    /// <summary>8-byte auto-layout payload (no stable field order).</summary>
    [StructLayout(LayoutKind.Auto)]
    public struct AutoPayload
    {
        public uint A;
        public uint B;
    }

    /// <summary>Payload nesting an auto-layout struct.</summary>
    public struct NestedAutoPayload
    {
        public AutoPayload Inner;
        public uint Tail;
    }

    /// <summary>Reply details of 16 bytes.</summary>
    public struct Extra16
    {
        public ulong A;
        public ulong B;
    }

    internal static Payload12 Sample12() => new() { A = 0x11223344u, B = 0xDEADBEEFu, C = 0xFFFFFFFFu };

    internal static Payload48 Sample48() => new()
    {
        A = 0x0102030405060708UL,
        B = ulong.MaxValue,
        C = 3,
        D = 0x8000000000000000UL,
        E = 5,
        F = 0xA5A5A5A5A5A5A5A5UL,
    };

    private static int Off<TField>(ref byte origin, ref TField field)
        where TField : unmanaged
    {
        return (int)Unsafe.ByteOffset(ref origin, ref Unsafe.As<TField, byte>(ref field));
    }

    private static ReadOnlySpan<byte> BytesOf<T>(ref T value)
        where T : unmanaged
    {
        return MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1));
    }

    // ---------------------------------------------------------------- AC-1: layouts

    [Fact]
    public void Ac1_Op_Is64Bytes_WithHeaderOffsets0_4_8_10_11_12_AndPayloadAt16()
    {
        var op = default(Op);
        ref byte origin = ref Unsafe.As<Op, byte>(ref op);

        Assert.Equal(64, Unsafe.SizeOf<Op>());
        Assert.Equal(Op.SizeBytes, Unsafe.SizeOf<Op>());
        Assert.Equal(0, Off(ref origin, ref op.OpSeq));
        Assert.Equal(4, Off(ref origin, ref op.Actor));
        Assert.Equal(8, Off(ref origin, ref op.Kind));
        Assert.Equal(10, Off(ref origin, ref op.PayloadLength));
        Assert.Equal(11, Off(ref origin, ref op.Reserved));
        Assert.Equal(12, Off(ref origin, ref op.ReqId));
        Assert.Equal(16, Off(ref origin, ref op.Payload));
    }

    [Fact]
    public void Ac1_Op_IsPadding_Free_AndBlittable()
    {
        Assert.Equal(64, DescriptorAssert.SumOfFieldSizes(typeof(Op)));
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<Op>());
        Assert.Equal(4, typeof(Op).StructLayoutAttribute!.Pack);
        Assert.Equal(LayoutKind.Sequential, typeof(Op).StructLayoutAttribute!.Value);
    }

    [Fact]
    public void Ac1_Reply_Is36Bytes_WithExtraAtOffset12_AndExplicitZeroPadFields()
    {
        var reply = default(Reply);
        ref byte origin = ref Unsafe.As<Reply, byte>(ref reply);

        Assert.Equal(36, Unsafe.SizeOf<Reply>());
        Assert.Equal(Reply.SizeBytes, Unsafe.SizeOf<Reply>());
        Assert.Equal(0, Off(ref origin, ref reply.ReqId));
        Assert.Equal(4, Off(ref origin, ref reply.Kind));
        Assert.Equal(6, Off(ref origin, ref reply.Reason));
        Assert.Equal(8, Off(ref origin, ref reply.Result));
        Assert.Equal(9, Off(ref origin, ref reply.Pad0));
        Assert.Equal(10, Off(ref origin, ref reply.Pad1));
        Assert.Equal(11, Off(ref origin, ref reply.Pad2));
        Assert.Equal(12, Off(ref origin, ref reply.Extra));
        Assert.Equal(36, DescriptorAssert.SumOfFieldSizes(typeof(Reply)));
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<Reply>());
        Assert.Equal(1, typeof(Reply).StructLayoutAttribute!.Pack);
    }

    [Fact]
    public void Ac1_PayloadAndExtraAreas_Are48And24Bytes()
    {
        Assert.Equal(48, Unsafe.SizeOf<OpPayload>());
        Assert.Equal(48, OpPayload.Size);
        Assert.Equal(24, Unsafe.SizeOf<ReplyExtra>());
        Assert.Equal(24, ReplyExtra.Size);
    }

    [Fact]
    public void Ac1_Enums_HaveTheDeclaredWidths_AndValues()
    {
        Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(OpKind)));
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(ReplyResult)));
        Assert.Equal(0, (int)ReplyResult.Ok);
        Assert.Equal(1, (int)ReplyResult.Rejected);
        Assert.Equal(0, (int)OpKind.None);
        Assert.Equal(1, (int)OpKind.PlayerJoin);
        Assert.Equal(2, (int)OpKind.PlayerLeave);
        Assert.Equal(3, (int)OpKind.FreezePlayer);
        Assert.Equal(4, (int)OpKind.TeleportAck);
    }

    [Fact]
    public void Ac1_DefaultOp_IsAllZero()
    {
        var op = default(Op);

        foreach (byte b in BytesOf(ref op))
        {
            Assert.Equal(0, b);
        }
    }

    // ---------------------------------------------------------------- AC-2: payload round trip

    [Fact]
    public void Ac2_SetPayload_Then_PayloadAs_RoundTripsA12ByteStruct()
    {
        var op = default(Op);

        op.SetPayload(Sample12());
        Payload12 back = op.PayloadAs<Payload12>();

        Assert.Equal(12, op.PayloadLength);
        Assert.Equal(Sample12(), back);
    }

    [Fact]
    public void Ac2_SetPayload_Then_PayloadAs_RoundTripsA48ByteStruct()
    {
        var op = default(Op);

        op.SetPayload(Sample48());
        Payload48 back = op.PayloadAs<Payload48>();

        Assert.Equal(48, op.PayloadLength);
        Assert.Equal(Sample48(), back);
    }

    [Fact]
    public void Ac2_SetPayload_WithValueAliasingItsOwnPayloadBytes_KeepsTheValue()
    {
        var op = default(Op);
        op.SetPayload(Sample48());
        ref Payload12 alias = ref Unsafe.As<OpPayload, Payload12>(ref op.Payload);
        Payload12 expected = alias;

        op.SetPayload(in alias);

        Assert.Equal(12, op.PayloadLength);
        Assert.Equal(expected, op.PayloadAs<Payload12>());
        Assert.NotEqual(default, expected);
        ReadOnlySpan<byte> payload = op.Payload;
        for (int i = 12; i < 48; i++)
        {
            Assert.Equal(0, payload[i]);
        }
    }

    [Fact]
    public void Ac2_SetPayload_OfA12ByteStruct_OverFilledBuffer_LeavesBytes12To47Zero()
    {
        var op = default(Op);
        Span<byte> raw = op.Payload;
        raw.Fill(0xFF);
        Assert.Equal(0xFF, ((ReadOnlySpan<byte>)op.Payload)[47]);

        op.SetPayload(Sample12());

        ReadOnlySpan<byte> payload = op.Payload;
        Assert.Equal(new byte[] { 0x44, 0x33, 0x22, 0x11, 0xEF, 0xBE, 0xAD, 0xDE, 0xFF, 0xFF, 0xFF, 0xFF }, payload.Slice(0, 12).ToArray());
        for (int i = 12; i < 48; i++)
        {
            Assert.Equal(0, payload[i]);
        }
    }

    [Fact]
    public void Ac2_SetPayload_DoesNotTouchTheHeader()
    {
        var op = new Op { OpSeq = 7, Actor = new ActorId(0x0100_0005u), Kind = OpKind.TeleportAck, ReqId = 0xCAFEBABEu };

        op.SetPayload(Sample12());

        Assert.Equal(7u, op.OpSeq);
        Assert.Equal(new ActorId(0x0100_0005u), op.Actor);
        Assert.Equal(OpKind.TeleportAck, op.Kind);
        Assert.Equal(0xCAFEBABEu, op.ReqId);
        Assert.Equal(0, op.Reserved);
    }

    [Fact]
    public void Ac2_PayloadAs_ReadsAnEightByteFieldAtAnUnalignedOffset()
    {
        var op = default(Op);
        var value = new MisalignedPayload { Lead = 0xAABBCCDDu, Wide = 0x0102030405060708UL };

        op.SetPayload(value);

        Assert.Equal(value, op.PayloadAs<MisalignedPayload>());
        Assert.Equal(12, op.PayloadLength);
    }

    [Fact]
    public void Ac2_PayloadAs_WithWrongSize_AssertsUnderSimAssert_AndReadsLeadingBytesInRelease()
    {
        var op = default(Op);
        op.SetPayload(Sample12());

        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() =>
            {
                var local = op;
                return local.PayloadAs<Payload48>();
            });
            Assert.Equal(SimAssertCode.OpPayloadLength, ex.Code);
        }
        else
        {
            Payload48 wide = op.PayloadAs<Payload48>();
            Assert.Equal(0x11223344u | (0xDEADBEEFUL << 32), wide.A);
            Assert.Equal(0xFFFFFFFFUL, wide.B);
            Assert.Equal(0UL, wide.C);
            Assert.Equal(0UL, wide.F);
        }
    }

    [Fact]
    public void Ac2_PayloadAs_WithZeroLengthPayload_AssertsForAnyType()
    {
        var op = default(Op);

        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() =>
            {
                var local = op;
                return local.PayloadAs<uint>();
            });
            Assert.Equal(SimAssertCode.OpPayloadLength, ex.Code);
        }
        else
        {
            Assert.Equal(0u, op.PayloadAs<uint>());
        }
    }

    [Fact]
    public void Ac2_PayloadAs_OfA49ByteType_AssertsUnderSimAssert_AndReturnsDefaultInRelease()
    {
        var op = default(Op);
        op.SetPayload(Sample48());

        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() =>
            {
                var local = op;
                return local.PayloadAs<Payload49>();
            });
            Assert.Equal(SimAssertCode.OpPayloadLength, ex.Code);
        }
        else
        {
            Assert.Equal(default, op.PayloadAs<Payload49>());
        }
    }

    [Fact]
    public void Ac2_SetPayload_OfA49ByteType_AssertsUnderSimAssert_AndStoresNothingInRelease()
    {
        var op = default(Op);
        op.SetPayload(Sample48());

        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() =>
            {
                var local = op;
                local.SetPayload(default(Payload49));
            });
            Assert.Equal(SimAssertCode.OpPayloadTooLarge, ex.Code);
        }
        else
        {
            op.SetPayload(default(Payload49));
            Assert.Equal(0, op.PayloadLength);
            foreach (byte b in (ReadOnlySpan<byte>)op.Payload)
            {
                Assert.Equal(0, b);
            }
        }
    }

    // ---------------------------------------------------------------- Reply helpers

    [Fact]
    public void Reply_Ok_CopiesReqIdAndKind_WithReasonZero_AndZeroPadAndExtra()
    {
        var op = new Op { ReqId = 0x01020304u, Kind = OpKind.FreezePlayer };

        Reply reply = Reply.Ok(in op);

        Assert.Equal(0x01020304u, reply.ReqId);
        Assert.Equal(OpKind.FreezePlayer, reply.Kind);
        Assert.Equal(0, reply.Reason);
        Assert.Equal(ReplyResult.Ok, reply.Result);
        Assert.True(reply.IsOk);
        Assert.Equal(0, reply.Pad0 | reply.Pad1 | reply.Pad2);
        foreach (byte b in (ReadOnlySpan<byte>)reply.Extra)
        {
            Assert.Equal(0, b);
        }
    }

    [Fact]
    public void Reply_Reject_CopiesReqIdAndKind_WithTheGivenReason()
    {
        var op = new Op { ReqId = 99u, Kind = OpKind.PlayerJoin };

        Reply reply = Reply.Reject(in op, CoreReason.SlotBusy);

        Assert.Equal(99u, reply.ReqId);
        Assert.Equal(OpKind.PlayerJoin, reply.Kind);
        Assert.Equal(CoreReason.SlotBusy, reply.Reason);
        Assert.Equal(ReplyResult.Rejected, reply.Result);
        Assert.False(reply.IsOk);
        Assert.Equal(0, reply.Pad0 | reply.Pad1 | reply.Pad2);
    }

    [Fact]
    public void Reply_Reject_WithSystemReason64_KeepsIt()
    {
        var op = new Op { ReqId = 1u, Kind = OpKind.TeleportAck };

        Reply reply = Reply.Reject(in op, 64);

        Assert.Equal(64, reply.Reason);
        Assert.Equal(ReplyResult.Rejected, reply.Result);
    }

    [Fact]
    public void Reply_Reject_WithReasonZero_AssertsUnderSimAssert_AndBecomesBadRequestInRelease()
    {
        var op = new Op { ReqId = 5u, Kind = OpKind.PlayerLeave };

        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() =>
            {
                var local = op;
                return Reply.Reject(in local, 0);
            });
            Assert.Equal(SimAssertCode.ReplyRejectZeroReason, ex.Code);
        }
        else
        {
            Reply reply = Reply.Reject(in op, 0);
            Assert.Equal(CoreReason.BadRequest, reply.Reason);
            Assert.Equal(ReplyResult.Rejected, reply.Result);
            Assert.False(reply.IsOk);
        }
    }

    [Fact]
    public void Reply_OkIff_ReasonIsZero_HoldsForOkAndEveryCoreReason()
    {
        var op = new Op { ReqId = 3u, Kind = OpKind.PlayerJoin };
        Assert.True(Reply.Ok(in op).IsOk);

        for (ushort reason = 1; reason <= CoreReason.BadRequest; reason++)
        {
            Reply reply = Reply.Reject(in op, reason);
            Assert.Equal(reason, reply.Reason);
            Assert.False(reply.IsOk);
            Assert.Equal(ReplyResult.Rejected, reply.Result);
        }
    }

    [Fact]
    public void Reply_Reject_WithReservedReasons9To63_AssertsUnderSimAssert_AndBecomesBadRequestInRelease()
    {
        var op = new Op { ReqId = 6u, Kind = OpKind.PlayerLeave };

        foreach (ushort reserved in new ushort[] { 9, 10, 32, 62, 63 })
        {
            if (TestBuildExpectation.SimAssertsExpected)
            {
                SimAssertException ex = Assert.Throws<SimAssertException>(() =>
                {
                    var local = op;
                    return Reply.Reject(in local, reserved);
                });
                Assert.Equal(SimAssertCode.ReplyRejectReservedReason, ex.Code);
            }
            else
            {
                Reply reply = Reply.Reject(in op, reserved);
                Assert.Equal(CoreReason.BadRequest, reply.Reason);
                Assert.Equal(ReplyResult.Rejected, reply.Result);
                Assert.False(reply.IsOk);
            }
        }
    }

    [Fact]
    public void Reply_Ok_WithExtra_CarriesTheDetails_AndMatchesOkThenSetExtra()
    {
        var op = new Op { ReqId = 21u, Kind = OpKind.PlayerJoin };
        var extra = new Extra16 { A = 0xAABBCCDDEEFF0011UL, B = 77UL };

        Reply reply = Reply.Ok(in op, in extra);

        Assert.Equal(21u, reply.ReqId);
        Assert.Equal(OpKind.PlayerJoin, reply.Kind);
        Assert.True(reply.IsOk);
        Assert.Equal(ReplyResult.Ok, reply.Result);
        Assert.Equal(extra, reply.ExtraAs<Extra16>());
        Reply manual = Reply.Ok(in op);
        manual.SetExtra(in extra);
        Assert.True(BytesOf(ref reply).SequenceEqual(BytesOf(ref manual)));
    }

    [Fact]
    public void Reply_SetExtra_WithValueAliasingItsOwnBytes_KeepsTheValue()
    {
        var op = new Op { ReqId = 22u, Kind = OpKind.PlayerJoin };
        Reply reply = Reply.Ok(in op, new Extra16 { A = 0x0102030405060708UL, B = 0x1112131415161718UL });
        ref Extra16 alias = ref Unsafe.As<ReplyExtra, Extra16>(ref reply.Extra);

        reply.SetExtra(in alias);

        Assert.Equal(new Extra16 { A = 0x0102030405060708UL, B = 0x1112131415161718UL }, reply.ExtraAs<Extra16>());
    }

    [Fact]
    public void Reply_SetExtra_Then_ExtraAs_RoundTrips_AndZeroFillsTheRest()
    {
        var op = new Op { ReqId = 8u, Kind = OpKind.PlayerJoin };
        Reply reply = Reply.Ok(in op);
        Span<byte> raw = reply.Extra;
        raw.Fill(0xFF);

        reply.SetExtra(new Extra16 { A = 0x1122334455667788UL, B = 9UL });

        Assert.Equal(new Extra16 { A = 0x1122334455667788UL, B = 9UL }, reply.ExtraAs<Extra16>());
        ReadOnlySpan<byte> extra = reply.Extra;
        for (int i = 16; i < 24; i++)
        {
            Assert.Equal(0, extra[i]);
        }
    }

    [Fact]
    public void Reply_Extra_OfA25ByteType_AssertsUnderSimAssert_AndIsInertInRelease()
    {
        var op = new Op { ReqId = 8u, Kind = OpKind.PlayerJoin };
        Reply reply = Reply.Ok(in op);
        reply.SetExtra(new Extra16 { A = 1, B = 2 });

        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(
                SimAssertCode.ReplyExtraTooLarge,
                Assert.Throws<SimAssertException>(() =>
                {
                    var local = reply;
                    local.SetExtra(default(Payload48));
                }).Code);
            Assert.Equal(
                SimAssertCode.ReplyExtraRead,
                Assert.Throws<SimAssertException>(() =>
                {
                    var local = reply;
                    return local.ExtraAs<Payload48>();
                }).Code);
        }
        else
        {
            Assert.Equal(default, reply.ExtraAs<Payload48>());
            reply.SetExtra(default(Payload48));
            Assert.Equal(default, reply.ExtraAs<Extra16>());
        }
    }

    // ---------------------------------------------------------------- AC-3: CoreReason, ReasonDef, descriptors

    [Fact]
    public void Ac3_CoreReason_HasExactlyTheDeclaredValues()
    {
        Assert.Equal(0, CoreReason.Ok);
        Assert.Equal(1, CoreReason.Busy);
        Assert.Equal(2, CoreReason.UnknownActor);
        Assert.Equal(3, CoreReason.UnknownKind);
        Assert.Equal(4, CoreReason.SlotInvalid);
        Assert.Equal(5, CoreReason.SlotBusy);
        Assert.Equal(6, CoreReason.DuplicatePlayer);
        Assert.Equal(7, CoreReason.JoinClosed);
        Assert.Equal(8, CoreReason.BadRequest);
        Assert.Equal(64, CoreReason.FirstSystemReason);

        var fields = typeof(CoreReason).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.Equal(10, fields.Length);
    }

    [Fact]
    public void Ac3_ReasonDef_CarriesCodeAndName_WithValueEquality()
    {
        var a = new ReasonDef(64, "no_room");

        Assert.Equal((ushort)64, a.Code);
        Assert.Equal("no_room", a.Name);
        Assert.Equal(new ReasonDef(64, "no_room"), a);
        Assert.NotEqual(new ReasonDef(65, "no_room"), a);
    }

    [Fact]
    public void Ac3_OpDescriptor_For_FillsLengthAndType_AndKeepsTheFields()
    {
        var reasons = new[] { new ReasonDef(64, "a"), new ReasonDef(65, "b") };

        OpDescriptor d = OpDescriptor.For<Payload12>(OpKind.TeleportAck, systemOnly: true, wireCodecId: 7, wireSize: 12, reasons);

        Assert.Equal(OpKind.TeleportAck, d.Kind);
        Assert.Equal(12, d.PayloadLength);
        Assert.True(d.SystemOnly);
        Assert.Equal(7, d.WireCodecId);
        Assert.Equal(12, d.WireSize);
        Assert.Equal(typeof(Payload12), d.PayloadType);
        Assert.Equal(2, d.Reasons.Length);
        Assert.Equal(new ReasonDef(64, "a"), d.Reasons[0]);
    }

    [Fact]
    public void Ac3_OpDescriptor_Reasons_AreCopied_SoLaterMutationOfTheSourceArrayIsNotSeen()
    {
        var reasons = new[] { new ReasonDef(64, "a"), new ReasonDef(65, "b") };

        OpDescriptor d = OpDescriptor.For<Payload12>(OpKind.TeleportAck, false, 7, 12, reasons);
        reasons[0] = new ReasonDef(3, "tampered");

        Assert.Equal(new ReasonDef(64, "a"), d.Reasons[0]);
        Assert.Empty(DescriptorAssert.Validate(d, OpKindBlock.Core));
    }

    [Fact]
    public void Ac3_OpDescriptor_NullReasons_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new OpDescriptor(OpKind.PlayerJoin, 0, false, 1, 0, null!));
        Assert.Throws<ArgumentNullException>(() => OpDescriptor.For<Payload12>(OpKind.PlayerJoin, false, 1, 12, null!));
        Assert.Throws<ArgumentNullException>(() => OpDescriptor.Empty(OpKind.PlayerJoin, false, 1, 0, null!));
    }

    [Fact]
    public void Ac3_OpDescriptor_For_OfAnOver48ByteType_ThrowsInsteadOfTruncatingTheLength()
    {
        Assert.Throws<ArgumentException>(() => OpDescriptor.For<Payload49>(OpKind.PlayerJoin, false, 1, 49));
        Assert.Throws<ArgumentException>(() => OpDescriptor.For<Payload256>(OpKind.PlayerJoin, false, 1, 0));
    }

    [Fact]
    public void Ac3_OpDescriptor_Empty_HasZeroLengthAndNoType_AndExplicitCtorKeepsFields()
    {
        OpDescriptor empty = OpDescriptor.Empty(OpKind.PlayerLeave, systemOnly: false, wireCodecId: 2, wireSize: 0);
        var raw = new OpDescriptor(OpKind.PlayerJoin, 12, false, 1, 12, Array.Empty<ReasonDef>(), typeof(Payload12));

        Assert.Equal(0, empty.PayloadLength);
        Assert.Null(empty.PayloadType);
        Assert.Empty(empty.Reasons);
        Assert.False(empty.SystemOnly);
        Assert.Equal(typeof(Payload12), raw.PayloadType);
        Assert.Equal(12, raw.PayloadLength);
    }

    [Fact]
    public void Ac3_DescriptorCheck_PassesForValidDescriptors()
    {
        DescriptorAssert.Check(OpDescriptor.For<Payload12>(OpKind.PlayerJoin, false, 1, 12, new ReasonDef(64, "x")), OpKindBlock.Core);
        DescriptorAssert.Check(OpDescriptor.For<Payload48>(OpKind.TeleportAck, false, 2, 48), OpKindBlock.Core);
        DescriptorAssert.Check(OpDescriptor.Empty(OpKind.PlayerLeave, false, 3, 0), OpKindBlock.Core);
        DescriptorAssert.Check(OpDescriptor.For<MisalignedPayload>(OpKind.FreezePlayer, true, 4, 12), OpKindBlock.Core);
        Assert.Empty(DescriptorAssert.Validate(OpDescriptor.For<Payload12>(OpKind.PlayerJoin, false, 1, 12), OpKindBlock.Core));
    }

    [Fact]
    public void Ac3_DescriptorCheck_FailsForAPaddedStruct()
    {
        OpDescriptor d = OpDescriptor.For<PaddedPayload>(OpKind.PlayerJoin, false, 1, 8);

        IReadOnlyList<string> violations = DescriptorAssert.Validate(d, OpKindBlock.Core);

        Assert.Contains(violations, v => v.Contains("padding", StringComparison.Ordinal));
        Assert.ThrowsAny<Exception>(() => DescriptorAssert.Check(d, OpKindBlock.Core));
    }

    [Fact]
    public void Ac3_DescriptorCheck_FailsWhenPaddingIsHiddenInANestedStruct()
    {
        OpDescriptor d = OpDescriptor.For<NestedPaddedPayload>(OpKind.PlayerJoin, false, 1, 12);

        Assert.Contains(DescriptorAssert.Validate(d, OpKindBlock.Core), v => v.Contains("padding", StringComparison.Ordinal));
    }

    [Fact]
    public void Ac3_DescriptorCheck_FailsForA49ByteStruct()
    {
        var d = new OpDescriptor(OpKind.PlayerJoin, 49, false, 1, 49, Array.Empty<ReasonDef>(), typeof(Payload49));

        IReadOnlyList<string> violations = DescriptorAssert.Validate(d, OpKindBlock.Core);

        Assert.Equal(49, d.PayloadLength);
        Assert.Contains(violations, v => v.Contains("over the 48 B", StringComparison.Ordinal));
        Assert.ThrowsAny<Exception>(() => DescriptorAssert.Check(d, OpKindBlock.Core));
    }

    [Fact]
    public void Ac3_DescriptorCheck_FailsForReason63_AndPassesForReason64()
    {
        OpDescriptor bad = OpDescriptor.For<Payload12>(OpKind.PlayerJoin, false, 1, 12, new ReasonDef(63, "steals_core_code"));
        OpDescriptor good = OpDescriptor.For<Payload12>(OpKind.PlayerJoin, false, 1, 12, new ReasonDef(64, "ok_system_code"));

        Assert.Contains(DescriptorAssert.Validate(bad, OpKindBlock.Core), v => v.Contains("below 64", StringComparison.Ordinal));
        Assert.ThrowsAny<Exception>(() => DescriptorAssert.Check(bad, OpKindBlock.Core));
        Assert.Empty(DescriptorAssert.Validate(good, OpKindBlock.Core));
    }

    [Fact]
    public void Ac3_DescriptorCheck_FailsForReasonZeroAndDuplicateReasons()
    {
        OpDescriptor d = OpDescriptor.For<Payload12>(OpKind.PlayerJoin, false, 1, 12, new ReasonDef(0, "ok"), new ReasonDef(70, "a"), new ReasonDef(70, "b"));

        IReadOnlyList<string> violations = DescriptorAssert.Validate(d, OpKindBlock.Core);

        Assert.Contains(violations, v => v.Contains("below 64", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("twice", StringComparison.Ordinal));
    }

    [Fact]
    public void Ac3_DescriptorCheck_FailsForKindOutsideItsOwnersBlock_AndForNone()
    {
        OpDescriptor inPlayers = OpDescriptor.For<Payload12>((OpKind)0x0041, false, 1, 12);
        OpDescriptor none = OpDescriptor.For<Payload12>(OpKind.None, false, 1, 12);

        Assert.Empty(DescriptorAssert.Validate(inPlayers, OpKindBlock.Players));
        Assert.Contains(DescriptorAssert.Validate(inPlayers, OpKindBlock.Core), v => v.Contains("outside", StringComparison.Ordinal));
        Assert.Contains(DescriptorAssert.Validate(none, OpKindBlock.Core), v => v.Contains("None", StringComparison.Ordinal));
    }

    [Fact]
    public void Ac3_DescriptorCheck_FailsWhenPayloadLengthDisagreesWithTheType()
    {
        var wrongLength = new OpDescriptor(OpKind.PlayerJoin, 8, false, 1, 8, Array.Empty<ReasonDef>(), typeof(Payload12));
        var emptyWithLength = new OpDescriptor(OpKind.PlayerJoin, 4, false, 1, 4, Array.Empty<ReasonDef>());

        Assert.Contains(DescriptorAssert.Validate(wrongLength, OpKindBlock.Core), v => v.Contains("PayloadLength is 8", StringComparison.Ordinal));
        Assert.Contains(DescriptorAssert.Validate(emptyWithLength, OpKindBlock.Core), v => v.Contains("payload-free", StringComparison.Ordinal));
    }

    [Fact]
    public void Ac3_DescriptorCheck_FailsForAReferenceTypedPayload()
    {
        var d = new OpDescriptor(OpKind.PlayerJoin, 8, false, 1, 8, Array.Empty<ReasonDef>(), typeof(RefPayload));

        Assert.Contains(DescriptorAssert.Validate(d, OpKindBlock.Core), v => v.Contains("reference types", StringComparison.Ordinal));
    }

    [Fact]
    public void Ac3_DescriptorHelper_SumOfFieldSizes_RecursesIntoInlineArraysAndStructs()
    {
        Assert.Equal(48, DescriptorAssert.SumOfFieldSizes(typeof(OpPayload)));
        Assert.Equal(24, DescriptorAssert.SumOfFieldSizes(typeof(ReplyExtra)));
        Assert.Equal(5, DescriptorAssert.SumOfFieldSizes(typeof(PaddedPayload)));
        Assert.Equal(9, DescriptorAssert.SumOfFieldSizes(typeof(NestedPaddedPayload)));
        Assert.Equal(12, DescriptorAssert.SizeOf(typeof(Payload12)));
    }

    [Fact]
    public void Ac3_DescriptorCheck_FailsForInt128_AtAnyNestingDepth()
    {
        var direct = new OpDescriptor(OpKind.PlayerJoin, 16, false, 1, 16, Array.Empty<ReasonDef>(), typeof(Int128Payload));
        var nested = new OpDescriptor(OpKind.PlayerJoin, 20, false, 1, 20, Array.Empty<ReasonDef>(), typeof(NestedInt128Payload));

        Assert.Contains(DescriptorAssert.Validate(direct, OpKindBlock.Core), v => v.Contains("Int128", StringComparison.Ordinal));
        Assert.Contains(DescriptorAssert.Validate(nested, OpKindBlock.Core), v => v.Contains("Int128", StringComparison.Ordinal));
        Assert.ThrowsAny<Exception>(() => DescriptorAssert.Check(direct, OpKindBlock.Core));
    }

    [Fact]
    public void Ac3_DescriptorCheck_FailsForAnAutoLayoutStruct_AtAnyNestingDepth()
    {
        OpDescriptor direct = OpDescriptor.For<AutoPayload>(OpKind.PlayerJoin, false, 1, 8);
        OpDescriptor nested = OpDescriptor.For<NestedAutoPayload>(OpKind.PlayerJoin, false, 1, 12);

        Assert.Contains(DescriptorAssert.Validate(direct, OpKindBlock.Core), v => v.Contains("Auto", StringComparison.Ordinal));
        Assert.Contains(DescriptorAssert.Validate(nested, OpKindBlock.Core), v => v.Contains("Auto", StringComparison.Ordinal));
    }

    [Fact]
    public void Ac3_DescriptorCheck_FailsForFloatingPointAndPlatformSizedFields()
    {
        OpDescriptor floating = OpDescriptor.For<FloatPayload>(OpKind.PlayerJoin, false, 1, 8);
        OpDescriptor native = OpDescriptor.For<NativeIntPayload>(OpKind.PlayerJoin, false, 1, (byte)Unsafe.SizeOf<NativeIntPayload>());

        Assert.Contains(DescriptorAssert.Validate(floating, OpKindBlock.Core), v => v.Contains("floating", StringComparison.Ordinal));
        Assert.Contains(DescriptorAssert.Validate(native, OpKindBlock.Core), v => v.Contains("nint", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- OpKind blocks

    [Fact]
    public void OpKindBlocks_All_ListsEveryBlockAscending()
    {
        Assert.Equal(
            [OpKindBlock.Core, OpKindBlock.Players, OpKindBlock.Inventory, OpKindBlock.MailDelivery, OpKindBlock.Throwing],
            OpKindBlock.All.ToArray());
    }

    [Fact]
    public void OpKindBlocks_TryOwnerOf_FindsTheOwner_AndRefusesNoneAndUnassignedKinds()
    {
        Assert.True(OpKindBlock.TryOwnerOf(OpKind.PlayerJoin, out OpKindBlock core));
        Assert.Equal(OpKindBlock.Core, core);
        Assert.True(OpKindBlock.TryOwnerOf((OpKind)0x0140, out OpKindBlock throwing));
        Assert.Equal(OpKindBlock.Throwing, throwing);
        Assert.True(OpKindBlock.TryOwnerOf((OpKind)0x007F, out OpKindBlock players));
        Assert.Equal(OpKindBlock.Players, players);

        Assert.False(OpKindBlock.TryOwnerOf(OpKind.None, out OpKindBlock none));
        Assert.Equal(default, none);
        Assert.False(OpKindBlock.TryOwnerOf((OpKind)0x0180, out _));
        Assert.False(OpKindBlock.TryOwnerOf((OpKind)0xFFFF, out _));
    }

    [Fact]
    public void OpKindBlocks_HaveTheAdrRanges_AndContainsIsInclusive()
    {
        Assert.Equal(((ushort)0x0001, (ushort)0x003F), (OpKindBlock.Core.First, OpKindBlock.Core.Last));
        Assert.Equal(((ushort)0x0040, (ushort)0x007F), (OpKindBlock.Players.First, OpKindBlock.Players.Last));
        Assert.Equal(((ushort)0x0080, (ushort)0x00FF), (OpKindBlock.Inventory.First, OpKindBlock.Inventory.Last));
        Assert.Equal(((ushort)0x0100, (ushort)0x013F), (OpKindBlock.MailDelivery.First, OpKindBlock.MailDelivery.Last));
        Assert.Equal(((ushort)0x0140, (ushort)0x017F), (OpKindBlock.Throwing.First, OpKindBlock.Throwing.Last));
        Assert.Equal("SimCore", OpKindBlock.Core.Name);
        Assert.True(OpKindBlock.Core.Contains((OpKind)0x0001));
        Assert.True(OpKindBlock.Core.Contains((OpKind)0x003F));
        Assert.False(OpKindBlock.Core.Contains((OpKind)0x0040));
        Assert.False(OpKindBlock.Core.Contains(OpKind.None));
        Assert.True(OpKindBlock.Players.Contains((OpKind)0x0040));
    }

    [Fact]
    public void OpKindBlocks_AreOrderedAndNonOverlapping()
    {
        var blocks = OpKindBlock.All;

        for (int i = 0; i < blocks.Length; i++)
        {
            Assert.True(blocks[i].First <= blocks[i].Last);
            if (i > 0)
            {
                Assert.True(blocks[i - 1].Last < blocks[i].First, "blocks must be disjoint and ascending");
                Assert.Equal(blocks[i - 1].Last + 1, blocks[i].First);
            }
        }

        Assert.Equal(blocks.Length, blocks.Select(b => b.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void OpKind_EveryDeclaredValue_IsDistinct_NonZero_AndInsideExactlyOneBlock()
    {
        var blocks = OpKindBlock.All;
        var seen = new HashSet<OpKind>();

        foreach (OpKind kind in Enum.GetValues<OpKind>())
        {
            Assert.True(seen.Add(kind));
            int containing = blocks.Count(b => b.Contains(kind));
            Assert.Equal(kind == OpKind.None ? 0 : 1, containing);
            Assert.Equal(kind != OpKind.None, OpKindBlock.TryOwnerOf(kind, out _));
        }

        Assert.Contains(OpKind.PlayerJoin, seen);
        foreach (OpKind kind in new[] { OpKind.PlayerJoin, OpKind.PlayerLeave, OpKind.FreezePlayer, OpKind.TeleportAck })
        {
            Assert.True(OpKindBlock.Core.Contains(kind));
        }
    }

    // ---------------------------------------------------------------- Guardrail

    internal static long Exercise(ref Op op, in Payload12 p12, in Payload48 p48, in Extra16 extra)
    {
        op.SetPayload(in p12);
        long sum = op.PayloadAs<Payload12>().A;
        op.SetPayload(in p48);
        sum += (long)(op.PayloadAs<Payload48>().F & 0xFFFF);
        Reply ok = Reply.Ok(in op);
        ok.SetExtra(in extra);
        sum += (long)ok.ExtraAs<Extra16>().A;
        Reply rejected = Reply.Reject(in op, CoreReason.Busy);
        sum += rejected.Reason + ok.Reason + (ok.IsOk ? 1 : 0);
        return sum;
    }
}

/// <summary>
/// Zero-allocation guardrail for <see cref="Op"/> payload accessors and <see cref="Reply"/> helpers, in the
/// non-parallel collection so runtime warm-up on other test threads cannot land in a measured window.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class OpTypesAllocationTests
{
    [Fact]
    public void PayloadAccessors_AndReplyHelpers_AllocateNothing_InOptimisedBuilds()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(Op).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        // Arrange
        var op = new Op { Kind = OpKind.PlayerJoin, ReqId = 1 };
        OpTypesTests.Payload12 p12 = OpTypesTests.Sample12();
        OpTypesTests.Payload48 p48 = OpTypesTests.Sample48();
        var extra = new OpTypesTests.Extra16 { A = 1, B = 2 };
        long sink = 0;

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("1,000 payload round trips and Reply helpers", () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                sink += OpTypesTests.Exercise(ref op, in p12, in p48, in extra);
            }
        });

        // Assert
        Assert.NotEqual(0L, sink);
    }
}
