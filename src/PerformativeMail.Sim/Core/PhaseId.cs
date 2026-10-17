namespace PerformativeMail.Sim.Core;

/// <summary>
/// The fixed per-tick phases (ADR-0003 section 1, Sim Core R3). Exactly 14 values, run in
/// ascending order: 0 to 12 in order, then the bound <see cref="ITickEndCheck"/>s (not a phase), then
/// <see cref="EventsFlush"/>. Values are explicit and fixed; a new phase amends ADR-0003 and Sim Core R3.
/// Systems bind only phases 1 to 12: <see cref="InputsApply"/> and <see cref="EventsFlush"/> are built in.
/// </summary>
/// <example>
/// <code>
/// builder.Bind(PhaseId.MailStep, mailPhase);   // runs after AgentsStep, before EconomyFlush
/// </code>
/// </example>
public enum PhaseId : byte
{
    /// <summary>Phase 0: drains the op FIFO (built in; not bindable).</summary>
    InputsApply = 0,

    /// <summary>Phase 1: item expiry.</summary>
    ItemsExpire = 1,

    /// <summary>Phase 2: intake drain.</summary>
    IntakeDrain = 2,

    /// <summary>Phase 3: players step.</summary>
    PlayersStep = 3,

    /// <summary>Phase 4: throwing step.</summary>
    ThrowingStep = 4,

    /// <summary>Phase 5: automation step.</summary>
    AutomationStep = 5,

    /// <summary>Phase 6: vehicles step.</summary>
    VehiclesStep = 6,

    /// <summary>Phase 7: agents step.</summary>
    AgentsStep = 7,

    /// <summary>Phase 8: mail step.</summary>
    MailStep = 8,

    /// <summary>Phase 9: economy flush.</summary>
    EconomyFlush = 9,

    /// <summary>Phase 10: combat resolve.</summary>
    CombatResolve = 10,

    /// <summary>Phase 11: combat react.</summary>
    CombatReact = 11,

    /// <summary>Phase 12: run step (the only phase where a stop request is not latched).</summary>
    RunStep = 12,

    /// <summary>Phase 13: event log flush, after the tick-end checks (built in; not bindable).</summary>
    EventsFlush = 13,
}
