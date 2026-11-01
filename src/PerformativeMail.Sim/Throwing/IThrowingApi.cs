namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Throwing's API to other systems (ADR-0015 Key Interfaces; ADR-0003 section 4): the live flight count and the run-end flush.
/// Implemented by <see cref="ThrowingSystem"/>.
/// </summary>
/// <example>
/// <code>
/// int inAir = throwing.LiveFlights;
/// throwing.FlushAllAsTimeout();   // Run Structure, at run end
/// </code>
/// </example>
public interface IThrowingApi
{
    /// <summary>The flights in the air.</summary>
    int LiveFlights { get; }

    /// <summary>
    /// Ends every live flight as a timeout (story 013 writes the body). Until then a no-op that returns immediately.
    /// </summary>
    void FlushAllAsTimeout();
}
