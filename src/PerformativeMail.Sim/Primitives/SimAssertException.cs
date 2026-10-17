namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Thrown by <see cref="SimAssert"/> when an assertion fails in a build compiled with
/// <c>SIM_ASSERT</c>. Carries the numeric assert code so a failure is identifiable
/// without string formatting.
/// </summary>
/// <example>
/// <code>
/// try { SimAssert.Fail(42); }
/// catch (SimAssertException e) { Console.WriteLine(e.Code); } // 42
/// </code>
/// </example>
public sealed class SimAssertException : Exception
{
    /// <summary>Creates the exception for the given assert code.</summary>
    /// <param name="code">Caller-defined assert code identifying the failed check.</param>
    public SimAssertException(ushort code)
        : base($"SimAssert failed: code {code}")
    {
        Code = code;
    }

    /// <summary>The assert code passed to <see cref="SimAssert.That"/> or <see cref="SimAssert.Fail"/>.</summary>
    public ushort Code { get; }
}
