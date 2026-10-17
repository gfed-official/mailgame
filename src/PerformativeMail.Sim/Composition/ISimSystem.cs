using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Composition;

/// <summary>
/// A composed Sim system (ADR-0003 section 4). It is constructed with plain <c>new</c> by <see cref="SimComposition"/>,
/// taking only constructor parameters from the closed injectable list, and then binds its phases, ops, event kinds,
/// readers, tick-end checks and state contributors in <see cref="Bind"/>. Bind order is construction order, so it also fixes
/// the same-phase binder order, the tick-end order and the dump-section order. A system never receives the
/// <see cref="SimWorld"/>, the <see cref="SimSystemContext"/> or the <see cref="RngRegistry"/>.
/// The <c>I…Api</c> interfaces a system provides to the systems above it are implemented by the system object itself.
/// Not thread-safe: built and bound on the thread that will call <see cref="SimWorld.Tick"/>.
/// </summary>
/// <example>
/// <code>
/// public void Bind(SimWorldBuilder builder)
/// {
///     builder.Bind(PhaseId.MailStep, _mailStep);
///     builder.BindStateContributor(_dump);
/// }
/// </code>
/// </example>
public interface ISimSystem
{
    /// <summary>Binds the system's phases, ops, event kinds, readers, tick-end checks and contributors. Called once, right after construction.</summary>
    /// <param name="builder">The world's builder (still <see cref="SimState.Constructed"/>); do not keep a reference to it.</param>
    void Bind(SimWorldBuilder builder);
}
