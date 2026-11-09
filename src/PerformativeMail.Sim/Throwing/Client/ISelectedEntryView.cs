using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The selected hotbar entry as the thrower's client shows it (throwing-catching story 004): the port the aim preview reads instead of the
/// own-container mirror. Throwing-defined and narrow on purpose: the client container mirror (<c>IPanelView</c>, manual-delivery story
/// 002) and the client's selected-slot state implement it when they land; tests use a double.
/// </summary>
/// <example><code>if (view.TryGet(out SelectedEntryFacts f) &amp;&amp; f.Units &gt; 0) { /* throwable */ }</code></example>
public interface ISelectedEntryView
{
    /// <summary>The selected hotbar index, 0 (the hands slot) to 7.</summary>
    byte SelectedIndex { get; }

    /// <summary>The entry in the selected slot, as displayed (confirmed plus replayed pending).</summary>
    /// <param name="facts">The entry; <c>default</c> when the slot is empty.</param>
    /// <returns>False when the selected slot holds no entry.</returns>
    bool TryGet(out SelectedEntryFacts facts);
}

/// <summary>The facts of one selected hotbar entry the aim preview needs (throwing-catching story 004). Immutable.</summary>
/// <example><code>var f = new SelectedEntryFacts(3, entry, 5, true, unit, WeightClass.Light, letter);</code></example>
public readonly struct SelectedEntryFacts
{
    /// <summary>The hotbar index of the entry.</summary>
    public readonly byte HotbarIndex;

    /// <summary>The entry id.</summary>
    public readonly EntryId Entry;

    /// <summary>The unit count (what a release sends as <c>units</c>).</summary>
    public readonly byte Units;

    /// <summary>True for a mail entry, false for any other item.</summary>
    public readonly bool IsMail;

    /// <summary>The unit facts <c>MailRules.Evaluate</c> reads (a non-mail item carries its own category).</summary>
    public readonly UnitFacts Unit;

    /// <summary>The thrown stack's weight class (selects <c>baseSpeed[class]</c>).</summary>
    public readonly WeightClass WeightClass;

    /// <summary>The item definition.</summary>
    public readonly DefIndex Item;

    /// <summary>Creates the facts.</summary>
    /// <param name="hotbarIndex">The hotbar index.</param>
    /// <param name="entry">The entry id.</param>
    /// <param name="units">The unit count.</param>
    /// <param name="isMail">True for mail.</param>
    /// <param name="unit">The unit facts.</param>
    /// <param name="weightClass">The weight class.</param>
    /// <param name="item">The item definition.</param>
    public SelectedEntryFacts(byte hotbarIndex, EntryId entry, byte units, bool isMail, UnitFacts unit, WeightClass weightClass, DefIndex item)
    {
        HotbarIndex = hotbarIndex;
        Entry = entry;
        Units = units;
        IsMail = isMail;
        Unit = unit;
        WeightClass = weightClass;
        Item = item;
    }
}
