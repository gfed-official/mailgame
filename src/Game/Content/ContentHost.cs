using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Godot;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Sources;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Game.Content;

/// <summary>
/// The Godot side of the content seam (ADR-0019 section 4, ADR-0010 section 1, GDD CD21, TR-content-021): a plain
/// synchronous <see cref="IContentSource"/> over a Godot folder (<c>res://content</c> by default). It reads
/// <c>&lt;root&gt;/manifest.txt</c> and each listed file as bytes with <c>FileAccess.GetFileAsBytes</c>, checks
/// <c>FileAccess.GetOpenError()</c> after every open and hands the bytes to the Sim's <see cref="ContentLoader"/>; it never
/// decodes content text and never enumerates folders. The read rule follows <see cref="IContentSource.TryRead"/>, approved by
/// Evan 2026-11-01 (TD-204 item 4): a file that is not there returns false; any other open error, and a missing manifest,
/// throws <see cref="ContentHostReadException"/>, so an error is never returned as an empty buffer.
/// <see cref="Load"/> drives <see cref="State"/> (<see cref="ContentHostState"/>) on the calling thread, with no threads and
/// no deferred calls, and logs its wall time against the 100 ms advisory budget (TR-content-028). Boot constructs it (it is
/// not a <c>Node</c>, not static and not an autoload). Main thread only.
/// </summary>
/// <example>
/// <code>
/// var host = new ContentHost();
/// if (host.Load() == ContentHostState.Valid) { ContentSet set = host.Set!; }
/// else { foreach (string line in host.Diagnostics) { GD.PrintErr(line); } }
/// </code>
/// </example>
public sealed class ContentHost : IContentSource
{
    /// <summary>The default root: the repo's <c>content/</c> folder.</summary>
    public const string DefaultRoot = "res://content";

    /// <summary>The manifest's file name under the root (ADR-0010 section 1: mandatory, generated, never committed).</summary>
    public const string ManifestName = "manifest.txt";

    /// <summary>The advisory load budget in milliseconds (TR-content-028): logged, not enforced.</summary>
    public const double AdvisoryBudgetMs = 100;

    // TD-207 item 4: Mail Dev default, pending Evan: the manifest is strict UTF-8, blank lines are skipped, and there is no BOM strip, no sort and no path rejection.
    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string _prefix;
    private readonly Action<string> _log;

    /// <summary>Creates an unloaded host.</summary>
    /// <param name="rootPath">The Godot folder holding <c>manifest.txt</c> and the content files (<c>res://</c> or <c>user://</c>).</param>
    /// <param name="log">Receives the load line; null for <c>GD.Print</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="rootPath"/> is null or empty.</exception>
    public ContentHost(string rootPath = DefaultRoot, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        Root = rootPath;
        _prefix = rootPath.EndsWith('/') ? rootPath : rootPath + "/";
        _log = log ?? GD.Print;
    }

    /// <summary>The root folder this host reads.</summary>
    public string Root { get; }

    /// <summary>The full path of the manifest (<c>&lt;root&gt;/manifest.txt</c>).</summary>
    public string ManifestPath => _prefix + ManifestName;

    /// <summary>Where the host is in its load.</summary>
    public ContentHostState State { get; private set; }

    /// <summary>The loaded set while <see cref="State"/> is <see cref="ContentHostState.Valid"/>; otherwise null.</summary>
    public ContentSet? Set { get; private set; }

    /// <summary>The loader's result of the last load, or null before one ran or when the manifest could not be read.</summary>
    public ContentLoadResult? Result { get; private set; }

    /// <summary>
    /// Every finding of the last load, one formatted line each (the loader's diagnostics, or the read failure that stopped
    /// it); empty before a load and after a clean one.
    /// </summary>
    public ImmutableArray<string> Diagnostics { get; private set; } = ImmutableArray<string>.Empty;

    /// <summary>The wall time of the last load (zero before one ran).</summary>
    public TimeSpan LoadTime { get; private set; }

