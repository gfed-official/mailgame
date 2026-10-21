namespace PerformativeMail.Game.Boot;

/// <summary>
/// A boot step its owning story has not wired yet: it does nothing and reports <c>NOT WIRED (owner)</c>, never a silent
/// skip. Story 011 asserts none is left.
/// </summary>
/// <param name="number">The step number.</param>
/// <param name="name">The step name.</param>
/// <param name="owner">The owning story, for example <c>story 005</c>.</param>
/// <example><code>new UnwiredBootStep(5, "session", "story 005")   // BOOT 5 session NOT WIRED (story 005)</code></example>
public sealed class UnwiredBootStep(int number, string name, string owner) : IBootStep
{
    private readonly BootStepResult _result = BootStepResult.NotWired(owner);

    /// <inheritdoc />
    public int Number { get; } = number;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <summary>The owning story.</summary>
    public string Owner => _result.UnwiredOwner!;

    /// <inheritdoc />
    public BootStepResult Run(BootContext ctx) => _result;
}
