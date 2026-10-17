namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Test seam: <see cref="SimAssert"/> call sites that live inside the Sim assembly, so
/// tests observe exactly what a real Sim call site does in the current build.
/// <para>
/// <c>[Conditional("SIM_ASSERT")]</c> binds at the caller. The manifest requires every
/// <see cref="SimAssert"/> call site to be inside Sim or Client.Core, so the test project
/// is never given <c>SIM_ASSERT</c> and instead calls these wrappers (visible via
/// <c>InternalsVisibleTo</c>). Under <c>SIM_ASSERT</c> they assert; in Release the inner
/// call site is compiled out, including evaluation of the condition delegate.
/// </para>
/// </summary>
internal static class SimAssertProbe
{
    /// <summary>Asserts the result of <paramref name="condition"/>; the delegate is not invoked when compiled out.</summary>
    /// <param name="condition">Evaluated only in builds that define <c>SIM_ASSERT</c>.</param>
    /// <param name="code">Assert code carried by the exception.</param>
    internal static void That(Func<bool> condition, ushort code) => SimAssert.That(condition(), code);

    /// <summary>Calls <see cref="SimAssert.Fail"/> from a Sim call site.</summary>
    /// <param name="code">Assert code carried by the exception.</param>
    internal static void Fail(ushort code) => SimAssert.Fail(code);
}
