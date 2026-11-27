using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Death bags (doc 01 §2.2): a carrier who goes down leaves what they carried where they fell.
    /// Anyone may take from the bag; after deathBagDespawnSeconds its mail is back in the Intake.
    /// </summary>
    public sealed partial class SimWorld
    {
        public const string DeathBagDefId = "death_bag";

        private readonly List<DeathBag> _bags = new List<DeathBag>();
        private readonly List<GridEntry> _bagScratch = new List<GridEntry>();

        public IReadOnlyList<DeathBag> Bags => _bags;

        /// <summary>
        /// Empties the carrier's hotbar and inventory into a bag at their feet, each stack in the
        /// cell it had: the hotbar is the bag's first row, the inventory the rows under it. Who
        /// keeps their hotbar (<see cref="Perks.Stat.KeepHotbarOnDeath"/>) leaves that row empty.
        /// </summary>
        private void DropBag(Player p, bool keepHotbar = false)
        {
            if ((keepHotbar || p.Hotbar.EntryCount == 0) && p.Inventory.EntryCount == 0) return;
            var info = CreateContainer(DeathBagDefId, EntityId.None, p.Move.X, p.Move.Z);
            var bag = new DeathBag
            {
                Container = info.Container, Owner = p.Id, X = info.X, Z = info.Z,
                DespawnTick = Tick + _combat.BagDespawnTicks,
            };
            // Pockets a perk made bigger: the bag holds them all the same.
            info.Container.Resize(Math.Max(0, p.Hotbar.Rows + p.Inventory.Rows - info.Container.Def.Rows));
            if (!keepHotbar) MoveIntoBag(p.Hotbar, bag.Container, 0);
            MoveIntoBag(p.Inventory, bag.Container, p.Hotbar.Rows);
            CommitContainer(p.Hotbar.Id);
            CommitContainer(p.Inventory.Id);
            CommitContainer(bag.Id);
            if (bag.Container.EntryCount == 0)
            {
                _containers.Remove(bag.Id);
                return;
            }
            _bags.Add(bag);
            Events.Add(new BagDroppedEvent { ContainerId = bag.Id, Owner = bag.Owner, X = bag.X, Z = bag.Z, DespawnTick = bag.DespawnTick });
        }

        private void MoveIntoBag(GridContainer from, GridContainer bag, int row)
        {
            SortedEntries(from);
            foreach (var e in _bagScratch)
            {
                int x = e.X, y = e.Y + row;
                // A bag smaller than the pockets: wherever there is room, and what has none stays on the carrier.
                if (!bag.CanPlace(x, y, e.W, e.H) && !bag.FindFreeSpot(e.W, e.H, out x, out y)) continue;
                from.Remove(e.EntryId);
                e.X = x;
                e.Y = y;
                bag.Add(e);
            }
        }

        /// <summary>The container's entries in entry-id order, whatever order the dictionary has them in.</summary>
        private void SortedEntries(GridContainer container)
        {
            _bagScratch.Clear();
            _bagScratch.AddRange(container.Entries);
            _bagScratch.Sort((a, b) => a.EntryId.CompareTo(b.EntryId));
        }

        private void StepBags()
        {
            for (int i = _bags.Count - 1; i >= 0; i--)
            {
                var bag = _bags[i];
                if (bag.Container.EntryCount == 0) RemoveBag(i, returned: false, mail: 0);
                else if (Tick >= bag.DespawnTick) RemoveBag(i, returned: true, ReturnBag(bag));
            }
        }

        /// <summary>
        /// Time is up (doc 03 §3.3: mail is never destroyed by a despawn): the mail goes back to the
        /// Intake, anything else back to its owner's pockets. What has no room there, or no owner
        /// any more, is lost. Returns the mail items sent back.
        /// </summary>
        private int ReturnBag(DeathBag bag)
        {
            var owner = GetPlayer(bag.Owner);
            int mail = 0;
            bool items = false;
            SortedEntries(bag.Container);
            foreach (var e in _bagScratch)
            {
                bag.Container.Remove(e.EntryId);
                if (e.Stack.Kind == StackKind.Mail)
                {
                    foreach (var m in e.Stack.Mail) Spawner.ReturnToIntake(m);
                    mail += e.Stack.Mail.Count;
                }
                else if (owner != null)
                {
                    items = true;
                    // The bag's first rows were the hotbar: what lay there goes back to its slot, or else to the hotbar
                    // before the inventory (the e5be644 playtest's empty hotbar after a knockdown).
                    if (e.Y < owner.Hotbar.Rows)
                    {
                        if (owner.Hotbar.CanPlace(e.X, e.Y, e.W, e.H)) owner.Hotbar.Add(e);
                        else if (!InventoryOps.AutoPlace(Content, owner.Hotbar, e.Stack, _entryIds))
                            InventoryOps.AutoPlace(Content, owner.Inventory, e.Stack, _entryIds);
                    }
                    else if (!InventoryOps.AutoPlace(Content, owner.Inventory, e.Stack, _entryIds))
                        InventoryOps.AutoPlace(Content, owner.Hotbar, e.Stack, _entryIds);
                }
            }
            if (mail > 0) CommitContainer(Intake.Id);
            if (items)
            {
                CommitContainer(owner!.Inventory.Id);
                CommitContainer(owner.Hotbar.Id);
            }
            return mail;
        }

        private void RemoveBag(int index, bool returned, int mail)
        {
            var bag = _bags[index];
            _bags.RemoveAt(index);
            // StepPlayers looks every open container up by id: nobody may still have this one open.
            foreach (var p in _players)
                if (p.OpenContainer == bag.Id) CloseExternal(p, notify: true);
            _containers.Remove(bag.Id);
            Events.Add(new BagGoneEvent { ContainerId = bag.Id, Returned = returned, Mail = (ushort)System.Math.Min(ushort.MaxValue, mail) });
        }

        /// <summary>The phase is over: every bag still lying about is returned as if its time were up.</summary>
        private void ReturnAllBags()
        {
            for (int i = _bags.Count - 1; i >= 0; i--) RemoveBag(i, returned: true, ReturnBag(_bags[i]));
        }

        /// <summary>A player has left: their bags stay where they lie, as nobody's.</summary>
        private void DisownBags(EntityId player)
        {
            foreach (var bag in _bags)
                if (bag.Owner == player) bag.Owner = EntityId.None;
        }
    }
}
