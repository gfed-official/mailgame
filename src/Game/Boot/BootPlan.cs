namespace PerformativeMail.Game.Boot;

/// <summary>
/// The nine boot steps of ADR-0019 section 3 as an explicit, ordered list (no reflection discovery, ADR-0003 style).
/// <see cref="Create"/> is the only place the order is written; <see cref="BootSequence"/> runs it from
/// <c>GameLoop.RequestBoot()</c>.
/// <para>Who fills each step (one explicit line here each; story 011 asserts no step is left NOT WIRED):</para>
/// <list type="table">
/// <listheader><term>Step</term><description>Owner</description></listheader>
/// <item><term>1 window</term><description>godot-host-shell 004: <see cref="WindowGuardStep"/> (min size 1280×720, env lines).</description></item>
/// <item><term>2 content</term><description>godot-host-shell 004: <see cref="ContentBootStep"/> over <see cref="IContentBootSource"/> (M-1a <see cref="BuiltInContentSource"/>; story 007 adds <c>ContentHost</c>).</description></item>
/// <item><term>3 persistence</term><description>Slot (<see cref="IBootContributor"/>, <see cref="PersistenceStep"/>): ADR-0024 persistence (not written yet).</description></item>
/// <item><term>4 input</term><description>Slot (<see cref="IBootContributor"/>, <see cref="InputStep"/>): input-controls 028 <c>InputBoot</c> (validate <c>BuiltInActionMap</c>, read <c>user://input.json</c>, build the router, wire sinks; the adapter node's placement is 028's).</description></item>
/// <item><term>5 session</term><description>godot-host-shell 005: <c>HostSession</c> (loopback, ADR-0011).</description></item>
/// <item><term>6 sim</term><description>godot-host-shell 005: <c>SimComposition.Build</c> then <c>ClientLayer.Attach(ports)</c>; catches <c>SimBuildException</c> (ADR-0003).</description></item>
/// <item><term>7 world</term><description>godot-host-shell 006 / 010: world tables, <c>Terrain</c> / <c>Destinations</c>, <c>Initialize(deps)</c>.</description></item>
/// <item><term>8 views</term><description>godot-host-shell 008 / 009: <c>ViewCatalogResolver.Resolve(ContentSet)</c>, pools pre-instantiated.</description></item>
/// <item><term>9 start</term><description>godot-host-shell 004: <see cref="ArmStartStep"/> (<c>GameLoop.ArmStart</c> only; the driver starts on the first <c>_Process</c>, ADR-0002).</description></item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// IReadOnlyList&lt;IBootStep&gt; plan = BootPlan.Create(
///     new GodotBootWindow(GetWindow()), OS.IsDebugBuild(), new BuiltInContentSource(), BootPlan.CreateContributors(), ArmStart);
/// </code>
/// </example>
public static class BootPlan
{
    /// <summary>Step 1, window guard.</summary>
    public const int WindowStep = 1;

    /// <summary>Step 2, content.</summary>
    public const int ContentStep = 2;

    /// <summary>Step 3, persistence slot.</summary>
    public const int PersistenceStep = 3;

    /// <summary>Step 4, input slot.</summary>
    public const int InputStep = 4;

    /// <summary>Step 5, session.</summary>
    public const int SessionStep = 5;

    /// <summary>Step 6, Sim composition.</summary>
    public const int SimStep = 6;

    /// <summary>Step 7, world.</summary>
    public const int WorldStep = 7;

    /// <summary>Step 8, views.</summary>
    public const int ViewsStep = 8;

    /// <summary>Step 9, start (armed only).</summary>
    public const int StartStep = 9;

    /// <summary>The production slot contributors, one explicit line each (fresh instances per boot).</summary>
    /// <returns>The contributors in registration order; none at this point (input-controls 028 adds <c>new InputBoot()</c>).</returns>
    public static IReadOnlyList<IBootContributor> CreateContributors() => [];

    /// <summary>Builds the nine steps in ADR-0019 section 3 order.</summary>
    /// <param name="window">The root window (step 1).</param>
    /// <param name="isDebugBuild"><c>OS.IsDebugBuild()</c>, logged by step 1.</param>
    /// <param name="content">The content source (step 2).</param>
    /// <param name="contributors">The slot contributors (steps 3 and 4), usually <see cref="CreateContributors"/>.</param>
    /// <param name="armStart">The step 9 callback, <c>GameLoop.ArmStart</c>.</param>
    /// <returns>Nine steps numbered 1 to 9.</returns>
    /// <exception cref="ArgumentNullException">An argument or a contributor is null.</exception>
    /// <exception cref="ArgumentException">A contributor names a step that is not a slot (3 or 4); it would never run.</exception>
    public static IReadOnlyList<IBootStep> Create(
        IBootWindow window,
        bool isDebugBuild,
        IContentBootSource content,
        IReadOnlyList<IBootContributor> contributors,
        Action armStart)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        foreach (IBootContributor c in contributors)
        {
            ArgumentNullException.ThrowIfNull(c, nameof(contributors));
            if (c.Step is not (PersistenceStep or InputStep))
            {
                throw new ArgumentException(
                    $"Boot contributor {c.GetType().Name} names step {c.Step}; only steps {PersistenceStep} and {InputStep} are slots.",
                    nameof(contributors));
            }
        }

        return
        [
            new WindowGuardStep(window, isDebugBuild),
            new ContentBootStep(content),
            new ContributorSlotStep(PersistenceStep, "persistence", contributors),
            new ContributorSlotStep(InputStep, "input", contributors),
            new UnwiredBootStep(SessionStep, "session", "story 005"),
            new UnwiredBootStep(SimStep, "sim", "story 005"),
            new UnwiredBootStep(WorldStep, "world", "story 006"),
            new UnwiredBootStep(ViewsStep, "views", "story 008"),
            new ArmStartStep(armStart),
        ];
    }
}
