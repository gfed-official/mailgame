using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Sources;

/// <summary>
/// The only way the content loader reads files (ADR-0010 section 1, TR-content-021). A source lists its content files and
/// serves their bytes; it never decides what a file means. The Sim ships <see cref="DirectorySource"/> (a folder on disk) and
/// <see cref="InMemorySource"/> (fixtures); the Godot host supplies its own implementation outside the Sim. Paths are
/// content-relative and <c>/</c>-separated. Implementations are not required to be thread-safe unless they say so.
/// </summary>
/// <example>
/// <code>
/// IContentSource source = new InMemorySource(("items.jsonc", "[]"u8.ToArray()));
/// foreach (string path in source.ListManifest())
/// {
///     if (source.TryRead(path, out ReadOnlyMemory&lt;byte&gt; bytes)) { /* parse bytes */ }
/// }
/// </code>
/// </example>
public interface IContentSource
{
    /// <summary>
    /// Lists every content file, as content-relative <c>/</c>-separated paths sorted by ordinal comparison, so two runs over
    /// the same content give the same order.
    /// </summary>
    ImmutableArray<string> ListManifest();

    /// <summary>
    /// Reads one file. A path the source does not have returns false; an empty file returns true with zero bytes, so a
    /// missing file is never confused with an empty one.
    /// </summary>
    /// <param name="path">A content-relative <c>/</c>-separated path, as listed by <see cref="ListManifest"/>.</param>
    /// <param name="bytes">The file bytes (empty when the method returns false).</param>
    /// <returns>True when the file exists.</returns>
    bool TryRead(string path, out ReadOnlyMemory<byte> bytes);
}
