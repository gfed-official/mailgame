using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Pings (doc 09 §2.6 "click to place a world ping visible to teammates … 10 s", design choice
    /// 117): a carrier marks a point of the map with one of five meanings; the server checks it and
    /// tells everyone, with the tick it is gone by. A carrier pings at most twice a second.
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>How long a ping stands (doc 09 §2.6: 10 s).</summary>
        public const float PingSeconds = 10f;
        /// <summary>The least time between two pings of one carrier.</summary>
        public const float PingIntervalSeconds = 0.5f;

        private OpResult PingOp(Player player, in SimRequest r)
        {
            if (r.Count > (ushort)PingKind.Materials) return OpResult.BadRequest;
            float x = r.EntryId / 100f, z = r.ToContainer / 100f;
            if (x > Map.WidthM || z > Map.DepthM) return OpResult.BadRequest;
            if (player.HasPinged && Tick - player.LastPingTick < SimConstants.SecondsToTicks(PingIntervalSeconds)) return OpResult.TooFast;
            player.HasPinged = true;
            player.LastPingTick = Tick;
            Events.Add(new PingEvent { Player = player.Id, X = x, Z = z, Kind = (PingKind)r.Count, ExpiresTick = Tick + SimConstants.SecondsToTicks(PingSeconds) });
            return OpResult.Ok;
        }
    }
}
