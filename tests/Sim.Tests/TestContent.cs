using System;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>The repo's real content/, loaded once for the whole test run.</summary>
    public static class TestContent
    {
        private static readonly Lazy<ContentDb> Lazy =
            new Lazy<ContentDb>(() => ContentDb.LoadFromDirectoryTree(AppContext.BaseDirectory));

        public static ContentDb Db => Lazy.Value;

        public static MailItem Mail(string kindId, AddressId address, uint id = 1, int shift = 1)
        {
            var kind = Db.MailKind(kindId);
            return new MailItem
            {
                Id = id,
                Kind = kind.Index,
                Address = address,
                Value = (ushort)kind.BaseValue,
                SpawnShift = (byte)shift,
                DeadlineShift = (byte)(shift + kind.DeadlineOffsetShifts),
            };
        }
    }

    public static class SimDriver
    {
        /// <summary>Puts a player somewhere without walking (tests only).</summary>
        public static void Teleport(Player p, float x, float z)
        {
            p.Move.X = x;
            p.Move.Z = z;
        }

        /// <summary>Queues one input per tick steering straight at a point until within <paramref name="stopDistance"/>.</summary>
        public static bool WalkTo(SimWorld world, Player p, float x, float z, float stopDistance = 0.3f, int maxTicks = 3000, bool sprint = false)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                float dx = x - p.Move.X, dz = z - p.Move.Z;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist <= stopDistance) return true;
                var input = new PlayerInput
                {
                    Seq = p.LastQueuedSeq + 1,
                    MoveX = PlayerInput.QuantizeAxis(dx / dist),
                    MoveZ = PlayerInput.QuantizeAxis(dz / dist),
                    Buttons = sprint ? InputButtons.Sprint : InputButtons.None,
                };
                world.QueueInput(p.Id, input);
                world.Step();
            }
            return false;
        }

        public static void StepTicks(SimWorld world, int ticks)
        {
            for (int i = 0; i < ticks; i++) world.Step();
        }
    }
}
