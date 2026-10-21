using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Composition;

/// <summary>
/// Marks a Sim system type and gives its composition identity (ADR-0003 section 4). The <see cref="Id"/> maps the type to
/// its row in <see cref="SystemGraph"/>. The attribute is read only by the architecture test
/// (<c>tests/unit/architecture/CompositionRulesTests.cs</c>): that test checks that the type has exactly one public
/// constructor whose parameters are all on the closed injectable list for this id. <see cref="SimComposition"/> never reads it;
/// construction is explicit code, not reflection-driven discovery.
/// </summary>
/// <example>
/// <code>
/// [SimSystem(SystemId.MailDestinations)]
/// public sealed class MailSystem : ISimSystem, IMailApi
/// {
///     public MailSystem(EventLogFacade events, MailIds mailIds, IRngStreams rng, IInventoryApi grid, ContentSet content) { }
///     public void Bind(SimWorldBuilder builder) { builder.Bind(PhaseId.MailStep, _phase); }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SimSystemAttribute : Attribute
{
    /// <summary>Marks the class as the Sim system <paramref name="id"/>.</summary>
    /// <param name="id">The system's composition identity.</param>
    public SimSystemAttribute(SystemId id)
    {
        Id = id;
    }

    /// <summary>The system's composition identity.</summary>
    public SystemId Id { get; }
}
