namespace PerformativeMail.Game.Boot;

/// <summary>
/// One numbered step of the ADR-0019 section 3 boot order, run once by <see cref="BootSequence"/> on the main thread.
/// A step reports failure by throwing; the sequence logs it, reports the boot fault and runs no later step.
/// Plain C# (no Godot types), so the sequence is unit-tested without the engine.
/// </summary>
/// <example>
/// <code>
/// IBootStep step = new UnwiredBootStep(5, "session", "story 005");
/// BootStepResult r = step.Run(ctx);   // BootStepResult.NotWired("story 005")
/// </code>
/// </example>
public interface IBootStep
{
    /// <summary>The step number, 1 to 9 (ADR-0019 section 3); printed in every <c>BOOT</c> line.</summary>
    int Number { get; }

    /// <summary>The short step name printed in the <c>BOOT</c> lines (log contract: one lowercase word).</summary>
    string Name { get; }

    /// <summary>Which error screen a failure of this step shows; <see cref="BootFailureKind.Boot"/> unless overridden.</summary>
    BootFailureKind FailureKind => BootFailureKind.Boot;

    /// <summary>Runs the step.</summary>
    /// <param name="ctx">The boot context; a step places its products here.</param>
    /// <returns><see cref="BootStepResult.Ok"/>, or <see cref="BootStepResult.NotWired"/> for a slot its story has not filled.</returns>
    BootStepResult Run(BootContext ctx);
}

/// <summary>Which error screen a failed boot step shows (ADR-0019 section 1: <c>Main/Screens</c>).</summary>
public enum BootFailureKind
{
    /// <summary>The generic boot error screen, <c>Main/Screens/BootError</c>.</summary>
    Boot,

    /// <summary>The content error screen, <c>Main/Screens/ContentError</c> (boot step 2).</summary>
    Content,
}

/// <summary>The non-throwing outcome of a boot step: ran (<c>ok</c>) or a slot not yet wired by its owning story.</summary>
/// <example><code>return BootStepResult.NotWired("story 005");</code></example>
public readonly record struct BootStepResult
{
    private BootStepResult(string? unwiredOwner) => UnwiredOwner = unwiredOwner;

    /// <summary>The step ran.</summary>
    public static BootStepResult Ok => default;

    /// <summary>True when the step is a slot its owning story has not wired yet.</summary>
    public bool IsNotWired => UnwiredOwner is not null;

    /// <summary>The owning story of an unwired slot (for example <c>story 005</c>); null when the step ran.</summary>
    public string? UnwiredOwner { get; }

    /// <summary>The step is not wired yet.</summary>
    /// <param name="owner">The owning story, printed as <c>NOT WIRED (owner)</c>.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException"><paramref name="owner"/> is null or blank.</exception>
    public static BootStepResult NotWired(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        return new BootStepResult(owner);
    }
}
