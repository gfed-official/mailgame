using System.Linq;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Pneumatic pipes (doc 04 §2.2, doc 10 §2.1, doc 11 "pipe speed 5 m/s, spacing 1 m"): pipes,
    /// junctions, underground pipes, inlets and outlets behind bp_pipes; an inlet sends each piece
    /// in a capsule to the outlet chosen for it, which puts it into what stands in front of it.
    /// </summary>
    public class PipeTests
    {
        private const int Tps = 30;
        private static ContentDb Db => TestContent.Db;
        private static BuildingDef Pipe => Db.Building("pipe");
        private static BuildingDef Under => Db.Building("pipe_underground");
        private static BuildingDef Junction => Db.Building("pipe_junction");
        private static BuildingDef Inlet => Db.Building("pipe_inlet");
        private static BuildingDef Outlet => Db.Building("pipe_outlet");
        private static BuildingDef Chest => Db.Building("chest");
        private static BuildingDef Belt => Db.Building("belt_mk1");

        private const int E = 0, S = 1, W = 2, N = 3;

        private uint _nextRequest = 1;
        private uint _nextMail = 980000;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        /// <summary>Prep of shift 3 (bp_pipes is sold from it), the blueprint bought unless not asked for, the pockets full.</summary>
        private (SimWorld World, Player Host) InPrep(bool blueprint = true)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.None, 3);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal((RunPhase.Prep, 3), (world.Run.Phase, world.Run.Shift));
            world.Economy.AddLoot(2000);
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("glass"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("log"), 10)));
            if (blueprint)
            {
                var po = world.Map.PostOffice;
                SimDriver.Teleport(host, po.ShopX + 1.0f, po.ShopZ);
                SimDriver.StepTicks(world, 2);
                Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Buy(0, Db.ShopItem("bp_pipes").Index)));
            }
            return (world, host);
        }

        /// <summary>Builds standing on an open tile beside it (none of the four: on the tile itself).</summary>
        private OpResult Place(SimWorld world, Player p, BuildingDef def, int tx, int tz, int rotation)
        {
            Beside(world, p, tx, tz);
            return Request(world, p, SimRequest.PlaceConstruct(0, def.Index, tx, tz, rotation));
        }

        /// <summary>Stands the carrier on an open tile beside (tx, tz).</summary>
        private static void Beside(SimWorld world, Player p, int tx, int tz)
        {
            float x = BuildRules.TileCentre(tx), z = BuildRules.TileCentre(tz);
            foreach (var (dx, dz) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1) })
            {
                int nx = tx + dx, nz = tz + dz;
                if (!world.Map.InBounds(nx, nz) || world.Map.IsNavBlocked(nx, nz) || world.Constructs.At(nx, nz) != null) continue;
                x = BuildRules.TileCentre(nx);
                z = BuildRules.TileCentre(nz);
                break;
            }
            SimDriver.Teleport(p, x, z);
            SimDriver.StepTicks(world, 2);
        }

        /// <summary>A request about the construct on (tx, tz), made standing beside it.</summary>
        private OpResult At(SimWorld world, Player p, int tx, int tz, SimRequest request)
        {
            Beside(world, p, tx, tz);
            return Request(world, p, request);
        }

        private void Build(SimWorld world, Player p, BuildingDef def, int tx, int tz, int rotation) =>
            Assert.Equal(OpResult.Ok, Place(world, p, def, tx, tz, rotation));

        private MailItem Mail(SimWorld world, AddressId address, string kind = "letter") =>
            TestContent.Mail(kind, address, _nextMail++, world.Run.Shift);

        private static int MailIn(GridContainer c) => c.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Mail.Count);

        /// <summary>
        /// The Intake's inlet on the outfeed (6, 8) facing north; pipes up the yard to a junction at
        /// (6, 4) on the open row north of the Post Office: west an outlet at (4, 4) into a chest at
        /// (3, 4), east pipes to an outlet at (11, 4) into a chest at (12, 4). Both are defaults
        /// until a filter is set.
        /// </summary>
        private (GridContainer West, GridContainer East) Network(SimWorld world, Player host)
        {
            Assert.Equal((6, 8), (world.Belts.OutfeedX, world.Belts.OutfeedZ));
            Build(world, host, Inlet, 6, 8, N);
            for (int z = 7; z >= 5; z--) Build(world, host, Pipe, 6, z, N);
            Build(world, host, Junction, 6, 4, 0);
            Build(world, host, Pipe, 5, 4, W);
            Build(world, host, Outlet, 4, 4, W);
            Build(world, host, Chest, 3, 4, 0);
            for (int x = 7; x <= 10; x++) Build(world, host, Pipe, x, 4, E);
            Build(world, host, Outlet, 11, 4, E);
            Build(world, host, Chest, 12, 4, 0);
            return (world.GetContainer(world.Constructs.At(3, 4)!.ContainerId)!, world.GetContainer(world.Constructs.At(12, 4)!.ContainerId)!);
        }

        private int Tile(SimWorld world, int tx, int tz) => tz * world.Map.Cols + tx;

        private static void EmptyIntake(SimWorld world)
        {
            foreach (var e in world.Intake.Entries.ToList()) world.Intake.Remove(e.EntryId);
        }

        [Fact]
        public void ThePipes_AreContent_BehindBpPipes_OnOneKey()
        {
            Assert.Equal((BuildingBehaviour.Pipe, PipeRole.Pipe, 100), (Pipe.Behaviour, Pipe.PipeRole, Pipe.Hp));
            Assert.Equal((PipeRole.Underground, 200, true), (Under.PipeRole, Under.Hp, Under.OnStreet));
            Assert.Equal((PipeRole.Junction, 120), (Junction.PipeRole, Junction.Hp));
            Assert.Equal((PipeRole.Inlet, 150), (Inlet.PipeRole, Inlet.Hp));
            Assert.Equal((PipeRole.Outlet, 120, 1), (Outlet.PipeRole, Outlet.Hp, Outlet.FilterOutputs));
            Assert.Equal(new[] { ("iron_ingot", 1), ("glass", 1) }, Pipe.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            Assert.Equal(new[] { ("iron_ingot", 2), ("glass", 2) }, Under.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            Assert.Equal(new[] { ("iron_ingot", 2), ("glass", 1) }, Junction.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            Assert.Equal(new[] { ("iron_ingot", 3), ("glass", 1) }, Inlet.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            Assert.Equal(new[] { ("iron_ingot", 2) }, Outlet.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            var bp = Db.ShopItem("bp_pipes");
            Assert.Equal((ShopKind.Blueprint, 700, 3), (bp.Kind, bp.Price, bp.FromShift));
            foreach (var def in new[] { Pipe, Under, Junction, Inlet, Outlet }) Assert.Same(bp, def.Recipe.Blueprint);
            Assert.Equal(new[] { "pipe", "pipe_underground", "pipe_junction", "pipe_inlet", "pipe_outlet" }, Db.RecipeKeys[Pipe.Recipe.Key].Select(r => r.Building.Id));
            // Splitter and merger share a key now (doc 09 §2.7 Transport), so Build mode keeps ten (the Oil Pump is on the chest's).
            Assert.Equal(Db.Building("splitter").Recipe.Key, Db.Building("merger").Recipe.Key);
            Assert.Equal(10, Db.RecipeKeys.Count);
            // A straight pipe and an underground one are walked over and dragged; the boxes stand.
            Assert.True(Pipe.Drags && Under.Drags && !Junction.Drags);
            Assert.False(Pipe.Solid || Under.Solid);
            Assert.True(Junction.Solid && Inlet.Solid && Outlet.Solid);
            Assert.Equal(5.0, Db.Balance.GetDouble("pipeSpeed"));
            Assert.Equal(1.0, Db.Balance.GetDouble("pipeSpacing"));
        }

        [Fact]
        public void WithoutTheBlueprint_NoPipe_AndOnlyTheUndergroundOneGoesOnAStreet()
        {
            var (world, host) = InPrep(blueprint: false);
            Assert.Equal(OpResult.NeedsBlueprint, Place(world, host, Pipe, 8, 12, S));
            var (w2, h2) = InPrep();
            Assert.Equal(OpResult.OnStreet, Place(w2, h2, Pipe, 8, 13, S));
            Build(w2, h2, Under, 8, 13, S);
            Build(w2, h2, Under, 8, 14, S);
            Assert.True(w2.Pipes.Joined(Tile(w2, 8, 13), S));
            // A carrier walks over it along the street.
            SimDriver.Teleport(h2, BuildRules.TileCentre(6), BuildRules.TileCentre(13));
            Assert.True(SimDriver.WalkTo(w2, h2, BuildRules.TileCentre(10), BuildRules.TileCentre(13), maxTicks: 4 * Tps), $"the carrier stopped at x {h2.Move.X}");
        }

        [Fact]
        public void TheIntakesMail_GoesByPipe_ToTheOutletWhoseFilterCatchesIt_AndTheRestToTheNearestDefault()
        {
            var (world, host) = InPrep();
            var (west, east) = Network(world, host);
            var pipes = world.Pipes;
            Assert.Equal(2, pipes.Outlets.Count());
            Assert.Single(pipes.Inlets);
            int inlet = Tile(world, 6, 8), westOut = Tile(world, 4, 4), eastOut = Tile(world, 11, 4);
            Assert.Equal(6, pipes.Distance(westOut, inlet));
            Assert.Equal(9, pipes.Distance(eastOut, inlet));
            // The far outlet takes Small Packages; the near one, without a filter, the rest.
            var small = Db.MailKind("small");
            Assert.Equal(OpResult.Ok, At(world, host, 11, 4, SimRequest.Configure(0, 11, 4, SimRequest.ConfigureSet, 0, 0, new MailFilter(FilterKind.Kind, small.Index).Encode())));
            // An outlet filters by street too, as a sorter does (and back to none).
            Assert.Equal(OpResult.Ok, At(world, host, 4, 4, SimRequest.Configure(0, 4, 4, SimRequest.ConfigureSet, 0, 0, new MailFilter(FilterKind.Street, world.Map.Streets[0].Id).Encode())));
            Assert.Equal(OpResult.Ok, At(world, host, 4, 4, SimRequest.Configure(0, 4, 4, SimRequest.ConfigureSet, 0, 0, MailFilter.Empty.Encode())));

            EmptyIntake(world);
            var address = world.Map.Destinations[1].Address;
            for (int i = 0; i < 3; i++) world.Spawner.ReturnToIntake(Mail(world, address));
            for (int i = 0; i < 2; i++) world.Spawner.ReturnToIntake(Mail(world, address, "small"));
            Assert.Equal(5, MailIn(world.Intake));
            world.Events.Clear();
            world.Step();
            var sent = world.Events.OfType<PipeCapsuleEvent>().Single();
            Assert.Equal((6, 8), ((int)sent.FromX, (int)sent.FromY));
            // A metre apart at 5 m/s: one capsule each 0.2 s (6 ticks).
            SimDriver.StepTicks(world, 5);
            Assert.Equal(1, pipes.ItemCount);
            SimDriver.StepTicks(world, 1);
            Assert.Equal(2, pipes.ItemCount);

            // The last leaves 0.8 s in, 18 m from the far outlet: 3.6 s.
            SimDriver.StepTicks(world, 6 * Tps);
            Assert.True(0 == MailIn(world.Intake) + pipes.ItemCount, $"intake {MailIn(world.Intake)} [{string.Join(",", world.Intake.Entries.SelectMany(e => e.Stack.Mail).Select(m => m.Id + "/" + m.Kind))}], pipes {pipes.ItemCount}, west {MailIn(west)}, east {MailIn(east)}");
            Assert.Equal(3, MailIn(west));
            Assert.Equal(2, MailIn(east));
            Assert.All(east.Entries.SelectMany(e => e.Stack.Mail), m => Assert.Equal(small.Index, m.Kind));
        }

        [Fact]
        public void ACapsule_RunsAtFiveMetresASecond_ByTheShortestWay()
        {
            var (world, host) = InPrep();
            Network(world, host);
            EmptyIntake(world);
            world.Spawner.ReturnToIntake(Mail(world, world.Map.Destinations[1].Address));
            world.Step();
            var cap = world.Pipes.Capsules.Single();
            Assert.Equal(new[] { Tile(world, 6, 8), Tile(world, 6, 7), Tile(world, 6, 6), Tile(world, 6, 5), Tile(world, 6, 4), Tile(world, 5, 4), Tile(world, 4, 4) }, cap.Path);
            // 12 m at 5 m/s: 2.4 s, 72 ticks; it was sent in the last one.
            SimDriver.StepTicks(world, 70);
            Assert.Single(world.Pipes.Capsules);
            SimDriver.StepTicks(world, 2);
            Assert.Empty(world.Pipes.Capsules);
        }

        [Fact]
        public void APipeTakenDown_UnderTheCapsules_SendsThemBackToTheIntake_AndAFullOutletHoldsTheInlet()
        {
            var (world, host) = InPrep();
            var (west, east) = Network(world, host);
            var pipes = world.Pipes;
            // The west outlet's chest stands taken down: what reaches it waits there, eight at most.
            Assert.Equal(OpResult.Ok, At(world, host, 3, 4, SimRequest.RemoveConstruct(0, 3, 4)));
            // The east one takes only Small Packages, so letters have only the west one.
            var small = Db.MailKind("small");
            Assert.Equal(OpResult.Ok, At(world, host, 11, 4, SimRequest.Configure(0, 11, 4, SimRequest.ConfigureSet, 0, 0, new MailFilter(FilterKind.Kind, small.Index).Encode())));
            EmptyIntake(world);
            var address = world.Map.Destinations[1].Address;
            for (int i = 0; i < 12; i++) world.Spawner.ReturnToIntake(Mail(world, address));
            SimDriver.StepTicks(world, 5 * Tps);
            var outlet = pipes.OutletAt(Tile(world, 4, 4))!;
            Assert.Equal(pipes.Capacity, outlet.Queue.Count);
            Assert.Equal(12 - pipes.Capacity, MailIn(world.Intake));
            Assert.Equal(0, MailIn(east));

            // The outlet taken down puts its queue back into the Intake.
            Assert.Equal(OpResult.Ok, At(world, host, 4, 4, SimRequest.RemoveConstruct(0, 4, 4)));
            SimDriver.StepTicks(world, 2);
            Assert.Equal(12, MailIn(world.Intake));
            // Built again: capsules go; a pipe of the yard taken down under them sends them back.
            Build(world, host, Outlet, 4, 4, W);
            SimDriver.StepTicks(world, 14);
            Assert.True(pipes.Capsules.Count >= 2, $"{pipes.Capsules.Count} capsules under way");
            Assert.Equal(OpResult.Ok, At(world, host, 6, 6, SimRequest.RemoveConstruct(0, 6, 6)));
            SimDriver.StepTicks(world, 2);
            Assert.Equal(12, MailIn(world.Intake) + pipes.ItemCount);
            Assert.All(pipes.Capsules, c => Assert.DoesNotContain(Tile(world, 6, 7), c.Path));
            Assert.Equal(-1, pipes.Distance(Tile(world, 4, 4), Tile(world, 6, 8)));
        }

        [Fact]
        public void AnInserterIntoAnInlet_AnOutletOntoABelt_ABeltIntoAnInlet_AndAnOutletIntoAChest()
        {
            var (world, host) = InPrep();
            // Along row 22: a chest, an inserter, an inlet, two pipes, an outlet onto three belts,
            // they into a second inlet, a pipe, an outlet into a chest.
            Build(world, host, Chest, 6, 22, 0);
            Build(world, host, Db.Building("inserter"), 7, 22, E);
            Build(world, host, Inlet, 8, 22, E);
            Build(world, host, Pipe, 9, 22, E);
            Build(world, host, Pipe, 10, 22, E);
            Build(world, host, Outlet, 11, 22, E);
            for (int x = 12; x <= 14; x++) Build(world, host, Belt, x, 22, E);
            Build(world, host, Inlet, 15, 22, E);
            Build(world, host, Pipe, 16, 22, E);
            Build(world, host, Outlet, 17, 22, E);
            Build(world, host, Chest, 18, 22, 0);
            Assert.Equal(BeltEndKind.Inlet, world.Belts.SegmentAt(14, 22)!.End);
            var from = world.GetContainer(world.Constructs.At(6, 22)!.ContainerId)!;
            var to = world.GetContainer(world.Constructs.At(18, 22)!.ContainerId)!;
            var address = world.Map.Destinations[1].Address;
            for (int i = 0; i < 4; i++) Assert.True(world.Give(from, ItemStack.OfMail(Mail(world, address))));
            SimDriver.StepTicks(world, 10 * Tps);
            Assert.Equal((0, 4), (MailIn(from), MailIn(to)));
            Assert.Equal(0, world.Pipes.ItemCount + world.Belts.ItemCount);
        }

        [Fact]
        public void AnOutletBeforeAMailbox_TakesThatHousesMail_AndPutsItInInDelivery()
        {
            var (world, host) = InPrep();
            // House 2's mailbox on (12, 15): a chest east of it, an inserter, an inlet and an outlet facing it.
            var dest = world.Map.Destinations[1];
            Assert.Equal((12, 15), ((int)(dest.MailboxX / SimConstants.TileSize), (int)(dest.MailboxZ / SimConstants.TileSize)));
            Build(world, host, Chest, 16, 15, 0);
            Build(world, host, Db.Building("inserter"), 15, 15, W);
            Build(world, host, Inlet, 14, 15, W);
            Build(world, host, Outlet, 13, 15, W);
            var chest = world.GetContainer(world.Constructs.At(16, 15)!.ContainerId)!;
            for (int i = 0; i < 3; i++) Assert.True(world.Give(chest, ItemStack.OfMail(Mail(world, dest.Address))));
            Assert.True(world.Give(chest, ItemStack.OfMail(Mail(world, world.Map.Destinations[0].Address))));
            // Prep: the mailbox is shut; the house's letters wait in the outlet, the other in the chest (nowhere to go).
            SimDriver.StepTicks(world, 3 * Tps);
            var outlet = world.Pipes.OutletAt(Tile(world, 13, 15))!;
            Assert.Equal(3, outlet.Queue.Count);
            Assert.Equal(1, MailIn(chest));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            int deliveries = 0;
            for (int t = 0; t < 3 * Tps; t++)
            {
                world.Events.Clear();
                world.Step();
                deliveries += world.Events.OfType<DeliveredEvent>().Count(e => e.Machine == DeliveryMachine.Pipe && e.Player.IsNone);
            }
            Assert.Equal(3, deliveries);
            Assert.Equal(0, world.Pipes.ItemCount);
            Assert.Equal(1, MailIn(chest));
        }
    }
}
