using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Delivery;

/// <summary>
/// The one deliver scan order (ADR-0016 section 3; TR-delivery-014; Manual Delivery MD3, grid-inventory R7 <c>deliver_matching</c>):
/// cursor, then hotbar, then inventory, then backpack, row-major within each, over an <see cref="IPanelView"/>. The client preview
/// walks the displayed mirror and the host handler walks <c>IInventoryApi.HostPanelView</c>, so both see the same order. Empty
/// hotbar slots are skipped, and an entry covering several hotbar slots is yielded once. A ref-struct iterator: no allocation.
/// </summary>
/// <example>
/// <code>
/// foreach (PanelEntryFacts e in DeliverScan.Enumerate(panel)) { /* cursor → hotbar → inventory → backpack */ }
/// </code>
/// </example>
public static class DeliverScan
{
    /// <summary>The most entries one scan of an M-1a panel can yield (ADR-0016 section 7 guardrail).</summary>
    public const int MaxEntries = 26;

    /// <summary>Starts a scan of <paramref name="panel"/>.</summary>
    /// <param name="panel">The panel view.</param>
    /// <returns>The iterator, positioned before the first entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="panel"/> is null.</exception>
    public static Enumerator Enumerate(IPanelView panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        return new Enumerator(panel);
    }

    /// <summary>The scan iterator; <see cref="Section"/> and <see cref="Index"/> locate <see cref="Current"/> in the view.</summary>
    public ref struct Enumerator
    {
        private readonly IPanelView _panel;
        private PanelSection _section;
        private int _index;
        private int _count;
        private EntryId _lastHotbar;

        internal Enumerator(IPanelView panel)
        {
            _panel = panel;
            _section = PanelSection.Cursor;
            _index = -1;
            _count = panel.EntryCount(PanelSection.Cursor);
            _lastHotbar = EntryId.None;
            Current = default;
        }

        /// <summary>The current entry's facts.</summary>
        public PanelEntryFacts Current { get; private set; }

        /// <summary>The section of <see cref="Current"/>.</summary>
        public readonly PanelSection Section => _section;

        /// <summary>The index of <see cref="Current"/> within <see cref="Section"/> (the slot for the hotbar).</summary>
        public readonly int Index => _index;

        /// <summary>Returns this iterator (for <c>foreach</c>).</summary>
        /// <returns>The iterator.</returns>
        public readonly Enumerator GetEnumerator() => this;

        /// <summary>Advances to the next entry in scan order.</summary>
        /// <returns>False when the scan is complete.</returns>
        public bool MoveNext()
        {
            while (true)
            {
                _index++;
                if (_index >= _count)
                {
                    if (_section == PanelSection.Backpack)
                    {
                        Current = default;
                        return false;
                    }

                    _section++;
                    _index = -1;
                    _count = _panel.EntryCount(_section);
                    continue;
                }

                PanelEntryFacts e = _panel.Entry(_section, _index);
                if (!e.IsEntry)
                {
                    continue;
                }

                if (_section == PanelSection.Hotbar)
                {
                    // A wide entry covers adjacent slots of the one-row bar: yield it at its first slot only.
                    if (e.EntryId == _lastHotbar)
                    {
                        continue;
                    }

                    _lastHotbar = e.EntryId;
                }

                Current = e;
                return true;
            }
        }
    }
}
