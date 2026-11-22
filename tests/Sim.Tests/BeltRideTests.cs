using System.Linq;
using PerformativeMail.Sim.Automation;
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
    /// Doc 04 §2.1 "player operation": a carrier standing on a belt is carried, at the belt's speed
    /// the way it runs, standing on its running surface; one who pushes a belt (interact held at it)
    /// makes its segment run at doc 11's operated multiplier, 1.5×, while held.
    /// </summary>
    public class BeltRideTests
    {
        private const int Tps = 30;
        private const int Row = 12;

        private uint _nextRequest = 1;
        private uint _nextMail = 990000;

        private static ContentDb Db => TestContent.Db;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static float Centre(int t) => BuildRules.TileCentre(t);

        /// <summary>Ticks of a carrier standing still: the server moves a player by their inputs, one a tick.</summary>
        private static void Stand(SimWorld world, Player p, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.QueueInput(p.Id, new PlayerInput { Seq = p.LastQueuedSeq + 1, Yaw = p.Move.Yaw });
                world.Step();
            }
        }

        private (SimWorld World, Player Host) WithALine(int x0 = 5, int x1 = 8)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 20)));
            SimDriver.Teleport(host, Centre(8), Centre(Row) + 1.2f);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceLine(0, Db.Building("belt_mk1").Index, x0, Row, x1, Row, 0)));
            world.Step();
            return (world, host);
        }

        [Fact]
        public void ACarrierOnABelt_IsCarriedTheWayItRuns_AtItsSpeed_StandingOnIt()
        {
            var (world, host) = WithALine();
            float ground = world.Map.HeightAt(Centre(5), Centre(Row));
            SimDriver.Teleport(host, Centre(5), Centre(Row));
            Stand(world, host, 1);
            float x = host.Move.X;
            Assert.Equal(ground + BeltNetwork.SurfaceHeight, host.Move.Y, 3);
            Stand(world, host, Tps);
            // No input: 2 m in a second, east along the line, still on it.
            Assert.InRange(host.Move.X - x, 1.95f, 2.05f);
            Assert.Equal(Centre(Row), host.Move.Z, 3);
            // Off the end it stands on the ground again, and nothing carries it.
            Stand(world, host, 3 * Tps);
            Assert.True(host.Move.X > Centre(8) + 1f, $"stopped at {host.Move.X}");
            float off = host.Move.X;
            Stand(world, host, Tps);
            Assert.Equal(off, host.Move.X, 3);
            Assert.Equal(world.Map.HeightAt(host.Move.X, host.Move.Z), host.Move.Y, 3);
        }

        [Fact]
        public void TheSameStep_OnAClient_PredictsTheRideExactly()
        {
            var (world, _) = WithALine();
            var c = MoveConstants.From(Db);
            var a = new PlayerMoveState { X = Centre(5), Z = Centre(Row), Grounded = true };
            var b = a;
            var input = new PlayerInput { MoveX = PlayerInput.QuantizeAxis(0.3f) };
            for (int i = 0; i < 40; i++)
            {
                PlayerMovement.Step(ref a, input, world.Map, 1f, c, world.Constructs);
                PlayerMovement.Step(ref b, input, world.Map, 1f, c, world.Constructs);
            }
            Assert.Equal((a.X, a.Y, a.Z), (b.X, b.Y, b.Z));
            // Walking with the belt: the walk and the belt add up.
            Assert.True(a.X - Centre(5) > 40 * SimConstants.TickDt * (2f + 0.3f * c.WalkSpeed) - 0.05f, $"at {a.X}");
        }

        [Fact]
        public void APush_SpeedsTheSegmentUp_ForAMoment_AndEverybodyHearsOfIt()
        {
            var (world, host) = WithALine();
            var seg = world.Belts.SegmentAt(5, Row)!;
            var dest = world.Map.Destinations.Single(d => d.Address.Number == 1);
            Assert.True(world.Belts.TryInsert(seg, TestContent.Mail("letter", dest.Address, _nextMail++, world.Run.Shift), out _));
            float before = seg.Items[0].Pos;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PushBelt(0, 7, Row)));
            var pushed = Assert.Single(world.Events.OfType<BeltPushedEvent>());
            Assert.Equal((5, Row, host.Id), (pushed.X, pushed.Y, pushed.Player));
            Assert.Equal(world.Belts.PushTickCount, pushed.Ticks);
            Assert.Equal(1.5f, world.Belts.PushMultiplier);
            // Ten ticks pushed: 3 m/s.
            float start = seg.Items[0].Pos;
            SimDriver.StepTicks(world, 10);
            Assert.Equal(10 * SimConstants.TickDt * 3f, seg.Items[0].Pos - start, 3);
            // Let go, it runs out and goes back to 2 m/s.
            SimDriver.StepTicks(world, world.Belts.PushTickCount);
            Assert.Equal(0, seg.PushTicks);
            start = seg.Items[0].Pos;
            SimDriver.StepTicks(world, 10);
            Assert.Equal(10 * SimConstants.TickDt * 2f, seg.Items[0].Pos - start, 3);
            Assert.True(start > before);

            // Out of reach, or no belt: nothing.
            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.PushBelt(0, 7, Row + 1)));
            SimDriver.Teleport(host, Centre(20), Centre(Row) + 1.2f);
            SimDriver.StepTicks(world, 15);
            Assert.Equal(OpResult.OutOfRange, Request(world, host, SimRequest.PushBelt(0, 7, Row)));
            Assert.Equal(0, seg.PushTicks);
        }
    }
}
