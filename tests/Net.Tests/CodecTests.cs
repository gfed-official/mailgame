using System;
using System.Collections.Generic;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Net;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    public static class Shared
    {
        private static readonly Lazy<ContentDb> Lazy =
            new Lazy<ContentDb>(() => ContentDb.LoadFromDirectoryTree(AppContext.BaseDirectory));

        public static ContentDb Content => Lazy.Value;
    }

    public class CodecTests
    {
        private static NetReader Body(NetWriter w, MsgType expected)
        {
            var r = new NetReader(w.ToArray());
            Assert.Equal(expected, (MsgType)r.U8());
            return r;
        }

        private static GridContainer SampleContainer()
        {
            var content = Shared.Content;
            var c = new GridContainer(42, content.Container("chest"));
            var ids = new EntryIdAllocator(100);
            var letter = content.MailKind("letter");
            var address = new AddressId(1, 1, 7);
            for (uint i = 0; i < 3; i++)
            {
                var item = new MailItem { Id = 500 + i, Kind = letter.Index, Address = address, Value = 8, SpawnShift = 1, DeadlineShift = 1 };
                Assert.True(InventoryOps.AutoPlace(content, c, ItemStack.OfMail(item), ids));
            }
            var small = content.MailKind("small");
            Assert.True(InventoryOps.AutoPlace(content, c, ItemStack.OfMail(new MailItem
            {
                Id = 900, Kind = small.Index, Address = new AddressId(1, 1, 3, 2), Value = 30, SpawnShift = 1, DeadlineShift = 2, Flags = MailFlags.Cursed,
            }), ids));
            Assert.True(InventoryOps.AutoPlace(content, c, ItemStack.OfItem(content.Item("plank"), 12), ids));
            return c;
        }

        [Fact]
        public void PrimitivesRoundTrip()
        {
            var w = new NetWriter(1);
            w.U8(200); w.I8(-5); w.Bool(true); w.U16(65535); w.I16(-2); w.U32(0xDEADBEEF); w.I32(-123456);
            w.U64(0x0123456789ABCDEFUL); w.F32(-1.5f); w.Str("Larch Lane — ünïcode");
            var r = new NetReader(w.ToArray());
            Assert.Equal(200, r.U8());
            Assert.Equal(-5, r.I8());
            Assert.True(r.Bool());
            Assert.Equal(65535, r.U16());
            Assert.Equal(-2, r.I16());
            Assert.Equal(0xDEADBEEF, r.U32());
            Assert.Equal(-123456, r.I32());
            Assert.Equal(0x0123456789ABCDEFUL, r.U64());
            Assert.Equal(-1.5f, r.F32());
            Assert.Equal("Larch Lane — ünïcode", r.Str());
            Assert.True(r.AtEnd);
        }

        [Fact]
        public void TruncatedMessageThrowsFormatException()
        {
            var w = new NetWriter();
            new HelloMsg { ProtocolHash = 1, Name = "someone" }.Write(w);
            byte[] full = w.ToArray();
            for (int cut = 1; cut < full.Length; cut++)
            {
                var r = new NetReader(full.AsSpan(0, cut).ToArray());
                r.U8();
                Assert.Throws<NetFormatException>(() => HelloMsg.Read(r));
            }
        }

        [Fact]
        public void InputsRoundTrip()
        {
            var m = new InputsMsg();
            m.Inputs.Add(new PlayerInput { Seq = 10, MoveX = -127, MoveZ = 64, Yaw = 40000, Buttons = InputButtons.Sprint | InputButtons.Jump, HeldSlot = 3 });
            m.Inputs.Add(new PlayerInput { Seq = 11, MoveX = 5, MoveZ = -6, Yaw = 1, Buttons = InputButtons.Interact, HeldSlot = 0 });
            var w = new NetWriter();
            m.Write(w);
            var back = InputsMsg.Read(Body(w, MsgType.Inputs));
            Assert.Equal(2, back.Inputs.Count);
            Assert.Equal(m.Inputs[0], back.Inputs[0]);
            Assert.Equal(m.Inputs[1], back.Inputs[1]);
        }

        [Fact]
        public void RequestRoundTrip()
        {
            var q = SimRequest.Move(77, 3, 1234, 9, 5, 2, rotated: true, count: 6);
            q.DestId = 9; q.SortKey = SortKey.Size;
            var w = new NetWriter();
            RequestMsg.Write(w, q);
            Assert.Equal(q, RequestMsg.Read(Body(w, MsgType.Request)));
        }

        [Fact]
        public void ContainerStateRoundTripKeepsTheHash()
        {
            var c = SampleContainer();
            c.Commit();
            var w = new NetWriter();
            ContainerCodec.WriteState(w, c.Snapshot());
            var state = ContainerCodec.ReadState(new NetReader(w.ToArray()));
            var mirror = new GridContainer(state.ContainerId, Shared.Content.Containers[state.DefIndex]);
            mirror.Load(state);
            Assert.Equal(c.Version, mirror.Version);
            Assert.Equal(c.ComputeHash(), mirror.ComputeHash());
            Assert.Equal(state.Hash, mirror.ComputeHash());
        }

        [Fact]
        public void ContainerDeltaRoundTripAppliesToAMirror()
        {
            var c = SampleContainer();
            c.Commit();
            var mirror = c.Clone();

            GridEntry? plank = null, letters = null;
            foreach (var e in c.Entries)
            {
                if (e.Stack.Kind == StackKind.Item) plank = e;
                else if (e.Stack.Count == 3) letters = e;
            }
            c.Remove(plank!.EntryId);
            letters!.Stack.Take(1);
            c.MarkDirty(letters.EntryId);
            var delta = c.Commit()!;

            var w = new NetWriter();
            ContainerCodec.WriteDelta(w, delta);
            var back = ContainerCodec.ReadDelta(new NetReader(w.ToArray()));
            Assert.True(mirror.Apply(back));
            Assert.Equal(back.Hash, mirror.ComputeHash());
            Assert.Equal(c.ComputeHash(), mirror.ComputeHash());
        }

        [Fact]
        public void SnapshotRoundTripQuantisesOthersToOneCentimetre()
        {
            var m = new SnapshotMsg
            {
                Tick = 1234, AckSeq = 99, HasOwn = true,
                Own = new PlayerMoveState { X = 12.3456f, Y = 0.777f, Z = 45.6789f, VelY = -3.25f, Grounded = false },
                OwnSpeedMult = 0.87f, HasGlobal = true,
                Global = new GlobalState
                {
                    Shift = 1, Phase = RunPhase.Delivery, PhaseEndTick = 7200, Quota = 600, ShiftEarnings = -12, Wallet = -480,
                    Complaint = 17, Backlog = 3, Deliveries = 40, Misdeliveries = 2,
                    Outcome = RunOutcome.QuotaMissed, Host = new EntityId(EntityClass.Player, 2),
                },
            };
            m.Players.Add(new PlayerSnap { Id = new EntityId(EntityClass.Player, 2), X = 12.3456f, Y = 1.204f, Z = 91.999f, Yaw = 65535, HeldSlot = 4, Ready = true });
            var w = new NetWriter();
            m.Write(w);
            var back = SnapshotMsg.Read(Body(w, MsgType.Snapshot));

            Assert.Equal(1234u, back.Tick);
            Assert.Equal(99u, back.AckSeq);
            Assert.Equal(m.Own.X, back.Own.X);
            Assert.Equal(m.Own.Z, back.Own.Z);
            Assert.Equal(m.Own.VelY, back.Own.VelY);
            Assert.False(back.Own.Grounded);
            Assert.Equal(0.87f, back.OwnSpeedMult);
            Assert.Equal(m.Global, back.Global);
            var p = Assert.Single(back.Players);
            Assert.Equal(m.Players[0].Id, p.Id);
            Assert.Equal(12.35f, p.X, 3);
            Assert.Equal(1.20f, p.Y, 3);
            Assert.Equal(92.00f, p.Z, 3);
            Assert.Equal(65535, p.Yaw);
            Assert.Equal(4, p.HeldSlot);
            Assert.True(p.Ready);
        }

        [Fact]
        public void EveryEventTypeRoundTrips()
        {
            var c = SampleContainer();
            var delta = c.Commit()!;
            var player = new EntityId(EntityClass.Player, 3);
            var events = new List<SimEvent>
            {
                new PlayerJoinedEvent { Player = player, Name = "Ada", Slot = 2 },
                new PlayerLeftEvent { Player = player },
                new ContainerStateEvent { State = c.Snapshot() },
                new ContainerDeltaEvent { Delta = delta },
                new ContainerClosedEvent { ContainerId = 42 },
                new RequestResultEvent { RequestId = 8, Result = OpResult.DoesNotFit },
                new MailSpawnedEvent { FirstMailId = 100, Count = 12, Backlog = 1 },
                new DeliveredEvent { MailId = 5, DestId = 3, Paid = 30, Player = player, Kind = 2 },
                new MisdeliveredEvent { MailId = 6, DestId = 4, Penalty = 15, Player = player, Kind = 2 },
                new PhaseChangedEvent { Shift = 5, Phase = RunPhase.Results, PhaseEndTick = 7201, Quota = 600, Outcome = RunOutcome.Victory },
                new ShiftSummaryEvent
                {
                    Summary = new ShiftSummary
                    {
                        Shift = 2, Earned = -40, Quota = 1100, QuotaMet = true, Bonus = 110, DeliveriesByKind = new ushort[] { 7, 0, 3 },
                        Misdeliveries = 2, LateDeliveries = 1, Mvp = player, MvpDeliveries = 6,
                    },
                },
                new RunResultsEvent
                {
                    Results = new RunResults
                    {
                        Outcome = RunOutcome.QuotaMissed, ShiftsCompleted = 3, TotalEarned = 4321, Score = 4321, Deliveries = 70000, Misdeliveries = 9,
                        Players = { new PlayerResult { Player = player, Deliveries = 41, Misdeliveries = 4, Earned = -15, Xp = 505 } },
                    },
                },
                new PausedChangedEvent { Paused = true, Tick = 90210 },
            };
            Assert.Equal(Enum.GetValues<EventType>().Length, events.Count);

            var w = new NetWriter();
            EventsMsg.Write(w, events);
            var back = EventsMsg.Read(Body(w, MsgType.Events));
            Assert.Equal(events.Count, back.Count);
            for (int i = 0; i < events.Count; i++) Assert.IsType(events[i].GetType(), back[i]);

            var joined = (PlayerJoinedEvent)back[0];
            Assert.Equal(("Ada", (byte)2, player), (joined.Name, joined.Slot, joined.Player));
            Assert.Equal(delta.Hash, ((ContainerDeltaEvent)back[3]).Delta.Hash);
            Assert.Equal(delta.Upserts.Count, ((ContainerDeltaEvent)back[3]).Delta.Upserts.Count);
            Assert.Equal(OpResult.DoesNotFit, ((RequestResultEvent)back[5]).Result);
            var delivered = (DeliveredEvent)back[7];
            Assert.Equal((5u, (ushort)3, (ushort)30, player, (ushort)2), (delivered.MailId, delivered.DestId, delivered.Paid, delivered.Player, delivered.Kind));
            var phase = (PhaseChangedEvent)back[9];
            Assert.Equal((RunPhase.Results, 7201u, 600, RunOutcome.Victory, (byte)5), (phase.Phase, phase.PhaseEndTick, phase.Quota, phase.Outcome, phase.Shift));

            var summary = ((ShiftSummaryEvent)back[10]).Summary;
            Assert.Equal((2, -40, 1100, true, 110), (summary.Shift, summary.Earned, summary.Quota, summary.QuotaMet, summary.Bonus));
            Assert.Equal(new ushort[] { 7, 0, 3 }, summary.DeliveriesByKind);
            Assert.Equal(10, summary.Deliveries);
            Assert.Equal((2, 1, player, 6), (summary.Misdeliveries, summary.LateDeliveries, summary.Mvp, summary.MvpDeliveries));

            var results = ((RunResultsEvent)back[11]).Results;
            Assert.Equal((RunOutcome.QuotaMissed, 3, 4321, 4321, 70000, 9),
                (results.Outcome, results.ShiftsCompleted, results.TotalEarned, results.Score, results.Deliveries, results.Misdeliveries));
            var row = Assert.Single(results.Players);
            Assert.Equal((player, 41, 4, -15, 505), (row.Player, row.Deliveries, row.Misdeliveries, row.Earned, row.Xp));

            var paused = (PausedChangedEvent)back[12];
            Assert.Equal((true, 90210u), (paused.Paused, paused.Tick));
        }

        [Fact]
        public void ProtocolHashFollowsContent()
        {
            uint a = Protocol.Hash(Shared.Content);
            Assert.Equal(a, Protocol.Hash(ContentDb.LoadFromDirectoryTree(AppContext.BaseDirectory)));
            Assert.NotEqual(0u, a);
        }
    }
}
