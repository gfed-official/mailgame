using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.Server
{
    /// <summary>
    /// The first tick of the first shift costs the server about 7 ms: the runtime compiles the
    /// shift's start (mail, addresses, the raid's plan) in the middle of the game. A host therefore
    /// runs the start of a shift once at boot, in a world of its own that nobody sees.
    /// </summary>
    public static class SimWarmUp
    {
        /// <summary>Three seconds: into the shift and past its first mail.</summary>
        public const int Ticks = 90;

        public static void Run(ContentDb content, uint seed)
        {
            var world = new SimWorld(content, new SimSettings { Seed = seed, QuickStart = true });
            world.AddPlayer("warm-up");
            for (int i = 0; i < Ticks; i++)
            {
                world.Step();
                world.Events.Clear();
            }
        }
    }
}
