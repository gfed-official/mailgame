namespace PerformativeMail.Sim.Players;

/// <summary>
/// A player's host-owned life state (PC GDD States and Transitions; ADR-0014 section 6, TR-movement-013). Values are explicit and
/// append-only: they are written to the canonical dump. At M-1a only <see cref="Joining"/> to <see cref="Alive"/> is exercised;
/// the other states exist so the data model and the dump layout are stable (timers and knockdown are M-1b).
/// </summary>
/// <example><code>LifeState s = players.GetLifeState(PlayerSlot.FromValue(0));   // Alive after a join; Despawned for an empty slot</code></example>
public enum LifeState : byte
{
    /// <summary>The record exists and is waiting for the join tick to be processed (immediately followed by <see cref="Alive"/> at M-1a).</summary>
    Joining = 0,

    /// <summary>The carrier is in play.</summary>
    Alive = 1,

    /// <summary>Knocked down; waiting for <c>ClockInTick</c> (M-1b).</summary>
    Dead = 2,

    /// <summary>Knocked down and disconnected (M-1b).</summary>
    DeadAway = 3,

    /// <summary>Disconnected ("on break"); the body waits until <c>FrozenUntilTick</c> (M-1b).</summary>
    Frozen = 4,

    /// <summary>No player in the slot (also the answer for an empty or out-of-range slot).</summary>
    Despawned = 5,
}

/// <summary>
/// The explicit life-state transition table (ADR-0003 state machines have explicit transition tables). At M-1a it holds exactly
/// one edge, <see cref="LifeState.Joining"/> to <see cref="LifeState.Alive"/>; the M-1b stories add their edges here.
/// </summary>
/// <example><code>bool ok = LifeStates.CanTransition(LifeState.Joining, LifeState.Alive);   // true</code></example>
public static class LifeStates
{
    /// <summary>True when the transition <paramref name="from"/> to <paramref name="to"/> is a legal edge of the table.</summary>
    /// <param name="from">The current state.</param>
    /// <param name="to">The requested state.</param>
    /// <returns>True when the edge exists.</returns>
    public static bool CanTransition(LifeState from, LifeState to) => from == LifeState.Joining && to == LifeState.Alive;
}
