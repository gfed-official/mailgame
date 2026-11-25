using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Oil Pumps (doc 10 §2.3, doc 11 "Pump output 1 Oil Can per 45 s", doc 12 M2 "Oil Pump for
    /// vehicle fuel (no power draw)"; design choice 116). A pump on an oil seep fills an Oil Can
    /// into its tank every <see cref="Content.BuildingDef.CycleSeconds"/> of Prep and Delivery
    /// while the tank has room; carriers take the cans out as from a chest. A fuelled vehicle
    /// standing still beside a pump is filled from its tank, a can at a time, while its own tank
    /// takes a whole can — the pump's forecourt.
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>Ticks of work into the can a pump is filling.</summary>
        private readonly Dictionary<Construct, uint> _pumpWork = new Dictionary<Construct, uint>();
        private readonly List<Construct> _pumpScratch = new List<Construct>();

        /// <summary>How far from a pump's middle a vehicle stands to be filled from it.</summary>
        public const float PumpReach = 3.2f;

        /// <summary>Ticks of work into a pump's next can (0 when it has just filled one or stands idle).</summary>
        public uint PumpWork(Construct pump) => _pumpWork.TryGetValue(pump, out uint t) ? t : 0;

        private void StepPumps()
        {
            if (Run.Phase != RunPhase.Prep && Run.Phase != RunPhase.Delivery) return;
            _pumpScratch.Clear();
            foreach (var c in Constructs.All) if (c.Def.IsPump) _pumpScratch.Add(c);
            if (_pumpScratch.Count == 0)
            {
                if (_pumpWork.Count > 0) _pumpWork.Clear();
                return;
            }
            var can = Content.Item("oil_can");
            foreach (var pump in _pumpScratch)
            {
                var tank = GetContainer(pump.ContainerId);
                if (tank == null) continue;
                var stack = ItemStack.OfItem(can, 1);
                if (InventoryOps.HasRoomFor(Content, tank, stack))
                {
                    uint work = PumpWork(pump) + 1;
                    if (work >= SimConstants.SecondsToTicks(pump.Def.CycleSeconds))
                    {
                        InventoryOps.AutoPlace(Content, tank, stack, _entryIds);
                        CommitContainer(tank.Id);
                        work = 0;
                    }
                    _pumpWork[pump] = work;
                }
                // The forecourt: a vehicle held still beside it, whose tank takes a whole can.
                if (_vehicles.Count == 0 || Tick % SimConstants.TickRate != 0) continue;
                foreach (var v in _vehicles)
                {
                    if (!v.Def.Fuelled || v.Fuel + v.Def.FuelSecondsPerCan > v.Def.TankSeconds + 0.5f) continue;
                    if (!Near(v.X, v.Z, pump.CentreX, pump.CentreZ, PumpReach + v.Def.BodyOffset)) continue;
                    if (v.NpcDriving || (v.Ridden && GetPlayer(v.Rider) is { } rider && rider.Move.RideSpeed > 0.5f)) continue;
                    if (!TakeOne(tank, can.Index)) break;
                    CommitContainer(tank.Id);
                    v.Fuel = MathF.Min(v.Def.TankSeconds, v.Fuel + v.Def.FuelSecondsPerCan);
                    if (GetPlayer(v.Rider) is { } skipper) skipper.Move.Becalmed = false;
                    Events.Add(new VehicleFuelEvent { Vehicle = v.Id, Seconds = v.Fuel });
                }
            }
            // A pump taken down forgets its work.
            if (_pumpWork.Count > _pumpScratch.Count)
            {
                var gone = new List<Construct>();
                foreach (var c in _pumpWork.Keys) if (!_pumpScratch.Contains(c)) gone.Add(c);
                foreach (var c in gone) _pumpWork.Remove(c);
            }
        }

        /// <summary>One of an item out of a container; false if it holds none.</summary>
        private static bool TakeOne(GridContainer box, int itemIndex)
        {
            foreach (var e in box.Entries)
            {
                if (e.Stack.Kind != StackKind.Item || e.Stack.DefIndex != itemIndex) continue;
                if (e.Stack.ItemCount > 1) { e.Stack.ItemCount--; box.MarkDirty(e.EntryId); }
                else box.Remove(e.EntryId);
                return true;
            }
            return false;
        }
    }
}