    /// <summary>
    /// Smoke seam: called after every open with the full path and the <c>FileAccess.GetOpenError()</c> value just read, so a
    /// scenario can show that the error was checked.
    /// </summary>
    internal Action<string, Error>? OpenChecked { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// Reads <see cref="ManifestPath"/> as bytes and decodes it as strict UTF-8: CRLF and LF both end a line, blank lines are
    /// dropped, and the order is kept (the generator writes it ordinal-sorted; the host does not sort).
    /// </remarks>
    /// <exception cref="ContentHostReadException">The manifest cannot be opened, including when it does not exist.</exception>
    /// <exception cref="DecoderFallbackException">The manifest is not valid UTF-8.</exception>
    public ImmutableArray<string> ListManifest()
    {
        string path = ManifestPath;
        byte[] bytes = Open(path, out Error error);
        if (error != Error.Ok)
        {
            throw new ContentHostReadException(path, error);
        }

        ImmutableArray<string>.Builder lines = ImmutableArray.CreateBuilder<string>();
        foreach (string line in _utf8.GetString(bytes).Split('\n'))
        {
            string entry = line.TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(entry))
            {
                lines.Add(entry);
            }
        }

        return lines.ToImmutable();
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ContentHostReadException">The file exists but cannot be opened (any open error other than
    /// <see cref="Error.FileNotFound"/>).</exception>
    public bool TryRead(string path, out ReadOnlyMemory<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        string full = _prefix + path;
        byte[] data = Open(full, out Error error);
        if (error == Error.Ok)
        {
            bytes = data;
            return true;
        }

        bytes = ReadOnlyMemory<byte>.Empty;
        // TD-207 item 3: Mail Dev default, pending Evan: only FileNotFound returns false; every other open error throws.
        if (error != Error.FileNotFound)
        {
            throw new ContentHostReadException(full, error);
        }

        return false;
    }

    /// <summary>
    /// Loads the content once (CD21): <see cref="State"/> goes to <see cref="ContentHostState.Loading"/>,
    /// <see cref="ContentLoader.Load"/> runs over this host on the calling thread, and the host ends
    /// <see cref="ContentHostState.Valid"/> with <see cref="Set"/>, or <see cref="ContentHostState.Failed"/> with every
    /// finding in <see cref="Diagnostics"/> (a read failure, such as a missing manifest, is one finding naming the path). The
    /// wall time is measured and logged as one <c>[ContentHost] load</c> line against the 100 ms advisory budget. A failed
    /// host may load again; a valid one refuses (no re-parse, CDD-LOAD-4).
    /// </summary>
    /// <returns>The state the load ended in.</returns>
    /// <exception cref="InvalidOperationException">The host is not <see cref="ContentHostState.Unloaded"/> or
    /// <see cref="ContentHostState.Failed"/>; nothing changes.</exception>
    public ContentHostState Load()
    {
        // TD-207 item 1: Mail Dev default, pending Evan: a Valid or Loading host refuses the load; a Failed host may retry.
        if (State is not (ContentHostState.Unloaded or ContentHostState.Failed))
        {
            throw new InvalidOperationException($"ContentHost {Root} is {State}; the load is refused (no re-parse, CDD-LOAD-4).");
        }

        State = ContentHostState.Loading;
        Set = null;
        Result = null;
        Diagnostics = ImmutableArray<string>.Empty;
        long start = Stopwatch.GetTimestamp();
        try
        {
            var rules = RuleRegistry.CreateDefault();
            ContentLoadResult result = new ContentLoader(rules).Load(this);
            Result = result;
            // TD-207 item 5: Mail Dev default, pending Evan: formatted lines plus the raw Result; a code defect ends Failed and rethrows.
            Diagnostics = result.Diagnostics.Select(d => d.Format(rules)).ToImmutableArray();
            Set = result.Set;
            State = result.Set is null ? ContentHostState.Failed : ContentHostState.Valid;
        }
        catch (Exception ex) when (ex is ContentHostReadException or DecoderFallbackException)
        {
            Fail(ex);
        }
        catch (Exception ex)
        {
            // A code defect, not a content error: the host still ends Failed, and the caller sees the exception.
            Fail(ex);
            throw;
        }
        finally
        {
            LoadTime = Stopwatch.GetElapsedTime(start);
            double ms = LoadTime.TotalMilliseconds;

            // TD-207 item 7: Mail Dev default, pending Evan: a load over 100 ms is only logged as OVER and never fails.
            _log($"[ContentHost] load {Root}: {State} in {ms.ToString("0.0", CultureInfo.InvariantCulture)} ms " +
                $"(advisory budget {AdvisoryBudgetMs.ToString(CultureInfo.InvariantCulture)} ms{(ms > AdvisoryBudgetMs ? ", OVER" : string.Empty)}), " +
                $"{Diagnostics.Length} diagnostic(s)");
        }

        return State;
    }

    private void Fail(Exception ex)
    {
        Set = null;
        Diagnostics = [$"{ex.GetType().Name}: {ex.Message}"];
        State = ContentHostState.Failed;
    }

    private byte[] Open(string path, out Error error)
    {
        byte[] bytes = Godot.FileAccess.GetFileAsBytes(path);
        error = Godot.FileAccess.GetOpenError();
        OpenChecked?.Invoke(path, error);
        return bytes;
    }
}
