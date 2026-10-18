using System.Collections.Immutable;
using System.Text;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Sources;

/// <summary>
/// An <see cref="IContentSource"/> over files held in memory: the fixture source for tests and tools. Built once from
/// (path, bytes) pairs and immutable afterwards, so it is safe to share between threads.
/// </summary>
/// <example>
/// <code>
/// var source = InMemorySource.FromText(("defs/items.jsonc", "[]"), ("balance/mail.jsonc", "{}"));
/// ImmutableArray&lt;string&gt; paths = source.ListManifest();   // ["balance/mail.jsonc", "defs/items.jsonc"]
/// bool found = source.TryRead("defs/items.jsonc", out ReadOnlyMemory&lt;byte&gt; bytes);   // true, 2 bytes
/// </code>
/// </example>
public sealed class InMemorySource : IContentSource
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly ImmutableArray<string> _manifest;

    /// <summary>Creates a source from (path, bytes) pairs.</summary>
    /// <param name="files">The files; each path is content-relative and <c>/</c>-separated.</param>
    /// <exception cref="ArgumentException">A path occurs twice.</exception>
    public InMemorySource(params (string Path, byte[] Bytes)[] files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var paths = new string[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            (string path, byte[] bytes) = files[i];
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(bytes);
            if (!_files.TryAdd(path, bytes))
            {
                throw new ArgumentException("Duplicate content path: " + path, nameof(files));
            }

            paths[i] = path;
        }

        StableSort.Sort(paths.AsSpan(), new string[paths.Length], new OrdinalPathComparer());
        _manifest = ImmutableArray.Create(paths);
    }

    /// <summary>Creates a source from (path, UTF-8 text) pairs.</summary>
    /// <param name="files">The files; the text is encoded as UTF-8 without a BOM.</param>
    public static InMemorySource FromText(params (string Path, string Text)[] files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var converted = new (string Path, byte[] Bytes)[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            converted[i] = (files[i].Path, Encoding.UTF8.GetBytes(files[i].Text));
        }

        return new InMemorySource(converted);
    }

    /// <inheritdoc />
    public ImmutableArray<string> ListManifest() => _manifest;

    /// <inheritdoc />
    public bool TryRead(string path, out ReadOnlyMemory<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_files.TryGetValue(path, out byte[]? found))
        {
            bytes = found;
            return true;
        }

        bytes = default;
        return false;
    }
}
