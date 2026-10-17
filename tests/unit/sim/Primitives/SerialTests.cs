using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Primitives;

/// <summary>Covers <see cref="SerialU8"/>, <see cref="SerialU16"/> and <see cref="SerialU32"/> (ADR-0004 section 5, RFC 1982).</summary>
public class SerialTests
{
    [Fact]
    public void SerialU16_IsNewer_HandlesWrapAround()
    {
        Assert.True(SerialU16.IsNewer(1, 65535));
        Assert.True(SerialU16.IsNewer(0, 65535));
        Assert.False(SerialU16.IsNewer(65535, 1));
        Assert.True(SerialU16.IsNewer(5, 4));
        Assert.False(SerialU16.IsNewer(4, 5));
        Assert.False(SerialU16.IsNewer(7, 7));
        Assert.True(SerialU16.IsNewer(32767, 0));
        Assert.False(SerialU16.IsNewer(32769, 0));
    }

    [Fact]
    public void SerialU16_Diff_IsSignedModuloDifference()
    {
        Assert.Equal(2, SerialU16.Diff(1, 65535));
        Assert.Equal(-2, SerialU16.Diff(65535, 1));
        Assert.Equal(0, SerialU16.Diff(9, 9));
        Assert.Equal(32767, SerialU16.Diff(32767, 0));
        Assert.Equal(-32767, SerialU16.Diff(32769, 0));
        Assert.Equal(-32768, SerialU16.Diff(32768, 0));
    }

    [Fact]
    public void SerialU16_HalfRangePair_AssertsInDebugAndIsNotNewerInRelease()
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => SerialU16.IsNewer(32768, 0));
            Assert.Equal(SimAssertCode.SerialHalfRange, ex.Code);
            Assert.Throws<SimAssertException>(() => SerialU16.IsNewer(0, 32768));
        }
        else
        {
            Assert.False(SerialU16.IsNewer(32768, 0));
            Assert.False(SerialU16.IsNewer(0, 32768));
        }
    }

    [Fact]
    public void SerialU8_And_SerialU32_FollowTheSameRules()
    {
        Assert.True(SerialU8.IsNewer(1, 255));
        Assert.False(SerialU8.IsNewer(255, 1));
        Assert.Equal(2, SerialU8.Diff(1, 255));
        Assert.Equal(127, SerialU8.Diff(127, 0));
        Assert.Equal(-128, SerialU8.Diff(128, 0));
        Assert.True(SerialU32.IsNewer(1, uint.MaxValue));
        Assert.False(SerialU32.IsNewer(uint.MaxValue, 1));
        Assert.Equal(2, SerialU32.Diff(1, uint.MaxValue));
        Assert.Equal(int.MinValue, SerialU32.Diff(0x8000_0000u, 0));

        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Throws<SimAssertException>(() => SerialU8.IsNewer(128, 0));
            Assert.Throws<SimAssertException>(() => SerialU32.IsNewer(0x8000_0000u, 0));
        }
        else
        {
            Assert.False(SerialU8.IsNewer(128, 0));
            Assert.False(SerialU32.IsNewer(0x8000_0000u, 0));
        }
    }
}
