using System.Globalization;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// Boot step 5, <c>session</c> (ADR-0019 section 3, ADR-0011 section 2; godot-host-shell story 005): builds the process's host session.
/// <see cref="HostSession"/> runs <c>SimComposition.Build</c> (profile <c>M1aGreybox</c> over <see cref="M1aWorldSource"/>, step 2's
/// <see cref="ContentSet"/> by reference, the run seed), the loopback transport pair, the host and client adapters, the tick driver
/// (the clock's frequency, <c>SimSettings</c>' catch-up cap) and the frame runner (<c>CatchUpGuardMs = 0</c>), and admits the local peer
/// into slot 0. The host client's layer gets the owned-carrier step (<see cref="CarrierStepBinder"/>, player-character 020) over the
/// client's own copy of the world tables. The driver is stopped again so that <c>GameLoop</c> starts it on the first <c>_Process</c>
/// (ADR-0002). The run seed is <see cref="M1aRunSeed"/> unless <c>--seed=&lt;n&gt;</c> is in the user args (or a smoke sets
/// <see cref="BootOptions.Seed"/>); it is logged. Product: <see cref="BootContext.Session"/>.
/// </summary>
/// <example><code>IBootStep five = new SessionBootStep();   // BOOT 5 session: loopback HostSession profile=M1aGreybox seed=… ok</code></example>
public sealed class SessionBootStep : IBootStep
{
    /// <summary>The M-1a run seed (a named constant; never the wall clock or <c>Random</c>).</summary>
    public const ulong M1aRunSeed = 0x4D31_6147_5245_5931UL;

    /// <summary>The user-arg prefix of a run-seed override.</summary>
    public const string SeedArgPrefix = "--seed=";

    /// <inheritdoc />
    public int Number => BootPlan.SessionStep;

    /// <inheritdoc />
    public string Name => "session";

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No content set (step 2), no clock, a session already built, or an unparsable <c>--seed=</c>.</exception>
    public BootStepResult Run(BootContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ContentSet content = ctx.Content ?? throw new InvalidOperationException("Boot step 5 needs step 2's content set.");
        IMonotonicClock clock = ctx.Options.Clock ?? throw new InvalidOperationException("Boot step 5 needs a monotonic clock (BootOptions.Clock).");
        if (ctx.Session is not null)
        {
            throw new InvalidOperationException("Boot step 5 already built this boot's session.");
        }

        (ulong seed, string seedSource) = ResolveSeed(ctx.Options);
        SimProfile profile = ctx.Options.FixtureProfile ?? SimProfile.M1aGreybox;
        IWorldSource worldSource = M1aWorldSource.Instance;

        // The client's carrier step reads its own world copy, settings and static stat sheet; never the host world.
        WorldTables clientWorld = ClientWorldTables.Build(worldSource, content);
        CarrierStepBinder carrier = new(
            clientWorld,
            PlayerSettings.FromBalance(content.Player),
            StatSheet.Build(content, new StatSources([])),
            new EmptyRemoteCapsuleSource(),
            new EmptySupportTransformSource());

        ctx.Log.Write(string.Create(
            CultureInfo.InvariantCulture,
            $"BOOT 5 session: loopback HostSession profile={profile.Name} seed={seed} ({seedSource}) world_source={worldSource.Kind}v{worldSource.Version} clock_frequency={clock.Frequency} catch_up_cap={content.Settings.Core.MaxCatchUpTicksPerFrame} catch_up_guard_ms={FrameRunnerSettings.Default.CatchUpGuardMs}"));
        var options = new HostSessionOptions { Frame = FrameRunnerSettings.Default, ConfigureClient = carrier.BindTo };
        var session = new HostSession(
            new SimBuildArgs(profile, content, seed) { WorldSource = worldSource },
            clock,
            ThrowingCodecs.CreateOps(),
            ThrowingCodecs.CreateEvents(),
            options);

        // HostSession starts its driver; GameLoop starts it on the first _Process instead, so boot time never becomes ticks (F12).
        session.Driver.Stop();
        ctx.Session = new BootSession(session, clientWorld, carrier, clock, seed);
        return BootStepResult.Ok;
    }

