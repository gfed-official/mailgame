using Godot;

namespace PerformativeMail.Game.Content;

/// <summary>
/// <see cref="ContentHost"/> could not open a content file for a reason other than the file not being there
/// (<c>FileAccess.GetOpenError()</c> returned something other than <see cref="Error.Ok"/> or <see cref="Error.FileNotFound"/>),
/// or the mandatory <c>manifest.txt</c> is missing. Carries the full Godot path and the error code, so a failed read is
/// never mistaken for an empty file (approved by Evan 2026-11-01 (TD-204 item 4)).
/// </summary>
/// <example>
/// <code>
/// try { host.ListManifest(); }
/// catch (ContentHostReadException e) { GD.PrintErr($"{e.Path}: {e.Error}"); }
/// </code>
/// </example>
public sealed class ContentHostReadException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="path">The full Godot path that failed to open (for example <c>res://content/manifest.txt</c>).</param>
    /// <param name="error">The error <c>FileAccess.GetOpenError()</c> returned.</param>
    /// <example><code>throw new ContentHostReadException("res://content/manifest.txt", Error.FileNotFound);</code></example>
    public ContentHostReadException(string path, Error error)
        : base($"cannot read {path}: {error} (Godot Error {(int)error})")
    {
        Path = path;
        Error = error;
    }

    /// <summary>The full Godot path that failed to open.</summary>
    public string Path { get; }

    /// <summary>The open error.</summary>
    public Error Error { get; }
}
