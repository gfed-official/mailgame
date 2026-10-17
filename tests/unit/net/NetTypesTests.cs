using PerformativeMail.Client.Core.Net;

namespace PerformativeMail.Sim.Tests.Unit.Net;

/// <summary>
/// Covers the small value types of the transport seam: <see cref="PeerHandle"/>,
/// <see cref="NetChannel"/>, <see cref="DisconnectReason"/> and <see cref="NetAssertCode"/>
/// (TR-sim-042: the adapter vocabulary lives outside the Sim; story network-adapter-loopback/001).
/// </summary>
public class NetTypesTests
{
    [Fact]
    public void PeerHandle_Default_EqualsNoneAndIsNone()
    {
        // Arrange
        PeerHandle fromDefault = default;

        // Act
        bool equalsNone = fromDefault == PeerHandle.None;

        // Assert
        Assert.True(equalsNone);
        Assert.True(fromDefault.IsNone);
        Assert.Equal(0u, PeerHandle.None.Value);
    }

    [Fact]
    public void PeerHandle_SameValue_EqualInEveryForm()
    {
        // Arrange
        var a = new PeerHandle(7);
        var b = new PeerHandle(7);

        // Act / Assert
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a.Equals(b));
        Assert.True(a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.IsNone);
    }

    [Fact]
    public void PeerHandle_DifferentValue_NotEqual()
    {
        // Arrange
        var a = new PeerHandle(7);
        var b = new PeerHandle(8);

        // Act / Assert
        Assert.False(a == b);
        Assert.True(a != b);
        Assert.False(a.Equals(b));
        Assert.False(a.Equals((object)b));
        Assert.False(a.Equals(null));
        Assert.False(a.Equals("7"));
    }

    [Fact]
    public void PeerHandle_MakesNoAssumptionAboutRange_RoundTripsFullUintRange()
    {
        // Arrange
        var max = new PeerHandle(uint.MaxValue);

        // Act / Assert
        Assert.Equal(uint.MaxValue, max.Value);
        Assert.False(max.IsNone);
    }

    [Theory]
    [InlineData(NetChannel.Unreliable, 0)]
    [InlineData(NetChannel.Reliable, 1)]
    [InlineData(NetChannel.Bulk, 2)]
    [InlineData(NetChannel.Comms, 3)]
    public void NetChannel_LogicalIds_MatchAdr0011(NetChannel channel, byte expected)
    {
        // Arrange - ids are wire-visible and never renumbered

        // Act
        byte actual = (byte)channel;

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NetChannel_DefinedValues_AreExactlyTheFourLogicalChannels()
    {
        // Arrange / Act
        NetChannel[] values = Enum.GetValues<NetChannel>();

        // Assert
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(DisconnectReason.Unspecified, 0)]
    [InlineData(DisconnectReason.Requested, 1)]
    [InlineData(DisconnectReason.Kicked, 2)]
    [InlineData(DisconnectReason.ProtocolViolation, 3)]
    [InlineData(DisconnectReason.Timeout, 4)]
    [InlineData(DisconnectReason.TransportError, 5)]
    public void DisconnectReason_Values_AreStable(DisconnectReason reason, byte expected)
    {
        // Arrange / Act
        byte actual = (byte)reason;

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NetAssertCode_PollReentered_IsAboveTheSimRange()
    {
        // Arrange - the Sim owns assert codes below 1000

        // Act
        ushort code = NetAssertCode.PollReentered;

        // Assert
        Assert.True(code >= 1000);
    }
}
