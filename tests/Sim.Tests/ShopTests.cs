using System;
using System.IO;
using System.Linq;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The Post Office shop (doc 03 §4.4, doc 10 §3): bought out of the team wallet at the counter
    /// in Prep and at Payday, sold to at the item's sell price, two specials a shift.
    /// </summary>
    public class ShopTests
    {
        private const int Tps = 30;

        private const string Blueprint =
            "{ \"id\": \"bp_defense\", \"name\": \"Defense Blueprint\", \"description\": \"-\", \"kind\": \"blueprint\", \"price\": 300, "
            + "\"grants\": { \"blueprint\": \"bp_defense\" }, \"tags\": [\"blueprint\", \"special\"] }, "
            // The Fast Belt's recipe names it; from shift 3, so the earlier shelves are as they were.
            + "{ \"id\": \"bp_fast_belts\", \"name\": \"Fast Belts\", \"description\": \"-\", \"kind\": \"blueprint\", \"price\": 500, "
            + "\"grants\": { \"blueprint\": \"bp_fast_belts\" }, \"availability\": { \"fromShift\": 3, \"slot\": \"fixed\" }, \"tags\": [\"blueprint\"] }, "
            // The Address Sorter's names this one; from the last shift, so the shelves before it are as they were.
            + "{ \"id\": \"bp_sorting\", \"name\": \"Sorting\", \"description\": \"-\", \"kind\": \"blueprint\", \"price\": 400, "
            + "\"grants\": { \"blueprint\": \"bp_sorting\" }, \"availability\": { \"fromShift\": 5, \"slot\": \"fixed\" }, \"tags\": [\"blueprint\"] }, "
            // The pipes' (from the last shift too).
            + "{ \"id\": \"bp_pipes\", \"name\": \"Pneumatics\", \"description\": \"-\", \"kind\": \"blueprint\", \"price\": 700, "
            + "\"grants\": { \"blueprint\": \"bp_pipes\" }, \"availability\": { \"fromShift\": 5, \"slot\": \"fixed\" }, \"tags\": [\"blueprint\"] }, "
            // The Vehicle Depot's (from the last shift too).
            + "{ \"id\": \"bp_truck\", \"name\": \"Motor Pool\", \"description\": \"-\", \"kind\": \"blueprint\", \"price\": 500, "
            + "\"grants\": { \"blueprint\": \"bp_truck\" }, \"availability\": { \"fromShift\": 5, \"slot\": \"fixed\" }, \"tags\": [\"blueprint\"] }";

        private uint _nextRequest = 1;
        private uint _nextMailId = 960000;

        private static ContentDb Db => TestContent.Db;

        private static SimWorld NewWorld(uint seed = 7, ContentDb? content = null) =>
            new SimWorld(content ?? Db, new SimSettings { Seed = seed, Raids = false, Draft = false });

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

        /// <summary>The real content with these files in place of its own.</summary>
        private static MemoryContentSource Source(string? shop = null, Func<string, string>? map = null)
        {
            string root = DirectoryContentSource.FindContentRoot(AppContext.BaseDirectory)!;
            var source = new MemoryContentSource();
            foreach (string file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                source.Files[Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')] = File.ReadAllText(file);
            if (shop != null)
            {
                foreach (string key in source.Files.Keys.Where(k => k.StartsWith("shop/", StringComparison.Ordinal)).ToList())
                    source.Files.Remove(key);
                source.Files["shop/shop.json"] = shop;
            }
            if (map != null)
            {
                string changed = map(source.Files["maps/test_street.json"]);
                Assert.NotEqual(source.Files["maps/test_street.json"], changed);
                source.Files["maps/test_street.json"] = changed;
            }
            return source;
        }

        private static string Entry(string id, string item, int count, int price, string rest = "", string tags = "") =>
            $"{{ \"id\": \"{id}\", \"name\": \"{id}\", \"kind\": \"item\", \"price\": {price}, "
            + $"\"grants\": {{ \"item\": \"{item}\", \"count\": {count} }}, \"tags\": [{tags}]{(rest.Length > 0 ? ", " + rest : "")} }}";

        private static string Shop(params string[] entries) => "[" + string.Join(",", entries) + "]";

        /// <summary>A run in its first Prep, the host at the counter with money in the wallet.</summary>
        private (SimWorld World, Player Host) AtTheCounter(uint seed = 7, int wallet = 1000, ContentDb? content = null)
        {
            var world = NewWorld(seed, content);
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            world.Economy.AddLoot(wallet);
            ToTheCounter(world, host);
            return (world, host);
        }

        private static void ToTheCounter(SimWorld world, Player p)
        {
            SimDriver.Teleport(p, world.Map.PostOffice.ShopX + 1.0f, world.Map.PostOffice.ShopZ);
            SimDriver.StepTicks(world, 2);
        }

        /// <summary>Plays the shift at hand on quota into its Payday.</summary>
        private void IntoPayday(SimWorld world, Player carrier)
        {
            if (world.Run.Phase == RunPhase.Prep)
                foreach (var p in world.Players) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            var home = world.Map.Destinations[0];
            var item = TestContent.Mail("letter", home.Address, _nextMailId++, world.Run.Shift);
            item.Value = (ushort)world.Run.Quota;
            Assert.True(world.Give(carrier.Hotbar, ItemStack.OfMail(item)));
            SimDriver.Teleport(carrier, home.MailboxX, home.MailboxZ);
            SimDriver.StepTicks(world, 15);
            var entry = carrier.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
            Assert.Equal(OpResult.Ok, Request(world, carrier, SimRequest.Deliver(0, carrier.Hotbar.Id, entry.EntryId, home.Id)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 310);
        }

        private OpResult Buy(SimWorld world, Player p, string id, ContentDb? content = null) =>
            Request(world, p, SimRequest.Buy(0, (content ?? Db).ShopItem(id).Index));

        private static int Count(GridContainer c, string item)
        {
            ushort index = Db.Item(item).Index;
            return c.Entries.Where(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == index).Sum(e => e.Stack.Count);
        }

        private static bool OnSpecial(SimWorld world, string id) => world.ShopSpecials.Any(s => s.Id == id);

        /// <summary>The first seed whose first shift has (or has not) the entry among its specials.</summary>
        private static uint SeedWhere(string id, bool special)
        {
            for (uint seed = 1; seed < 200; seed++)
            {
                var world = NewWorld(seed);
                var host = world.AddPlayer("host")!;
                world.Submit(host.Id, SimRequest.StartRun(1));
                world.Step();
                if (OnSpecial(world, id) == special) return seed;
            }
            throw new InvalidOperationException($"no seed below 200 with '{id}' {(special ? "on" : "off")} special");
        }

        // --- the catalog ------------------------------------------------------------------------

        [Fact]
        public void TheCatalogSellsBandagesAndRefinedMaterials_AndOneRotatingKit()
        {
            // The tools came with harvesting (HarvestTests); they are never a special, so the roll is as it was.
            // So did the Fortify blueprint with building (FortifyTests), the Shovel with the beach and the Bike (VehicleTests).
            Assert.Equal(new[] { "bandage_x3", "plank_x20", "rope_x10", "glass_x10", "iron_ingot_x10", "oil_can_x3", "first_aid_kit", "axe", "pickaxe", "shovel", "bike", "bp_defense", "bp_fast_belts", "bp_sorting", "bp_truck", "mail_truck", "bp_pipes" },
                Db.ShopItems.Select(s => s.Id).ToArray());
            for (int i = 0; i < Db.ShopItems.Count; i++) Assert.Equal(i, Db.ShopItems[i].Index);

            var bandages = Db.ShopItem("bandage_x3");
            Assert.Equal((ShopKind.Item, 80, "bandage", 3, 1, false, false), (bandages.Kind, bandages.Price, bandages.Item!.Id, bandages.Count, bandages.FromShift, bandages.Rotating, bandages.OncePerRun));
            Assert.Equal((140, 20), (Db.ShopItem("plank_x20").Price, Db.ShopItem("plank_x20").Count));
            Assert.Equal(2, Db.ShopItem("oil_can_x3").FromShift);
            var kit = Db.ShopItem("first_aid_kit");
            Assert.True(kit.Rotating && kit.Special);
            Assert.Equal(("bandage", 5, 105), (kit.Item!.Id, kit.Count, kit.Price));
            Assert.Equal(ShopKind.Vehicle, Db.ShopItem("bike").Kind);

            // Doc 05 §1.1: a Bandage gives 50 HP at once, Berries 25 over five seconds.
            Assert.Equal((50, 0f), (Db.Item("bandage").Heal, Db.Item("bandage").HealSeconds));
            Assert.Equal((25, 5f), (Db.Item("berries").Heal, Db.Item("berries").HealSeconds));
            Assert.Equal(0, Db.Item("plank").Heal);

            Assert.True(Db.Map("test_street").PostOffice.HasShop);
        }

        [Fact]
        public void ASpecialIsAFifthOff_ARotatingEntryCostsWhatItSays()
        {
            int discount = ShopRules.DiscountPermille(Db.Balance);
            Assert.Equal(200, discount);
            Assert.Equal(2, ShopRules.SpecialSlots(Db.Balance));
            Assert.Equal(80, ShopRules.Price(Db.ShopItem("bandage_x3"), false, discount));
            Assert.Equal(64, ShopRules.Price(Db.ShopItem("bandage_x3"), true, discount));
            Assert.Equal(112, ShopRules.Price(Db.ShopItem("plank_x20"), true, discount));
            Assert.Equal(280, ShopRules.Price(Db.ShopItem("iron_ingot_x10"), true, discount));
            Assert.Equal(105, ShopRules.Price(Db.ShopItem("first_aid_kit"), true, discount));
            // Nothing is ever free.
            Assert.Equal(1, ShopRules.Price(Db.ShopItem("bandage_x3"), true, 1000));

            // Nothing the counter sells is worth more when it is sold straight back.
            foreach (var entry in Db.ShopItems)
                if (entry.Item != null) Assert.True(ShopRules.Price(entry, entry.Special, discount) >= ShopRules.SaleValue(entry.Item, entry.Count), entry.Id);
        }

        [Fact]
        public void TheValidatorRefusesWhatTheCounterCouldNotSell()
        {
            string json = Shop(
                Entry("ok", "bandage", 3, 80),
                Entry("ok", "bandage", 3, 80),
                Entry("stranger", "medkit", 1, 50),
                Entry("heap", "bandage", 6, 200),
                Entry("free", "plank", 5, 0),
                Entry("loop", "glass", 10, 60),
                Entry("special_loop", "iron_ingot", 10, 110, tags: "\"special\""),
                Entry("hidden", "rope", 5, 80, "\"availability\": { \"slot\": \"rotating\" }"),
                Entry("sideways", "rope", 5, 80, "\"availability\": { \"slot\": \"weekly\" }"),
                Entry("early", "rope", 5, 80, "\"availability\": { \"fromShift\": 0 }"),
                "{ \"id\": \"trike\", \"name\": \"Trike\", \"kind\": \"vehicle\", \"price\": 200, \"grants\": { \"vehicle\": \"trike\" } }",
                "{ \"id\": \"porter\", \"name\": \"Porter\", \"kind\": \"hire\", \"price\": 200, \"grants\": { } }",
                "{ \"id\": \"luck\", \"name\": \"Luck\", \"kind\": \"wish\", \"price\": 200, \"grants\": { } }",
                "{ \"id\": \"bare\", \"name\": \"Bare\", \"kind\": \"item\", \"price\": 200 }",
                "{ \"id\": \"bp_other\", \"name\": \"B\", \"kind\": \"blueprint\", \"price\": 200, \"grants\": { \"blueprint\": \"bp_defense\" } }",
                "{ \"id\": \"bp_often\", \"name\": \"B\", \"kind\": \"blueprint\", \"price\": 200, \"grants\": { \"blueprint\": \"bp_often\" }, \"oncePerRun\": false }",
                Blueprint);

            var db = ContentDb.TryLoad(Source(json), out var errors);
            string all = string.Join("\n", errors);
            Assert.Contains("shop/shop.json: duplicate id 'ok'", all);
            Assert.Contains("'stranger': grants unknown item 'medkit'", all);
            Assert.Contains("'heap': count must be in 1..5 (one stack of 'bandage')", all);
            Assert.Contains("'free': price must be in 1..65535", all);
            Assert.Contains("'loop': costs less than the shop pays for 10 'glass' (70 ¢)", all);
            // 110 pays for ten ingots at 10 ¢, but not at a fifth off.
            Assert.Contains("'special_loop': costs less than the shop pays for 10 'iron_ingot' (100 ¢)", all);
            Assert.Contains("'hidden': a rotating entry must be tagged 'special'", all);
            Assert.Contains("'sideways': availability.slot must be 'fixed' or 'rotating', not 'weekly'", all);
            Assert.Contains("'early': availability.fromShift must be >= 1", all);
            Assert.Contains("'trike': grants unknown vehicle 'trike'", all);
            Assert.Contains("'porter': kind 'hire' is not sold yet", all);
            Assert.Contains("'luck': unknown kind 'wish'", all);
            Assert.Contains("'bare': missing 'grants' object", all);
            Assert.Contains("'bp_other': a blueprint grants itself", all);
            Assert.Contains("'bp_often': a blueprint is bought once per run", all);
            Assert.DoesNotContain("'bp_defense'", all);

            var good = ContentDb.Load(Source(Shop(Entry("ok", "bandage", 3, 80), Blueprint)));
            Assert.Equal((ShopKind.Blueprint, true, 300), (good.ShopItem("bp_defense").Kind, good.ShopItem("bp_defense").OncePerRun, good.ShopItem("bp_defense").Price));
            Assert.Null(good.ShopItem("bp_defense").Item);
        }

        [Fact]
        public void TheCatalogIsPartOfTheContentHash_AndSoIsWhereTheCounterStands()
        {
            string real = File.ReadAllText(Path.Combine(DirectoryContentSource.FindContentRoot(AppContext.BaseDirectory)!, "shop", "shop.json"));
            Assert.Equal(Db.ContentHash, ContentDb.Load(Source(real)).ContentHash);
            Assert.NotEqual(Db.ContentHash, ContentDb.Load(Source(real.Replace("\"price\": 140", "\"price\": 141"))).ContentHash);

            var moved = ContentDb.Load(Source(map: m => m.Replace("\"shop\": [12.0, 22.0]", "\"shop\": [12.0, 21.0]")));
            Assert.NotEqual(Db.Map("test_street").MapHash, moved.Map("test_street").MapHash);
        }

        [Fact]
        public void TheCounterMustStandOnTheMap_AndWhereSomebodyCanReachIt()
        {
            ContentDb.TryLoad(Source(map: m => m.Replace("\"shop\": [12.0, 22.0]", "\"shop\": [500.0, 22.0]")), out var outside);
            Assert.Contains(outside, e => e.Contains("postOffice.shop (500, 22) is outside the map"));

            // The middle of the hall: walls all around.
            ContentDb.TryLoad(Source(map: m => m.Replace("\"shop\": [12.0, 22.0]", "\"shop\": [8.0, 18.0]")), out var walled);
            Assert.Contains(walled, e => e.Contains("postOffice.shop is walled in"));

            // A map without a counter has no shop: nothing is sold there.
            var none = ContentDb.Load(Source(map: m => m.Replace("\"shop\": [12.0, 22.0],", "").Replace(", \"shop\": [12.0, 22.0]", "")));
            Assert.False(none.Map("test_street").PostOffice.HasShop);
            var (world, host) = AtTheCounter(content: none);
            Assert.Equal(OpResult.BadRequest, Buy(world, host, "bandage_x3"));
        }

        // --- buying -----------------------------------------------------------------------------

        [Fact]
        public void APurchaseComesOutOfTheTeamWallet_AndGoesIntoTheBuyersPockets()
        {
            var (world, host) = AtTheCounter(SeedWhere("bandage_x3", special: false));
            Assert.False(OnSpecial(world, "plank_x20") && OnSpecial(world, "bandage_x3"));
            int bandages = world.ShopPrice(Db.ShopItem("bandage_x3"));
            Assert.Equal(80, bandages);

            Assert.Equal(OpResult.Ok, Buy(world, host, "bandage_x3"));
            Assert.Equal(1000 - 80, world.Economy.Wallet);
            Assert.Equal(80, world.Economy.TotalSpent);
            // What is used from the hand lies in the hotbar.
            Assert.Equal(3, Count(host.Hotbar, "bandage"));
            Assert.Equal(0, Count(host.Inventory, "bandage"));
            var bought = world.Events.OfType<ShopBoughtEvent>().Single();
            Assert.Equal((host.Id, Db.ShopItem("bandage_x3").Index, (ushort)80), (bought.Player, bought.Item, bought.Price));

            // Materials go to the inventory.
            int planks = world.ShopPrice(Db.ShopItem("plank_x20"));
            Assert.Equal(OpResult.Ok, Buy(world, host, "plank_x20"));
            Assert.Equal(20, Count(host.Inventory, "plank"));
            Assert.Equal(0, Count(host.Hotbar, "plank"));
            Assert.Equal(1000 - 80 - planks, world.Economy.Wallet);

            // A second lot of bandages tops the stack up to its five and starts another.
            Assert.Equal(OpResult.Ok, Buy(world, host, "bandage_x3"));
            Assert.Equal(new[] { 1, 5 }, host.Hotbar.Entries.Where(e => e.Stack.DefIndex == Db.Item("bandage").Index).Select(e => e.Stack.Count).OrderBy(n => n).ToArray());

            // The money is the team's: what the shop takes is not what the shift earned.
            Assert.Equal(0, world.Economy.ShiftEarnings);
        }

        [Fact]
        public void TheCounterServesInPrepAndAtPayday_AndWhoStandsAtIt()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            world.Economy.AddLoot(1000);
            ToTheCounter(world, host);
            Assert.Equal(OpResult.WrongPhase, Buy(world, host, "bandage_x3"));

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            world.Economy.AddLoot(1000);
            ToTheCounter(world, host);
            Assert.Equal(OpResult.Ok, Buy(world, host, "bandage_x3"));
            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.Buy(0, Db.ShopItems.Count)));

            // The counter's reach is its own, wider than a mailbox's: every seat of the pad before it is served.
            var po = world.Map.PostOffice;
            float reach = Db.Balance.GetFloat("serverShopRange");
            Assert.True(reach > Db.Balance.GetFloat("serverInteractRange"));
            Assert.True(Db.Balance.GetFloat("shopRange") < reach);
            SimDriver.Teleport(host, po.ShopX + reach - 0.2f, po.ShopZ);
            SimDriver.StepTicks(world, 30);
            Assert.Equal(OpResult.Ok, Buy(world, host, "bandage_x3"));
            SimDriver.Teleport(host, po.ShopX + reach + 0.3f, po.ShopZ);
            SimDriver.StepTicks(world, 30);
            Assert.Equal(OpResult.OutOfRange, Buy(world, host, "bandage_x3"));

            // Across the street nobody is served.
            var home = world.Map.Destinations[0];
            SimDriver.Teleport(host, home.MailboxX, home.MailboxZ);
            SimDriver.StepTicks(world, 30);
            Assert.Equal(OpResult.OutOfRange, Buy(world, host, "bandage_x3"));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 2)));
            Assert.Equal(OpResult.OutOfRange, Request(world, host, SimRequest.Sell(0, host.Inventory.Id, host.Inventory.Entries.First().EntryId)));

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            ToTheCounter(world, host);
            Assert.Equal(OpResult.WrongPhase, Buy(world, host, "bandage_x3"));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 5)));
            Assert.Equal(OpResult.WrongPhase, Request(world, host, SimRequest.Sell(0, host.Inventory.Id, host.Inventory.Entries.First().EntryId)));

            IntoPayday(world, host);
            ToTheCounter(world, host);
            int before = world.Economy.Wallet;
            Assert.Equal(OpResult.Ok, Buy(world, host, "rope_x10"));
            Assert.Equal(before - world.ShopPrice(Db.ShopItem("rope_x10")), world.Economy.Wallet);
        }

        [Fact]
        public void AtPaydayTheCounterServesTheTeamWhereItStands()
        {
            var (world, host) = AtTheCounter();
            var guest = world.AddPlayer("guest")!;
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 5)));

            // The shift ends with the carriers out on their rounds: nobody walks back in a Payday.
            IntoPayday(world, host);
            var home = world.Map.Destinations[0];
            var po = world.Map.PostOffice;
            SimDriver.Teleport(host, home.MailboxX, home.MailboxZ);
            SimDriver.Teleport(guest, world.Map.WidthM - 3f, world.Map.DepthM - 3f);
            SimDriver.StepTicks(world, 30);
            float dx = host.Move.X - po.ShopX, dz = host.Move.Z - po.ShopZ;
            Assert.True(dx * dx + dz * dz > 10f * 10f, "the mailbox of the first house is not far enough from the counter to tell");

            int before = world.Economy.Wallet;
            Assert.Equal(OpResult.Ok, Buy(world, host, "bandage_x3"));
            Assert.Equal(OpResult.Ok, Buy(world, guest, "rope_x10"));
            Assert.Equal(before - world.ShopPrice(Db.ShopItem("bandage_x3")) - world.ShopPrice(Db.ShopItem("rope_x10")), world.Economy.Wallet);
            Assert.Equal(3, Count(host.Hotbar, "bandage"));
            Assert.Equal(10, Count(guest.Inventory, "rope"));

            // And buys what they carry, where they carry it.
            before = world.Economy.Wallet;
            var planks = host.Inventory.Entries.First(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == Db.Item("plank").Index);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Sell(0, host.Inventory.Id, planks.EntryId)));
            Assert.Equal(before + Db.Item("plank").SellPrice * 5, world.Economy.Wallet);

            // The next Prep the counter is a counter again.
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 120);
            world.Economy.AddLoot(500);
            Assert.Equal(OpResult.OutOfRange, Buy(world, host, "bandage_x3"));
        }

        [Fact]
        public void WithoutTheMoneyOrTheRoomNothingChangesHands()
        {
            var (world, host) = AtTheCounter(wallet: 50);
            Assert.Equal(OpResult.CantAfford, Buy(world, host, "iron_ingot_x10"));
            Assert.Equal(50, world.Economy.Wallet);
            Assert.Empty(host.Inventory.Entries);

            world.Economy.AddLoot(2000);
            // Every cell holds a full stack of glass: no plank goes in.
            var glass = Db.Item("glass");
            while (world.Give(host.Inventory, ItemStack.OfItem(glass, glass.MaxStack))) { }
            while (world.Give(host.Hotbar, ItemStack.OfItem(glass, glass.MaxStack))) { }
            int wallet = world.Economy.Wallet;
            Assert.Equal(OpResult.DoesNotFit, Buy(world, host, "plank_x20"));
            Assert.Equal(wallet, world.Economy.Wallet);
            Assert.Equal(0, Count(host.Inventory, "plank") + Count(host.Hotbar, "plank"));
            Assert.Empty(world.Events.OfType<ShopBoughtEvent>());

            // Two half stacks of planks, one in each: the twenty go onto them.
            var plank = Db.Item("plank");
            host.Inventory.Remove(host.Inventory.Entries.First().EntryId);
            host.Hotbar.Remove(host.Hotbar.Entries.First().EntryId);
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(plank, 10)));
            Assert.True(world.Give(host.Hotbar, ItemStack.OfItem(plank, 10)));
            Assert.Equal(OpResult.Ok, Buy(world, host, "plank_x20"));
            Assert.Equal((20, 20), (Count(host.Inventory, "plank"), Count(host.Hotbar, "plank")));
            Assert.Equal(wallet - world.ShopPrice(Db.ShopItem("plank_x20")), world.Economy.Wallet);

            // And with nineteen free it is still no sale: a purchase is bought whole.
            var half = host.Hotbar.Entries.Single(e => e.Stack.DefIndex == plank.Index);
            half.Stack.ItemCount = 1;
            var full = host.Inventory.Entries.Single(e => e.Stack.DefIndex == plank.Index);
            full.Stack.ItemCount = 20;
            wallet = world.Economy.Wallet;
            Assert.Equal(OpResult.DoesNotFit, Buy(world, host, "plank_x20"));
            Assert.Equal(wallet, world.Economy.Wallet);
            Assert.Equal(21, Count(host.Inventory, "plank") + Count(host.Hotbar, "plank"));
        }

        [Fact]
        public void OilIsSoldFromTheSecondShift()
        {
            var (world, host) = AtTheCounter();
            Assert.Equal(OpResult.ShopLocked, Buy(world, host, "oil_can_x3"));
            Assert.DoesNotContain(world.ShopSpecials, s => s.Id == "oil_can_x3");

            IntoPayday(world, host);
            ToTheCounter(world, host);
            // The Payday is still the first shift's.
            Assert.Equal(OpResult.ShopLocked, Buy(world, host, "oil_can_x3"));
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 21);
            Assert.Equal(2, world.Run.Shift);
            ToTheCounter(world, host);
            Assert.Equal(OpResult.Ok, Buy(world, host, "oil_can_x3"));
            Assert.Equal(3, Count(host.Inventory, "oil_can"));
        }

        // --- specials ---------------------------------------------------------------------------

        [Fact]
        public void EveryShiftHasTwoSpecials_TheSameForTheSameSeed()
        {
            string[] First(uint seed)
            {
                var world = NewWorld(seed);
                var host = world.AddPlayer("host")!;
                Assert.Empty(world.ShopSpecials);
                Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
                var offer = world.Events.OfType<ShopOfferEvent>().Single();
                Assert.Equal(1, offer.Shift);
                Assert.Equal(world.ShopSpecials.Select(s => s.Index), offer.Specials);
                // The offer is on the table before the shift is.
                Assert.True(world.Events.IndexOf(offer) < world.Events.FindIndex(e => e is PhaseChangedEvent));
                return world.ShopSpecials.Select(s => s.Id).ToArray();
            }

            string[] seven = First(7);
            Assert.Equal(2, seven.Length);
            Assert.Equal(2, seven.Distinct().Count());
            Assert.Equal(seven.OrderBy(id => Db.ShopItem(id).Index), seven);
            Assert.All(seven, id => Assert.True(Db.ShopItem(id).Special && Db.ShopItem(id).FromShift <= 1));
            Assert.Equal(seven, First(7));
            Assert.Contains(Enumerable.Range(8, 8), seed => !First((uint)seed).SequenceEqual(seven));
        }

        [Fact]
        public void TheSpecialsAreDrawnAnewForEveryShift_AndHoldThroughItsPayday()
        {
            var (world, host) = AtTheCounter();
            string[] first = world.ShopSpecials.Select(s => s.Id).ToArray();
            IntoPayday(world, host);
            Assert.Equal(first, world.ShopSpecials.Select(s => s.Id).ToArray());

            var offers = new System.Collections.Generic.List<string[]> { first };
            for (int shift = 2; shift <= 4; shift++)
            {
                RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 21);
                Assert.Equal(shift, world.Run.Shift);
                // One offer a shift, and it says whose it is.
                Assert.Equal(Enumerable.Range(1, shift), world.Events.OfType<ShopOfferEvent>().Select(o => (int)o.Shift));
                Assert.Equal(2, world.ShopSpecials.Count);
                offers.Add(world.ShopSpecials.Select(s => s.Id).ToArray());
                IntoPayday(world, host);
            }
            Assert.Contains(offers, o => !o.SequenceEqual(first));
        }

        [Fact]
        public void AFixedEntryOnSpecialIsCheaper_ARotatingOneIsOnlyThereAsASpecial()
        {
            var (cheap, buyer) = AtTheCounter(SeedWhere("plank_x20", special: true));
            Assert.Equal(112, cheap.ShopPrice(Db.ShopItem("plank_x20")));
            Assert.Equal(OpResult.Ok, Buy(cheap, buyer, "plank_x20"));
            Assert.Equal(1000 - 112, cheap.Economy.Wallet);
            Assert.Equal((ushort)112, cheap.Events.OfType<ShopBoughtEvent>().Single().Price);

            var (without, nobody) = AtTheCounter(SeedWhere("first_aid_kit", special: false));
            Assert.Equal(OpResult.BadRequest, Buy(without, nobody, "first_aid_kit"));
            Assert.Equal(1000, without.Economy.Wallet);

            var (with, host) = AtTheCounter(SeedWhere("first_aid_kit", special: true));
            Assert.Equal(105, with.ShopPrice(Db.ShopItem("first_aid_kit")));
            Assert.Equal(OpResult.Ok, Buy(with, host, "first_aid_kit"));
            Assert.Equal(1000 - 105, with.Economy.Wallet);
            Assert.Equal(5, Count(host.Hotbar, "bandage"));
        }

        // --- blueprints -------------------------------------------------------------------------

        [Fact]
        public void ABlueprintIsBoughtOnce_IsTheWholeTeams_AndEndsWithTheRun()
        {
            var content = ContentDb.Load(Source(Shop(Entry("bandage_x3", "bandage", 3, 80), Blueprint)));
            var world = NewWorld(7, content);
            var host = world.AddPlayer("host")!;
            var guest = world.AddPlayer("guest")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            world.Economy.AddLoot(1000);
            ToTheCounter(world, host);
            ToTheCounter(world, guest);
            Assert.False(world.OwnsBlueprint("bp_defense"));
            int price = world.ShopPrice(content.ShopItem("bp_defense"));

            Assert.Equal(OpResult.Ok, Buy(world, guest, "bp_defense", content));
            Assert.True(world.OwnsBlueprint("bp_defense"));
            Assert.Equal(new[] { "bp_defense" }, world.ShopOwned.Select(s => s.Id).ToArray());
            Assert.Equal(1000 - price, world.Economy.Wallet);
            // Nothing lands in anybody's pockets: the team knows how to build it now.
            Assert.Empty(guest.Inventory.Entries);
            Assert.Empty(guest.Hotbar.Entries);

            Assert.Equal(OpResult.AlreadyOwned, Buy(world, host, "bp_defense", content));
            Assert.Equal(1000 - price, world.Economy.Wallet);

            // What the team owns is not put on special again.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(OpResult.Ok, Request(world, guest, SimRequest.SetReady(0, true)));
            // Nothing is delivered: the quota is missed and the run is over.
            RunUntil(world, () => world.Run.Phase == RunPhase.Results, 310);
            Assert.True(world.OwnsBlueprint("bp_defense"));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.ContinueRun(0)));
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);
            Assert.False(world.OwnsBlueprint("bp_defense"));
            Assert.Empty(world.ShopOwned);
            Assert.Empty(world.ShopSpecials);

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            world.Economy.AddLoot(1000);
            ToTheCounter(world, host);
            Assert.Equal(OpResult.Ok, Buy(world, host, "bp_defense", content));
        }

        [Fact]
        public void WhatTheTeamOwnsIsNotOnSpecialAgain()
        {
            var content = ContentDb.Load(Source(Shop(Blueprint)));
            var (world, host) = AtTheCounter(content: content);
            Assert.Equal(new[] { "bp_defense" }, world.ShopSpecials.Select(s => s.Id).ToArray());
            // A blueprint on special is a fifth off like anything else.
            Assert.Equal(240, world.ShopPrice(content.ShopItem("bp_defense")));
            Assert.Equal(OpResult.Ok, Buy(world, host, "bp_defense", content));

            IntoPayday(world, host);
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 21);
            Assert.Empty(world.ShopSpecials);
            var second = world.Events.OfType<ShopOfferEvent>().Last();
            Assert.Equal(2, second.Shift);
            Assert.Empty(second.Specials);
        }

        // --- selling ----------------------------------------------------------------------------

        [Fact]
        public void TheShopPaysTheSellPrice_IntoTheTeamWallet()
        {
            var (world, host) = AtTheCounter(wallet: 0);
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ore"), 12)));
            var ore = host.Inventory.Entries.Single();

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Sell(0, host.Inventory.Id, ore.EntryId, 5)));
            Assert.Equal(5 * Db.Item("iron_ore").SellPrice, world.Economy.Wallet);
            Assert.Equal(7, Count(host.Inventory, "iron_ore"));
            var sold = world.Events.OfType<ShopSoldEvent>().Single();
            Assert.Equal((host.Id, Db.Item("iron_ore").Index, (ushort)5, 20), (sold.Player, sold.Item, sold.Count, sold.Paid));

            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.Sell(0, host.Inventory.Id, ore.EntryId, 8)));
            // 0: the whole stack.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Sell(0, host.Inventory.Id, ore.EntryId)));
            Assert.Empty(host.Inventory.Entries);
            Assert.Equal(12 * 4, world.Economy.Wallet);
            Assert.Equal(48, world.Economy.TotalSales);
            Assert.Equal(OpResult.UnknownEntry, Request(world, host, SimRequest.Sell(0, host.Inventory.Id, ore.EntryId)));

            // Sales are the team's money, but neither the shift's earnings nor loot: they pay no quota.
            Assert.Equal(0, world.Economy.ShiftEarnings);
            Assert.Equal(0, world.Economy.TotalEarned);
            Assert.Equal(0, world.Economy.TotalLoot);
        }

        [Fact]
        public void MailIsNotForSale_AndNeitherIsWhatIsNotYours()
        {
            var (world, host) = AtTheCounter(wallet: 0);
            var guest = world.AddPlayer("guest")!;
            var home = world.Map.Destinations[0];
            Assert.True(world.Give(host.Hotbar, ItemStack.OfMail(TestContent.Mail("letter", home.Address, _nextMailId++))));
            Assert.True(world.Give(guest.Inventory, ItemStack.OfItem(Db.Item("glass"), 4)));

            Assert.Equal(OpResult.NotSellable, Request(world, host, SimRequest.Sell(0, host.Hotbar.Id, host.Hotbar.Entries.Single().EntryId)));
            Assert.Equal(OpResult.NotAllowed, Request(world, host, SimRequest.Sell(0, guest.Inventory.Id, guest.Inventory.Entries.Single().EntryId)));
            Assert.Equal(OpResult.NotAllowed, Request(world, host, SimRequest.Sell(0, world.Intake.Id, 1)));
            Assert.Equal(OpResult.UnknownContainer, Request(world, host, SimRequest.Sell(0, 999999, 1)));
            Assert.Equal(0, world.Economy.Wallet);
            Assert.Single(host.Hotbar.Entries);
            Assert.Equal(4, Count(guest.Inventory, "glass"));

            Assert.Equal(0, ShopRules.SaleValue(new ItemDef { Id = "keepsake", SellPrice = 0 }, 3));
            Assert.Equal(0, ShopRules.SaleValue(Db.Item("glass"), 0));
            Assert.Equal(21, ShopRules.SaleValue(Db.Item("glass"), 3));
        }

        // --- using ------------------------------------------------------------------------------

        [Fact]
        public void ABandageGivesFiftyAtOnce_AndIsNotWastedOnTheHealthy()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            Assert.True(world.Give(host.Hotbar, ItemStack.OfItem(Db.Item("bandage"), 3)));
            var bandage = host.Hotbar.Entries.Single();
            // Anywhere, in any phase: it is theirs to use.
            Assert.Equal(OpResult.FullHealth, Request(world, host, SimRequest.Use(0, host.Hotbar.Id, bandage.EntryId)));
            Assert.Equal(3, bandage.Stack.Count);

            host.Hp = 30f;
            host.LastDamageTick = world.Tick;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Use(0, host.Hotbar.Id, bandage.EntryId)));
            Assert.Equal(80f, host.Hp, 3);
            Assert.Equal(2, bandage.Stack.Count);

            // No more than they can hold.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Use(0, host.Hotbar.Id, bandage.EntryId)));
            Assert.Equal(host.MaxHp, host.Hp);
            host.Hp = 10f;
            host.LastDamageTick = world.Tick;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Use(0, host.Hotbar.Id, bandage.EntryId)));
            Assert.Equal(60f, host.Hp, 3);
            Assert.Empty(host.Hotbar.Entries);
            Assert.Equal(OpResult.UnknownEntry, Request(world, host, SimRequest.Use(0, host.Hotbar.Id, bandage.EntryId)));
        }

        [Fact]
        public void BerriesHealOverFiveSeconds()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("berries"), 4)));
            var berries = host.Inventory.Entries.Single();
            host.Hp = 20f;
            host.LastDamageTick = world.Tick;

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Use(0, host.Inventory.Id, berries.EntryId)));
            Assert.Equal(3, berries.Stack.Count);
            Assert.True(host.Hp < 21f, $"{host.Hp} HP right after the first berry");
            SimDriver.StepTicks(world, 74);
            Assert.InRange(host.Hp, 32f, 33f);
            // Hurt in between, it goes on healing.
            host.Hp -= 5f;
            host.LastDamageTick = world.Tick;
            SimDriver.StepTicks(world, 80);
            Assert.Equal(40f, host.Hp, 1);
            Assert.Equal(0f, host.HealPool);

            // Two at once heal twice as much, not twice as fast.
            host.LastDamageTick = world.Tick;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Use(0, host.Inventory.Id, berries.EntryId)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Use(0, host.Inventory.Id, berries.EntryId)));
            SimDriver.StepTicks(world, 148);
            Assert.InRange(host.Hp, 64f, 66f);
            host.LastDamageTick = world.Tick;
            SimDriver.StepTicks(world, 150);
            Assert.InRange(host.Hp, 89.9f, 90.2f);
            Assert.Equal(0f, host.HealPool);
        }

        [Fact]
        public void OnlyWhatHealsIsUsed_OnlyByItsOwner_AndNotByTheDowned()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            var guest = world.AddPlayer("guest")!;
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 4)));
            Assert.True(world.Give(host.Hotbar, ItemStack.OfItem(Db.Item("berries"), 2)));
            Assert.True(world.Give(guest.Hotbar, ItemStack.OfItem(Db.Item("bandage"), 1)));
            host.Hp = 50f;
            host.LastDamageTick = world.Tick;

            Assert.Equal(OpResult.NotUsable, Request(world, host, SimRequest.Use(0, host.Inventory.Id, host.Inventory.Entries.Single().EntryId)));
            Assert.Equal(OpResult.NotAllowed, Request(world, host, SimRequest.Use(0, guest.Hotbar.Id, guest.Hotbar.Entries.Single().EntryId)));
            Assert.Equal(OpResult.UnknownContainer, Request(world, host, SimRequest.Use(0, 999999, 1)));
            Assert.Equal(1, Count(guest.Hotbar, "bandage"));

            // Down with berries in the belly: what was left of them is gone with the fall.
            uint berries = host.Hotbar.Entries.Single().EntryId;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Use(0, host.Hotbar.Id, berries)));
            Assert.True(host.HealPool > 0f);
            world.DamagePlayer(host, 500f);
            Assert.True(host.Dead);
            Assert.Equal(0f, host.HealPool);
            Assert.Equal(OpResult.Dead, Request(world, host, SimRequest.Use(0, host.Hotbar.Id, berries)));
            Assert.Equal(OpResult.Dead, Request(world, host, SimRequest.Buy(0, 0)));
        }
    }
}
