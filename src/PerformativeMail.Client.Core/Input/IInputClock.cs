namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The router's monotonic integer-millisecond clock (ADR-0012 sections 1 and 5; TR-input-010, the ms half). It drives repeat,
/// drag, glyph timing and throw aim; the Sim's tick counter drives holds and range and is a different clock. The Sim never
/// receives milliseconds. The production implementation is Stopwatch-based and lives in the Godot adapter (story 026);
/// tests use one fake clock. Implementations must never go backwards.
/// </summary>
/// <example>
/// <code>
/// long now = clock.NowMs;
/// bool repeatDue = now - lastRepeatMs >= repeatIntervalMs;
/// </code>
/// </example>
public interface IInputClock
{
    /// <summary>The current monotonic time in whole milliseconds.</summary>
    long NowMs { get; }
}
