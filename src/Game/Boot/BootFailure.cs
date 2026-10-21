namespace PerformativeMail.Game.Boot;

/// <summary>The step that failed a boot run and why; recorded in <see cref="BootContext.Failure"/>.</summary>
/// <param name="Step">The failing step number.</param>
/// <param name="Name">The failing step name.</param>
/// <param name="Kind">Which error screen shows.</param>
/// <param name="Exception">What the step threw.</param>
/// <example><code>if (ctx.Failure is { Kind: BootFailureKind.Content }) { /* content error screen */ }</code></example>
public sealed record BootFailure(int Step, string Name, BootFailureKind Kind, Exception Exception);

/// <summary>
/// The error display the boot sequence prepares before it reports the fault: told which step failed (and so which of
/// <c>BootError</c> / <c>ContentError</c> to show) before the one <c>IFaultSink.OnFault</c> call delivers the message.
/// Implemented by the <c>Main/Screens</c> error screen.
/// </summary>
/// <example><code>display.PrepareBootFailure(new BootFailure(2, "content", BootFailureKind.Content, ex));</code></example>
public interface IBootFailureDisplay
{
    /// <summary>Records the failure the next fault message belongs to.</summary>
    /// <param name="failure">The failing step.</param>
    void PrepareBootFailure(BootFailure failure);
}
