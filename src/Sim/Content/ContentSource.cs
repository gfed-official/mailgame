using System;
using System.Collections.Generic;
using System.IO;

namespace PerformativeMail.Sim.Content
{
    /// <summary>Abstracts where content files come from (disk, a pack, an in-memory test fixture).</summary>
    public interface IContentSource
    {
        /// <summary>Returns the file text, or null if it does not exist. Path is relative, '/' separated.</summary>
        string? ReadText(string relativePath);

        /// <summary>Lists *.json files in a directory (relative paths, sorted ordinal).</summary>
        IReadOnlyList<string> ListJson(string relativeDir);
    }

    public sealed class DirectoryContentSource : IContentSource
    {
        public string Root { get; }

        public DirectoryContentSource(string root) { Root = root; }

        public string? ReadText(string relativePath)
        {
            string path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        public IReadOnlyList<string> ListJson(string relativeDir)
        {
            string dir = Path.Combine(Root, relativeDir.Replace('/', Path.DirectorySeparatorChar));
            var result = new List<string>();
            if (!Directory.Exists(dir)) return result;
            foreach (string file in Directory.GetFiles(dir, "*.json"))
                result.Add(relativeDir.TrimEnd('/') + "/" + Path.GetFileName(file));
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>Walks up from a start directory until a folder containing content/balance.json is found.</summary>
        public static string? FindContentRoot(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "content");
                if (File.Exists(Path.Combine(candidate, "balance.json"))) return candidate;
                dir = dir.Parent;
            }
            return null;
        }
    }

    public sealed class MemoryContentSource : IContentSource
    {
        public Dictionary<string, string> Files { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public string? ReadText(string relativePath) => Files.TryGetValue(relativePath, out var t) ? t : null;

        public IReadOnlyList<string> ListJson(string relativeDir)
        {
            string prefix = relativeDir.TrimEnd('/') + "/";
            var result = new List<string>();
            foreach (string key in Files.Keys)
                if (key.StartsWith(prefix, StringComparison.Ordinal) && key.EndsWith(".json", StringComparison.Ordinal)
                    && key.IndexOf('/', prefix.Length) < 0)
                    result.Add(key);
            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}
