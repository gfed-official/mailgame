namespace PerformativeMail.Sim.Players;

/// <summary>
/// What the carrier's body is doing, for the input router's <c>body</c> dimension (ADR-0012 section 2) and the interaction gate's
/// context filter (ADR-0014 section 2; PC GDD P14). Exactly the four ADR-0012 values, in the same numeric order as the router's
/// body layer; no value is ever added here. Mounted and Dead are declared but unused at M-1a (no vehicles, no knockdown).
/// </summary>
/// <example><code>BodyContext b = BodyContexts.From(LifeState.Alive, LocoState.Grounded, buildMode: false);   // OnFoot</code></example>
public enum BodyContext : byte
{
    /// <summary>Alive and on foot (Grid Inventory's World, Panel and Holding contexts).</summary>
    OnFoot = 0,

    /// <summary>Alive in build mode: World with only <see cref="AnchorFlags.BuildModeOnly"/> anchors focusable (P11).</summary>
    Build = 1,

    /// <summary>Alive in a vehicle seat or on a turret (M1).</summary>
    Mounted = 2,

    /// <summary>No carrier verbs and no interaction candidates: knocked down, and also joining, dead-away, frozen or despawned.</summary>
    Dead = 3,
}

/// <summary>
/// The <see cref="LifeState"/> to <see cref="BodyContext"/> mapping (player-character story 013; ADR-0014 section 6 states). Only
/// <see cref="LifeState.Alive"/> has a body mode; every other state (Joining, Dead, DeadAway, Frozen, Despawned) is
/// <see cref="BodyContext.Dead"/>, the conservative "no gameplay verbs" mapping. For an Alive carrier, a
/// <see cref="LocoState.Mounted"/> locomotion mode wins over build mode (a seated carrier cannot build), then build mode, then on foot.
/// Pure, allocation free.
/// </summary>
/// <example><code>BodyContext b = BodyContexts.From(LifeState.Frozen, LocoState.Grounded, buildMode: false);   // Dead</code></example>
public static class BodyContexts
{
    /// <summary>Maps a life state and the carrier's mode to a body context.</summary>
    /// <param name="life">The host-owned life state.</param>
    /// <param name="loco">The carrier's locomotion mode (<see cref="LocoState.Mounted"/> selects Mounted).</param>
    /// <param name="buildMode">
    /// True when the player's build mode is on. Build mode is not part of <see cref="CarrierState"/> or <see cref="PlayerRecord"/>;
    /// the caller (story 020's status publisher; the Building system at M1) supplies it, and it is false at M-1a.
    /// </param>
    /// <returns>The body context.</returns>
    /// <example><code>BodyContext b = BodyContexts.From(LifeState.Alive, LocoState.Swimming, buildMode: true);   // Build</code></example>
    public static BodyContext From(LifeState life, LocoState loco, bool buildMode)
    {
        if (life != LifeState.Alive)
        {
            return BodyContext.Dead;
        }

        if (loco == LocoState.Mounted)
        {
            return BodyContext.Mounted;
        }

        return buildMode ? BodyContext.Build : BodyContext.OnFoot;
    }
}
