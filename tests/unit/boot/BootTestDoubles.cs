using PerformativeMail.Game.Boot;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.Sim.Tests.Unit.Boot;

/// <summary>An in-memory <see cref="IBootWindow"/>: records the min size, can read back another value or throw.</summary>
internal sealed class StubWindow : IBootWindow
{
    private (int Width, int Height) _min;

    public (int Width, int Height)? ReadBackOverride { get; set; }

    public bool ThrowOnSet { get; set; }

    public int SetCalls { get; private set; }

    public (int Width, int Height) MinSize
    {
        get => ReadBackOverride ?? _min;
        set
        {
            SetCalls++;
            if (ThrowOnSet)
            {
                throw new InvalidOperationException("min size setter failed");
            }

            _min = value;
        }
    }

    public (int Width, int Height) Size => (1600, 900);

    public string ContentScaleMode => "CanvasItems";

    public string ContentScaleAspect => "KeepHeight";
}

/// <summary>A content source over <see cref="BuiltInContent.CreateM1a"/> that counts loads.</summary>
internal sealed class TallyingContentSource : IContentBootSource
{
    public int Loads { get; private set; }

    public string Name => "Tallying";

    public ContentSet Load()
    {
        Loads++;
        return BuiltInContent.CreateM1a();
    }
}

/// <summary>A content source that throws (or returns null when <see cref="Exception"/> is null).</summary>
internal sealed class BrokenContentSource(Exception? exception) : IContentBootSource
{
    public string Name => "Broken";

    public ContentSet Load() => exception is null ? null! : throw exception;
}

/// <summary>A step that records its run into a shared list and optionally throws.</summary>
internal sealed class ScriptedStep(int number, string name, List<string> record, Exception? exception = null) : IBootStep
{
    public int Number { get; } = number;

    public string Name { get; } = name;

    public BootStepResult Run(BootContext ctx)
    {
        record.Add($"step {Number}");
        return exception is null ? BootStepResult.Ok : throw exception;
    }
}

/// <summary>A contributor that records its run into a shared list and optionally throws.</summary>
internal sealed class SlotProbe(int step, string name, List<string> record, Exception? exception = null) : IBootContributor
{
    public int Step { get; } = step;

    public void Run(BootContext ctx)
    {
        record.Add(name);
        if (exception is not null)
        {
            throw exception;
        }
    }
}

/// <summary>An error display that records preparations into a shared list.</summary>
internal sealed class DisplaySpy(List<string> record, bool throws = false) : IBootFailureDisplay
{
    public List<BootFailure> Prepared { get; } = [];

    public void PrepareBootFailure(BootFailure failure)
    {
        Prepared.Add(failure);
        record.Add($"prepare {failure.Step} {failure.Kind}");
        if (throws)
        {
            throw new InvalidOperationException("display failed");
        }
    }
}

/// <summary>A boot harness: context with recording fault entry and display, and the full plan over doubles.</summary>
internal sealed class BootHarness
{
    public BootHarness(bool displayThrows = false)
    {
        Log = new BootLog(Echoed.Add);
        Display = new DisplaySpy(Record, displayThrows);
        Context = new BootContext(Log, System.Environment.CurrentManagedThreadId, ex =>
        {
            Faults.Add(ex);
            Record.Add($"fault {ex.GetType().Name}");
        }, Display);
    }

    public List<string> Record { get; } = [];

    public List<Exception> Faults { get; } = [];

    public List<string> Echoed { get; } = [];

    public BootLog Log { get; }

    public DisplaySpy Display { get; }

    public BootContext Context { get; }

    public StubWindow Window { get; } = new();

    public TallyingContentSource Content { get; } = new();

    public int ArmCalls { get; private set; }

    public IReadOnlyList<IBootStep> Plan(IContentBootSource? content = null, IReadOnlyList<IBootContributor>? contributors = null) =>
        BootPlan.Create(Window, isDebugBuild: true, content ?? Content, contributors ?? BootPlan.CreateContributors(), () =>
        {
            ArmCalls++;
            Record.Add("arm");
        });

    public bool Run(IReadOnlyList<IBootStep> plan) => new BootSequence(plan).Run(Context);

    /// <summary>The <c>BOOT n name status</c> lines (not detail or env lines).</summary>
    public string[] StatusLines() =>
    [
        .. Log.Lines.Where(l =>
        {
            string[] p = l.Split(' ', 4);
            return p.Length == 4 && p[0] == "BOOT" && int.TryParse(p[1], out _) && !p[2].EndsWith(':')
                && (p[3] == "ok" || p[3].StartsWith("FAIL ", StringComparison.Ordinal) || p[3].StartsWith("NOT WIRED (", StringComparison.Ordinal));
        }),
    ];
}
