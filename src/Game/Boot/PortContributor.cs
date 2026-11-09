using System.Text;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// Adds client ports before boot step 6 attaches them (godot-host-shell story 005, ADR-0016 section 1). Each contributor is one explicit
/// line in <see cref="BootPlan.CreatePortContributors"/>; step 6 runs them in that order, after the session exists and before
/// <c>ClientLayer.Attach</c>. A contributor reports failure by throwing (the boot error screen shows).
/// </summary>
/// <example>
/// <code>
/// public sealed class HeldItemPorts : IPortContributor   // player-character 025
/// {
///     public void Contribute(ClientPortsBuilder b, BootContext ctx) { /* b.X = new XAdapter(ctx.Session!); */ }
/// }
/// </code>
/// </example>
public interface IPortContributor
{
    /// <summary>Sets this contributor's ports on <paramref name="b"/>.</summary>
    /// <param name="b">The ports being assembled.</param>
    /// <param name="ctx">The boot context (<see cref="BootContext.Session"/> is set).</param>
    void Contribute(ClientPortsBuilder b, BootContext ctx);
}

/// <summary>
/// Assembles the <see cref="ClientPorts"/> step 6 attaches. Step 6 sets <see cref="Requests"/> and <see cref="HostClock"/> from the
/// session, then the <see cref="IPortContributor"/>s set theirs. <see cref="Build"/> binds every port still unset to a named null object
/// (or, for the carrier load, to the constant full-speed source) and reports each choice in the boot log line, never a silent default.
/// Boot-time only; main thread.
/// </summary>
/// <example>
/// <code>
/// var b = new ClientPortsBuilder { Requests = requests, HostClock = clock };
/// ClientPorts ports = b.Build(out string line);   // "panel=Null(...) requests=ClientAdapterRequestSink ..."
/// </code>
/// </example>
public sealed class ClientPortsBuilder
{
    /// <summary>The request sink (step 6: <see cref="ClientAdapterRequestSink"/>).</summary>
    public IRequestSink? Requests { get; set; }

    /// <summary>The host tick source (step 6: the client adapter's estimator, the clock the layer was built with).</summary>
    public IHostClock? HostClock { get; set; }

    /// <summary>The owned carrier's sample sink (player-character 020: <see cref="HostSampleSink"/>).</summary>
    public ITransformSampleSink? TransformSamples { get; set; }

    /// <summary>The owned carrier's load (no client weight adapter exists yet; unset binds <see cref="ConstantCarrierLoadSource.Unloaded"/>).</summary>
    public ICarrierLoadSource? CarrierLoad { get; set; }

    /// <summary>The local player (player-character 020: <see cref="HostLocalPlayer"/>).</summary>
    public ILocalPlayer? LocalPlayer { get; set; }

    /// <summary>Fills the unset ports and builds the struct.</summary>
    /// <param name="summary">One <c>name=Type</c> entry per port, for the <c>BOOT 6 sim: ports: …</c> line.</param>
    /// <returns>The ports, every member non-null.</returns>
    /// <exception cref="InvalidOperationException"><see cref="HostClock"/> is unset (it has no null object).</exception>
    public ClientPorts Build(out string summary)
    {
        // The panel port (IPanelView) is declared by manual-delivery 002 and implemented by the own-container mirror; until then it
        // is not a member of ClientPorts at all, and the line says so.
        var line = new StringBuilder("panel=Null(IPanelView not declared yet)");
        IRequestSink requests = Requests ?? NullRequestSink.Instance;
        Append(line, "requests", requests, Requests is null);
        IHostClock clock = HostClock ?? throw new InvalidOperationException("ClientPortsBuilder.HostClock is unset; step 6 sets it from the session.");
        Append(line, "hostClock", clock, isDefault: false);
        ITransformSampleSink samples = TransformSamples ?? NullTransformSampleSink.Instance;
        Append(line, "samples", samples, TransformSamples is null);
        ICarrierLoadSource load = CarrierLoad ?? ConstantCarrierLoadSource.Unloaded();
        Append(line, "load", load, CarrierLoad is null);
        ILocalPlayer local = LocalPlayer ?? UnknownLocalPlayer.Instance;
        Append(line, "localPlayer", local, LocalPlayer is null);
        summary = line.ToString();
        return new ClientPorts { Requests = requests, HostClock = clock, TransformSamples = samples, CarrierLoad = load, LocalPlayer = local };
    }

    private static void Append(StringBuilder line, string name, object port, bool isDefault) =>
        line.Append(' ').Append(name).Append('=').Append(port.GetType().Name).Append(isDefault ? "(default)" : string.Empty);

    /// <summary>The null request sink: every send is the local "busy".</summary>
    private sealed class NullRequestSink : IRequestSink
    {
        public static readonly NullRequestSink Instance = new();

        public bool TrySend(in Op op) => false;
    }

    /// <summary>The null sample sink: every write is refused (and counted by the carrier step).</summary>
    private sealed class NullTransformSampleSink : ITransformSampleSink
    {
        public static readonly NullTransformSampleSink Instance = new();

        public bool TryWrite(in TransformSample s) => false;
    }

    /// <summary>The null local player: never known, so the carrier step never seeds.</summary>
    private sealed class UnknownLocalPlayer : ILocalPlayer
    {
        public static readonly UnknownLocalPlayer Instance = new();

        public PlayerSlot Slot => PlayerSlot.None;

        public EntityId Id => EntityId.None;

        public bool IsKnown => false;
    }
}
