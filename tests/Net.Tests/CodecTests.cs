using System;
using System.Collections.Generic;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;
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
                    Outcome = RunOutcome.QuotaMissed, Host = new EntityId(EntityClass.Player, 2), Raid = RaidState.Active, ClockStopped = true, QuotaWaived = true,
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
            Assert.False(p.Protected);
            Assert.False(p.Away);
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
                new PhaseChangedEvent { Shift = 5, Phase = RunPhase.Results, Tick = 7141, PhaseEndTick = 7201, Quota = 600, Outcome = RunOutcome.Victory },
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
                new AttackEvent { Player = player, Weapon = Weapon.Cannon, X = 12.5f, Z = 28.25f, Yaw = 16384, Hits = 3 },
                new BlastEvent { X = 40.5f, Z = 27f, Hits = 4 },
                new EnemyDiedEvent { Enemy = new EntityId(EntityClass.Enemy, 77), Kind = 2, Mega = true, X = 50f, Z = 28f, Killer = player },
                new LootDroppedEvent
                {
                    LootId = 9001, X = 50f, Z = 28f, EnemyKind = 2, Mega = true, Coins = 120,
                    Items = { new LootItem { Item = 4, Count = 9 }, new LootItem { Item = 6, Count = 3 } },
                },
                new LootTakenEvent { LootId = 9001, Player = player, Coins = 120, Items = { new LootItem { Item = 4, Count = 5 } }, Gone = true },
                new LootGoneEvent { LootId = 9002 },
                new RaidChangedEvent { Raid = RaidState.Warning, Tick = 4500 },
                new PlayerDiedEvent { Player = player, RespawnTick = 4800, Killer = 1 },
                new PlayerRespawnedEvent { Player = player, ProtectedUntilTick = 4950 },
                new PlayerParkedEvent { Destination = 517 },
                new BagDroppedEvent { ContainerId = 77, Owner = player, X = 50.25f, Z = 28.5f, DespawnTick = 5400 },
                new BagGoneEvent { ContainerId = 77, Returned = true, Mail = 12 },
                new PlayerAwayEvent { Player = player, UntilTick = 9000 },
                new PlayerBackEvent { Player = player },
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
            Assert.Equal((RunPhase.Results, 7141u, 7201u, 600, RunOutcome.Victory, (byte)5), (phase.Phase, phase.Tick, phase.PhaseEndTick, phase.Quota, phase.Outcome, phase.Shift));

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

            var attack = (AttackEvent)back[13];
            Assert.Equal((player, Weapon.Cannon, 12.5f, 28.25f, (ushort)16384, (byte)3), (attack.Player, attack.Weapon, attack.X, attack.Z, attack.Yaw, attack.Hits));
            var blast = (BlastEvent)back[14];
            Assert.Equal((40.5f, 27f, (byte)4), (blast.X, blast.Z, blast.Hits));
            var died = (EnemyDiedEvent)back[15];
            Assert.Equal((new EntityId(EntityClass.Enemy, 77), (byte)2, true, 50f, 28f, player), (died.Enemy, died.Kind, died.Mega, died.X, died.Z, died.Killer));
            var dropped = (LootDroppedEvent)back[16];
            Assert.Equal((9001u, 50f, 28f, (byte)2, true, (ushort)120), (dropped.LootId, dropped.X, dropped.Z, dropped.EnemyKind, dropped.Mega, dropped.Coins));
            Assert.Equal(2, dropped.Items.Count);
            Assert.Equal(((ushort)6, (byte)3), (dropped.Items[1].Item, dropped.Items[1].Count));
            var taken = (LootTakenEvent)back[17];
            Assert.Equal((9001u, player, (ushort)120, true), (taken.LootId, taken.Player, taken.Coins, taken.Gone));
            Assert.Equal(((ushort)4, (byte)5), (taken.Items[0].Item, taken.Items[0].Count));
            Assert.Equal(9002u, ((LootGoneEvent)back[18]).LootId);
            var raid = (RaidChangedEvent)back[19];
            Assert.Equal((RaidState.Warning, 4500u), (raid.Raid, raid.Tick));
            var fell = (PlayerDiedEvent)back[20];
            Assert.Equal((player, 4800u, (byte)1), (fell.Player, fell.RespawnTick, fell.Killer));
            var stood = (PlayerRespawnedEvent)back[21];
            Assert.Equal((player, 4950u), (stood.Player, stood.ProtectedUntilTick));
            Assert.Equal((ushort)517, ((PlayerParkedEvent)back[22]).Destination);
            var bag = (BagDroppedEvent)back[23];
            Assert.Equal((77u, player, 50.25f, 28.5f, 5400u), (bag.ContainerId, bag.Owner, bag.X, bag.Z, bag.DespawnTick));
            var bagGone = (BagGoneEvent)back[24];
            Assert.Equal((77u, true, (ushort)12), (bagGone.ContainerId, bagGone.Returned, bagGone.Mail));
            var away = (PlayerAwayEvent)back[25];
            Assert.Equal((player, 9000u), (away.Player, away.UntilTick));
            Assert.Equal(player, ((PlayerBackEvent)back[26]).Player);
        }

        [Fact]
        public void WelcomeNamesTheChestsAndTheBagsLyingAbout()
        {
            var c = SampleContainer();
            var owner = new EntityId(EntityClass.Player, 2);
            var m = new WelcomeMsg
            {
                PlayerId = new EntityId(EntityClass.Player, 3), Tick = 900, Seed = 7, MapId = "test_street", MapHash = 0xFEEDFACECAFEBEEFUL,
                IntakeContainerId = 1, Global = new GlobalState { Shift = 2, Phase = RunPhase.Delivery },
                RaidTick = 640, RespawnTick = 1100, ProtectedUntilTick = 1250,
                Hotbar = c.Snapshot(), Inventory = c.Snapshot(),
            };
            m.Loot.Add(new LootDroppedEvent { LootId = 9001, X = 50f, Z = 28f, EnemyKind = 2, Mega = true, Coins = 120, Items = { new LootItem { Item = 4, Count = 5 } } });
            m.ChestContainerIds.Add(2);
            m.Bags.Add(new BagInfo { ContainerId = 9, Owner = owner, X = 50.25f, Z = 28.5f, DespawnTick = 1800 });
            m.Bags.Add(new BagInfo { ContainerId = 11, X = 12f, Z = 30f, DespawnTick = 1900 });
            m.Players.Add(new PlayerInfo { Id = owner, Name = "Cass", Slot = 1 });
            var w = new NetWriter();
            m.Write(w);
            var back = WelcomeMsg.Read(Body(w, MsgType.Welcome));

            Assert.Equal((m.PlayerId, 900u, 7u, "test_street", m.MapHash, 1u), (back.PlayerId, back.Tick, back.Seed, back.MapId, back.MapHash, back.IntakeContainerId));
            Assert.Equal(new[] { 2u }, back.ChestContainerIds);
            Assert.Equal(2, back.Bags.Count);
            Assert.Equal((9u, owner, 50.25f, 28.5f, 1800u), (back.Bags[0].ContainerId, back.Bags[0].Owner, back.Bags[0].X, back.Bags[0].Z, back.Bags[0].DespawnTick));
            Assert.Equal((11u, EntityId.None, 12f, 30f, 1900u), (back.Bags[1].ContainerId, back.Bags[1].Owner, back.Bags[1].X, back.Bags[1].Z, back.Bags[1].DespawnTick));
            Assert.Equal(m.Global, back.Global);
            // What somebody who comes into a running shift was not there to hear.
            Assert.Equal((640u, 1100u, 1250u), (back.RaidTick, back.RespawnTick, back.ProtectedUntilTick));
            var loot = Assert.Single(back.Loot);
            Assert.Equal((9001u, 50f, 28f, (byte)2, true, (ushort)120), (loot.LootId, loot.X, loot.Z, loot.EnemyKind, loot.Mega, loot.Coins));
            Assert.Equal(((ushort)4, (byte)5), (Assert.Single(loot.Items).Item, loot.Items[0].Count));
            Assert.Equal(("Cass", (byte)1), (back.Players[0].Name, back.Players[0].Slot));
            Assert.Equal(c.ComputeHash(), back.Inventory.Hash);
        }

        [Fact]
        public void HelloCarriesThePlaytestFlags()
        {
            var w = new NetWriter();
            new HelloMsg { ProtocolHash = 0xC0FFEE, Name = "Bo", Playtest = PlaytestMask.All, StartShift = 3, Account = 0xFEDCBA9876543210UL }.Write(w);
            var back = HelloMsg.Read(Body(w, MsgType.Hello));
            Assert.Equal((0xC0FFEEu, "Bo", PlaytestFlags.NoTimer | PlaytestFlags.AtMailbox | PlaytestFlags.NoQuota | PlaytestFlags.RaidNow | PlaytestFlags.Packages, (byte)3),
                (back.ProtocolHash, back.Name, back.Playtest, back.StartShift));
            Assert.Equal(0xFEDCBA9876543210UL, back.Account);

            w = new NetWriter();
            new HelloMsg { ProtocolHash = 1, Name = "Ada" }.Write(w);
            back = HelloMsg.Read(Body(w, MsgType.Hello));
            Assert.Equal((PlaytestFlags.None, (byte)0, 0ul), (back.Playtest, back.StartShift, back.Account));
        }

        [Fact]
        public void SnapshotCarriesTheFight_EnemiesInNineBytesEach()
        {
            var m = new SnapshotMsg
            {
                Tick = 50, AckSeq = 7, HasOwn = true, HasEnemies = true, OwnHp = 62.5f,
                Own = new PlayerMoveState { X = 1f, Z = 2f, Grounded = true, Winded = true, Dead = true, Fatigue = 0.75f },
            };
            m.Players.Add(new PlayerSnap { Id = new EntityId(EntityClass.Player, 1), Hp = 128, Protected = true });
            m.Players.Add(new PlayerSnap { Id = new EntityId(EntityClass.Player, 2), Hp = 255, Away = true });
            var empty = new NetWriter();
            m.Write(empty);
            m.Enemies.Add(new EnemySnap { Id = 65535, Kind = 2, Mega = true, State = EnemyState.Flee, X = 91.99f, Z = 28.013f, Yaw = -MathF.PI / 2f, Hp = 1 });
            m.Enemies.Add(new EnemySnap { Id = 3, Kind = 0, State = EnemyState.Windup, X = 0f, Z = 5.5f, Yaw = MathF.PI / 4f, Hp = 255 });
            var w = new NetWriter();
            m.Write(w);
            Assert.Equal(18, w.ToArray().Length - empty.ToArray().Length);

            var back = SnapshotMsg.Read(Body(w, MsgType.Snapshot));
            Assert.True(back.HasEnemies);
            Assert.False(back.HasGlobal);
            Assert.Equal((true, true, true, 0.75f, 62.5f), (back.Own.Grounded, back.Own.Winded, back.Own.Dead, back.Own.Fatigue, back.OwnHp));
            Assert.Equal(2, back.Players.Count);
            Assert.Equal(((byte)128, true, false, false), (back.Players[0].Hp, back.Players[0].Protected, back.Players[0].Away, back.Players[0].Ready));
            Assert.Equal(((byte)255, false, true, false), (back.Players[1].Hp, back.Players[1].Protected, back.Players[1].Away, back.Players[1].Ready));
            Assert.Equal(2, back.Enemies.Count);
            var a = back.Enemies[0];
            Assert.Equal(((ushort)65535, (byte)2, true, EnemyState.Flee, (byte)1), (a.Id, a.Kind, a.Mega, a.State, a.Hp));
            Assert.Equal(92.0f, a.X, 2);
            Assert.Equal(28.02f, a.Z, 2);
            // −90° comes back as 270°: the same heading.
            Assert.Equal(MathF.PI * 1.5f, a.Yaw, 2);
            var b = back.Enemies[1];
            Assert.Equal(((ushort)3, (byte)0, false, EnemyState.Windup, (byte)255), (b.Id, b.Kind, b.Mega, b.State, b.Hp));
            Assert.Equal(MathF.PI / 4f, b.Yaw, 2);

            // Without the flag the list is not on the wire at all, and a reader does not take "none" for "all gone".
            m.HasEnemies = false;
            var w2 = new NetWriter();
            m.Write(w2);
            var none = SnapshotMsg.Read(Body(w2, MsgType.Snapshot));
            Assert.False(none.HasEnemies);
            Assert.Empty(none.Enemies);
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
