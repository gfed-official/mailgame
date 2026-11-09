using Godot;
using PerformativeMail.Client.Core.Ui;
using PerformativeMail.Game.Presentation;

namespace PerformativeMail.Game.Hud;

/// <summary>
/// The persistent HUD layer (hud-notifications story 002): refreshes <see cref="IHudState"/> once per presented frame, before its views
/// (it is registered before its children, so it is presented first). Children are built in code; nothing is looked up in <c>_Ready</c>.
/// </summary>
/// <example><code>HudLayers.Install(hudPresenter);</code></example>
public partial class HudLayer : CanvasLayer, IInjectable<PresenterDeps>, IPresentedView
{
    private IHudState? _hud;

    /// <summary>Creates the layer with the three views.</summary>
    public HudLayer()
    {
        Name = "Persistent";
        AddChild(new WalletView());
        AddChild(new ShiftTimerView());
        AddChild(new DeliverPromptView());
    }

    /// <inheritdoc />
    public bool IsInitialized { get; private set; }

    /// <summary>Money formatting for every view (the one formatter).</summary>
    public IMoneyFormatter Money { get; set; } = new EnMoneyFormatter();

    /// <inheritdoc />
    public void Initialize(PresenterDeps deps)
    {
        _hud = deps.HudState;
        IsInitialized = true;
    }

    /// <inheritdoc />
    public void Present(int alphaPermille) => _hud?.Refresh();
}

/// <summary>Entry point for the boot assembly.</summary>
public static class HudLayers
{
    /// <summary>The layer's place in the HUD stack (ADR-0018 section 4.1, first).</summary>
    public const int PersistentOrder = 1;

    /// <summary>Adds the persistent HUD layer to <paramref name="hud"/>. Wire after WP-A part 2 (boot step 7, with <c>PresenterDeps.HudState</c> set).</summary>
    /// <param name="hud">The HUD presenter.</param>
    /// <param name="money">The money formatter; English cents when null.</param>
    /// <returns>The layer.</returns>
    public static HudLayer Install(HudPresenter hud, IMoneyFormatter? money = null)
    {
        var layer = new HudLayer();
        if (money is not null)
        {
            layer.Money = money;
        }

        hud.AddLayer(layer, PersistentOrder);
        return layer;
    }
}
