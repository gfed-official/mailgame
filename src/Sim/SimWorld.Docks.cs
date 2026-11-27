using System.Collections.Generic;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Business Docks (doc 02 §3.5, doc 03 §1.1 and §2.1; design choice 114): a warehouse on the
    /// coast that takes the Cargo addressed to it, which no hand can carry. In Delivery a vehicle
    /// standing in its unloading zone — a boat at its quay or a truck on its yard, within
    /// <c>dockZoneReach</c> of the zone's middle, parked or held still by its driver — has that
    /// Cargo taken out of its hold at the dock's insert rate, by the rules a belt goes by (it
    /// never takes what it would not pay for). A belt ending into the zone's tile feeds it too.
    /// An NPC driver or captain unloads by themselves.
    /// </summary>
    public sealed partial class SimWorld
    {
        private readonly Dictionary<ushort, uint> _dockNextTick = new Dictionary<ushort, uint>();

        /// <summary>True if a vehicle stands in a Business Dock's unloading zone.</summary>
        public bool InDockZone(DestinationDef dock, Vehicles.Vehicle v)
        {
            float reach = Content.Balance.GetFloat("dockZoneReach");
            return dock.IsDock && Near(v.X, v.Z, dock.MailboxX, dock.MailboxZ, reach);
        }

        private void StepDocks()
        {
            if (Run.Phase != RunPhase.Delivery || _vehicles.Count == 0) return;
            foreach (var dock in Map.Destinations)
            {
                if (!dock.IsDock || Map.OpensAt(dock, DistrictsEarly) > Run.Shift) continue;
                if (_dockNextTick.TryGetValue(dock.Id, out uint next) && Tick < next) continue;
                foreach (var v in _vehicles)
                {
                    if (v.NpcDriving || !InDockZone(dock, v)) continue;
                    if (v.Ridden && GetPlayer(v.Rider) is { } rider && rider.Move.RideSpeed > 0.5f) continue;
                    if (!UnloadOne(v.Cargo, dock, DeliveryMachine.Dock)) continue;
                    _dockNextTick[dock.Id] = Tick + InsertTicks(dock);
                    break;
                }
            }
        }
    }
}
