namespace PerformativeMail.Sim.Content.Balance;

/// <summary>
/// The handles of every <c>sim.*</c> balance row, resolved once against one registry by
/// <see cref="BalanceKeys.Sim.Resolve"/>. Immutable; keep it in a <c>readonly</c> field. Each handle works only with
/// tables built from that registry's layout.
/// </summary>
/// <param name="OpQueueCapacity"><c>sim.opQueueCapacity</c>.</param>
/// <param name="SystemOpReserve"><c>sim.systemOpReserve</c>.</param>
/// <param name="MaxOpsDrainedPerTick"><c>sim.maxOpsDrainedPerTick</c>.</param>
/// <param name="EventLogCapacity"><c>sim.eventLogCapacity</c>.</param>
/// <param name="EventLogReserve"><c>sim.eventLogReserve</c>.</param>
/// <param name="MaxCatchUpTicksPerFrame"><c>sim.maxCatchUpTicksPerFrame</c>.</param>
/// <param name="CheckRingCapacity"><c>sim.checkRingCapacity</c>.</param>
/// <param name="WarmupTicks"><c>sim.warmupTicks</c>.</param>
/// <param name="ModifierPoolPerTeam"><c>sim.modifierPoolPerTeam</c>.</param>
/// <param name="ModifierPoolPerPlayer"><c>sim.modifierPoolPerPlayer</c>.</param>
/// <param name="MailTablePages"><c>sim.mailTablePages</c>.</param>
/// <example>
/// <code>
/// SimKeys keys = BalanceKeys.Sim.Resolve(table.Registry);
/// long warmup = table.Get(keys.WarmupTicks);
/// </code>
/// </example>
public readonly record struct SimKeys(
    BalanceKey OpQueueCapacity,
    BalanceKey SystemOpReserve,
    BalanceKey MaxOpsDrainedPerTick,
    BalanceKey EventLogCapacity,
    BalanceKey EventLogReserve,
    BalanceKey MaxCatchUpTicksPerFrame,
    BalanceKey CheckRingCapacity,
    BalanceKey WarmupTicks,
    BalanceKey ModifierPoolPerTeam,
    BalanceKey ModifierPoolPerPlayer,
    BalanceKey MailTablePages);
