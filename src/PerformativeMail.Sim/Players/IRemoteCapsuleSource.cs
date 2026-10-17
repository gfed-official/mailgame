namespace PerformativeMail.Sim.Players;

/// <summary>
/// Tick-time port for other players' capsules (ADR-0013 section 7, TR-movement-004). On a client it reads the ClientLayer mirror's latest
/// received transform batch; on the host, the T-1 ingest latch. Never render-time interpolation. Empty in solo (M-1a).
/// </summary>
/// <example><code>foreach (RemoteCapsule c in env.Remotes.Capsules) { /* push */ }</code></example>
public interface IRemoteCapsuleSource
{
    /// <summary>The latest received capsules; valid until the next tick. Allocation-free.</summary>
    ReadOnlySpan<RemoteCapsule> Capsules { get; }
}
