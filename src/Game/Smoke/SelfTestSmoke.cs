namespace PerformativeMail.Game.Smoke;

// Self-test scenarios. They exist so story 011's wrapper and the SmokeRunner exit-code table
// have executed evidence: each declares the exit it is designed to end in as ExpectedExit (non-zero
// except selftest-needs-boot), so `--smoke=list` shows the expect= values and the wrapper can compare.

/// <summary>One pass then one fail: runner must exit 1.</summary>
[SmokeScenario("selftest-fail")]
public sealed class SelfTestFailSmoke : SmokeScenario
{
    /// <inheritdoc />
    public override int ExpectedExit => 1;

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        ctx.Pass("first assertion passes");
        ctx.Fail("second assertion fails on purpose");
    }
}

/// <summary>Throws in Start: runner must report the exception and exit 1.</summary>
[SmokeScenario("selftest-throws")]
public sealed class SelfTestThrowsSmoke : SmokeScenario
{
    /// <inheritdoc />
    public override int ExpectedExit => 1;

    /// <inheritdoc />
    public override void Start(SmokeContext ctx) => throw new InvalidOperationException("selftest-throws threw on purpose");
}

/// <summary>Asserts nothing: runner must exit 3.</summary>
[SmokeScenario("selftest-empty")]
public sealed class SelfTestEmptySmoke : SmokeScenario
{
    /// <inheritdoc />
    public override int ExpectedExit => 3;

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        // Intentionally no assertions.
    }
}

/// <summary>
/// Needs boot: the real boot (story 004) succeeds, so the runner starts the scenario and it exits 0. The exit-4 path
/// (RequestBoot returns false) is <c>boot-failure-exit4</c>.
/// </summary>
[SmokeScenario("selftest-needs-boot")]
public sealed class SelfTestNeedsBootSmoke : SmokeScenario
{
    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override void Start(SmokeContext ctx) => ctx.Pass("booted");
}

/// <summary>Never finishes: runner must time out after 0.5 s and exit 5.</summary>
[SmokeScenario("selftest-timeout")]
public sealed class SelfTestTimeoutSmoke : SmokeScenario
{
    /// <inheritdoc />
    public override int ExpectedExit => 5;

    /// <inheritdoc />
    public override double TimeoutSeconds => 0.5;

    /// <inheritdoc />
    public override void Start(SmokeContext ctx) => ctx.Pass("started");

    /// <inheritdoc />
    public override SmokeStatus Poll(SmokeContext ctx) => SmokeStatus.Running;
}
