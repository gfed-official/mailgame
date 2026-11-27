using System;
using System.Collections.Generic;
using System.Linq;
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
            m.Ack = 4321;
            m.EnemyAck = 4320;
            m.Inputs.Add(new PlayerInput { Seq = 10, MoveX = -127, MoveZ = 64, Yaw = 40000, Buttons = InputButtons.Sprint | InputButtons.Jump, HeldSlot = 3, View = 500 });
            m.Inputs.Add(new PlayerInput { Seq = 11, MoveX = 5, MoveZ = -6, Yaw = 1, Buttons = InputButtons.Interact, HeldSlot = 0, View = 501 });
            var w = new NetWriter();
            m.Write(w);
            // Doc 06 §4.2's 8 kbps up: the two acks, the seq and the drawn tick once a packet, 6 bytes an input.
            Assert.Equal(1 + 1 + 2 + 2 + 4 + 2 + 2 * 6, w.ToArray().Length);
            var back = InputsMsg.Read(Body(w, MsgType.Inputs));
            Assert.Equal(2, back.Inputs.Count);
            Assert.Equal(((ushort)4321, (ushort)4320), (back.Ack, back.EnemyAck));
            Assert.Equal(m.Inputs[0], back.Inputs[0]);
            Assert.Equal(m.Inputs[1], back.Inputs[1]);

            // Not consecutive: refused; none drawn: none said for any.
            m.Inputs[1] = m.Inputs[1] with { Seq = 13 };
            Assert.Throws<System.InvalidOperationException>(() => m.Write(new NetWriter()));
            m.Inputs.Clear();
            m.Inputs.Add(new PlayerInput { Seq = 4 });
            m.Inputs.Add(new PlayerInput { Seq = 5 });
            w = new NetWriter();
            m.Write(w);
            Assert.All(InputsMsg.Read(Body(w, MsgType.Inputs)).Inputs, i => Assert.Equal(0, i.View));
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
                Own = new PlayerMoveState { X = 12.3456f, Y = 0.777f, Z = 45.6789f, VelY = -3.25f, Grounded = false, Ride = 1, RideSpeed = 7.25f, HeadX = 0.6f, HeadZ = -0.8f },
                OwnSpeedMult = 0.87f, HasGlobal = true,
                Global = new GlobalState
                {
                    Shift = 1, Phase = RunPhase.Delivery, PhaseEndTick = 7200, Quota = 600, ShiftEarnings = -12, Wallet = -480,
                    Complaint = 17, Backlog = 3, Deliveries = 40, Misdeliveries = 2,
                    Outcome = RunOutcome.QuotaMissed, Host = new EntityId(EntityClass.Player, 2), Raid = RaidState.Active, ClockStopped = true, QuotaWaived = true, PostOfficeHp = 2345, Stamps = 5, Events = 0x141, EventStreet = 3,
                },
            };
            m.Players.Add(new PlayerSnap { Id = new EntityId(EntityClass.Player, 2), X = 12.3456f, Y = 1.204f, Z = 91.999f, Yaw = 65535, HeldSlot = 4, HeldTool = 17, Ready = true, Ride = 1, Heading = MathF.PI / 2f, Kit = 2 });
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
            // The rider's own vehicle state, unquantised, for the replay of their prediction.
            Assert.Equal(((byte)1, 7.25f, 0.6f, -0.8f), (back.Own.Ride, back.Own.RideSpeed, back.Own.HeadX, back.Own.HeadZ));
            Assert.Equal(m.Global, back.Global);
            var p = Assert.Single(back.Players);
            Assert.Equal(m.Players[0].Id, p.Id);
            Assert.Equal(12.35f, p.X, 3);
            Assert.Equal(1.20f, p.Y, 3);
            Assert.Equal(92.00f, p.Z, 3);
            Assert.Equal(65535, p.Yaw);
            Assert.Equal((byte)2, p.Kit);
            Assert.Equal(4, p.HeldSlot);
            Assert.Equal(17, p.HeldTool);
            Assert.True(p.Ready);
            Assert.False(p.Protected);
            Assert.False(p.Away);
            // Somebody else in a saddle: the vehicle, and its heading in 256 steps.
            Assert.Equal(1, p.Ride);
            Assert.Equal(MathF.PI / 2f, p.Heading, 3);
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
                        MvpDamage = new EntityId(EntityClass.Player, 9), MvpDamageDealt = 340, MvpBuilt = player, MvpBuiltCount = 7, ConstructsLost = 3,
                        MisdeliveredAt = new ushort[] { 4, 1 }, MisdeliveredCounts = new ushort[] { 2, 1 },
                    },
                },
                new RunResultsEvent
                {
                    Results = new RunResults
                    {
                        Outcome = RunOutcome.QuotaMissed, ShiftsCompleted = 3, FirstShift = 2, LastShift = 5, QuotaWaived = true, TotalEarned = 4321, Score = 4321, Deliveries = 70000, Misdeliveries = 9, ConstructsLost = 12,
                        Players = { new PlayerResult { Player = player, Deliveries = 41, Misdeliveries = 4, Earned = -15, Xp = 505, Kills = 17, Built = 23, Damage = 4567 } },
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
                new DraftOfferEvent { Perks = { 4, 0, 9 }, Reroll = true, RerollsLeft = 1 },
                new PerkPickedEvent { Player = player, Perk = 9, Card = 2, Auto = true },
                new ShopOfferEvent { Shift = 3, Specials = { 1, 6 } },
                new ShopBoughtEvent { Player = player, Item = 6, Price = 105 },
                new ShopSoldEvent { Player = player, Item = 7, Count = 12, Paid = 48 },
                new NodeHarvestedEvent { Player = player, Node = 317, HitsLeft = 4, Item = 1, Count = 3 },
                new NodesRegrownEvent { All = true },
                new ConstructPlacedEvent { Player = player, Building = 1, X = 14, Y = 9, Rotation = 3, Hp = 200, ContainerId = 4021 },
                new ConstructRemovedEvent { X = 14, Y = 9, Player = player, Destroyed = true },
                new ConstructDamagedEvent { X = 15, Y = 9, Hp = 40000 },
                new ConstructsClearedEvent(),
                new ConstructConfiguredEvent { X = 16, Y = 21, Player = player, Filters = { 0x0104, 0, 0x0302 } },
                new SorterStatusEvent { X = 16, Y = 21, Buffered = 7, Sorted = 1234, Overflowed = 56, Matches = { 3, 0, 9, 12 } },
                new InserterSwungEvent { X = 11, Y = 12, Kind = 3 },
                new PipeCapsuleEvent { Item = new BeltItemInfo { MailId = 79, Kind = 2, AddressKey = 0x01020005, Unit = 1 }, FromX = 6, FromY = 8, OutX = 11, OutY = 4 },
                new PipeCapsuleRemovedEvent { MailId = 79 },
                new VehicleFuelEvent { Vehicle = new EntityId(EntityClass.Vehicle, 4), Seconds = 287.5f },
                new VehicleRemovedEvent { Vehicle = new EntityId(EntityClass.Vehicle, 4) },
                new DriverEvent
                {
                    X = 14, Y = 21, State = DriverState.Unloading, Vehicle = new EntityId(EntityClass.Vehicle, 5), House = 7, Houses = 3, At = 1, Delivered = 9, Player = player,
                },
                new MailWaitingEvent { Houses = { (3, 7), (11, 1) } },
                new ForecastEvent { Shift = 2, Streets = { (1, 14), (4, 9) } },
                new RunEventEvent { Event = 4, Shift = 3, Stage = RunEventStage.Paid, Street = 2, Amount = 123456 },
                new TurretFiredEvent { X = 30, Y = 12, TargetX = 70.5f, TargetZ = 22.25f },
                new TurretOperatedEvent { X = 31, Y = 13, Player = player },
                new EnemyShotEvent { Kind = EnemyShotKind.Arrow, FromX = 61.5f, FromZ = 28.25f, ToX = 50f, ToZ = 27.75f, AtConstruct = true },
                new ConstructPushedEvent { FromX = 40, FromY = 9, X = 39, Y = 9, Enemy = 7 },
                new PostOfficeDamagedEvent { Hp = 2960, Enemy = 2 },
                new RepairedEvent { X = 33, Y = 15, Player = player, PostOffice = true },
                new CursedRaidEvent { DestId = 301, Player = player, Kind = 2, Raiders = 5 },
                new SlingshotFiredEvent { Player = player, FromX = 30.5f, FromZ = 28.25f, ToX = 18.75f, ToZ = 28f, Hit = SlingHit.Air, Loaded = 3 },
                new MegaSightedEvent { Enemy = new EntityId(EntityClass.Enemy, 12), Kind = 2, Edge = 1 },
                new LostParcelEvent { Enemy = 2, Kind = 3, Address = new PerformativeMail.Sim.World.AddressId(0x00020107, 0), Value = 315, Bag = 4031 },
                new RuinEvent { X = 32, Y = 14, Building = 7, Rotation = 3, Gone = true, Rebuilt = true, Player = player },
                new VehicleAddedEvent { Vehicle = new EntityId(EntityClass.Vehicle, 3), Def = 0, X = 14.5f, Z = 22.25f, Heading = 1.5f, CargoContainer = 4022, Buyer = player },
                new VehicleMountedEvent { Vehicle = new EntityId(EntityClass.Vehicle, 3), Player = player },
                new VehicleParkedEvent { Vehicle = new EntityId(EntityClass.Vehicle, 3), X = 60.75f, Z = 28f, Heading = -2.5f, Player = player },
                new PingEvent { Player = player, X = 40.25f, Z = 12.5f, Kind = PingKind.Materials, ExpiresTick = 4321 },
                new VehiclesClearedEvent(),
                new WorldChangedEvent { MapId = "small_island", Seed = 0xC0FFEE, MapHash = 0x1122334455667788UL, ChestContainerIds = { 41, 42 } },
                new KitGivenEvent { Player = new EntityId(EntityClass.Player, 2), Kit = 1, Team = true },
                new BeltStateEvent { X = 8, Y = 12, Tick = 901, Items = { new BeltItemInfo { MailId = 77, Kind = 5, AddressKey = 0x01020003, Unit = 2, Lane = 2, Pos = 3.25f } } },
                new BeltItemAddedEvent { X = 7, Y = 9, Item = new BeltItemInfo { MailId = 78, Kind = 1, AddressKey = 0x01010004, Lane = 1, Pos = 0f } },
                new BeltItemMovedEvent { MailId = 78, X = 11, Y = 12, Lane = 0 },
                new BeltItemRemovedEvent { MailId = 77 },
                new BeltChecksumEvent { X = 8, Y = 12, Count = 3, Hash = 0xDEADBEEF },
                new BeltPushedEvent { X = 5, Y = 12, Ticks = 21, Player = player },
            };
            Assert.Equal(Enum.GetValues<EventType>().Length, events.Count);

            var w = new NetWriter();
            EventsMsg.Write(w, events);
            var back = EventsMsg.Read(Body(w, MsgType.Events));
            Assert.Equal(events.Count, back.Count);
            for (int i = 0; i < events.Count; i++) Assert.IsType(events[i].GetType(), back[i]);

            var pinged = (PingEvent)back[^10];
            Assert.Equal((player, 40.25f, 12.5f, PingKind.Materials, 4321u), (pinged.Player, pinged.X, pinged.Z, pinged.Kind, pinged.ExpiresTick));
            var changed = (WorldChangedEvent)back[^8];
            Assert.Equal(("small_island", 0xC0FFEEu, 0x1122334455667788UL), (changed.MapId, changed.Seed, changed.MapHash));
            Assert.Equal(new uint[] { 41, 42 }, changed.ChestContainerIds);
            var kit = (KitGivenEvent)back[^7];
            var belt = (BeltStateEvent)back[^6];
            Assert.Equal((8, 12, 901u), (belt.X, belt.Y, belt.Tick));
            var info = belt.Items.Single();
            Assert.Equal((77u, (ushort)5, 0x01020003u, (byte)2, (byte)2, 3.25f), (info.MailId, info.Kind, info.AddressKey, info.Unit, info.Lane, info.Pos));
            var onBelt = (BeltItemAddedEvent)back[^5];
            Assert.Equal((7, 9, 78u, (byte)1), (onBelt.X, onBelt.Y, onBelt.Item.MailId, onBelt.Item.Lane));
            var moved = (BeltItemMovedEvent)back[^4];
            Assert.Equal((78u, 11, 12, (byte)0), (moved.MailId, moved.X, moved.Y, moved.Lane));
            Assert.Equal(77u, ((BeltItemRemovedEvent)back[^3]).MailId);
            var sum = (BeltChecksumEvent)back[^2];
            var push = (BeltPushedEvent)back[^1];
            var configured = back.OfType<ConstructConfiguredEvent>().Single();
            Assert.Equal((16, 21, player), (configured.X, configured.Y, configured.Player));
            Assert.Equal(new ushort[] { 0x0104, 0, 0x0302 }, configured.Filters);
            var capsule = back.OfType<PipeCapsuleEvent>().Single();
            Assert.Equal((79u, (ushort)2, 0x01020005u, (byte)1, (byte)6, (byte)8, (byte)11, (byte)4),
                (capsule.Item.MailId, capsule.Item.Kind, capsule.Item.AddressKey, capsule.Item.Unit, capsule.FromX, capsule.FromY, capsule.OutX, capsule.OutY));
            Assert.Equal(79u, back.OfType<PipeCapsuleRemovedEvent>().Single().MailId);
            var fuel = back.OfType<VehicleFuelEvent>().Single();
            Assert.Equal((new EntityId(EntityClass.Vehicle, 4), 287.5f), (fuel.Vehicle, fuel.Seconds));
            Assert.Equal(new EntityId(EntityClass.Vehicle, 4), back.OfType<VehicleRemovedEvent>().Single().Vehicle);
            var driver = back.OfType<DriverEvent>().Single();
            Assert.Equal(((byte)14, (byte)21, DriverState.Unloading, new EntityId(EntityClass.Vehicle, 5), (ushort)7, (byte)3, (byte)1, (ushort)9, player),
                (driver.X, driver.Y, driver.State, driver.Vehicle, driver.House, driver.Houses, driver.At, driver.Delivered, driver.Player));
            Assert.Equal(new (ushort, ushort)[] { (3, 7), (11, 1) }, back.OfType<MailWaitingEvent>().Single().Houses);
            var runEvent = back.OfType<RunEventEvent>().Single();
            Assert.Equal(((byte)4, (byte)3, RunEventStage.Paid, (byte)2, 123456), (runEvent.Event, runEvent.Shift, runEvent.Stage, runEvent.Street, runEvent.Amount));
            var forecast = back.OfType<ForecastEvent>().Single();
            Assert.Equal(2, forecast.Shift);
            Assert.Equal(new (ushort, ushort)[] { (1, 14), (4, 9) }, forecast.Streets);
            var fired = back.OfType<TurretFiredEvent>().Single();
            Assert.Equal(((byte)30, (byte)12, 70.5f, 22.25f), (fired.X, fired.Y, fired.TargetX, fired.TargetZ));
            var manned = back.OfType<TurretOperatedEvent>().Single();
            Assert.Equal(((byte)31, (byte)13, player), (manned.X, manned.Y, manned.Player));
            var arrow = back.OfType<EnemyShotEvent>().Single();
            Assert.Equal((EnemyShotKind.Arrow, 61.5f, 28.25f, 50f, 27.75f, true), (arrow.Kind, arrow.FromX, arrow.FromZ, arrow.ToX, arrow.ToZ, arrow.AtConstruct));
            var shoved = back.OfType<ConstructPushedEvent>().Single();
            Assert.Equal(((byte)40, (byte)9, (byte)39, (byte)9, (byte)7), (shoved.FromX, shoved.FromY, shoved.X, shoved.Y, shoved.Enemy));
            var po = back.OfType<PostOfficeDamagedEvent>().Single();
            Assert.Equal(((ushort)2960, (byte)2), (po.Hp, po.Enemy));
            var curse = back.OfType<CursedRaidEvent>().Single();
            Assert.Equal(((ushort)301, player, (ushort)2, (byte)5), (curse.DestId, curse.Player, curse.Kind, curse.Raiders));
            var mended = back.OfType<RepairedEvent>().Single();
            Assert.Equal(((byte)33, (byte)15, player, true), (mended.X, mended.Y, mended.Player, mended.PostOffice));
            var ruin = back.OfType<RuinEvent>().Single();
            Assert.Equal(((byte)32, (byte)14, (byte)7, (byte)3, true, true, player), (ruin.X, ruin.Y, ruin.Building, ruin.Rotation, ruin.Gone, ruin.Rebuilt, ruin.Player));
            var swung = back.OfType<InserterSwungEvent>().Single();
            Assert.Equal(((byte)11, (byte)12, (ushort)3), (swung.X, swung.Y, swung.Kind));
            var status = back.OfType<SorterStatusEvent>().Single();
            Assert.Equal((16, 21, (byte)7, (ushort)1234, (ushort)56), (status.X, status.Y, status.Buffered, status.Sorted, status.Overflowed));
            Assert.Equal(new ushort[] { 3, 0, 9, 12 }, status.Matches);
            Assert.Equal((5, 12, (ushort)21, player), (push.X, push.Y, push.Ticks, push.Player));
            Assert.Equal((8, 12, (ushort)3, 0xDEADBEEFu), (sum.X, sum.Y, sum.Count, sum.Hash));
            Assert.Equal((new EntityId(EntityClass.Player, 2), (byte)1, true), (kit.Player, kit.Kit, kit.Team));
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
            Assert.Equal((new EntityId(EntityClass.Player, 9), (ushort)340, player, (ushort)7, (ushort)3), (summary.MvpDamage, summary.MvpDamageDealt, summary.MvpBuilt, summary.MvpBuiltCount, summary.ConstructsLost));
            Assert.Equal(new ushort[] { 4, 1 }, summary.MisdeliveredAt);
            Assert.Equal(new ushort[] { 2, 1 }, summary.MisdeliveredCounts);

            var results = ((RunResultsEvent)back[11]).Results;
            Assert.Equal((RunOutcome.QuotaMissed, 3, 4321, 4321, 70000, 9),
                (results.Outcome, results.ShiftsCompleted, results.TotalEarned, results.Score, results.Deliveries, results.Misdeliveries));
            Assert.Equal((2, 5, true), (results.FirstShift, results.LastShift, results.QuotaWaived));
            var row = Assert.Single(results.Players);
            Assert.Equal((player, 41, 4, -15, 505), (row.Player, row.Deliveries, row.Misdeliveries, row.Earned, row.Xp));
            Assert.Equal(((ushort)17, (ushort)23, 4567, 12), (row.Kills, row.Built, row.Damage, results.ConstructsLost));

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
            Assert.Equal(new byte[] { 4, 0, 9 }, ((DraftOfferEvent)back[27]).Perks);
            Assert.Equal((true, (byte)1), (((DraftOfferEvent)back[27]).Reroll, ((DraftOfferEvent)back[27]).RerollsLeft));
            var picked = (PerkPickedEvent)back[28];
            Assert.Equal((player, (byte)9, (byte)2, true), (picked.Player, picked.Perk, picked.Card, picked.Auto));
            var offer = (ShopOfferEvent)back[29];
            Assert.Equal((byte)3, offer.Shift);
            Assert.Equal(new byte[] { 1, 6 }, offer.Specials);
            var bought = (ShopBoughtEvent)back[30];
            Assert.Equal((player, (byte)6, (ushort)105), (bought.Player, bought.Item, bought.Price));
            var sold = (ShopSoldEvent)back[31];
            Assert.Equal((player, (ushort)7, (ushort)12, 48), (sold.Player, sold.Item, sold.Count, sold.Paid));
            var harvested = (NodeHarvestedEvent)back[32];
            Assert.Equal((player, (ushort)317, (byte)4, (ushort)1, (byte)3), (harvested.Player, harvested.Node, harvested.HitsLeft, harvested.Item, harvested.Count));
            Assert.True(((NodesRegrownEvent)back[33]).All);
            var placed = (ConstructPlacedEvent)back[34];
            Assert.Equal((player, (byte)1, (byte)14, (byte)9, (byte)3, (ushort)200, 4021u),
                (placed.Player, placed.Building, placed.X, placed.Y, placed.Rotation, placed.Hp, placed.ContainerId));
            var removed = (ConstructRemovedEvent)back[35];
            Assert.Equal(((byte)14, (byte)9, player, true), (removed.X, removed.Y, removed.Player, removed.Destroyed));
            var bike = new EntityId(EntityClass.Vehicle, 3);
            var added = back.OfType<VehicleAddedEvent>().Single();
            Assert.Equal((bike, (byte)0, 14.5f, 22.25f, 1.5f, 4022u, player), (added.Vehicle, added.Def, added.X, added.Z, added.Heading, added.CargoContainer, added.Buyer));
            var mounted = back.OfType<VehicleMountedEvent>().Single();
            Assert.Equal((bike, player), (mounted.Vehicle, mounted.Player));
            var parked = back.OfType<VehicleParkedEvent>().Single();
            Assert.Equal((bike, 60.75f, 28f, -2.5f, player), (parked.Vehicle, parked.X, parked.Z, parked.Heading, parked.Player));
            var damaged = (ConstructDamagedEvent)back[36];
            Assert.Equal(((byte)15, (byte)9, (ushort)40000), (damaged.X, damaged.Y, damaged.Hp));
        }

        [Fact]
        public void TheBuildRequestsRoundTrip()
        {
            var place = SimRequest.PlaceConstruct(9, 1, 14, 9, 3);
            Assert.Equal((RequestKind.PlaceConstruct, (ushort)1, (byte)14, (byte)9, (ushort)3), (place.Kind, place.DestId, place.X, place.Y, place.Count));
            var remove = SimRequest.RemoveConstruct(10, 14, 9);
            Assert.Equal((RequestKind.RemoveConstruct, (byte)14, (byte)9), (remove.Kind, remove.X, remove.Y));
            var mount = SimRequest.Mount(11, new EntityId(EntityClass.Vehicle, 3).Value);
            Assert.Equal((RequestKind.Mount, new EntityId(EntityClass.Vehicle, 3).Value), (mount.Kind, mount.EntryId));
            foreach (var q in new[] { place, remove, mount })
            {
                var w = new NetWriter();
                RequestMsg.Write(w, q);
                Assert.Equal(q, RequestMsg.Read(Body(w, MsgType.Request)));
            }
        }

        [Fact]
        public void TheHarvestRequestRoundTrips()
        {
            var q = SimRequest.Harvest(9, 317, 2);
            Assert.Equal((RequestKind.Harvest, (ushort)317, (byte)2), (q.Kind, q.DestId, q.X));
            var w = new NetWriter();
            RequestMsg.Write(w, q);
            Assert.Equal(q, RequestMsg.Read(Body(w, MsgType.Request)));
        }

        [Fact]
        public void TheShopRequestsRoundTrip()
        {
            foreach (var q in new[] { SimRequest.Buy(5, 6), SimRequest.Sell(6, 3, 1234, 7), SimRequest.Sell(7, 3, 1234), SimRequest.Use(8, 2, 99) })
            {
                var w = new NetWriter();
                RequestMsg.Write(w, q);
                Assert.Equal(q, RequestMsg.Read(Body(w, MsgType.Request)));
            }
            Assert.Equal((RequestKind.Buy, (ushort)6), (SimRequest.Buy(5, 6).Kind, SimRequest.Buy(5, 6).Count));
            Assert.Equal((RequestKind.Sell, 3u, 1234u, (ushort)0), (SimRequest.Sell(7, 3, 1234).Kind, SimRequest.Sell(7, 3, 1234).Container, SimRequest.Sell(7, 3, 1234).EntryId, SimRequest.Sell(7, 3, 1234).Count));
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
            m.Perks.Add(new PerkInfo { Perk = 6 });
            m.Perks.Add(new PerkInfo { Holder = owner, Perk = 1 });
            m.DraftOffer.AddRange(new byte[] { 3, 1, 8 });
            m.DraftPicks.Add(new DraftPickInfo { Player = owner, Card = 1, Perk = 7 });
            m.RerollsLeft = 2;
            m.ShopSpecials.AddRange(new byte[] { 0, 6 });
            m.ShopOwned.Add(4);
            m.Nodes.Add(new NodeInfo { Node = 12, HitsLeft = 0 });
            m.Nodes.Add(new NodeInfo { Node = 317, HitsLeft = 4 });
            m.Constructs.Add(new ConstructInfo { Building = 0, X = 14, Y = 9, Rotation = 1, Hp = 180 });
            m.Constructs.Add(new ConstructInfo { Building = 1, X = 15, Y = 9, Rotation = 3, Hp = 200, ContainerId = 4021 });
            m.Constructs.Add(new ConstructInfo { Building = 12, X = 16, Y = 21, Rotation = 2, Hp = 500, Filters = new List<ushort> { 0x0104, 0, 0x0302 } });
            m.Vehicles.Add(new VehicleInfo { Id = new EntityId(EntityClass.Vehicle, 1), Def = 0, X = 14f, Z = 25f, Heading = 0.5f, CargoContainer = 4030 });
            m.Vehicles.Add(new VehicleInfo { Id = new EntityId(EntityClass.Vehicle, 2), Def = 0, X = 40f, Z = 28f, Heading = -1f, CargoContainer = 4031, Rider = owner });
            var w = new NetWriter();
            m.Write(w);
            var back = WelcomeMsg.Read(Body(w, MsgType.Welcome));

            // What stands built, in the order it was built.
            Assert.Equal(new[] { ((byte)0, (byte)14, (byte)9, (byte)1, (ushort)180, 0u), ((byte)1, (byte)15, (byte)9, (byte)3, (ushort)200, 4021u), ((byte)12, (byte)16, (byte)21, (byte)2, (ushort)500, 0u) },
                back.Constructs.Select(c => (c.Building, c.X, c.Y, c.Rotation, c.Hp, c.ContainerId)));
            // A sorter's filters come with it; what has none has none.
            Assert.Null(back.Constructs[0].Filters);
            Assert.Equal(new ushort[] { 0x0104, 0, 0x0302 }, back.Constructs[2].Filters);

            // The vehicles of the run, parked or ridden.
            Assert.Equal(new[] { (new EntityId(EntityClass.Vehicle, 1), (byte)0, 14f, 25f, 0.5f, 4030u, EntityId.None), (new EntityId(EntityClass.Vehicle, 2), (byte)0, 40f, 28f, -1f, 4031u, owner) },
                back.Vehicles.Select(v => (v.Id, v.Def, v.X, v.Z, v.Heading, v.CargoContainer, v.Rider)));

            // The nodes somebody has swung at, and what each has left.
            Assert.Equal(new[] { ((ushort)12, (byte)0), ((ushort)317, (byte)4) }, back.Nodes.Select(n => (n.Node, n.HitsLeft)));

            // What the counter has on special this shift, and what the team bought for the run.
            Assert.Equal(new byte[] { 0, 6 }, back.ShopSpecials);
            Assert.Equal(new byte[] { 4 }, back.ShopOwned);

            // The perks of the run and the Draft under way, for somebody who comes back into it.
            Assert.Equal(new[] { (EntityId.None, (byte)6), (owner, (byte)1) }, back.Perks.Select(p => (p.Holder, p.Perk)));
            Assert.Equal(new byte[] { 3, 1, 8 }, back.DraftOffer);
            Assert.Equal((owner, (byte)1, (byte)7), (Assert.Single(back.DraftPicks).Player, back.DraftPicks[0].Card, back.DraftPicks[0].Perk));
            Assert.Equal((byte)2, back.RerollsLeft);

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
            new HelloMsg { ProtocolHash = 0xC0FFEE, Name = "Bo", Playtest = PlaytestMask.All, StartShift = 3, Account = 0xFEDCBA9876543210UL, Rank = 7, StartWallet = 450 }.Write(w);
            var back = HelloMsg.Read(Body(w, MsgType.Hello));
            Assert.Equal((0xC0FFEEu, "Bo", PlaytestFlags.NoTimer | PlaytestFlags.AtMailbox | PlaytestFlags.NoQuota | PlaytestFlags.RaidNow | PlaytestFlags.Packages | PlaytestFlags.Materials | PlaytestFlags.AllUnlocks, (byte)3),
                (back.ProtocolHash, back.Name, back.Playtest, back.StartShift));
            Assert.Equal(0xFEDCBA9876543210UL, back.Account);
            Assert.Equal((byte)7, back.Rank);
            Assert.Equal(450u, back.StartWallet);

            w = new NetWriter();
            new HelloMsg { ProtocolHash = 1, Name = "Ada" }.Write(w);
            back = HelloMsg.Read(Body(w, MsgType.Hello));
            Assert.Equal((PlaytestFlags.None, (byte)0, 0ul, (byte)1, 0u), (back.Playtest, back.StartShift, back.Account, back.Rank, back.StartWallet));
        }

        [Fact]
        public void SnapshotCarriesTheFight_EnemiesInTenBytesEachWhole()
        {
            var m = new SnapshotMsg
            {
                Tick = 50, AckSeq = 7, HasOwn = true, HasEnemies = true, OwnHp = 62.5f,
                Own = new PlayerMoveState { X = 1f, Z = 2f, Grounded = true, Winded = true, Dead = true, Becalmed = true, Fatigue = 0.75f },
            };
            m.Players.Add(new PlayerSnap { Id = new EntityId(EntityClass.Player, 1), Hp = 128, Protected = true });
            m.Players.Add(new PlayerSnap { Id = new EntityId(EntityClass.Player, 2), Hp = 255, Away = true });
            var empty = new NetWriter();
            m.Write(empty);
            m.Enemies.Add(new EnemySnap { Id = 65535, Kind = 2, Mega = true, State = EnemyState.Flee, X = 91.99f, Z = 28.013f, Yaw = -MathF.PI / 2f, Hp = 1 });
            m.Enemies.Add(new EnemySnap { Id = 3, Kind = 0, State = EnemyState.Windup, X = 0f, Z = 5.5f, Yaw = MathF.PI / 4f, Hp = 255 });
            var w = new NetWriter();
            m.Write(w);
            // Whole (no baseline): id, mask, kind/mega/state, position, yaw, health.
            Assert.Equal(20, w.ToArray().Length - empty.ToArray().Length);

            var back = SnapshotMsg.Read(Body(w, MsgType.Snapshot));
            Assert.True(back.HasEnemies);
            Assert.False(back.HasGlobal);
            Assert.Equal((true, true, true, true, 0.75f, 62.5f), (back.Own.Grounded, back.Own.Winded, back.Own.Dead, back.Own.Becalmed, back.Own.Fatigue, back.OwnHp));
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

        private sealed class Kept : ISnapshotBaselines
        {
            public readonly Dictionary<uint, IReadOnlyDictionary<uint, PlayerQ>> Players = new Dictionary<uint, IReadOnlyDictionary<uint, PlayerQ>>();
            public readonly Dictionary<uint, IReadOnlyDictionary<ushort, EnemyQ>> Enemies = new Dictionary<uint, IReadOnlyDictionary<ushort, EnemyQ>>();
            public bool TryPlayers(uint tick, out IReadOnlyDictionary<uint, PlayerQ> players) => Players.TryGetValue(tick, out players!);
            public bool TryEnemies(uint tick, out IReadOnlyDictionary<ushort, EnemyQ> enemies) => Enemies.TryGetValue(tick, out enemies!);
        }

        [Fact]
        public void ASnapshotDeltaCodedAgainstAnAckedOne_CarriesOnlyWhatChanged()
        {
            // Doc 06 §4.2 "delta-compressed against the last acked snapshot per client (bitmask of changed fields per entity)".
            var first = new SnapshotMsg { Tick = 100, HasEnemies = true };
            first.Players.Add(new PlayerSnap { Id = new EntityId(EntityClass.Player, 1), X = 10f, Y = 0f, Z = 20f, Yaw = 100, Hp = 255, Kit = 1 });
            first.Enemies.Add(new EnemySnap { Id = 7, Kind = 2, X = 30f, Z = 40f, Yaw = 1f, Hp = 255 });
            first.Enemies.Add(new EnemySnap { Id = 8, Kind = 0, X = 50f, Z = 40f, Yaw = 2f, Hp = 200 });
            var w = new NetWriter();
            first.Write(w);
            var kept = new Kept();
            var got = SnapshotMsg.Read(Body(w, MsgType.Snapshot), kept);
            Assert.False(got.Undecodable);
            kept.Players[100] = got.PlayersQ.ToDictionary(q => q.Id);
            kept.Enemies[100] = got.EnemiesQ.ToDictionary(q => q.Id);

            // Four ticks on: the carrier walked half a metre and turned, raider 7 walked 0.6 m, raider 8 stood, a new one came.
            var next = new SnapshotMsg { Tick = 104, HasEnemies = true, PlayerBaseTick = 100, EnemyBaseTick = 100, PlayerBase = kept.Players[100], EnemyBase = kept.Enemies[100] };
            next.Players.Add(first.Players[0] with { X = 10.5f, Yaw = 300 });
            next.Enemies.Add(first.Enemies[0] with { Z = 40.6f });
            next.Enemies.Add(first.Enemies[1]);
            next.Enemies.Add(new EnemySnap { Id = 9, Kind = 1, X = 5f, Z = 6f, Yaw = 0f, Hp = 255 });
            var w2 = new NetWriter();
            next.Write(w2);
            int header = 1 + 4 + 4 + 1, player = 4 + 1 + 3 + 2, enemies = 2 + 1 + (2 + 1 + 2) + (2 + 1) + (2 + 1 + 1 + 4 + 1 + 1);
            Assert.Equal(header + 2 + 1 + player + enemies, w2.ToArray().Length);
            var back = SnapshotMsg.Read(Body(w2, MsgType.Snapshot), kept);
            Assert.False(back.Undecodable);
            Assert.Equal((100u, 100u), (back.PlayerBaseTick, back.EnemyBaseTick));
            Assert.Equal((10.5f, 20f, (ushort)300, (byte)255, (byte)1), (back.Players[0].X, back.Players[0].Z, back.Players[0].Yaw, back.Players[0].Hp, back.Players[0].Kit));
            Assert.Equal(40.6f, back.Enemies[0].Z, 2);
            Assert.Equal((50f, (byte)200), (back.Enemies[1].X, back.Enemies[1].Hp));
            Assert.Equal(((ushort)9, (byte)1, 5f), (back.Enemies[2].Id, back.Enemies[2].Kind, back.Enemies[2].X));

            // A long walk does not fit a byte: the position goes whole.
            next.Players[0] = first.Players[0] with { X = 14f };
            w2 = new NetWriter();
            next.Write(w2);
            Assert.Equal(14f, SnapshotMsg.Read(Body(w2, MsgType.Snapshot), kept).Players[0].X);

            // Against a baseline the reader does not have, nothing of it may be used.
            Assert.True(SnapshotMsg.Read(Body(w2, MsgType.Snapshot), new Kept()).Undecodable);
            Assert.True(SnapshotMsg.Read(Body(w2, MsgType.Snapshot)).Undecodable);
        }

        [Fact]
        public void SnapshotCarriesTheTrucksNpcDriversDrive_InThirteenBytesEach_OnlyWhileThereAreAny()
        {
            var m = new SnapshotMsg { Tick = 9 };
            var empty = new NetWriter();
            m.Write(empty);
            m.NpcVehicles.Add((new EntityId(EntityClass.Vehicle, 5).Value, 31.004f, 44.996f, -MathF.PI / 2f));
            var w = new NetWriter();
            m.Write(w);
            Assert.Equal(1 + 13, w.ToArray().Length - empty.ToArray().Length);
            var back = SnapshotMsg.Read(Body(w, MsgType.Snapshot));
            var (id, x, z, heading) = back.NpcVehicles.Single();
            Assert.Equal(new EntityId(EntityClass.Vehicle, 5).Value, id);
            Assert.Equal((31.0f, 45.0f), (x, z));
            Assert.Equal(MathF.PI * 1.5f, heading, 2);
            Assert.Empty(SnapshotMsg.Read(Body(empty, MsgType.Snapshot)).NpcVehicles);
        }

        [Fact]
        public void ProtocolHashFollowsContent()
        {
            uint a = Protocol.Hash(Shared.Content);
            Assert.Equal(a, Protocol.Hash(ContentDb.LoadFromDirectoryTree(AppContext.BaseDirectory)));
            Assert.NotEqual(0u, a);
        }

        [Fact]
        public void PriorityMail_CarriesItsDueTick_OverTheWire()
        {
            var content = Shared.Content;
            var c = new GridContainer(43, content.Container("chest"));
            var ids = new EntryIdAllocator(100);
            var letter = content.MailKind("letter");
            Assert.True(InventoryOps.AutoPlace(content, c, ItemStack.OfMail(new MailItem
            {
                Id = 601, Kind = letter.Index, Address = new AddressId(1, 1, 7), Value = 24, SpawnShift = 1, DeadlineShift = 1, Flags = MailFlags.Priority, DueTick = 123456,
            }), ids));
            Assert.True(InventoryOps.AutoPlace(content, c, ItemStack.OfMail(new MailItem
            {
                Id = 602, Kind = letter.Index, Address = new AddressId(1, 1, 7), Value = 8, SpawnShift = 1, DeadlineShift = 1,
            }), ids));
            // Priority mail never stacks with mail that is not.
            Assert.Equal(2, c.EntryCount);
            c.Commit();
            var w = new NetWriter();
            ContainerCodec.WriteState(w, c.Snapshot());
            var state = ContainerCodec.ReadState(new NetReader(w.ToArray()));
            var priority = state.Entries.Single(e => e.Stack.Priority).Stack;
            Assert.Equal(123456u, priority.Mail.Single().DueTick);
            Assert.Equal(123456u, priority.PriorityDue);
            Assert.Equal(0u, state.Entries.Single(e => !e.Stack.Priority).Stack.Mail.Single().DueTick);
        }
    }
}
