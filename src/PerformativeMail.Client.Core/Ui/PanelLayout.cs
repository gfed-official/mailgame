namespace PerformativeMail.Client.Core.Ui;

/// <summary>A pixel rectangle.</summary>
/// <param name="X">Left.</param>
/// <param name="Y">Top.</param>
/// <param name="W">Width.</param>
/// <param name="H">Height.</param>
public readonly record struct PxRect(int X, int Y, int W, int H)
{
    /// <summary>True when the point is inside.</summary>
    /// <param name="px">Point x.</param>
    /// <param name="py">Point y.</param>
    /// <returns>True when inside.</returns>
    public bool Contains(int px, int py) => px >= X && py >= Y && px < X + W && py < Y + H;
}

/// <summary>
/// Pure layout and hit testing for the one panel <c>Control</c> (grid-inventory 023): hotbar strip bottom centre, own grid above it on the
/// left, Intake grid on the right. Greybox: fixed cell size, no scaling.
/// </summary>
/// <example><code>var l = new PanelLayout(1920, 1080); int slot = l.HotbarSlotAt(px, py);</code></example>
public sealed class PanelLayout
{
    /// <summary>Hotbar slots.</summary>
    public const int HotbarSlots = 8;

    /// <summary>Cell edge in pixels.</summary>
    public const int Cell = 40;

    private const int Margin = 24;

    private readonly int _viewW;
    private readonly int _viewH;

    /// <summary>Creates the layout for a viewport.</summary>
    /// <param name="viewW">Viewport width.</param>
    /// <param name="viewH">Viewport height.</param>
    public PanelLayout(int viewW, int viewH)
    {
        _viewW = viewW;
        _viewH = viewH;
    }

    /// <summary>The hotbar slot's rectangle.</summary>
    /// <param name="slot">0 to 7.</param>
    /// <returns>The rectangle.</returns>
    public PxRect HotbarSlot(int slot) => new(((_viewW - (HotbarSlots * Cell)) / 2) + (slot * Cell), _viewH - Margin - Cell, Cell, Cell);

    /// <summary>The own grid's bounds.</summary>
    /// <param name="w">Columns.</param>
    /// <param name="h">Rows.</param>
    /// <returns>The rectangle.</returns>
    public PxRect OwnBounds(int w, int h) => new(Margin, _viewH - (2 * Margin) - Cell - (h * Cell), w * Cell, h * Cell);

    /// <summary>The Intake grid's bounds.</summary>
    /// <param name="w">Columns.</param>
    /// <param name="h">Rows.</param>
    /// <returns>The rectangle.</returns>
    public PxRect IntakeBounds(int w, int h) => new(_viewW - Margin - (w * Cell), Margin, w * Cell, h * Cell);

    /// <summary>An entry's rectangle inside a grid.</summary>
    /// <param name="bounds">The grid's bounds.</param>
    /// <param name="e">The entry.</param>
    /// <returns>The rectangle.</returns>
    public static PxRect EntryRect(PxRect bounds, in PanelGridEntry e) => new(bounds.X + (e.X * Cell), bounds.Y + (e.Y * Cell), e.W * Cell, e.H * Cell);

    /// <summary>The hotbar slot under a point.</summary>
    /// <param name="px">Point x.</param>
    /// <param name="py">Point y.</param>
    /// <returns>0 to 7, else -1.</returns>
    public int HotbarSlotAt(int px, int py)
    {
        for (int i = 0; i < HotbarSlots; i++)
        {
            if (HotbarSlot(i).Contains(px, py))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The entry under a point in a grid.</summary>
    /// <param name="grid">The grid.</param>
    /// <param name="bounds">Its bounds.</param>
    /// <param name="px">Point x.</param>
    /// <param name="py">Point y.</param>
    /// <returns>The entry id, 0 when the point is on no entry.</returns>
    public static uint EntryAt(IGridSource grid, PxRect bounds, int px, int py)
    {
        for (int i = 0; i < grid.EntryCount; i++)
        {
            PanelGridEntry e = grid.Entry(i);
            if (EntryRect(bounds, in e).Contains(px, py))
            {
                return e.EntryId;
            }
        }

        return 0;
    }
}
