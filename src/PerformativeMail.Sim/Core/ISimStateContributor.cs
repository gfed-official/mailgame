using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// A stateful system's contribution to the canonical state dump (ADR-0006 section 4, GDD R30). Bound once at composition with
/// <see cref="SimWorldBuilder.BindStateContributor"/>; <see cref="SimWorld.TryWriteDump"/> calls <see cref="Write"/> between
/// ticks only, in bind (composition) order, after Sim Core's own section.
/// <para>
/// The dump wraps each call in <c>BeginSection(SectionId)</c> / <c>EndSection()</c>, so an implementation only writes fields.
/// Rules: write every field one by one with the <see cref="CanonicalWriter"/> methods (never blit a struct); write each
/// collection as a <c>u32</c> count then its elements in ascending key order (id, slot, <c>opSeq</c>), never in dictionary or
/// insertion order; write no strings and nothing derivable from other state; never mutate state or call back into the world.
/// </para>
/// </summary>
/// <example>
/// <code>
/// sealed class BalanceState : ISimStateContributor
/// {
///     public ushort SectionId =&gt; SectionIds.Economy;
///     public void Write(ref CanonicalWriter w)
///     {
///         w.WriteU32((uint)_accounts.Length);                 // count, then ascending slot order
///         for (int i = 0; i &lt; _accounts.Length; i++) { w.WriteI64(_accounts[i].Cents); }
///     }
/// }
/// builder.BindStateContributor(new BalanceState());
/// </code>
/// </example>
public interface ISimStateContributor
{
    /// <summary>The section's id: unique per world and never reused for another layout (see <see cref="SectionIds"/>). Must not be 0.</summary>
    ushort SectionId { get; }

    /// <summary>Writes this system's canonical state into the open section. Called between ticks only.</summary>
    /// <param name="w">The writer; a section for <see cref="SectionId"/> is already open.</param>
    void Write(ref CanonicalWriter w);
}
