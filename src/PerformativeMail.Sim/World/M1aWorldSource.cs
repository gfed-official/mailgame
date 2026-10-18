namespace PerformativeMail.Sim.World;

/// <summary>The M-1a world source: the hand-authored <see cref="M1aStreet"/>, identified by <see cref="M1aStreet.Version"/>. Immutable singleton.</summary>
/// <example>
/// <code>
/// var args = new SimBuildArgs(profile, content, seed) { WorldSource = M1aWorldSource.Instance };
/// </code>
/// </example>
public sealed class M1aWorldSource : IWorldSource
{
    /// <summary>The <see cref="IWorldSource.Kind"/> of <see cref="M1aStreet"/>.</summary>
    public const ushort M1aKind = 1;

    private M1aWorldSource()
    {
    }

    /// <summary>The shared instance (it holds no state).</summary>
    public static M1aWorldSource Instance { get; } = new();

    /// <inheritdoc />
    public ushort Kind => M1aKind;

    /// <inheritdoc />
    public uint Version => (uint)M1aStreet.Version;

    /// <inheritdoc />
    public void Author(WorldTablesBuilder b)
    {
        M1aStreet.Author(b);
    }
}
