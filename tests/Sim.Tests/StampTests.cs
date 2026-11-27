using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The eight Postage Stamps (doc 01 §6; no Brownout): their rules and numbers through the closed StampRule and
    /// Stat sets on the team's sheet, their tiers locked to the host's Postal Rank (doc 01 §5.2), the score's
    /// multipliers multiplied.
    /// </summary>
    public class StampTests
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

        private static byte Mask(params string[] ids) => (byte)ids.Aggregate(0, (m, id) => m | 1 << Db.Stamp(id).Index);

        /// <summary>A world with these stamps on, into the Delivery of shift 1 (or just its Prep).</summary>
        private (SimWorld World, Player Host) Run(bool raids, bool delivery, params string[] stamps)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = raids, Draft = false });
            var host = world.AddPlayer("host")!;
            host.MaxHp = host.Hp = 100000f;
            world.SetPlaytest(host, PlaytestFlags.NoQuota, 1);
            world.SetStamps(Mask(stamps));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            if (delivery) Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            return (world, host);
        }

        [Fact]
        public void TheContentHasTheEightStampsOfDoc01_TheirTiersAndMultipliers()
        {
            Assert.Equal(new[]
            {
                ("cursed_mail", 1, 1.15f), ("rush_hour", 1, 1.10f), ("heavy_load", 1, 1.10f), ("double_raids", 2, 1.25f),
                ("no_roads", 2, 1.15f), ("postal_audit", 3, 1.20f), ("megamail", 3, 1.25f), ("skeleton_crew", 3, 1.30f),
            }, Db.Stamps.Select(s => (s.Id, s.Tier, s.ScoreMult)));
            Assert.DoesNotContain(Db.Stamps, s => s.Id.Contains("brownout"));
            Assert.All(Db.Stamps, s => Assert.True(s.Rules.Count + s.Modifiers.Count > 0, s.Id));
        }

        [Fact]
        public void ATierTheHostsRankHasNotOpened_IsRefused_UnlessThePlaytestOpensThemAll()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            Assert.Equal(1, world.StampTierOpen(host));
            Assert.Equal(OpResult.ShopLocked, Request(world, host, SimRequest.SetStamps(0, Mask("double_raids"))));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetStamps(0, Mask("rush_hour", "heavy_load"))));
            host.Rank = 6;
            Assert.Equal(2, world.StampTierOpen(host));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetStamps(0, Mask("double_raids", "no_roads"))));
            Assert.Equal(OpResult.ShopLocked, Request(world, host, SimRequest.SetStamps(0, Mask("megamail"))));
            host.Rank = 1;
            // One put on already may be taken off at any rank.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetStamps(0, 0)));
            world.SetPlaytest(host, PlaytestFlags.AllStamps, 1);
            Assert.Equal(3, world.StampTierOpen(host));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetStamps(0, 0xFF)));
        }

        [Fact]
        public void RushHour_TakesThirtySecondsOffEveryDelivery()
        {
            var (world, _) = Run(false, true, "rush_hour");
            double want = Db.Balance.GetForShift("deliverySeconds", 1) - 30;
            Assert.InRange((long)(world.Run.PhaseEndTick - world.Tick), (long)SimConstants.SecondsToTicks(want) - 2, (long)SimConstants.SecondsToTicks(want) + 2);
        }

        [Fact]
        public void SkeletonCrew_ReckonsTheQuotaForTwoMore()
        {
            var (world, _) = Run(false, true, "skeleton_crew");
            Assert.Equal(QuotaFormula.Quota(Db.Balance, 1, 3), world.Run.Quota);
            var (plain, _) = Run(false, true);
            Assert.Equal(QuotaFormula.Quota(Db.Balance, 1, 1), plain.Run.Quota);
            Assert.True(world.Run.Quota > plain.Run.Quota);
        }

        [Fact]
        public void PostalAudit_TheComplaintDecaysHalfAsFast()
        {
            int Left(params string[] stamps)
            {
                var (world, _) = Run(false, true, stamps);
                world.Economy.AddComplaint(20);
                for (int i = 0; i < 60 * Tps; i++) world.Step();
                return world.Economy.Complaint;
            }
            int plain = 20 - Left(), audit = 20 - Left("postal_audit");
            Assert.True(plain >= 4, $"{plain} decayed");
            Assert.InRange(audit, plain / 2 - 1, plain / 2 + 1);
        }

        [Fact]
        public void Megamail_DoublesTheMegaChance_AndNoRoads_TakesTwoFifthsOffTheRoads()
        {
            var (world, _) = Run(false, false, "megamail", "no_roads");
            Assert.Equal(2f, world.TeamStatOf(Stat.MegaChance, 1f), 3);
            Assert.Equal(0.6f, world.TeamStatOf(Stat.VehicleRoadSpeed, 1f), 3);
            // A stamp lies on the team's sheet with the perks, and the run's end keeps it there.
            var host = world.Players.First();
            Assert.Equal(OpResult.Ok, world.GrantPerk(host, Db.Perk("union_rep")));
            Assert.Equal(2f, world.TeamStatOf(Stat.MegaChance, 1f), 3);
            var v = Db.Vehicle("bike");
            var s = new PlayerMoveState { X = 30f, Z = 28.5f, Grounded = true, Ride = (byte)(v.Index + 1), HeadX = 1f };
            var c = MoveConstants.From(Db);
            for (int i = 0; i < 3 * Tps; i++)
                PlayerMovement.Step(ref s, new PlayerInput { MoveX = 127, Yaw = 0 }, world.Map, 1f, c, world.Constructs, 0, 1f, world.TeamStatOf(Stat.VehicleRoadSpeed, 1f));
            Assert.True(PlayerMovement.OnRoad(world.Map, s.X, s.Z));
            Assert.Equal(v.RoadSpeed * 0.6f, s.RideSpeed, 2);
        }

        [Fact]
        public void HeavyLoad_SkewsThePackagesOneSizeLarger()
        {
            List<MailItem> Mail(params string[] stamps)
            {
                var (world, _) = Run(false, true, stamps);
                for (int i = 0; i < 200 * Tps && world.Run.Phase == RunPhase.Delivery; i++) world.Step();
                return world.Intake.Entries.Where(e => e.Stack.Kind == StackKind.Mail).SelectMany(e => e.Stack.Mail).ToList();
            }
            var small = Db.MailKind("small").Index;
            var medium = Db.MailKind("medium").Index;
            var plain = Mail();
            var heavy = Mail("heavy_load");
            Assert.Contains(plain, m => m.Kind == small);
            // Shift 1's 30 % of Small Packages are Medium ones now; the letters as they were.
            Assert.DoesNotContain(heavy, m => m.Kind == small);
            Assert.True(heavy.Count(m => m.Kind == medium) > plain.Count(m => m.Kind == medium));
            var letter = Db.MailKind("letter").Index;
            Assert.InRange(heavy.Count(m => m.Kind == letter), plain.Count(m => m.Kind == letter) * 8 / 10, plain.Count(m => m.Kind == letter) * 12 / 10 + 1);
        }

        [Fact]
        public void DoubleRaids_ARaidInTheFirstMinute_ThenTheShiftsOwn()
        {
            var (world, _) = Run(true, false, "double_raids");
            // Shift 1 has no raid of its own: Double Raids then adds nothing.
            Assert.Equal(OpResult.Ok, Request(world, world.Players.First(), SimRequest.SetReady(0, true)));
            var states = new List<(RaidState, uint)>();
            void Watch(int seconds)
            {
                for (int i = 0; i < seconds * Tps && world.Run.Phase == RunPhase.Delivery; i++)
                {
                    world.Step();
                    if (states.Count == 0 || states[^1].Item1 != world.Raid) states.Add((world.Raid, world.Tick));
                }
            }
            if (Db.Waves.BudgetFor(1) <= 0)
            {
                Watch(30);
                Assert.All(states, s => Assert.Equal(RaidState.None, s.Item1));
                world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = true, Draft = false });
                var host = world.AddPlayer("host")!;
                host.MaxHp = host.Hp = 100000f;
                world.SetPlaytest(host, PlaytestFlags.NoQuota, 2);
                world.SetStamps(Mask("double_raids"));
                Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
                Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
                states.Clear();
            }
            uint start = world.Tick;
            Watch(400);
            var seen = states.Select(s => s.Item1).ToList();
            // Warned, on, and gone within the first minute; then the shift's own raid, warned and on.
            int first = seen.IndexOf(RaidState.Active);
            Assert.True(first > 0 && seen[first - 1] == RaidState.Warning, string.Join(",", seen));
            Assert.True(states[first].Item2 - start < 15 * Tps);
            Assert.Equal(RaidState.Fleeing, seen[first + 1]);
            Assert.InRange(states[first + 1].Item2 - start, 55u * Tps, 61u * Tps);
            int second = seen.IndexOf(RaidState.Active, first + 1);
            Assert.True(second > first + 1, string.Join(",", seen));
            Assert.Equal(RaidState.Warning, seen[second - 1]);
        }
    }
}
