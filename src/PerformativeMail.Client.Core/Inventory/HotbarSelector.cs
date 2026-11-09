using PerformativeMail.Client.Core.Ui;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Ops;

namespace PerformativeMail.Client.Core.Inventory;

/// <summary>
/// The hotbar selection's sender (WP-F): selects the slot in the own-panel mirror at once (what the aim and deliver previews and the
/// hotbar strip read) and tells the host with <c>HotbarSelect</c> (the throw check compares the host's selected slot). The panel stays
/// closed at M-1a, so <see cref="QuickMove"/> sends nothing. Allocation-free.
/// </summary>
/// <param name="mirror">The own-panel mirror holding the local selection.</param>
/// <param name="requests">The request port.</param>
/// <example><code>IInventoryActions actions = new HotbarSelector(mirror, requests); actions.SelectHotbar(1);</code></example>
public sealed class HotbarSelector(OwnPanelMirror mirror, IRequestSink requests) : IInventoryActions
{
    private readonly OwnPanelMirror _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
    private readonly IRequestSink _requests = requests ?? throw new ArgumentNullException(nameof(requests));

    /// <inheritdoc />
    public bool SelectHotbar(int slot)
    {
        if (!_mirror.Select(slot))
        {
            return false;
        }

        var op = new Op { Kind = PlayerOpKinds.HotbarSelect };
        op.SetPayload(new HotbarSelectPayload { Index = (uint)slot });
        _ = _requests.TrySend(in op);
        return true;
    }

    /// <inheritdoc />
    public bool QuickMove(uint fromContainer, uint entryId) => false;
}
