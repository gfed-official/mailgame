namespace PerformativeMail.Sim.Players;

/// <summary>
/// The closed v1 interaction verb catalogue (Player Character GDD P10; ADR-0014 section 1; TR-movement-011). Members are numbered
/// from 1 in the GDD order, 0 is never a valid registered value, and a value is never renumbered, reordered or reused. There is
/// deliberately NO revive verb (PCM-P10-1): a numeric value outside 1 to 13 is rejected at registration. Data spellings are in
/// <see cref="VerbSpelling"/>; default mode, hold base and priority are in <see cref="VerbCatalogue"/>.
/// </summary>
/// <example>
/// <code>
/// bool ok = VerbSpelling.TryParse("pick_up"u8, out Verb v);   // ok, v == Verb.PickUp
/// bool revive = VerbSpelling.TryParse("revive"u8, out _);     // false: no such verb
/// </code>
/// </example>
public enum Verb : byte
{
    /// <summary>Deliver mail to a destination (Hold, priority 2). Data spelling <c>deliver</c>.</summary>
    Deliver = 1,

    /// <summary>Open a container or pile (Tap, priority 1). Data spelling <c>open</c>.</summary>
    Open = 2,

    /// <summary>Pick an item up (Tap, priority 1). Data spelling <c>pick_up</c>.</summary>
    PickUp = 3,

    /// <summary>Mount a seat or vehicle (Tap, priority 1). Data spelling <c>mount</c>.</summary>
    Mount = 4,

    /// <summary>Operate a machine (Tap). Data spelling <c>operate</c>.</summary>
    Operate = 5,

    /// <summary>Configure a construct (Tap). Data spelling <c>configure</c>.</summary>
    Configure = 6,

    /// <summary>Shop at a stall (Tap). Data spelling <c>shop</c>.</summary>
    Shop = 7,

    /// <summary>Announce at a board (Tap). Data spelling <c>announce</c>.</summary>
    Announce = 8,

    /// <summary>Dismount a seat or vehicle (Tap). Data spelling <c>dismount</c>.</summary>
    Dismount = 9,

    /// <summary>Catch a thrown item (Channel). Data spelling <c>catch</c>.</summary>
    Catch = 10,

    /// <summary>Push an object (Channel). Data spelling <c>push</c>.</summary>
    Push = 11,

    /// <summary>Rebuild a construct (Hold). Data spelling <c>rebuild</c>.</summary>
    Rebuild = 12,

    /// <summary>Deconstruct a construct (Hold). Data spelling <c>deconstruct</c>.</summary>
    Deconstruct = 13,
}
