using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
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
            belts.Step(SimConstants.TickDt, this, Tick);
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
        /// outfeed, one piece a tick while it has room.
        /// </summary>
        private void StepOutfeed()
        {
            if (!BuildRules.IsOpen(Run.Phase)) return;
            var seg = Belts.Outfeed;
            if (seg == null) return;
            GridEntry? oldest = null;
            int index = -1;
            foreach (var e in Intake.Entries)
            {
                if (e.Stack.Kind != StackKind.Mail) continue;
                var mail = e.Stack.Mail;
                for (int i = 0; i < mail.Count; i++)
                    if (oldest == null || mail[i].Id < oldest.Stack.Mail[index].Id) { oldest = e; index = i; }
            }
            if (oldest == null) return;
            var item = oldest.Stack.Mail[index];
            if (!BeltNetwork.PickLane(seg, Content.MailKinds[item.Kind], out _)) return;
            if (!PutOnBelt(seg, item)) return;
            oldest.Stack.Mail.RemoveAt(index);
            if (oldest.Stack.Mail.Count == 0) Intake.Remove(oldest.EntryId);
            else Intake.MarkDirty(oldest.EntryId);
            CommitContainer(Intake.Id);
        }

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
            if (Run.Phase != RunPhase.Delivery) return false;
            var dest = Map.GetDestination(segment.DestId);
            if (dest == null || item.Mail.Address != dest.Address) return false;
            var kind = Content.MailKinds[item.Mail.Kind];
            if (!DeliveryRules.KindAccepted(kind, dest.Type)) return false;
            var verdict = DeliveryRules.Evaluate(item.Mail, kind, dest, Run.Shift, Economy.Wallet, Content, _teamStats);
            if (verdict.Outcome != DeliveryOutcome.Delivered) return false;
            Economy.Apply(verdict, kind.Index);
            Events.Add(new BeltItemRemovedEvent { MailId = item.Mail.Id });
            Events.Add(new DeliveredEvent
            {
                MailId = item.Mail.Id, DestId = dest.Id, Paid = (ushort)verdict.Amount, Player = EntityId.None, Kind = item.Mail.Kind,
            });
            segment.NextInsertTick = Tick + SimConstants.SecondsToTicks(1.0 / Math.Max(0.01, dest.Type.InsertRate));
            return true;
        }

        void IBeltSink.Moved(BeltSegment from, BeltSegment to, in BeltItem item) =>
            Events.Add(new BeltItemMovedEvent { MailId = item.Mail.Id, X = HeadX(to), Y = HeadZ(to), Lane = item.Lane });
    }
}
