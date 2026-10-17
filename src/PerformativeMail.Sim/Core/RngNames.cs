using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// The compile-time registry of RNG streams (ADR-0005 section 2, v1 table). The only legal source of a
/// <see cref="StreamName"/>: names are never built from runtime strings or player text. Adding a stream is an
/// amendment to the ADR table plus one field here (appended, so existing ordinals do not move); it never
/// changes <see cref="RngDerive"/>. Each <c>Fnv</c> is computed once at type initialisation from a
/// <c>"..."u8</c> literal. Fields are immutable values, so they are allowed static state.
/// </summary>
/// <example>
/// <code>
/// for (int i = 0; i &lt; RngNames.Count; i++)
/// {
///     StreamName n = RngNames.ByOrdinal(i);   // declaration order, as the state dump walks them
/// }
/// </code>
/// </example>
public static class RngNames
{
    private const ushort OrdWorldgenTerrain = 0;
    private const ushort OrdMail = 1;
    private const ushort OrdMailSpecial = 2;
    private const ushort OrdWaves = 3;
    private const ushort OrdMega = 4;
    private const ushort OrdDrops = 5;
    private const ushort OrdPerks = 6;
    private const ushort OrdShop = 7;
    private const ushort OrdEvents = 8;

    /// <summary>Number of registered names (v1: 9).</summary>
    public const int Count = 9;

    /// <summary><c>worldgen.terrain</c>, index 0, owned by World Generation. Other <c>worldgen.*</c> stages are appended by the world-generation epic.</summary>
    public static readonly StreamName WorldgenTerrain = Make(OrdWorldgenTerrain, SystemId.WorldGeneration, 0, 0);

    /// <summary><c>mail</c>, index 1 to 5 (shift), owned by Mail &amp; Destinations.</summary>
    public static readonly StreamName Mail = Make(OrdMail, SystemId.MailDestinations, 1, 5);

    /// <summary><c>mail.special</c>, index 1 to 5 (shift), owned by Mail &amp; Destinations.</summary>
    public static readonly StreamName MailSpecial = Make(OrdMailSpecial, SystemId.MailDestinations, 1, 5);

    /// <summary><c>waves</c>, index 0, owned by Enemy AI &amp; Waves.</summary>
    public static readonly StreamName Waves = Make(OrdWaves, SystemId.EnemyAiWaves, 0, 0);

    /// <summary><c>mega</c>, index 0, owned by Enemy AI &amp; Waves.</summary>
    public static readonly StreamName Mega = Make(OrdMega, SystemId.EnemyAiWaves, 0, 0);

    /// <summary><c>drops</c>, index 0. ADR-0005 lists "Health &amp; Damage / Enemy AI"; registered to Health &amp; Damage (first listed), pending an ADR erratum.</summary>
    public static readonly StreamName Drops = Make(OrdDrops, SystemId.HealthDamage, 0, 0);

    /// <summary><c>perks</c>, index 0, owned by Perks &amp; Perk Draft.</summary>
    public static readonly StreamName Perks = Make(OrdPerks, SystemId.Perks, 0, 0);

    /// <summary><c>shop</c>, index 0, owned by Economy, Wallet &amp; Shop.</summary>
    public static readonly StreamName Shop = Make(OrdShop, SystemId.Economy, 0, 0);

    /// <summary><c>events</c>, index 1 to 5 (shift), owned by Stamps &amp; Run Events.</summary>
    public static readonly StreamName Events = Make(OrdEvents, SystemId.StampsRunEvents, 1, 5);

    /// <summary>Returns the name at <paramref name="ordinal"/> in declaration order.</summary>
    /// <param name="ordinal">0 to <see cref="Count"/> - 1.</param>
    /// <returns>The name; <c>default</c> (not registered) for an ordinal out of range.</returns>
    public static StreamName ByOrdinal(int ordinal) => ordinal switch
    {
        OrdWorldgenTerrain => WorldgenTerrain,
        OrdMail => Mail,
        OrdMailSpecial => MailSpecial,
        OrdWaves => Waves,
        OrdMega => Mega,
        OrdDrops => Drops,
        OrdPerks => Perks,
        OrdShop => Shop,
        OrdEvents => Events,
        _ => default,
    };

    /// <summary>True when <paramref name="name"/> is one of the registered names (rejects <c>default</c>).</summary>
    /// <param name="name">The name to check.</param>
    /// <returns><c>true</c> for a registered name.</returns>
    public static bool IsRegistered(in StreamName name)
    {
        StreamName registered = ByOrdinal(name.Ordinal);
        return registered.Fnv != 0 && registered.Fnv == name.Fnv;
    }

    /// <summary>The single source of each name's UTF-8 literal. Allocation-free.</summary>
    internal static ReadOnlySpan<byte> NameOf(ushort ordinal) => ordinal switch
    {
        OrdWorldgenTerrain => "worldgen.terrain"u8,
        OrdMail => "mail"u8,
        OrdMailSpecial => "mail.special"u8,
        OrdWaves => "waves"u8,
        OrdMega => "mega"u8,
        OrdDrops => "drops"u8,
        OrdPerks => "perks"u8,
        OrdShop => "shop"u8,
        OrdEvents => "events"u8,
        _ => default,
    };

    private static StreamName Make(ushort ordinal, SystemId owner, uint minIndex, uint maxIndex)
        => new(ordinal, Fnv1a64Hash.Hash(NameOf(ordinal)), owner, minIndex, maxIndex);
}
