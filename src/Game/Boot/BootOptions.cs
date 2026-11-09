using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Hosting;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// Inputs of boot steps 5-6 (godot-host-shell story 005) on <see cref="BootContext.Options"/>. <c>GameLoop</c> supplies the real values
/// (the Stopwatch clock and the user args); <see cref="Clock"/>, <see cref="Seed"/> and <see cref="FixtureProfile"/> are otherwise
/// smoke and test seams, and their defaults are what a real run uses.
/// </summary>
/// <example>
/// <code>
/// var options = new BootOptions { Clock = fakeClock, Seed = 7 };   // a smoke's deterministic session
/// </code>
/// </example>
public sealed class BootOptions
{
    /// <summary>The monotonic clock the session's frame runner reads once per frame; step 5 fails without one.</summary>
    public IMonotonicClock? Clock { get; init; }

    /// <summary>A run seed that overrides both <c>--seed=</c> and <see cref="SessionBootStep.M1aRunSeed"/>; null in real runs.</summary>
    public ulong? Seed { get; init; }

    /// <summary>The Sim profile to compose; null composes <see cref="SimProfile.M1aGreybox"/> (real runs).</summary>
    public SimProfile? FixtureProfile { get; init; }

    /// <summary>The process's user args (<c>OS.GetCmdlineUserArgs()</c>); step 5 reads <c>--seed=&lt;n&gt;</c> from them.</summary>
    public IReadOnlyList<string> UserArgs { get; init; } = [];
}
