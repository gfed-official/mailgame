namespace PerformativeMail.Game.Boot;

/// <summary>
/// Fills an extension slot of the boot order: step 3 (persistence, ADR-0024) or step 4 (input router, ADR-0012). Each
/// contributor is one explicit line in <see cref="BootPlan.Contributors"/>; the slot runs its contributors in registration
/// order. A contributor reports failure by throwing (the boot error screen shows).
/// </summary>
/// <example>
/// <code>
/// public sealed class InputBoot : IBootContributor   // input-controls 028
/// {
///     public int Step => BootPlan.InputStep;
///     public void Run(BootContext ctx) { /* validate the action map, build the router, wire sinks */ }
/// }
/// </code>
/// </example>
public interface IBootContributor
{
    /// <summary>The slot this contributor fills: <see cref="BootPlan.PersistenceStep"/> or <see cref="BootPlan.InputStep"/>.</summary>
    int Step { get; }

    /// <summary>Runs the contribution on the main thread.</summary>
    /// <param name="ctx">The boot context.</param>
    void Run(BootContext ctx);
}
