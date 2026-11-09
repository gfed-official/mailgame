using Godot;
using PerformativeMail.Client.Core.Ui;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Game.Hud;

/// <summary>Wallet readout, top right (story 002). Text is written at <c>Initialize</c> and when <see cref="HudDirty.Wallet"/> is set.</summary>
public partial class WalletView : Control, IInjectable<PresenterDeps>, IPresentedView
{
    private readonly Label _label = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private IHudState? _hud;
    private IMoneyFormatter _money = new EnMoneyFormatter();
    private string _last = string.Empty;

    /// <summary>Lays out the label in the top-right safe area.</summary>
    public WalletView()
    {
        Name = "WalletView";
        FocusMode = FocusModeEnum.None;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.TopRight);
        OffsetLeft = -248;
        OffsetRight = -48;
        OffsetTop = 32;
        OffsetBottom = 72;
        _label.AddThemeFontSizeOverride("font_size", 24);
        _label.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_label);
    }

    /// <inheritdoc />
    public bool IsInitialized { get; private set; }

    /// <inheritdoc />
    public void Initialize(PresenterDeps deps)
    {
        _hud = deps.HudState;
        if (GetParent() is HudLayer layer)
        {
            _money = layer.Money;
        }

        IsInitialized = true;
        Write();
    }

    /// <inheritdoc />
    public void Present(int alphaPermille)
    {
        if (_hud is not null && (_hud.Dirty & HudDirty.Wallet) != 0)
        {
            Write();
        }
    }

    private void Write()
    {
        if (_hud is null)
        {
            return;
        }

        string text = _money.Format(_hud.WalletCents.Value, MoneySign.Balance);
        if (_last != text)
        {
            _label.Text = text;
            _last = text;
        }
    }
}
