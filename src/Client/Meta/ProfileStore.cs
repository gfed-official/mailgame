using System;
using System.IO;

namespace PerformativeMail.Client.Meta
{
    /// <summary>Where the profile text is kept. The game uses a file; tests use memory.</summary>
    public interface IProfileStore
    {
        /// <summary>The stored text, or null when there is none yet.</summary>
        string? Read();
        void Write(string json);
        /// <summary>Puts unreadable contents aside so that a fresh profile does not overwrite them.</summary>
        void SetAside();
    }

    /// <summary>The profile as a file (doc 07 §7.1: <c>user://profile.json</c>).</summary>
    public sealed class FileProfileStore : IProfileStore
    {
        public string Path { get; }

        public FileProfileStore(string path) { Path = path; }

        public string? Read() => File.Exists(Path) ? File.ReadAllText(Path) : null;

        /// <summary>Writes beside the file and swaps it in, so a crash mid-write leaves the old profile whole.</summary>
        public void Write(string json)
        {
            string? dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string temp = Path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, Path, overwrite: true);
        }

        public void SetAside()
        {
            if (File.Exists(Path)) File.Move(Path, Path + ".bad", overwrite: true);
        }
    }

    public sealed class MemoryProfileStore : IProfileStore
    {
        public string? Json;
        public string? Bad;
        public int Writes;

        public string? Read() => Json;

        public void Write(string json)
        {
            Json = json;
            Writes++;
        }

        public void SetAside()
        {
            Bad = Json;
            Json = null;
        }
    }
}
