using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.World;

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
    internal BootSession(HostSession session, IWorldQuery world, CarrierStepBinder carrier, IMonotonicClock clock, ulong seed)
    {
        Session = session;
        World = world;
        Carrier = carrier;
        Clock = clock;
        Seed = seed;
    }

    /// <summary>The host session (world, client layer, adapters, frame runner, tick driver).</summary>
    public HostSession Session { get; }

    /// <summary>The client's own immutable copy of the world tables (<see cref="ClientWorldTables"/>); the carrier step and Presentation read it, never the host world.</summary>
    public IWorldQuery World { get; }

    /// <summary>The owned-carrier step bound at <c>CarrierStep</c> (its <see cref="CarrierStepBinder.View"/> is the published carrier).</summary>
    public CarrierStepBinder Carrier { get; }

    /// <summary>The monotonic clock the frame runner reads.</summary>
    public IMonotonicClock Clock { get; }

    /// <summary>The run seed the world was composed with.</summary>
    public ulong Seed { get; }

    /// <summary>The render clock (ADR-0016 section 6) over the host clock and the driver; set by step 6.</summary>
    public RenderClock? RenderClock { get; internal set; }

    /// <summary>The outcome-beat feed step 7 set as the client adapter's <see cref="ClientAdapter.DecodedEvents"/>; null before step 7.</summary>
    public PresentationEventStream? Events { get; internal set; }

    /// <summary>The local-player port the carrier contributor attached; null before step 6.</summary>
    public HostLocalPlayer? LocalPlayer { get; internal set; }

    /// <summary>The transform-sample adapter the carrier contributor attached; null before step 6.</summary>
    public HostSampleSink? Samples { get; internal set; }

    /// <summary>The client mirrors step 5 bound (wallet, run, flights).</summary>
    public ClientMirrors? Mirrors { get; internal init; }

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

    /// <summary>The panel port step 6 attached; null before step 6.</summary>
    public IPanelView? Panel => Session.Client.Ports.Panel;

    /// <summary>
    /// Smoke seam (player-character 022's <c>player-view</c> smoke): latches a move intent as the client's continuous input state, as the
    /// input router will (Q15 on the unit disc, y forward). Keeps smoke code from naming the client layer (<c>check-sim-access.sh</c>).
    /// </summary>
    /// <param name="moveX">Move intent X, Q15.</param>
    /// <param name="moveY">Move intent Y, Q15.</param>
    public void SetMoveInputForSmoke(short moveX, short moveY) => Session.Client.InputWriter.SetState(new InputState { MoveX = moveX, MoveY = moveY });
}
