using PerformativeMail.Client.Core.Mirror;
using PerformativeMail.Client.Core.Throwing;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Economy;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// The host client's mirror parts the HUD and the throw views read (M-1a wave-3 assembly, ADR-0016 section 1): the wallet and run
/// mirrors bound with <see cref="ClientLayerBuilder.BindMirrorPart"/>, and the flight mirror fed through its consumer on the session's
/// <see cref="MirrorDrainStep"/>. Step 5 builds it, binds it and publishes the three for step 7. Main thread only.
/// </summary>
/// <example>
/// <code>
/// var mirrors = new ClientMirrors(new ThrowSettings(content.Throw), PlayerSlot.FromValue(0));
/// options = new HostSessionOptions { ConfigureClient = b => { carrier.BindTo(b); mirrors.BindTo(b); } };
/// mirrors.Register(session.MirrorDrain);
/// </code>
/// </example>
/// <param name="throwSettings">The throw knobs the flight mirror samples with.</param>
/// <param name="ownSlot">The local player's slot (whose launches take the local verdict).</param>
public sealed class ClientMirrors(ThrowSettings throwSettings, PlayerSlot ownSlot)
{
    /// <summary>The wallet mirror.</summary>
    public WalletMirror Wallet { get; } = new();

    /// <summary>The run (shift phase) mirror.</summary>
    public RunMirror Run { get; } = new();

    /// <summary>The flight mirror's drain consumer; the flight view samples it once per frame.</summary>
    public FlightMirrorConsumer Flights { get; } = new(new FlightMirror(throwSettings, ownSlot));

    /// <summary>Binds the wallet and run mirrors; call from <c>HostSessionOptions.ConfigureClient</c>.</summary>
    /// <param name="builder">The host client's layer builder.</param>
    public void BindTo(ClientLayerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.BindMirrorPart(WalletMirror.Kinds, Wallet);
        builder.BindMirrorPart(RunMirror.Kinds, Run);
    }

    /// <summary>Registers the flight consumer; call once the session exists, before the first frame.</summary>
    /// <param name="drain">The session's MirrorDrain step.</param>
    public void Register(MirrorDrainStep drain)
    {
        ArgumentNullException.ThrowIfNull(drain);
        drain.Register(Flights);
    }
}
