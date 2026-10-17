namespace PerformativeMail.Sim.Core;

/// <summary>
/// Composition identity of a Sim system (ADR-0003 <c>SimSystemAttribute(SystemId)</c>). Defined in
/// <c>Core</c> because <see cref="StreamName.Owner"/> needs it and Core must not depend on Composition.
/// Values are explicit and append-only: never renumber, reuse or reorder one. The composition story
/// (Story 025) appended the remaining Sim systems (9 to 18); their dependencies are in
/// <c>Composition.SystemGraph</c>.
/// </summary>
/// <example>
/// <code>
/// IRngStreams view = registry.ViewFor(SystemId.MailDestinations);
/// RngStream mail = view.Create(in RngNames.Mail, 1);
/// </code>
/// </example>
public enum SystemId : byte
{
    /// <summary>No system. Owns no stream; the view for it exposes nothing.</summary>
    None = 0,

    /// <summary>World Generation (owns the <c>worldgen.*</c> streams).</summary>
    WorldGeneration = 1,

    /// <summary>Mail &amp; Destinations (owns <c>mail</c> and <c>mail.special</c>).</summary>
    MailDestinations = 2,

    /// <summary>Enemy AI &amp; Waves (owns <c>waves</c> and <c>mega</c>).</summary>
    EnemyAiWaves = 3,

    /// <summary>Perks &amp; Perk Draft (owns <c>perks</c>).</summary>
    Perks = 4,

    /// <summary>Economy, Wallet &amp; Shop (owns <c>shop</c>).</summary>
    Economy = 5,

    /// <summary>Stamps &amp; Run Events (owns <c>events</c>).</summary>
    StampsRunEvents = 6,

    /// <summary>Health &amp; Damage (owns <c>drops</c>; ADR-0005 lists "Health &amp; Damage / Enemy AI", first listed wins).</summary>
    HealthDamage = 7,

    /// <summary>Throwing &amp; Catching. Has no RNG stream (<c>throwNoDice</c>); its view exposes nothing.</summary>
    Throwing = 8,

    /// <summary>Grid Inventory &amp; Containers (incl. <c>WorldItem</c>). No RNG stream.</summary>
    GridInventory = 9,

    /// <summary>Player Character &amp; Movement (incl. the interaction framework). No RNG stream.</summary>
    PlayerCharacter = 10,

    /// <summary>Manual Delivery. No RNG stream.</summary>
    ManualDelivery = 11,

    /// <summary>Run Structure (the one orchestrator; the only holder of <see cref="IRunControl"/>). No RNG stream.</summary>
    RunStructure = 12,

    /// <summary>Building System. No RNG stream.</summary>
    Building = 13,

    /// <summary>Belt Logistics (belts, splitter/merger, Address Sorter, inserters, chests, depots). No RNG stream.</summary>
    BeltLogistics = 14,

    /// <summary>Vehicles &amp; NPC Drivers. No RNG stream.</summary>
    Vehicles = 15,

    /// <summary>Player Combat. No RNG stream.</summary>
    PlayerCombat = 16,

    /// <summary>Defenses &amp; Construct Damage. No RNG stream.</summary>
    Defenses = 17,

    /// <summary>Resource Harvesting. No RNG stream.</summary>
    ResourceHarvesting = 18,
}
