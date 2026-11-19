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
            if (def.Rotating && !special) return OpResult.BadRequest;
            if (def.FromShift > shift) return OpResult.ShopLocked;
            if (def.OncePerRun && owned) return OpResult.AlreadyOwned;
            return OpResult.Ok;
        }

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
