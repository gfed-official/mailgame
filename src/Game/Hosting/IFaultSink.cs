using Godot;

namespace PerformativeMail.Game.Hosting;

/// <summary>
/// Receives the one message of a session-ending fault (ADR-0002 Implementation Guidelines): an exception from a hook,
/// <c>RunFrame</c>, the render-clock sample or <c>Present</c>, a world that is <c>Faulted</c>, or a wrong-thread frame.
/// <c>GameLoop</c> has already logged the exception and stopped the Sim when this is called. Story 004 supplies the error
/// screen; until then <see cref="LogFaultSink"/> is the default. Main thread only.
/// </summary>
/// <example><code>loop.FaultSink = errorScreen;   // story 004</code></example>
public interface IFaultSink
{
    /// <summary>Called once, when the loop faults.</summary>
    /// <param name="message">A one-line description (exception type and message, or the world fault).</param>
    void OnFault(string message);
}

/// <summary>The log-only default <see cref="IFaultSink"/>: prints the message with <c>GD.PrintErr</c>.</summary>
/// <example><code>IFaultSink sink = LogFaultSink.Instance;</code></example>
public sealed class LogFaultSink : IFaultSink
{
    private LogFaultSink()
    {
    }

    /// <summary>The shared instance (stateless).</summary>
    public static LogFaultSink Instance { get; } = new LogFaultSink();

    /// <inheritdoc />
    public void OnFault(string message) => GD.PrintErr($"[GameLoop] session ended by fault: {message}");
}
