using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Conveyor belts (doc 04 §2.1) in the tick, after the fight and before the mail spawns (doc
    /// 06 §3): the network is compiled again when a belt is built or taken down, the items move,
    /// and what reaches a mailbox goes in by the rules a carrier's hand goes by. The clients hear
    /// of it as doc 06 §4 has it: whole segments when they change, each item that comes on, moves
    /// between segments or goes off, and a checksum per segment every 2 s.
    /// </summary>
    public sealed partial class SimWorld : IBeltSink
    {
        /// <summary>Doc 06 §4: "every 2 s per visible segment".</summary>
        public const int BeltChecksumTicks = 60;

        private BeltNetwork? _belts;
        private readonly List<BeltSegment> _beltChanged = new List<BeltSegment>();
        private readonly List<BeltItem> _beltOrphans = new List<BeltItem>();

        /// <summary>Every belt of the map, compiled (made on first use, again with a new map).</summary>
        public BeltNetwork Belts
        {
            get
            {
                if (_belts == null || !ReferenceEquals(_beltsField, Constructs)) NewBelts();
                return _belts!;
            }
        }

        private ConstructField? _beltsField;

        private void NewBelts()
        {
            _belts = new BeltNetwork(Content, Map, Constructs);
            _beltsField = Constructs;
        }

        private void StepBelts()
        {
            var belts = Belts;
            CompileBelts();
            if (belts.Segments.Count == 0) return;
            StepOutfeed();
            belts.SorterThroughputMult = TeamStat(Stat.SorterThroughput, 1f);
            belts.Step(SimConstants.TickDt, this, Tick, TeamStat(Stat.BeltSpeed, 1f));
            for (int i = 0; i < belts.Segments.Count; i++)
            {
                if ((Tick + (uint)i) % BeltChecksumTicks != 0) continue;
                var s = belts.Segments[i];
                Events.Add(new BeltChecksumEvent { X = HeadX(s), Y = HeadZ(s), Count = (ushort)s.Items.Count, Hash = BeltNetwork.Checksum(s) });
            }
        }

        /// <summary>After building: the segments as they are now, what fell off back to the Intake, the changed ones sent whole.</summary>
        private void CompileBelts()
        {
            _beltChanged.Clear();
            _beltOrphans.Clear();
            if (!Belts.Compile(_beltChanged, _beltOrphans)) return;
            foreach (var it in _beltOrphans)
            {
                Spawner.ReturnToIntake(it.Mail);
                Events.Add(new BeltItemRemovedEvent { MailId = it.Mail.Id });
            }
            if (_beltOrphans.Count > 0) CommitContainer(Intake.Id);
            foreach (var s in _beltChanged) Events.Add(BeltState(s, EntityId.None));
        }

        private byte HeadX(BeltSegment s) => (byte)(s.Head % Map.Cols);
        private byte HeadZ(BeltSegment s) => (byte)(s.Head / Map.Cols);

        private BeltStateEvent BeltState(BeltSegment s, EntityId audience)
        {
            var ev = new BeltStateEvent { Audience = audience, X = HeadX(s), Y = HeadZ(s), Tick = Tick };
            foreach (var it in s.Items) ev.Items.Add(Info(it));
            return ev;
        }

        public static BeltItemInfo Info(in BeltItem it) => new BeltItemInfo
        {
            MailId = it.Mail.Id, Kind = it.Mail.Kind, AddressKey = it.Mail.Address.Key, Unit = it.Mail.Address.Unit, Lane = it.Lane, Pos = it.Pos,
        };

        /// <summary>Who joins (or comes back) gets every segment whole after the Welcome (doc 06 §4: entering interest).</summary>
        public void SendBeltStates(EntityId player)
        {
            CompileBelts();
            foreach (var s in Belts.Segments) Events.Add(BeltState(s, player));
        }

        private OpResult BeltResyncOp(Player player, in SimRequest r)
        {
            if (!Map.InBounds(r.X, r.Y)) return OpResult.BadRequest;
            var s = Belts.SegmentByHead(r.Y * Map.Cols + r.X);
            if (s == null) return OpResult.BadRequest;
            Events.Add(BeltState(s, player.Id));
            return OpResult.Ok;
        }

        /// <summary>
        /// A carrier pushes a belt (doc 04 §2.1 "player operation"): the segment of the tile runs at
        /// the operated multiplier for a moment, and everybody's belts with it. A carrier holding
        /// the key asks again before it runs out.
        /// </summary>
        private OpResult PushBeltOp(Player player, in SimRequest r)
        {
            if (player.Move.Dead) return OpResult.Dead;
            if (!Map.InBounds(r.X, r.Y) || !BeltNetwork.IsBelt(Constructs.At(r.X, r.Y))) return OpResult.BadRequest;
            if (!player.WasWithin(BuildRules.TileCentre(r.X), BuildRules.TileCentre(r.Y), _serverInteractRange, _rewindTicks)) return OpResult.OutOfRange;
            CompileBelts();
            var s = Belts.Push(r.X, r.Y);
            if (s == null) return OpResult.BadRequest;
            Events.Add(new BeltPushedEvent { X = HeadX(s), Y = HeadZ(s), Ticks = (ushort)s.PushTicks, Player = player.Id });
            return OpResult.Ok;
        }

        /// <summary>Puts a piece of mail on a segment's head and tells the clients; false without room (the outfeed; tests).</summary>
        public bool PutOnBelt(BeltSegment s, in MailItem mail)
        {
            if (!Belts.TryInsert(s, mail, out var item)) return false;
            Events.Add(new BeltItemAddedEvent { X = HeadX(s), Y = HeadZ(s), Item = Info(item) });
            return true;
        }

        /// <summary>
        /// The Intake's outfeed (doc 04 §2.6): in Prep and in Delivery, the oldest mail in the
        /// Intake (spawn order, FIFO — mail ids rise as it spawns) onto the head of the belt at the
        /// outfeed, one piece a tick while it has room. With Presort (doc 10 §4.2, rule flag Presort) the oldest of
        /// the street the last piece was for goes first, so the mail comes out a street at a time.
        /// </summary>
        private void StepOutfeed()
        {
            if (!BuildRules.IsOpen(Run.Phase)) return;
            var seg = Belts.Outfeed;
            if (seg == null) return;
            GridEntry? oldest = null, sameStreet = null;
            int index = -1, sameIndex = -1;
            bool presort = TeamHas(RuleFlag.Presort);
            foreach (var e in Intake.Entries)
            {
                if (e.Stack.Kind != StackKind.Mail) continue;
                var mail = e.Stack.Mail;
                for (int i = 0; i < mail.Count; i++)
                {
                    if (oldest == null || mail[i].Id < oldest.Stack.Mail[index].Id) { oldest = e; index = i; }
                    if (presort && StreetKey(mail[i].Address) == _outfeedStreet && (sameStreet == null || mail[i].Id < sameStreet.Stack.Mail[sameIndex].Id))
                    {
                        sameStreet = e;
                        sameIndex = i;
                    }
                }
            }
            if (sameStreet != null) { oldest = sameStreet; index = sameIndex; }
            if (oldest == null) return;
            var item = oldest.Stack.Mail[index];
            if (!BeltNetwork.PickLane(seg, Content.MailKinds[item.Kind], out _)) return;
            if (!PutOnBelt(seg, item)) return;
            _outfeedStreet = StreetKey(item.Address);
            oldest.Stack.Mail.RemoveAt(index);
            if (oldest.Stack.Mail.Count == 0) Intake.Remove(oldest.EntryId);
            else Intake.MarkDirty(oldest.EntryId);
            CommitContainer(Intake.Id);
        }

        /// <summary>The street the outfeed's last piece was for (Presort); -1 before any.</summary>
        private int _outfeedStreet = -1;

        private static int StreetKey(AddressId a) => a.District << 8 | a.Street;

        /// <summary>A run ends or begins: the mail on the belts goes back to the Intake with them.</summary>
        private void ClearBelts()
        {
            if (_belts == null) return;
            var items = _belts.Clear();
            foreach (var it in items) Spawner.ReturnToIntake(it.Mail);
            if (items.Count > 0) CommitContainer(Intake.Id);
        }

        // --- IBeltSink ----------------------------------------------------------------------------

        /// <summary>
        /// A belt's end at a mailbox (doc 04 §2.1 endpoints): the mailbox takes mail in Delivery at
        /// its insert rate, by the rules of a hand. A belt never guesses (doc 04 §3.2 has NPCs the
        /// same): mail for another address, or a kind the box does not take, waits at the end and
        /// the line backs up behind it (design choice 94).
        /// </summary>
        bool IBeltSink.TryDeliver(BeltSegment segment, in BeltItem item)
        {
            var dest = Map.GetDestination(segment.DestId);
            if (dest == null || !AutomatedDelivers(dest, item.Mail)) return false;
            Events.Add(new BeltItemRemovedEvent { MailId = item.Mail.Id });
            DeliverAutomated(dest, item.Mail, DeliveryMachine.Belt);
            segment.NextInsertTick = Tick + SimConstants.SecondsToTicks(1.0 / Math.Max(0.01, dest.Type.InsertRate));
            return true;
        }

        /// <summary>
        /// Whether a mailbox takes a piece from a belt or an inserter: in Delivery, its own address,
        /// a kind it takes, and a delivery that pays (design choice 94: automation never guesses).
        /// </summary>
        private bool AutomatedDelivers(DestinationDef dest, in MailItem mail)
        {
            if (Run.Phase != RunPhase.Delivery || mail.Address != dest.Address) return false;
            var kind = Content.MailKinds[mail.Kind];
            if (!DeliveryRules.KindAccepted(kind, dest.Type)) return false;
            return DeliveryRules.Evaluate(mail, kind, dest, Run.Shift, Economy.Wallet, Content, _teamStats, Tick).Outcome == DeliveryOutcome.Delivered;
        }

        /// <summary>A delivery by a belt or an inserter: the team's, credited to nobody (design choice 94 g).</summary>
        private void DeliverAutomated(DestinationDef dest, in MailItem mail, DeliveryMachine machine)
        {
            var kind = Content.MailKinds[mail.Kind];
            var verdict = DeliveryRules.Evaluate(mail, kind, dest, Run.Shift, Economy.Wallet, Content, _teamStats, Tick);
            Economy.Apply(verdict, kind.Index);
            Events.Add(new DeliveredEvent
            {
                MailId = mail.Id, DestId = dest.Id, Paid = (ushort)verdict.Amount, Player = EntityId.None, Kind = mail.Kind, Machine = machine,
            });
        }

        void IBeltSink.Moved(BeltSegment from, BeltSegment to, in BeltItem item) =>
            Events.Add(new BeltItemMovedEvent { MailId = item.Mail.Id, X = HeadX(to), Y = HeadZ(to), Lane = item.Lane });

        /// <summary>A belt's end at a Depot: the piece goes into its grid if there is room, else it waits and the line backs up.</summary>
        bool IBeltSink.TryStore(BeltSegment segment, in BeltItem item)
        {
            var store = GetContainer(segment.ContainerId);
            var stack = ItemStack.OfMail(item.Mail);
            if (store == null || !InventoryOps.HasRoomFor(Content, store, stack)) return false;
            InventoryOps.AutoPlace(Content, store, stack, _entryIds);
            CommitContainer(store.Id);
            Events.Add(new BeltItemRemovedEvent { MailId = item.Mail.Id });
            return true;
        }

        void IBeltSink.Took(BeltSegment from, in BeltItem item) => Events.Add(new BeltItemRemovedEvent { MailId = item.Mail.Id });

        void IBeltSink.Sorted(SorterNode sorter, int port, BeltSegment to, in BeltItem item) =>
            Events.Add(new BeltItemAddedEvent { X = HeadX(to), Y = HeadZ(to), Item = Info(item) });
    }
}
