using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Ui;

/// <summary>What changed in the last <see cref="IHudState.Refresh"/>.</summary>
[Flags]
public enum HudDirty : byte
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The wallet balance.</summary>
    Wallet = 1,

    /// <summary>The timer's display seconds or visibility.</summary>
    Timer = 2,

    /// <summary>Any prompt field.</summary>
    Prompt = 4,
}

/// <summary>The HUD read model views pull each frame (H30). Read-only over published state; <see cref="Refresh"/> is called once per frame before the views.</summary>
public interface IHudState
{
    /// <summary>The wallet balance.</summary>
    Cents WalletCents { get; }

    /// <summary>The shift timer.</summary>
    ShiftTimer Timer { get; }

    /// <summary>The focused prompt.</summary>
    DeliverPrompt Prompt { get; }

    /// <summary>What the last refresh changed.</summary>
    HudDirty Dirty { get; }

    /// <summary>Samples the sources.</summary>
    void Refresh();
}

/// <summary><see cref="IHudState"/> over the narrow ports, the interaction query and the render clock.</summary>
/// <example><code>var hud = new HudState(wallet, run, interaction, renderClock); hud.Refresh(); if ((hud.Dirty &amp; HudDirty.Wallet) != 0) { }</code></example>
public sealed class HudState : IHudState
{
    private readonly IHudWallet _wallet;
    private readonly IHudRun _run;
    private readonly IInteractionQuery _interaction;
    private readonly IRenderClock _clock;

    /// <summary>Creates the read model.</summary>
    /// <param name="wallet">Wallet port.</param>
    /// <param name="run">Run port.</param>
    /// <param name="interaction">Interaction snapshot port.</param>
    /// <param name="clock">The render clock.</param>
    public HudState(IHudWallet wallet, IHudRun run, IInteractionQuery interaction, IRenderClock clock)
    {
        _wallet = wallet;
        _run = run;
        _interaction = interaction;
        _clock = clock;
    }

    /// <inheritdoc />
    public Cents WalletCents { get; private set; }

    /// <inheritdoc />
    public ShiftTimer Timer { get; private set; }

    /// <inheritdoc />
    public DeliverPrompt Prompt { get; private set; }

    /// <inheritdoc />
    public HudDirty Dirty { get; private set; }

    /// <inheritdoc />
    public void Refresh()
    {
        HudDirty dirty = HudDirty.None;

        Cents wallet = _wallet.Balance;
        if (wallet != WalletCents)
        {
            WalletCents = wallet;
            dirty |= HudDirty.Wallet;
        }

        if (_clock.IsValid)
        {
            ShiftTimer timer = ShiftTimerF3.Step(Timer, _run.Phase, _run.DeadlineTick, _clock.RenderSimTimeMs);
            if (timer.Visible != Timer.Visible || timer.DisplaySeconds != Timer.DisplaySeconds)
            {
                dirty |= HudDirty.Timer;
            }

            Timer = timer;
        }

        DeliverPrompt prompt = DeliverPrompt.From(in _interaction.Current);
        if (prompt != Prompt)
        {
            Prompt = prompt;
            dirty |= HudDirty.Prompt;
        }

        Dirty = dirty;
    }
}
