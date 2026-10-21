using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The F1 input (ADR-0015 Key Interfaces plus the two references the Mail Lead added so
/// <see cref="LaunchMath.Compute"/> keeps the exact ADR signature): the thrower's feet, the client-quantised int16 x3
/// direction, the throwing hand, the thrown stack's weight class, the thrower's <c>ThrowRangePermille</c> (read by the
/// caller from the StatSheet; <see cref="LaunchMath"/> never touches <c>IStatSheet</c>), the body yaw (used only by the
/// defence branch), the <see cref="ThrowSettings"/> view and the <see cref="IChestOriginProbe"/> port. Immutable.
/// </summary>
/// <example>
/// <code>
/// var input = new LaunchInput(feet, 0, 3406, -16_026, 1, WeightClass.Light, 1000, 0, settings);
/// bool ok = LaunchMath.Compute(in input, out Launch l, out bool fromChest);
/// </code>
/// </example>
public readonly struct LaunchInput
{
    /// <summary>The thrower's feet, mm.</summary>
    public readonly Vec3Mm Feet;

    /// <summary>Direction x (client-quantised, length about 16 384).</summary>
    public readonly short Dx;

    /// <summary>Direction y (up).</summary>
    public readonly short Dy;

    /// <summary>Direction z.</summary>
    public readonly short Dz;

    /// <summary>Throwing hand: +1 right-handed, -1 left-handed (the GDD <c>side</c>).</summary>
    public readonly sbyte Side;

    /// <summary>The thrown stack's weight class (selects <c>baseSpeed[class]</c>).</summary>
    public readonly WeightClass Class;

    /// <summary>The thrower's <c>ThrowRangePermille</c> stat (1000 = no perk; Cannon Arm sets 1500).</summary>
    public readonly int ThrowRangePermille;

    /// <summary>The body yaw in BAM (ADR-0013 section 3), the horizontal basis only when <c>h &lt; 2048</c>.</summary>
    public readonly ushort BodyYawBam;

    /// <summary>The throw knobs.</summary>
    public readonly ThrowSettings Settings;

    /// <summary>The chest-to-origin probe (<see cref="NoChestProbe.Instance"/> when none was given).</summary>
    public readonly IChestOriginProbe ChestProbe;

    /// <summary>Creates an input.</summary>
    /// <param name="feet">The thrower's feet, mm.</param>
    /// <param name="dx">Direction x.</param>
    /// <param name="dy">Direction y.</param>
    /// <param name="dz">Direction z.</param>
    /// <param name="side">+1 right-handed, -1 left-handed.</param>
    /// <param name="weightClass">The thrown stack's weight class.</param>
    /// <param name="throwRangePermille">The thrower's <c>ThrowRangePermille</c>.</param>
    /// <param name="bodyYawBam">The body yaw in BAM.</param>
    /// <param name="settings">The throw knobs.</param>
    /// <param name="chestProbe">The chest-to-origin probe; null means <see cref="NoChestProbe.Instance"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
    public LaunchInput(
        Vec3Mm feet,
        short dx,
        short dy,
        short dz,
        sbyte side,
        WeightClass weightClass,
        int throwRangePermille,
        ushort bodyYawBam,
        ThrowSettings settings,
        IChestOriginProbe? chestProbe = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Feet = feet;
        Dx = dx;
        Dy = dy;
        Dz = dz;
        Side = side;
        Class = weightClass;
        ThrowRangePermille = throwRangePermille;
        BodyYawBam = bodyYawBam;
        Settings = settings;
        ChestProbe = chestProbe ?? NoChestProbe.Instance;
    }
}
