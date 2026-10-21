using PerformativeMail.Game.Boot;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Hashing;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Tests.Unit.Boot;

/// <summary>
/// Story godot-host-shell/004 (ADR-0019 section 3): the engine-free boot core — the nine-step plan in order, the
/// return contract, the failure path (one FAIL line, kind recorded, display prepared before the one boot-fault call, no
/// later step), the content step, the extension slots and step 9 arming only. The Godot side (window adapter, screens,
/// <c>GameLoop.RequestBoot</c>) is covered by the <c>boot-*</c> / <c>main-tree-noboot</c> / <c>screen-*</c> smoke scenarios.
/// </summary>
public class BootSequenceTests
{
    private static readonly string[] _expectedStatus =
    [
        "BOOT 1 window ok",
        "BOOT 2 content ok",
        "BOOT 3 persistence ok",
        "BOOT 4 input ok",
        "BOOT 5 session NOT WIRED (story 005)",
        "BOOT 6 sim NOT WIRED (story 005)",
        "BOOT 7 world NOT WIRED (story 006)",
        "BOOT 8 views NOT WIRED (story 008)",
        "BOOT 9 start ok",
    ];

    [Fact]
    [Trait("Id", "GHS-004-AC1")]
    public void Run_ProductionPlan_LogsNineStatusLinesInOrderAndReturnsTrue()
    {
        var h = new BootHarness();

        bool ok = h.Run(h.Plan());

        Assert.True(ok);
        Assert.Equal(_expectedStatus, h.StatusLines());
        Assert.Null(h.Context.Failure);
        Assert.Empty(h.Faults);
        Assert.Empty(h.Display.Prepared);
    }

