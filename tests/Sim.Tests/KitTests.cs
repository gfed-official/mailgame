using System.Linq;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The starting kits (doc 03 §4.4, doc 01 §5.2, doc 06 §5–§6): picked in the Lobby from those the
    /// player's Postal Rank has opened; personal items into the pockets as the run starts (or as
    /// one joins in Prep), the host's team items into the chest by the Post Office and its bike by
    /// the counter, once a run.
    /// </summary>
    public class KitTests
    {
        private static ContentDb Db => TestContent.Db;
        private uint _nextRequest = 1;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static int Count(GridContainer c, string item) =>
            c.Entries.Where(e => e.Stack.Kind == StackKind.Item && Db.Items[e.Stack.DefIndex].Id == item).Sum(e => e.Stack.Count);

        [Fact]
        public void TheContentHasTheKitsOfTheDocs()
        {
            Assert.Equal(new[] { "land", "sea", "pipes" }, Db.Kits.Select(k => k.Id));
            Assert.Same(Db.Kit("land"), Db.DefaultKit);
            Assert.Equal((1, 2, 10), (Db.Kit("land").Rank, Db.Kit("sea").Rank, Db.Kit("pipes").Rank));
            var land = Db.Kit("land");
            Assert.Equal(new[] { ("axe", 1), ("fiber", 4) }, land.Personal.Select(s => (s.Item.Id, s.Count)));
            Assert.Equal(new[] { ("log", 10) }, land.Team.Select(s => (s.Item.Id, s.Count)));
            Assert.Equal("bike", Assert.Single(land.TeamVehicles).Id);
            Assert.Equal(new[] { ("axe", 1), ("rope", 2) }, Db.Kit("sea").Personal.Select(s => (s.Item.Id, s.Count)));
            Assert.Equal(new[] { ("pickaxe", 1) }, Db.Kit("pipes").Personal.Select(s => (s.Item.Id, s.Count)));
            Assert.Equal(new[] { ("iron_ingot", 10) }, Db.Kit("pipes").Team.Select(s => (s.Item.Id, s.Count)));
        }

        [Fact]
        public void AKitIsPickedInTheLobby_UpToThePlayersRank()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = true });
            var ada = world.AddPlayer("Ada")!;
            Assert.Same(Db.DefaultKit, ada.Kit);
            Assert.Equal(OpResult.RankTooLow, Request(world, ada, SimRequest.SetKit(0, Db.Kit("sea").Index)));
            world.SetRank(ada, 2);
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.SetKit(0, Db.Kit("sea").Index)));
            Assert.Same(Db.Kit("sea"), ada.Kit);
            Assert.Equal(OpResult.RankTooLow, Request(world, ada, SimRequest.SetKit(0, Db.Kit("pipes").Index)));
            Assert.Equal(OpResult.BadRequest, Request(world, ada, SimRequest.SetKit(0, Db.Kits.Count)));
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            // Once the run has started the kit stays as it was.
            Assert.Equal(OpResult.WrongPhase, Request(world, ada, SimRequest.SetKit(0, Db.DefaultKit.Index)));
            Assert.Same(Db.Kit("sea"), ada.Kit);
        }

        [Fact]
        public void AsTheRunStarts_EachGetsTheirKit_AndTheTeamTheHosts()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = true });
            var ada = world.AddPlayer("Ada")!;
            var bo = world.AddPlayer("Bo")!;
            world.SetRank(bo, 2);
            Assert.Equal(OpResult.Ok, Request(world, bo, SimRequest.SetKit(0, Db.Kit("sea").Index)));
            Assert.Empty(ada.Inventory.Entries);
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.StartRun(0)));

            Assert.Equal((1, 4), (Count(ada.Inventory, "axe"), Count(ada.Inventory, "fiber")));
            Assert.Equal((1, 2, 0), (Count(bo.Inventory, "axe"), Count(bo.Inventory, "rope"), Count(bo.Inventory, "fiber")));
            // Ada is the host: her Land kit gives the team Logs in the chest by the Post Office and a Bike by the counter.
            Assert.Equal(10, Count(world.MapChests[0], "log"));
            var bike = Assert.Single(world.Vehicles);
            Assert.Equal("bike", bike.Def.Id);
            var given = world.Events.OfType<KitGivenEvent>().ToList();
            Assert.Equal(3, given.Count);
            Assert.Contains(given, e => e.Team && e.Player == ada.Id && e.Kit == Db.DefaultKit.Index);
            Assert.Contains(given, e => !e.Team && e.Player == bo.Id && e.Kit == Db.Kit("sea").Index);

            // Who joins in Prep gets their own on the way in; nothing more for the team.
            var cara = world.AddPlayer("Cara")!;
            Assert.Equal(1, Count(cara.Inventory, "axe"));
            Assert.Single(world.Vehicles);
            Assert.Equal(10, Count(world.MapChests[0], "log"));

            // Who loses the connection and comes back is not given it again.
            world.Disconnect(cara.Id);
            world.Step();
            world.Reconnect(cara);
            Assert.Equal(1, Count(cara.Inventory, "axe"));
        }

        [Fact]
        public void EveryRunHandsTheKitsOutAnew_AndWithoutKitsNothingIsGiven()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = true, QuickStart = true });
            var ada = world.AddPlayer("Ada")!;
            world.Step();
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.Equal(1, Count(ada.Inventory, "axe"));
            for (int i = 0; i < 3000 * 30 && world.Run.Phase != RunPhase.Lobby; i++) world.Step();
            for (int i = 0; i < 60 && world.Run.Phase != RunPhase.Delivery; i++) world.Step();
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.Equal(1, Count(ada.Inventory, "axe"));
            Assert.Single(world.Vehicles);

            var plain = new SimWorld(Db, new SimSettings { Seed = 3, Raids = false, Draft = false });
            var bo = plain.AddPlayer("Bo")!;
            Assert.Equal(OpResult.Ok, Request(plain, bo, SimRequest.StartRun(0)));
            Assert.Empty(bo.Inventory.Entries);
            Assert.Empty(plain.Vehicles);
            Assert.Empty(plain.MapChests[0].Entries);
        }
    }
}
