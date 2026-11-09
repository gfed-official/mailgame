using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// The ports the host shell attaches to a built <see cref="ClientLayer"/> once, after <c>SimComposition.Build</c> and before the first
/// <see cref="ClientLayer.Step"/> (ADR-0016 section 1 and Key Interfaces; added by godot-host-shell story 005). Steps read them through
/// <see cref="ClientStepContext.Ports"/>. Every port is required: an absent one is bound to a null object by the shell, never left null.
/// <para>
/// Differences from the Key Interfaces sketch (recorded in the story): <c>Inbox</c> is not here because the drain is Client.Core's
/// <c>MirrorDrainStep</c>, bound at build over its own inbox (network-adapter-loopback 007); <c>Panel</c> is added by the story that
/// declares <c>IPanelView</c> (manual-delivery 002). <see cref="TransformSamples"/>, <see cref="CarrierLoad"/> and
/// <see cref="LocalPlayer"/> are player-character story 020's additive ports. Init properties instead of readonly fields, so a port
/// can be appended without breaking a constructor.
/// </para>
/// </summary>
/// <example>
/// <code>
/// layer.Attach(new ClientPorts { Requests = requests, HostClock = clock, TransformSamples = sink, CarrierLoad = load, LocalPlayer = local });
/// </code>
/// </example>
public readonly struct ClientPorts
{
    /// <summary>The request sink (edge resolution and hold completion send through it).</summary>
    public IRequestSink Requests { get; init; }

    /// <summary>The host tick source (ADR-0011).</summary>
    public IHostClock HostClock { get; init; }

    /// <summary>Where the owned carrier's samples go (player-character 020).</summary>
    public ITransformSampleSink TransformSamples { get; init; }

    /// <summary>The owned carrier's load (player-character 020).</summary>
    public ICarrierLoadSource CarrierLoad { get; init; }

    /// <summary>The local player's slot and id (player-character 020).</summary>
    public ILocalPlayer LocalPlayer { get; init; }
}
