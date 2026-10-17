using System.Diagnostics;

namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Debug-time assertions for Sim and Client.Core code. Stateless.
/// <para>
/// Both methods are <c>[Conditional("SIM_ASSERT")]</c>: the compiler removes every call
/// site, <b>including evaluation of its arguments</b>, from builds that do not define
/// <c>SIM_ASSERT</c> (Release). Arguments must therefore be pure. Because the attribute
/// binds at the caller, <c>SIM_ASSERT</c> is defined for the Sim and Client.Core
/// assemblies by <c>Directory.Build.targets</c>. Always-on checks use the per-world
/// check ring instead (a later story), not this class.
/// </para>
/// </summary>
/// <example>
/// <code>
/// SimAssert.That(slot &lt; 8, 101);   // throws SimAssertException(101) under SIM_ASSERT
/// SimAssert.Fail(102);               // unconditional failure under SIM_ASSERT
/// </code>
/// </example>
public static class SimAssert
{
    /// <summary>Throws <see cref="SimAssertException"/> if <paramref name="condition"/> is false.</summary>
    /// <param name="condition">The invariant; must be side-effect free (not evaluated when compiled out).</param>
    /// <param name="code">Assert code carried by the exception.</param>
    /// <exception cref="SimAssertException"><paramref name="condition"/> is false (SIM_ASSERT builds only).</exception>
    [Conditional("SIM_ASSERT")]
    public static void That(bool condition, ushort code)
    {
        if (!condition)
        {
            throw new SimAssertException(code);
        }
    }

    /// <summary>Unconditionally throws <see cref="SimAssertException"/> (SIM_ASSERT builds only).</summary>
    /// <param name="code">Assert code carried by the exception.</param>
    /// <exception cref="SimAssertException">Always, in SIM_ASSERT builds.</exception>
    [Conditional("SIM_ASSERT")]
    public static void Fail(ushort code)
    {
        throw new SimAssertException(code);
    }
}
