using Godot;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;

namespace PerformativeMail.Game.Screens;

/// <summary>
/// The M-1a error screens under <c>Main/Screens</c> (ADR-0019 sections 1 and 3): a title, a detail line, the message
/// (exception type, <c>SimBuildException</c> code name and message) and a Quit button; stock <c>ui_*</c> focus. Both
/// <c>BootError.tscn</c> and <c>ContentError.tscn</c> use this script. The <c>BootError</c> instance is the one
/// <see cref="IFaultSink"/> <c>GameLoop</c> installs before boot step 1; its <see cref="ContentErrorScreen"/> points at the
/// sibling <c>ContentError</c> instance. The boot sequence calls <see cref="PrepareBootFailure"/> before the boot-fault
/// entry, so <see cref="OnFault"/> shows <c>ContentError</c> for a step-2 failure and <c>BootError</c> otherwise; a frame
/// fault after boot (nothing prepared) shows <c>BootError</c>. No Retry and no diagnostics list (M0 content work). Looks
/// up only its own children; it is not a presenter or view. Main thread only.
/// </summary>
/// <example>
/// <code>
/// loop.FaultSink = bootErrorScreen;                              // GameLoop.CreateBootContext, before step 1
/// bootErrorScreen.PrepareBootFailure(failure);                   // BootSequence, on a throwing step
/// bootErrorScreen.OnFault("SimBuildException (code ReservedPhase): …");
/// </code>
/// </example>
public partial class BootErrorScreen : Control, IFaultSink, IBootFailureDisplay
{
    /// <summary>The title of a boot-step failure.</summary>
    public const string BootFailedTitle = "Boot failed";

    /// <summary>The title of a content (step 2) failure.</summary>
    public const string ContentFailedTitle = "Content failed to load";

    /// <summary>The title of a fault after boot (a frame fault).</summary>
    public const string SessionFaultTitle = "The game stopped after an error";

    /// <summary>The process exit code of the Quit button (the session ended by a fault).</summary>
    public const int QuitExitCode = 1;

    private Label? _title;
    private Label? _detail;
    private Label? _message;
    private Button? _quit;
    private BootFailure? _pending;

    /// <summary>The sibling content error screen this sink shows for a step-2 failure; null on that screen itself.</summary>
    [Export]
    public BootErrorScreen? ContentErrorScreen { get; set; }

    /// <summary>How many times <see cref="OnFault"/> was called on this sink.</summary>
    public int FaultCount { get; private set; }

    /// <summary>The shown title text (empty before <c>_Ready</c>).</summary>
    public string TitleText => _title?.Text ?? string.Empty;

    /// <summary>The shown detail text (empty before <c>_Ready</c>).</summary>
    public string DetailText => _detail?.Text ?? string.Empty;

    /// <summary>The shown message text (empty before <c>_Ready</c>).</summary>
    public string MessageText => _message?.Text ?? string.Empty;

    /// <summary>True when the Quit button exists and is visible in the tree.</summary>
    public bool QuitVisible => _quit is not null && _quit.IsVisibleInTree();

    /// <summary>True when the Quit button has the GUI focus.</summary>
    public bool QuitHasFocus => _quit is not null && _quit.HasFocus();

    /// <inheritdoc />
    public override void _Ready()
    {
        _title = GetNode<Label>("Margin/Column/Title");
        _detail = GetNode<Label>("Margin/Column/Detail");
        _message = GetNode<Label>("Margin/Column/Scroll/Message");
        _quit = GetNode<Button>("Margin/Column/Quit");
        _quit.Pressed += OnQuitPressed;
    }

    /// <inheritdoc />
    public void PrepareBootFailure(BootFailure failure) =>
        _pending = failure ?? throw new ArgumentNullException(nameof(failure));

    /// <inheritdoc />
    public void OnFault(string message)
    {
        FaultCount++;
        BootFailure? failure = _pending;
        _pending = null;
        if (failure is null)
        {
            ContentErrorScreen?.Hide();
            ShowError(SessionFaultTitle, "The session ended; the simulation will not run again.", message);
            return;
        }

        string detail = $"Boot step {failure.Step} ({failure.Name}) failed.";
        if (failure.Kind == BootFailureKind.Content && ContentErrorScreen is not null)
        {
            Hide();
            ContentErrorScreen.ShowError(ContentFailedTitle, detail, message);
            return;
        }

        ContentErrorScreen?.Hide();
        ShowError(failure.Kind == BootFailureKind.Content ? ContentFailedTitle : BootFailedTitle, detail, message);
    }

    /// <summary>Fills the labels, shows this screen and focuses Quit.</summary>
    /// <param name="title">The title.</param>
    /// <param name="detail">The detail line.</param>
    /// <param name="message">The message.</param>
    public void ShowError(string title, string detail, string message)
    {
        if (_title is null || _detail is null || _message is null || _quit is null)
        {
            GD.PrintErr($"[BootErrorScreen] {Name} is not ready; fault: {message}");
            return;
        }

        _title.Text = title;
        _detail.Text = detail;
        _message.Text = message;
        Show();
        _quit.GrabFocus();
    }

    private void OnQuitPressed() => GetTree().Quit(QuitExitCode);
}
