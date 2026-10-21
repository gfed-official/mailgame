namespace PerformativeMail.Game.Smoke;

/// <summary>Shared one-line assertions for smoke scenarios (each prints one PASS or FAIL line).</summary>
public static class SmokeChecks
{
    /// <summary>Passes when <paramref name="actual"/> equals <paramref name="expected"/>.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="ctx">The assertion sink.</param>
    /// <param name="what">What is checked.</param>
    /// <param name="actual">The observed value.</param>
    /// <param name="expected">The expected value.</param>
    /// <returns>True on pass.</returns>
    public static bool Expect<T>(SmokeContext ctx, string what, T actual, T expected)
        where T : notnull
    {
        if (actual.Equals(expected))
        {
            ctx.Pass($"{what} == {expected}");
            return true;
        }

        ctx.Fail($"{what} is {actual}, expected {expected}");
        return false;
    }

    /// <summary>Passes when <paramref name="action"/> throws <typeparamref name="TEx"/>.</summary>
    /// <typeparam name="TEx">The expected exception type.</typeparam>
    /// <param name="ctx">The assertion sink.</param>
    /// <param name="what">What is checked.</param>
    /// <param name="action">The code expected to throw.</param>
    public static void ExpectThrows<TEx>(SmokeContext ctx, string what, Action action)
        where TEx : Exception
    {
        try
        {
            action();
            ctx.Fail($"{what}: did not throw {typeof(TEx).Name}");
        }
        catch (TEx ex)
        {
            ctx.Pass($"{what}: threw {typeof(TEx).Name} ({ex.Message})");
        }
    }
}
