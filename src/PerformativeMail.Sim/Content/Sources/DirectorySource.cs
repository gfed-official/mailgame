using System.Collections.Immutable;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Sources;

/// <summary>
/// An <see cref="IContentSource"/> over a folder on disk, using plain <c>System.IO</c> (ADR-0010 section 1). It lists every
/// <c>*.jsonc</c> file below the content root with <c>/</c>-separated relative paths in ordinal order, and reads bytes
/// without interpreting them. It never leaves the root: a path with a <c>..</c> segment or an absolute path reads as missing.
/// There is no manifest file; the folder is enumerated each call. Not thread-safe against concurrent changes to the folder.
/// </summary>
/// <example>
/// <code>
/// var source = new DirectorySource("/game/content");
/// foreach (string path in source.ListManifest())   // "balance/mail.jsonc", "defs/items.jsonc", ...
/// {
///     if (source.TryRead(path, out ReadOnlyMemory&lt;byte&gt; bytes)) { /* parse bytes */ }
/// }
/// </code>
/// </example>
public sealed class DirectorySource : IContentSource
{
    private const string Extension = ".jsonc";

    private readonly string _root;

    /// <summary>Creates a source over <paramref name="root"/>.</summary>
    /// <param name="root">The content root folder.</param>
    public DirectorySource(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        _root = Path.GetFullPath(root);
    }

    /// <inheritdoc />
    public ImmutableArray<string> ListManifest()
    {
        if (!Directory.Exists(_root))
        {
            return ImmutableArray<string>.Empty;
        }

        var paths = new List<string>();
        foreach (string file in Directory.EnumerateFiles(_root, "*" + Extension, SearchOption.AllDirectories))
        {
            // The OS pattern can match more than the exact extension on some platforms; keep only the exact ordinal suffix.
            if (!file.EndsWith(Extension, StringComparison.Ordinal))
            {
                continue;
            }

            string relative = Path.GetRelativePath(_root, file);
            paths.Add(Path.DirectorySeparatorChar == '/' ? relative : relative.Replace(Path.DirectorySeparatorChar, '/'));
        }

        string[] sorted = paths.ToArray();
        StableSort.Sort(sorted.AsSpan(), new string[sorted.Length], new OrdinalPathComparer());
        return ImmutableArray.Create(sorted);
    }

    /// <inheritdoc />
    public bool TryRead(string path, out ReadOnlyMemory<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        bytes = default;
        if (path.Length == 0 || Path.IsPathRooted(path))
        {
            return false;
        }

        string full = Path.GetFullPath(Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal) || !File.Exists(full))
        {
            return false;
        }

        try
        {
            bytes = File.ReadAllBytes(full);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }
}
