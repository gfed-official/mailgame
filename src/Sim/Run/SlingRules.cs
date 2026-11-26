using System;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;

namespace PerformativeMail.Sim.Run
{
    /// <summary>
    /// The Slingshot (doc 05 §1.2 "12 | 0.8 s | 25 m | Shop 200 ¢; uses Stone as ammo (1 Stone = 5
    /// shots)"; design choice 143): what a carrier holding it can loose, as the server decides and a
    /// client words its line by.
    /// </summary>
    public static class SlingRules
    {
        /// <summary>How far beside the line of the shot a raider's body may be and still be struck, m.</summary>
        public const float Leeway = 0.3f;

        /// <summary>The tool in the hand if it shoots; null otherwise.</summary>
        public static ItemDef? Held(ContentDb content, GridContainer hotbar, int slot) =>
            HarvestRules.Held(content, hotbar, slot) is { Shoots: true, Ammo: not null } held ? held : null;

        /// <summary>Units of <paramref name="ammo"/> in the hotbar and the inventory together.</summary>
        public static int Ammo(ItemDef ammo, GridContainer hotbar, GridContainer inventory) => Count(hotbar, ammo) + Count(inventory, ammo);

        /// <summary>The shots there are: those still in the pouch, and five for every Stone carried.</summary>
        public static int Shots(ItemDef sling, int loaded, GridContainer hotbar, GridContainer inventory) =>
            loaded + sling.ShotsPerAmmo * Ammo(sling.Ammo!, hotbar, inventory);

        /// <summary>
        /// Where along a shot from (<paramref name="x"/>, <paramref name="z"/>) heading (<paramref name="fx"/>,
        /// <paramref name="fz"/>) a body of <paramref name="radius"/> at (<paramref name="ex"/>, <paramref name="ez"/>)
        /// is struck, in metres from the carrier; a negative number when the shot passes it by or falls short.
        /// </summary>
        public static float Along(float x, float z, float fx, float fz, float range, float ex, float ez, float radius)
        {
            float dx = ex - x, dz = ez - z;
            float t = dx * fx + dz * fz;
            if (t < 0f || t > range + radius) return -1f;
            float side = MathF.Abs(dx * fz - dz * fx);
            return side <= radius + Leeway ? t : -1f;
        }

        private static int Count(GridContainer container, ItemDef item)
        {
            int n = 0;
            foreach (var e in container.Entries)
                if (e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == item.Index) n += e.Stack.ItemCount;
            return n;
        }
    }
}
