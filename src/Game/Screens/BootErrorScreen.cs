using Godot;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;

namespace PerformativeMail.Game.Screens;

/// <summary>
/// One M-1a error panel under <c>Main/Screens</c> (ADR-0019 sections 1 and 3): a heading, a line saying where it failed,
/// the fault message (exception type, <c>SimBuildException</c> code name, message) and a Quit button that gets the stock
/// <c>ui_*</c> focus. <c>BootError.tscn</c> and <c>ContentError.tscn</c> both use this script.
/// <para>
/// The <c>BootError</c> panel is the loop's single <see cref="IFaultSink"/>; <c>GameLoop</c> pairs it with the
/// <c>ContentError</c> panel (<see cref="RouteContentFailuresTo"/>) before boot step 1. <see cref="BootSequence"/> tells
/// the sink which step failed (<see cref="PrepareBootFailure"/>) just before the boot-fault entry delivers the message, and
/// <see cref="OnFault"/> then decides what to show: a step-2 (content) failure on the content panel, any other step on
/// this panel, and a fault with nothing prepared — a frame fault after boot — on this panel as a session fault.
/// </para>
/// No Retry and no diagnostics list (the M0 content-error screen). The script reaches only its own children; it is not a
/// presenter or view. Main thread only.
/// </summary>
/// <example>
/// <code>
/// bootErrorPanel.RouteContentFailuresTo(contentErrorPanel);   // GameLoop, before boot step 1
/// loop.FaultSink = bootErrorPanel;
/// </code>
/// </example>
public partial class BootErrorScreen : Control, IFaultSink, IBootFailureDisplay
{
    /// <summary>Heading for a failed boot step other than content.</summary>
    public const string BootHeading = "The game could not start";

    /// <summary>Heading for a failed content step (boot step 2).</summary>
    public const string ContentHeading = "The game content could not be loaded";

    /// <summary>Heading for a fault after boot (a frame fault).</summary>
    public const string SessionHeading = "The game stopped after an error";

    /// <summary>Process exit code of the Quit button: the session ended by a fault.</summary>
    public const int QuitExitCode = 1;

    /// <summary>What the sink is showing, on this panel or the content panel.</summary>
    public enum Shown
    {
        /// <summary>No fault yet.</summary>
        Nothing,

        /// <summary>A failed boot step (this panel).</summary>
        BootStep,

        /// <summary>A failed content step (the content panel, or this one when none is paired).</summary>
        ContentStep,

        /// <summary>A fault after boot (this panel).</summary>
        SessionFault,
    }

    private Label? _heading;
    private Label? _where;
    private Label? _message;
    private Button? _quit;
    private BootErrorScreen? _contentPanel;
    private BootFailure? _announced;

    /// <summary>What this sink currently shows.</summary>
    public Shown Showing { get; private set; }

    /// <summary>How many fault messages this panel received as a sink.</summary>
    public int FaultsReceived { get; private set; }

    /// <summary>This panel's heading text (empty before <c>_Ready</c>).</summary>
    public string HeadingText => _heading?.Text ?? string.Empty;

    /// <summary>This panel's "where it failed" text (empty before <c>_Ready</c>).</summary>
    public string WhereText => _where?.Text ?? string.Empty;

    /// <summary>This panel's message text (empty before <c>_Ready</c>).</summary>
    public string MessageText => _message?.Text ?? string.Empty;

    /// <summary>True when this panel's Quit button is visible in the tree.</summary>
    public bool QuitShown => _quit?.IsVisibleInTree() ?? false;

    /// <summary>True when this panel's Quit button holds the GUI focus.</summary>
    public bool QuitFocused => _quit?.HasFocus() ?? false;

    /// <inheritdoc />
    public override void _Ready()
    {
        _heading = GetNode<Label>("Layout/Heading");
        _where = GetNode<Label>("Layout/Where");
        _message = GetNode<Label>("Layout/Details/Message");
        _quit = GetNode<Button>("Layout/Actions/QuitButton");
        _quit.Pressed += () => GetTree().Quit(QuitExitCode);
    }

    /// <summary>Pairs this sink with the panel that shows content (step 2) failures; null or this panel shows them here.</summary>
    /// <param name="contentPanel">The <c>ContentError</c> panel.</param>
    public void RouteContentFailuresTo(BootErrorScreen? contentPanel) =>
        _contentPanel = ReferenceEquals(contentPanel, this) ? null : contentPanel;

    /// <inheritdoc />
    public void PrepareBootFailure(BootFailure failure) =>
        _announced = failure ?? throw new ArgumentNullException(nameof(failure));

    /// <summary>Forgets a prepared failure whose fault will not be delivered (the loop had already faulted).</summary>
    public void DropBootFailure() => _announced = null;

    /// <inheritdoc />
    public void OnFault(string message)
    {
        FaultsReceived++;
        BootFailure? failure = _announced;
        _announced = null;
        Shown next = failure switch
        {
            null => Shown.SessionFault,
            { Kind: BootFailureKind.Content } => Shown.ContentStep,
            _ => Shown.BootStep,
        };
        string where = failure is null
            ? "The session ended; the simulation will not run again."
            : $"Failed at boot step {failure.Step} ({failure.Name}).";
        BootErrorScreen target = next == Shown.ContentStep && _contentPanel is not null ? _contentPanel : this;
        BootErrorScreen? other = ReferenceEquals(target, this) ? _contentPanel : this;
        if (target.Present(HeadingFor(next), where, message))
        {
            other?.Hide();
            Showing = next;
        }
    }

    private static string HeadingFor(Shown shown) => shown switch
    {
        Shown.ContentStep => ContentHeading,
        Shown.SessionFault => SessionHeading,
        _ => BootHeading,
    };

    // False when the panel is not ready yet: nothing was shown, so the sink state and the other panel stay as they were.
    private bool Present(string heading, string where, string message)
    {
        if (_heading is null || _where is null || _message is null || _quit is null)
        {
            GD.PrintErr($"[BootErrorScreen] {Name} received a fault before _Ready: {message}");
            return false;
        }

        _heading.Text = heading;
        _where.Text = where;
        _message.Text = message;
        Show();
        _quit.GrabFocus();
        return true;
    }
}
