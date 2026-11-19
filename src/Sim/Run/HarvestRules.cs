using System;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;

namespace PerformativeMail.Sim.Run
{
    /// <summary>
    /// The rules of harvesting (doc 02 §3.9) that the server decides by and a client words its
    /// prompt by: when a node may be hit, what a hit gives the hand that lands it, and whether
    /// that goes into the pockets.
    /// </summary>
    public static class HarvestRules
    {
        /// <summary>Doc 01 §1 phase table: harvesting in Prep and in Delivery, the raid included.</summary>
        public static bool IsOpen(RunPhase phase) => phase == RunPhase.Prep || phase == RunPhase.Delivery;

        /// <summary>The item in a hotbar slot; null for bare hands, an empty slot and mail.</summary>
        public static ItemDef? Held(ContentDb content, GridContainer hotbar, int slot)
        {
            var entry = slot > 0 && slot < hotbar.Cols ? hotbar.EntryAt(slot, 0) : null;
            return entry != null && entry.Stack.Kind == StackKind.Item ? content.Items[entry.Stack.DefIndex] : null;
        }

        /// <summary>Units one hit gives, never more than a stack; 0 where the node asks for a tool and gets none.</summary>
        public static int Yield(NodeDef node, ItemDef? held) =>
            Math.Min(node.YieldWith(held), Math.Min(node.Item.MaxStack, byte.MaxValue));

        /// <summary>What a hit goes into first: what is used from the hand to the hotbar, materials to the inventory.</summary>
        public static bool HotbarFirst(NodeDef node) => ShopRules.HotbarFirst(node.Item);

        /// <summary>
        /// Whether a swing at the node lands, phase and distance aside. A hit that does not fit
        /// into the pockets is not spent.
        /// </summary>
        public static OpResult Check(ContentDb content, NodeDef node, int hitsLeft, ItemDef? held, GridContainer hotbar, GridContainer inventory)
        {
            if (hitsLeft <= 0) return OpResult.Depleted;
            int count = Yield(node, held);
            if (count <= 0) return OpResult.NeedsTool;
            bool hotbarFirst = HotbarFirst(node);
            return ShopRules.Fits(content, hotbarFirst ? hotbar : inventory, hotbarFirst ? inventory : hotbar, node.Item, count)
                ? OpResult.Ok : OpResult.DoesNotFit;
        }

        /// <summary>Seconds between two swings at the node with <paramref name="held"/> in the hand (doc 03 §3.4).</summary>
        public static double SwingSeconds(Balance b, NodeDef node, ItemDef? held) => node.SwingSeconds(held, b.GetDouble("harvestSwingSeconds"));
    }
}
