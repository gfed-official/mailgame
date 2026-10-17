namespace PerformativeMail.Sim.Core;

/// <summary>
/// An invariant check run after phase 12 and before phase 13 (ADR-0003 section 2). It must not mutate
/// state or raise events; it may record to the check ring or counters it was given.
/// </summary>
/// <example>
/// <code>
/// sealed class NoNegativeBalance : ITickEndCheck { public void Check(uint tick) { /* read-only verification */ } }
/// builder.BindTickEnd(new NoNegativeBalance());
/// </code>
/// </example>
public interface ITickEndCheck
{
    /// <summary>Checks invariants for the tick that just finished its phases.</summary>
    /// <param name="tick">The tick being finished.</param>
    void Check(uint tick);
}
