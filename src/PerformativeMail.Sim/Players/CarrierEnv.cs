using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// Everything <c>OwnedCarrier.Step</c> may read besides its state and input (ADR-0013 section 1 and 7): the world query, the two tick-time
/// ports, the settings and the carrier's own id. A <c>ref struct</c> so it cannot be captured or boxed; keep it out of lambdas and async code.
/// It holds no <c>SimWorld</c>, clock, RNG or static. <see cref="PlayerSettings"/> is an immutable class, so the field is a plain reference
/// (passing the env with <c>in</c> costs one pointer). <b><c>default(CarrierEnv)</c> is invalid</b> (null ports and settings); always build one through the
/// constructor, <see cref="ForM1a"/> or <c>PlayersSystem.CreateCarrierEnv</c>.
/// </summary>
/// <example>
/// <code>
/// CarrierEnv env = players.CreateCarrierEnv(selfId);   // M-1a: empty ports
/// OwnedCarrier.Step(ref state, in input, in env, ref scratch);
/// </code>
/// </example>
public readonly ref struct CarrierEnv
{
    /// <summary>The world's read interface.</summary>
    public readonly IWorldQuery World;

    /// <summary>Other players' capsules (latest received snapshot).</summary>
    public readonly IRemoteCapsuleSource Remotes;

    /// <summary>Vehicles and belts (latest received snapshot).</summary>
    public readonly ISupportTransformSource Supports;

    /// <summary>The <c>players</c> settings.</summary>
    public readonly PlayerSettings Settings;

    /// <summary>The stepped carrier's entity id (push tie-break).</summary>
    public readonly EntityId Self;

    /// <summary>Creates an env over explicit ports (real sources at M0 and later, scripted doubles in tests).</summary>
    /// <param name="world">The world query.</param>
    /// <param name="remotes">The remote-capsule port.</param>
    /// <param name="supports">The support-transform port.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="self">The carrier's id.</param>
    /// <exception cref="ArgumentNullException">An argument is null (checked at construction, never inside the solver).</exception>
    public CarrierEnv(IWorldQuery world, IRemoteCapsuleSource remotes, ISupportTransformSource supports, PlayerSettings settings, EntityId self)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(remotes);
        ArgumentNullException.ThrowIfNull(supports);
        ArgumentNullException.ThrowIfNull(settings);
        World = world;
        Remotes = remotes;
        Supports = supports;
        Settings = settings;
        Self = self;
    }

    /// <summary>
    /// The <c>M1aGreybox</c> default: solo, so the ports are the empty sources. The empty-source instances are passed in (owned by
    /// <see cref="PlayersSystem"/>) rather than held in statics, because the solver surface forbids statics.
    /// </summary>
    /// <param name="world">The world query.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="self">The carrier's id.</param>
    /// <param name="remotes">The empty remote-capsule source.</param>
    /// <param name="supports">The empty support source.</param>
    /// <returns>The env.</returns>
    public static CarrierEnv ForM1a(
        IWorldQuery world, PlayerSettings settings, EntityId self, EmptyRemoteCapsuleSource remotes, EmptySupportTransformSource supports) =>
        new(world, remotes, supports, settings, self);
}
