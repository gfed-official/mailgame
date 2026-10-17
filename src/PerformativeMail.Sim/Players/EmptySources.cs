namespace PerformativeMail.Sim.Players;

/// <summary>The M-1a remote-capsule source: solo play, no other players. Stateless; owned by <see cref="PlayersSystem"/> (no statics).</summary>
/// <example><code>var s = new EmptyRemoteCapsuleSource(); int n = s.Capsules.Length;   // 0</code></example>
public sealed class EmptyRemoteCapsuleSource : IRemoteCapsuleSource
{
    /// <inheritdoc />
    public ReadOnlySpan<RemoteCapsule> Capsules => default;
}

/// <summary>The M-1a support source: no vehicles and no belts. Stateless; owned by <see cref="PlayersSystem"/> (no statics).</summary>
/// <example><code>var s = new EmptySupportTransformSource(); bool belt = s.TryGetBeltVelocity(0, 0, out _, out _, out _);   // false</code></example>
public sealed class EmptySupportTransformSource : ISupportTransformSource
{
    /// <inheritdoc />
    public ReadOnlySpan<VehiclePose> Vehicles => default;

    /// <inheritdoc />
    public bool TryGetBeltVelocity(int tileX, int tileZ, out int vx, out int vz, out uint segmentId)
    {
        vx = 0;
        vz = 0;
        segmentId = 0;
        return false;
    }
}
