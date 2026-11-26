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
    /// The Tank (doc 05 §2 "900 HP, 60 dmg, 3.0 s, 2 m/s; PO > depot > any construct; Shift 4–5 only.
    /// Pushes constructs it walks into (destroys belts in path). Pickaxe deals 2×"; §2.2 "4 Iron Ingot
    /// (100%)"; §3.2 "150, from 4, 1 (max 1) / 10 (max 3)"; design choice 136).
    /// </summary>
    public class TankTests
    {
        private const int Tps = SimConstants.TickRate;
        private const float West = MathF.PI / 2f;
        private static ContentDb Db => TestContent.Db;
        private static EnemyDef Tank => Db.Enemy("tank");
        private uint _nextRequest = 1;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        /// <summary>A run in Prep, the carrier with building materials, and a strip of open ground two rows deep and ten tiles long.</summary>
        private (SimWorld World, Player Carrier, int Tx, int Tz) OnOpenGround()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var p = world.AddPlayer("carrier")!;
            p.MaxHp = p.Hp = 10000f;
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.StartRun(0)));
            Assert.True(world.Give(p.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 20)));
            Assert.True(world.Give(p.Inventory, ItemStack.OfItem(Db.Item("log"), 20)));
            Assert.True(world.Give(p.Inventory, ItemStack.OfItem(Db.Item("plank"), 20)));
            var map = world.Map;
            for (int tz = 1; tz < map.Rows - 2; tz++)
                for (int tx = 1; tx < map.Cols - 11; tx++)
                {
                    bool open = true;
                    for (int i = 0; i < 10 && open; i++)
                        open = BuildRules.CheckTile(map, world.Constructs, tx + i, tz) == OpResult.Ok && BuildRules.CheckTile(map, world.Constructs, tx + i, tz + 1) == OpResult.Ok;
                    if (!open) continue;
                    // The carrier out of the way, to the north of it.
                    SimDriver.Teleport(p, BuildRules.TileCentre(tx + 2), BuildRules.TileCentre(tz) - 3f);
                    SimDriver.StepTicks(world, Player.HistoryLength);
                    return (world, p, tx, tz);
                }
            throw new InvalidOperationException("no open strip of ground on the test street");
        }

        /// <summary>Put up turned north–south (a wall across the tank's way west, joined to the one beside it).</summary>
        private static Construct Put(SimWorld world, string id, int tx, int tz) => world.PutUp(Db.Building(id), tx, tz, id == "depot" ? 0 : 1);

        /// <summary>A tank on the strip's east end, level with the middle of its two rows.</summary>
        private static Enemy TankAtEastEnd(SimWorld world, int tx, int tz) =>
            world.SpawnEnemy(Tank, BuildRules.TileCentre(tx + 9), (tz + 1) * SimConstants.TileSize)!;

        [Fact]
        public void TheContentHasTheTankOfTheDocs()
        {
            Assert.Equal((900f, 60f, 3f, 2f, 150, 4), (Tank.Hp, Tank.Damage, Tank.AttackRate, Tank.Speed, Tank.Cost, Tank.UnlockShift));
            Assert.Equal(EnemyTraits.PushesConstructs, Tank.Traits);
            Assert.Equal(new[] { EnemyTarget.PostOffice, EnemyTarget.Depot, EnemyTarget.AnyConstruct }, Tank.TargetPriority);
            Assert.Equal(2f, Tank.PickaxeMult);
            Assert.All(Db.Enemies.Where(e => e != Tank), e => Assert.Equal(1f, e.PickaxeMult));
            var drop = Assert.Single(Tank.Drops);
            Assert.Equal(("iron_ingot", 1000, 4), (drop.Item.Id, drop.ChancePermille, drop.Count));
            var waves = Db.Waves;
            Assert.Contains(waves.WeightsFor(4), w => w.Key == Tank && w.Value == 1);
            Assert.Contains(waves.WeightsFor(5), w => w.Key == Tank && w.Value == 10);
            Assert.Equal((1, 3), (waves.MaxFor(4, Tank), waves.MaxFor(5, Tank)));
            Assert.Equal(int.MaxValue, waves.MaxFor(5, Db.Enemy("barbarian")));
            Assert.All(new[] { 2, 3 }, shift => Assert.DoesNotContain(waves.WeightsFor(shift), w => w.Key == Tank));
        }

        [Fact]
        public void AWallInItsWay_IsShovedATileOn_AsItWas()
        {
            var (world, p, tx, tz) = OnOpenGround();
            var depot = Put(world, "depot", tx, tz);
            var a = Put(world, "wall_wood", tx + 5, tz);
            var b = Put(world, "wall_wood", tx + 5, tz + 1);
            world.DamageConstruct(a, 100f);
            var e = TankAtEastEnd(world, tx, tz);
            var pushed = new List<ConstructPushedEvent>();
            for (int i = 0; i < 6 * Tps && pushed.Count == 0; i++)
            {
                world.Events.Clear();
                world.Step();
                pushed.AddRange(world.Events.OfType<ConstructPushedEvent>());
            }
            Assert.True(pushed.Count == 1, $"{pushed.Count} pushes; the tank at ({e.X:0.0}, {e.Z:0.0}) {e.State} goal {e.GoalTx},{e.GoalTz}, strip at {tx},{tz}, walls {a.Hp} {b.Hp}, depot {depot.Hp}, path {e.Path.Count}");
            var push = pushed[0];
            Assert.Equal(Tank.Index, push.Enemy);
            Assert.Equal(tx + 4, push.X);
            Assert.Equal(tx + 5, push.FromX);
            var moved = world.Constructs.At(push.X, push.Y)!;
            Assert.Equal("wall_wood", moved.Def.Id);
            Assert.Null(world.Constructs.At(push.FromX, push.FromY));
            // As it was: the one that was hurt is still hurt; nothing is left behind of it.
            int hurt = push.FromY == a.Tz ? a.Def.Hp - 100 : b.Def.Hp;
            Assert.Equal(hurt, moved.Hp);
            Assert.Null(world.Constructs.RuinAt(push.FromX, push.FromY));
            Assert.Equal(depot.Def.Hp, depot.Hp);
            Assert.Equal(p.MaxHp, p.Hp);
        }

        [Fact]
        public void AWallWithNoRoomBehindIt_IsStruckDown_SixtyEveryThreeSeconds()
        {
            var (world, p, tx, tz) = OnOpenGround();
            Put(world, "depot", tx, tz);
            var front = new[] { Put(world, "wall_wood", tx + 5, tz), Put(world, "wall_wood", tx + 5, tz + 1) };
            Put(world, "wall_wood", tx + 4, tz);
            Put(world, "wall_wood", tx + 4, tz + 1);
            var e = TankAtEastEnd(world, tx, tz);
            int pushes = 0;
            for (int i = 0; i < 8 * Tps; i++)
            {
                world.Events.Clear();
                world.Step();
                pushes += world.Events.OfType<ConstructPushedEvent>().Count();
            }
            Assert.Equal(0, pushes);
            int lost = front.Sum(w => w.Def.Hp - w.Hp);
            // Two or three blows in 8 s (the first after the windup), sixty each.
            Assert.True(lost % 60 == 0 && lost >= 120 && lost <= 180, $"the front walls lost {lost}");
        }

        [Fact]
        public void BeltsInItsWay_AreCrushed()
        {
            var (world, p, tx, tz) = OnOpenGround();
            var belt = Db.Building("belt_mk1");
            SimDriver.Teleport(p, BuildRules.TileCentre(tx + 5), BuildRules.TileCentre(tz) - 3f);
            SimDriver.StepTicks(world, Player.HistoryLength);
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.PlaceConstruct(0, belt.Index, tx + 5, tz, 0)));
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.PlaceConstruct(0, belt.Index, tx + 5, tz + 1, 0)));
            SimDriver.Teleport(p, BuildRules.TileCentre(tx + 2), BuildRules.TileCentre(tz) - 3f);
            var depot = Put(world, "depot", tx, tz);
            var e = TankAtEastEnd(world, tx, tz);
            for (int i = 0; i < 6 * Tps && (world.Constructs.At(tx + 5, tz) != null || world.Constructs.At(tx + 5, tz + 1) != null); i++) world.Step();
            // Doc 05 §2 "destroys belts in path": at least the one it went through is gone, a ruin in its place.
            bool crushed = world.Constructs.At(tx + 5, tz) == null || world.Constructs.At(tx + 5, tz + 1) == null;
            Assert.True(crushed, "no belt in its way was crushed");
            Assert.True(world.Constructs.RuinAt(tx + 5, tz) != null || world.Constructs.RuinAt(tx + 5, tz + 1) != null);
            // And on to the depot, struck from its reach.
            for (int i = 0; i < 12 * Tps && depot.Hp == depot.Def.Hp; i++) world.Step();
            Assert.Equal(depot.Def.Hp - 60, depot.Hp);
        }

        [Fact]
        public void ItGoesForNoCarrier_AndAPickaxeHurtsItTwiceAsMuch()
        {
            var (world, p, tx, tz) = OnOpenGround();
            var e = world.SpawnEnemy(Tank, p.Move.X - 1.2f, p.Move.Z)!;
            float hammer = Db.Balance.GetFloat("hammerDamage");
            Swing(world, p, e);
            Assert.Equal(Tank.Hp - hammer, e.Hp, 2);

            // With the Pickaxe in the hand: twice that.
            Assert.True(world.Give(p.Hotbar, ItemStack.OfItem(Db.Item("pickaxe"), 1)));
            p.HeldSlot = (byte)p.Hotbar.Entries.Single(x => x.Stack.Kind == StackKind.Item && x.Stack.DefIndex == Db.Item("pickaxe").Index).X;
            for (int i = 0; i < Tps; i++) world.Step();
            Swing(world, p, e);
            Assert.NotEqual(0, world.HeldTool(p));
            Assert.Equal(Tank.Hp - 3 * hammer, e.Hp, 2);

            // Hurt twice, beside it: it stands or goes its way, and never strikes the carrier.
            for (int i = 0; i < 8 * Tps; i++) world.Step();
            Assert.Equal(p.MaxHp, p.Hp);
            Assert.True(e.Target.IsNone);
        }

        /// <summary>A swing of the hammer at the tank held where it stood, then a second's wait.</summary>
        private static void Swing(SimWorld world, Player p, Enemy e)
        {
            for (int i = 0; i < Tps; i++)
            {
                (e.X, e.Z) = (p.Move.X - 1.2f, p.Move.Z);
                world.QueueInput(p.Id, new PlayerInput { Seq = p.LastQueuedSeq + 1, Yaw = PlayerInput.QuantizeYaw(West), Buttons = i == 0 ? InputButtons.Attack : InputButtons.None, HeldSlot = p.HeldSlot });
                world.Step();
            }
        }

        [Fact]
        public void ARaidSendsNoMoreTanksThanItsCap()
        {
            int seen = 0;
            foreach (int shift in new[] { 4, 5 })
                foreach (uint seed in new uint[] { 1, 2, 3, 4, 5, 6 })
                {
                    var world = new SimWorld(Db, new SimSettings { Seed = seed, Draft = false });
                    var team = Enumerable.Range(0, 8).Select(i => world.AddPlayer("c" + i)!).ToList();
                    foreach (var c in team) world.SetPlaytest(c, PlaytestFlags.NoTimer | PlaytestFlags.NoQuota | PlaytestFlags.RaidNow, shift);
                    Assert.Equal(OpResult.Ok, Request(world, team[0], SimRequest.StartRun(0)));
                    foreach (var c in team) Request(world, c, SimRequest.SetReady(0, true));
                    var tanks = new HashSet<EntityId>();
                    for (int t = 0; t < 120 * Tps && world.RaidPulsesSpawned < Db.Waves.Pulses && world.Run.Phase == RunPhase.Delivery; t++)
                    {
                        world.Step();
                        foreach (var en in world.Enemies) if (en.Def == Tank) tanks.Add(en.Id);
                    }
                    // Eight carriers who stand about may lose the Post Office to three Tanks before the finale.
                    Assert.True(world.RaidPulsesSpawned == Db.Waves.Pulses || world.Run.Outcome == RunOutcome.PostOfficeLost, $"shift {shift}, seed {seed}");
                    Assert.True(tanks.Count <= Db.Waves.MaxFor(shift, Tank), $"shift {shift}, seed {seed}: {tanks.Count} tanks");
                    seen += tanks.Count;
                }
            Assert.True(seen > 0, "eight carriers' raids of shifts 4 and 5 never sent a tank");
        }

        [Theory]
        [InlineData(4, 2)]
        [InlineData(5, 2)]
        [InlineData(5, 4)]
        public void ATeamOfFourOrFewer_WhoBuildNothing_HasThePostOfficeStruckByTheTank(int shift, int players)
        {
            // The aa106cb playtest: host and guest in shift 5, nothing built, eight minutes in the yard,
            // 29 raiders and never a blow at the Post Office (design choice 141).
            var world = new SimWorld(Db, new SimSettings { Seed = 3, Draft = false });
            var team = Enumerable.Range(0, players).Select(i => world.AddPlayer("c" + i)!).ToList();
            foreach (var c in team)
            {
                c.MaxHp = c.Hp = 100000f;
                world.SetPlaytest(c, PlaytestFlags.NoTimer | PlaytestFlags.NoQuota | PlaytestFlags.RaidNow, shift);
            }
            Assert.Equal(OpResult.Ok, Request(world, team[0], SimRequest.StartRun(0)));
            foreach (var c in team) Request(world, c, SimRequest.SetReady(0, true));
            bool byTank = false;
            for (int t = 0; t < 300 * Tps && !byTank; t++)
            {
                world.Events.Clear();
                world.Step();
                byTank = world.Events.OfType<PostOfficeDamagedEvent>().Any(e => e.Enemy == Tank.Index);
            }
            Assert.True(byTank, $"shift {shift}, {players} carriers: the Post Office at {world.PostOfficeHp} and no tank's blow");
        }
    }
}
