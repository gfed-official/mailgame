using System;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Doc 05 §2's steals_items trait, the Hog Rider: "each hit on a container pulls 1 random item
    /// out and drops it as a WorldItem 3 m away", and when felled it "returns 1 stolen item". The
    /// first thing it pulls out it rides off with; every further one lands in a bag that is
    /// nobody's 3 m from the container, on the rider's side (design choice 132).
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>How far from the container what is pulled out of it lands.</summary>
        private const float StolenThrow = 3f;

        /// <summary>One unit of a random stack of the container's, out of it: kept, or thrown aside.</summary>
        private void Steal(Enemy e, Construct built)
        {
            var chest = built.ContainerId != 0 ? GetContainer(built.ContainerId) : null;
            if (chest == null || chest.EntryCount == 0) return;
            SortedEntries(chest);
            var entry = _bagScratch[_lootRng.NextInt(_bagScratch.Count)];
            ItemStack one;
            if (entry.Stack.Count <= 1)
            {
                chest.Remove(entry.EntryId);
                one = entry.Stack;
            }
            else
            {
                one = entry.Stack.Take(1);
                chest.MarkDirty(entry.EntryId);
            }
            CommitContainer(chest.Id);
            if (e.Carried == null) { e.Carried = one; return; }

            // 3 m from the container towards the rider; where that is no open ground, at its feet.
            float cx = built.CentreX, cz = built.CentreZ;
            float dx = e.X - cx, dz = e.Z - cz, len = MathF.Sqrt(dx * dx + dz * dz);
            float x = e.X, z = e.Z;
            if (len > 1e-3f)
            {
                float tx = cx + dx / len * StolenThrow, tz = cz + dz / len * StolenThrow;
                int ttx = (int)MathF.Floor(tx / SimConstants.TileSize), ttz = (int)MathF.Floor(tz / SimConstants.TileSize);
                if (Map.InBounds(ttx, ttz) && !Map.IsNavBlocked(ttx, ttz) && !Constructs.Blocks(ttx, ttz)) { x = tx; z = tz; }
            }
            // Into the bag it threw the last one in while that lies there and has room.
            if (e.StolenBag != 0)
            {
                foreach (var bag in _bags)
                {
                    if (bag.Id != e.StolenBag || !InventoryOps.AutoPlace(Content, bag.Container, one, _entryIds)) continue;
                    CommitContainer(bag.Id);
                    return;
                }
            }
            e.StolenBag = DropAsBag(one, x, z);
        }

        /// <summary>
        /// A stack on the ground in a bag that is nobody's (as a broken chest's, <see cref="Spill"/>):
        /// when its time is up its mail is back in the Intake. The bag's id, 0 if it held nothing.
        /// </summary>
        private uint DropAsBag(ItemStack stack, float x, float z, uint despawnTick = 0)
        {
            var info = CreateContainer(DeathBagDefId, EntityId.None, x, z);
            var bag = new DeathBag { Container = info.Container, Owner = EntityId.None, X = x, Z = z, DespawnTick = despawnTick > Tick ? despawnTick : Tick + _combat.BagDespawnTicks };
            if (!InventoryOps.AutoPlace(Content, bag.Container, stack, _entryIds))
            {
                _containers.Remove(bag.Id);
                LoseStack(stack);
                return 0;
            }
            CommitContainer(bag.Id);
            _bags.Add(bag);
            Events.Add(new BagDroppedEvent { ContainerId = bag.Id, Owner = bag.Owner, X = bag.X, Z = bag.Z, DespawnTick = bag.DespawnTick });
            return bag.Id;
        }

        /// <summary>Felled: what it rode off with lies where it fell (doc 05 §2.2 "returns 1 stolen item").</summary>
        private void DropCarried(Enemy e)
        {
            if (e.Carried == null) return;
            DropAsBag(e.Carried, e.X, e.Z);
            e.Carried = null;
        }

        /// <summary>Got away with it: mail is never destroyed (doc 03 §3.3), it is back in the Intake; anything else is gone.</summary>
        private void LoseCarried(Enemy e)
        {
            if (e.Carried == null) return;
            LoseStack(e.Carried);
            e.Carried = null;
        }

        private void LoseStack(ItemStack stack)
        {
            if (stack.Kind != StackKind.Mail) return;
            foreach (var m in stack.Mail) Spawner.ReturnToIntake(m);
            CommitContainer(Intake.Id);
        }
    }
}
