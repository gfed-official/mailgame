using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The host state the <c>ThrowRelease</c> validation chain reads (ADR-0015 section 1; throwing-catching story 009), all through
/// read-only ports of the owning systems: Grid Inventory's <see cref="IInventoryApi.CheckDetachForThrow"/> (checks 1 and 2), the
/// selected hotbar slot (check 2), Players' life state, swimming and mounted reads (checks 3 to 6), the slot's <c>Tat</c> (check 7 and the
/// plan's <c>tat'</c>), claim-and-corroborate (check 9, with <c>claimToleranceMm</c> from the Players settings record) and the flight table
/// (check 10). <see cref="Counters"/> takes the diagnostics only (<see cref="CounterId.ThrowingReleaseRejections"/>,
/// <see cref="CounterId.ThrowingFlightCapRefusals"/>). Built per call by the handler (story 010); stack-only.
/// </summary>
internal readonly ref struct ThrowHostFacts
{
    /// <summary>Grid Inventory's API (only <see cref="IInventoryApi.CheckDetachForThrow"/> is called).</summary>
    public readonly IInventoryApi Inventory;

    /// <summary>Players' API (life state, swimming, mounted).</summary>
    public readonly IPlayersApi Players;

    /// <summary>The slot table: the actor's slot.</summary>
    public readonly ISlotView Slots;

    /// <summary>The selected hotbar slot.</summary>
    public readonly IHotbarSelection Hotbar;

    /// <summary>Claim-and-corroborate.</summary>
    public readonly IClaimCorroborator Corroborator;

    /// <summary><c>claimToleranceMm</c> (Players settings record, <c>player.claimToleranceMm</c>).</summary>
    public readonly int ClaimToleranceMm;

    /// <summary>The live flights (check 10: <see cref="FlightTable.TryAppend"/> refuses exactly when this check does).</summary>
    public readonly FlightTable Flights;

    /// <summary>The per-slot leaky-bucket clocks <c>Tat</c>, indexed by slot value.</summary>
    public readonly ReadOnlySpan<uint> Tat;

    /// <summary>The world's counters (diagnostics, not dumped).</summary>
    public readonly SimCounters Counters;

    /// <summary>Creates the facts.</summary>
    /// <param name="inventory">Grid Inventory's API.</param>
    /// <param name="players">Players' API.</param>
    /// <param name="slots">The slot table.</param>
    /// <param name="hotbar">The selected hotbar slot.</param>
    /// <param name="corroborator">Claim-and-corroborate.</param>
    /// <param name="claimToleranceMm">The claim tolerance, mm.</param>
    /// <param name="flights">The live flights.</param>
    /// <param name="tat">The per-slot <c>Tat</c> (length <see cref="Primitives.PlayerSlot.Count"/>).</param>
    /// <param name="counters">The world's counters.</param>
    public ThrowHostFacts(
        IInventoryApi inventory,
        IPlayersApi players,
        ISlotView slots,
        IHotbarSelection hotbar,
        IClaimCorroborator corroborator,
        int claimToleranceMm,
        FlightTable flights,
        ReadOnlySpan<uint> tat,
        SimCounters counters)
    {
        Inventory = inventory;
        Players = players;
        Slots = slots;
        Hotbar = hotbar;
        Corroborator = corroborator;
        ClaimToleranceMm = claimToleranceMm;
        Flights = flights;
        Tat = tat;
        Counters = counters;
    }
}
