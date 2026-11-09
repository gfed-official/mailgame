using Godot;
using PerformativeMail.Client.Core.Ui;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Game.Hud;

/// <summary>
/// The Deliver prompt (story 003): up to three lines on a plate, 96 ref px under the viewport centre, shown iff the snapshot has a
/// focus. Rows are rebuilt only on <see cref="HudDirty.Prompt"/>; the mark comes from the Sim's branch.
/// </summary>
public partial class DeliverPromptView : Control, IInjectable<PresenterDeps>, IPresentedView
{
    private const float PlateWidth = 720f;
    private readonly PanelContainer _plate = new();
    private readonly Label[] _lines = [new Label(), new Label(), new Label()];
    private IHudState? _hud;
    private IMoneyFormatter _money = new EnMoneyFormatter();
    private IHudText _text = new NoText();
    private FrameworkReasons _reasons = PlayerReasons.Framework;

    /// <summary>Builds the plate and lines.</summary>
    public DeliverPromptView()
    {
        Name = "DeliverPromptView";
        FocusMode = FocusModeEnum.None;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.Center);
        OffsetLeft = -PlateWidth / 2;
        OffsetRight = PlateWidth / 2;
        OffsetTop = 96;
        OffsetBottom = 96;
        GrowVertical = GrowDirection.End;
        Visible = false;
        _plate.MouseFilter = MouseFilterEnum.Ignore;
        _plate.CustomMinimumSize = new Vector2(PlateWidth, 0);
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (Label line in _lines)
        {
            line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            line.HorizontalAlignment = HorizontalAlignment.Center;
            line.AddThemeFontSizeOverride("font_size", 20);
            box.AddChild(line);
        }

        _plate.AddChild(box);
        AddChild(_plate);
    }

    /// <inheritdoc />
    public bool IsInitialized { get; private set; }

    /// <summary>The binding glyph shown in the verb row; the input layer supplies the real one later.</summary>
    public string Glyph { get; set; } = "[E]";

    /// <summary>The framework reason ids (<c>reason.close_panel</c>, <c>reason.dry_land</c>); wire after WP-A part 2 with the real ids.</summary>
    public FrameworkReasons Reasons
    {
        get => _reasons;
        set => _reasons = value;
    }

    /// <inheritdoc />
    public void Initialize(PresenterDeps deps)
    {
        _hud = deps.HudState;
        _text = HudSources.Text(deps.World);
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
        if (_hud is not null && (_hud.Dirty & HudDirty.Prompt) != 0)
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

        DeliverPrompt p = _hud.Prompt;
        PromptRows rows = DeliverPromptRows.Build(in p, _money, _text, Glyph, _reasons);
        Set(_lines[0], rows.Line1, rows.Dim);
        Set(_lines[1], rows.Line2, rows.Dim);
        Set(_lines[2], rows.Line3, rows.Dim);
        bool show = p.Shown && rows.Line1.Length > 0;
        if (Visible != show)
        {
            Visible = show;
        }
    }

    private static void Set(Label label, string text, bool dim)
    {
        if (label.Text != text)
        {
            label.Text = text;
        }

        label.Modulate = dim ? new Color(1, 1, 1, 0.6f) : Colors.White;
        label.Visible = text.Length > 0;
    }

    private sealed class NoText : IHudText
    {
        public bool TryGetDisplayText(PerformativeMail.Sim.Primitives.StringId id, out string text)
        {
            text = string.Empty;
            return false;
        }
    }
}
