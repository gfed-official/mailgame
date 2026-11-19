using System;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Harvesting (doc 02 §3.9): in Prep and in Delivery a carrier beside a tree, a bush or a
    /// boulder swings at it, and every swing that lands puts the node's yield into their pockets.
    /// A bush is full again when the next shift begins; a tree and a boulder are gone for the run.
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>
        /// A felled tree and a broken boulder still stand in the way (the stump, the rubble):
        /// what is solid and where the raiders walk does not change with the nodes.
        /// </summary>
        private OpResult HarvestOp(Player player, in SimRequest r)
        {
            if (player.Dead) return OpResult.Dead;
            if (!HarvestRules.IsOpen(Run.Phase)) return OpResult.WrongPhase;
            int index = r.DestId;
            var node = Nodes.Def(index);
            if (node == null) return OpResult.BadRequest;
            var prop = Map.Props[index];
            if (!player.WasWithin(prop.X, prop.Z, _serverInteractRange + prop.Radius, _rewindTicks)) return OpResult.OutOfRange;
            var held = HarvestRules.Held(Content, player.Hotbar, r.X);
            var verdict = HarvestRules.Check(Content, node, Nodes.HitsLeft(index), held, player.Hotbar, player.Inventory);
            if (verdict != OpResult.Ok) return verdict;
            if (player.HasHarvested && Tick - player.LastHarvestTick < MinHarvestIntervalTicks(node, held)) return OpResult.TooFast;

            int count = HarvestRules.Yield(node, held);
            bool hotbarFirst = HarvestRules.HotbarFirst(node);
            var first = hotbarFirst ? player.Hotbar : player.Inventory;
            var second = hotbarFirst ? player.Inventory : player.Hotbar;
            var stack = ItemStack.OfItem(node.Item, count);
            if (!InventoryOps.AutoPlace(Content, first, stack, _entryIds)) InventoryOps.AutoPlace(Content, second, stack, _entryIds);
            CommitContainer(first.Id);
            CommitContainer(second.Id);

            Nodes.Set(index, Nodes.HitsLeft(index) - 1);
            player.LastHarvestTick = Tick;
            player.HasHarvested = true;
            Events.Add(new NodeHarvestedEvent
            {
                Player = player.Id, Node = (ushort)index, HitsLeft = (byte)Nodes.HitsLeft(index), Item = node.Item.Index, Count = (byte)count,
            });
            return OpResult.Ok;
        }

        /// <summary>
        /// The swing is timed on the client, as the hold at a mailbox is; the server only refuses
        /// swings that come implausibly fast: the swing time minus 0.1 s of network slack.
        /// </summary>
        private uint MinHarvestIntervalTicks(NodeDef node, ItemDef? held) =>
            SimConstants.SecondsToTicks(Math.Max(0.0, HarvestRules.SwingSeconds(Content.Balance, node, held) - 0.1));

        /// <summary>Whole again: what regrows when a shift begins, everything when a run begins or ends.</summary>
        private void RegrowNodes(bool all)
        {
            if (Nodes.Regrow(all)) Events.Add(new NodesRegrownEvent { All = all });
        }
    }
}
