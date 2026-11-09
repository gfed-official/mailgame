using Godot;
using PerformativeMail.Game.Presentation;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// The <c>presenter-injection</c> smoke's probe (godot-host-shell story 006): a scene node (or, with <c>new</c>, a late view) that
/// counts its <see cref="Initialize"/> calls and appends its <see cref="Tag"/> to <see cref="Log"/> on every <see cref="Present"/>.
/// Debug smoke code only.
/// </summary>
public partial class PresenterProbeView : Node, IInjectable<PresenterDeps>, IPresentedView
{
    /// <summary>The name written to <see cref="Log"/>.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Where presents are recorded (shared by the scenario's probes).</summary>
    public List<string>? Log { get; set; }

    /// <summary>Called on every <see cref="Present"/>, after the log entry.</summary>
    public Action? OnPresent { get; set; }

    /// <summary><see cref="Initialize"/> calls so far.</summary>
    public int Inits { get; private set; }

    /// <summary>The injected dependencies; null until <see cref="Initialize"/>.</summary>
    public PresenterDeps? Deps { get; private set; }

    /// <summary>The alpha of the last <see cref="Present"/>; -1 before the first.</summary>
    public int LastAlpha { get; private set; } = -1;

    /// <inheritdoc />
    public bool IsInitialized => Inits > 0;

    /// <inheritdoc />
    public void Initialize(PresenterDeps deps)
    {
        Inits++;
        Deps = deps;
    }

    /// <inheritdoc />
    public void Present(int alphaPermille)
    {
        LastAlpha = alphaPermille;
        Log?.Add(Tag);
        OnPresent?.Invoke();
    }
}
