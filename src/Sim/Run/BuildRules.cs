using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Run
{
    /// <summary>
    /// The rules of building by hand (doc 04 §1) that the server decides by and a client colours
    /// its ghost by: when one may build, what a recipe takes, which tiles take a construct, and
    /// what taking one down gives back.
    /// </summary>
    public static class BuildRules
    {
        /// <summary>Doc 01 §1 phase table: building in Prep and in Delivery, the raid included.</summary>
        public static bool IsOpen(RunPhase phase) => phase == RunPhase.Prep || phase == RunPhase.Delivery;

        /// <summary>Whether the team may build the recipe at all yet, materials aside.</summary>
        public static OpResult CheckRecipe(RecipeDef recipe, int shift, bool blueprintOwned)
        {
            if (recipe.Blueprint != null && !blueprintOwned) return OpResult.NeedsBlueprint;
            return recipe.UnlockShift > shift ? OpResult.RecipeLocked : OpResult.Ok;
        }

        /// <summary>Units of an item in the hotbar and the inventory together.</summary>
        public static int Have(GridContainer hotbar, GridContainer inventory, ItemDef item) => Count(hotbar, item) + Count(inventory, item);

        private static int Count(GridContainer container, ItemDef item)
        {
            int n = 0;
            foreach (var e in container.Entries)
                if (e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == item.Index) n += e.Stack.ItemCount;
            return n;
        }

        /// <summary>True if the pockets hold everything the recipe takes.</summary>
        public static bool HasMaterials(RecipeDef recipe, GridContainer hotbar, GridContainer inventory)
        {
            foreach (var input in recipe.Inputs)
                if (Have(hotbar, inventory, input.Item) < input.Count) return false;
            return true;
        }

        /// <summary>The longest line one drag lays (doc 04 §1.1), in tiles.</summary>
        public const int MaxLine = 16;

        /// <summary>
        /// The tiles of a drag line from the first to the last, which share a row or a column;
        /// false (and no tiles) for any other pair or a line longer than <see cref="MaxLine"/>.
        /// </summary>
        public static bool LineTiles(int x0, int z0, int x1, int z1, System.Collections.Generic.List<(int, int)> tiles)
        {
            tiles.Clear();
            if (x0 != x1 && z0 != z1) return false;
            int n = Math.Max(Math.Abs(x1 - x0), Math.Abs(z1 - z0)) + 1;
            if (n > MaxLine) return false;
            int dx = Math.Sign(x1 - x0), dz = Math.Sign(z1 - z0);
            for (int i = 0; i < n; i++) tiles.Add((x0 + dx * i, z0 + dz * i));
            return true;
        }

        public static float TileCentre(int t) => (t + 0.5f) * SimConstants.TileSize;

        /// <summary>
        /// Whether the ground of a tile takes a construct, whoever may be standing on it: open
        /// ground that is no street, with no house, tree, fence or fixture of the map on it and
        /// nothing built there yet.
        /// </summary>
        public static OpResult CheckTile(WorldMap map, ConstructField constructs, int tx, int tz, bool onStreet = false)
        {
            if (!map.InBounds(tx, tz)) return OpResult.BadRequest;
            // The street stays open: the mail has to get through, and so do the raiders (doc 04 §1.1);
            // an elevated belt crosses it overhead.
            if (map.GetTile(tx, tz) == TileType.Street && !onStreet) return OpResult.OnStreet;
            if (map.IsWater(tx, tz)) return OpResult.NotOnWater;
            if (map.IsNavBlocked(tx, tz) || constructs.At(tx, tz) != null || HoldsFixture(map, tx, tz)) return OpResult.Blocked;
            return OpResult.Ok;
        }

        /// <summary>
        /// Whether a building turned so takes the ground from a tile on: every tile it would stand
        /// on (a Belt Ramp's two), by <see cref="CheckTile"/>; the first one's verdict that is not Ok.
        /// </summary>
        public static OpResult CheckPlacement(WorldMap map, ConstructField constructs, BuildingDef def, int tx, int tz, int rotation)
        {
            if (def.OnWater) return CheckOnWater(map, constructs, def, tx, tz, rotation);
            if (def.IsPort) return CheckPort(map, constructs, def, tx, tz, rotation);
            for (int i = 0; i < Math.Max(1, def.TileCount); i++)
            {
                var (x, z) = Construct.TileOf(tx, tz, rotation, i % def.Length, i / def.Length);
                var verdict = CheckTile(map, constructs, x, z, def.OnStreet);
                if (verdict != OpResult.Ok) return verdict;
                // A Business Dock's yard is its unloading zone's way in: belts may run over it, nothing stands on it (design choice 114).
                if (map.DockAt(x, z) != null && !def.Carries) return OpResult.Blocked;
            }
            return OpResult.Ok;
        }

        /// <summary>
        /// Doc 04 §3.3: a Pier over shallow water on all its three tiles, built out from the shore
        /// (the tile behind it walkable ground or another pier's deck); a Rowboat on a tile of water
        /// of either depth. Nothing else may be built over them.
        /// </summary>
        public static OpResult CheckOnWater(WorldMap map, ConstructField constructs, BuildingDef def, int tx, int tz, int rotation)
        {
            if (!map.InBounds(tx, tz)) return OpResult.BadRequest;
            for (int i = 0; i < Math.Max(1, def.TileCount); i++)
            {
                var (x, z) = Construct.TileOf(tx, tz, rotation, i % def.Length, i / def.Length);
                if (!map.InBounds(x, z)) return OpResult.BadRequest;
                if (def.Deck ? !map.IsShallowWater(x, z) : !map.IsWater(x, z)) return OpResult.NotOnWater;
                if (constructs.At(x, z) != null) return OpResult.Blocked;
            }
            if (def.Deck)
            {
                var (bx, bz) = Construct.TileOf(tx, tz, rotation, -1, 0);
                if (!map.InBounds(bx, bz) || map.IsSolid(bx, bz) || (map.IsWater(bx, bz) && !Players.PlayerMovement.DeckOn(constructs, bx, bz))) return OpResult.NotFromShore;
            }
            return OpResult.Ok;
        }

        /// <summary>
        /// Doc 04 §3.3 Small Port, "3 × 4 with 2 tiles over deep water": its back two rows on open
        /// ground as anything built, its front two on deep water with nothing over it.
        /// </summary>
        public static OpResult CheckPort(WorldMap map, ConstructField constructs, BuildingDef def, int tx, int tz, int rotation)
        {
            for (int i = 0; i < def.TileCount; i++)
            {
                int along = i % def.Length;
                var (x, z) = Construct.TileOf(tx, tz, rotation, along, i / def.Length);
                if (!map.InBounds(x, z)) return OpResult.BadRequest;
                if (along < PortQuayRows)
                {
                    var verdict = CheckTile(map, constructs, x, z);
                    if (verdict != OpResult.Ok) return verdict;
                    if (map.DockAt(x, z) != null) return OpResult.Blocked;
                }
                else if (!map.IsWater(x, z) || map.IsShallowWater(x, z)) return OpResult.NotOnWater;
                else if (constructs.At(x, z) != null) return OpResult.Blocked;
            }
            return OpResult.Ok;
        }

        /// <summary>A Small Port's rows on land, from its back: the quay.</summary>
        public const int PortQuayRows = 2;

        /// <summary>
        /// True for a tile with something of the map on it that has to stay within reach: a
        /// mailbox, the Intake, the shop counter, a chest of the map, the pad and its seats.
        /// </summary>
        public static bool HoldsFixture(WorldMap map, int tx, int tz)
        {
            var po = map.PostOffice;
            if (OnTile(tx, tz, po.IntakeX, po.IntakeZ) || OnTile(tx, tz, po.SpawnPadX, po.SpawnPadZ)) return true;
            if (po.HasShop && OnTile(tx, tz, po.ShopX, po.ShopZ)) return true;
            for (int slot = 0; slot < PostOfficeDef.Seats; slot++)
            {
                po.Seat(slot, out float sx, out float sz);
                if (OnTile(tx, tz, sx, sz)) return true;
            }
            foreach (var d in map.Destinations)
                if (OnTile(tx, tz, d.MailboxX, d.MailboxZ)) return true;
            foreach (var c in map.Chests)
                if (OnTile(tx, tz, c.X, c.Z)) return true;
            return false;
        }

        private static bool OnTile(int tx, int tz, float x, float z) =>
            (int)MathF.Floor(x / SimConstants.TileSize) == tx && (int)MathF.Floor(z / SimConstants.TileSize) == tz;

        /// <summary>True if a body (a carrier, a raider) stands on the tile or reaches onto it.</summary>
        public static bool Touches(int tx, int tz, float x, float z, float radius)
        {
            const float tile = SimConstants.TileSize;
            float dx = x - Math.Clamp(x, tx * tile, (tx + 1) * tile), dz = z - Math.Clamp(z, tz * tile, (tz + 1) * tile);
            return dx * dx + dz * dz < radius * radius;
        }

        /// <summary>True if the middle of the tile is within <paramref name="range"/> of a point on the ground.</summary>
        public static bool InRange(int tx, int tz, float x, float z, float range)
        {
            float dx = TileCentre(tx) - x, dz = TileCentre(tz) - z;
            return dx * dx + dz * dz <= range * range;
        }

        /// <summary>The part of its materials a construct gives back when it is taken down (doc 04 §1.4): all of it in Prep, half in Delivery.</summary>
        public static double RefundRatio(Balance b, RunPhase phase) =>
            b.GetDouble(phase == RunPhase.Prep ? "deconstructRefundPrep" : "deconstructRefundDelivery");

        /// <summary>
        /// Units of one input that come back: the phase's part of what was put in, and of that as
        /// much as the construct has left of its HP. Rounded down.
        /// </summary>
        public static int Refund(int count, double ratio, int hp, int maxHp)
        {
            if (maxHp <= 0) return 0;
            double part = Math.Clamp(ratio, 0.0, 1.0) * Math.Clamp(hp, 0, maxHp) / maxHp;
            return (int)Math.Floor(count * part + 1e-9);
        }

        /// <summary>
        /// Units of each input that come back (playtest df77576: a Conveyor Belt, one Plank and one
        /// Iron Ingot, gave nothing back in Delivery). The part is reckoned over the materials as a
        /// whole, rounded down: each input gets its own share rounded down, and the units the whole
        /// has over those go one each to the inputs with the most left over, the dearer item first.
        /// Half of a belt is its Iron Ingot; half of a splitter (two Ingots and a Plank) one Ingot.
        /// </summary>
        public static int[] RefundCounts(IReadOnlyList<RecipeInput> inputs, double ratio, int hp, int maxHp)
        {
            var back = new int[inputs.Count];
            if (maxHp <= 0) return back;
            double part = Math.Clamp(ratio, 0.0, 1.0) * Math.Clamp(hp, 0, maxHp) / maxHp;
            int total = 0, given = 0;
            for (int i = 0; i < inputs.Count; i++)
            {
                total += inputs[i].Count;
                back[i] = Refund(inputs[i].Count, ratio, hp, maxHp);
                given += back[i];
            }
            int extra = (int)Math.Floor(total * part + 1e-9) - given;
            while (extra-- > 0)
            {
                int best = -1;
                double bestRest = 0;
                for (int i = 0; i < inputs.Count; i++)
                {
                    if (back[i] >= inputs[i].Count) continue;
                    double rest = inputs[i].Count * part - back[i];
                    if (best < 0 || rest > bestRest + 1e-9
                        || (Math.Abs(rest - bestRest) <= 1e-9 && inputs[i].Item.SellPrice > inputs[best].Item.SellPrice))
                    {
                        best = i;
                        bestRest = rest;
                    }
                }
                if (best < 0) break;
                back[best]++;
            }
            return back;
        }

        /// <summary>
        /// Puts what a construct gives back into the pockets, the inventory first. False (with
        /// whatever fitted put in) if not all of it has room: try it on copies first.
        /// </summary>
        public static bool GiveRefund(ContentDb content, RecipeDef recipe, double ratio, int hp, GridContainer inventory, GridContainer hotbar, EntryIdAllocator ids)
        {
            var counts = RefundCounts(recipe.Inputs, ratio, hp, recipe.Building.Hp);
            for (int i = 0; i < recipe.Inputs.Count; i++)
            {
                var input = recipe.Inputs[i];
                int left = counts[i];
                while (left > 0)
                {
                    int n = Math.Min(left, Math.Max(1, input.Item.MaxStack));
                    var stack = ItemStack.OfItem(input.Item, n);
                    if (!InventoryOps.AutoPlace(content, inventory, stack, ids) && !InventoryOps.AutoPlace(content, hotbar, stack, ids)) return false;
                    left -= n;
                }
            }
            return true;
        }

        /// <summary>True if everything the construct gives back has room in the pockets.</summary>
        public static bool RefundFits(ContentDb content, RecipeDef recipe, double ratio, int hp, GridContainer inventory, GridContainer hotbar) =>
            GiveRefund(content, recipe, ratio, hp, inventory.Clone(), hotbar.Clone(), new EntryIdAllocator(uint.MaxValue / 2));
    }
}
