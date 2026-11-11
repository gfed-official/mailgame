using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim
{
    public sealed partial class SimWorld
    {
        /// <summary>Doc 06 §7: 60 action RPCs per second per client.</summary>
        private const float RequestsPerTick = 2f;
        private const float RequestBurst = 60f;

        private readonly List<GridContainer> _quickTargets = new List<GridContainer>(2);

        private void ApplyRequests()
        {
            foreach (var p in _players)
                p.RequestCredit = Math.Min(p.RequestCredit + RequestsPerTick, RequestBurst);
            if (_requests.Count == 0) return;

            // Arrival order across all players (doc 03 §3.3): first valid op wins a contested cell.
            for (int i = 0; i < _requests.Count; i++)
            {
                var pending = _requests[i];
                var player = GetPlayer(pending.Player);
                if (player == null) continue;
                OpResult result;
                if (player.RequestCredit < 1f) result = OpResult.RateLimited;
                else
                {
                    player.RequestCredit -= 1f;
                    result = Apply(player, pending.Request);
                }
                Events.Add(new RequestResultEvent { Audience = player.Id, RequestId = pending.Request.RequestId, Result = result });
            }
            _requests.Clear();
        }

        private OpResult Apply(Player player, in SimRequest r)
        {
            switch (r.Kind)
            {
                case RequestKind.OpenContainer: return OpenContainer(player, r.Container);
                case RequestKind.CloseContainer:
                    if (player.OpenContainer == 0 || player.OpenContainer != r.Container) return OpResult.NotOpen;
                    CloseExternal(player, notify: false);
                    return OpResult.Ok;
                case RequestKind.Move: return MoveOp(player, r);
                case RequestKind.QuickMove: return QuickMoveOp(player, r);
                case RequestKind.Sort: return SortOp(player, r);
                case RequestKind.Deliver: return DeliverOp(player, r);
                default: return OpResult.BadRequest;
            }
        }

        private OpResult OpenContainer(Player player, uint containerId)
        {
            if (!_containers.TryGetValue(containerId, out var info)) return OpResult.UnknownContainer;
            if (!info.Owner.IsNone) return info.Owner == player.Id ? OpResult.Ok : OpResult.NotAllowed;
            if (!InRange(player, info)) return OpResult.OutOfRange;
            if (player.OpenContainer == containerId) return OpResult.Ok;
            CloseExternal(player, notify: true);
            player.OpenContainer = containerId;
            info.Viewers.Add(player.Id);
            Events.Add(new ContainerStateEvent { Audience = player.Id, State = info.Container.Snapshot() });
            return OpResult.Ok;
        }

        /// <summary>
        /// Ownership and range validation (doc 06 §7): a player may only touch their body containers
        /// and the one external container they have open, and only within 3 m of it.
        /// </summary>
        private OpResult Access(Player player, uint containerId, out GridContainer container)
        {
            container = null!;
            if (!_containers.TryGetValue(containerId, out var info)) return OpResult.UnknownContainer;
            container = info.Container;
            if (info.Owner == player.Id) return OpResult.Ok;
            if (!info.Owner.IsNone || player.OpenContainer != containerId) return OpResult.NotOpen;
            return InRange(player, info) ? OpResult.Ok : OpResult.OutOfRange;
        }

        private OpResult MoveOp(Player player, in SimRequest r)
        {
            var access = Access(player, r.Container, out var from);
            if (access != OpResult.Ok) return access;
            access = Access(player, r.ToContainer, out var to);
            if (access != OpResult.Ok) return access;
            var result = InventoryOps.Move(Content, from, r.EntryId, to, r.X, r.Y, r.Rotated, r.Count, _entryIds);
            if (result != OpResult.Ok) return result;
            CommitContainer(from.Id);
            if (!ReferenceEquals(from, to)) CommitContainer(to.Id);
            return OpResult.Ok;
        }

        private OpResult QuickMoveOp(Player player, in SimRequest r)
        {
            var access = Access(player, r.Container, out var from);
            if (access != OpResult.Ok) return access;

            // "To the other panel": body → the open external container, or between hotbar and
            // inventory when nothing is open; external → hotbar first, then inventory.
            _quickTargets.Clear();
            if (player.Owns(from))
            {
                if (player.OpenContainer != 0)
                {
                    access = Access(player, player.OpenContainer, out var external);
                    if (access != OpResult.Ok) return access;
                    _quickTargets.Add(external);
                }
                else _quickTargets.Add(ReferenceEquals(from, player.Hotbar) ? player.Inventory : player.Hotbar);
            }
            else
            {
                _quickTargets.Add(player.Hotbar);
                _quickTargets.Add(player.Inventory);
            }

            var result = InventoryOps.QuickMove(Content, from, r.EntryId, _quickTargets);
            if (result != OpResult.Ok) return result;
            CommitContainer(from.Id);
            foreach (var t in _quickTargets) CommitContainer(t.Id);
            return OpResult.Ok;
        }

        private OpResult SortOp(Player player, in SimRequest r)
        {
            var access = Access(player, r.Container, out var container);
            if (access != OpResult.Ok) return access;
            if (r.SortKey != SortKey.Address && r.SortKey != SortKey.Size) return OpResult.BadRequest;
            var result = InventoryOps.Sort(Content, container, r.SortKey);
            if (result != OpResult.Ok)
            {
                container.ClearChanges();
                return result;
            }
            CommitContainer(container.Id);
            return OpResult.Ok;
        }

        /// <summary>
        /// Manual mailbox insert of one stack (doc 03 §2.2, §3.4; doc 06 §4.3). The stack goes in
        /// item by item; each item is judged by <see cref="DeliveryRules"/>.
        /// </summary>
        private OpResult DeliverOp(Player player, in SimRequest r)
        {
            var dest = Map.GetDestination(r.DestId);
            if (dest == null) return OpResult.UnknownDestination;
            if (!_containers.TryGetValue(r.Container, out var info)) return OpResult.UnknownContainer;
            if (info.Owner != player.Id) return OpResult.NotOpen;
            var entry = info.Container.Get(r.EntryId);
            if (entry == null) return OpResult.UnknownEntry;
            if (entry.Stack.Kind != StackKind.Mail) return OpResult.NotMail;
            if (!player.WasWithin(dest.MailboxX, dest.MailboxZ, _serverInteractRange, _rewindTicks)) return OpResult.OutOfRange;
            if (player.HasDelivered && Tick - player.LastDeliverTick < _minDeliverIntervalTicks) return OpResult.TooFast;

            var kind = Content.MailKinds[entry.Stack.DefIndex];
            if (!DeliveryRules.KindAccepted(kind, dest.Type)) return OpResult.WontFitHere;

            var mail = entry.Stack.Mail;
            int consumed = 0;
            while (mail.Count > 0)
            {
                var item = mail[mail.Count - 1];
                var verdict = DeliveryRules.Evaluate(item, kind, dest, Run.Shift, Economy.Wallet, Content);
                if (!verdict.Consumed) break;
                mail.RemoveAt(mail.Count - 1);
                consumed++;
                Economy.Apply(verdict);
                if (verdict.Outcome == DeliveryOutcome.Delivered)
                {
                    player.Deliveries++;
                    player.Earned += verdict.Amount;
                    Events.Add(new DeliveredEvent
                    {
                        MailId = item.Id, DestId = dest.Id, Paid = (ushort)verdict.Amount, Player = player.Id, Kind = item.Kind,
                    });
                }
                else
                {
                    player.Misdeliveries++;
                    Events.Add(new MisdeliveredEvent
                    {
                        MailId = item.Id, DestId = dest.Id, Penalty = (ushort)verdict.Amount, Player = player.Id, Kind = item.Kind,
                    });
                }
            }
            if (consumed == 0) return OpResult.WalletFloor;

            if (mail.Count == 0) info.Container.Remove(entry.EntryId);
            else info.Container.MarkDirty(entry.EntryId);
            CommitContainer(info.Container.Id);
            player.LastDeliverTick = Tick;
            player.HasDelivered = true;
            return OpResult.Ok;
        }
    }
}
