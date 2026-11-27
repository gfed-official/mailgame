using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Doc 01 §2.4's Payday callouts (most deliveries, most damage, most built; constructs lost) and doc 09 §2.11's run
    /// stats on Results (raiders felled, damage, built; constructs lost over the run).
    /// </summary>
    public class MvpTests
    {
        private static ContentDb Db => TestContent.Db;
        private uint _nextRequest = 1;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        [Fact]
        public void Payday_NamesWhoDealtTheMostDamage_AndWhoBuiltTheMost_AndCountsTheConstructsLost_ResultsAddThemUp()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var ada = world.AddPlayer("ada")!;
            var bo = world.AddPlayer("bo")!;
            ada.MaxHp = ada.Hp = bo.MaxHp = bo.Hp = 100000f;
            world.SetPlaytest(ada, PlaytestFlags.Materials | PlaytestFlags.NoQuota, 1);
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.StartRun(0)));
            // Ada builds two walls in Prep, Bo none.
            var wall = Db.Building("wall_wood");
            foreach (int tx in new[] { 22, 25 })
            {
                SimDriver.Teleport(ada, BuildRules.TileCentre(tx - 1), BuildRules.TileCentre(15));
                Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.PlaceConstruct(0, wall.Index, tx, 15, 0)));
            }
            foreach (var p in world.Players) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            // Bo hurts a giant more than Ada and fells it; a wall breaks.
            var giant = world.SpawnEnemy(Db.Enemy("giant"), 60f, 28f)!;
            world.DamageEnemy(giant, 30f, ada);
            world.DamageEnemy(giant, 1e6f, bo);
            var one = world.Constructs.All.First(c => c.Def == wall);
            world.DamageConstruct(one, 1e6f);
            world.Run.PhaseEndTick = world.Tick + 1;
            world.Step();
            var s = world.Run.LastSummary!;
            Assert.Equal((bo.Id, ada.Id, (ushort)2, (ushort)1), (s.MvpDamage, s.MvpBuilt, s.MvpBuiltCount, s.ConstructsLost));
            Assert.InRange((int)s.MvpDamageDealt, (int)Db.Enemy("giant").Hp - 31, (int)Db.Enemy("giant").Hp - 29);

            // The next shift counts afresh; the run's Results add them up.
            world.Run.PhaseEndTick = world.Tick + 1;
            world.Step();
            Assert.Equal((2, 0, 0), (world.Run.Shift, ada.ShiftBuilt, bo.ShiftDamage));
            foreach (var p in world.Players) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            world.Run.PhaseEndTick = world.Tick + 1;
            world.Step();
            Assert.True(world.Run.LastSummary!.MvpDamage.IsNone);
            Assert.True(world.Run.LastSummary!.MvpBuilt.IsNone);
            Assert.Equal(0, world.Run.LastSummary!.ConstructsLost);
        }

        [Fact]
        public void Results_CarryEachCarriersFelledDamageAndBuilt_AndTheConstructsLost()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var ada = world.AddPlayer("ada")!;
            ada.MaxHp = ada.Hp = 100000f;
            world.SetPlaytest(ada, PlaytestFlags.Materials, 5);
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.StartRun(0)));
            var wall = Db.Building("wall_wood");
            SimDriver.Teleport(ada, BuildRules.TileCentre(21), BuildRules.TileCentre(15));
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.PlaceConstruct(0, wall.Index, 22, 15, 0)));
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.SetReady(0, true)));
            world.DamageEnemy(world.SpawnEnemy(Db.Enemy("barbarian"), 60f, 28f)!, 1e6f, ada);
            world.DamageConstruct(world.Constructs.All.First(c => c.Def == wall), 1e6f);
            world.Run.PhaseEndTick = world.Tick + 1;
            world.Step();
            world.Run.PhaseEndTick = world.Tick + 1;
            world.Step();
            Assert.Equal(RunPhase.Results, world.Run.Phase);
            var r = world.Run.Results!;
            var row = r.Players.Single();
            Assert.Equal(((ushort)1, (ushort)1, (int)Db.Enemy("barbarian").Hp, 1), (row.Kills, row.Built, row.Damage, r.ConstructsLost));
        }
    }
}
