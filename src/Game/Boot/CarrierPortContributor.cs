using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Sim.Players.Client;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// Player-character 020's ports for the host client (godot-host-shell story 005): the local player (<see cref="HostLocalPlayer"/>,
/// slot 0 once the loopback join is answered) and the owned carrier's samples to the host world (<see cref="HostSampleSink"/>). The
/// carrier load is left unset, so step 6 binds the constant full-speed source and logs it as the default. It also publishes the owned
/// carrier's <see cref="ICarrierView"/> and the local player (<see cref="ILocalPlayerView"/>) into <see cref="BootContext.Published"/> for player-character
/// 022/023's views (story 006; 022's player presenter spawns the view once the local player is known).
/// </summary>
/// <example><code>IReadOnlyList&lt;IPortContributor&gt; ports = [new CarrierPortContributor()];</code></example>
public sealed class CarrierPortContributor : IPortContributor
{
    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Step 5 has not produced a session.</exception>
    public void Contribute(ClientPortsBuilder b, BootContext ctx)
    {
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(ctx);
        BootSession session = ctx.Session ?? throw new InvalidOperationException("CarrierPortContributor needs boot step 5's session.");
        var local = new HostLocalPlayer(session.Session);
        var samples = new HostSampleSink(session.Session.World, local);
        b.LocalPlayer = local;
        b.TransformSamples = samples;
        session.LocalPlayer = local;
        session.Samples = samples;
        ctx.Published.Add<ICarrierView>(session.Carrier.View);
        ctx.Published.Add<ILocalPlayerView>(local);
    }
}
