using System.Buffers;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Primitives;

/// <summary>
/// Covers TR-sim-035 (writer half) for story 019 AC-1: the little-endian primitives, the dump header, section framing
/// (<c>id u16, length u32, payload</c> flushed at <c>EndSection</c>), the reusable section buffer and the misuse asserts.
/// Misuse behaviour branches on <see cref="TestBuildExpectation.SimAssertsExpected"/>.
/// </summary>
public class CanonicalWriterTests
{
    // ---- Header and framing ----

    [Fact]
    public void Ac1_WriteHeader_EmitsPmsdAndVersion1()
    {
        var output = new ArrayBufferWriter<byte>();
        var w = new CanonicalWriter(output, new CanonicalBuffer());

        w.WriteHeader();

        Assert.Equal(new byte[] { 0x50, 0x4D, 0x53, 0x44, 0x01, 0x00 }, output.WrittenSpan.ToArray());
        Assert.Equal(0x44534D50u, CanonicalWriter.Magic);
    }

    [Fact]
    public void Ac1_WriteU16_0x1234_EmitsLittleEndian3412()
    {
        var output = new ArrayBufferWriter<byte>();
        var w = new CanonicalWriter(output, new CanonicalBuffer());

        w.BeginSection(7);
        w.WriteU16(0x1234);
        w.EndSection();

        Assert.Equal(new byte[] { 0x07, 0x00, 0x02, 0x00, 0x00, 0x00, 0x34, 0x12 }, output.WrittenSpan.ToArray());
    }

    [Fact]
    public void Ac1_Primitives_AreLittleEndianAndFieldByField()
    {
        var output = new ArrayBufferWriter<byte>();
        var w = new CanonicalWriter(output, new CanonicalBuffer());

        w.BeginSection(1);
        w.WriteU8(0xAB);
        w.WriteU32(0x01020304u);
        w.WriteU64(0x0102030405060708ul);
        w.WriteI32(-2);
        w.WriteI64(-3);
        w.WriteBool(true);
        w.WriteBool(false);
        w.WriteBytes(new byte[] { 9, 8, 7 });
        w.EndSection();

        byte[] expectedPayload =
        {
            0xAB,
            0x04, 0x03, 0x02, 0x01,
            0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
            0xFE, 0xFF, 0xFF, 0xFF,
            0xFD, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0x01, 0x00,
            9, 8, 7,
        };
        byte[] written = output.WrittenSpan.ToArray();
        Assert.Equal(6 + expectedPayload.Length, written.Length);
        Assert.Equal(expectedPayload, written[6..]);
        Assert.Equal((uint)expectedPayload.Length, BitConverter.ToUInt32(written, 2));
    }

    [Fact]
    public void Ac1_Section_IsBufferedUntilEndSection()
    {
        var output = new ArrayBufferWriter<byte>();
        var w = new CanonicalWriter(output, new CanonicalBuffer());

        w.BeginSection(3);
        w.WriteU32(5);
        Assert.Equal(0, output.WrittenCount);
        Assert.True(w.InSection);
        Assert.Equal(4, w.SectionLength);
        w.EndSection();

        Assert.False(w.InSection);
        Assert.Equal(10, output.WrittenCount);
    }

    [Fact]
    public void Ac1_Sections_AppendInCallOrder_AndEmptySectionHasZeroLength()
    {
        var output = new ArrayBufferWriter<byte>();
        var w = new CanonicalWriter(output, new CanonicalBuffer());

        w.BeginSection(9);
        w.WriteU8(1);
        w.EndSection();
        w.BeginSection(2);
        w.EndSection();

        Assert.Equal(
            new byte[] { 9, 0, 1, 0, 0, 0, 1, 2, 0, 0, 0, 0, 0 },
            output.WrittenSpan.ToArray());
    }

    // ---- Reusable buffer ----

