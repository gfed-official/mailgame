using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// The Post Office shop (doc 03 §4.4, doc 10 §3): in Prep and at Payday a carrier at the
    /// counter buys out of the team wallet and sells what they carry. What is bought once per
    /// run is the whole team's until the run ends; every shift two entries are on special.
    /// </summary>
    public sealed partial class SimWorld
    {
        private RngStream? _shopRng;
        private readonly List<ShopItemDef> _shopSpecials = new List<ShopItemDef>();
        private readonly List<ShopItemDef> _shopOwned = new List<ShopItemDef>();
        private readonly List<ShopItemDef> _shopCandidates = new List<ShopItemDef>();

        /// <summary>The shift's specials, in the order of ContentDb.ShopItems.</summary>
        public IReadOnlyList<ShopItemDef> ShopSpecials => _shopSpecials;

        /// <summary>What the team has bought of the entries sold once per run, in the order it was bought.</summary>
        public IReadOnlyList<ShopItemDef> ShopOwned => _shopOwned;

        /// <summary>Whether the team has bought the blueprint (doc 04 §1.3: once per run, for everybody).</summary>
        public bool OwnsBlueprint(string id)
        {
            foreach (var def in _shopOwned)
                if (def.Kind == ShopKind.Blueprint && def.Id == id) return true;
            return false;
        }

        /// <summary>What the entry costs this shift.</summary>
        public int ShopPrice(ShopItemDef def) =>
            ShopRules.Price(def, _shopSpecials.Contains(def), ShopRules.DiscountPermille(Content.Balance));

        /// <summary>A run starts and ends owning nothing, and draws its specials from the seed anew.</summary>
        private void ClearShop()
        {
            _shopSpecials.Clear();
            _shopOwned.Clear();
            _shopRng = new RngStream(Settings.Seed, "shop");
        }

        /// <summary>
        /// Doc 10 §3: the specials of the shift that begins, drawn from the entries tagged special
        /// the counter may sell by now. They hold for the shift's Prep and its Payday.
        /// </summary>
        private void RollShopSpecials()
        {
            var rng = _shopRng ??= new RngStream(Settings.Seed, "shop");
            _shopSpecials.Clear();
            _shopCandidates.Clear();
            foreach (var def in Content.ShopItems)
                if (def.Special && def.FromShift <= Run.Shift && !(def.OncePerRun && _shopOwned.Contains(def))) _shopCandidates.Add(def);
            int slots = Math.Min(ShopRules.SpecialSlots(Content.Balance), _shopCandidates.Count);
            for (int i = 0; i < slots; i++)
            {
                int n = rng.NextInt(_shopCandidates.Count);
                _shopSpecials.Add(_shopCandidates[n]);
                _shopCandidates.RemoveAt(n);
            }
            _shopSpecials.Sort((a, b) => a.Index.CompareTo(b.Index));

            var offer = new ShopOfferEvent { Shift = (byte)Run.Shift };
            foreach (var def in _shopSpecials) offer.Specials.Add(def.Index);
            Events.Add(offer);
        }

        /// <summary>The counter serves in Prep whoever stands at it, and at Payday the whole team where it is.</summary>
        private OpResult AtShop(Player player)
        {
            var po = Map.PostOffice;
            if (!po.HasShop) return OpResult.BadRequest;
            if (!ShopRules.IsOpen(Run.Phase)) return OpResult.WrongPhase;
            if (ShopRules.ServesAnywhere(Run.Phase)) return OpResult.Ok;
            if (!player.WasWithin(po.ShopX, po.ShopZ, _serverShopRange, _rewindTicks)) return OpResult.OutOfRange;
            return OpResult.Ok;
        }

        private OpResult BuyOp(Player player, in SimRequest r)
        {
            if (player.Dead) return OpResult.Dead;
            var at = AtShop(player);
            if (at != OpResult.Ok) return at;
            if (r.Count >= Content.ShopItems.Count) return OpResult.BadRequest;
            var def = Content.ShopItems[r.Count];
            bool special = _shopSpecials.Contains(def);
            var verdict = ShopRules.Check(def, Run.Shift, special, _shopOwned.Contains(def));
            if (verdict != OpResult.Ok) return verdict;
            int price = ShopRules.Price(def, special, ShopRules.DiscountPermille(Content.Balance));
            if (Economy.Wallet < price) return OpResult.CantAfford;

            if (def.Kind == ShopKind.Item)
            {
                // One purchase is one stack, and it is bought whole or not at all.
                var item = def.Item!;
                bool hotbarFirst = ShopRules.HotbarFirst(item);
                var first = hotbarFirst ? player.Hotbar : player.Inventory;
                var second = hotbarFirst ? player.Inventory : player.Hotbar;
                if (!ShopRules.Fits(Content, first, second, item, def.Count)) return OpResult.DoesNotFit;
                var stack = ItemStack.OfItem(item, def.Count);
                if (!InventoryOps.AutoPlace(Content, first, stack, _entryIds)) InventoryOps.AutoPlace(Content, second, stack, _entryIds);
                CommitContainer(first.Id);
                CommitContainer(second.Id);
            }
            Economy.Spend(price);
            if (def.OncePerRun) _shopOwned.Add(def);
            Events.Add(new ShopBoughtEvent { Player = player.Id, Item = def.Index, Price = (ushort)price });
            return OpResult.Ok;
        }

        /// <summary>Doc 03 §4.4: selling items at the shop, at the item's sell price; the money is the team's.</summary>
        private OpResult SellOp(Player player, in SimRequest r)
        {
            if (player.Dead) return OpResult.Dead;
            var at = AtShop(player);
            if (at != OpResult.Ok) return at;
            if (!_containers.TryGetValue(r.Container, out var info)) return OpResult.UnknownContainer;
            if (info.Owner != player.Id) return OpResult.NotAllowed;
            var entry = info.Container.Get(r.EntryId);
            if (entry == null) return OpResult.UnknownEntry;
            if (entry.Stack.Kind != StackKind.Item) return OpResult.NotSellable;
            var item = Content.Items[entry.Stack.DefIndex];
            int count = r.Count == 0 ? entry.Stack.Count : r.Count;
            if (count > entry.Stack.Count) return OpResult.BadRequest;
            int paid = ShopRules.SaleValue(item, count);
            if (paid <= 0) return OpResult.NotSellable;

            TakeUnits(info.Container, entry, count);
            Economy.AddSale(paid);
            Events.Add(new ShopSoldEvent { Player = player.Id, Item = item.Index, Count = (ushort)count, Paid = paid });
            return OpResult.Ok;
        }

        /// <summary>
        /// A carrier uses one of what they carry on themselves (doc 05 §1.1): a Bandage gives its
        /// HP at once, Berries over a few seconds.
        /// </summary>
        private OpResult UseOp(Player player, in SimRequest r)
        {
            if (player.Dead) return OpResult.Dead;
            if (!_containers.TryGetValue(r.Container, out var info)) return OpResult.UnknownContainer;
            if (info.Owner != player.Id) return OpResult.NotAllowed;
            var entry = info.Container.Get(r.EntryId);
            if (entry == null) return OpResult.UnknownEntry;
            if (entry.Stack.Kind != StackKind.Item) return OpResult.NotUsable;
            var item = Content.Items[entry.Stack.DefIndex];
            if (item.Heal <= 0) return OpResult.NotUsable;
            if (player.Hp >= player.MaxHp) return OpResult.FullHealth;

            TakeUnits(info.Container, entry, 1);
            uint ticks = SimConstants.SecondsToTicks(item.HealSeconds);
            if (ticks <= 1) player.Hp = MathF.Min(player.MaxHp, player.Hp + item.Heal);
            else
            {
                player.HealPool += item.Heal;
                player.HealPerTick = MathF.Max(player.HealPerTick, item.Heal / (float)ticks);
            }
            return OpResult.Ok;
        }

        private void TakeUnits(GridContainer container, GridEntry entry, int count)
        {
            entry.Stack.ItemCount -= count;
            if (entry.Stack.Count <= 0) container.Remove(entry.EntryId);
            else container.MarkDirty(entry.EntryId);
            CommitContainer(container.Id);
        }

        /// <summary>One tick of what a carrier ate: it heals whether or not they are being hit.</summary>
        private static void StepHealing(Player p)
        {
            if (p.HealPool <= 0f) return;
            float amount = MathF.Min(p.HealPerTick, p.HealPool);
            p.HealPool -= amount;
            p.Hp = MathF.Min(p.MaxHp, p.Hp + amount);
            if (p.HealPool <= 0.0001f) StopHealing(p);
        }

        private static void StopHealing(Player p)
        {
            p.HealPool = 0f;
            p.HealPerTick = 0f;
        }
    }
}
