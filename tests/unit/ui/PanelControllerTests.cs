using PerformativeMail.Client.Core.Ui;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Ui;

/// <summary>Panel interactions (grid-inventory 024, minimal): Shift quick-move from the Intake behind the range gate, and keys 1 to 8.</summary>
public class PanelControllerTests
{
    private static readonly PanelLayout _layout = new(1920, 1080);

    private sealed class Grid(uint id, int w, int h, params PanelGridEntry[] entries) : IGridSource
    {
        public uint ContainerId => id;

        public int Width => w;

        public int Height => h;

        public int EntryCount => entries.Length;

        public PanelGridEntry Entry(int i) => entries[i];
    }

    private sealed class Source : IInventoryPanelSource
    {
        public bool IsOpen { get; set; } = true;

        public int SelectedSlot { get; set; }

        public IPanelView Panel => throw new NotSupportedException();

        public IGridSource Own { get; } = new Grid(1, 8, 2);

        public IGridSource? Intake { get; set; } = new Grid(9, 20, 16, new PanelGridEntry(77, 3, 2, 2, 1, ItemCategory.Mail, 0, 1, default));
    }

    private sealed class Actions : IInventoryActions
    {
        public bool Accept { get; set; } = true;

        public List<string> Sent { get; } = [];

        public bool SelectHotbar(int slot)
        {
            Sent.Add("select " + slot);
            return Accept;
        }

        public bool QuickMove(uint fromContainer, uint entryId)
        {
            Sent.Add("qm " + fromContainer + " " + entryId);
            return Accept;
        }
    }

    private sealed class Gate(InteractionCheckKind kind) : IIntakeRangeGate
    {
        public InteractionCheckKind Check() => kind;
    }

    private static (int X, int Y) CellCentre(int cx, int cy)
    {
        PxRect b = _layout.IntakeBounds(20, 16);
        return (b.X + (cx * PanelLayout.Cell) + 5, b.Y + (cy * PanelLayout.Cell) + 5);
    }

    [Fact]
    public void ShiftClickOnIntakeEntryInRangeSendsQuickMoveFromTheIntake()
    {
        var actions = new Actions();
        var c = new PanelController(new Source(), actions, new Gate(InteractionCheckKind.Ok), _layout);
        (int x, int y) = CellCentre(4, 2);   // second cell of the 2x1 entry at (3,2)

        Assert.Equal(PanelClickResult.Sent, c.OnShiftClick(x, y));
        Assert.Equal(["qm 9 77"], actions.Sent);
    }

    [Fact]
    public void ShiftClickOutOfRangeOrOnNothingOrPanelClosedSendsNothing()
    {
        var actions = new Actions();
        var source = new Source();
        (int x, int y) = CellCentre(3, 2);
        (int ex, int ey) = CellCentre(10, 10);

        Assert.Equal(PanelClickResult.OutOfRange, new PanelController(source, actions, new Gate(InteractionCheckKind.OutOfRange), _layout).OnShiftClick(x, y));
        var open = new PanelController(source, actions, new Gate(InteractionCheckKind.Ok), _layout);
        Assert.Equal(PanelClickResult.Ignored, open.OnShiftClick(ex, ey));
        source.IsOpen = false;
        Assert.Equal(PanelClickResult.Ignored, open.OnShiftClick(x, y));
        Assert.Empty(actions.Sent);
    }

    [Fact]
    public void HotbarKeyShowsTheSlotAtOnceThenFollowsTheMirror()
    {
        var actions = new Actions();
        var source = new Source { SelectedSlot = 0 };
        var c = new PanelController(source, actions, new Gate(InteractionCheckKind.Ok), _layout);

        Assert.True(c.OnHotbarKey(3));
        Assert.Equal(2, c.SelectedSlot);       // before the mirror echoes
        source.SelectedSlot = 2;
        Assert.Equal(2, c.SelectedSlot);
        source.SelectedSlot = 5;               // a later change from elsewhere wins
        Assert.Equal(5, c.SelectedSlot);
        Assert.Equal(["select 2"], actions.Sent);
    }

    [Fact]
    public void HotbarKeyRefusedByTheRequestPortLeavesTheSelection()
    {
        var actions = new Actions { Accept = false };
        var c = new PanelController(new Source { SelectedSlot = 1 }, actions, new Gate(InteractionCheckKind.Ok), _layout);

        Assert.False(c.OnHotbarKey(4));
        Assert.Equal(1, c.SelectedSlot);
        Assert.False(c.OnHotbarKey(9));
    }
}
