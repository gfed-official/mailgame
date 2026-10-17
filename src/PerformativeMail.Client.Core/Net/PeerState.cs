namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// Lifecycle of one peer-table entry (ADR-0011 section 4). At M-1a the host admits its own loopback peer locally
/// (<c>Connected</c> to <c>Joining</c> to <c>Admitted</c>); at M0 the <c>Hello</c> / <c>HelloAck</c> handshake slots in
/// between <c>Connected</c> and <c>Joining</c>. Only <see cref="Admitted"/> peers may submit requests. Values are stable.
/// </summary>
public enum PeerState : byte
{
    /// <summary>No entry (the peer table has no row for this peer).</summary>
    None = 0,

    /// <summary>The transport reported the peer; it has not been admitted. Its messages are dropped and counted.</summary>
    Connected = 1,

    /// <summary>The <c>System</c> <c>PlayerJoin</c> was enqueued; the join reply has not been delivered yet. Its messages are dropped and counted.</summary>
    Joining = 2,

    /// <summary>The join succeeded: the peer maps to a player id and slot, and its requests are decoded and enqueued.</summary>
    Admitted = 3,
}
