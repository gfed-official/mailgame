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
            Assert.StartsWith("content OK: 6 mail kinds, 4 destination types, 14 containers, 17 items, 2 maps", _out.ToString());
            Assert.Contains("25 shop entries, 6 resource nodes, 27 buildings, 4 vehicles, 3 kits, 8 stamps, 9 run events;", _out.ToString());
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
        public void AKitMustHandOutWhatExists_OneStackEach_TheDefaultFromRankOne()
        {
            File.WriteAllText(Content("kits/kits.json"), "[" +
                "{ \"id\": \"land\", \"name\": \"Land\", \"rank\": 2, \"personal\": [ { \"item\": \"crowbar\", \"count\": 1 } ], \"team\": [ { \"item\": \"log\", \"count\": 11 } ], \"teamVehicles\": [ \"tram\" ] }," +
                "{ \"id\": \"sea\", \"name\": \"Sea\", \"rank\": 0, \"team\": [] }]");
            Assert.Equal(Program.ExitInvalid, Run(_dir, "content"));
            string err = _err.ToString();
            Assert.Contains("'land': personal names unknown item 'crowbar'", err);
            Assert.Contains("'land': team 'log' count must be 1..10 (one stack)", err);
            Assert.Contains("'land': teamVehicles names unknown vehicle 'tram'", err);
            Assert.Contains("'land': the first kit is the default and must be open from rank 1", err);
            Assert.Contains("'sea': missing 'personal' array", err);
            Assert.Contains("'sea': rank must be >= 1", err);
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
            Assert.Contains("\"at\": [15.0, 12.8]", map);
            File.WriteAllText(Content("maps/test_street.json"), map.Replace(
                "{\"container\": \"chest\", \"at\": [15.0, 12.8], \"yawDegrees\": -90}",
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
        public void ASiegeEnemyWithoutItsProvocation_OrARaiderWithOne_IsAProblem()
        {
            string enemies = File.ReadAllText(Content("enemies/enemies.json"));
            Assert.Contains("\"provokeRange\": 5, \"provokeSeconds\": 3", enemies);
            Assert.Contains("\"traitParams\": { \"keepDistance\": 10 }", enemies);
            File.WriteAllText(Content("enemies/enemies.json"), enemies
                .Replace("\"provokeRange\": 5, \"provokeSeconds\": 3", "\"provokeRange\": 5")
                .Replace("\"traitParams\": { \"keepDistance\": 10 }", "\"traitParams\": { \"keepDistance\": 10, \"provokeRange\": 4, \"provokeSeconds\": 2 }"));
            Assert.Equal(Program.ExitInvalid, Run(_dir, "content"));
            string err = _err.ToString();
            Assert.Contains("'giant': a siege enemy, and only one, has traitParams.provokeRange and traitParams.provokeSeconds > 0", err);
            Assert.Contains("'archer': a siege enemy, and only one, has traitParams.provokeRange and traitParams.provokeSeconds > 0", err);
        }

        [Fact]
        public void AnAirborneEnemyWithoutItsAltitude_OrARaiderWithABomb_IsAProblem()
        {
            string enemies = File.ReadAllText(Content("enemies/enemies.json"));
            Assert.Contains("\"altitude\": 6, \"bombRadius\": 2", enemies);
            Assert.Contains("\"traitParams\": { \"keepDistance\": 10 }", enemies);
            File.WriteAllText(Content("enemies/enemies.json"), enemies
                .Replace("\"altitude\": 6, \"bombRadius\": 2", "\"bombRadius\": 2")
                .Replace("\"traitParams\": { \"keepDistance\": 10 }", "\"traitParams\": { \"keepDistance\": 10, \"altitude\": 4, \"bombRadius\": 1 }"));
            Assert.Equal(Program.ExitInvalid, Run(_dir, "content"));
            string err = _err.ToString();
            Assert.Contains("'balloon': an airborne enemy, and only one, has traitParams.altitude and traitParams.bombRadius > 0", err);
            Assert.Contains("'archer': an airborne enemy, and only one, has traitParams.altitude and traitParams.bombRadius > 0", err);
        }

        [Fact]
        public void SpikesWithoutDamage_OrAWallThatHurts_AreAProblem()
        {
            string buildings = File.ReadAllText(Content("buildings/buildings.json"));
            Assert.Contains("\"damagePerSecond\": 15", buildings);
            Assert.Contains("\"radius\": 0.25, \"height\": 2.2", buildings);
            File.WriteAllText(Content("buildings/buildings.json"), buildings
                .Replace("\"damagePerSecond\": 15", "\"damagePerSecond\": 0")
                .Replace("\"radius\": 0.25, \"height\": 2.2", "\"radius\": 0.25, \"height\": 2.2, \"damagePerSecond\": 5"));
            Assert.Equal(Program.ExitInvalid, Run(_dir, "content"));
            string err = _err.ToString();
            Assert.Contains("'spike_strip': spikes need params.damagePerSecond > 0", err);
            Assert.Contains("'wall_stone': only spikes do damage (params.damagePerSecond)", err);
        }

        [Fact]
        public void ABuildingNothingCanHonourOrARecipeThatDoesNotAddUpIsAProblem()
        {
            string buildings = File.ReadAllText(Content("buildings/buildings.json"));
            Assert.Contains("\"rotations\": 2, \"hp\": 300", buildings);
            Assert.Contains("\"radius\": 0.45", buildings);
            int end = buildings.LastIndexOf(']');
            File.WriteAllText(Content("buildings/buildings.json"), buildings.Substring(0, end)
                .Replace("\"rotations\": 2, \"hp\": 300", "\"rotations\": 3, \"hp\": 70000")
                .Replace("\"radius\": 0.45", "\"radius\": 0.8")
                + ", { \"id\": \"belt\", \"name\": \"Belt\", \"footprint\": [1, 1], \"hp\": 50, \"behaviour\": \"catapult\", \"params\": { \"radius\": 0.2, \"height\": 0.2 }, \"recipe\": \"recipe_belt\" }"
                + ", { \"id\": \"barn\", \"name\": \"Barn\", \"footprint\": [2, 2], \"hp\": 50, \"placement\": { \"onStreet\": true }, \"behaviour\": \"wall\", \"container\": \"chest\","
                + " \"params\": { \"radius\": 0.2, \"height\": 2 }, \"recipe\": \"recipe_barn\" }]");
            File.WriteAllText(Content("recipes/recipes.json"),
                "[ { \"id\": \"recipe_wall_wood\", \"produces\": { \"building\": \"wall_wood\" }, \"craftSeconds\": 2, \"blueprint\": \"axe\","
                + " \"inputs\": [ { \"item\": \"log\", \"count\": 3 }, { \"item\": \"log\", \"count\": 1 }, { \"item\": \"plank\", \"count\": 0 }, { \"item\": \"nails\", \"count\": 2 } ] },"
                + " { \"id\": \"recipe_planks\", \"produces\": { \"item\": \"log\" }, \"inputs\": [ { \"item\": \"log\", \"count\": 1 } ] },"
                + " { \"id\": \"recipe_shed\", \"produces\": { \"building\": \"shed\" }, \"inputs\": [ { \"item\": \"log\", \"count\": 1 } ] },"
                + " { \"id\": \"recipe_chest_2\", \"produces\": { \"building\": \"chest\" }, \"inputs\": [] } ]");
            Assert.Equal(Program.ExitInvalid, Run(_dir, "content"));
            string err = _err.ToString();
            Assert.Contains("'wall_wood': rotations must be 1, 2 or 4", err);
            Assert.Contains("'wall_wood': hp must be in 1..65535", err);
            Assert.Contains("'chest': params.radius must be in (0, 0.5]", err);
            Assert.Contains("'belt': behaviour 'catapult' is not built yet", err);
            Assert.Contains("'barn': a footprint of [1, 1] (a ramp: [1, 2], a sorter: [2, 2], a container [1, 1] or [2, 2], a vehicle depot [3, 3], a pier [1, 3], a port [3, 4])", err);
            Assert.Contains("'barn': only an elevated belt and an underground pipe are built on a street", err);
            Assert.Contains("'barn': only a building whose behaviour is 'container' or 'pump' has a container", err);
            Assert.Contains("'recipe_wall_wood': craftSeconds must be 0, placement is instant", err);
            Assert.Contains("'recipe_wall_wood': 'log' is an input twice", err);
            Assert.Contains("'recipe_wall_wood': the count of 'plank' must be in 1..255", err);
            Assert.Contains("'recipe_wall_wood': unknown input item 'nails'", err);
            Assert.Contains("'recipe_wall_wood': 'axe' is not a blueprint the shop sells", err);
            Assert.Contains("'recipe_planks': a recipe produces a building, never an item", err);
            Assert.Contains("'recipe_shed': produces unknown building 'shed'", err);
            Assert.Contains("'recipe_chest_2': a recipe needs at least one input", err);
            Assert.Contains("'recipe_chest_2': 'chest' names 'recipe_chest' as its recipe", err);
            Assert.Contains("'chest': its recipe 'recipe_chest' is not in content/recipes", err);
            Assert.Contains("'barn': its recipe 'recipe_barn' is not in content/recipes", err);
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
