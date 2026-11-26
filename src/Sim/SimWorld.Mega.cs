using System;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Inventory;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// A Mega's drops (doc 05 §2.1): "Guaranteed drop on death: a Lost Parcel (Medium Package with a
    /// random unlocked address, value ×3) plus 2 Iron Ingots"; design choice 142.
    /// </summary>
    public sealed partial class SimWorld
    {
        private void AddMegaItems(LootBundle bundle)
        {
            foreach (var drop in Content.Waves.MegaItems)
            {
                int at = bundle.Items.FindIndex(i => i.Item == drop.Item.Index);
                if (at < 0) bundle.Items.Add(new LootItem { Item = drop.Item.Index, Count = (byte)Math.Min(255, drop.Count) });
                else bundle.Items[at] = new LootItem { Item = drop.Item.Index, Count = (byte)Math.Min(255, bundle.Items[at].Count + drop.Count) };
            }
        }

        /// <summary>
        /// The Lost Parcel lies where the Mega fell, in a bag that is nobody's: whoever takes it
        /// delivers it like any mail; left lying, it goes back to the Intake with the bag.
        /// </summary>
        private void DropLostParcel(Enemy e)
        {
            var waves = Content.Waves;
            if (waves.LostParcelKind == null) return;
            var parcel = Spawner.LostParcel(waves.LostParcelKind, Math.Max(1, Run.Shift), waves.LostParcelValueMult, _lootRng);
            if (parcel == null) return;
            var item = parcel.Value;
            uint bag = DropAsBag(ItemStack.OfMail(item), e.X, e.Z);
            if (bag == 0) return;
            Events.Add(new LostParcelEvent { Enemy = e.Def.Index, Kind = item.Kind, Address = item.Address, Value = item.Value, Bag = bag });
        }
    }
}
