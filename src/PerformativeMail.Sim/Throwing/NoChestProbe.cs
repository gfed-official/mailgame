using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The default <see cref="IChestOriginProbe"/>: the chest-to-origin segment is always clear, so <c>originFromChest</c> is
/// always false (ADR-0015 section 3; Throwing story 003 supplies the real probe). A stateless sealed null object, so the
/// shared <see cref="Instance"/> holds no state (ADR-0003 section 4).
/// </summary>
/// <example>
/// <code>
/// IChestOriginProbe probe = NoChestProbe.Instance;
/// </code>
/// </example>
public sealed class NoChestProbe : IChestOriginProbe
{
    /// <summary>The shared stateless instance.</summary>
    public static readonly NoChestProbe Instance = new();

    private NoChestProbe()
    {
    }

    /// <inheritdoc />
    public bool IsBlocked(in Vec3Mm chest, in Vec3Mm origin) => false;
}
