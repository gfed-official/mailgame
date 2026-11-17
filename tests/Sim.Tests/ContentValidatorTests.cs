using System;
using System.IO;
using PerformativeMail.Sim.Content;
using PerformativeMail.Tools.ContentValidator;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>tools/ContentValidator (doc 07 §6): the command CI runs over content/.</summary>
    public sealed class ContentValidatorTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "pm-content-" + Guid.NewGuid().ToString("N"));
        private readonly StringWriter _out = new StringWriter(), _err = new StringWriter();

        public ContentValidatorTests()
        {
            // A copy of the real content to break.
            string real = DirectoryContentSource.FindContentRoot(AppContext.BaseDirectory)!;
            foreach (string file in Directory.GetFiles(real, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(_dir, "content", Path.GetRelativePath(real, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        private string Content(string relative) => Path.Combine(_dir, "content", relative);

        private int Run(string workingDir, params string[] args) => Program.Run(args, workingDir, _out, _err);

        [Fact]
        public void TheRealContentIsValid()
        {
            Assert.Equal(Program.ExitOk, Run(_dir, "content"));
            Assert.StartsWith("content OK: 6 mail kinds, 4 destination types, 10 containers, 11 items, 1 maps", _out.ToString());
            Assert.Contains($"content hash {TestContent.Db.ContentHash:x8}", _out.ToString());
            Assert.Equal("", _err.ToString());
        }

        [Fact]
        public void WithoutADirectoryItFindsContentAboveTheWorkingDirectory()
        {
            string deep = Path.Combine(_dir, "tools", "somewhere");
            Directory.CreateDirectory(deep);
            Assert.Equal(Program.ExitOk, Run(deep, "--quiet"));
            Assert.Equal("", _out.ToString());
        }

        [Fact]
        public void EveryProblemIsListedAndTheExitCodeIsOne()
        {
            // A duplicate id, a reference to nothing, a mix that does not add up, and a file with a misspelt name.
            string kinds = File.ReadAllText(Content("mail/kinds.json"));
            int end = kinds.LastIndexOf(']');
            File.WriteAllText(Content("mail/kinds.json"), kinds.Substring(0, end)
                + ", { \"id\": \"letter\", \"name\": \"Letter again\", \"grid\": [1, 1], \"maxStack\": 20, \"baseValue\": 8, \"weightClass\": \"light\", \"acceptedBy\": [\"house\"] }"
                + ", { \"id\": \"telegram\", \"name\": \"Telegram\", \"grid\": [1, 0], \"maxStack\": 20, \"baseValue\": 8, \"weightClass\": \"light\", \"acceptedBy\": [\"pigeon_loft\"] }]");
            File.WriteAllText(Content("mail/mix.json"), File.ReadAllText(Content("mail/mix.json")).Replace("\"letter\": 0.", "\"letter\": 1."));
            File.Copy(Content("items/materials.json"), Content("items/materails.json.bak.json"));
            File.WriteAllText(Content("perks.json"), "[]");

            Assert.Equal(Program.ExitInvalid, Run(_dir, "content"));

            string err = _err.ToString();
            Assert.Contains("error: mail/kinds.json: duplicate id 'letter'", err);
            Assert.Contains("'telegram': grid sizes must be positive", err);
            Assert.Contains("acceptedBy references unknown destination type 'pigeon_loft'", err);
            Assert.Contains("expected 1.0 ± 0.001", err);
            Assert.Contains("duplicate id 'log'", err); // the copied materials file is read, and collides
            Assert.Contains("error: perks.json: not read by any loader", err);
            Assert.Contains("content INVALID:", err);
            Assert.Equal("", _out.ToString());
        }

        [Fact]
        public void BrokenJsonIsAProblemNotACrash()
        {
            File.WriteAllText(Content("balance.json"), "{ \"walkSpeed\": ");
            Assert.Equal(Program.ExitInvalid, Run(_dir, "content"));
            Assert.Contains("error: balance.json", _err.ToString());
        }

        [Fact]
        public void AChestInAWallOrOfAnUnknownKindIsAProblem()
        {
            string map = File.ReadAllText(Content("maps/test_street.json"));
            Assert.Contains("\"at\": [12.7, 14.6]", map);
            File.WriteAllText(Content("maps/test_street.json"), map.Replace(
                "{\"container\": \"chest\", \"at\": [12.7, 14.6], \"yawDegrees\": -90}",
                "{\"container\": \"chest\", \"at\": [8.0, 14.6]}, {\"container\": \"crate\", \"at\": [14.0, 14.6]}, "
                + "{\"container\": \"po_intake\", \"at\": [14.0, 16.0]}, {\"container\": \"chest\", \"at\": [30.0, 27.0]}"));
            Assert.Equal(Program.ExitInvalid, Run(_dir, "content"));
            string err = _err.ToString();
            Assert.Contains("chest 0 is inside a solid tile", err);
            Assert.Contains("unknown container 'crate'", err);
            Assert.Contains("container 'po_intake' is not a storage grid", err);
            Assert.Contains("stands on a street", err);
        }

        [Fact]
        public void AWrongCommandIsExitCodeTwo()
        {
            Assert.Equal(Program.ExitUsage, Run(_dir, "no-such-dir"));
            Assert.Equal(Program.ExitUsage, Run(_dir, "content", "--frobnicate"));
            Assert.Equal(Program.ExitUsage, Run(Path.GetTempPath(), Array.Empty<string>()));
            Assert.Contains("is not a directory", _err.ToString());
            Assert.Contains("unexpected argument '--frobnicate'", _err.ToString());
            Assert.Contains("no content/ directory", _err.ToString());
        }
    }
}
