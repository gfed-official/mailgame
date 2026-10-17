namespace PerformativeMail.Client.Core.Net;

/// <summary>Outcome of <see cref="HostAdapter.AdmitLocal"/>. Values are stable and never renumbered.</summary>
public enum AdmitResult : byte
{
    /// <summary>The <c>System</c> <c>PlayerJoin</c> was enqueued; the peer is now <see cref="PeerState.Joining"/>.</summary>
    Ok = 0,

    /// <summary>The slot is <c>None</c> or not below <c>PlayerSlot.Count</c>.</summary>
    SlotInvalid = 1,

    /// <summary>The peer already is <see cref="PeerState.Joining"/> or <see cref="PeerState.Admitted"/>.</summary>
    AlreadyAdmitted = 2,

    /// <summary>Another peer holds (or is joining into) that slot.</summary>
    SlotTaken = 3,

    /// <summary>The peer table has no free row.</summary>
    TableFull = 4,

    /// <summary>The Sim refused the <c>PlayerJoin</c> op (ring full or world not accepting ops); the peer is unchanged.</summary>
    Refused = 5,
}
