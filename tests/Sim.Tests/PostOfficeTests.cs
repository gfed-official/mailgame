using System;
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
    /// The Post Office's HP (doc 05 §5 "PO 3000 … at 0 HP the run ends. The PO regenerates 5 HP/s
    /// during Prep"; doc 01 §7 "Post Office HP reaches 0 — Run Over (immediate)"; doc 05 §2 Tank
    /// "PO > depot > any construct"; design choice 137).
    /// </summary>
    public class PostOfficeTests
    {
        private const int Tps = SimConstants.TickRate;
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

        private (SimWorld World, Player Host) InPrep(PlaytestFlags flags = PlaytestFlags.None)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            host.MaxHp = host.Hp = 100000f;
            world.SetPlaytest(host, flags, 1);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            return (world, host);
        }

        private (SimWorld World, Player Host) InDelivery(PlaytestFlags flags = PlaytestFlags.None)
        {
            var (world, host) = InPrep(flags);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            return (world, host);
        }

        /// <summary>A point on the street <paramref name="metres"/> from the Post Office's walls, the way the raiders come.</summary>
        private static (float X, float Z) OffThePostOffice(SimWorld world, float metres)
        {
            var po = world.Map.PostOffice;
            for (float x = po.SpawnPadX; x < po.SpawnPadX + 60f; x += 0.25f)
                if (world.PostOfficeDistance(x, po.SpawnPadZ, out _, out _) >= metres) return (x, po.SpawnPadZ);
            throw new InvalidOperationException("no street beside the Post Office");
        }

        [Fact]
        public void ItHasThreeThousand_AndMendsFivePerSecondInPrep_NotInDelivery()
        {
            Assert.Equal((3000f, 5f), (Db.Balance.GetFloat("postOfficeHp"), Db.Balance.GetFloat("postOfficeRegenPerSecond")));
            var (world, host) = InPrep();
            Assert.Equal(3000f, world.PostOfficeHp);
            Assert.Equal((ushort)3000, world.GetGlobalState().PostOfficeHp);
            world.DamagePostOffice(1000f);
            var hit = world.Events.OfType<PostOfficeDamagedEvent>().Last();
            Assert.Equal(((ushort)2000, (byte)255), (hit.Hp, hit.Enemy));
            for (int i = 0; i < 10 * Tps; i++) world.Step();
            Assert.Equal(2050f, world.PostOfficeHp, 1);

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            float was = world.PostOfficeHp;
            for (int i = 0; i < 10 * Tps; i++) world.Step();
            Assert.Equal(was, world.PostOfficeHp);
        }

        [Fact]
        public void AGiantWithNothingBuiltAbout_StrikesThePostOffice_FortyEveryTwoSeconds()
        {
            var (world, host) = InDelivery();
            SimDriver.Teleport(host, world.Map.PostOffice.SpawnPadX - 20f, world.Map.PostOffice.SpawnPadZ);
            var (x, z) = OffThePostOffice(world, 12f);
            var giant = world.SpawnEnemy(Db.Enemy("giant"), x, z)!;
            for (int i = 0; i < 15 * Tps && world.PostOfficeHp > 2920f; i++) world.Step();
            Assert.Equal(2920f, world.PostOfficeHp);
            Assert.True(giant.GoalPostOffice);
            var hit = world.Events.OfType<PostOfficeDamagedEvent>().Last();
            Assert.Equal(Db.Enemy("giant").Index, hit.Enemy);
            Assert.Equal(host.MaxHp, host.Hp);
        }

        [Fact]
        public void ATank_GoesForThePostOffice_BeforeADepotNearer()
        {
            var (world, host) = InDelivery();
            SimDriver.Teleport(host, world.Map.PostOffice.SpawnPadX - 20f, world.Map.PostOffice.SpawnPadZ);
            var (x, z) = OffThePostOffice(world, 14f);
            var depot = world.PutUp(Db.Building("depot"), (int)MathF.Floor((x + 6f) / SimConstants.TileSize), (int)MathF.Floor((z - 6f) / SimConstants.TileSize));
            var tank = world.SpawnEnemy(Db.Enemy("tank"), x, z)!;
            for (int i = 0; i < 20 * Tps && world.PostOfficeHp == 3000f; i++) world.Step();
            Assert.Equal(3000f - 60f, world.PostOfficeHp);
            Assert.Equal(depot.Def.Hp, depot.Hp);
        }

        [Fact]
        public void ARaiderHoldsItsLine_ItDoesNotGoForThePostOffice()
        {
            var (world, host) = InDelivery();
            SimDriver.Teleport(host, world.Map.PostOffice.SpawnPadX - 30f, world.Map.PostOffice.SpawnPadZ);
            var (x, z) = OffThePostOffice(world, 6f);
            world.SpawnEnemy(Db.Enemy("barbarian"), x, z);
            for (int i = 0; i < 15 * Tps; i++) world.Step();
            Assert.Equal(3000f, world.PostOfficeHp);
        }

        [Fact]
        public void TheBlowThatFellsIt_AmongOtherRaiders_EndsTheRunWithoutBreakingTheirStep()
        {
            // The aa106cb fix's RaidFightTests: four carriers' Tank felled the Post Office while
            // others stood behind it in the list, and the cleared field broke the enemies' step.
            var (world, _) = InDelivery(PlaytestFlags.NoQuota);
            world.DamagePostOffice(2950f);
            var (x, z) = OffThePostOffice(world, 3f);
            var (fx, fz) = OffThePostOffice(world, 25f);
            world.SpawnEnemy(Db.Enemy("barbarian"), fx, fz + 2f);
            world.SpawnEnemy(Db.Enemy("barbarian"), fx, fz - 2f);
            world.SpawnEnemy(Db.Enemy("giant"), x, z);
            for (int i = 0; i < 15 * Tps && world.Run.Phase == RunPhase.Delivery; i++) world.Step();
            Assert.Equal(RunOutcome.PostOfficeLost, world.Run.Outcome);
            Assert.Empty(world.Enemies);
        }

        [Fact]
        public void AtZero_TheRunIsOverAtOnce_TheQuotaWaivedOrNot()
        {
            var (world, host) = InDelivery(PlaytestFlags.NoQuota);
            Assert.True(world.QuotaWaived);
            world.DamagePostOffice(2950f);
            var (x, z) = OffThePostOffice(world, 3f);
            world.SpawnEnemy(Db.Enemy("giant"), x, z);
            for (int i = 0; i < 15 * Tps && world.Run.Phase == RunPhase.Delivery; i++) world.Step();
            Assert.Equal(RunPhase.Results, world.Run.Phase);
            Assert.Equal(RunOutcome.PostOfficeLost, world.Run.Outcome);
            Assert.Equal(RunOutcome.PostOfficeLost, world.Run.Results!.Outcome);
            Assert.Equal(0f, world.PostOfficeHp);
            Assert.Empty(world.Enemies);
            // Back in the Lobby it stands whole again for the next run.
            for (int i = 0; i < 2 * Tps && world.Run.Phase == RunPhase.Results; i++) Request(world, host, SimRequest.ContinueRun(0));
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);
            Assert.Equal(3000f, world.PostOfficeHp);
        }
    }
}
