using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    public class RaidFightTests
    {
        private const int Tps = 30;
        private uint _nextRequest = 1;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private sealed class Outcome
        {
            public int Spawned, Killed, Downs, Seconds, FastReDowns, Left, Tanks, Giants;
            public bool Won;
            public string LeftKinds = "";
            public float MinUpSeconds = float.MaxValue;
        }

        private Outcome Fight(uint seed, int shift, int players, bool cannon, bool clumsy, int maxSeconds = 420)
        {
            var world = new SimWorld(TestContent.Db, new SimSettings { Seed = seed, Draft = false });
            var team = new List<Player>();
            for (int i = 0; i < players; i++) team.Add(world.AddPlayer("carrier" + i)!);
            var flags = PlaytestFlags.NoTimer | PlaytestFlags.NoQuota | PlaytestFlags.RaidNow | (cannon ? PlaytestFlags.Packages : 0);
            foreach (var p in team) world.SetPlaytest(p, flags, shift);
            Assert.Equal(OpResult.Ok, Request(world, team[0], SimRequest.StartRun(0)));
            foreach (var p in team) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.Equal(shift, world.Run.Shift);

            var bots = new Dictionary<Player, RaidBot>();
            foreach (var p in team)
                bots[p] = clumsy ? new RaidBot(seed + p.Slot) { AimErrorDegrees = 25f, LookEveryTicks = 12, SwingEveryTicks = 36 } : new RaidBot(seed + p.Slot);
            var o = new Outcome();
            var upSince = new Dictionary<Player, uint>();
            var wasDead = new Dictionary<Player, bool>();
            foreach (var p in team) wasDead[p] = false;
            int pulses = TestContent.Db.Waves.Pulses;
            var seen = new HashSet<EntityId>();
            for (int t = 0; t < maxSeconds * Tps; t++)
            {
                foreach (var p in team) world.QueueInput(p.Id, bots[p].Decide(world, p));
                world.Step();
                foreach (var e in world.Enemies)
                    if (seen.Add(e.Id))
                    {
                        if (e.Def.Id == "tank") o.Tanks++;
                        if (e.Def.Id == "giant") o.Giants++;
                    }
                foreach (var p in team)
                {
                    if (p.Dead && !wasDead[p])
                    {
                        o.Downs++;
                        if (upSince.TryGetValue(p, out uint since))
                        {
                            float up = (world.Tick - since) / (float)Tps;
                            o.MinUpSeconds = MathF.Min(o.MinUpSeconds, up);
                            if (up < 8f) o.FastReDowns++;
                        }
                    }
                    if (!p.Dead && wasDead[p]) upSince[p] = world.Tick;
                    wasDead[p] = p.Dead;
                }
                o.Seconds = t / Tps;
                // A Balloon is beyond the hammer's reach (doc 05 §2, design choice 135) and goes for no carrier:
                // the raid is won when the ground is clear; what floats over nothing of its own is left.
                if (world.RaidPulsesSpawned >= pulses && world.Enemies.All(e => e.Def.Airborne)) { o.Won = true; break; }
            }
            o.Spawned = world.EnemiesSpawnedTotal;
            o.Killed = world.EnemiesKilledTotal;
            o.Left = world.Enemies.Count;
            o.LeftKinds = string.Join(", ", world.Enemies.Select(e => $"{e.Def.Id} {e.State} at ({e.X:0.0}, {e.Z:0.0}) goal {e.GoalTx},{e.GoalTz} target {e.Target} hp {e.Hp}"))
                + "; carriers " + string.Join(", ", team.Select(p => $"({p.Move.X:0.0}, {p.Move.Z:0.0}) hp {p.Hp} dead {p.Dead} prot {world.Protected(p)}"));
            return o;
        }

        private static readonly uint[] Seeds = { 3, 11, 21, 40 };
        private static readonly int Shifts = new SimWorld(TestContent.Db, new SimSettings()).ShiftCount;

        [Fact]
        public void TheBudgetGrowsByAThirdAtMostIntoShiftThree_ThenFasterWithTheDefences()
        {
            // The a5d987e playtest: 16 raiders in shift 2, 32 in shift 3, and no way to win it.
            var waves = TestContent.Db.Waves;
            Assert.Equal(120, waves.BudgetFor(2));
            Assert.InRange(waves.BudgetFor(3) / waves.BudgetFor(2), 1.15, 1.34);
            // Design choice 141: with walls, turrets, the shop and perks in, shifts 4 and 5 climb
            // toward doc 05 §3.2's 360 and 560 again, never past them.
            double[] doc = { 0, 120, 220, 360, 560 };
            for (int shift = 4; shift <= Shifts; shift++)
            {
                Assert.InRange(waves.BudgetFor(shift) / waves.BudgetFor(shift - 1), 1.15, 1.7);
                Assert.True(waves.BudgetFor(shift) <= doc[Math.Min(shift, doc.Length) - 1]);
            }
        }

        [Theory]
        [InlineData(4, 2)]
        [InlineData(4, 4)]
        [InlineData(5, 1)]
        [InlineData(5, 2)]
        [InlineData(5, 4)]
        public void ATeamOfFourOrFewer_MeetsATankInShiftsFourAndFive_AndBeatsIt(int shift, int players)
        {
            // The aa106cb playtest: a pulse's share (budget / 6) never bought a Tank (150) for a team
            // of four or fewer, so the Post Office was never struck (design choice 141).
            foreach (uint seed in Seeds)
            {
                var o = Fight(seed, shift, players, cannon: false, clumsy: true);
                Assert.True(o.Tanks >= 1, $"shift {shift}, {players} carriers, seed {seed}: no tank");
                Assert.True(o.Won, $"shift {shift}, {players} carriers, seed {seed}: {o.Killed} of {o.Spawned} felled; left {o.LeftKinds}");
            }
        }

        [Fact]
        public void ASoloShiftFour_HasNoTank()
        {
            foreach (uint seed in Seeds) Assert.Equal(0, Fight(seed, 4, 1, cannon: false, clumsy: true).Tanks);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        public void ShiftFiveBringsGiants_MoreThanTheOldBudgetBought(int players)
        {
            int giants = 0;
            foreach (uint seed in Seeds) giants += Fight(seed, 5, players, cannon: false, clumsy: true).Giants;
            Assert.True(giants >= 2 * Seeds.Length, $"{giants} giants over {Seeds.Length} seeds");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        public void ShiftThreeSendsAboutAQuarterMoreThanShiftTwo_NotTwiceAsMany(int players)
        {
            int two = 0, three = 0;
            foreach (uint seed in Seeds)
            {
                two += Fight(seed, 2, players, cannon: false, clumsy: true).Spawned;
                three += Fight(seed, 3, players, cannon: false, clumsy: true).Spawned;
            }
            // Heads, not weight: a richer pulse buys two Raiders where a poorer one bought a Raider and a Swarmling.
            Assert.InRange(three, two * 9 / 10, two * 3 / 2);
            // 18 since the Wall Breakers (design choice 129): a pulse that buys one buys fewer heads.
            if (players == 2) Assert.InRange(three / Seeds.Length, 18, 28);
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(2, true)]
        [InlineData(4, false)]
        public void AClumsyTeamWinsTheRaidOfEveryShift(int players, bool cannon)
        {
            // 25 degrees off, a blow every 1.2 s, straight at the nearest raider: who is new to it
            // gets through, and is not down more often than a few times a head on the way.
            for (int shift = 2; shift <= Shifts; shift++)
                foreach (uint seed in Seeds)
                {
                    var o = Fight(seed, shift, players, cannon, clumsy: true);
                    string what = $"shift {shift}, {players} carriers, seed {seed}: {o.Killed} of {o.Spawned} felled in {o.Seconds} s, {o.Downs} downs; left {o.LeftKinds}";
                    Assert.True(o.Won, what);
                    Assert.True(o.Seconds <= 240, what);
                    // 9 since the Giants' siege (design choice 131): four carriers' shift 5 on seed 11
                    // had 31 downs before and 35 after, while the four seeds' sum fell from 97 to 74.
                    Assert.True(o.Downs <= 9 * players, what);
                }
        }

        [Fact]
        public void TwoCarriersInShiftThree_AreDownFiveTimesAtMostBetweenThem()
        {
            // Four before the Archers (design choice 128) shot at them from 15 m; 1–5 over the seeds since.
            foreach (uint seed in Seeds)
            {
                var o = Fight(seed, 3, 2, cannon: false, clumsy: true);
                Assert.True(o.Won && o.Downs <= 5 && o.FastReDowns == 0, $"seed {seed}: won {o.Won}, {o.Downs} downs, {o.FastReDowns} of them within 8 s of standing up");
            }
        }
        [Theory]
        [InlineData(2u)]
        [InlineData(13u)]
        public void OnTheGeneratedTown_TheRaidersFindTheirWayToThePostOffice(uint seed)
        {
            // The raid was tuned on the test street; on the seed's town they come in by the main street's east end.
            var world = new SimWorld(TestContent.Db, new SimSettings { Seed = seed, MapId = WorldGenerator.SmallIsland, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.NoTimer | PlaytestFlags.NoQuota | PlaytestFlags.RaidNow, 2);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            // Nobody walks out to them, so they come down the street to their line (nit 17) and hold it.
            world.RaidLine(out float lx, out float lz);
            float nearest = float.MaxValue;
            for (int t = 0; t < 120 * Tps && nearest > 3f; t++)
            {
                world.Step();
                foreach (var e in world.Enemies)
                    nearest = MathF.Min(nearest, MathF.Sqrt((e.X - lx) * (e.X - lx) + (e.Z - lz) * (e.Z - lz)));
            }
            Assert.True(world.EnemiesSpawnedTotal > 0);
            Assert.True(nearest <= 3f, $"seed {seed}: the nearest raider got no closer than {nearest:F1} m to the line at ({lx:F1}, {lz:F1})");
        }
    }

    /// <summary>
    /// A carrier who plays the raid the plain way: walks at the nearest raider and swings when it
    /// is in reach. Clumsy on purpose: looks again only every so often, aims off by some degrees
    /// and takes its time between blows, like a player who is new to it.
    /// </summary>
    public sealed class RaidBot
    {
        public float AimErrorDegrees = 0f;
        public int LookEveryTicks = 1;
        public int SwingEveryTicks = 1;
        public bool Cannon = true;

        private uint _rng;
        private uint _nextLook, _nextSwing;
        private ushort _yaw;
        private float _moveX, _moveZ, _dist = float.MaxValue;
        private bool _see;
        private uint _asideUntil;
        private float _asideSign = 1f;
        private float _lastX, _lastZ;

        public RaidBot(uint seed) { _rng = seed * 2654435761u + 12345u; }

        private float Noise()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng % 20001) / 10000f - 1f;
        }

        public PlayerInput Decide(SimWorld world, Player p)
        {
            var input = new PlayerInput { Seq = p.LastQueuedSeq + 1, Yaw = p.Move.Yaw };
            if (p.Dead) { _see = false; return input; }
            if (world.Tick >= _nextLook)
            {
                _nextLook = world.Tick + (uint)LookEveryTicks;
                Enemy? nearest = null;
                float best = float.MaxValue;
                foreach (var e in world.Enemies)
                {
                    // Out of the hammer's reach and after nobody: left to the cannon and the turrets.
                    if (e.Def.Airborne) continue;
                    float d = MathF.Sqrt((e.X - p.Move.X) * (e.X - p.Move.X) + (e.Z - p.Move.Z) * (e.Z - p.Move.Z));
                    if (d >= best) continue;
                    best = d;
                    nearest = e;
                }
                _see = nearest != null;
                if (nearest != null)
                {
                    float dx = nearest.X - p.Move.X, dz = nearest.Z - p.Move.Z;
                    _yaw = PlayerInput.QuantizeYaw(MathF.Atan2(-dx, -dz) + Noise() * AimErrorDegrees * MathF.PI / 180f);
                    _dist = best;
                    _moveX = dx / MathF.Max(best, 1e-3f);
                    _moveZ = dz / MathF.Max(best, 1e-3f);
                }
            }
            if (!_see) return input;
            input.Yaw = _yaw;
            float hammer = TestContent.Db.Balance.GetFloat("hammerRange");
            if (Cannon && world.HasAmmo(p) && _dist >= 6f && _dist <= 26f)
            {
                input.Buttons = InputButtons.AltAttack;
                return input;
            }
            if (_dist <= hammer)
            {
                if (world.Tick >= _nextSwing)
                {
                    input.Buttons = InputButtons.Attack;
                    _nextSwing = world.Tick + (uint)SwingEveryTicks;
                }
                return input;
            }
            // Against a wall for a second: a second and a half along it, the same way round, unless the
            // last time that way did not get it loose: then the other way.
            if (world.Tick % 30 == 0)
            {
                if (world.Tick >= _asideUntil && MathF.Abs(p.Move.X - _lastX) + MathF.Abs(p.Move.Z - _lastZ) < 0.5f)
                {
                    if (world.Tick <= _asideUntil + 30) _asideSign = -_asideSign;
                    _asideUntil = world.Tick + 45;
                }
                _lastX = p.Move.X;
                _lastZ = p.Move.Z;
            }
            bool aside = world.Tick < _asideUntil;
            input.MoveX = PlayerInput.QuantizeAxis(aside ? -_moveZ * _asideSign : _moveX);
            input.MoveZ = PlayerInput.QuantizeAxis(aside ? _moveX * _asideSign : _moveZ);
            return input;
        }
    }
}
