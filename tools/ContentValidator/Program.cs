using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.Tools.ContentValidator
{
    /// <summary>
    /// The content check of doc 07 §6 as a command (CI runs it): loads a content/ directory through
    /// the same <see cref="ContentDb.TryLoad"/> the game boots with and lists every problem.
    ///   ContentValidator [CONTENT_DIR] [--quiet]
    /// Without a directory, the content/ found by walking up from the working directory.
    /// Exit code 0: valid; 1: problems found; 2: the command itself was wrong.
    /// </summary>
    public static class Program
    {
        public const int ExitOk = 0, ExitInvalid = 1, ExitUsage = 2;

        public static int Main(string[] args) => Run(args, Directory.GetCurrentDirectory(), Console.Out, Console.Error);

        public static int Run(string[] args, string workingDir, TextWriter output, TextWriter errors)
        {
            string? dir = null;
            bool quiet = false;
            foreach (string arg in args)
            {
                if (arg == "--quiet" || arg == "-q") quiet = true;
                else if (arg == "--help" || arg == "-h")
                {
                    output.WriteLine("usage: ContentValidator [CONTENT_DIR] [--quiet]");
                    output.WriteLine("  Checks the game's content files; exit code 0 if valid, 1 if not.");
                    return ExitOk;
                }
                else if (arg.StartsWith('-') || dir != null)
                {
                    errors.WriteLine($"ContentValidator: unexpected argument '{arg}' (usage: ContentValidator [CONTENT_DIR] [--quiet])");
                    return ExitUsage;
                }
                else dir = arg;
            }

            string? root = dir != null ? Path.GetFullPath(dir, workingDir) : DirectoryContentSource.FindContentRoot(workingDir);
            if (root == null)
            {
                errors.WriteLine($"ContentValidator: no content/ directory with a balance.json found above '{workingDir}'");
                return ExitUsage;
            }
            if (!Directory.Exists(root))
            {
                errors.WriteLine($"ContentValidator: '{root}' is not a directory");
                return ExitUsage;
            }

            var source = new RecordingSource(new DirectoryContentSource(root));
            var db = ContentDb.TryLoad(source, out var problems);

            // A file nothing reads is a mistake waiting to be made: a misspelt name, or a def type without a loader.
            foreach (string file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                if (!source.Read.Contains(relative)) problems.Add($"{relative}: not read by any loader");
            }

            if (problems.Count > 0)
            {
                foreach (string problem in problems) errors.WriteLine("error: " + problem);
                errors.WriteLine($"content INVALID: {problems.Count} {(problems.Count == 1 ? "problem" : "problems")} in {root}");
                return ExitInvalid;
            }
            if (!quiet)
            {
                output.WriteLine($"content OK: {db.MailKinds.Count} mail kinds, {db.DestinationTypes.Count} destination types, {db.Containers.Count} containers, "
                    + $"{db.Items.Count} items, {db.Maps.Count} maps, {db.StreetNames.Count} street names, {db.Enemies.Count} enemies, {db.Perks.Count} perks, {db.ShopItems.Count} shop entries, {db.Nodes.Count} resource nodes, {db.Buildings.Count} buildings, {db.Vehicles.Count} vehicles, {db.Kits.Count} kits, {db.Stamps.Count} stamps, {db.RunEvents.Count} run events; content hash {db.ContentHash:x8} ({root})");
            }
            return ExitOk;
        }

        /// <summary>Remembers which files the loaders asked for and got.</summary>
        private sealed class RecordingSource : IContentSource
        {
            private readonly IContentSource _inner;

            public readonly HashSet<string> Read = new HashSet<string>(StringComparer.Ordinal);

            public RecordingSource(IContentSource inner) { _inner = inner; }

            public string? ReadText(string relativePath)
            {
                string? text = _inner.ReadText(relativePath);
                if (text != null) Read.Add(relativePath);
                return text;
            }

            public IReadOnlyList<string> ListJson(string relativeDir) => _inner.ListJson(relativeDir);
        }
    }
}
