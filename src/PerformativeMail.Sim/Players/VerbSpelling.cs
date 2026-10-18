namespace PerformativeMail.Sim.Players;

/// <summary>
/// The snake_case data spelling table for <see cref="Verb"/> (Player Character GDD P10). Hand-written and compile-time: no
/// reflection, no <c>Enum.Parse</c>, no static state. Lookup compares UTF-8 bytes ordinally (no numeric parse, no
/// case-insensitive fallback). Every member has exactly one spelling and every spelling maps back to its member (checked by
/// <c>ContentEnumTests</c>). Stateless, so safe to call from any thread.
/// </summary>
/// <example>
/// <code>
/// if (VerbSpelling.TryParse("deconstruct"u8, out Verb v)) { /* v == Verb.Deconstruct */ }
/// ReadOnlySpan&lt;byte&gt; back = VerbSpelling.ToSpelling(Verb.PickUp);   // "pick_up"u8
/// </code>
/// </example>
public static class VerbSpelling
{
    /// <summary>Looks up the member whose data spelling equals <paramref name="utf8"/> byte for byte.</summary>
    /// <param name="utf8">The spelling as UTF-8 bytes (no quotes, no terminator).</param>
    /// <param name="value">The member, or <c>default</c> (0, never a valid verb) when not found.</param>
    /// <returns><c>true</c> when <paramref name="utf8"/> is exactly one of the spellings; <c>false</c> otherwise (so <c>revive</c> is not found).</returns>
    /// <example><code>bool ok = VerbSpelling.TryParse("open"u8, out Verb v);   // true, Verb.Open</code></example>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out Verb value)
    {
        if (utf8.SequenceEqual("deliver"u8))
        {
            value = Verb.Deliver;
            return true;
        }

        if (utf8.SequenceEqual("open"u8))
        {
            value = Verb.Open;
            return true;
        }

        if (utf8.SequenceEqual("pick_up"u8))
        {
            value = Verb.PickUp;
            return true;
        }

        if (utf8.SequenceEqual("mount"u8))
        {
            value = Verb.Mount;
            return true;
        }

        if (utf8.SequenceEqual("operate"u8))
        {
            value = Verb.Operate;
            return true;
        }

        if (utf8.SequenceEqual("configure"u8))
        {
            value = Verb.Configure;
            return true;
        }

        if (utf8.SequenceEqual("shop"u8))
        {
            value = Verb.Shop;
            return true;
        }

        if (utf8.SequenceEqual("announce"u8))
        {
            value = Verb.Announce;
            return true;
        }

        if (utf8.SequenceEqual("dismount"u8))
        {
            value = Verb.Dismount;
            return true;
        }

        if (utf8.SequenceEqual("catch"u8))
        {
            value = Verb.Catch;
            return true;
        }

        if (utf8.SequenceEqual("push"u8))
        {
            value = Verb.Push;
            return true;
        }

        if (utf8.SequenceEqual("rebuild"u8))
        {
            value = Verb.Rebuild;
            return true;
        }

        if (utf8.SequenceEqual("deconstruct"u8))
        {
            value = Verb.Deconstruct;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the data spelling of a member.</summary>
    /// <param name="value">The member.</param>
    /// <returns>The UTF-8 spelling, or an empty span when <paramref name="value"/> is not a defined member.</returns>
    /// <example><code>ReadOnlySpan&lt;byte&gt; s = VerbSpelling.ToSpelling(Verb.Catch);   // "catch"u8</code></example>
    public static ReadOnlySpan<byte> ToSpelling(Verb value) => value switch
    {
        Verb.Deliver => "deliver"u8,
        Verb.Open => "open"u8,
        Verb.PickUp => "pick_up"u8,
        Verb.Mount => "mount"u8,
        Verb.Operate => "operate"u8,
        Verb.Configure => "configure"u8,
        Verb.Shop => "shop"u8,
        Verb.Announce => "announce"u8,
        Verb.Dismount => "dismount"u8,
        Verb.Catch => "catch"u8,
        Verb.Push => "push"u8,
        Verb.Rebuild => "rebuild"u8,
        Verb.Deconstruct => "deconstruct"u8,
        _ => default,
    };
}
