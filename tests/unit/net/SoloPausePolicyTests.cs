using PerformativeMail.Client.Core.Net;

namespace PerformativeMail.Sim.Tests.Unit.Net;

/// <summary>
/// Covers <see cref="SoloPausePolicy"/>, <see cref="IPeerPresence"/> and <see cref="LoopbackPeerPresence"/> (story
/// godot-host-shell/003 AC4, TR-sim-009, ADR-0002 section 4, GDD simulation-core R16): Networking and Session owns the
/// solo-pause rule; the M-1a loopback presence reports no remote peers, so a solo pause is allowed; any remote peer
/// refuses it with a reason. The <c>GameLoop</c> side is proven by the <c>pause-driver-state</c> smoke.
/// </summary>
public class SoloPausePolicyTests
{
    private sealed class FakePresence(bool anyRemotePeer) : IPeerPresence
    {
        public bool AnyRemotePeer { get; } = anyRemotePeer;
    }

    [Fact]
    public void Loopback_ReportsNoRemotePeers()
    {
        // Act
        bool any = LoopbackPeerPresence.Instance.AnyRemotePeer;

        // Assert
        Assert.False(any);
        Assert.Same(LoopbackPeerPresence.Instance, LoopbackPeerPresence.Instance);
    }

    [Fact]
    public void Loopback_AllowsPause_WithEmptyReason()
    {
        // Act
        bool allowed = SoloPausePolicy.CanPause(LoopbackPeerPresence.Instance, out string reason);

        // Assert
        Assert.True(allowed);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void NoRemotePeer_AllowsPause_WithEmptyReason()
    {
        // Act
        bool allowed = SoloPausePolicy.CanPause(new FakePresence(false), out string reason);

        // Assert
        Assert.True(allowed);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void RemotePeerPresent_RefusesPause_WithReason()
    {
        // Act
        bool allowed = SoloPausePolicy.CanPause(new FakePresence(true), out string reason);

        // Assert
        Assert.False(allowed);
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Equal(SoloPausePolicy.RemotePeerPresentReason, reason);
        Assert.Contains("multiplayer never pauses", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NullPresence_Throws()
    {
        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => SoloPausePolicy.CanPause(null!, out _));
    }
}
