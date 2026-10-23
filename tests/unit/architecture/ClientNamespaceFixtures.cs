using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Tests.Unit.Architecture.Client;

/// <summary>Seen-failing fixture for the SC-ARCH-1 <c>.Client</c> rule: holds a gameplay type (allowed) and names <see cref="SimWorld"/> in a property and a body.</summary>
internal sealed class SystemClientStepWorldFixture
{
    private readonly AnchorRegistry? _anchors = null;

    public SimWorld? World => null;

    public bool HasAnchors => _anchors is not null;

    public bool IsWorld(object candidate) => candidate is SimWorld;
}
