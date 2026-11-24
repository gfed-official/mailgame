using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Inserters (doc 04 §2.5) in the tick, after the belts: each, when its rate lets it, takes one
    /// piece of mail from what stands behind it and puts it into what stands in front (a Long
    /// Inserter over the tile between, two off each way). It only
    /// takes what the front will take (and its kind filter lets through), so nothing is ever held
    /// in the arm and nothing is ever dropped.
    /// </summary>
    public sealed partial class SimWorld
    {
        private readonly Dictionary<Construct, uint> _inserterNext = new Dictionary<Construct, uint>();
        private readonly List<Construct> _inserterGone = new List<Construct>();

        /// <summary>What an inserter (or a pipe outlet) puts into, found anew each time it may move a piece.</summary>
        private struct InserterTarget
        {
            public DestinationDef? Mailbox;
            public GridContainer? Container;
            public SorterNode? Sorter;
            public BeltSegment? Belt;
            public InletNode? Inlet;
            public bool Any => Mailbox != null || Container != null || Sorter != null || Belt != null || Inlet != null;
        }

        private void StepInserters()
        {
            _inserterGone.Clear();
            foreach (var c in _inserterNext.Keys)
                if (Constructs.At(c.Tx, c.Tz) != c) _inserterGone.Add(c);
            foreach (var c in _inserterGone) _inserterNext.Remove(c);
            // Like building, in Prep and in Delivery (mailboxes take mail in Delivery only).
            if (!BuildRules.IsOpen(Run.Phase)) return;
            foreach (var c in Constructs.All)
            {
                if (c.Def.Behaviour != BuildingBehaviour.Inserter) continue;
                if (_inserterNext.TryGetValue(c, out uint next) && Tick < next) continue;
                if (!MoveOne(c, out ushort kind)) continue;
                _inserterNext[c] = Tick + Math.Max(1u, SimConstants.SecondsToTicks(1.0 / c.Def.Rate));
                Events.Add(new InserterSwungEvent { X = (byte)c.Tx, Y = (byte)c.Tz, Kind = kind });
            }
        }

        private bool MoveOne(Construct c, out ushort kind)
        {
            kind = 0;
            var target = TargetOf(c);
            if (!target.Any) return false;
            var (dx, dz) = BeltNetwork.Dirs[c.Facing];
            int bx = c.Tx - dx * c.Def.Reach, bz = c.Tz - dz * c.Def.Reach;
            var filter = c.Filter(0, 0);

            // The Intake (doc 04 §2.6: inserters pull from it too): the oldest piece the front takes.
            if (Map.InBounds(bx, bz) && bz * Map.Cols + bx == Belts.IntakeTile)
                return TakeFromContainer(Intake, c, filter, target, oldestFirst: true, out kind);
            // A chest or a depot behind it.
            var behind = Constructs.At(bx, bz);
            if (behind != null && behind.ContainerId != 0 && GetContainer(behind.ContainerId) is { } box)
                return TakeFromContainer(box, c, filter, target, oldestFirst: false, out kind);
            // The end of a belt running into it.
            var seg = Belts.EndingAt(c.Tx, c.Tz, c.Facing, c.Def.Reach);
            if (seg == null || !BeltNetwork.FrontWaiting(seg, out var item)) return false;
            if (!Passes(filter, item.Mail) || !Takes(target, item.Mail)) return false;
            Belts.TakeFront(seg);
            Events.Add(new BeltItemRemovedEvent { MailId = item.Mail.Id });
            Put(target, item.Mail, DeliveryMachine.Inserter);
            kind = item.Mail.Kind;
            return true;
        }

        private static bool Passes(MailFilter filter, in MailItem mail) => filter.IsEmpty || filter.Matches(mail);

        private InserterTarget TargetOf(Construct c)
        {
            var (dx, dz) = BeltNetwork.Dirs[c.Facing];
            int fx = c.Tx + dx * c.Def.Reach, fz = c.Tz + dz * c.Def.Reach;
            var t = new InserterTarget();
            if (!Map.InBounds(fx, fz)) return t;
            int mailbox = Belts.MailboxOn(fz * Map.Cols + fx);
            if (mailbox >= 0)
            {
                t.Mailbox = Map.GetDestination(mailbox);
                return t;
            }
            var front = Constructs.At(fx, fz);
            if (front != null && front.ContainerId != 0)
            {
                t.Container = GetContainer(front.ContainerId);
                return t;
            }
            // A pipe inlet takes it from behind (doc 04 §2.2).
            if (front != null && front.Def.PipeRole == PipeRole.Inlet && front.Facing == c.Facing)
            {
                t.Inlet = Pipes.InletOf(front);
                return t;
            }
            // A sorter takes it at its input: into the tile it was built on, from behind.
            if (front != null && front.Def.Behaviour == BuildingBehaviour.Sorter && front.Tx == fx && front.Tz == fz && front.Facing == c.Facing)
            {
                t.Sorter = Belts.SorterOf(front);
                return t;
            }
            t.Belt = Belts.FedBy(c.Tx, c.Tz, c.Facing, c.Def.Reach);
            // Nothing built there: a parked truck's rear (doc 04 §3.1 "a belt end or inserter facing it").
            if (t.Belt == null && front == null && _vehicles.Count > 0 && LoadingFaceOn(fx, fz) is { } truck) t.Container = truck.Cargo;
            return t;
        }

        private bool Takes(in InserterTarget t, in MailItem mail)
        {
            if (t.Mailbox != null) return AutomatedDelivers(t.Mailbox, mail);
            if (t.Container != null) return InventoryOps.HasRoomFor(Content, t.Container, ItemStack.OfMail(mail));
            if (t.Sorter != null) return t.Sorter.Buffer.Count < t.Sorter.Built.Def.BufferSize;
            if (t.Inlet != null) return TryLaunch(t.Inlet, mail, dryRun: true);
            return t.Belt != null && BeltNetwork.PickLane(t.Belt, Content.MailKinds[mail.Kind], out _);
        }

        private void Put(in InserterTarget t, in MailItem mail, DeliveryMachine machine)
        {
            if (t.Mailbox != null) DeliverAutomated(t.Mailbox, mail, machine);
            else if (t.Container != null)
            {
                InventoryOps.AutoPlace(Content, t.Container, ItemStack.OfMail(mail), _entryIds);
                CommitContainer(t.Container.Id);
            }
            else if (t.Sorter != null) t.Sorter.Buffer.Add(mail);
            else if (t.Belt != null) PutOnBelt(t.Belt, mail);
            else if (t.Inlet != null) TryLaunch(t.Inlet, mail, dryRun: false);
        }

        /// <summary>One piece out of a container: the first (or, from the Intake, the oldest) its filter and the front take.</summary>
        private bool TakeFromContainer(GridContainer from, Construct c, MailFilter filter, in InserterTarget target, bool oldestFirst, out ushort kind)
        {
            kind = 0;
            GridEntry? pick = null;
            int index = -1;
            foreach (var e in from.Entries)
            {
                if (e.Stack.Kind != StackKind.Mail) continue;
                var list = e.Stack.Mail;
                for (int i = 0; i < list.Count; i++)
                {
                    if (pick != null && (!oldestFirst || list[i].Id >= pick.Stack.Mail[index].Id)) continue;
                    if (!Passes(filter, list[i]) || !Takes(target, list[i])) continue;
                    pick = e;
                    index = i;
                    if (!oldestFirst) break;
                }
                if (pick != null && !oldestFirst) break;
            }
            if (pick == null) return false;
            var mail = pick.Stack.Mail[index];
            pick.Stack.Mail.RemoveAt(index);
            if (pick.Stack.Mail.Count == 0) from.Remove(pick.EntryId);
            else from.MarkDirty(pick.EntryId);
            CommitContainer(from.Id);
            Put(target, mail, DeliveryMachine.Inserter);
            kind = mail.Kind;
            return true;
        }
    }
}