    /// <summary>The run seed and where it came from: the options, then <c>--seed=&lt;n&gt;</c> (decimal or <c>0x</c> hex), then <see cref="M1aRunSeed"/>.</summary>
    /// <param name="options">The boot options.</param>
    /// <returns>The seed and its source label.</returns>
    /// <exception cref="InvalidOperationException">A <c>--seed=</c> value that is not a 64-bit unsigned number.</exception>
    public static (ulong Seed, string Source) ResolveSeed(BootOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Seed is { } fixedSeed)
        {
            return (fixedSeed, "BootOptions.Seed");
        }

        foreach (string arg in options.UserArgs)
        {
            if (!arg.StartsWith(SeedArgPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string text = arg[SeedArgPrefix.Length..];
            bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (!ulong.TryParse(hex ? text[2..] : text, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed))
            {
                throw new InvalidOperationException($"--seed= value '{text}' is not a 64-bit unsigned number.");
            }

            return (parsed, SeedArgPrefix + "<n>");
        }

        return (M1aRunSeed, nameof(M1aRunSeed));
    }
}

/// <summary>
/// Boot step 6, <c>sim</c> (ADR-0019 section 3, ADR-0016 section 1; godot-host-shell story 005): after step 5's <c>Build</c> returned,
/// assembles the client ports (the request sink and host clock from the session, then the <see cref="IPortContributor"/>s in order,
/// then null objects or defaults for whatever is still unset, each named in the <c>BOOT 6 sim: ports: …</c> line), calls
/// <c>ClientLayer.Attach</c> once, constructs the <see cref="RenderClock"/> over the host clock and the driver, and checks on the boot
/// thread that the world is <c>Ready</c> at tick 0 (<c>BOOT 6 sim: compose+attach …</c>).
/// </summary>
/// <example><code>IBootStep six = new AttachPortsBootStep(BootPlan.CreatePortContributors());</code></example>
public sealed class AttachPortsBootStep : IBootStep
{
    private readonly IPortContributor[] _contributors;

    /// <summary>Creates the step.</summary>
    /// <param name="contributors">The port contributors, run in order before <c>Attach</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="contributors"/> or an entry is null.</exception>
    public AttachPortsBootStep(IReadOnlyList<IPortContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        _contributors = new IPortContributor[contributors.Count];
        for (int i = 0; i < _contributors.Length; i++)
        {
            _contributors[i] = contributors[i] ?? throw new ArgumentNullException(nameof(contributors), $"Port contributor {i} is null.");
        }
    }

    /// <inheritdoc />
    public int Number => BootPlan.SimStep;

    /// <inheritdoc />
    public string Name => "sim";

    /// <summary>The port contributors, in run order.</summary>
    public IReadOnlyList<IPortContributor> Contributors => _contributors;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No session (step 5), off the boot thread, or the world is not Ready at tick 0.</exception>
    public BootStepResult Run(BootContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        BootSession booted = ctx.Session ?? throw new InvalidOperationException("Boot step 6 needs step 5's session.");
        HostSession session = booted.Session;
        var ports = new ClientPortsBuilder { Requests = new ClientAdapterRequestSink(session.ClientAdapter), HostClock = session.ClientAdapter.Clock };
        foreach (IPortContributor c in _contributors)
        {
            ctx.Log.Write($"BOOT 6 sim: port contributor {c.GetType().Name}");
            c.Contribute(ports, ctx);
        }

        ClientPorts attached = ports.Build(out string summary);
        session.Client.Attach(in attached);
        ctx.Log.Write($"BOOT 6 sim: ports: {summary}");
        booted.RenderClock = new RenderClock(session.HostClock, session.Driver);

        int thread = Environment.CurrentManagedThreadId;
        SimState state = session.World.State;
        uint tick = session.World.CurrentTick;
        if (state != SimState.Ready || tick != 0 || thread != ctx.BootThreadId)
        {
            throw new InvalidOperationException($"After compose+attach the world must be Ready at tick 0 on the boot thread {ctx.BootThreadId}; it is {state} at tick {tick} on thread {thread}.");
        }

        ctx.Log.Write(string.Create(CultureInfo.InvariantCulture, $"BOOT 6 sim: compose+attach world={state} tick={tick} client_attached={session.Client.IsAttached} thread={thread}"));
        return BootStepResult.Ok;
    }
}
