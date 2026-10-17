namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// How an action reaches its consumer (GDD R1, ADR-0012 section 4). <c>0</c> is "missing" and never valid.
/// </summary>
/// <example><code>bool viaFrame = def.Delivery == Delivery.Frame;</code></example>
public enum Delivery : byte
{
    /// <summary>Missing. Never valid.</summary>
    Unspecified = 0,

    /// <summary>Through the per-tick input frame. Carries a <c>FrameAction</c> bit unless the grammar is <see cref="Grammar.Axis"/>.</summary>
    Frame = 1,

    /// <summary>A reliable request on the event's frame (R11). Never has a frame bit.</summary>
    Immediate = 2,
}
