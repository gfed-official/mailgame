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
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Run events (doc 10 §6, doc 01 §3.3; design choice 162): the scheduler (none in shift 1, at most one a shift from
    /// shift 2, on the waves' own stream) and each event of the Small Island and the test street, through the closed
    /// RunEventRule set and the Stat sheet.
    /// </summary>
    public class RunEventTests
    {
        private const int Tps = SimConstants.TickRate;
        private static ContentDb Db => TestContent.Db;
        private uint _nextRequest = 1;
        private uint _nextMail = 900000;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        /// <summary>A run from shift <paramref name="shift"/> with events on (and <paramref name="forced"/>, if any), in its Prep.</summary>
        private (SimWorld World, Player Host) Run(string? forced, int shift = 2, uint seed = 7, bool events = true, byte stamps = 0)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = seed, Raids = false, Draft = false, RunEvents = events, ForcedEvent = forced });
            var host = world.AddPlayer("host")!;
            host.MaxHp = host.Hp = 100000f;
            world.SetPlaytest(host, PlaytestFlags.NoQuota, shift);
            world.SetStamps(stamps);
            world.Events.Clear();
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            return (world, host);
        }

        private void ToDelivery(SimWorld world, Player host)
        {
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
        }

        /// <summary>The phase's clock runs out on the next tick.</summary>
        private static void EndPhase(SimWorld world)
        {
            world.Run.PhaseEndTick = world.Tick + 1;
            world.Step();
        }

        private static RunEventEvent Fired(SimWorld world, string id) =>
            world.Events.OfType<RunEventEvent>().Last(e => e.Event == Db.RunEvent(id).Index && e.Stage == RunEventStage.Fired);

        [Fact]
        public void TheContent_HasTheEventsOfDoc10_NoPowerSurge_SeaFogOnTheLargeIslandOnly()
        {
            Assert.Equal(new[] { "festival", "inspector", "lost_parcels", "wildfire", "overtime", "delivery_bonus", "mega_migration", "bulk_drop", "fog" },
                Db.RunEvents.Select(e => e.Id));
            Assert.DoesNotContain(Db.RunEvents, e => e.Id == "power_surge");
            var fog = Db.RunEvent("fog");
            Assert.Equal(RunEventTrigger.DeliveryStart, fog.Trigger);
            Assert.Equal(new[] { "large_island" }, fog.Maps);
            Assert.True(fog.Has(RunEventRule.SeaFog));
            Assert.Contains(fog.Modifiers, m => m.Stat == Stat.BoatSpeed && m.Op == ModifierOp.Mul && m.Value == 0.7f);
            Assert.Equal(4, Db.RunEvent("wildfire").MinShift);
            Assert.Equal(3, Db.RunEvent("mega_migration").MinShift);
            Assert.Equal(RunEventTrigger.Complaint, Db.RunEvent("inspector").Trigger);
            Assert.True(Db.RunEvent("overtime").OncePerRun);
            Assert.True(Db.RunEvent("bulk_drop").NeedsDock);
            Assert.Equal(0.5, Db.Balance.GetDouble("runEventChance"));
            // Postal Audit's "Inspector at ≥ 50": the stamp lowers the event's 75 by 25.
            Assert.Contains(Db.Stamp("postal_audit").Modifiers, m => m.Stat == Stat.InspectorComplaint && m.Op == ModifierOp.Add && m.Value == -25f);
        }

        [Fact]
        public void NoneInShiftOne_AtMostOneAShiftFromTwo_AboutHalfTheShifts_TheSameSeedTheSame_AndOffUnlessAsked()
        {
            Assert.Null(Run(null, shift: 1).World.ShiftEvent);
            Assert.Null(Run(null, shift: 3, events: false, seed: 11).World.ShiftEvent);
            int rolled = 0, total = 0;
            var seen = new HashSet<string>();
            for (uint seed = 1; seed <= 120; seed++)
                for (int shift = 2; shift <= 5; shift++)
                {
                    var e = Run(null, shift, seed).World.ShiftEvent;
                    total++;
                    if (e == null) continue;
                    rolled++;
                    seen.Add(e.Id);
                    Assert.True(shift >= e.MinShift, $"{e.Id} in shift {shift}");
                    Assert.NotEqual(RunEventTrigger.Complaint, e.Trigger);
                    Assert.False(e.NeedsDock && shift < 3, "a Bulk Drop with no Business Dock open");
                    Assert.Same(e, Run(null, shift, seed).World.ShiftEvent);
                }
            Assert.InRange(rolled / (double)total, 0.4, 0.6);
            Assert.Equal(7, seen.Count);
        }

        [Fact]
        public void ARolledEvent_LeavesTheMailOfTheSeedAsItWas()
        {
            static List<(uint, int, ushort)> Mail(bool events)
            {
                var world = new SimWorld(Db, new SimSettings { Seed = 5, Raids = false, Draft = false, RunEvents = events, ForcedEvent = events ? "lost_parcels" : null });
                var host = world.AddPlayer("host")!;
                world.SetPlaytest(host, PlaytestFlags.NoQuota, 2);
                world.Submit(host.Id, SimRequest.StartRun(1));
                world.Step();
                world.Submit(host.Id, SimRequest.SetReady(2, true));
                SimDriver.StepTicks(world, 60 * Tps);
                return world.Intake.Entries.Where(e => e.Stack.Kind == StackKind.Mail).SelectMany(e => e.Stack.Mail)
                    .Where(m => (m.Flags & MailFlags.Lost) == 0).Select(m => (m.Address.Key, (int)m.Kind, m.Value)).OrderBy(m => m).ToList();
            }
            var plain = Mail(false);
            Assert.NotEmpty(plain);
            Assert.Equal(plain, Mail(true));
        }

        [Fact]
        public void StreetFestival_ClosesAStreetToVehicles_AndItsLettersPayDouble()
        {
            var (world, host) = Run("festival");
            var ev = Fired(world, "festival");
            Assert.Equal(1, world.FestivalStreet);
            Assert.Equal((byte)2, ev.Street);
            Assert.Equal((byte)2, world.GetGlobalState().EventStreet);
            Assert.True(world.EventLive(Db.RunEvent("festival")));
            ToDelivery(world, host);

            // A letter to Larch Lane pays twice what the rules pay; a Small Package there as it would.
            var dest = world.Map.Destinations.First(d => !d.IsDock && d.Street.Id == 1 && world.Map.OpensAt(d) <= world.Run.Shift);
            int Deliver(string kind)
            {
                var item = TestContent.Mail(kind, dest.Address, _nextMail++, world.Run.Shift);
                Assert.True(world.Give(host.Hotbar, ItemStack.OfMail(item)));
                var entry = host.Hotbar.Entries.First(e => e.Stack.Kind == StackKind.Mail && e.Stack.Mail.Any(m => m.Id == item.Id));
                SimDriver.Teleport(host, dest.MailboxX + 0.8f, dest.MailboxZ + 0.8f);
                SimDriver.StepTicks(world, Tps);
                world.Events.Clear();
                Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Deliver(0, host.Hotbar.Id, entry.EntryId, dest.Id)));
                int plain = DeliveryRules.Evaluate(item, Db.MailKind(kind), dest, world.Run.Shift, 0, Db, null).Amount;
                int paid = world.Events.OfType<DeliveredEvent>().Single().Paid;
                return paid - plain * (kind == "letter" ? 2 : 1);
            }
            Assert.Equal(0, Deliver("letter"));
            Assert.Equal(0, Deliver("small"));

            // A bike on its street crawls at a fifth of its road speed.
            var bike = Db.Vehicle("bike");
            var c = MoveConstants.From(Db);
            var street = world.Map.GetStreet(1)!;
            float RideFor(TileRect closed, float mult)
            {
                var s = new PlayerMoveState { X = 30f, Z = street.Rect.CenterZ, Grounded = true, Ride = (byte)(bike.Index + 1), HeadX = 1f };
                for (int i = 0; i < 3 * Tps; i++)
                    PlayerMovement.Step(ref s, new PlayerInput { MoveX = 127, Yaw = 0 }, world.Map, 1f, c, null, 0, 1f, 1f, closed, mult);
                return s.RideSpeed;
            }
            Assert.Equal(bike.RoadSpeed, RideFor(default, 1f), 2);
            Assert.Equal(bike.RoadSpeed * 0.2f, RideFor(street.Rect, 0.2f), 2);
        }

        [Fact]
        public void PostalInspector_ComplaintAt75AtPayday_TheNextShiftPaysTenPercentLess_FiftyWithPostalAudit()
        {
            var (world, host) = Run(null, events: true, seed: 3);
            var inspector = Db.RunEvent("inspector");
            ToDelivery(world, host);
            world.Economy.AddComplaint(80);
            world.Events.Clear();
            EndPhase(world);
            Assert.Equal(RunPhase.Payday, world.Run.Phase);
            var coming = world.Events.OfType<RunEventEvent>().Single(e => e.Event == inspector.Index);
            Assert.Equal((RunEventStage.Coming, 80), (coming.Stage, coming.Amount));
            Assert.False(world.EventLive(inspector));
            EndPhase(world);
            Assert.Equal((3, RunPhase.Prep), (world.Run.Shift, world.Run.Phase));
            Assert.True(world.EventLive(inspector));
            Assert.Equal(0.9f, world.TeamStatOf(Stat.DeliveryEarnings, 1f), 3);
            Assert.Equal((byte)3, Fired(world, "inspector").Shift);
            // Over with its shift, unless the meter calls it again.
            ToDelivery(world, host);
            world.Economy.AddComplaint(-100);
            EndPhase(world);
            EndPhase(world);
            Assert.Equal((4, RunPhase.Prep), (world.Run.Shift, world.Run.Phase));
            Assert.False(world.EventLive(inspector));
            Assert.Equal(1f, world.TeamStatOf(Stat.DeliveryEarnings, 1f), 3);

            // 60 at Payday is under 75: no Inspector; with Postal Audit's 50 there is.
            bool Comes(byte stamps)
            {
                var (w, h) = Run(null, events: true, seed: 3, stamps: stamps);
                ToDelivery(w, h);
                w.Economy.AddComplaint(60 - w.Economy.Complaint);
                w.Events.Clear();
                EndPhase(w);
                return w.Events.OfType<RunEventEvent>().Any(e => e.Event == inspector.Index && e.Stage == RunEventStage.Coming);
            }
            Assert.False(Comes(0));
            Assert.True(Comes((byte)(1 << Db.Stamp("postal_audit").Index)));

            // At the last shift's Payday there is no next shift to come to.
            var (last, lastHost) = Run(null, shift: 5, seed: 3);
            ToDelivery(last, lastHost);
            last.Economy.AddComplaint(100);
            last.Events.Clear();
            EndPhase(last);
            Assert.DoesNotContain(last.Events.OfType<RunEventEvent>(), e => e.Event == inspector.Index);
        }

        [Fact]
        public void LostParcels_ToldOfInPrep_FiveLieOnTheStreetsAtDeliverysStart_ThreeTimesTheValue_TillItsEnd()
        {
            var (world, host) = Run("lost_parcels");
            Assert.Equal(5, Fired(world, "lost_parcels").Amount);
            Assert.Empty(world.Bags);
            world.Events.Clear();
            ToDelivery(world, host);
            Assert.Equal(5, world.Bags.Count);
            var events = world.Events.OfType<LostParcelEvent>().ToList();
            Assert.Equal(5, events.Count);
            Assert.All(events, e => Assert.Equal(LostParcelEvent.NoEnemy, e.Enemy));
            var medium = Db.MailKind("medium");
            var mix = Db.MailMix;
            foreach (var bag in world.Bags)
            {
                Assert.Equal(EntityId.None, bag.Owner);
                Assert.Equal(world.Run.PhaseEndTick, bag.DespawnTick);
                int tx = (int)MathF.Floor(bag.X / SimConstants.TileSize), tz = (int)MathF.Floor(bag.Z / SimConstants.TileSize);
                Assert.Equal(TileType.Street, world.Map.GetTile(tx, tz));
                var parcel = bag.Container.Entries.Single().Stack.Mail.Single();
                Assert.True((parcel.Flags & MailFlags.Lost) != 0);
                Assert.Equal(medium.Index, parcel.Kind);
                var dest = world.Map.Destinations.Single(d => d.Address == parcel.Address);
                double plain = medium.BaseValue * (1 + mix.DistanceMultiplierPerDistrict * (dest.Address.District - 1)) * (1 + mix.ShiftMultiplierPerShift * (world.Run.Shift - 1));
                Assert.Equal((ushort)Math.Round(plain * 3), parcel.Value);
            }
        }

        [Fact]
        public void Wildfire_BurnsWoodenWallsAndBelts_ASecondAPoint_ForAMinute_SparingWhatStandsNearWater()
        {
            var (world, host) = Run("wildfire", shift: 4);
            var map = world.Map;
            // A wall beside the water and one far from it, and a stone wall that does not burn.
            (int, int) Find(Func<int, int, bool> ok)
            {
                for (int z = 0; z < map.Rows; z++)
                    for (int x = 0; x < map.Cols; x++)
                        if (!map.IsSolid(x, z) && !map.IsWater(x, z) && map.GetTile(x, z) != TileType.Street && world.Constructs.At(x, z) == null && ok(x, z)) return (x, z);
                throw new InvalidOperationException("no tile");
            }
            bool WaterWithin(int x, int z, int r)
            {
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if (map.IsWater(x + dx, z + dz)) return true;
                return false;
            }
            var (nx, nz) = Find((x, z) => map.IsWater(x, z + 1));
            var (fx, fz) = Find((x, z) => !WaterWithin(x, z, 8) && x > 4);
            var (sx, sz) = Find((x, z) => !WaterWithin(x, z, 8) && x > fx + 2);
            var near = world.PutUp(Db.Building("wall_wood"), nx, nz);
            var far = world.PutUp(Db.Building("wall_wood"), fx, fz);
            var stone = world.PutUp(Db.Building("wall_stone"), sx, sz);
            ToDelivery(world, host);
            Assert.Same(Db.RunEvent("wildfire"), world.ShiftEvent);
            world.Events.Clear();
            int ticks = 0;
            while (!world.Events.OfType<RunEventEvent>().Any(e => e.Stage == RunEventStage.Fired) && ticks++ < 300 * Tps) world.Step();
            Assert.Equal(60, Fired(world, "wildfire").Amount);
            int hp = far.Hp;
            SimDriver.StepTicks(world, 10 * Tps);
            Assert.InRange(hp - far.Hp, 9, 11);
            Assert.Equal(near.MaxHp, near.Hp);
            Assert.Equal(stone.MaxHp, stone.Hp);
            world.Events.Clear();
            SimDriver.StepTicks(world, 51 * Tps);
            Assert.Contains(world.Events.OfType<RunEventEvent>(), e => e.Stage == RunEventStage.Ended);
            Assert.False(world.EventLive(Db.RunEvent("wildfire")));
            hp = far.Hp;
            SimDriver.StepTicks(world, 5 * Tps);
            Assert.Equal(hp, far.Hp);
        }

        [Fact]
        public void Overtime_AQuotaMissedByTenPercentOrLess_KeepsDeliveryAMinuteMore_OnceARun()
        {
            var (world, host) = Run("overtime");
            ToDelivery(world, host);
            world.Run.Quota = 1000;
            world.Economy.Apply(new DeliveryResult(DeliveryOutcome.Delivered, 950));
            world.Events.Clear();
            EndPhase(world);
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.Equal(50, Fired(world, "overtime").Amount);
            Assert.Equal(world.Tick + 60 * Tps, world.Run.PhaseEndTick, 1u);
            // The shortfall covered, Payday finds the quota met.
            world.Economy.Apply(new DeliveryResult(DeliveryOutcome.Delivered, 60));
            EndPhase(world);
            Assert.Equal(RunPhase.Payday, world.Run.Phase);
            Assert.True(world.Run.LastSummary!.QuotaMet);
            // Once a run: the next shift does not roll it again.
            EndPhase(world);
            Assert.Equal(3, world.Run.Shift);
            Assert.Null(world.ShiftEvent);

            // Missed by more than a tenth: Payday at once.
            var (short2, host2) = Run("overtime");
            ToDelivery(short2, host2);
            short2.Run.Quota = 1000;
            short2.Economy.Apply(new DeliveryResult(DeliveryOutcome.Delivered, 850));
            EndPhase(short2);
            Assert.Equal(RunPhase.Payday, short2.Run.Phase);
        }

        [Fact]
        public void NeighbourhoodWatch_NoMisdelivery_AQuarterOfTheEarningsMoreAtPayday_NotTowardsTheQuota()
        {
            var (world, host) = Run("delivery_bonus");
            Assert.NotNull(Fired(world, "delivery_bonus"));
            ToDelivery(world, host);
            world.Economy.Apply(new DeliveryResult(DeliveryOutcome.Delivered, 400));
            int wallet = world.Economy.Wallet;
            world.Events.Clear();
            EndPhase(world);
            var summary = world.Run.LastSummary!;
            Assert.Equal(400, summary.Earned);
            Assert.Equal(40 + 100, summary.Bonus);
            Assert.Equal(wallet + 140, world.Economy.Wallet);
            var paid = world.Events.OfType<RunEventEvent>().Single(e => e.Stage == RunEventStage.Paid);
            Assert.Equal(100, paid.Amount);

            var (spoilt, host2) = Run("delivery_bonus");
            ToDelivery(spoilt, host2);
            spoilt.Economy.Apply(new DeliveryResult(DeliveryOutcome.Delivered, 400));
            spoilt.Economy.Apply(new DeliveryResult(DeliveryOutcome.Misdelivered, 4));
            spoilt.Events.Clear();
            EndPhase(spoilt);
            Assert.Equal(0, spoilt.Run.LastSummary!.Bonus);
            Assert.Contains(spoilt.Events.OfType<RunEventEvent>(), e => e.Stage == RunEventStage.Fizzled);
        }

        [Fact]
        public void MegaMigration_ThreeTimesTheMegaChance_AndEveryMegaDropsTwoLostParcels()
        {
            var (world, host) = Run("mega_migration", shift: 3);
            Assert.Equal(3f, world.TeamStatOf(Stat.MegaChance, 1f), 3);
            ToDelivery(world, host);
            var mega = world.SpawnEnemy(Db.Enemy("giant"), 40f, 28f, mega: true)!;
            world.Events.Clear();
            world.DamageEnemy(mega, 1e6f, host);
            Assert.Equal(2, world.Events.OfType<LostParcelEvent>().Count());
        }

        [Fact]
        public void BulkDrop_ACargoItemForEveryOpenBusinessDock_AtTheIntake_HalfAgainItsValue()
        {
            // The test street's dock opens at shift 3: none to drop for before.
            Assert.Null(Run("bulk_drop", shift: 2).World.ShiftEvent);
            var (world, host) = Run("bulk_drop", shift: 3);
            var cargo = Db.MailKind("cargo");
            var docks = world.Map.Destinations.Where(d => d.IsDock && world.Map.OpensAt(d) <= 3).ToList();
            Assert.Single(docks);
            world.Events.Clear();
            ToDelivery(world, host);
            Assert.Equal(1, Fired(world, "bulk_drop").Amount);
            var dropped = world.Intake.Entries.Where(e => e.Stack.Kind == StackKind.Mail).SelectMany(e => e.Stack.Mail)
                .Where(m => m.Kind == cargo.Index && m.Address == docks[0].Address).ToList();
            var item = Assert.Single(dropped);
            var mix = Db.MailMix;
            double plain = cargo.BaseValue * (1 + mix.DistanceMultiplierPerDistrict * (docks[0].Address.District - 1)) * (1 + mix.ShiftMultiplierPerShift * 2);
            Assert.Equal((ushort)Math.Round(plain * 1.5), item.Value);
        }
    }
}
