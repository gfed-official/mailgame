using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Ui;

/// <summary>One drawn entry of a grid: its footprint after rotation and the facts the cell shows (grid-inventory 023, ux inventory-panel cell anatomy).</summary>
/// <param name="EntryId">The entry id (raw; 0 is none).</param>
/// <param name="X">Anchor column.</param>
/// <param name="Y">Anchor row.</param>
/// <param name="W">Footprint width in cells, rotation applied.</param>
/// <param name="H">Footprint height in cells, rotation applied.</param>
/// <param name="Category">The item category (mail or an item class).</param>
/// <param name="KindKey">Mail kind index or item def index.</param>
/// <param name="Units">The unit count.</param>
/// <param name="Address">The address on a letter; none for an item.</param>
public readonly record struct PanelGridEntry(uint EntryId, int X, int Y, int W, int H, ItemCategory Category, ushort KindKey, byte Units, AddressId Address);

/// <summary>A readable grid container: the own inventory (8x2) or the open Intake (20x16). Wire after PR 87: adapt the client own-container mirror.</summary>
public interface IGridSource
{
    /// <summary>The container id (for the quick-move op's <c>From</c>).</summary>
    uint ContainerId { get; }

    /// <summary>Columns.</summary>
    int Width { get; }

    /// <summary>Rows.</summary>
    int Height { get; }

    /// <summary>Number of entries.</summary>
    int EntryCount { get; }

    /// <summary>Entry <paramref name="i"/>, 0 to <see cref="EntryCount"/> - 1.</summary>
    /// <param name="i">The index.</param>
    /// <returns>The entry.</returns>
    PanelGridEntry Entry(int i);
}

/// <summary>Everything the panel view reads each frame. Wire after PR 87 (own-container mirror) and after WP-A part 2 (Intake open state).</summary>
public interface IInventoryPanelSource
{
    /// <summary>True while the inventory panel is open.</summary>
    bool IsOpen { get; }

    /// <summary>The selected hotbar slot, 0 to 7, as the mirror knows it.</summary>
    int SelectedSlot { get; }

    /// <summary>The hotbar and cursor facts (slot 0 is the hands).</summary>
    IPanelView Panel { get; }

    /// <summary>The own inventory grid.</summary>
    IGridSource Own { get; }

    /// <summary>The open Intake grid, else null.</summary>
    IGridSource? Intake { get; }
}

/// <summary>The ops the panel sends. Wire after PR 87 (<c>HotbarSelect</c> op) and onto the client request port for <c>QuickMove</c>.</summary>
public interface IInventoryActions
{
    /// <summary>Sends <c>HotbarSelect{slot}</c>.</summary>
    /// <param name="slot">The slot, 0 to 7.</param>
    /// <returns>True when sent; false when the request port was busy.</returns>
    bool SelectHotbar(int slot);

    /// <summary>Sends <c>QuickMove{from, entry, count 0}</c>.</summary>
    /// <param name="fromContainer">The source container id.</param>
    /// <param name="entryId">The entry.</param>
    /// <returns>True when sent.</returns>
    bool QuickMove(uint fromContainer, uint entryId);
}

/// <summary>The range gate for the open Intake: player-character's <c>CheckInteraction</c> asked at the Intake's anchor. Advisory on the client; the host re-checks.</summary>
public interface IIntakeRangeGate
{
    /// <summary>Runs the check.</summary>
    /// <returns>The kind; <see cref="InteractionCheckKind.Ok"/> to allow.</returns>
    InteractionCheckKind Check();
}

/// <summary>Adapts <see cref="IPlayersApi.CheckInteraction"/> (loopback host) to <see cref="IIntakeRangeGate"/>. Wire after WP-A part 2.</summary>
/// <example><code>var gate = new CheckInteractionGate(players, actor, () =&gt; intakeAnchor, () =&gt; claimedFeet);</code></example>
public sealed class CheckInteractionGate : IIntakeRangeGate
{
    private readonly IPlayersApi _players;
    private readonly ActorId _actor;
    private readonly Func<AnchorRef> _anchor;
    private readonly Func<ClaimedPosition> _claim;

    /// <summary>Creates the gate.</summary>
    /// <param name="players">The players API.</param>
    /// <param name="actor">The local actor.</param>
    /// <param name="anchor">The open Intake's anchor.</param>
    /// <param name="claim">The local claimed position.</param>
    public CheckInteractionGate(IPlayersApi players, ActorId actor, Func<AnchorRef> anchor, Func<ClaimedPosition> claim)
    {
        _players = players;
        _actor = actor;
        _anchor = anchor;
        _claim = claim;
    }

    /// <inheritdoc />
    public InteractionCheckKind Check() => _players.CheckInteraction(_actor, _anchor(), _claim()).Kind;
}
