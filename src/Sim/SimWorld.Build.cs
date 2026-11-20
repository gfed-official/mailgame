using System;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Building by hand (doc 04 §1): in Prep and in Delivery a carrier puts a Wooden Wall or a
    /// Chest on a tile within reach, out of the materials in their pockets, and takes it down
    /// again for a part of them. What is built stands for the run; the raiders break what is in
    /// their way.
    /// </summary>
    public sealed partial class SimWorld
    {
        private readonly float _serverBuildRange;

        private OpResult PlaceConstructOp(Player player, in SimRequest r)
        {
            if (player.Dead) return OpResult.Dead;
            if (!BuildRules.IsOpen(Run.Phase)) return OpResult.WrongPhase;
            if (r.DestId >= Content.Buildings.Count) return OpResult.BadRequest;
            var def = Content.Buildings[r.DestId];
            var recipe = def.Recipe;
            if (r.Count >= def.Rotations) return OpResult.BadRequest;
            int tx = r.X, tz = r.Y;
            var verdict = BuildRules.CheckRecipe(recipe, Run.Shift, recipe.Blueprint == null || _shopOwned.Contains(recipe.Blueprint));
            if (verdict != OpResult.Ok) return verdict;
            verdict = BuildRules.CheckTile(Map, Constructs, tx, tz);
            if (verdict != OpResult.Ok) return verdict;
            float cx = BuildRules.TileCentre(tx), cz = BuildRules.TileCentre(tz);
            if (!player.WasWithin(cx, cz, _serverBuildRange, _rewindTicks)) return OpResult.OutOfRange;
            if (TileOccupied(tx, tz)) return OpResult.Occupied;
            if (!BuildRules.HasMaterials(recipe, player.Hotbar, player.Inventory)) return OpResult.MissingMaterials;

            foreach (var input in recipe.Inputs) TakeMaterials(player, input.Item, input.Count);
            CommitContainer(player.Inventory.Id);
            CommitContainer(player.Hotbar.Id);
            uint containerId = def.Container != null ? CreateContainer(def.Container.Id, EntityId.None, cx, cz).Container.Id : 0;
            var built = Constructs.Add(def, tx, tz, r.Count, def.Hp, containerId);
            Events.Add(new ConstructPlacedEvent
            {
                Player = player.Id, Building = def.Index, X = (byte)tx, Y = (byte)tz, Rotation = built.Rotation, Hp = (ushort)built.Hp, ContainerId = containerId,
            });
            return OpResult.Ok;
        }

        /// <summary>
        /// Taking down gives materials back (doc 04 §1.4): all of them in Prep, half in Delivery,
        /// and of that as much as the construct has left of its HP. Anybody of the team may take
        /// down what anybody built; a chest only once it is empty.
        /// </summary>
        private OpResult RemoveConstructOp(Player player, in SimRequest r)
        {
            if (player.Dead) return OpResult.Dead;
            if (!BuildRules.IsOpen(Run.Phase)) return OpResult.WrongPhase;
            var built = Constructs.At(r.X, r.Y);
            if (built == null) return OpResult.BadRequest;
            if (!player.WasWithin(built.X, built.Z, _serverBuildRange, _rewindTicks)) return OpResult.OutOfRange;
            if (built.ContainerId != 0 && GetContainer(built.ContainerId) is { EntryCount: > 0 }) return OpResult.NotEmpty;
            var recipe = built.Def.Recipe;
            double ratio = BuildRules.RefundRatio(Content.Balance, Run.Phase);
            if (!BuildRules.RefundFits(Content, recipe, ratio, built.Hp, player.Inventory, player.Hotbar)) return OpResult.DoesNotFit;

            BuildRules.GiveRefund(Content, recipe, ratio, built.Hp, player.Inventory, player.Hotbar, _entryIds);
            CommitContainer(player.Inventory.Id);
            CommitContainer(player.Hotbar.Id);
            RemoveConstruct(built, player.Id, destroyed: false);
            return OpResult.Ok;
        }

        /// <summary>True if a carrier or a raider stands on the tile or reaches onto it: nothing is built into a body.</summary>
        private bool TileOccupied(int tx, int tz)
        {
            foreach (var p in _players)
                if (!p.Dead && BuildRules.Touches(tx, tz, p.Move.X, p.Move.Z, _move.Radius)) return true;
            foreach (var e in _enemies)
                if (BuildRules.Touches(tx, tz, e.X, e.Z, e.Radius)) return true;
            return false;
        }

        /// <summary>Out of the inventory first, then the hotbar; stacks in entry order. The caller commits.</summary>
        private void TakeMaterials(Player player, ItemDef item, int count)
        {
            for (int i = 0; i < 2 && count > 0; i++)
            {
                var container = i == 0 ? player.Inventory : player.Hotbar;
                SortedEntries(container);
                foreach (var e in _bagScratch)
                {
                    if (count == 0) break;
                    if (e.Stack.Kind != StackKind.Item || e.Stack.DefIndex != item.Index) continue;
                    int n = Math.Min(count, e.Stack.ItemCount);
                    e.Stack.ItemCount -= n;
                    count -= n;
                    if (e.Stack.ItemCount <= 0) container.Remove(e.EntryId);
                    else container.MarkDirty(e.EntryId);
                }
            }
        }

        /// <summary>
        /// A raider's blow on something built. At 0 HP it is gone for good (ruins and rebuilding
        /// come with M3); what a chest held lies on its tile in a bag that is nobody's.
        /// </summary>
        public void DamageConstruct(Construct built, float damage)
        {
            if (damage <= 0f || Constructs.At(built.Tx, built.Tz) != built) return;
            int hp = built.Hp - Math.Max(1, (int)MathF.Round(damage));
            if (hp > 0)
            {
                Constructs.SetHp(built, hp);
                Events.Add(new ConstructDamagedEvent { X = (byte)built.Tx, Y = (byte)built.Tz, Hp = (ushort)hp });
                return;
            }
            var chest = built.ContainerId != 0 ? GetContainer(built.ContainerId) : null;
            if (chest != null && chest.EntryCount > 0) Spill(chest, built.X, built.Z);
            RemoveConstruct(built, EntityId.None, destroyed: true);
        }

        /// <summary>
        /// What a broken chest held, as a bag where it stood (doc 05 §4: a destroyed container
        /// spills its items). The bag is nobody's: when its time is up its mail is back in the
        /// Intake and the rest is lost.
        /// </summary>
        private void Spill(GridContainer chest, float x, float z)
        {
            var info = CreateContainer(DeathBagDefId, EntityId.None, x, z);
            var bag = new DeathBag { Container = info.Container, Owner = EntityId.None, X = x, Z = z, DespawnTick = Tick + _combat.BagDespawnTicks };
            MoveIntoBag(chest, bag.Container, 0);
            CommitContainer(bag.Id);
            // What a bag has no room for: mail is never destroyed (doc 03 §3.3).
            bool mail = false;
            foreach (var e in chest.Entries)
            {
                if (e.Stack.Kind != StackKind.Mail) continue;
                foreach (var m in e.Stack.Mail) Spawner.ReturnToIntake(m);
                mail = true;
            }
            if (mail) CommitContainer(Intake.Id);
            _bags.Add(bag);
            Events.Add(new BagDroppedEvent { ContainerId = bag.Id, Owner = bag.Owner, X = bag.X, Z = bag.Z, DespawnTick = bag.DespawnTick });
        }

        private void RemoveConstruct(Construct built, EntityId by, bool destroyed)
        {
            DropContainer(built);
            Constructs.Remove(built.Tx, built.Tz);
            Events.Add(new ConstructRemovedEvent { X = (byte)built.Tx, Y = (byte)built.Tz, Player = by, Destroyed = destroyed });
        }

        private void DropContainer(Construct built)
        {
            if (built.ContainerId == 0) return;
            // StepPlayers looks every open container up by id: nobody may still have this one open.
            foreach (var p in _players)
                if (p.OpenContainer == built.ContainerId) CloseExternal(p, notify: true);
            _containers.Remove(built.ContainerId);
        }

        /// <summary>A run ends or begins: nothing built stands any more, and what was in the chests is gone with them.</summary>
        private void ClearConstructs()
        {
            if (Constructs.All.Count == 0) return;
            foreach (var built in Constructs.All) DropContainer(built);
            Constructs.Clear();
            Events.Add(new ConstructsClearedEvent());
        }
    }
}
