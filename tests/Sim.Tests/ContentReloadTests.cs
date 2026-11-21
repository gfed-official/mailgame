using System;
using System.IO;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Hot reload (doc 07 §6): content files changed on disk are loaded again; the same defs take
    /// the new values, the sim reads its balance again and applies the perks taken again. A change
    /// of ids or maps is refused, and then nothing changes. Every test works on a copy of content/.
    /// </summary>
    public sealed class ContentReloadTests : IDisposable
    {
        private readonly string _dir;

        public ContentReloadTests()
        {
            string root = DirectoryContentSource.FindContentRoot(AppContext.BaseDirectory)!;
            _dir = Path.Combine(Path.GetTempPath(), "pm-reload-" + Guid.NewGuid().ToString("N"));
            Copy(root, _dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }

        private static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(from)) Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        private ContentDb Load() => ContentDb.Load(new DirectoryContentSource(_dir));

        private bool Reload(ContentDb db, out string problems)
        {
            bool ok = db.ReloadFrom(new DirectoryContentSource(_dir), out var list);
            problems = string.Join("\n", list);
            return ok;
        }

        private void Edit(string file, string from, string to)
        {
            string path = Path.Combine(_dir, file);
            string text = File.ReadAllText(path);
            Assert.Contains(from, text);
            File.WriteAllText(path, text.Replace(from, to));
        }

        [Fact]
        public void TheSameDefsTakeTheNewValues_DefsInThemStayTheLoadedOnes()
        {
            var db = Load();
            var bandage = db.Item("bandage");
            var log = db.Item("log");
            var wall = db.Building("wall_wood");
            var recipe = wall.Recipe;
            var bike = db.Vehicle("bike");
            var cargo = bike.Cargo;
            uint hash = db.ContentHash;
            Assert.Equal(50f, bandage.Heal);
            Assert.Equal(5f, db.Balance.GetFloat("walkSpeed"));

            Edit("items/materials.json", "\"buyPrice\": 30, \"heal\": 50", "\"buyPrice\": 30, \"heal\": 70");
            Edit("balance.json", "\"walkSpeed\": 5.0", "\"walkSpeed\": 6.0");
            Edit("recipes/recipes.json", "\"inputs\": [ { \"item\": \"log\", \"count\": 3 } ]", "\"inputs\": [ { \"item\": \"log\", \"count\": 2 } ]");
            Edit("vehicles/vehicles.json", "\"radius\": 0.45", "\"radius\": 0.5");

            Assert.True(Reload(db, out string problems), problems);
            Assert.Same(bandage, db.Item("bandage"));
            Assert.Equal(70f, bandage.Heal);
            Assert.Equal(6f, db.Balance.GetFloat("walkSpeed"));
            Assert.Equal(0.5f, bike.Radius);
            // The recipe is the loaded one, its input the loaded Log, with the new count.
            Assert.Same(recipe, wall.Recipe);
            Assert.Same(log, recipe!.Inputs[0].Item);
            Assert.Equal(2, recipe.Inputs[0].Count);
            Assert.Same(cargo, bike.Cargo);
            Assert.Same(db.Container(cargo.Id), bike.Cargo);
            Assert.Same(wall, recipe.Building);
            Assert.NotEqual(hash, db.ContentHash);
            Assert.Equal(Load().ContentHash, db.ContentHash);
        }

        [Fact]
        public void TheSameFilesAgain_ChangeNothing()
        {
            var db = Load();
            uint hash = db.ContentHash;
            var perk = db.Perk("long_legs");
            Assert.True(Reload(db, out string problems), problems);
            Assert.Equal(hash, db.ContentHash);
            Assert.Same(perk, db.Perk("long_legs"));
            Assert.Equal(1.12f, perk.Modifiers[0].Value, 4);
        }

        [Theory]
        [InlineData("items/materials.json", "\"heal\": 70 }", "\"heal\": 70 },\n  { \"id\": \"gauze\", \"name\": \"Gauze\", \"category\": \"material\", \"grid\": [1, 1], \"maxStack\": 5, \"weightClass\": \"light\", \"sellPrice\": 1 }", "Items: +gauze")]
        [InlineData("streets.json", "\"Larch Lane\",", "\"Larch Lane\", \"Gull Row\",", "StreetNames")]
        [InlineData("balance.json", "\"walkSpeed\": 5.0,", "\"walkSpeed\": \"fast\",", "walkSpeed")]
        public void AChangeThatNeedsARestart_OrBrokenContent_IsRefused_AndNothingChanges(string file, string from, string to, string named)
        {
            var db = Load();
            uint hash = db.ContentHash;
            var bandage = db.Item("bandage");
            Edit("items/materials.json", "\"buyPrice\": 30, \"heal\": 50", "\"buyPrice\": 30, \"heal\": 70");
            Edit(file, from, to);

            Assert.False(Reload(db, out string problems));
            Assert.Contains(named, problems);
            Assert.Equal(hash, db.ContentHash);
            Assert.Equal(50f, bandage.Heal);
            Assert.Same(bandage, db.Item("bandage"));
        }

        [Fact]
        public void TheSimReadsItsBalanceAgain_AndAppliesThePerksTakenAgain()
        {
            var db = Load();
            var world = new SimWorld(db, new SimSettings { Seed = 3, Draft = false, Raids = false });
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, world.GrantPerk(host, db.Perk("long_legs")));
            float before = Walk(world, host);
            Assert.Equal(5f * 1.12f, before, 1);
            host.Hp = 50f;

            Edit("balance.json", "\"walkSpeed\": 5.0", "\"walkSpeed\": 6.0");
            Edit("balance.json", "\"playerHp\": 100", "\"playerHp\": 200");
            Edit("perks/perks.json", "\"value\": 1.12", "\"value\": 1.5");
            Assert.True(Reload(db, out string problems), problems);
            world.ContentReloaded();

            Assert.Equal(6f, world.MoveConstants.WalkSpeed);
            Assert.Equal(6f * 1.5f, Walk(world, host), 1);
            // Half the health it had, of the new most.
            Assert.Equal((100f, 200f), (host.Hp, host.MaxHp));
            // A carrier who comes now has the new most.
            var guest = world.AddPlayer("guest")!;
            Assert.Equal(200f, guest.MaxHp);
        }

        /// <summary>Metres a second the carrier walks along the street: one second of walking east from its middle.</summary>
        private static float Walk(SimWorld world, Player p)
        {
            SimDriver.Teleport(p, 30f, 28f);
            SimDriver.StepTicks(world, 2);
            float x0 = p.Move.X;
            for (int i = 0; i < 30; i++)
            {
                world.QueueInput(p.Id, new PlayerInput { Seq = p.LastQueuedSeq + 1, MoveX = PlayerInput.QuantizeAxis(1f) });
                world.Step();
            }
            return p.Move.X - x0;
        }
    }
}
