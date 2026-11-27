using System;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.Vehicles;
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
        private float _serverBuildRange;

        private OpResult PlaceConstructOp(Player player, in SimRequest r)
        {
            var verdict = CheckPlace(player, r, out var def);
            return verdict != OpResult.Ok ? verdict : PlaceOne(player, def!, r.X, r.Y, r.Count);
        }

        /// <summary>
        /// Drag placement (doc 04 §1.1): one request for a straight line, each tile judged by
        /// itself. What can be built is built, from the start of the line on, until the materials
        /// run out; Ok if one tile or more was, else why the first could not be.
        /// </summary>
        private OpResult PlaceLineOp(Player player, in SimRequest r)
        {
            var verdict = CheckPlace(player, r, out var def);
            if (verdict != OpResult.Ok) return verdict;
            int x1 = (int)(r.ToContainer & 0xFF), z1 = (int)((r.ToContainer >> 8) & 0xFF);
            if (!BuildRules.LineTiles(r.X, r.Y, x1, z1, _lineScratch)) return OpResult.BadRequest;
            OpResult first = OpResult.Ok;
            int built = 0;
            foreach (var (tx, tz) in _lineScratch)
            {
                var one = PlaceOne(player, def!, tx, tz, r.Count);
                if (one == OpResult.Ok) built++;
                else if (built == 0 && first == OpResult.Ok) first = one;
                if (one == OpResult.MissingMaterials) break;
            }
            return built > 0 ? OpResult.Ok : first;
        }

        private readonly System.Collections.Generic.List<(int, int)> _lineScratch = new System.Collections.Generic.List<(int, int)>();

        private OpResult CheckPlace(Player player, in SimRequest r, out BuildingDef? def)
        {
            def = null;
            if (player.Dead) return OpResult.Dead;
            if (!BuildRules.IsOpen(Run.Phase)) return OpResult.WrongPhase;
            if (r.DestId >= Content.Buildings.Count) return OpResult.BadRequest;
            def = Content.Buildings[r.DestId];
            if (r.Count >= def.Rotations) return OpResult.BadRequest;
            var recipe = def.Recipe;
            return BuildRules.CheckRecipe(recipe, Run.Shift, recipe.Blueprint == null || _shopOwned.Contains(recipe.Blueprint) || Unlimited);
        }

        private OpResult PlaceOne(Player player, BuildingDef def, int tx, int tz, int rotation)
        {
            var recipe = def.Recipe;
            var verdict = BuildRules.CheckPlacement(Map, Constructs, def, tx, tz, rotation);
            if (verdict != OpResult.Ok) return verdict;
            float cx = BuildRules.TileCentre(tx), cz = BuildRules.TileCentre(tz);
            if (!player.WasWithin(cx, cz, _serverBuildRange, _rewindTicks)) return OpResult.OutOfRange;
            if (def.IsVehicleDepot && VehicleDepotCount() >= VehicleDepotLimit) return OpResult.DepotLimit;
            // What lies flat is laid under the feet.
            if (def.Solid)
                for (int i = 0; i < def.TileCount; i++)
                {
                    var (x, z) = Construct.TileOf(tx, tz, rotation, i % def.Length, i / def.Length);
                    if (TileOccupied(x, z)) return OpResult.Occupied;
                }
            if (def.Vehicle != null)
            {
                // Doc 10 §2 rowboat_build: a boat put down on the water, one of each kind for each carrier.
                foreach (var other in _vehicles)
                    if (Near(other.X, other.Z, cx, cz, 1.8f)) return OpResult.Occupied;
                int same = 0;
                foreach (var other in _vehicles) if (other.Def == def.Vehicle) same++;
                if (same >= Math.Max(1, PresentCount)) return OpResult.OnePerCarrier;
            }
            if (!Unlimited)
            {
                if (!BuildRules.HasMaterials(recipe, player.Hotbar, player.Inventory)) return OpResult.MissingMaterials;
                foreach (var input in recipe.Inputs) TakeMaterials(player, input.Item, input.Count);
                CommitContainer(player.Inventory.Id);
                CommitContainer(player.Hotbar.Id);
            }
            if (def.Vehicle != null)
            {
                var (fx, fz) = Construct.TileOf(0, 0, rotation, 1, 0);
                AddVehicleAt(def.Vehicle, cx, cz, Vehicle.YawOf(fx, fz, 0f), player.Id);
                return OpResult.Ok;
            }
            Erect(def, tx, tz, rotation, player.Id);
            return OpResult.Ok;
        }

        /// <summary>Puts a construct up for nothing and nobody, the clients told (the smoke; no placement is judged).</summary>
        public Construct PutUp(BuildingDef def, int tx, int tz, int rotation = 0) => Erect(def, tx, tz, rotation, EntityId.None);

        /// <summary>Takes a construct down for nothing, the clients told, no ruin left (the smoke).</summary>
        public void TakeDown(Construct built)
        {
            if (Constructs.At(built.Tx, built.Tz) == built) RemoveConstruct(built, EntityId.None, destroyed: false);
        }

        /// <summary>Puts a construct up on (tx, tz) turned so, the placement already judged; <paramref name="by"/> is told as its builder.</summary>
        private Construct Erect(BuildingDef def, int tx, int tz, int rotation, EntityId by)
        {
            float cx = BuildRules.TileCentre(tx), cz = BuildRules.TileCentre(tz);
            uint containerId = 0;
            if (def.Container != null)
            {
                // A Depot's grid is reached from round all of its four tiles.
                var centre = def.Width > 1 ? Construct.TileOf(tx, tz, rotation, def.Length - 1, def.Width - 1) : (tx, tz);
                float x = (cx + BuildRules.TileCentre(centre.Item1)) * 0.5f, z = (cz + BuildRules.TileCentre(centre.Item2)) * 0.5f;
                var info = CreateContainer(def.Container.Id, EntityId.None, x, z);
                if (def.Width > 1) info.Reach = def.Radius;
                containerId = info.Container.Id;
            }
            var built = Constructs.Add(def, tx, tz, rotation, Constructs.MaxHpOf(def), containerId);
            Events.Add(new ConstructPlacedEvent
            {
                Player = by, Building = def.Index, X = (byte)tx, Y = (byte)tz, Rotation = built.Rotation, Hp = (ushort)built.Hp, ContainerId = containerId,
            });
            if (def.Behaviour == BuildingBehaviour.Alarm) SetRaidWarnTick();
            return built;
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
            // Taking down a ruin clears it away, for nothing and giving nothing back.
            if (built == null) return Constructs.RuinAt(r.X, r.Y) != null ? ClearRuin(player, r.X, r.Y) : OpResult.BadRequest;
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
                if (!e.Def.Airborne && BuildRules.Touches(tx, tz, e.X, e.Z, e.Radius)) return true;
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
        /// A raider's blow on something built. At 0 HP it is gone and its ruin is left on its
        /// tiles (doc 05 §5, <see cref="LeaveRuin"/>); what a chest held lies on its tile in a bag
        /// that is nobody's, and the mail on a belt goes back to the Intake.
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
            LeaveRuin(built);
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
            if (built.Def.Behaviour == BuildingBehaviour.Alarm) SetRaidWarnTick();
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
            ClearDrivers();
            ClearTurrets();
            ClearBelts();
            ClearPipes();
            if (Constructs.All.Count == 0 && Constructs.Ruins.Count == 0) return;
            foreach (var built in Constructs.All) DropContainer(built);
            Constructs.Clear();
            Events.Add(new ConstructsClearedEvent());
        }
    }
}
