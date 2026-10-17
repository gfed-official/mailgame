using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// Collects <see cref="IClientStep"/> binds, then <see cref="Build"/>s the frozen <see cref="ClientLayer"/>
/// (the freeze pattern of <see cref="SimWorldBuilder"/>). Owning systems bind their steps by <see cref="ClientStepId"/>;
/// binds to one id run in bind order. Errors are <see cref="SimBuildException"/>s. Not thread-safe.
/// <para>
/// Previews storage is a per-id list so a later additive <c>BindPreview(PreviewSubStep, IClientStep)</c>
/// (ADR-0016, player-character story 014) can sit beside <see cref="Bind"/> without changing it.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var builder = new ClientLayerBuilder(hostClock);
/// builder.Bind(ClientStepId.CarrierStep, ownedCarrierStep);
/// ClientLayer layer = builder.Build();
/// layer.Step(isFirstTickInBatch: true);
/// </code>
/// </example>
public sealed class ClientLayerBuilder
{
    private const int StepIdCount = 4;

    private readonly IHostClock _hostClock;
    private readonly SimCounters _counters;
    private readonly List<IClientStep>[] _steps = new List<IClientStep>[StepIdCount];
    private readonly SimCheckRing? _checks;
    private ulong _levelActionMask;
    private bool _built;

    /// <summary>Creates a builder.</summary>
    /// <param name="hostClock">The host tick source the layer exposes to steps (implemented by the adapter).</param>
    /// <param name="counters">The client's counters (default a new <see cref="SimCounters"/>); the latch counts overflow here.</param>
    /// <param name="checks">The check ring the latch writes one <see cref="CheckCode.InputEdgeOverflow"/> record to per overflow (default none).</param>
    /// <exception cref="ArgumentNullException"><paramref name="hostClock"/> is null.</exception>
    /// <example><code>var builder = new ClientLayerBuilder(hostClock);</code></example>
    public ClientLayerBuilder(IHostClock hostClock, SimCounters? counters = null, SimCheckRing? checks = null)
    {
        ArgumentNullException.ThrowIfNull(hostClock);
        _hostClock = hostClock;
        _counters = counters ?? new SimCounters();
        _checks = checks;
        for (int i = 0; i < StepIdCount; i++)
        {
            _steps[i] = new List<IClientStep>();
        }
    }

    /// <summary>Binds <paramref name="step"/> to run at <paramref name="id"/>, after earlier binds of the same id.</summary>
    /// <param name="id">The step slot.</param>
    /// <param name="step">The work to run.</param>
    /// <exception cref="SimBuildException"><c>BindAfterFreeze</c> after <see cref="Build"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is not a defined <see cref="ClientStepId"/>.</exception>
    /// <example><code>builder.Bind(ClientStepId.Previews, availabilityPreview);</code></example>
    public void Bind(ClientStepId id, IClientStep step)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(step);
        if ((byte)id >= StepIdCount)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        _steps[(int)id].Add(step);
    }

    /// <summary>
    /// Declares which <see cref="FrameAction"/>s are level actions (Tap and Held-state grammar) whose repeated Press edges
    /// the <see cref="InputLatch"/> may coalesce on overflow. Bit <c>n</c> is <c>(FrameAction)n</c>. Default 0: nothing is
    /// coalesced and overflow only drops the newest presses. Hold, Aim and Interact grammar must stay out of the mask.
    /// </summary>
    /// <param name="mask">The level-action mask.</param>
    /// <exception cref="SimBuildException"><c>BindAfterFreeze</c> after <see cref="Build"/>.</exception>
    /// <example><code>builder.SetLevelActionMask(FrameActions.Bit(FrameAction.Jump) | FrameActions.Bit(FrameAction.Sprint));</code></example>
    public void SetLevelActionMask(ulong mask)
    {
        ThrowIfBuilt();
        _levelActionMask = mask;
    }

    /// <summary>Freezes the binds into a <see cref="ClientLayer"/>. The built-in <see cref="InputLatch"/> runs first at <see cref="ClientStepId.InputLatch"/>.</summary>
    /// <returns>The layer, at client tick 0.</returns>
    /// <exception cref="SimBuildException"><c>BindAfterFreeze</c> when already built.</exception>
    /// <example><code>ClientLayer layer = builder.Build();</code></example>
    public ClientLayer Build()
    {
        ThrowIfBuilt();
        _built = true;

        var latch = new InputLatch(_counters, _checks, _levelActionMask);
        var table = new IClientStep[StepIdCount][];
        for (int i = 0; i < StepIdCount; i++)
        {
            int offset = i == (int)ClientStepId.InputLatch ? 1 : 0;
            var row = new IClientStep[_steps[i].Count + offset];
            if (offset == 1)
            {
                row[0] = latch;
            }

            _steps[i].CopyTo(row, offset);
            table[i] = row;
        }

        return new ClientLayer(_hostClock, table, latch, _counters);
    }

    private void ThrowIfBuilt()
    {
        if (_built)
        {
            throw new SimBuildException(SimBuildErrorCode.BindAfterFreeze, "The ClientLayer is already built; bind steps before Build().");
        }
    }
}
