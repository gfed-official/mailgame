using System;
using System.Collections.Generic;
using Godot;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.App
{
    /// <summary>Reads content files through Godot's virtual file system, so it works from the editor and from an exported pack.</summary>
    public sealed class GodotContentSource : IContentSource
    {
        private readonly string _root;

        public GodotContentSource(string root = "res://content") { _root = root.TrimEnd('/'); }

        public string? ReadText(string relativePath)
        {
            string path = _root + "/" + relativePath;
            if (!FileAccess.FileExists(path)) return null;
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            return file?.GetAsText();
        }

        public IReadOnlyList<string> ListJson(string relativeDir)
        {
            var result = new List<string>();
            string dir = relativeDir.TrimEnd('/');
            using var access = DirAccess.Open(_root + "/" + dir);
            if (access == null) return result;
            foreach (string file in access.GetFiles())
                if (file.EndsWith(".json", StringComparison.Ordinal)) result.Add(dir + "/" + file);
            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}
