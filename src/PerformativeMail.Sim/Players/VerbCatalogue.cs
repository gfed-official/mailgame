namespace PerformativeMail.Sim.Players;

/// <summary>
/// The default mode, hold base and priority of each <see cref="Verb"/> (Player Character GDD PCM-P10-2). Owners start from these
/// through <see cref="AnchorDef.For"/> and may override them per anchor. Pure, allocation-free, stateless: switch expressions over
/// compile-time constants.
/// <para>
/// PROPOSED: the Rebuild and Deconstruct hold base (400 ms) is not stated by the GDD; it equals the Deliver hold and the default
/// <c>holdDefaultMs</c> until the Building epic decides.
/// </para>
/// </summary>
/// <example>
/// <code>
/// InteractMode mode = VerbCatalogue.DefaultMode(Verb.Catch);    // Channel
/// byte priority = VerbCatalogue.DefaultPriority(Verb.Deliver);  // 2
/// </code>
/// </example>
public static class VerbCatalogue
{
    /// <summary>The hold base, ms, of every Hold verb (Deliver per the GDD; Rebuild and Deconstruct PROPOSED).</summary>
    public const int DefaultHoldMs = 400;

    /// <summary>Whether <paramref name="verb"/> is one of the 13 catalogue members.</summary>
    /// <param name="verb">The verb (any byte value).</param>
    /// <returns>True for 1 to 13.</returns>
    /// <example><code>bool ok = VerbCatalogue.IsDefined((Verb)14);   // false</code></example>
    public static bool IsDefined(Verb verb) => verb >= Verb.Deliver && verb <= Verb.Deconstruct;

    /// <summary>The default start mode of <paramref name="verb"/>.</summary>
    /// <param name="verb">The verb.</param>
    /// <returns>The mode; <c>default</c> (0, invalid) for an undefined verb.</returns>
    /// <example><code>InteractMode m = VerbCatalogue.DefaultMode(Verb.Open);   // Tap</code></example>
    public static InteractMode DefaultMode(Verb verb) => verb switch
    {
        Verb.Deliver or Verb.Rebuild or Verb.Deconstruct => InteractMode.Hold,
        Verb.Open or Verb.PickUp or Verb.Mount or Verb.Operate or Verb.Configure or Verb.Shop or Verb.Announce or Verb.Dismount => InteractMode.Tap,
        Verb.Catch or Verb.Push => InteractMode.Channel,
        _ => default,
    };

    /// <summary>The default base hold length of <paramref name="verb"/>, ms.</summary>
    /// <param name="verb">The verb.</param>
    /// <returns><see cref="DefaultHoldMs"/> for Deliver, Rebuild and Deconstruct; 0 otherwise.</returns>
    /// <example><code>int ms = VerbCatalogue.DefaultHoldBaseMs(Verb.Deliver);   // 400</code></example>
    public static int DefaultHoldBaseMs(Verb verb) => verb switch
    {
        Verb.Deliver or Verb.Rebuild or Verb.Deconstruct => DefaultHoldMs,
        _ => 0,
    };

    /// <summary>The default focus priority (0 to 3) of <paramref name="verb"/>.</summary>
    /// <param name="verb">The verb.</param>
    /// <returns>2 for Deliver; 1 for Open, PickUp and Mount; 0 for every other verb.</returns>
    /// <example><code>byte p = VerbCatalogue.DefaultPriority(Verb.Mount);   // 1</code></example>
    public static byte DefaultPriority(Verb verb) => verb switch
    {
        Verb.Deliver => 2,
        Verb.Open or Verb.PickUp or Verb.Mount => 1,
        _ => 0,
    };
}
