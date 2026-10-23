namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// The local interaction's state (ADR-0014 section 7, <c>InteractionSnapshot.InteractState</c>). Owned by player-character story
/// 016 (the press writes <see cref="Holding"/> or <see cref="Channel"/>); story 017 advances and ends it, story 018 publishes it.
/// <see cref="ToggleArmed"/> is declared but never entered here: the router translates Toggle into logical press and release
/// (ADR-0012). Explicit, append-only values.
/// </summary>
/// <example><code>bool busy = state is InteractState.Holding or InteractState.Channel;</code></example>
public enum InteractState : byte
{
    /// <summary>No interaction in progress.</summary>
    Idle = 0,

    /// <summary>A Hold verb is latched and counting.</summary>
    Holding = 1,

    /// <summary>A channel is held: Catch, catch-armed, or a Channel verb.</summary>
    Channel = 2,

    /// <summary>Toggle mode's armed state (never entered by the Sim; see the type remarks).</summary>
    ToggleArmed = 3,

    /// <summary>A hold completed and sent its request; it does not repeat (G6).</summary>
    Complete = 4,
}
