using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;
using ClientLayerShell = PerformativeMail.Sim.ClientLayer.ClientLayer;

namespace PerformativeMail.Sim.Hosting;

/// <summary>
/// The per-frame pump (GDD R2/R17, ADR-0002 section 2, ADR-0011 section 2), Godot-free. One per process; the host's
/// <c>GameLoop</c> calls <see cref="RunFrame"/> once per <c>_Process</c>. Headless hosts skip the runner and call
/// <c>SimWorld.Tick()</c> in a loop. Main thread only, not thread-safe; <see cref="RunFrame"/> allocates 0 B.
/// <para>
/// <see cref="RunFrame"/> order: (1) <see cref="IFrameTransport.PumpInbound"/>; (2) <c>n = Driver.Advance(clock.Timestamp)</c>;
/// (3) for <c>i</c> in 0..n-1: <c>client.Step(i == 0)</c>, <c>world?.Tick()</c>, <see cref="IFrameTransport.FlushOutbound"/>
/// for that tick; (4) if the driver's <c>Hitches</c> rose this frame, one <see cref="CheckCode.TickHitch"/> record
/// (a = ticks dropped this frame, b = clock counts clamped this frame) in the world's check ring.
/// </para>
/// <para>
/// A pure client (<c>world == null</c>) runs the client step and flush only: the flush tick is the client tick counter,
/// its owner thread is the one that constructed the runner, and no hitch record is written (no ring is specified).
/// </para>
/// </summary>
/// <example>
/// <code>
/// var runner = new FrameRunner(clock, new TickDriver(clock.Frequency, 4), client, world, transport, FrameRunnerSettings.Default);
/// runner.Driver.Start(clock.Timestamp);          // first _Process after boot
/// runner.RunFrame();                             // each frame
/// int alpha = runner.Driver.AlphaPermille;       // Presentation only
/// </code>
/// </example>
public sealed class FrameRunner
{
    private readonly IMonotonicClock _clock;
    private readonly ClientLayerShell _client;
    private readonly SimWorld? _world;
    private readonly IFrameTransport _transport;
    private readonly int _ownerThread;

    /// <summary>Creates a runner.</summary>
    /// <param name="clock">The host's monotonic clock; read once per frame.</param>
    /// <param name="driver">The tick driver; the host calls <c>Start</c> on it.</param>
    /// <param name="client">The client shell, stepped before every tick.</param>
    /// <param name="world">The host's world, or null for a pure client.</param>
    /// <param name="transport">The adapter hooks.</param>
    /// <param name="settings">Runner settings (guard off at M-1a).</param>
    /// <exception cref="ArgumentNullException">A reference argument other than <paramref name="world"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="FrameRunnerSettings.CatchUpGuardMs"/> is negative.</exception>
    /// <example><code>var runner = new FrameRunner(clock, driver, client, null, transport, FrameRunnerSettings.Default); // pure client</code></example>
    public FrameRunner(
        IMonotonicClock clock,
        TickDriver driver,
        ClientLayerShell client,
        SimWorld? world,
        IFrameTransport transport,
        FrameRunnerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(transport);
        if (settings.CatchUpGuardMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "CatchUpGuardMs must not be negative.");
        }

        _clock = clock;
        Driver = driver;
        _client = client;
        _world = world;
        _transport = transport;
        Settings = settings;
        _ownerThread = world is null ? Environment.CurrentManagedThreadId : world.OwnerThreadId;
    }

    /// <summary>The tick driver (Presentation reads <c>Driver.AlphaPermille</c>).</summary>
    public TickDriver Driver { get; }

    /// <summary>The settings given at construction.</summary>
    public FrameRunnerSettings Settings { get; }

    /// <summary>
    /// Runs one frame: pump inbound, advance the driver, run the released ticks (client step, world tick, outbound flush
    /// each), then record a tick hitch if ticks were dropped. Under SIM_ASSERT, calling from a thread other than the
    /// owner asserts (<see cref="SimAssertCode.WrongThread"/>).
    /// </summary>
    /// <example><code>runner.RunFrame();   // from GameLoop._Process</code></example>
    public void RunFrame()
    {
        AssertOwnerThread();

        // Step 1: drain the adapter's inbound queue into EnqueueOp and the sample ring (remote, then loopback).
        _transport.PumpInbound();

        long hitchesBefore = Driver.Hitches;
        long droppedBefore = Driver.DroppedTicksTotal;
        long clampedBefore = Driver.ClampedTimeTotal;

        // Step 2: the only clock read of the frame.
        int n = Driver.Advance(_clock.Timestamp);

        // Step 3: the released ticks. M0 wall-clock guard: NOT built at M-1a (catchUpGuardMs = 0). When it lands (ADR-0002
        // section 2 step 3) it breaks out of this loop once the guard elapsed (at least one tick always runs) and must call
        // Driver.DropRemaining(n - i - 1), with a test; TD-012(1): DropRemaining's assert is against the cumulative total,
        // so it cannot catch m > n for the current frame.
        SimWorld? world = _world;
        for (int i = 0; i < n; i++)
        {
            uint tick = world is null ? _client.ClientTick : world.CurrentTick;
            _client.Step(isFirstTickInBatch: i == 0);
            world?.Tick();
            _transport.FlushOutbound(tick);
        }

        // Step 4: one hitch record per frame in which the driver dropped ticks.
        if (world is not null && Driver.Hitches > hitchesBefore)
        {
            world.CheckRing.Record(
                CheckCode.TickHitch,
                world.CurrentTick,
                Driver.DroppedTicksTotal - droppedBefore,
                Driver.ClampedTimeTotal - clampedBefore);
        }
    }

    [System.Diagnostics.Conditional("SIM_ASSERT")]
    private void AssertOwnerThread() =>
        SimAssert.That(Environment.CurrentManagedThreadId == _ownerThread, SimAssertCode.WrongThread);
}
