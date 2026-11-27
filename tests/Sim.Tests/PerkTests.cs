using System;
using System.IO;
using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Perks (doc 01 §2.5, doc 08 §2.9): the stat sheet, what content/perks may say, who may hold
    /// what, and what each perk of the content does.
    /// </summary>
    public class PerkTests
    {
        private const int Tps = 30;
        private const float East = -MathF.PI / 2f;

        private uint _nextRequest = 1;
        private uint _nextMailId = 900000;

        private static ContentDb Db => TestContent.Db;

        private static SimWorld NewWorld(uint seed = 7, bool raids = true, ContentDb? content = null) =>
            new SimWorld(content ?? Db, new SimSettings { Seed = seed, Raids = raids, Draft = false });

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static void RunUntil(SimWorld world, Func<bool> done, int maxSeconds)
        {
            for (int i = 0; i < maxSeconds * Tps && !done(); i++) world.Step();
            Assert.True(done(), $"still in {world.Run.Phase} of shift {world.Run.Shift} after {maxSeconds} s");
        }

        /// <summary>Starts the run; perks are given from here on, a run begins without any.</summary>
        private void StartRun(SimWorld world, Player host)
        {
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
        }

        private void ReadyUp(SimWorld world)
        {
            foreach (var p in world.Players) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
        }

        /// <summary>A world in the Delivery of shift 1 with one carrier holding these perks.</summary>
        private Player InDelivery(SimWorld world, params string[] perks)
        {
            var p = world.AddPlayer("carrier")!;
            StartRun(world, p);
            foreach (string id in perks) Assert.Equal(OpResult.Ok, world.GrantPerk(p, world.Content.Perk(id)));
            ReadyUp(world);
            return p;
        }

        /// <summary>Hands the player one item worth <paramref name="value"/> ¢ and has them put it in a mailbox.</summary>
        private OpResult Deliver(SimWorld world, Player p, int value, bool wrong = false, string kind = "letter", int? deadlineShift = null, int wait = 15)
        {
            var home = world.Map.Destinations[0];
            var target = wrong ? world.Map.Destinations[1] : home;
            var item = TestContent.Mail(kind, home.Address, _nextMailId++, world.Run.Shift);
            item.Value = (ushort)value;
            if (deadlineShift != null) item.DeadlineShift = (byte)deadlineShift.Value;
            Assert.True(world.Give(p.Hotbar, ItemStack.OfMail(item)));
            SimDriver.Teleport(p, target.MailboxX, target.MailboxZ);
            SimDriver.StepTicks(world, wait);
            var entry = p.Hotbar.Entries.Single();
            var result = Request(world, p, SimRequest.Deliver(0, p.Hotbar.Id, entry.EntryId, target.Id));
            if (result != OpResult.Ok) p.Hotbar.Remove(entry.EntryId);
            return result;
        }

        private static void Press(SimWorld world, Player p, InputButtons buttons, float yaw = East)
        {
            world.QueueInput(p.Id, new PlayerInput { Seq = p.LastQueuedSeq + 1, Yaw = PlayerInput.QuantizeYaw(yaw), Buttons = buttons });
            world.Step();
        }

        /// <summary>The real content with another perks file.</summary>
        private static MemoryContentSource SourceWithPerks(string perksJson)
        {
            string root = DirectoryContentSource.FindContentRoot(AppContext.BaseDirectory)!;
            var source = new MemoryContentSource();
            foreach (string file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                source.Files[Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')] = File.ReadAllText(file);
            foreach (string key in source.Files.Keys.Where(k => k.StartsWith("perks/", StringComparison.Ordinal)).ToList())
                source.Files.Remove(key);
            source.Files["perks/perks.json"] = perksJson;
            return source;
        }

        private static string PerkJson(string id, string scope, string modifiers, string rest = "") =>
            $"{{ \"id\": \"{id}\", \"name\": \"{id}\", \"description\": \"-\", \"category\": \"carrier\", \"scope\": \"{scope}\", "
            + $"\"rarity\": \"common\", \"modifiers\": [{modifiers}]{(rest.Length > 0 ? ", " + rest : "")} }}";

        private const string SpeedMod = "{ \"stat\": \"PlayerSpeed\", \"op\": \"mul\", \"value\": 1.1 }";

        // --- the sheet --------------------------------------------------------------------------

        [Fact]
        public void AStatIsItsBaseTimesTheMultipliersPlusTheAdders()
        {
            var sheet = new StatSheet();
            Assert.Equal(10f, sheet.Resolve(Stat.RespawnSeconds, 10f));

            var legs = Db.Perk("long_legs");
            sheet.Apply(legs);
            sheet.Apply(legs);
            Assert.Equal(1.12f * 1.12f, sheet.Resolve(Stat.PlayerSpeed, 1f), 5);
            Assert.Equal(10f, sheet.Resolve(Stat.RespawnSeconds, 10f));

            // "mul 0, add 3" is how a perk sets a value outright.
            sheet.Apply(Db.Perk("second_wind"));
            Assert.Equal(3f, sheet.Resolve(Stat.RespawnSeconds, 10f));
            Assert.Equal(1f, sheet.Resolve(Stat.KeepHotbarOnDeath, 0f));

            // The team's sheet and a carrier's own over one base.
            var team = new StatSheet();
            team.Apply(Db.Perk("union_muscle"));
            Assert.Equal(37.5f, team.Resolve(Stat.MeleeDamage, 25f, sheet));
            Assert.Equal(1.12f * 1.12f, team.Resolve(Stat.PlayerSpeed, 1f, sheet), 5);

            sheet.Clear();
            Assert.Equal(1f, sheet.Resolve(Stat.PlayerSpeed, 1f));
            Assert.Equal(10f, sheet.Resolve(Stat.RespawnSeconds, 10f));
        }

        // --- content ----------------------------------------------------------------------------

        [Fact]
        public void TheContentHasThePerksOfDoc10_TeamAndPersonal()
        {
            Assert.Equal(new[]
            {
                "long_legs", "quick_hands", "strong_back", "second_wind", "local_knowledge", "postal_sprint",
                "express_lane", "greased_inserters", "presort",
                "union_rep", "stamp_collector", "fragile_handling", "grace_period", "priority_post", "second_district",
                "early_warning", "union_muscle", "reinforced", "hot_barrels", "insured",
            }, Db.Perks.Select(p => p.Id));
            for (int i = 0; i < Db.Perks.Count; i++) Assert.Equal(i, Db.Perks[i].Index);
            Assert.Equal(6, Db.Perks.Count(p => !p.Team));
            Assert.All(Db.Perks, p => Assert.True(p.Modifiers.Count + p.Rules.Count > 0, p.Id));
            // Doc 12 M4: the rule-flag perks are data over the closed RuleFlag set.
            Assert.Equal(new[] { RuleFlag.InsuredRebuild }, Db.Perk("insured").Rules);
            Assert.Equal(new[] { "bp_turret" }, Db.Perk("hot_barrels").BlueprintAny);
            Assert.All(Db.Perks, p => Assert.False(string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Description)));
            Assert.All(Db.Perks.Where(p => p.Team), p => Assert.Equal(1, p.MaxStacks));
            // A personal perk never touches a number that is the whole team's.
            Assert.All(Db.Perks.Where(p => !p.Team), p => Assert.All(p.Modifiers, m => Assert.False(Stats.IsTeam(m.Stat))));

            var legs = Db.Perk("long_legs");
            Assert.Equal((PerkCategory.Carrier, PerkRarity.Common, 3), (legs.Category, legs.Rarity, legs.MaxStacks));
            Assert.Equal((PerkCategory.Defense, PerkRarity.Uncommon, true), (Db.Perk("union_muscle").Category, Db.Perk("union_muscle").Rarity, Db.Perk("union_muscle").Team));
            Assert.Equal(PerkRarity.Rare, Db.Perk("postal_sprint").Rarity);
        }

        [Fact]
        public void EveryStatHasItsNameAndNoOther()
        {
            foreach (Stat stat in Enum.GetValues(typeof(Stat)))
            {
                Assert.True(Stats.TryParse(stat.ToString(), out var parsed));
                Assert.Equal(stat, parsed);
            }
            Assert.False(Stats.TryParse("playerSpeed", out _));
            Assert.False(Stats.TryParse("PlayerSpeed, Quota", out _));
            Assert.False(Stats.TryParse("3", out _));
            Assert.False(Stats.IsTeam(Stat.MeleeKnockback));
            Assert.True(Stats.IsTeam(Stat.Quota));
            Assert.True(Stats.IsTeam(Stat.RaidWarningSeconds));
        }

        [Fact]
        public void APerksFileMayOnlyNameWhatExists()
        {
            string json = "[" + string.Join(",\n",
                PerkJson("ok", "personal", SpeedMod, "\"excludes\": [\"also_ok\"], \"maxStacks\": 2, \"prerequisites\": { \"shiftMin\": 2 }"),
                PerkJson("also_ok", "team", "{ \"stat\": \"Quota\", \"op\": \"mul\", \"value\": 0.9 }"),
                PerkJson("ok", "personal", SpeedMod),
                PerkJson("no_stat", "personal", "{ \"stat\": \"Luck\", \"op\": \"mul\", \"value\": 2 }"),
                PerkJson("no_op", "personal", "{ \"stat\": \"PlayerSpeed\", \"op\": \"pow\", \"value\": 2 }"),
                PerkJson("team_stat", "personal", "{ \"stat\": \"Quota\", \"op\": \"mul\", \"value\": 0.5 }"),
                PerkJson("negative", "personal", "{ \"stat\": \"PlayerSpeed\", \"op\": \"mul\", \"value\": -1 }"),
                PerkJson("nothing", "personal", ""),
                PerkJson("no_scope", "squad", SpeedMod),
                PerkJson("flag", "team", SpeedMod, "\"rules\": [\"BeltsIgnoreJams\"]"),
                PerkJson("recipe", "team", SpeedMod, "\"unlocks\": [{ \"recipe\": \"express_belt\" }]"),
                PerkJson("built", "team", SpeedMod, "\"prerequisites\": { \"builtAny\": [\"belt\"] }"),
                PerkJson("stranger", "team", SpeedMod, "\"excludes\": [\"nobody\"]"),
                PerkJson("selfish", "team", SpeedMod, "\"excludes\": [\"selfish\"]"),
                PerkJson("team_stack", "team", SpeedMod, "\"maxStacks\": 2"),
                PerkJson("no_stack", "personal", SpeedMod, "\"maxStacks\": 0")) + "]";

            var db = ContentDb.TryLoad(SourceWithPerks(json), out var errors);
            string all = string.Join("\n", errors);
            Assert.Contains("perks/perks.json: duplicate id 'ok'", all);
            Assert.Contains("'no_stat': unknown stat 'Luck'", all);
            Assert.Contains("'no_op': modifier op must be 'mul' or 'add', not 'pow'", all);
            Assert.Contains("'team_stat': a personal perk cannot change the team stat Quota", all);
            Assert.Contains("'negative': the multiplier on PlayerSpeed must not be negative", all);
            Assert.Contains("'nothing': a perk without a modifier or a rule flag does nothing", all);
            Assert.Contains("'no_scope': unknown scope 'squad'", all);
            Assert.Contains("'flag': unknown rule flag 'BeltsIgnoreJams'", all);
            Assert.Contains("'recipe': unlocks references unknown recipe 'express_belt'", all);
            Assert.Contains("'built'", all);
            Assert.Contains("unknown building 'belt'", all);
            Assert.Contains("'stranger'", all);
            Assert.Contains("'nobody'", all);
            Assert.Contains("'selfish'", all);
            Assert.Contains("'team_stack'", all);
            Assert.Contains("'no_stack'", all);
            // The two that are right raise nothing, and are there.
            Assert.DoesNotContain("'also_ok'", all);
            Assert.Equal((2, 2, false), (db.Perk("ok").MaxStacks, db.Perk("ok").ShiftMin, db.Perk("ok").Team));
            Assert.True(db.Perk("ok").ConflictsWith(db.Perk("also_ok")));
            Assert.True(db.Perk("also_ok").ConflictsWith(db.Perk("ok")));
        }

        [Fact]
        public void ThePerksAreInTheContentHash()
        {
            string root = DirectoryContentSource.FindContentRoot(AppContext.BaseDirectory)!;
            string real = File.ReadAllText(Path.Combine(root, "perks", "perks.json"));
            var same = ContentDb.Load(SourceWithPerks(real));
            var other = ContentDb.Load(SourceWithPerks(real.Replace("1.12", "1.13")));
            Assert.Equal(Db.ContentHash, same.ContentHash);
            Assert.NotEqual(Db.ContentHash, other.ContentHash);
        }

        // --- who holds what -----------------------------------------------------------------------

        [Fact]
        public void APersonalPerkIsItsHolders_AndStacksAsOftenAsItSays()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a")!;
            var b = world.AddPlayer("b")!;
            StartRun(world, a);
            var legs = Db.Perk("long_legs");

            Assert.Equal(OpResult.Ok, world.GrantPerk(a, legs));
            Assert.Equal(1.12f, a.SpeedMult, 5);
            Assert.Equal(1f, b.SpeedMult);
            Assert.Equal(OpResult.Ok, world.GrantPerk(a, legs));
            Assert.Equal(OpResult.Ok, world.GrantPerk(a, legs));
            Assert.Equal(1.12f * 1.12f * 1.12f, a.SpeedMult, 5);
            Assert.Equal(3, SimWorld.Stacks(a, legs));
            Assert.Equal(OpResult.PerkMaxed, world.GrantPerk(a, legs));
            Assert.Equal(3, a.Perks.Count);
            // Several carriers may hold the same personal perk.
            Assert.Equal(OpResult.Ok, world.GrantPerk(b, legs));
            Assert.Empty(world.TeamPerks);

            Assert.Equal(OpResult.Ok, world.GrantPerk(a, Db.Perk("quick_hands")));
            Assert.Equal(OpResult.PerkMaxed, world.GrantPerk(a, Db.Perk("quick_hands")));
        }

        [Fact]
        public void ATeamPerkIsEverybodysOnce_AlsoWhoJoinsLater()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a")!;
            var b = world.AddPlayer("b")!;
            StartRun(world, a);
            var muscle = Db.Perk("union_muscle");

            Assert.Equal(OpResult.Ok, world.GrantPerk(a, muscle));
            Assert.Equal(OpResult.PerkTaken, world.GrantPerk(b, muscle));
            Assert.Equal(new[] { muscle }, world.TeamPerks);
            Assert.Empty(a.Perks);

            // Prep still takes players in; the newcomer swings as hard as the rest.
            var late = world.AddPlayer("late")!;
            var raider = Db.Enemy("barbarian");
            foreach (var p in new[] { a, b, late })
            {
                SimDriver.Teleport(p, 50f, 28f);
                var target = world.SpawnEnemy(raider, 52f, 28f)!;
                Press(world, p, InputButtons.Attack);
                Assert.Equal(raider.Hp - 37.5f, target.Hp);
                target.Hp = 0.01f;
                world.DamageEnemy(target, 1f, null);
            }
        }

        [Fact]
        public void PerksThatExcludeEachOtherNeverShareARun()
        {
            string json = "[" + string.Join(",",
                PerkJson("sprinter", "personal", SpeedMod, "\"excludes\": [\"plodder\", \"marching_band\"]"),
                PerkJson("plodder", "personal", "{ \"stat\": \"WeightPenalty\", \"op\": \"mul\", \"value\": 0.5 }"),
                PerkJson("marching_band", "team", "{ \"stat\": \"Quota\", \"op\": \"mul\", \"value\": 0.9 }"),
                PerkJson("loner", "team", "{ \"stat\": \"Quota\", \"op\": \"mul\", \"value\": 0.8 }")) + "]";
            var db = ContentDb.Load(SourceWithPerks(json));

            var world = NewWorld(content: db);
            var a = world.AddPlayer("a")!;
            var b = world.AddPlayer("b")!;
            StartRun(world, a);
            Assert.Equal(OpResult.Ok, world.GrantPerk(a, db.Perk("sprinter")));
            // The exclusion holds whichever of the two names the other, for the one who holds it...
            Assert.Equal(OpResult.PerkExcluded, world.GrantPerk(a, db.Perk("plodder")));
            Assert.Equal(OpResult.Ok, world.GrantPerk(b, db.Perk("plodder")));
            Assert.Equal(OpResult.PerkExcluded, world.GrantPerk(b, db.Perk("sprinter")));
            // ...and a team perk is everybody's, so anybody's perk can stand in its way.
            Assert.Equal(OpResult.PerkExcluded, world.GrantPerk(b, db.Perk("marching_band")));
            Assert.Equal(OpResult.Ok, world.GrantPerk(b, db.Perk("loner")));

            var world2 = NewWorld(content: db);
            var c = world2.AddPlayer("c")!;
            StartRun(world2, c);
            Assert.Equal(OpResult.Ok, world2.GrantPerk(c, db.Perk("marching_band")));
            Assert.Equal(OpResult.PerkExcluded, world2.GrantPerk(c, db.Perk("sprinter")));
            Assert.Equal(OpResult.Ok, world2.GrantPerk(c, db.Perk("plodder")));
        }

        [Fact]
        public void PerksLastForTheRun_AndNoLonger()
        {
            var world = NewWorld();
            var p = world.AddPlayer("carrier")!;
            StartRun(world, p);
            world.GrantPerk(p, Db.Perk("long_legs"));
            world.GrantPerk(p, Db.Perk("union_rep"));
            ReadyUp(world);
            Assert.Equal(540, world.Run.Quota);

            // Nothing delivered: the quota is missed and the run ends.
            RunUntil(world, () => world.Run.Phase == RunPhase.Results, 262);
            Assert.Single(p.Perks);
            Assert.Single(world.TeamPerks);
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.ContinueRun(0)));
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);
            Assert.Empty(p.Perks);
            Assert.Empty(world.TeamPerks);
            Assert.Equal(1f, p.SpeedMult);

            StartRun(world, p);
            ReadyUp(world);
            Assert.Equal(600, world.Run.Quota);
            Assert.Equal(OpResult.Ok, world.GrantPerk(p, Db.Perk("union_rep")));
        }

        // --- what each perk does --------------------------------------------------------------------

        [Fact]
        public void LongLegs_WalksFarther()
        {
            float Walked(bool perk)
            {
                var world = NewWorld();
                var p = InDelivery(world, perk ? new[] { "long_legs" } : Array.Empty<string>());
                SimDriver.Teleport(p, 30f, 28f);
                SimDriver.WalkTo(world, p, 80f, 28f, maxTicks: 60);
                return p.Move.X - 30f;
            }
            float plain = Walked(false), fast = Walked(true);
            Assert.True(plain > 3f, $"walked {plain} m");
            Assert.Equal(1.12f, fast / plain, 1);
        }

        [Fact]
        public void StrongBack_HalvesWhatTheLoadCosts()
        {
            var world = NewWorld();
            var strong = world.AddPlayer("strong")!;
            var plain = world.AddPlayer("plain")!;
            StartRun(world, strong);
            Assert.Equal(OpResult.Ok, world.GrantPerk(strong, Db.Perk("strong_back")));
            foreach (var p in new[] { strong, plain })
                for (int i = 0; i < 4; i++)
                    Assert.True(world.Give(p.Inventory, ItemStack.OfMail(TestContent.Mail("medium", world.Map.Destinations[0].Address, _nextMailId++))));

            float floor = Db.Balance.GetFloat("weightSpeedFloor");
            Assert.True(plain.SpeedMult < 1f && plain.SpeedMult > floor, $"plain carries at {plain.SpeedMult}");
            Assert.Equal((1f - plain.SpeedMult) / 2f, 1f - strong.SpeedMult, 5);
        }

        [Fact]
        public void QuickHands_MayInsertTwiceAsFast()
        {
            foreach (bool perk in new[] { false, true })
            {
                var world = NewWorld();
                var p = InDelivery(world, perk ? new[] { "quick_hands" } : Array.Empty<string>());
                Assert.Equal(OpResult.Ok, Deliver(world, p, 10));
                // 0.2 s hold minus the slack is 3 ticks; the plain 0.4 s asks for 9.
                Assert.Equal(perk ? OpResult.Ok : OpResult.TooFast, Deliver(world, p, 10, wait: 4));
                Assert.Equal(OpResult.TooFast, Deliver(world, p, 10, wait: 0));
            }
        }

        [Fact]
        public void SecondWind_StandsUpAfterThreeSeconds_WithTheHotbar()
        {
            var world = NewWorld(raids: false);
            var p = InDelivery(world, "second_wind");
            SimDriver.Teleport(p, 50f, 28f);
            var letter = TestContent.Mail("letter", world.Map.Destinations[0].Address, _nextMailId++);
            Assert.True(world.Give(p.Hotbar, ItemStack.OfMail(letter)));
            Assert.True(world.Give(p.Inventory, ItemStack.OfItem(Db.Item("stone"), 3)));

            world.DamagePlayer(p, 1000f);
            Assert.True(p.Dead);
            Assert.Equal(world.Tick + 3 * Tps, p.RespawnTick);
            Assert.Equal(p.RespawnTick, world.Events.OfType<PlayerDiedEvent>().Single().RespawnTick);
            Assert.Equal(1, p.Hotbar.EntryCount);
            Assert.Equal(0, p.Inventory.EntryCount);
            var bag = Assert.Single(world.Bags);
            Assert.All(bag.Container.Entries, e => Assert.Equal(StackKind.Item, e.Stack.Kind));

            RunUntil(world, () => !p.Dead, 4);
            Assert.Equal(1, p.Hotbar.EntryCount);

            // With nothing but the hotbar on them there is no bag at all.
            var world2 = NewWorld(raids: false);
            var q = InDelivery(world2, "second_wind");
            Assert.True(world2.Give(q.Hotbar, ItemStack.OfMail(letter)));
            world2.DamagePlayer(q, 1000f);
            Assert.Empty(world2.Bags);
            Assert.Empty(world2.Events.OfType<BagDroppedEvent>());
        }

        [Fact]
        public void WithoutSecondWind_TenSecondsAndEverythingInTheBag()
        {
            var world = NewWorld(raids: false);
            var p = InDelivery(world);
            Assert.True(world.Give(p.Hotbar, ItemStack.OfMail(TestContent.Mail("letter", world.Map.Destinations[0].Address, _nextMailId++))));
            world.DamagePlayer(p, 1000f);
            Assert.Equal(world.Tick + 10 * Tps, p.RespawnTick);
            Assert.Equal(0, p.Hotbar.EntryCount);
            Assert.Single(world.Bags);
        }

        [Fact]
        public void PostalSprint_ACorrectDeliveryIsFourSecondsOfSpeed()
        {
            var world = NewWorld();
            var p = InDelivery(world, "postal_sprint");
            Assert.Equal(1f, p.SpeedMult);

            Assert.Equal(OpResult.Ok, Deliver(world, p, 10, wrong: true));
            Assert.Equal(1f, p.SpeedMult);
            Assert.Equal(0u, p.BoostUntilTick);

            Assert.Equal(OpResult.Ok, Deliver(world, p, 10));
            Assert.Equal(1.3f, p.SpeedMult, 5);
            Assert.Equal(world.Tick + 4 * Tps, p.BoostUntilTick);
            SimDriver.StepTicks(world, 4 * Tps - 1);
            Assert.Equal(1.3f, p.SpeedMult, 5);
            world.Step();
            Assert.Equal(1f, p.SpeedMult);
            Assert.Equal(0u, p.BoostUntilTick);

            // The next delivery starts the four seconds anew; they do not add up.
            Assert.Equal(OpResult.Ok, Deliver(world, p, 10));
            Assert.Equal(OpResult.Ok, Deliver(world, p, 10, wait: 30));
            Assert.Equal(world.Tick + 4 * Tps, p.BoostUntilTick);
            Assert.Equal(1.3f, p.SpeedMult, 5);
        }

        [Fact]
        public void UnionRep_LowersTheQuota_NotTheMail()
        {
            int Spawned(bool perk, out int quota)
            {
                var world = NewWorld(seed: 11);
                var p = InDelivery(world, perk ? new[] { "union_rep" } : Array.Empty<string>());
                quota = world.Run.Quota;
                Assert.Equal(quota, world.GetGlobalState().Quota);
                RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 241);
                Assert.Equal(quota, world.Run.LastSummary!.Quota);
                return (int)world.Spawner.MailSpawnedTotal;
            }
            int plainMail = Spawned(false, out int plainQuota);
            int perkMail = Spawned(true, out int perkQuota);
            Assert.Equal(600, plainQuota);
            Assert.Equal(540, perkQuota);
            Assert.Equal(plainMail, perkMail);
        }

        [Fact]
        public void UnionRep_AShiftOnTheLowerQuotaIsPaid()
        {
            var world = NewWorld();
            var p = InDelivery(world, "union_rep");
            Assert.Equal(OpResult.Ok, Deliver(world, p, 540));
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 241);
            Assert.True(world.Run.LastSummary!.QuotaMet);
        }

        [Fact]
        public void StampCollector_PaperPaysAQuarterMore_PackagesAsBefore()
        {
            var world = NewWorld();
            var p = InDelivery(world, "stamp_collector");
            Assert.Equal(OpResult.Ok, Deliver(world, p, 100));
            Assert.Equal(125, world.Events.OfType<DeliveredEvent>().Last().Paid);
            Assert.Equal(125, world.Economy.ShiftEarnings);
            Assert.Equal(125, p.Earned);

            Assert.Equal(OpResult.Ok, Deliver(world, p, 100, kind: "small"));
            Assert.Equal(100, world.Events.OfType<DeliveredEvent>().Last().Paid);
            Assert.Equal(225, world.Economy.ShiftEarnings);

            // Whole ¢: 2 × 1.25 is 2.5, and the half goes to the carriers.
            Assert.Equal(OpResult.Ok, Deliver(world, p, 2));
            Assert.Equal(3, world.Events.OfType<DeliveredEvent>().Last().Paid);
        }

        [Fact]
        public void FragileHandling_AMisdeliveryCostsHalf()
        {
            (int Penalty, int Complaint) Misdeliver(bool perk)
            {
                var world = NewWorld();
                var p = InDelivery(world, perk ? new[] { "fragile_handling" } : Array.Empty<string>());
                int before = world.Economy.Complaint;
                Assert.Equal(OpResult.Ok, Deliver(world, p, 100, wrong: true));
                return (world.Events.OfType<MisdeliveredEvent>().Single().Penalty, world.Economy.Complaint - before);
            }
            Assert.Equal((50, 5), Misdeliver(false));
            // Half of 5 complaint points is 2.5; halves round up.
            Assert.Equal((25, 3), Misdeliver(true));
        }

        [Fact]
        public void GracePeriod_MailOneShiftLatePaysInFull_AndIsStillLate()
        {
            (int Paid, int Late) DeliverLate(bool perk, int deadline)
            {
                var world = NewWorld();
                var p = InDelivery(world, perk ? new[] { "grace_period" } : Array.Empty<string>());
                Assert.Equal(OpResult.Ok, Deliver(world, p, 100, deadlineShift: deadline));
                return (world.Events.OfType<DeliveredEvent>().Single().Paid, world.Economy.ShiftLateDeliveries);
            }
            // Shift 1: a deadline of 0 is one shift late.
            Assert.Equal((50, 1), DeliverLate(false, 0));
            Assert.Equal((100, 1), DeliverLate(true, 0));
            Assert.Equal((100, 0), DeliverLate(true, 1));
        }

        [Fact]
        public void TheRulesWithoutASheetAreTheContentsOwn()
        {
            var dest = Db.Map("test_street").Destinations[0];
            var other = Db.Map("test_street").Destinations[1];
            var item = TestContent.Mail("letter", dest.Address);
            item.Value = 100;
            item.DeadlineShift = 0;
            var kind = Db.MailKinds[item.Kind];
            var none = new StatSheet();
            foreach (var d in new[] { dest, other })
            {
                var bare = DeliveryRules.Evaluate(item, kind, d, 1, 0, Db);
                var sheet = DeliveryRules.Evaluate(item, kind, d, 1, 0, Db, none);
                Assert.Equal((bare.Outcome, bare.Amount, bare.Complaint, bare.Late), (sheet.Outcome, sheet.Amount, sheet.Complaint, sheet.Late));
            }
        }

        [Fact]
        public void EarlyWarning_TheRaidIsAnnouncedTwentySecondsSooner()
        {
            var world = NewWorld(seed: 21);
            var p = InDelivery(world, "early_warning");
            Assert.Equal(OpResult.Ok, Deliver(world, p, world.Run.Quota));
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 241);
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 60);
            ReadyUp(world);
            Assert.Equal(2, world.Run.Shift);

            uint start = world.RaidStartTick;
            Assert.Equal(world.Run.PhaseEndTick - 90 * Tps, start);
            RunUntil(world, () => world.Raid == RaidState.Warning, 271);
            Assert.Equal(start - 35 * Tps, world.Tick);
            RunUntil(world, () => world.Raid == RaidState.Active, 36);
            Assert.Equal(start, world.Tick);
        }

        [Fact]
        public void UnionMuscle_TheHammerHitsHalfAgainAsHard_AndShovesTwiceAsFar()
        {
            float Shoved(bool perk, out float hp)
            {
                var world = NewWorld(raids: false);
                var p = InDelivery(world, perk ? new[] { "union_muscle" } : Array.Empty<string>());
                SimDriver.Teleport(p, 50f, 28f);
                var giant = world.SpawnEnemy(Db.Enemy("giant"), 52.5f, 28f)!;
                Press(world, p, InputButtons.Attack);
                hp = giant.Hp;
                return MathF.Sqrt(giant.PushX * giant.PushX + giant.PushZ * giant.PushZ) + (giant.X - 52.5f);
            }
            float plain = Shoved(false, out float plainHp);
            float strong = Shoved(true, out float strongHp);
            Assert.Equal(400f - 25f, plainHp);
            Assert.Equal(400f - 37.5f, strongHp);
            // Both giants took the same step of their own in that tick; what differs is the shove.
            Assert.Equal(Db.Balance.GetFloat("hammerKnockback"), strong - plain, 3);
        }

        [Fact]
        public void ZoningBoard_OpensEachDistrictAShiftEarly_NeverBeforeTheFirst_AndSpawnsMoreValue()
        {
            var map = PerformativeMail.Sim.World.WorldGenerator.Generate(Db, 3);
            Assert.True(map.Districts.Count >= 3);
            foreach (var d in map.Districts)
                Assert.Equal(Math.Max(1, d.UnlockShift - 1), PerformativeMail.Sim.World.WorldMap.DistrictOpensAt(d, 1));
            var second = map.Destinations.First(d => !d.IsDock && map.OpensAt(d) == 2);
            Assert.Equal(1, map.OpensAt(second, 1));
            // A dock keeps its own later shift.
            foreach (var dock in map.Destinations.Where(d => d.IsDock)) Assert.True(map.OpensAt(dock, 1) >= dock.UnlockShift);

            var world = NewWorld(raids: false);
            var host = world.AddPlayer("host")!;
            StartRun(world, host);
            Assert.Equal(0, world.DistrictsEarly);
            Assert.Equal(OpResult.Ok, world.GrantPerk(host, Db.Perk("second_district")));
            Assert.Equal(1, world.DistrictsEarly);
            ReadyUp(world);
            Assert.Equal(1, world.Spawner.DistrictsEarly);
        }

        [Fact]
        public void PriorityPost_SpawnsSomeMailPriority_WorthThreeTimes_ForSixtySeconds()
        {
            var world = NewWorld(raids: false);
            var p = InDelivery(world, "priority_post");
            Assert.True(world.TeamHas(RuleFlag.PriorityPost));
            uint start = world.Tick;
            RunUntil(world, () => world.Run.Phase != RunPhase.Delivery, 400);
            var all = world.Intake.Entries.Where(e => e.Stack.Kind == StackKind.Mail).SelectMany(e => e.Stack.Mail).ToList();
            Assert.True(all.Count >= 30, $"{all.Count} pieces");
            var priority = all.Where(m => m.IsPriority).ToList();
            Assert.InRange(priority.Count * 100 / all.Count, 3, 20);
            Assert.All(priority, m => Assert.InRange(m.DueTick, start + 60u * Tps, world.Tick + 60u * Tps));
            Assert.DoesNotContain(all, m => m.IsPriority && (m.Flags & MailFlags.Cursed) != 0);

            // Without the perk no mail is Priority, and the stream is not drawn from.
            var plain = NewWorld(raids: false);
            InDelivery(plain);
            RunUntil(plain, () => plain.Run.Phase != RunPhase.Delivery, 400);
            Assert.DoesNotContain(plain.Intake.Entries.Where(e => e.Stack.Kind == StackKind.Mail).SelectMany(e => e.Stack.Mail), m => m.IsPriority);
        }

        [Fact]
        public void APriorityPiece_PaysItsValueInTime_AndAThirdOfItOnceLate()
        {
            var dest = NewWorld(raids: false).Map.Destinations[0];
            var letter = Db.MailKind("letter");
            var item = TestContent.Mail("letter", dest.Address, 1, 1);
            item.Value = 300;
            item.Flags = MailFlags.Priority;
            item.DueTick = 1000;
            Assert.Equal(300, DeliveryRules.Evaluate(item, letter, dest, 1, 0, Db, null, 1000).Amount);
            Assert.Equal(100, DeliveryRules.Evaluate(item, letter, dest, 1, 0, Db, null, 1001).Amount);
        }
    }
}
