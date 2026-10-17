namespace PerformativeMail.Sim.Content.Stats;

/// <summary>
/// The write face of the stat sheets (ADR-0010 section 7): push and remove a source by identity. Constructor-injected
/// <b>only</b> into Perks &amp; Perk Draft and Stamps &amp; Run Events (a reflection test enforces it); meta-progression never
/// pushes modifiers, and no other system receives this interface. Every successful call rebuilds the sheet before it returns
/// and raises <see cref="IStatSheet.Version"/> by exactly 1, so every read in a tick sees one sheet version. A refused call
/// changes nothing. Not thread-safe: call from the Sim tick thread (inside a phase or op) only.
/// </summary>
/// <example>
/// <code>
/// var src = new ModifierSource(SourceKind.Perk, perkIndex, stack, ModifierOwner.ForSlot(slot), maxStacks, ModifierLifetime.Run);
/// if (!writer.Push(in src, [longLegsModifier])) { /* refused: duplicate, over max stacks, invalid or pool full */ }
/// writer.Remove(in src);
/// </code>
/// </example>
public interface IStatWriter
{
    /// <summary>
    /// Adds a source and its modifiers. Refused (returns false, sheet and version unchanged) when: the owner is unowned
    /// (<c>default</c>), the kind or lifetime is not a defined member, a flag is not a defined <see cref="RuleFlag"/>,
    /// <c>StackIndex &gt;= MaxStacks</c>, a modifier cannot apply to its owner's sheet (unknown stat, wrong scope, an op the
    /// stat does not accept, a bad qualifier), the identity is already live, or the owner's fixed pool or source table
    /// has no room (this one is also counted in <c>CounterId.StatPoolFull</c>; every other refusal in
    /// <c>CounterId.StatWriteRefused</c>). A source may carry no modifiers (a flag-only source).
    /// </summary>
    /// <param name="src">The source identity and shape.</param>
    /// <param name="mods">The source's modifiers; the identity and lifetime of <paramref name="src"/> override the ones inside.</param>
    /// <returns>True when the source was added.</returns>
    /// <example><code>bool ok = writer.Push(in src, [mod]);</code></example>
    bool Push(in ModifierSource src, ReadOnlySpan<Modifier> mods);

    /// <summary>
    /// Removes a live source by identity <c>(Kind, Id, StackIndex, Owner)</c>; the other fields of <paramref name="src"/> are
    /// ignored. Returns false (sheet and version unchanged, counted in <c>CounterId.StatWriteRefused</c>) when no such source is live.
    /// </summary>
    /// <param name="src">The identity of the source to remove.</param>
    /// <returns>True when a source was removed.</returns>
    /// <example><code>bool removed = writer.Remove(in src);</code></example>
    bool Remove(in ModifierSource src);
}
