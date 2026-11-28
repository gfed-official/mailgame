using System;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;

namespace PerformativeMail.Sim.Run
{
    /// <summary>
    /// The rules of the Post Office shop (doc 03 §4.4, doc 10 §3) that the server decides by and a
    /// client greys its cards by: when the counter serves, what an entry costs this shift, and
    /// whether a purchase goes into the buyer's pockets.
    /// </summary>
    public static class ShopRules
    {
        /// <summary>Doc 01 §1 phase table: the shop is open in Prep and at Payday.</summary>
        public static bool IsOpen(RunPhase phase) => phase == RunPhase.Prep || phase == RunPhase.Payday;

        /// <summary>
        /// At Payday the counter serves the team wherever it stands: the shift ends with the
        /// carriers out on their rounds, and the window is too short to walk back in.
        /// </summary>
        public static bool ServesAnywhere(RunPhase phase) => phase == RunPhase.Payday;

        /// <summary>
        /// Whether a carrier that far from the counter (squared, on the ground) is served.
        /// <paramref name="range"/> is the shop's own: the server's, or a client's shorter one.
        /// </summary>
        public static bool InReach(RunPhase phase, float distSq, float range) =>
            IsOpen(phase) && (ServesAnywhere(phase) || distSq <= range * range);

        /// <summary>How many specials a shift has.</summary>
        public static int SpecialSlots(Balance b) => Math.Max(0, b.GetInt("shopSpecialSlots"));

        /// <summary>What a special is cheaper by, in thousandths of its price.</summary>
        public static int DiscountPermille(Balance b) =>
            (int)Math.Round(Math.Clamp(b.GetDouble("shopSpecialDiscount"), 0.0, 1.0) * 1000.0);

        /// <summary>
        /// The price this shift. A fixed entry rolled into a special slot is cheaper; a rotating
        /// entry is only there as a special, and its price is the offer.
        /// </summary>
        public static int Price(ShopItemDef def, bool special, int discountPermille)
        {
            if (!special || def.Rotating) return def.Price;
            return Math.Max(1, (int)(((long)def.Price * (1000 - discountPermille) + 500) / 1000));
        }

        /// <summary>
        /// Whether the counter sells the entry at all right now, before the wallet and the pockets
        /// are looked at: BadRequest for a rotating entry that is not among the shift's specials.
        /// </summary>
        public static OpResult Check(ShopItemDef def, int shift, bool special, bool owned)
        {
            if ((def.Rotating && !special) || def.KitOnly) return OpResult.BadRequest;
            if (def.FromShift > shift) return OpResult.ShopLocked;
            if ((def.OncePerRun || def.OncePerShift) && owned) return OpResult.AlreadyOwned;
            return OpResult.Ok;
        }

        /// <summary>
        /// Doc 10 §3's services: the Extended Forecast needs a Delivery still to come (at the last shift's Payday there is
        /// none); the Emergency PO Repair a Post Office that is hurt. Ok for every other kind of entry.
        /// </summary>
        public static OpResult ServiceCheck(ShopItemDef def, RunPhase phase, int shift, int shifts, int poHp, int poMaxHp) => def.Service switch
        {
            ShopService.Forecast when phase == RunPhase.Payday && shift >= shifts => OpResult.WrongPhase,
            ShopService.PostOfficeRepair when poHp >= poMaxHp => OpResult.FullHealth,
            _ => OpResult.Ok,
        };

        /// <summary>
        /// A vehicle has one driver (doc 04 §3.1): the counter sells the team one of each kind for
        /// each carrier there is, no more (<paramref name="vehicles"/>: those of its kind). Ok for
        /// every other kind of entry.
        /// </summary>
        public static OpResult VehicleCheck(ShopItemDef def, int vehicles, int carriers) =>
            def.Kind == ShopKind.Vehicle && vehicles >= Math.Max(1, carriers) ? OpResult.OnePerCarrier : OpResult.Ok;

        /// <summary>What is used from the hand goes to the hotbar first; materials to the inventory.</summary>
        public static bool HotbarFirst(ItemDef item) => item.Category == "consumable" || item.Category == "tool";

        /// <summary>
        /// True if the whole purchase goes into these two containers: onto stacks of the same item
        /// in either, or as one new stack where there is a free spot for it.
        /// </summary>
        public static bool Fits(ContentDb content, GridContainer first, GridContainer second, ItemDef item, int count)
        {
            var stack = ItemStack.OfItem(item, count);
            var shape = StackShape.Of(content, stack);
            int room = 0;
            for (int i = 0; i < 2; i++)
            {
                var c = i == 0 ? first : second;
                if (!InventoryOps.Accepts(c, shape)) continue;
                if (c.FindFreeSpot(shape.W, shape.H, out _, out _)) return true;
                if (shape.W != shape.H && c.FindFreeSpot(shape.H, shape.W, out _, out _)) return true;
                if (shape.MaxStack <= 1) continue;
                foreach (var e in c.Entries)
                    if (e.Stack.CanStackWith(stack)) room += shape.MaxStack - e.Stack.Count;
            }
            return room >= count;
        }

        /// <summary>What the shop pays for <paramref name="count"/> units of an item; 0: it does not buy it.</summary>
        public static int SaleValue(ItemDef item, int count) => item.SellPrice <= 0 || count <= 0 ? 0 : item.SellPrice * count;
    }
}
