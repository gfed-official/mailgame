using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Stats;

/// <summary>
/// The read face of the stat sheets (ADR-0010 section 7): one team sheet plus one personal sheet per
/// <see cref="PlayerSlot"/>, answered through one interface. Injectable into any system that reads a stat. A sheet is a
/// derived cache of the source list (never replicated, never mutated by a read): <see cref="Read"/> never walks
/// modifiers and allocates 0 B. Implementations built by <see cref="StatSheet.Build"/> are immutable and safe to read
/// from any thread. Rule flags are read with <see cref="Has"/>.
/// </summary>
/// <example>
/// <code>
/// int stack = sheet.Read(Stat.MailMaxStack, qualifier: letterIndex.Value);
/// int speedAdd = sheet.Read(Stat.MoveSpeedAddPm, slot: playerSlot, transientAdd: sprintBonusPm);
/// </code>
/// </example>
public interface IStatSheet
{
    /// <summary>
    /// The resolved value of one cell (F1). With <paramref name="transientAdd"/> 0 this is one array load of the cached
    /// value; with a transient it is one Int128 F1 evaluation from the cached components. Invalid arguments trip
    /// <c>SIM_ASSERT</c> and otherwise return a fixed value: an unknown stat returns 0; a qualified stat with a qualifier
    /// that is 0xFFFF or out of range returns <c>clamp(min)</c>; a non-null slot that is <see cref="PlayerSlot.None"/>
    /// reads the team-only value; a negative transient, or a transient on a stat that does not accept Add, is treated as 0. A qualifier other than <see cref="StatSheet.NoQualifier"/> on an unqualified stat asserts and is ignored.
    /// </summary>
    /// <param name="stat">The stat.</param>
    /// <param name="qualifier">The qualifier def's ordinal for a qualified stat; <see cref="StatSheet.NoQualifier"/> (the default) for an unqualified stat. A qualifier on an unqualified stat trips <c>SIM_ASSERT</c> and is ignored in Release. A wrong-kind id (a mail-kind ordinal on a container stat) is detectable only when it is out of range for that kind's table.</param>
    /// <param name="slot">A personal or <c>both</c> stat reads that slot's personal sheet (team modifiers merged for <c>both</c>); null reads the team sheet. A team stat ignores the slot.</param>
    /// <param name="transientAdd">The caller's live timed Add (CD18), 0 or more; 0 if none.</param>
    /// <returns>The clamped value.</returns>
    /// <example>
    /// <code>
    /// int rows = sheet.Read(Stat.InventoryRows, containerIndex.Value, slot);
    /// </code>
    /// </example>
    int Read(Stat stat, ushort qualifier = StatSheet.NoQualifier, PlayerSlot? slot = null, int transientAdd = 0);

    /// <summary>
    /// The same cell as <see cref="Read"/> with the pre-clamp value, the clamp that applied and the contributing sources
    /// (CD19). A UI/debug path, not per tick, but it allocates nothing. Invalid arguments are handled exactly as in
    /// <see cref="Read"/>, with an empty source list.
    /// </summary>
    /// <param name="stat">The stat.</param>
    /// <param name="qualifier">As <see cref="Read"/>.</param>
    /// <param name="slot">As <see cref="Read"/>.</param>
    /// <param name="transientAdd">As <see cref="Read"/>.</param>
    /// <returns>The explanation.</returns>
    /// <example>
    /// <code>
    /// StatExplain e = sheet.Explain(Stat.MoveSpeedAddPm, slot: slot, transientAdd: 300);
    /// </code>
    /// </example>
    StatExplain Explain(Stat stat, ushort qualifier = StatSheet.NoQualifier, PlayerSlot? slot = null, int transientAdd = 0);

    /// <summary>
    /// True while at least one live source holds <paramref name="flag"/> (TR-content-018): the sheet keeps a count of live
    /// sources per flag and the flag is on while the count is above 0. With a slot, the team's sources and that slot's
    /// sources both count; null reads the team sheet. An undefined flag is false. A non-null slot that is
    /// <see cref="PlayerSlot.None"/> trips <c>SIM_ASSERT</c> and reads the team sheet. Allocates nothing.
    /// </summary>
    /// <param name="flag">The rule flag.</param>
    /// <param name="slot">The player slot, or null for the team sheet.</param>
    /// <returns>True when the flag is on.</returns>
    /// <example>
    /// <code>
    /// if (sheet.Has(RuleFlag.PriorityMail, playerSlot)) { /* ... */ }
    /// </code>
    /// </example>
    bool Has(RuleFlag flag, PlayerSlot? slot = null);

    /// <summary>
    /// How many successful source adds and removes the owning writer has applied: 0 for a sheet from
    /// <see cref="StatSheet.Build"/> and for a world's sheet before any push. It rises by exactly 1 per successful add or
    /// remove and never changes on a read or a refused write, so every read in a tick sees one version.
    /// </summary>
    /// <example><code>uint before = sheet.Version;</code></example>
    uint Version { get; }
}
