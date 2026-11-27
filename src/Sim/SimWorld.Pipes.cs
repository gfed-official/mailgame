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
    /// Pneumatic pipes (doc 04 §2.2) in the tick, before the belts: an inlet takes a piece of mail
    /// (from the end of a belt, an inserter, or the Intake on the outfeed's tile) only when it can
    /// send it on, and sends it in a capsule to the outlet chosen for it there — the nearest whose
    /// filter catches it (an outlet without one before a mailbox catches that house's mail), else
    /// the nearest with no filter (the default) — that has room. The capsule runs to it; the outlet
    /// puts it into what stands in front of it, as an inserter would.
    /// </summary>
    public sealed partial class SimWorld
    {
        private PipeNetwork? _pipes;
        private ConstructField? _pipesField;
        private readonly List<PipeCapsule> _pipeRerouted = new List<PipeCapsule>();
        private readonly List<MailItem> _pipeOrphans = new List<MailItem>();

        /// <summary>Every pipe of the map (made on first use, again with a new map).</summary>
        public PipeNetwork Pipes
        {
            get
            {
                if (_pipes == null || !ReferenceEquals(_pipesField, Constructs))
                {
                    _pipes = new PipeNetwork(Content, Map, Constructs);
                    _pipesField = Constructs;
                }
                return _pipes;
            }
        }

        private void StepPipes()
        {
            CompileBelts();
            CompilePipes();
            var pipes = Pipes;
            pipes.Step(SimConstants.TickDt, arrive: true);
            if (!BuildRules.IsOpen(Run.Phase)) return;
            foreach (var inlet in pipes.Inlets) StepIntakeInlet(inlet);
            foreach (var outlet in pipes.Outlets) StepOutlet(outlet);
        }

        /// <summary>After building: capsules whose way broke go round (everybody hears), those with nowhere to go back to the Intake.</summary>
        private void CompilePipes()
        {
            _pipeRerouted.Clear();
            _pipeOrphans.Clear();
            if (!Pipes.Compile(_pipeRerouted, _pipeOrphans)) return;
            foreach (var mail in _pipeOrphans)
            {
                Spawner.ReturnToIntake(mail);
                Events.Add(new PipeCapsuleRemovedEvent { MailId = mail.Id });
            }
            if (_pipeOrphans.Count > 0) CommitContainer(Intake.Id);
            foreach (var cap in _pipeRerouted) Events.Add(CapsuleEvent(cap, cap.Path[0], EntityId.None));
        }

        private PipeCapsuleEvent CapsuleEvent(PipeCapsule cap, int from, EntityId audience) => new PipeCapsuleEvent
        {
            Audience = audience,
            Item = Info(new BeltItem { Mail = cap.Mail }),
            FromX = (ushort)(from % Map.Cols), FromY = (ushort)(from / Map.Cols),
            OutX = (ushort)(cap.Outlet % Map.Cols), OutY = (ushort)(cap.Outlet / Map.Cols),
        };

        /// <summary>Who joins gets every capsule under way, from the tile it is at.</summary>
        public void SendPipeStates(EntityId player)
        {
            CompileBelts();
            CompilePipes();
            foreach (var cap in Pipes.Capsules) Events.Add(CapsuleEvent(cap, cap.Tile, player));
        }

        /// <summary>A run ends or begins: the mail in the pipes and outlets goes back to the Intake.</summary>
        private void ClearPipes()
        {
            if (_pipes == null) return;
            var mail = _pipes.Clear();
            foreach (var m in mail) Spawner.ReturnToIntake(m);
            if (mail.Count > 0) CommitContainer(Intake.Id);
        }

        /// <summary>The tile in front of a construct (one tile on).</summary>
        private int FrontTile(Construct c)
        {
            var (dx, dz) = BeltNetwork.Dirs[c.Facing];
            int fx = c.Tx + dx, fz = c.Tz + dz;
            return Map.InBounds(fx, fz) ? fz * Map.Cols + fx : -1;
        }

        /// <summary>
        /// Whether an outlet takes a piece: 1 if its filter catches it (or, with none, it stands
        /// before the mailbox of the piece's house), 0 if it is a default (no filter, no mailbox),
        /// −1 if not.
        /// </summary>
        private int OutletTakes(OutletNode outlet, in MailItem mail)
        {
            var filter = outlet.Built.Filter(0, 0);
            if (!filter.IsEmpty) return filter.Matches(mail) ? 1 : -1;
            int front = FrontTile(outlet.Built);
            int dest = front >= 0 ? Belts.MailboxOn(front) : -1;
            if (dest >= 0) return Map.GetDestination(dest)?.Takes(mail.Address) == true ? 1 : -1;
            return 0;
        }

        /// <summary>
        /// The outlet a piece goes to from an inlet: the nearest of those it reaches whose filter
        /// catches it, else the nearest default; of those with room. A piece one of them catches
        /// but none has room for waits rather than going to a default (automation never guesses).
        /// </summary>
        private OutletNode? RouteFor(InletNode inlet, in MailItem mail)
        {
            var pipes = Pipes;
            OutletNode? best = null;
            int bestDist = int.MaxValue;
            bool bestCaught = false, anyCaught = false;
            foreach (var o in pipes.Outlets)
            {
                int d = pipes.Distance(o.Tile, inlet.Tile);
                if (d < 0) continue;
                int takes = OutletTakes(o, mail);
                if (takes < 0) continue;
                bool caught = takes > 0;
                anyCaught |= caught;
                if (o.Reserved >= pipes.Capacity) continue;
                bool better = best == null || (caught && !bestCaught)
                    || (caught == bestCaught && (d < bestDist || (d == bestDist && o.Tile < best.Tile)));
                if (!better) continue;
                best = o;
                bestDist = d;
                bestCaught = caught;
            }
            if (anyCaught && !bestCaught) return null;
            return best;
        }

        /// <summary>Sends a piece from an inlet if it may now (its spacing, a route with room); with <paramref name="dryRun"/> only says whether it would.</summary>
        private bool TryLaunch(InletNode inlet, in MailItem mail, bool dryRun)
        {
            if (Tick < inlet.NextTick) return false;
            var outlet = RouteFor(inlet, mail);
            if (outlet == null) return false;
            if (dryRun) return true;
            var cap = Pipes.Send(mail, inlet.Tile, outlet.Tile);
            if (cap == null) return false;
            Events.Add(CapsuleEvent(cap, inlet.Tile, EntityId.None));
            var pipes = Pipes;
            inlet.NextTick = Tick + Math.Max(1u, SimConstants.SecondsToTicks(pipes.Spacing / pipes.Speed));
            inlet.Sent++;
            return true;
        }

        /// <summary>An inlet on the outfeed's tile, facing away from the counter (doc 04 §2.6): the oldest piece of the Intake it can send.</summary>
        private void StepIntakeInlet(InletNode inlet)
        {
            if (inlet.Tile != Belts.OutfeedTile || inlet.Built.Facing != BeltNetwork.OutfeedRotation || Tick < inlet.NextTick) return;
            GridEntry? pick = null;
            int index = -1;
            foreach (var e in Intake.Entries)
            {
                if (e.Stack.Kind != StackKind.Mail) continue;
                var list = e.Stack.Mail;
                for (int i = 0; i < list.Count; i++)
                {
                    if (pick != null && list[i].Id >= pick.Stack.Mail[index].Id) continue;
                    if (RouteFor(inlet, list[i]) == null) continue;
                    pick = e;
                    index = i;
                }
            }
            if (pick == null) return;
            var mail = pick.Stack.Mail[index];
            if (!TryLaunch(inlet, mail, dryRun: false)) return;
            pick.Stack.Mail.RemoveAt(index);
            if (pick.Stack.Mail.Count == 0) Intake.Remove(pick.EntryId);
            else Intake.MarkDirty(pick.EntryId);
            CommitContainer(Intake.Id);
        }

        /// <summary>An outlet puts its oldest piece into what stands in front of it, at its rate (a mailbox's, if slower).</summary>
        private void StepOutlet(OutletNode outlet)
        {
            if (outlet.Queue.Count == 0 || Tick < outlet.NextTick) return;
            var target = TargetOf(outlet.Built);
            var mail = outlet.Queue[0];
            if (!target.Any || !Takes(target, mail)) return;
            outlet.Queue.RemoveAt(0);
            Events.Add(new PipeCapsuleRemovedEvent { MailId = mail.Id });
            Put(target, mail, DeliveryMachine.Pipe);
            double rate = Content.Balance.GetDouble("pipeOutletRate");
            if (target.Mailbox != null) rate = Math.Min(rate, target.Mailbox.Type.InsertRate);
            outlet.NextTick = Tick + Math.Max(1u, SimConstants.SecondsToTicks(1.0 / Math.Max(0.01, rate)));
        }

        /// <summary>A belt's end before a parked truck's rear (doc 04 §3.1 loading zone): into its cargo while it has room.</summary>
        bool IBeltSink.TryLoad(BeltSegment segment, in BeltItem item)
        {
            if (_vehicles.Count == 0) return false;
            var v = LoadingFaceOn(segment.FrontTile % Map.Cols, segment.FrontTile / Map.Cols);
            var stack = ItemStack.OfMail(item.Mail);
            if (v == null || !InventoryOps.HasRoomFor(Content, v.Cargo, stack)) return false;
            InventoryOps.AutoPlace(Content, v.Cargo, stack, _entryIds);
            CommitContainer(v.Cargo.Id);
            Events.Add(new BeltItemRemovedEvent { MailId = item.Mail.Id });
            return true;
        }

        /// <summary>A belt's end at an inlet: the piece goes into a capsule if the inlet can send it now, else it waits.</summary>
        bool IBeltSink.TryInlet(BeltSegment segment, in BeltItem item)
        {
            var inlet = Pipes.InletAt(segment.FrontTile);
            if (inlet == null || !TryLaunch(inlet, item.Mail, dryRun: false)) return false;
            Events.Add(new BeltItemRemovedEvent { MailId = item.Mail.Id });
            return true;
        }

        /// <summary>For the filter panel: of the Intake's mail, what an outlet would be sent by its filter (a default: what no other outlet's catches).</summary>
        private int OutletMatches(Construct built)
        {
            var node = Pipes.OutletOf(built);
            if (node == null) return 0;
            int n = 0;
            foreach (var e in Intake.Entries)
            {
                if (e.Stack.Kind != StackKind.Mail) continue;
                foreach (var mail in e.Stack.Mail)
                {
                    int takes = OutletTakes(node, mail);
                    if (takes > 0) n++;
                    else if (takes == 0)
                    {
                        bool other = false;
                        foreach (var o in Pipes.Outlets)
                            if (o != node && OutletTakes(o, mail) > 0) other = true;
                        if (!other) n++;
                    }
                }
            }
            return n;
        }
    }
}
