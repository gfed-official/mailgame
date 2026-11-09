namespace PerformativeMail.Client.Core.Presentation.Delivery;

/// <summary>
/// GDD F4, the sweep riffle (presentation): <c>shown = min(k, 5)</c> stacks over <c>totalMs = k ≥ 2 ? min(riffleCapMs, shown × riffleStepMs) : 0</c>
/// on the render clock from the <c>ok</c> reply; it never gates input. <c>riffleStepMs</c> 60 and <c>riffleCapMs</c> 300 are PROPOSED
/// data values, passed in by the caller (the defaults until a presentation data file carries them). Pure.
/// </summary>
/// <example><code>RiffleTiming r = RiffleTiming.For(9);   // 5 stacks over 300 ms</code></example>
/// <param name="Stacks">The stacks shown.</param>
/// <param name="TotalMs">The riffle's length, ms.</param>
public readonly record struct RiffleTiming(int Stacks, int TotalMs)
{
    /// <summary>GDD F4 <c>riffleStepMs</c> (PROPOSED).</summary>
    public const int DefaultStepMs = 60;

    /// <summary>GDD F4 <c>riffleCapMs</c> (PROPOSED).</summary>
    public const int DefaultCapMs = 300;

    /// <summary>The most stacks shown in world (beyond it the count is the HUD pill's).</summary>
    public const int MaxStacks = 5;

    /// <summary>The riffle for <paramref name="k"/> entries posted.</summary>
    /// <param name="k">The entries in the reply's <c>got</c>.</param>
    /// <param name="stepMs">The per-stack step.</param>
    /// <param name="capMs">The cap.</param>
    /// <returns>The stacks and length.</returns>
    public static RiffleTiming For(int k, int stepMs = DefaultStepMs, int capMs = DefaultCapMs)
    {
        int shown = Math.Clamp(k, 0, MaxStacks);
        return new RiffleTiming(shown, k >= 2 ? Math.Min(capMs, shown * stepMs) : 0);
    }
}
