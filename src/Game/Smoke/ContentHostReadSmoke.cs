using Godot;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Content;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Story godot-host-shell/007 AC 1 and AC 2, and AC 3's success arm (ADR-0019 section 4, CD21). (1) A <see cref="ContentHost"/>
/// rooted at <c>res://</c> reads <c>project.godot</c>: true, non-empty, starting with <c>; Engine configuration file</c>,
/// byte-identical to <c>System.IO.File.ReadAllBytes</c>, and the <c>OpenChecked</c> seam saw the <c>Ok</c> open error;
/// <c>TryRead("does_not_exist.bin")</c> returns false with no bytes (approved by Evan 2026-11-01 (TD-204 item 4)), and opening
/// a folder (any other open error) throws <see cref="ContentHostReadException"/> with the path and the code. (2) The repo's
/// <c>content/**</c> files are copied to <c>user://content-host-fixture/</c> with a CRLF manifest that lists them (plus a blank
/// line) and a host rooted there goes <c>Unloaded → Valid</c> with no diagnostics and one logged load line; a second
/// <c>Load()</c> is refused and keeps the same set (approved by Evan 2026-11-01 (TD-204 item 5)). (3) Boot step 2 over a
/// <see cref="HostContentBootSource"/> on the fixture logs <c>source=ContentHost</c> and places the host's set.
/// </summary>
[SmokeScenario("content-host-read")]
public sealed class ContentHostReadSmoke : SmokeScenario
{
    private const string FixtureRoot = "user://content-host-fixture";

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        ReadProjectFile(ctx);
        IReadOnlyList<string> listed = WriteFixture();
        LoadFixture(ctx, listed);
        LoadThroughBootStep(ctx);
        ctx.Quit();
    }

    private static void ReadProjectFile(SmokeContext ctx)
    {
        var opens = new List<(string Path, Error Error)>();
        var host = new ContentHost("res://") { OpenChecked = (path, error) => opens.Add((path, error)) };

        bool found = host.TryRead("project.godot", out ReadOnlyMemory<byte> bytes);
        byte[] disk = File.ReadAllBytes(ProjectSettings.GlobalizePath("res://project.godot"));
        Expect(ctx, "TryRead(project.godot) returns", found, true);
        Expect(ctx, "project.godot bytes are non-empty", bytes.Length > 0, true);
        Expect(ctx, "project.godot starts with '; Engine configuration file'", bytes.Span.StartsWith("; Engine configuration file"u8), true);
        Expect(ctx, $"project.godot bytes ({bytes.Length}) equal File.ReadAllBytes ({disk.Length})", bytes.Span.SequenceEqual(disk), true);
        Expect(ctx, "open error checked once for project.godot", string.Join(", ", opens), "(res://project.godot, Ok)");

        opens.Clear();
        found = host.TryRead("does_not_exist.bin", out ReadOnlyMemory<byte> missing);
        Expect(ctx, "TryRead(does_not_exist.bin) returns", found, false);
        Expect(ctx, "TryRead(does_not_exist.bin) bytes length", missing.Length, 0);
        Expect(ctx, "open error checked once for does_not_exist.bin", string.Join(", ", opens), "(res://does_not_exist.bin, FileNotFound)");

        // A folder exists but cannot be opened as a file: an open error other than FileNotFound.
        opens.Clear();
        ContentHostReadException? thrown = null;
        try
        {
            host.TryRead("content", out _);
        }
        catch (ContentHostReadException ex)
        {
            thrown = ex;
        }

        Expect(ctx, "TryRead(content) (a folder) throws ContentHostReadException", thrown is not null, true);
        Expect(ctx, "the exception carries the path", thrown?.Path ?? "none", "res://content");
        Expect(ctx, "the exception carries the open error the seam saw", thrown?.Error.ToString() ?? "none", opens.Count == 1 ? opens[0].Error.ToString() : "no single open");
        ctx.Pass($"folder read: {thrown?.Message}");
    }

    // Copies content/** to the fixture folder and writes a CRLF manifest (with a trailing blank line) listing the copies.
    private static List<string> WriteFixture()
    {
        string source = ProjectSettings.GlobalizePath(ContentHost.DefaultRoot);
        string target = ProjectSettings.GlobalizePath(FixtureRoot);
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        List<string> paths = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(source, file).Replace('\\', '/'))
            .Where(path => path != ContentHost.ManifestName)
            .Order(StringComparer.Ordinal)
            .ToList();
        foreach (string path in paths)
        {
            string to = Path.Combine(target, path);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(Path.Combine(source, path), to);
        }

        File.WriteAllText(Path.Combine(target, ContentHost.ManifestName), string.Join("\r\n", paths) + "\r\n\r\n");
        return paths;
    }

    private static void LoadFixture(SmokeContext ctx, IReadOnlyList<string> listed)
    {
        var lines = new List<string>();
        var host = new ContentHost(FixtureRoot, lines.Add);
        Expect(ctx, "fixture: state before Load", host.State, ContentHostState.Unloaded);
        Expect(ctx, $"fixture: ListManifest keeps the {listed.Count} CRLF lines in order, blank line skipped", string.Join(",", host.ListManifest()), string.Join(",", listed));

        Expect(ctx, "fixture: Load() ends", host.Load(), ContentHostState.Valid);
        Expect(ctx, "fixture: State", host.State, ContentHostState.Valid);
        Expect(ctx, "fixture: Set present", host.Set is not null, true);
        Expect(ctx, $"fixture: diagnostics ({string.Join(" | ", host.Diagnostics)})", host.Diagnostics.Length, 0);
        Expect(ctx, "fixture: one load line logged", lines.Count, 1);
        Expect(ctx, $"fixture: load line '{lines.FirstOrDefault()}' names the state and the 100 ms advisory budget",
            lines.FirstOrDefault() is string line && line.StartsWith($"[ContentHost] load {FixtureRoot}: Valid in ", StringComparison.Ordinal)
                && line.Contains(" ms (advisory budget 100 ms", StringComparison.Ordinal),
            true);

        object? set = host.Set;
        ExpectThrows<InvalidOperationException>(ctx, "fixture: Load() while Valid is refused", () => host.Load());
        Expect(ctx, "fixture: State after the refused load", host.State, ContentHostState.Valid);
        Expect(ctx, "fixture: same set after the refused load", ReferenceEquals(host.Set, set), true);
        Expect(ctx, "fixture: no second load line (no re-parse)", lines.Count, 1);
    }

    private static void LoadThroughBootStep(SmokeContext ctx)
    {
        var source = new HostContentBootSource(new ContentHost(FixtureRoot));
        BootContext boot = ctx.Loop.BeginBootForSmoke();
        new ContentSourceStep(source).Run(boot);
        Expect(ctx, "boot step 2: BOOT 2 content line names source=ContentHost", BootSmokeFaults.Logged(boot.Log.Lines, "BOOT 2 content: source=ContentHost "), true);
        Expect(ctx, "boot step 2: BootContext.Content is the host's set", ReferenceEquals(boot.Content, source.Host.Set), true);
    }
}
