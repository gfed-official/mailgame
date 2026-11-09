using Godot;
using PerformativeMail.Client.Core.Ui;
using PerformativeMail.Game.Presentation;

namespace PerformativeMail.Game.Hud;

/// <summary>Shift timer, top left, time only (story 002). Written only when <see cref="HudDirty.Timer"/> is set.</summary>
public partial class ShiftTimerView : Control, IInjectable<PresenterDeps>, IPresentedView
{
    private readonly Label _label = new();
    private IHudState? _hud;
    private string _last = string.Empty;

    /// <summary>Lays out the label in the top-left safe area.</summary>
    public ShiftTimerView()
    {
        Name = "ShiftTimerView";
        FocusMode = FocusModeEnum.None;
        MouseFilter = MouseFilterEnum.Ignore;
        Position = new Vector2(48, 32);
        Size = new Vector2(200, 48);
        Visible = false;
        _label.AddThemeFontSizeOverride("font_size", 32);
        AddChild(_label);
    }

    /// <inheritdoc />
    public bool IsInitialized { get; private set; }

    /// <inheritdoc />
    public void Initialize(PresenterDeps deps)
    {
        _hud = deps.HudState;
        IsInitialized = true;
        Write();
    }

    /// <inheritdoc />
    public void Present(int alphaPermille)
    {
        if (_hud is not null && (_hud.Dirty & HudDirty.Timer) != 0)
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

        ShiftTimer t = _hud.Timer;
        if (Visible != t.Visible)
        {
            Visible = t.Visible;
        }

        string text = t.Visible ? HudStringsEn.TimerText(t.DisplaySeconds) : string.Empty;
        if (_last != text)
        {
            _label.Text = text;
            _last = text;
        }
    }
}
