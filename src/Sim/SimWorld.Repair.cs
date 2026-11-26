using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// The Repair Hammer (doc 05 §4 "Repairs 50 HP/s to a construct; consumes materials at 25% of
    /// build cost per full HP bar"; §5 "the PO … can be repaired with the hammer"; design choice 139).
    /// A carrier with it in the hand holding attack at a hurt construct — or at the Post Office's wall
    /// — mends it <see cref="RepairRules.Chunk"/> HP at a time, paying for each out of their pockets:
    /// a unit of an input is taken when what was paid runs short, and what it did not use is kept for
    /// the next. With nothing hurt before them the hammer swings as the hammer does.
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>HP mended so far, all carriers together, constructs and the Post Office alike.</summary>
        public int HpRepaired { get; private set; }

        /// <summary>
        /// Attack held with a mending tool in the hand: true if there was something hurt to mend before
        /// them (whether or not the pockets could pay for it), so the hammer is not also swung.
        /// </summary>
        private bool StepRepair(Player p)
        {
            if (!RepairRules.IsOpen(Run.Phase)) return false;
            var hammer = RepairRules.Hammer(Content, p.Hotbar, p.HeldSlot);
            if (hammer == null) return false;
            bool poHurt = PostOfficeStands && PostOfficeHp < PostOfficeMaxHp;
            var target = RepairRules.Target(Map, Constructs, p.Move.X, p.Move.Z, PlayerInput.YawToRadians(p.Move.Yaw), poHurt, out bool postOffice);
            if (target == null && !postOffice) { p.RepairOwed = 0f; return false; }
            if (target != p.RepairTarget || postOffice != p.RepairingPostOffice) p.RepairOwed = 0f;
            p.RepairTarget = target;
            p.RepairingPostOffice = postOffice;
            // Others see the hammer go up and down at it, as often as it swings.
            if (Tick >= p.HammerReadyTick)
            {
                p.HammerReadyTick = Tick + _combat.HammerCooldownTicks;
                Events.Add(new AttackEvent { Player = p.Id, Weapon = Weapon.Hammer, X = p.Move.X, Z = p.Move.Z, Yaw = p.Move.Yaw });
            }
            p.RepairOwed += hammer.RepairPerSecond * SimConstants.TickDt;
            if (p.RepairOwed < RepairRules.Chunk) return true;
            p.RepairOwed -= RepairRules.Chunk;
            if (postOffice) MendPostOffice(p, hammer);
            else MendConstruct(p, hammer, target!);
            return true;
        }

        private void MendConstruct(Player p, ItemDef hammer, Construct c)
        {
            int amount = Math.Min(RepairRules.Chunk, c.Def.Hp - c.Hp);
            if (amount <= 0) return;
            if (!Unlimited && c.Def.Recipe != null && hammer.RepairCostRatio > 0f)
            {
                var inputs = c.Def.Recipe.Inputs;
                foreach (var input in inputs)
                    if (!CanPay(p, input.Item, RepairRules.PerFullBar(input, hammer) * amount / c.Def.Hp)) return;
                foreach (var input in inputs) Pay(p, input.Item, RepairRules.PerFullBar(input, hammer) * amount / c.Def.Hp);
            }
            int hp = c.Hp + amount;
            Constructs.SetHp(c, hp);
            HpRepaired += amount;
            Events.Add(new ConstructDamagedEvent { X = (byte)c.Tx, Y = (byte)c.Tz, Hp = (ushort)hp });
            if (hp >= c.Def.Hp) Events.Add(new RepairedEvent { X = (byte)c.Tx, Y = (byte)c.Tz, Player = p.Id });
        }

        private void MendPostOffice(Player p, ItemDef hammer)
        {
            float max = PostOfficeMaxHp;
            float amount = MathF.Min(RepairRules.Chunk, max - PostOfficeHp);
            if (amount <= 0f) return;
            float logs = Content.Balance.GetFloat("postOfficeRepairLogs") * hammer.RepairCostRatio * amount / max;
            if (!Unlimited && logs > 0f)
            {
                var log = Content.Item(RepairRules.PostOfficeItem);
                if (!CanPay(p, log, logs)) return;
                Pay(p, log, logs);
            }
            PostOfficeHp = MathF.Min(max, PostOfficeHp + amount);
            HpRepaired += (int)amount;
            if (PostOfficeHp >= max) Events.Add(new RepairedEvent { PostOffice = true, Player = p.Id });
        }

        /// <summary>Whether what was paid before, and a unit more out of the pockets if need be, covers <paramref name="part"/> of a unit.</summary>
        private static bool CanPay(Player p, ItemDef item, float part) =>
            p.RepairPaid.TryGetValue(item.Index, out float paid) && paid >= part - 1e-4f || BuildRules.Have(p.Hotbar, p.Inventory, item) > 0;

        private void Pay(Player p, ItemDef item, float part)
        {
            p.RepairPaid.TryGetValue(item.Index, out float paid);
            while (paid < part - 1e-4f)
            {
                TakeMaterials(p, item, 1);
                CommitContainer(p.Inventory.Id);
                CommitContainer(p.Hotbar.Id);
                paid += 1f;
            }
            p.RepairPaid[item.Index] = MathF.Max(0f, paid - part);
        }
    }
}
