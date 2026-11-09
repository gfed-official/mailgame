using System.Diagnostics;
using PerformativeMail.Client.Core.Input;

namespace PerformativeMail.Game.Input;

/// <summary>
/// The router's production ms clock (ADR-0012 sections 1 and 7, TR-input-008): whole milliseconds since construction, from a
/// <see cref="Stopwatch"/>, so it never goes backwards. Godot 4.7.1's <c>InputEvent</c> has no timestamp (spike B1); the
/// <see cref="InputAdapter"/> reads <see cref="NowMs"/> once at the top of each <c>_Input</c> call, so stamps are
/// frame-granular. Build one instance and hand the same one to the router and the adapter.
/// </summary>
/// <example>
/// <code>
/// var clock = new StopwatchInputClock();
/// var router = new InputRouter(map, settings, clock, interaction, presented, carrier, throwAim, ticks, log);
/// adapter.Initialize(new AdapterDeps { Router = router, Clock = clock, Settings = settings });
/// </code>
/// </example>
public sealed class StopwatchInputClock : IInputClock
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();

    /// <inheritdoc />
    public long NowMs => _watch.ElapsedMilliseconds;
}
