using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Building by hand (doc 04 §1): a Wooden Wall and a Chest out of the Logs in a carrier's
    /// pockets, on open ground beside the street; taken down again for a part of them; in the
    /// way of whoever walks, and broken by the raiders it is in the way of.
    /// </summary>
    public class BuildTests
    {
        private const int Tps = 30;
        /// <summary>Open ground south of the street, between the mailboxes of numbers 4 and 6 (tile 22, 15 and around).</summary>
        private const int Tx = 22, Tz = 15;

        private uint _nextRequest = 1;
        private uint _nextMailId = 980000;

        private static ContentDb Db => TestContent.Db;
        private static BuildingDef Wall => Db.Building("wall_wood");
        private static BuildingDef Chest => Db.Building("chest");

        private static SimWorld NewWorld(uint seed = 7) =>
            new SimWorld(Db, new SimSettings { Seed = seed, Raids = false, Draft = false });

        /// <summary>One request and the tick that answers it; <see cref="SimWorld.Events"/> holds that tick's events after it.</summary>
        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
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

        /// <summary>A run in its first Prep, the host one tile west of (22, 15) with Logs in the inventory.</summary>
        private (SimWorld World, Player Host) InPrep(int logs = 0, uint seed = 7)
        {
            var world = NewWorld(seed);
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            SimDriver.Teleport(host, Centre(Tx - 1), Centre(Tz));
            GiveLogs(world, host, logs);
            return (world, host);
        }

        private static float Centre(int t) => BuildRules.TileCentre(t);

        private static void GiveLogs(SimWorld world, Player p, int logs)
        {
            var log = Db.Item("log");
            for (; logs > 0; logs -= Math.Min(logs, log.MaxStack))
                Assert.True(world.Give(p.Inventory, ItemStack.OfItem(log, Math.Min(logs, log.MaxStack))));
        }

        private static int Count(GridContainer c, string item)
        {
            ushort index = Db.Item(item).Index;
            return c.Entries.Where(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == index).Sum(e => e.Stack.Count);
        }

        private static int Logs(Player p) => Count(p.Hotbar, "log") + Count(p.Inventory, "log");

        private OpResult Place(SimWorld world, Player p, BuildingDef def, int tx, int tz, int rotation = 0) =>
            Request(world, p, SimRequest.PlaceConstruct(0, def.Index, tx, tz, rotation));

        private OpResult Remove(SimWorld world, Player p, int tx, int tz) =>
            Request(world, p, SimRequest.RemoveConstruct(0, tx, tz));

        private void IntoDelivery(SimWorld world, Player host)
        {
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
        }

        /// <summary>Plays the shift at hand on quota into its Payday, and puts the carrier back where they stood.</summary>
        private void IntoPayday(SimWorld world, Player carrier)
        {
            float x = carrier.Move.X, z = carrier.Move.Z;
            if (world.Run.Phase == RunPhase.Prep) IntoDelivery(world, carrier);
            var home = world.Map.Destinations[0];
            var item = TestContent.Mail("letter", home.Address, _nextMailId++, world.Run.Shift);
            item.Value = (ushort)world.Run.Quota;
            Assert.True(world.Give(carrier.Hotbar, ItemStack.OfMail(item)));
            SimDriver.Teleport(carrier, home.MailboxX, home.MailboxZ);
            SimDriver.StepTicks(world, 15);
            var entry = carrier.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
            Assert.Equal(OpResult.Ok, Request(world, carrier, SimRequest.Deliver(0, carrier.Hotbar.Id, entry.EntryId, home.Id)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 310);
            SimDriver.Teleport(carrier, x, z);
            SimDriver.StepTicks(world, 15);
        }

        /// <summary>Walks a carrier in a direction for some ticks, whatever is in the way.</summary>
        private static void Walk(SimWorld world, Player p, float dirX, float dirZ, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.QueueInput(p.Id, new PlayerInput { Seq = p.LastQueuedSeq + 1, MoveX = PlayerInput.QuantizeAxis(dirX), MoveZ = PlayerInput.QuantizeAxis(dirZ) });
                world.Step();
            }
        }

        // --- content ----------------------------------------------------------------------------

        [Fact]
        public void ContentHasTheWoodenWallAndTheChest_EachWithItsRecipe()
        {
            // The Fortify blueprint's two follow them (FortifyTests), then the Gate and the Alarm Post (GateAndAlarmTests).
            Assert.Equal(new[] { "wall_wood", "chest", "wall_stone", "spike_strip", "gate", "alarm_post", "belt_mk1", "splitter", "merger" }, Db.Buildings.Select(b => b.Id));
            Assert.Equal((300, 2, BuildingBehaviour.Wall, true), (Wall.Hp, Wall.Rotations, Wall.Behaviour, Wall.Joins));
            Assert.Equal((200, 4, BuildingBehaviour.Container, false), (Chest.Hp, Chest.Rotations, Chest.Behaviour, Chest.Joins));
            Assert.Equal("chest", Chest.Container!.Id);
            Assert.Null(Wall.Container);

            // Doc 08 §4: three Logs a wall, four a chest, from the first shift and without a blueprint.
            var wall = Wall.Recipe;
            Assert.Same(Wall, wall.Building);
            Assert.Equal(("log", 3), (wall.Inputs.Single().Item.Id, wall.Inputs.Single().Count));
            Assert.Equal(("log", 4), (Chest.Recipe.Inputs.Single().Item.Id, Chest.Recipe.Inputs.Single().Count));
            Assert.All(new[] { Wall.Recipe, Chest.Recipe }, r => Assert.Equal((1, null), (r.UnlockShift, r.Blueprint)));
            Assert.Equal(9, Db.Recipes.Count);

            Assert.Equal(8.0, Db.Balance.GetDouble("buildRange"));
            Assert.True(Db.Balance.GetDouble("serverBuildRange") > Db.Balance.GetDouble("buildRange"));
            Assert.Equal((1.0, 0.5), (Db.Balance.GetDouble("deconstructRefundPrep"), Db.Balance.GetDouble("deconstructRefundDelivery")));
        }

        [Fact]
        public void ARecipeWaitsForItsShiftAndItsBlueprint()
        {
            var open = Wall.Recipe;
            Assert.Equal(OpResult.Ok, BuildRules.CheckRecipe(open, 1, blueprintOwned: false));
            var later = new RecipeDef { Id = "later", Building = Wall, Inputs = open.Inputs, UnlockShift = 3 };
            Assert.Equal(OpResult.RecipeLocked, BuildRules.CheckRecipe(later, 2, blueprintOwned: true));
            Assert.Equal(OpResult.Ok, BuildRules.CheckRecipe(later, 3, blueprintOwned: true));
            var card = Db.ShopItems[0];
            var drawn = new RecipeDef { Id = "drawn", Building = Wall, Inputs = open.Inputs, UnlockShift = 3, Blueprint = card };
            // The blueprint is asked for first: it is what the team can do something about.
            Assert.Equal(OpResult.NeedsBlueprint, BuildRules.CheckRecipe(drawn, 1, blueprintOwned: false));
            Assert.Equal(OpResult.RecipeLocked, BuildRules.CheckRecipe(drawn, 1, blueprintOwned: true));
            Assert.Equal(OpResult.Ok, BuildRules.CheckRecipe(drawn, 3, blueprintOwned: true));
        }

        [Theory]
        [InlineData(3, 1.0, 300, 3)]
        [InlineData(3, 0.5, 300, 1)]
        [InlineData(4, 0.5, 200, 2)]
        [InlineData(3, 1.0, 150, 1)]
        [InlineData(3, 1.0, 299, 2)]
        [InlineData(4, 0.5, 99, 0)]
        [InlineData(3, 1.0, 0, 0)]
        public void TheRefundIsThePhasesPartOfWhatIsLeftStanding_RoundedDown(int count, double ratio, int hp, int back)
        {
            int maxHp = count == 4 ? 200 : 300;
            Assert.Equal(back, BuildRules.Refund(count, ratio, hp, maxHp));
        }

        // --- placing ----------------------------------------------------------------------------

        [Fact]
        public void AWallAndAChestGoUpOutOfTheLogsInThePockets()
        {
            var (world, host) = InPrep(logs: 7);

            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz, rotation: 1));
            var placed = world.Events.OfType<ConstructPlacedEvent>().Single();
            Assert.Equal((host.Id, Wall.Index, (byte)Tx, (byte)Tz, (byte)1, (ushort)300, 0u),
                (placed.Player, placed.Building, placed.X, placed.Y, placed.Rotation, placed.Hp, placed.ContainerId));
            var wall = world.Constructs.At(Tx, Tz)!;
            Assert.Equal((Wall, 300, true, 0u), (wall.Def, wall.Hp, wall.AlongZ, wall.ContainerId));
            Assert.Equal((Centre(Tx), Centre(Tz)), (wall.X, wall.Z));
            Assert.Equal(4, Logs(host));

            Assert.Equal(OpResult.Ok, Place(world, host, Chest, Tx, Tz + 1, rotation: 3));
            placed = world.Events.OfType<ConstructPlacedEvent>().Single();
            var chest = world.Constructs.At(Tx, Tz + 1)!;
            Assert.Equal((Chest, 200, (byte)3), (chest.Def, chest.Hp, chest.Rotation));
            Assert.NotEqual(0u, chest.ContainerId);
            Assert.Equal(chest.ContainerId, placed.ContainerId);
            Assert.Equal(0, Logs(host));
            Assert.Equal(new[] { wall, chest }, world.Constructs.All);

            // The chest is a chest: it opens, takes things and gives them back.
            var box = world.GetContainer(chest.ContainerId)!;
            Assert.Equal("chest", box.Def.Id);
            SimDriver.Teleport(host, Centre(Tx - 1), Centre(Tz + 1));
            SimDriver.StepTicks(world, 15);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Open(0, box.Id)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 5)));
            var stone = host.Inventory.Entries.Single();
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.QuickMove(0, host.Inventory.Id, stone.EntryId)));
            Assert.Equal(5, Count(box, "stone"));
        }

        [Fact]
        public void TheLogsComeOutOfTheInventoryFirst_ThenOffTheHotbar()
        {
            var (world, host) = InPrep(logs: 2);
            Assert.True(world.Give(host.Hotbar, ItemStack.OfItem(Db.Item("log"), 3)));
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz));
            Assert.Equal((0, 2), (Count(host.Inventory, "log"), Count(host.Hotbar, "log")));
            Assert.Empty(host.Inventory.Entries);

            // Two are not three.
            Assert.Equal(OpResult.MissingMaterials, Place(world, host, Wall, Tx, Tz + 1));
            Assert.Null(world.Constructs.At(Tx, Tz + 1));
            Assert.Equal(2, Logs(host));
        }

        [Fact]
        public void NothingIsBuiltOutsidePrepAndDelivery_NorByTheDead()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            SimDriver.Teleport(host, Centre(Tx - 1), Centre(Tz));
            GiveLogs(world, host, 20);
            Assert.Equal(OpResult.WrongPhase, Place(world, host, Wall, Tx, Tz));

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            SimDriver.Teleport(host, Centre(Tx - 1), Centre(Tz));
            SimDriver.StepTicks(world, 15);
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz));
            IntoDelivery(world, host);
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz + 1));

            IntoPayday(world, host);
            Assert.Equal(OpResult.WrongPhase, Place(world, host, Wall, Tx + 1, Tz));
            Assert.Equal(OpResult.WrongPhase, Remove(world, host, Tx, Tz));
            Assert.Equal(2, world.Constructs.All.Count);

            // What is built stands into the next shift.
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 30);
            Assert.Equal(2, world.Run.Shift);
            Assert.Equal(2, world.Constructs.All.Count);
            world.DamagePlayer(host, 10000f);
            Assert.True(host.Dead);
            Assert.Equal(OpResult.Dead, Place(world, host, Wall, Tx + 1, Tz));
            Assert.Equal(OpResult.Dead, Remove(world, host, Tx, Tz));
        }

        [Fact]
        public void ATileTakesAConstructOnlyIfItIsOpenGroundOffTheStreet()
        {
            var (world, host) = InPrep(logs: 30);
            var map = world.Map;

            // A request that names nothing.
            Assert.Equal(OpResult.BadRequest, Place(world, host, Wall, 200, Tz));
            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.PlaceConstruct(0, Db.Buildings.Count, Tx, Tz, 0)));
            Assert.Equal(OpResult.BadRequest, Place(world, host, Wall, Tx, Tz, rotation: 2));
            Assert.Equal(OpResult.Ok, Place(world, host, Chest, Tx, Tz, rotation: 2));

            // The street stays open, every tile of it.
            Assert.Equal(TileType.Street, map.GetTile(Tx, Tz - 1));
            Assert.Equal(OpResult.OnStreet, Place(world, host, Wall, Tx, Tz - 1));
            Assert.Equal(OpResult.OnStreet, Place(world, host, Wall, Tx - 1, Tz - 2));
            for (int tx = 0; tx < map.Cols; tx++)
                Assert.Equal(OpResult.OnStreet, BuildRules.CheckTile(map, world.Constructs, tx, map.Streets[0].Rect.Y));

            // One construct a tile; and none where a mailbox stands or a house, a tree or a fence is in the way.
            Assert.Equal(OpResult.Blocked, Place(world, host, Wall, Tx, Tz));
            var box = map.Destinations[3];
            int boxTx = (int)(box.MailboxX / SimConstants.TileSize), boxTz = (int)(box.MailboxZ / SimConstants.TileSize);
            Assert.Equal((Tx - 3, Tz), (boxTx, boxTz));
            Assert.Equal(OpResult.Blocked, Place(world, host, Wall, boxTx, boxTz));
            Assert.True(map.IsNavBlocked(Tx + 2, Tz + 1));
            Assert.Equal(OpResult.Blocked, Place(world, host, Wall, Tx + 2, Tz + 1));
            var po = map.PostOffice;
            foreach (var (x, z) in new[] { (po.IntakeX, po.IntakeZ), (po.SpawnPadX, po.SpawnPadZ), (po.ShopX, po.ShopZ), (map.Chests[0].X, map.Chests[0].Z) })
                Assert.Equal(OpResult.Blocked, BuildRules.CheckTile(map, world.Constructs, (int)(x / SimConstants.TileSize), (int)(z / SimConstants.TileSize)));
            for (int slot = 0; slot < PostOfficeDef.Seats; slot++)
            {
                po.Seat(slot, out float sx, out float sz);
                Assert.Equal(OpResult.Blocked, BuildRules.CheckTile(map, world.Constructs, (int)(sx / SimConstants.TileSize), (int)(sz / SimConstants.TileSize)));
            }

            Assert.Single(world.Constructs.All);
            Assert.Equal(26, Logs(host));
        }

        [Fact]
        public void ATileIsBuiltOnFromWithinReach_AndNotIntoABody()
        {
            var (world, host) = InPrep(logs: 30);

            // 8.5 m to the middle of the tile, from where the carrier was these last ticks.
            SimDriver.Teleport(host, Centre(Tx - 1) - 0.6f, Centre(Tz));
            SimDriver.StepTicks(world, 15);
            Assert.Equal(OpResult.OutOfRange, Place(world, host, Wall, Tx + 3, Tz));
            SimDriver.Teleport(host, Centre(Tx - 1), Centre(Tz));
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx + 3, Tz));
            SimDriver.StepTicks(world, 15);

            // Not onto the tile the carrier stands on, nor onto one their shoulder reaches over.
            Assert.Equal(OpResult.Occupied, Place(world, host, Wall, Tx - 1, Tz));
            SimDriver.Teleport(host, Centre(Tx - 1) + 0.8f, Centre(Tz));
            SimDriver.StepTicks(world, 15);
            Assert.Equal(OpResult.Occupied, Place(world, host, Wall, Tx, Tz));
            SimDriver.Teleport(host, Centre(Tx - 1) + 0.5f, Centre(Tz));
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz));

            // Nor onto a raider.
            var raider = world.SpawnEnemy(Db.Enemy("barbarian"), Centre(Tx + 1), Centre(Tz))!;
            Assert.Equal(OpResult.Occupied, Place(world, host, Wall, Tx + 1, Tz));
            world.DamageEnemy(raider, 10000f, host);
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx + 1, Tz));
        }

        // --- taking down ------------------------------------------------------------------------

        [Fact]
        public void TakenDownInPrepAConstructGivesAllOfItsLogsBack_InDeliveryHalf()
        {
            var (world, host) = InPrep(logs: 7);
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz));
            Assert.Equal(OpResult.Ok, Place(world, host, Chest, Tx, Tz + 1));
            Assert.Equal(0, Logs(host));

            Assert.Equal(OpResult.Ok, Remove(world, host, Tx, Tz));
            var removed = world.Events.OfType<ConstructRemovedEvent>().Single();
            Assert.Equal(((byte)Tx, (byte)Tz, host.Id, false), (removed.X, removed.Y, removed.Player, removed.Destroyed));
            Assert.Null(world.Constructs.At(Tx, Tz));
            Assert.Equal(3, Logs(host));
            Assert.Equal(OpResult.BadRequest, Remove(world, host, Tx, Tz));

            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz));
            IntoDelivery(world, host);
            uint chestId = world.Constructs.At(Tx, Tz + 1)!.ContainerId;
            Assert.Equal(OpResult.Ok, Remove(world, host, Tx, Tz));
            Assert.Equal(1, Logs(host));
            Assert.Equal(OpResult.Ok, Remove(world, host, Tx, Tz + 1));
            Assert.Equal(3, Logs(host));
            Assert.Empty(world.Constructs.All);
            // The chest's container went with it.
            Assert.Null(world.GetContainer(chestId));
        }

        [Fact]
        public void AnybodyOfTheTeamTakesDownWhatAnybodyBuilt_FromWithinReach()
        {
            var (world, host) = InPrep(logs: 3);
            var mate = world.AddPlayer("mate")!;
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz));
            Assert.Equal(OpResult.OutOfRange, Remove(world, mate, Tx, Tz));
            SimDriver.Teleport(mate, Centre(Tx + 2), Centre(Tz));
            SimDriver.StepTicks(world, 15);
            Assert.Equal(OpResult.Ok, Remove(world, mate, Tx, Tz));
            Assert.Equal((0, 3), (Logs(host), Logs(mate)));
        }

        [Fact]
        public void ABatteredWallGivesBackLess()
        {
            var (world, host) = InPrep(logs: 3);
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz));
            var wall = world.Constructs.At(Tx, Tz)!;
            world.DamageConstruct(wall, 150f);
            var damaged = world.Events.OfType<ConstructDamagedEvent>().Single();
            Assert.Equal(((byte)Tx, (byte)Tz, (ushort)150), (damaged.X, damaged.Y, damaged.Hp));
            Assert.Equal(150, wall.Hp);
            Assert.Equal(OpResult.Ok, Remove(world, host, Tx, Tz));
            Assert.Equal(1, Logs(host));
        }

        [Fact]
        public void AChestIsTakenDownOnlyOnceItIsEmpty()
        {
            var (world, host) = InPrep(logs: 4);
            Assert.Equal(OpResult.Ok, Place(world, host, Chest, Tx, Tz));
            var box = world.GetContainer(world.Constructs.At(Tx, Tz)!.ContainerId)!;
            Assert.True(world.Give(box, ItemStack.OfItem(Db.Item("stone"), 5)));
            Assert.Equal(OpResult.NotEmpty, Remove(world, host, Tx, Tz));
            Assert.NotNull(world.Constructs.At(Tx, Tz));

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Open(0, box.Id)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.QuickMove(0, box.Id, box.Entries.Single().EntryId)));
            Assert.Equal(OpResult.Ok, Remove(world, host, Tx, Tz));
            // Whoever had it open has it open no longer.
            Assert.Contains(world.Events.OfType<ContainerClosedEvent>(), e => e.ContainerId == box.Id);
            Assert.Equal(0u, host.OpenContainer);
            Assert.Equal((4, 5), (Logs(host), Count(host.Inventory, "stone") + Count(host.Hotbar, "stone")));
            SimDriver.StepTicks(world, 5);
        }

        [Fact]
        public void WhatComesBackHasToFitThePockets()
        {
            var (world, host) = InPrep(logs: 3);
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz));
            var log = Db.Item("log");
            while (world.Give(host.Inventory, ItemStack.OfItem(log, log.MaxStack))) { }
            while (world.Give(host.Hotbar, ItemStack.OfItem(log, log.MaxStack))) { }
            int before = Logs(host);

            Assert.Equal(OpResult.DoesNotFit, Remove(world, host, Tx, Tz));
            Assert.NotNull(world.Constructs.At(Tx, Tz));
            Assert.Equal(before, Logs(host));

            // Room for two is not room for three.
            var stack = host.Inventory.Entries.First();
            stack.Stack.ItemCount -= 2;
            host.Inventory.MarkDirty(stack.EntryId);
            Assert.Equal(OpResult.DoesNotFit, Remove(world, host, Tx, Tz));
            Assert.Equal(before - 2, Logs(host));
            stack.Stack.ItemCount -= 1;
            host.Inventory.MarkDirty(stack.EntryId);
            Assert.Equal(OpResult.Ok, Remove(world, host, Tx, Tz));
            Assert.Equal(before, Logs(host));
        }

        // --- in the way -------------------------------------------------------------------------

        [Fact]
        public void AWallStandsInACarriersWay_AcrossItsWholeTile()
        {
            var (world, host) = InPrep(logs: 3);
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz, rotation: 1));
            float r = (float)Db.Balance.GetDouble("playerRadius");
            float face = Centre(Tx) - Wall.Radius - r;

            // Straight at it, and at it towards either end.
            foreach (float z in new[] { Centre(Tz), Centre(Tz) - 0.7f, Centre(Tz) + 0.7f })
            {
                SimDriver.Teleport(host, Centre(Tx - 1), z);
                Walk(world, host, 1f, 0f, 60);
                Assert.InRange(host.Move.X, face - 0.02f, face + 0.02f);
                Assert.InRange(host.Move.Z, z - 0.02f, z + 0.02f);
            }

            // At its very end a walker slides off its round corner and is past: one wall is no fence.
            SimDriver.Teleport(host, Centre(Tx - 1), Centre(Tz) - 0.95f);
            Walk(world, host, 1f, 0f, 60);
            Assert.True(host.Move.X > Centre(Tx) + 1f, $"x {host.Move.X}");

            // Along it there is nothing in the way, and past its tile neither.
            SimDriver.Teleport(host, Centre(Tx) - 0.7f, Centre(Tz) - 0.9f);
            Walk(world, host, 0f, 1f, 20);
            Assert.True(host.Move.Z > Centre(Tz) + 0.9f, $"z {host.Move.Z}");
            SimDriver.Teleport(host, Centre(Tx - 1), Centre(Tz + 1));
            Walk(world, host, 1f, 0f, 60);
            Assert.True(host.Move.X > Centre(Tx) + 1f, $"x {host.Move.X}");
        }

        [Fact]
        public void WallsThatMeetCloseTheirCorner()
        {
            var (world, host) = InPrep(logs: 9);
            // One along the street, one running south from its middle.
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz, rotation: 0));
            var across = world.Constructs.At(Tx, Tz)!;
            Assert.Equal(ConstructField.ArmEast | ConstructField.ArmWest, world.Constructs.Arms(across));
            int version = world.Constructs.Version;
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz + 1, rotation: 1));
            Assert.True(world.Constructs.Version > version);
            var down = world.Constructs.At(Tx, Tz + 1)!;
            Assert.Equal(ConstructField.ArmEast | ConstructField.ArmWest | ConstructField.ArmSouth, world.Constructs.Arms(across));
            Assert.Equal(ConstructField.ArmNorth | ConstructField.ArmSouth, world.Constructs.Arms(down));

            // A wall beside it that runs the same way joins nothing: they stand in a row already.
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx + 1, Tz, rotation: 0));
            Assert.Equal(ConstructField.ArmEast | ConstructField.ArmWest | ConstructField.ArmSouth, world.Constructs.Arms(across));

            // Nobody slips through between the two: from the west along the seam of the two tiles.
            float r = (float)Db.Balance.GetDouble("playerRadius");
            SimDriver.Teleport(host, Centre(Tx - 1), Centre(Tz) + 1f);
            Walk(world, host, 1f, 0f, 60);
            Assert.InRange(host.Move.X, Centre(Tx) - Wall.Radius - r - 0.05f, Centre(Tx) - Wall.Radius - r + 0.05f);
        }

        [Fact]
        public void AChestIsInTheWayToo_ButNotOfAJump()
        {
            var (world, host) = InPrep(logs: 4);
            Assert.Equal(OpResult.Ok, Place(world, host, Chest, Tx, Tz));
            float r = (float)Db.Balance.GetDouble("playerRadius");
            Walk(world, host, 1f, 0f, 60);
            Assert.InRange(host.Move.X, Centre(Tx) - Chest.Radius - r - 0.02f, Centre(Tx) - Chest.Radius - r + 0.02f);

            // Round it there is room on its own tile.
            SimDriver.Teleport(host, Centre(Tx - 1), Centre(Tz) + 0.9f);
            Walk(world, host, 1f, 0f, 60);
            Assert.True(host.Move.X > Centre(Tx) + 1f, $"x {host.Move.X}");

            // Above its lid a body is not pushed out of it.
            float x = Centre(Tx), z = Centre(Tz);
            Assert.False(world.Constructs.Push(ref x, ref z, Chest.Height, r, carrier: true));
            Assert.True(world.Constructs.Push(ref x, ref z, Chest.Height - 0.1f, r, carrier: true));
        }

        [Fact]
        public void ATilePathKeepsOffWhatIsBuilt()
        {
            var (world, host) = InPrep(logs: 30);
            var path = new List<(float X, float Z)>();
            float fromX = Centre(Tx), fromZ = Centre(Tz - 1), toX = Centre(Tx), toZ = Centre(Tz + 1);
            Assert.True(TilePathfinder.Find(world.Map, fromX, fromZ, toX, toZ, path, null, world.Constructs));
            Assert.Equal(2, path.Count);

            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz));
            Assert.True(world.Constructs.Blocks(Tx, Tz));
            Assert.True(TilePathfinder.Find(world.Map, fromX, fromZ, toX, toZ, path, null, world.Constructs));
            Assert.DoesNotContain((Centre(Tx), Centre(Tz)), path);
            // Round the end of the wall, and not across its corner.
            Assert.Equal(4, path.Count);
            // Whoever does not ask about the constructs is told the old way.
            Assert.True(TilePathfinder.Find(world.Map, fromX, fromZ, toX, toZ, path));
            Assert.Equal(2, path.Count);
        }

        // --- raiders ----------------------------------------------------------------------------

        /// <summary>
        /// Shuts the host in on tile (22, 16): walls on every tile about it that a raider could
        /// come over (the eighth is a tree's).
        /// </summary>
        private void WallIn(SimWorld world, Player host)
        {
            Assert.True(world.Map.IsNavBlocked(Tx + 1, Tz + 2));
            SimDriver.Teleport(host, Centre(Tx), Centre(Tz + 1));
            SimDriver.StepTicks(world, 15);
            foreach (var (tx, tz, rot) in new[]
            {
                (Tx - 1, Tz, 0), (Tx, Tz, 0), (Tx + 1, Tz, 0), (Tx - 1, Tz + 1, 1), (Tx + 1, Tz + 1, 1), (Tx - 1, Tz + 2, 0), (Tx, Tz + 2, 0),
            })
                Assert.Equal(OpResult.Ok, Place(world, host, Wall, tx, tz, rot));
        }

        [Fact]
        public void ARaiderGoesRoundAWallThatLeavesAWay()
        {
            var (world, host) = InPrep(logs: 3);
            IntoDelivery(world, host);
            SimDriver.Teleport(host, Centre(Tx), Centre(Tz + 1));
            SimDriver.StepTicks(world, 15);
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz));
            var wall = world.Constructs.At(Tx, Tz)!;
            world.SpawnEnemy(Db.Enemy("barbarian"), Centre(Tx), Centre(Tz - 1));

            RunUntil(world, () => host.Hp < host.MaxHp, 8);
            Assert.Equal(Wall.Hp, wall.Hp);
        }

        [Fact]
        public void ARaiderBreaksTheWallThatShutsItOut_ABlowEveryAttackRate()
        {
            var (world, host) = InPrep(logs: 21);
            IntoDelivery(world, host);
            WallIn(world, host);
            var raider = world.SpawnEnemy(Db.Enemy("barbarian"), Centre(Tx), Centre(Tz - 1))!;

            int blows = 0;
            Construct? struck = null;
            for (int t = 0; t < 12 * Tps; t++)
            {
                world.Events.Clear();
                world.Step();
                foreach (var e in world.Events.OfType<ConstructDamagedEvent>())
                {
                    blows++;
                    struck ??= world.Constructs.At(e.X, e.Y);
                    Assert.Same(struck, world.Constructs.At(e.X, e.Y));
                }
            }
            // About a blow a second once it has walked up, 10 HP each; and the carrier behind the wall is not hurt.
            Assert.NotNull(struck);
            Assert.InRange(blows, 9, 12);
            Assert.Equal(Wall.Hp - blows * (int)raider.Damage, struck!.Hp);
            Assert.Equal(host.MaxHp, host.Hp);
            // It stands at the wall, not in it.
            Assert.True(world.Constructs.Distance(struck, raider.X, raider.Z, out _, out _) >= Wall.Radius + raider.Radius - 0.02f);
        }

        [Fact]
        public void ThreeRaidersAreThroughAWoodenWallInAboutTenSeconds_AndThenAtTheCarrier()
        {
            var (world, host) = InPrep(logs: 21);
            IntoDelivery(world, host);
            WallIn(world, host);
            var def = Db.Enemy("barbarian");
            for (int i = 0; i < 3; i++) world.SpawnEnemy(def, Centre(Tx) - 0.9f + i * 0.9f, Centre(Tz - 1));

            int fellAt = -1;
            ConstructRemovedEvent? fell = null;
            for (int t = 0; t < 40 * Tps && fell == null; t++)
            {
                world.Events.Clear();
                world.Step();
                fell = world.Events.OfType<ConstructRemovedEvent>().FirstOrDefault();
                fellAt = t / Tps;
            }
            Assert.NotNull(fell);
            Assert.True(fell!.Destroyed);
            Assert.True(fell.Player.IsNone);
            Assert.InRange(fellAt, 9, 16);
            Assert.Equal(6, world.Constructs.All.Count);
            Assert.Equal(host.MaxHp, host.Hp);

            // The way is open now.
            RunUntil(world, () => host.Hp < host.MaxHp, 8);
        }

        [Fact]
        public void TheBlowAtACarrierBehindAWallLandsOnTheWall()
        {
            var (world, host) = InPrep(logs: 3);
            IntoDelivery(world, host);
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz));
            var wall = world.Constructs.At(Tx, Tz)!;
            // Right behind it, within a raider's reach from the street side.
            SimDriver.Teleport(host, Centre(Tx), Centre(Tz) + 0.8f);
            var raider = world.SpawnEnemy(Db.Enemy("barbarian"), Centre(Tx), Centre(Tz) - 0.8f)!;
            Assert.Same(wall, world.Constructs.Between(raider.X, raider.Z, host.Move.X, host.Move.Z));
            Assert.Null(world.Constructs.Between(raider.X, raider.Z, raider.X + 3f, raider.Z - 2f));

            RunUntil(world, () => wall.Hp < Wall.Hp, 3);
            Assert.Equal(host.MaxHp, host.Hp);
            SimDriver.StepTicks(world, 3 * Tps);
            Assert.True(wall.Hp <= Wall.Hp - 3 * (int)raider.Damage, $"wall at {wall.Hp}");
            Assert.Equal(host.MaxHp, host.Hp);
        }

        [Fact]
        public void ABrokenChestSpillsWhatItHeldIntoABagThatIsNobodys()
        {
            var (world, host) = InPrep(logs: 4);
            Assert.Equal(OpResult.Ok, Place(world, host, Chest, Tx, Tz));
            var chest = world.Constructs.At(Tx, Tz)!;
            var box = world.GetContainer(chest.ContainerId)!;
            Assert.True(world.Give(box, ItemStack.OfItem(Db.Item("stone"), 5)));
            var home = world.Map.Destinations[0];
            Assert.True(world.Give(box, ItemStack.OfMail(TestContent.Mail("letter", home.Address, _nextMailId++))));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Open(0, box.Id)));
            Assert.Equal(box.Id, host.OpenContainer);

            world.DamageConstruct(chest, 199f);
            Assert.Equal(1, chest.Hp);
            Assert.Empty(world.Bags);
            world.DamageConstruct(chest, 0.2f);
            Assert.Null(world.Constructs.At(Tx, Tz));
            var bag = Assert.Single(world.Bags);
            Assert.True(bag.Owner.IsNone);
            Assert.Equal((chest.X, chest.Z), (bag.X, bag.Z));
            Assert.Equal(5, Count(bag.Container, "stone"));
            Assert.Single(bag.Container.Entries, e => e.Stack.Kind == StackKind.Mail);
            Assert.Null(world.GetContainer(box.Id));
            Assert.Equal(0u, host.OpenContainer);
            Assert.Contains(world.Events.OfType<ConstructRemovedEvent>(), e => e.Destroyed && e.X == Tx && e.Y == Tz);
            Assert.Contains(world.Events.OfType<BagDroppedEvent>(), e => e.ContainerId == bag.Id);
            Assert.Contains(world.Events.OfType<ContainerClosedEvent>(), e => e.ContainerId == box.Id);

            // A blow at what is gone is a blow at nothing.
            world.DamageConstruct(chest, 50f);
            SimDriver.StepTicks(world, 5);
            Assert.Single(world.Bags);
        }

        // --- the run ----------------------------------------------------------------------------

        [Fact]
        public void WhenTheRunIsOverNothingBuiltIsLeftStanding()
        {
            var (world, host) = InPrep(logs: 7);
            Assert.Equal(OpResult.Ok, Place(world, host, Wall, Tx, Tz));
            Assert.Equal(OpResult.Ok, Place(world, host, Chest, Tx, Tz + 1));
            uint chestId = world.Constructs.At(Tx, Tz + 1)!.ContainerId;
            IntoDelivery(world, host);

            // Nothing delivered: the quota is missed and the run is over.
            RunUntil(world, () => world.Run.Phase == RunPhase.Results, 400);
            Assert.Equal(2, world.Constructs.All.Count);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.ContinueRun(0)));
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);
            Assert.Empty(world.Constructs.All);
            Assert.Null(world.Constructs.At(Tx, Tz));
            Assert.Null(world.GetContainer(chestId));
            Assert.Single(world.Events.OfType<ConstructsClearedEvent>());

            // And a run that had nothing built says nothing of it.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Empty(world.Events.OfType<ConstructsClearedEvent>());
        }
    }
}
