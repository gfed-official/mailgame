using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Combat;
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
            public int Spawned, Killed, Downs, Seconds, FastReDowns, Left;
            public bool Won;
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
            for (int t = 0; t < maxSeconds * Tps; t++)
            {
                foreach (var p in team) world.QueueInput(p.Id, bots[p].Decide(world, p));
                world.Step();
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
                if (world.RaidPulsesSpawned >= pulses && world.Enemies.Count == 0) { o.Won = true; break; }
            }
            o.Spawned = world.EnemiesSpawnedTotal;
            o.Killed = world.EnemiesKilledTotal;
            o.Left = world.Enemies.Count;
            return o;
        }

        private static readonly uint[] Seeds = { 3, 11, 21, 40 };
        private static readonly int Shifts = new SimWorld(TestContent.Db, new SimSettings()).ShiftCount;

        [Fact]
        public void TheBudgetGrowsFromShiftToShift_ByAThirdAtMost()
        {
            // The a5d987e playtest: 16 raiders in shift 2, 32 in shift 3, and no way to win it.
            var waves = TestContent.Db.Waves;
            Assert.Equal(120, waves.BudgetFor(2));
            for (int shift = 3; shift <= Shifts; shift++)
            {
                double step = waves.BudgetFor(shift) / waves.BudgetFor(shift - 1);
                Assert.InRange(step, 1.15, 1.34);
            }
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
                    string what = $"shift {shift}, {players} carriers, seed {seed}: {o.Killed} of {o.Spawned} felled in {o.Seconds} s, {o.Downs} downs";
                    Assert.True(o.Won, what);
                    Assert.True(o.Seconds <= 240, what);
                    Assert.True(o.Downs <= 8 * players, what);
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
            // Against a wall for a second: a second along it, the same way round every time.
            if (world.Tick % 30 == 0)
            {
                if (world.Tick >= _asideUntil && MathF.Abs(p.Move.X - _lastX) + MathF.Abs(p.Move.Z - _lastZ) < 0.5f) _asideUntil = world.Tick + 45;
                _lastX = p.Move.X;
                _lastZ = p.Move.Z;
            }
            bool aside = world.Tick < _asideUntil;
            input.MoveX = PlayerInput.QuantizeAxis(aside ? -_moveZ : _moveX);
            input.MoveZ = PlayerInput.QuantizeAxis(aside ? _moveX : _moveZ);
            return input;
        }
    }
}