    [Fact]
    public void Ac1_LargeSection_GrowsTheBuffer_AndKeepsEveryByte()
    {
        var scratch = new CanonicalBuffer();
        var output = new ArrayBufferWriter<byte>();
        var w = new CanonicalWriter(output, scratch);
        const int Count = 5000;

        w.BeginSection(4);
        for (int i = 0; i < Count; i++)
        {
            w.WriteU8((byte)(i * 31));
        }

        w.EndSection();

        Assert.True(scratch.Capacity >= Count);
        byte[] written = output.WrittenSpan.ToArray();
        Assert.Equal((uint)Count, BitConverter.ToUInt32(written, 2));
        for (int i = 0; i < Count; i++)
        {
            Assert.Equal((byte)(i * 31), written[6 + i]);
        }
    }

    [Fact]
    public void Ac1_ReusedBuffer_ProducesIdenticalBytes_WithoutGrowing()
    {
        var scratch = new CanonicalBuffer();
        byte[] first = WriteSample(scratch);
        int capacity = scratch.Capacity;

        byte[] second = WriteSample(scratch);

        Assert.Equal(first, second);
        Assert.Equal(capacity, scratch.Capacity);
    }

    private static byte[] WriteSample(CanonicalBuffer scratch)
    {
        var output = new ArrayBufferWriter<byte>();
        var w = new CanonicalWriter(output, scratch);
        w.WriteHeader();
        w.BeginSection(1);
        w.WriteU64(0xDEADBEEFCAFEF00Dul);
        w.EndSection();
        w.BeginSection(2);
        w.WriteI32(-1);
        w.EndSection();
        return output.WrittenSpan.ToArray();
    }

    // ---- Misuse ----

    [Fact]
    public void Ac1_WriteOutsideSection_AssertsOrDrops()
    {
        var output = new ArrayBufferWriter<byte>();
        Exception? thrown = Capture(() =>
        {
            var w = new CanonicalWriter(output, new CanonicalBuffer());
            w.WriteU32(1);
            w.BeginSection(1);
            w.EndSection();
        });

        AssertMisuse(thrown);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            // The stray write was dropped; only the empty section was emitted.
            Assert.Equal(new byte[] { 1, 0, 0, 0, 0, 0 }, output.WrittenSpan.ToArray());
        }
    }

    [Fact]
    public void Ac1_NestedBeginSection_AssertsOrIsIgnored()
    {
        var output = new ArrayBufferWriter<byte>();
        Exception? thrown = Capture(() =>
        {
            var w = new CanonicalWriter(output, new CanonicalBuffer());
            w.BeginSection(1);
            w.WriteU8(5);
            w.BeginSection(2);
            w.EndSection();
        });

        AssertMisuse(thrown);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(new byte[] { 1, 0, 1, 0, 0, 0, 5 }, output.WrittenSpan.ToArray());
        }
    }

    [Fact]
    public void Ac1_EndSectionWithoutBegin_AssertsOrIsIgnored()
    {
        var output = new ArrayBufferWriter<byte>();
        Exception? thrown = Capture(() =>
        {
            var w = new CanonicalWriter(output, new CanonicalBuffer());
            w.EndSection();
        });

        AssertMisuse(thrown);
        Assert.Equal(0, output.WrittenCount);
    }

    [Fact]
    public void Ac1_HeaderInsideSection_AssertsOrIsIgnored()
    {
        var output = new ArrayBufferWriter<byte>();
        Exception? thrown = Capture(() =>
        {
            var w = new CanonicalWriter(output, new CanonicalBuffer());
            w.BeginSection(1);
            w.WriteHeader();
        });

        AssertMisuse(thrown);
        Assert.Equal(0, output.WrittenCount);
    }

    [Fact]
    public void Ac1_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new CanonicalWriter(null!, new CanonicalBuffer()));
        Assert.Throws<ArgumentNullException>(() => new CanonicalWriter(new ArrayBufferWriter<byte>(), null!));
    }

    private static Exception? Capture(Action body)
    {
        try
        {
            body();
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }

    private static void AssertMisuse(Exception? thrown)
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException e = Assert.IsType<SimAssertException>(thrown);
            Assert.Equal(SimAssertCode.DumpWriterMisuse, e.Code);
        }
        else
        {
            Assert.Null(thrown);
        }
    }
}
