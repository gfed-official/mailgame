namespace PerformativeMail.Game.Boot;

/// <summary>
/// The boot log: every line is kept in memory (the smoke scenarios read <see cref="Lines"/>) and echoed to the host's
/// printer (<c>GD.Print</c> in the game). Line format is a contract parsed by story 011's wrapper:
/// <c>BOOT &lt;n&gt; &lt;name&gt; ok|FAIL|NOT WIRED (story …)</c>, one per step; detail lines are
/// <c>BOOT &lt;n&gt; &lt;name&gt;: …</c> (or the mandated <c>BOOT 1 window min-size readback &lt;value&gt;</c>); environment
/// facts are <c>BOOT env key=value</c>.
/// </summary>
/// <example>
/// <code>
/// var log = new BootLog(GD.Print);
/// log.Write("BOOT 3 persistence: no contributor");
/// bool seen = log.Lines.Contains("BOOT 3 persistence ok");
/// </code>
/// </example>
/// <param name="echo">Optional printer each line is also written to; null keeps the lines in memory only.</param>
public sealed class BootLog(Action<string>? echo = null)
{
    private readonly List<string> _lines = [];

    /// <summary>Every line written so far, in order.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Appends one line and echoes it.</summary>
    /// <param name="line">A single line (line breaks are replaced by spaces so the contract stays one line each).</param>
    public void Write(string line)
    {
        string oneLine = line.ReplaceLineEndings(" ");
        _lines.Add(oneLine);
        echo?.Invoke(oneLine);
    }
}
