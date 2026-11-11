using System;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    public class MovementTests
    {
        private static WorldMap Map => TestContent.Db.Map("test_street");
        private static readonly MoveConstants Consts = MoveConstants.From(TestContent.Db.Balance);

        private static PlayerMoveState At(float x, float z) =>
            new PlayerMoveState { X = x, Z = z, Grounded = true };

        private static PlayerInput Input(float mx, float mz, InputButtons buttons = InputButtons.None) => new PlayerInput
        {
            MoveX = PlayerInput.QuantizeAxis(mx),
            MoveZ = PlayerInput.QuantizeAxis(mz),
            Buttons = buttons,
        };

        private static void Run(ref PlayerMoveState s, PlayerInput input, int ticks, float speedMult = 1f)
        {
            for (int i = 0; i < ticks; i++) PlayerMovement.Step(ref s, input, Map, speedMult, Consts);
        }

        [Fact]
        public void WalksFiveMetresPerSecond()
        {
            var s = At(30f, 28f);
            Run(ref s, Input(1, 0), 30);
            Assert.InRange(s.X, 34.98f, 35.02f);
            Assert.Equal(28f, s.Z, 3);
        }

        [Fact]
        public void SprintsSevenAndAHalfMetresPerSecond()
        {
            var s = At(30f, 28f);
            Run(ref s, Input(1, 0, InputButtons.Sprint), 30);
            Assert.InRange(s.X, 37.48f, 37.52f);
        }

        [Fact]
        public void DiagonalInputIsNotFaster()
        {
            var s = At(30f, 28f);
            float d = MathF.Sqrt(0.5f);
            Run(ref s, Input(d, d), 12);
            float moved = MathF.Sqrt((s.X - 30f) * (s.X - 30f) + (s.Z - 28f) * (s.Z - 28f));
            Assert.InRange(moved, 1.98f, 2.02f);
        }

        [Fact]
        public void WeightMultiplierScalesSpeed()
        {
            var s = At(30f, 28f);
            Run(ref s, Input(1, 0), 30, speedMult: 0.6f);
            Assert.InRange(s.X, 32.98f, 33.02f);
        }

        [Fact]
        public void BuildingsAreSolid()
        {
            // House 1's building covers x 20..26 m, z 16..20 m; walk north into its south wall.
            var s = At(23f, 22f);
            Run(ref s, Input(0, -1), 60);
            Assert.InRange(s.Z, 20.399f, 20.41f);
            Assert.Equal(23f, s.X, 3);
        }

        [Fact]
        public void SlidesAlongWalls()
        {
            var s = At(21f, 20.4f);
            float d = MathF.Sqrt(0.5f);
            Run(ref s, Input(d, -d), 30);
            Assert.InRange(s.Z, 20.399f, 20.41f);
            Assert.True(s.X > 23f, $"expected to slide east, x = {s.X}");
        }

        [Fact]
        public void MapEdgeIsSolid()
        {
            var s = At(1f, 28f);
            Run(ref s, Input(-1, 0), 60);
            Assert.InRange(s.X, 0.399f, 0.41f);
        }

        [Fact]
        public void NeverEndsInsideASolidTile()
        {
            // Sweep a ring of directions from the lot beside the building.
            for (int i = 0; i < 32; i++)
            {
                double a = i * Math.PI / 16.0;
                var s = At(27.5f, 21f);
                var input = Input((float)Math.Cos(a), (float)Math.Sin(a), InputButtons.Sprint);
                for (int t = 0; t < 90; t++)
                {
                    PlayerMovement.Step(ref s, input, Map, 1f, Consts);
                    Assert.False(Map.IsSolid((int)MathF.Floor(s.X / 2f), (int)MathF.Floor(s.Z / 2f)),
                        $"inside solid at ({s.X}, {s.Z}) heading {i}");
                }
            }
        }

        [Fact]
        public void JumpReachesConfiguredHeightAndLands()
        {
            var s = At(30f, 28f);
            PlayerMovement.Step(ref s, Input(0, 0, InputButtons.Jump), Map, 1f, Consts);
            Assert.False(s.Grounded);
            float apex = s.Y;
            int ticks = 1;
            while (!s.Grounded && ticks < 120)
            {
                PlayerMovement.Step(ref s, Input(0, 0), Map, 1f, Consts);
                apex = MathF.Max(apex, s.Y);
                ticks++;
            }
            Assert.True(s.Grounded);
            Assert.Equal(0f, s.Y);
            Assert.InRange(apex, 1.18f, 1.201f);
        }

        [Fact]
        public void SameInputsGiveSameState()
        {
            var a = At(14f, 21f);
            var b = At(14f, 21f);
            var rng = new Random(7);
            for (int i = 0; i < 600; i++)
            {
                var input = Input((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1),
                    (InputButtons)rng.Next(0, 4));
                PlayerMovement.Step(ref a, input, Map, 1f, Consts);
                PlayerMovement.Step(ref b, input, Map, 1f, Consts);
            }
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Y, b.Y);
            Assert.Equal(a.Z, b.Z);
        }

        [Fact]
        public void YawQuantisationRoundTrips()
        {
            ushort q = PlayerInput.QuantizeYaw(MathF.PI / 2f);
            Assert.Equal(16384, q);
            Assert.Equal(MathF.PI / 2f, PlayerInput.YawToRadians(q), 3);
            Assert.Equal(0, PlayerInput.QuantizeYaw(2f * MathF.PI));
        }
    }
}
