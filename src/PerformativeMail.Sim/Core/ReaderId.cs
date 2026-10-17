namespace PerformativeMail.Sim.Core;

/// <summary>
/// Identity of one pull reader (ADR-0008 section 4). A system declares its id as a constant and binds it with
/// <see cref="SimWorldBuilder.BindEventReader"/>; value 0 is <see cref="None"/> and is never bound, so a <c>default</c>
/// cursor is rejected by <see cref="EventLog.Read"/> (asserts under <c>SIM_ASSERT</c>, returns false otherwise). At most <see cref="SimCoreSettings.MaxEventReaders"/> (8) readers exist per world.
/// </summary>
/// <param name="Value">The id; 1 to 255 for a real reader.</param>
/// <example>
/// <code>
/// static readonly ReaderId PlayerCharacter = new(1);
/// builder.BindEventReader(PlayerCharacter, new[] { EventKind.OpReply });
/// </code>
/// </example>
public readonly record struct ReaderId(byte Value)
{
    /// <summary>The invalid id (0): never bound.</summary>
    public static ReaderId None => default;
}
