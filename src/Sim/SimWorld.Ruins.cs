using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
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
            if (!Unlimited && !BuildRules.HasRuinMaterials(def, player.Hotbar, player.Inventory)) return OpResult.MissingMaterials;
            // The ruin holds its own ground: it is judged as if it were not there.
            Constructs.RemoveRuin(ruin.Tx, ruin.Tz);
            verdict = BuildRules.CheckPlacement(Map, Constructs, def, ruin.Tx, ruin.Tz, ruin.Rotation);
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
            if (!Unlimited)
            {
                foreach (var input in recipe.Inputs) TakeMaterials(player, input.Item, BuildRules.RuinCost(input.Count, def.RuinRebuildRatio));
                CommitContainer(player.Inventory.Id);
                CommitContainer(player.Hotbar.Id);
            }
            Events.Add(new RuinEvent { X = (byte)ruin.Tx, Y = (byte)ruin.Tz, Building = def.Index, Rotation = ruin.Rotation, Gone = true, Rebuilt = true, Player = player.Id });
            var built = Erect(def, ruin.Tx, ruin.Tz, ruin.Rotation, player.Id);
            // As it was set: a sorter's filters, a console's route.
            if (ruin.Filters.Length > 0)
            {
                Constructs.SetFilters(built, ruin.Filters);
                Events.Add(Configured(built, player.Id));
            }
            return OpResult.Ok;
        }

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