    [Fact]
    [Trait("Id", "GHS-004-AC1")]
    public void Run_EveryLine_IsEchoedToTheHostPrinterInOrder()
    {
        var h = new BootHarness();

        h.Run(h.Plan());

        Assert.Equal(h.Log.Lines, h.Echoed);
        Assert.All(h.Log.Lines, l => Assert.StartsWith("BOOT ", l, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Id", "GHS-004-AC1")]
    public void Plan_Create_HasNineStepsNumberedOneToNineWithTheContractNames()
    {
        var h = new BootHarness();

        IReadOnlyList<IBootStep> plan = h.Plan();

        Assert.Equal(Enumerable.Range(1, 9), plan.Select(s => s.Number));
        Assert.Equal(
            ["window", "content", "persistence", "input", "session", "sim", "world", "views", "start"],
            plan.Select(s => s.Name));
        Assert.Equal(
            [BootPlan.SessionStep, BootPlan.SimStep, BootPlan.WorldStep, BootPlan.ViewsStep],
            plan.OfType<UnwiredBootStep>().Select(s => s.Number));
        Assert.Empty(BootPlan.CreateContributors());
    }

    [Fact]
    [Trait("Id", "GHS-004-AC1")]
    public void Ctor_StepNumbersNotStrictlyAscending_Throws()
    {
        var record = new List<string>();

        Assert.Throws<ArgumentException>(() => new BootSequence([new RecordingStep(2, "b", record), new RecordingStep(2, "c", record)]));
        Assert.Throws<ArgumentException>(() => new BootSequence([new RecordingStep(3, "b", record), new RecordingStep(1, "a", record)]));
        Assert.Throws<ArgumentException>(() => new BootSequence([]));
        Assert.Throws<ArgumentNullException>(() => new BootSequence([new RecordingStep(1, "a", record), null!]));
    }

    [Fact]
    [Trait("Id", "TR-sim-007")]
    public void Run_OffTheBootThread_ThrowsBeforeAnyStep()
    {
        var h = new BootHarness();
        IReadOnlyList<IBootStep> plan = h.Plan();

        Exception? thrown = null;
        var other = new Thread(() => thrown = Record.Exception(() => h.Run(plan)));
        other.Start();
        other.Join();

        Assert.IsType<InvalidOperationException>(thrown);
        Assert.Empty(h.Log.Lines);
        Assert.Equal(0, h.Window.SetCalls);
    }

    [Fact]
    [Trait("Id", "GHS-004-AC5")]
    public void Run_StepThrows_LogsOneFailLineReportsOnceAndRunsNoLaterStep()
    {
        var h = new BootHarness();
        var boom = new InvalidOperationException("step three broke");
        IBootStep[] plan =
        [
            new RecordingStep(1, "one", h.Record),
            new RecordingStep(2, "two", h.Record),
            new RecordingStep(3, "three", h.Record, boom),
            new RecordingStep(4, "four", h.Record),
        ];

        bool ok = h.Run(plan);

        Assert.False(ok);
        Assert.Equal(["step 1", "step 2", "step 3", "prepare 3 Boot", "fault InvalidOperationException"], h.Record);
        Assert.Same(boom, Assert.Single(h.Faults));
        Assert.Equal(["BOOT 1 one ok", "BOOT 2 two ok", "BOOT 3 three FAIL InvalidOperationException: step three broke"], h.StatusLines());
        Assert.DoesNotContain(h.Log.Lines, l => l.StartsWith("BOOT 4", StringComparison.Ordinal));
        Assert.Equal(new BootFailure(3, "three", BootFailureKind.Boot, boom), h.Context.Failure);
        Assert.Same(h.Context.Failure, Assert.Single(h.Display.Prepared));
    }

    [Fact]
    [Trait("Id", "GHS-004-AC5")]
    public void Run_ContributorThrowsAtStep4_FailsStep4AndStopsBeforeStep5()
    {
        var h = new BootHarness();
        var boom = new InvalidOperationException("input boot failed");

        bool ok = h.Run(h.Plan(contributors: [new RecordingContributor(BootPlan.InputStep, "input-boot", h.Record, boom)]));

        Assert.False(ok);
        Assert.Equal(
            ["BOOT 1 window ok", "BOOT 2 content ok", "BOOT 3 persistence ok", "BOOT 4 input FAIL InvalidOperationException: input boot failed"],
            h.StatusLines());
        Assert.Equal(0, h.ArmCalls);
        Assert.Equal(BootFailureKind.Boot, h.Context.Failure?.Kind);
        Assert.Same(boom, Assert.Single(h.Faults));
    }

    [Fact]
    [Trait("Id", "GHS-004-AC5")]
    public void Run_SimBuildException_FailLineNamesTheCode()
    {
        var h = new BootHarness();
        var plan = new List<IBootStep>(h.Plan());
        plan[BootPlan.SimStep - 1] = new RecordingStep(BootPlan.SimStep, "sim", h.Record, new SimBuildException(SimBuildErrorCode.ReservedPhase, "bound phase 0"));

        bool ok = h.Run(plan);

        Assert.False(ok);
        Assert.Contains("BOOT 6 sim FAIL SimBuildException (code ReservedPhase): bound phase 0", h.Log.Lines);
        Assert.DoesNotContain(h.Log.Lines, l => l.StartsWith("BOOT 7", StringComparison.Ordinal));
        Assert.IsType<SimBuildException>(Assert.Single(h.Faults));
        Assert.Equal(0, h.ArmCalls);
    }

    [Fact]
    [Trait("Id", "GHS-004-AC3")]
    public void Run_ContentSourceThrows_ContentKindAndNoStep3Line()
    {
        var h = new BootHarness();
        var findings = new InvalidOperationException("The M-1a built-in content does not validate:\nCV-GEN-01 dup");

        bool ok = h.Run(h.Plan(content: new FailingContentSource(findings)));

        Assert.False(ok);
        Assert.Equal(["BOOT 1 window ok", "BOOT 2 content FAIL InvalidOperationException: The M-1a built-in content does not validate: CV-GEN-01 dup"], h.StatusLines());
        Assert.DoesNotContain(h.Log.Lines, l => l.StartsWith("BOOT 3", StringComparison.Ordinal));
        Assert.Equal(new BootFailure(2, "content", BootFailureKind.Content, findings), h.Context.Failure);
        Assert.Equal(["prepare 2 Content", "fault InvalidOperationException"], h.Record);
        Assert.Null(h.Context.Content);
    }

    [Fact]
    [Trait("Id", "GHS-004-AC3")]
    public void Run_ContentSourceReturnsNull_FailsStep2()
    {
        var h = new BootHarness();

        bool ok = h.Run(h.Plan(content: new FailingContentSource(null)));

        Assert.False(ok);
        Assert.IsType<InvalidOperationException>(Assert.Single(h.Faults));
        Assert.Equal(BootFailureKind.Content, h.Context.Failure?.Kind);
    }

    [Fact]
    [Trait("Id", "GHS-004-AC5")]
    public void Run_DisplayThrows_FaultIsStillReportedOnce()
    {
        var h = new BootHarness(displayThrows: true);
        var boom = new InvalidOperationException("boom");

        bool ok = h.Run([new RecordingStep(1, "one", h.Record, boom)]);

        Assert.False(ok);
        Assert.Same(boom, Assert.Single(h.Faults));
        Assert.Contains("BOOT 1 one: error display threw InvalidOperationException: display failed", h.Log.Lines);
    }

    [Fact]
    [Trait("Id", "GHS-004-AC5")]
    public void Run_ContextOfAFailedBoot_IsRefused()
    {
        var h = new BootHarness();
        h.Run([new RecordingStep(1, "one", h.Record, new InvalidOperationException("boom"))]);

        Assert.Throws<InvalidOperationException>(() => h.Run([new RecordingStep(1, "one", h.Record)]));
        Assert.Single(h.Faults);
    }

    [Fact]
    [Trait("Id", "TR-content-021")]
    public void ContentStep_LoadsExactlyOncePlacesTheSetAndLogsCountsAndHash()
    {
        var h = new BootHarness();

        h.Run(h.Plan());

        Assert.Equal(1, h.Content.Loads);
        ContentSet set = Assert.IsType<ContentSet>(h.Context.Content);
        string line = Assert.Single(h.Log.Lines, l => l.StartsWith("BOOT 2 content: ", StringComparison.Ordinal));
        Assert.StartsWith(
            $"BOOT 2 content: source=Counting containers={set.Containers.Count} items={set.Items.Count} mail_kinds={set.MailKinds.Count} " +
            $"destination_types={set.DestinationTypes.Count} mail_mixes={set.MailMixes.Count} hash={ContentHash.Compute(set).ToDisplayString()} load_ms=",
            line,
            StringComparison.Ordinal);
        Assert.True(set.Containers.Count > 0 && set.Items.Count > 0 && set.MailKinds.Count > 0);
    }

    [Fact]
    [Trait("Id", "CD21")]
    public void BuiltInContentSource_LoadsTheM1aSet()
    {
        var source = new BuiltInContentSource();

        ContentSet set = source.Load();

        Assert.Equal("BuiltInContent", source.Name);
        Assert.Equal(ContentHash.Compute(BuiltInContent.CreateM1a()), ContentHash.Compute(set));
    }

    [Fact]
    [Trait("Id", "GHS-004-AC4")]
    public void Slots_NoContributor_LogNoContributorAndPass()
    {
        var h = new BootHarness();

        h.Run(h.Plan());

        Assert.Contains("BOOT 3 persistence: no contributor", h.Log.Lines);
        Assert.Contains("BOOT 4 input: no contributor", h.Log.Lines);
    }

    [Fact]
    [Trait("Id", "GHS-004-AC4")]
    public void Slots_RunTheirContributorsInRegistrationOrderOnly()
    {
        var h = new BootHarness();
        IBootContributor[] contributors =
        [
            new RecordingContributor(BootPlan.InputStep, "input-a", h.Record),
            new RecordingContributor(BootPlan.PersistenceStep, "persist", h.Record),
            new RecordingContributor(BootPlan.InputStep, "input-b", h.Record),
        ];

        bool ok = h.Run(h.Plan(contributors: contributors));

        Assert.True(ok);
        Assert.Equal(["persist", "input-a", "input-b", "arm"], h.Record);
        Assert.Equal(2, h.Log.Lines.Count(l => l == "BOOT 4 input: contributor RecordingContributor"));
        Assert.DoesNotContain("BOOT 4 input: no contributor", h.Log.Lines);
    }

    [Theory]
    [Trait("Id", "GHS-004-AC4")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(9)]
    public void Plan_ContributorForANonSlotStep_Throws(int step)
    {
        var h = new BootHarness();

        Assert.Throws<ArgumentException>(() => h.Plan(contributors: [new RecordingContributor(step, "x", h.Record)]));
    }

    [Fact]
    [Trait("Id", "GHS-004-AC6")]
    public void Step9_OnlyArmsTheStartOnceAfterEveryOtherStep()
    {
        var h = new BootHarness();
        IBootContributor[] contributors = [new RecordingContributor(BootPlan.InputStep, "input", h.Record)];

        h.Run(h.Plan(contributors: contributors));

        Assert.Equal(1, h.ArmCalls);
        Assert.Equal(["input", "arm"], h.Record);
        Assert.Equal("BOOT 9 start ok", h.StatusLines()[^1]);
    }

    [Fact]
    [Trait("Id", "GHS-004-AC2")]
    public void WindowStep_SetsMinSizeReadsItBackAndLogsEveryEnvKeyOnce()
    {
        var h = new BootHarness();

        h.Run(h.Plan());

        Assert.Equal(1, h.Window.SetCalls);
        Assert.Equal((1280, 720), h.Context.MinSizeReadBack);
        Assert.Contains("BOOT 1 window: min-size set 1280x720 readback 1280x720", h.Log.Lines);
        Assert.DoesNotContain(h.Log.Lines, l => l.StartsWith("BOOT 1 window min-size readback", StringComparison.Ordinal));
        string[] keys = [.. h.Log.Lines.Where(l => l.StartsWith("BOOT env ", StringComparison.Ordinal)).Select(l => l["BOOT env ".Length..].Split('=')[0])];
        Assert.Equal(
            ["window_size", "content_scale_mode", "content_scale_aspect", "sim_asserts_enabled", "is_debug_build", "dotnet_version", "stopwatch_frequency", "culture", "boot_thread"],
            keys);
        Assert.Contains($"BOOT env sim_asserts_enabled={PerformativeMail.Sim.Sim.AssertsEnabled}", h.Log.Lines);
        Assert.Contains("BOOT env is_debug_build=True", h.Log.Lines);
        Assert.Contains($"BOOT env boot_thread={System.Environment.CurrentManagedThreadId}", h.Log.Lines);
        Assert.Contains($"BOOT env stopwatch_frequency={System.Diagnostics.Stopwatch.Frequency}", h.Log.Lines);
        Assert.Contains("BOOT env window_size=1600x900", h.Log.Lines);
    }

    [Fact]
    [Trait("Id", "GHS-004-AC2")]
    public void WindowStep_ReadBackDiffers_LogsTheReadBackAndStillPasses()
    {
        var h = new BootHarness();
        h.Window.ReadBackOverride = (64, 64);

        bool ok = h.Run(h.Plan());

        Assert.True(ok);
        Assert.Contains("BOOT 1 window min-size readback 64x64", h.Log.Lines);
        Assert.Equal((64, 64), h.Context.MinSizeReadBack);
        Assert.Equal("BOOT 1 window ok", h.StatusLines()[0]);
    }

    [Fact]
    [Trait("Id", "GHS-004-AC2")]
    public void WindowStep_SetterThrows_FailsStep1()
    {
        var h = new BootHarness();
        h.Window.ThrowOnSet = true;

        bool ok = h.Run(h.Plan());

        Assert.False(ok);
        Assert.Equal(["BOOT 1 window FAIL InvalidOperationException: min size setter failed"], h.StatusLines());
        Assert.Equal(0, h.Content.Loads);
    }

    [Fact]
    [Trait("Id", "GHS-004-AC5")]
    public void Describe_NamesTheTypeTheSimBuildCodeAndTheMessage()
    {
        Assert.Equal("InvalidOperationException: x", BootFaultText.Describe(new InvalidOperationException("x")));
        Assert.Equal(
            "SimBuildException (code BindAfterFreeze): late",
            BootFaultText.Describe(new SimBuildException(SimBuildErrorCode.BindAfterFreeze, "late")));
        Assert.Throws<ArgumentNullException>(() => BootFaultText.Describe(null!));
    }

    [Fact]
    [Trait("Id", "GHS-004-AC1")]
    public void UnwiredStep_ReportsItsOwnerAndNotWiredNeedsAnOwner()
    {
        var step = new UnwiredBootStep(5, "session", "story 005");
        var h = new BootHarness();

        BootStepResult result = step.Run(h.Context);

        Assert.True(result.IsNotWired);
        Assert.Equal("story 005", result.UnwiredOwner);
        Assert.False(BootStepResult.Ok.IsNotWired);
        Assert.Throws<ArgumentException>(() => BootStepResult.NotWired(" "));
    }

    [Fact]
    [Trait("Id", "GHS-004-AC1")]
    public void BootLog_KeepsEachLineOnOneLine()
    {
        var echoed = new List<string>();
        var log = new BootLog(echoed.Add);

        log.Write("BOOT 2 content FAIL a\nb");

        Assert.Equal(["BOOT 2 content FAIL a b"], log.Lines);
        Assert.Equal(log.Lines, echoed);
    }
}
