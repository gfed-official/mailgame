using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Ruins (doc 05 §5; design choice 126): a construct broken at 0 HP leaves a ruin on its tiles
    /// — it does nothing, stops nobody and is no raider's target, but holds the ground and what it
    /// was set to (a sorter's filters, a console's route). In Prep a carrier rebuilds it from
    /// beside it for <see cref="Content.BuildingDef.RuinRebuildRatio"/> of the recipe (half, each
    /// input rounded up), as it was; in Prep or Delivery it is cleared away for nothing, and the
    /// tile is free to build on at full cost. The run's end clears them with everything built.
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>Ruins left since the world began.</summary>
        public int RuinsLeft { get; private set; }

        private void LeaveRuin(Construct broken)
        {
            Constructs.SetHp(broken, 0);
            Constructs.AddRuin(broken);
            RuinsLeft++;
            Events.Add(new RuinEvent { X = (byte)broken.Tx, Y = (byte)broken.Tz, Building = broken.Def.Index, Rotation = broken.Rotation });
        }

        private OpResult RebuildRuinOp(Player player, in SimRequest r)
        {
            if (player.Dead) return OpResult.Dead;
            if (!Map.InBounds(r.X, r.Y)) return OpResult.BadRequest;
            if (r.Count == 0) return ClearRuin(player, r.X, r.Y);
            var ruin = Constructs.RuinAt(r.X, r.Y);
            if (ruin == null) return OpResult.BadRequest;
            // Doc 05 §5: rebuilt in Prep; in Delivery it is cleared and built anew, or left.
            if (Run.Phase != RunPhase.Prep) return OpResult.WrongPhase;
            if (!player.WasWithin(ruin.CentreX, ruin.CentreZ, _serverBuildRange + ruin.Def.Radius, _rewindTicks)) return OpResult.OutOfRange;
            var def = ruin.Def;
            var recipe = def.Recipe;
            var verdict = BuildRules.CheckRecipe(recipe, Run.Shift, recipe.Blueprint == null || _shopOwned.Contains(recipe.Blueprint) || Unlimited);
            if (verdict != OpResult.Ok) return verdict;
            if (def.IsVehicleDepot && VehicleDepotCount() >= VehicleDepotLimit) return OpResult.DepotLimit;
            var depot = BuildDepot;
            if (!Unlimited && !BuildRules.HasRuinMaterials(def, player.Hotbar, player.Inventory, depot)) return OpResult.MissingMaterials;
            verdict = Raise(ruin, player.Id, () =>
            {
                if (Unlimited) return;
                foreach (var input in recipe.Inputs) TakeMaterials(player, input.Item, BuildRules.RuinCost(input.Count, def.RuinRebuildRatio), depot);
                CommitContainer(player.Inventory.Id);
                CommitContainer(player.Hotbar.Id);
                if (depot != null) CommitContainer(depot.Id);
            });
            return verdict;
        }

        /// <summary>
        /// Stands the ruin up again as it was, if its ground allows; <paramref name="pay"/> runs just before. By a
        /// carrier's hand, or by nobody's (<see cref="EntityId.None"/>) when the team's insurance pays.
        /// </summary>
        private OpResult Raise(Construct ruin, EntityId by, System.Action? pay)
        {
            var def = ruin.Def;
            // The ruin holds its own ground: it is judged as if it were not there.
            Constructs.RemoveRuin(ruin.Tx, ruin.Tz);
            var verdict = BuildRules.CheckPlacement(Map, Constructs, def, ruin.Tx, ruin.Tz, ruin.Rotation);
            if (verdict == OpResult.Ok && def.Solid)
                for (int i = 0; i < def.TileCount && verdict == OpResult.Ok; i++)
                {
                    var (x, z) = ruin.TileAt(i);
                    if (TileOccupied(x, z)) verdict = OpResult.Occupied;
                }
            if (verdict != OpResult.Ok)
            {
                Constructs.AddRuin(ruin);
                return verdict;
            }
            pay?.Invoke();
            Events.Add(new RuinEvent { X = (byte)ruin.Tx, Y = (byte)ruin.Tz, Building = def.Index, Rotation = ruin.Rotation, Gone = true, Rebuilt = true, Player = by });
            var built = Erect(def, ruin.Tx, ruin.Tz, ruin.Rotation, by);
            // As it was set: a sorter's filters, a console's route.
            if (ruin.Filters.Length > 0)
            {
                Constructs.SetFilters(built, ruin.Filters);
                Events.Add(Configured(built, by));
            }
            return OpResult.Ok;
        }

        /// <summary>
        /// Doc 10 §4.4 Insured (rule flag InsuredRebuild): as Prep begins, every ruin stands again, free, where its
        /// ground allows (one under a carrier or a vehicle stays a ruin, to be rebuilt by hand). A Vehicle Depot over
        /// the limit stays a ruin too. Returns how many stood up.
        /// </summary>
        private int RaiseInsuredRuins()
        {
            if (!TeamHas(RuleFlag.InsuredRebuild)) return 0;
            int raised = 0;
            foreach (var ruin in new System.Collections.Generic.List<Construct>(Constructs.Ruins))
            {
                if (ruin.Def.IsVehicleDepot && VehicleDepotCount() >= VehicleDepotLimit) continue;
                if (Raise(ruin, EntityId.None, null) == OpResult.Ok) raised++;
            }
            if (raised > 0) InsuredRaised += raised;
            return raised;
        }

        /// <summary>Ruins the team's insurance has stood up again since the world began.</summary>
        public int InsuredRaised { get; private set; }

        private OpResult ClearRuin(Player player, int tx, int tz)
        {
            var ruin = Constructs.RuinAt(tx, tz);
            if (ruin == null) return OpResult.BadRequest;
            if (!BuildRules.IsOpen(Run.Phase)) return OpResult.WrongPhase;
            if (!player.WasWithin(ruin.CentreX, ruin.CentreZ, _serverBuildRange + ruin.Def.Radius, _rewindTicks)) return OpResult.OutOfRange;
            Constructs.RemoveRuin(tx, tz);
            Events.Add(new RuinEvent { X = (byte)ruin.Tx, Y = (byte)ruin.Tz, Building = ruin.Def.Index, Rotation = ruin.Rotation, Gone = true, Player = player.Id });
            return OpResult.Ok;
        }
    }
}
