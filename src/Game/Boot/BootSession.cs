using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// What boot steps 5-6 hand to <c>GameLoop</c> (godot-host-shell story 005): the one <see cref="HostSession"/> of the process (one
/// <c>SimWorld</c>, one client layer, the host's own client only through <c>ClientAdapter</c> over <c>LoopbackTransport</c>), the
/// owned-carrier step bound into that client, the clock, the run seed and, after step 6, the attached carrier ports and the
/// <see cref="RenderClock"/>. After boot <c>GameLoop</c> is its only holder. The read-only facade members are for smoke checks, so
/// they do not have to name the Sim's pump types. Main thread only.
/// </summary>
/// <example>
/// <code>
/// BootSession s = ctx.Session!;
/// uint tick = s.CurrentTick;          // 0 right after step 6
/// bool joined = s.LocalPlayer!.IsKnown;
/// </code>
/// </example>
public sealed class BootSession
{
    internal BootSession(HostSession session, CarrierStepBinder carrier, IMonotonicClock clock, ulong seed)
    {
        Session = session;
        Carrier = carrier;
        Clock = clock;
        Seed = seed;
    }

    /// <summary>The host session (world, client layer, adapters, frame runner, tick driver).</summary>
    public HostSession Session { get; }

    /// <summary>The owned-carrier step bound at <c>CarrierStep</c> (its <see cref="CarrierStepBinder.View"/> is the published carrier).</summary>
    public CarrierStepBinder Carrier { get; }

    /// <summary>The monotonic clock the frame runner reads.</summary>
    public IMonotonicClock Clock { get; }

    /// <summary>The run seed the world was composed with.</summary>
    public ulong Seed { get; }

    /// <summary>The render clock (ADR-0016 section 6) over the host clock and the driver; set by step 6.</summary>
    public RenderClock? RenderClock { get; internal set; }

    /// <summary>The local-player port the carrier contributor attached; null before step 6.</summary>
    public HostLocalPlayer? LocalPlayer { get; internal set; }

    /// <summary>The transform-sample adapter the carrier contributor attached; null before step 6.</summary>
    public HostSampleSink? Samples { get; internal set; }

    /// <summary>The tick driver.</summary>
    public TickDriver Driver => Session.Driver;

    /// <summary>The world's current tick.</summary>
    public uint CurrentTick => Session.World.CurrentTick;

    /// <summary>The world's state.</summary>
    public SimState WorldState => Session.World.State;

    /// <summary>The world's counters (host side).</summary>
    public SimCounters WorldCounters => Session.World.Counters;

    /// <summary>True once step 6 attached the client layer's ports.</summary>
    public bool IsAttached => Session.Client.IsAttached;

    /// <summary>Player Character's host API (host-side checks only; never handed to the client).</summary>
    public IPlayersApi? Players => Session.Host.Players;
}
