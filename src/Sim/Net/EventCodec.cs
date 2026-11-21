using System.Collections.Generic;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Sim.Net
{
    public enum EventType : byte
    {
        PlayerJoined = 1,
        PlayerLeft = 2,
        ContainerState = 3,
        ContainerDelta = 4,
        ContainerClosed = 5,
        RequestResult = 6,
        MailSpawned = 7,
        Delivered = 8,
        Misdelivered = 9,
        PhaseChanged = 10,
        ShiftSummary = 11,
        RunResults = 12,
        PausedChanged = 13,
        Attack = 14,
        Blast = 15,
        EnemyDied = 16,
        LootDropped = 17,
        LootTaken = 18,
        LootGone = 19,
        RaidChanged = 20,
        PlayerDied = 21,
        PlayerRespawned = 22,
        PlayerParked = 23,
        BagDropped = 24,
        BagGone = 25,
        PlayerAway = 26,
        PlayerBack = 27,
        DraftOffer = 28,
        PerkPicked = 29,
        ShopOffer = 30,
        ShopBought = 31,
        ShopSold = 32,
        NodeHarvested = 33,
        NodesRegrown = 34,
        ConstructPlaced = 35,
        ConstructRemoved = 36,
        ConstructDamaged = 37,
        ConstructsCleared = 38,
        VehicleAdded = 39,
        VehicleMounted = 40,
        VehicleParked = 41,
        VehiclesCleared = 42,
        KitGiven = 43,
    }

    /// <summary>Wire format of reliable sim events. The audience is not sent: the server routes by it.</summary>
    public static class EventCodec
    {
        public static void Write(NetWriter w, SimEvent ev)
        {
            switch (ev)
            {
                case PlayerJoinedEvent e:
                    w.U8((byte)EventType.PlayerJoined);
                    w.U32(e.Player.Value); w.Str(e.Name); w.U8(e.Slot);
                    break;
                case PlayerLeftEvent e:
                    w.U8((byte)EventType.PlayerLeft);
                    w.U32(e.Player.Value);
                    break;
                case ContainerStateEvent e:
                    w.U8((byte)EventType.ContainerState);
                    ContainerCodec.WriteState(w, e.State);
                    break;
                case ContainerDeltaEvent e:
                    w.U8((byte)EventType.ContainerDelta);
                    ContainerCodec.WriteDelta(w, e.Delta);
                    break;
                case ContainerClosedEvent e:
                    w.U8((byte)EventType.ContainerClosed);
                    w.U32(e.ContainerId);
                    break;
                case RequestResultEvent e:
                    w.U8((byte)EventType.RequestResult);
                    w.U32(e.RequestId); w.U8((byte)e.Result);
                    break;
                case MailSpawnedEvent e:
                    w.U8((byte)EventType.MailSpawned);
                    w.U32(e.FirstMailId); w.U16(e.Count); w.U16(e.Backlog);
                    break;
                case DeliveredEvent e:
                    w.U8((byte)EventType.Delivered);
                    w.U32(e.MailId); w.U16(e.DestId); w.U16(e.Paid); w.U32(e.Player.Value); w.U16(e.Kind);
                    break;
                case MisdeliveredEvent e:
                    w.U8((byte)EventType.Misdelivered);
                    w.U32(e.MailId); w.U16(e.DestId); w.U16(e.Penalty); w.U32(e.Player.Value); w.U16(e.Kind);
                    break;
                case PhaseChangedEvent e:
                    w.U8((byte)EventType.PhaseChanged);
                    w.U8(e.Shift); w.U8((byte)e.Phase); w.U32(e.Tick); w.U32(e.PhaseEndTick); w.I32(e.Quota); w.U8((byte)e.Outcome);
                    break;
                case ShiftSummaryEvent e:
                {
                    var s = e.Summary;
                    w.U8((byte)EventType.ShiftSummary);
                    w.U8(s.Shift); w.I32(s.Earned); w.I32(s.Quota); w.Bool(s.QuotaMet); w.I32(s.Bonus);
                    w.U8((byte)s.DeliveriesByKind.Length);
                    foreach (ushort n in s.DeliveriesByKind) w.U16(n);
                    w.U16(s.Misdeliveries); w.U16(s.LateDeliveries); w.U32(s.Mvp.Value); w.U16(s.MvpDeliveries);
                    break;
                }
                case RunResultsEvent e:
                {
                    var s = e.Results;
                    w.U8((byte)EventType.RunResults);
                    w.U8((byte)s.Outcome); w.U8(s.ShiftsCompleted); w.I32(s.TotalEarned); w.I32(s.Score);
                    w.I32(s.Deliveries); w.I32(s.Misdeliveries);
                    w.U8((byte)s.Players.Count);
                    foreach (var p in s.Players)
                    {
                        w.U32(p.Player.Value); w.U16(p.Deliveries); w.U16(p.Misdeliveries); w.I32(p.Earned); w.I32(p.Xp);
                    }
                    break;
                }
                case PausedChangedEvent e:
                    w.U8((byte)EventType.PausedChanged);
                    w.Bool(e.Paused); w.U32(e.Tick);
                    break;
                case AttackEvent e:
                    w.U8((byte)EventType.Attack);
                    w.U32(e.Player.Value); w.U8((byte)e.Weapon); w.F32(e.X); w.F32(e.Z); w.U16(e.Yaw); w.U8(e.Hits);
                    break;
                case BlastEvent e:
                    w.U8((byte)EventType.Blast);
                    w.F32(e.X); w.F32(e.Z); w.U8(e.Hits);
                    break;
                case EnemyDiedEvent e:
                    w.U8((byte)EventType.EnemyDied);
                    w.U32(e.Enemy.Value); w.U8(e.Kind); w.Bool(e.Mega); w.F32(e.X); w.F32(e.Z); w.U32(e.Killer.Value);
                    break;
                case LootDroppedEvent e:
                    w.U8((byte)EventType.LootDropped);
                    w.U32(e.LootId); w.F32(e.X); w.F32(e.Z); w.U8(e.EnemyKind); w.Bool(e.Mega); w.U16(e.Coins);
                    WriteLoot(w, e.Items);
                    break;
                case LootTakenEvent e:
                    w.U8((byte)EventType.LootTaken);
                    w.U32(e.LootId); w.U32(e.Player.Value); w.U16(e.Coins);
                    WriteLoot(w, e.Items);
                    w.Bool(e.Gone);
                    break;
                case LootGoneEvent e:
                    w.U8((byte)EventType.LootGone);
                    w.U32(e.LootId);
                    break;
                case RaidChangedEvent e:
                    w.U8((byte)EventType.RaidChanged);
                    w.U8((byte)e.Raid); w.U32(e.Tick);
                    break;
                case PlayerDiedEvent e:
                    w.U8((byte)EventType.PlayerDied);
                    w.U32(e.Player.Value); w.U32(e.RespawnTick); w.U8(e.Killer);
                    break;
                case PlayerRespawnedEvent e:
                    w.U8((byte)EventType.PlayerRespawned);
                    w.U32(e.Player.Value); w.U32(e.ProtectedUntilTick);
                    break;
                case PlayerParkedEvent e:
                    w.U8((byte)EventType.PlayerParked);
                    w.U16(e.Destination);
                    break;
                case BagDroppedEvent e:
                    w.U8((byte)EventType.BagDropped);
                    w.U32(e.ContainerId); w.U32(e.Owner.Value); w.F32(e.X); w.F32(e.Z); w.U32(e.DespawnTick);
                    break;
                case BagGoneEvent e:
                    w.U8((byte)EventType.BagGone);
                    w.U32(e.ContainerId); w.Bool(e.Returned); w.U16(e.Mail);
                    break;
                case PlayerAwayEvent e:
                    w.U8((byte)EventType.PlayerAway);
                    w.U32(e.Player.Value); w.U32(e.UntilTick);
                    break;
                case PlayerBackEvent e:
                    w.U8((byte)EventType.PlayerBack);
                    w.U32(e.Player.Value);
                    break;
                case DraftOfferEvent e:
                    w.U8((byte)EventType.DraftOffer);
                    w.U8((byte)e.Perks.Count);
                    foreach (byte perk in e.Perks) w.U8(perk);
                    break;
                case PerkPickedEvent e:
                    w.U8((byte)EventType.PerkPicked);
                    w.U32(e.Player.Value); w.U8(e.Perk); w.U8(e.Card); w.Bool(e.Auto);
                    break;
                case ShopOfferEvent e:
                    w.U8((byte)EventType.ShopOffer);
                    w.U8(e.Shift);
                    w.U8((byte)e.Specials.Count);
                    foreach (byte item in e.Specials) w.U8(item);
                    break;
                case ShopBoughtEvent e:
                    w.U8((byte)EventType.ShopBought);
                    w.U32(e.Player.Value); w.U8(e.Item); w.U16(e.Price);
                    break;
                case ShopSoldEvent e:
                    w.U8((byte)EventType.ShopSold);
                    w.U32(e.Player.Value); w.U16(e.Item); w.U16(e.Count); w.I32(e.Paid);
                    break;
                case NodeHarvestedEvent e:
                    w.U8((byte)EventType.NodeHarvested);
                    w.U32(e.Player.Value); w.U16(e.Node); w.U8(e.HitsLeft); w.U16(e.Item); w.U8(e.Count);
                    break;
                case NodesRegrownEvent e:
                    w.U8((byte)EventType.NodesRegrown);
                    w.Bool(e.All);
                    break;
                case ConstructPlacedEvent e:
                    w.U8((byte)EventType.ConstructPlaced);
                    w.U32(e.Player.Value); w.U8(e.Building); w.U8(e.X); w.U8(e.Y); w.U8(e.Rotation); w.U16(e.Hp); w.U32(e.ContainerId);
                    break;
                case ConstructRemovedEvent e:
                    w.U8((byte)EventType.ConstructRemoved);
                    w.U8(e.X); w.U8(e.Y); w.U32(e.Player.Value); w.Bool(e.Destroyed);
                    break;
                case ConstructDamagedEvent e:
                    w.U8((byte)EventType.ConstructDamaged);
                    w.U8(e.X); w.U8(e.Y); w.U16(e.Hp);
                    break;
                case ConstructsClearedEvent _:
                    w.U8((byte)EventType.ConstructsCleared);
                    break;
                case VehicleAddedEvent e:
                    w.U8((byte)EventType.VehicleAdded);
                    w.U32(e.Vehicle.Value); w.U8(e.Def); w.F32(e.X); w.F32(e.Z); w.F32(e.Heading); w.U32(e.CargoContainer); w.U32(e.Buyer.Value);
                    break;
                case VehicleMountedEvent e:
                    w.U8((byte)EventType.VehicleMounted);
                    w.U32(e.Vehicle.Value); w.U32(e.Player.Value);
                    break;
                case VehicleParkedEvent e:
                    w.U8((byte)EventType.VehicleParked);
                    w.U32(e.Vehicle.Value); w.F32(e.X); w.F32(e.Z); w.F32(e.Heading); w.U32(e.Player.Value);
                    break;
                case VehiclesClearedEvent _:
                    w.U8((byte)EventType.VehiclesCleared);
                    break;
                case KitGivenEvent e:
                    w.U8((byte)EventType.KitGiven);
                    w.U32(e.Player.Value);
                    w.U8(e.Kit);
                    w.U8((byte)(e.Team ? 1 : 0));
                    break;
                default:
                    throw new System.ArgumentException($"no wire format for {ev.GetType().Name}");
            }
        }

        public static SimEvent Read(NetReader r)
        {
            var type = (EventType)r.U8();
            switch (type)
            {
                case EventType.PlayerJoined:
                    return new PlayerJoinedEvent { Player = new EntityId(r.U32()), Name = r.Str(), Slot = r.U8() };
                case EventType.PlayerLeft:
                    return new PlayerLeftEvent { Player = new EntityId(r.U32()) };
                case EventType.ContainerState:
                    return new ContainerStateEvent { State = ContainerCodec.ReadState(r) };
                case EventType.ContainerDelta:
                    return new ContainerDeltaEvent { Delta = ContainerCodec.ReadDelta(r) };
                case EventType.ContainerClosed:
                    return new ContainerClosedEvent { ContainerId = r.U32() };
                case EventType.RequestResult:
                    return new RequestResultEvent { RequestId = r.U32(), Result = (OpResult)r.U8() };
                case EventType.MailSpawned:
                    return new MailSpawnedEvent { FirstMailId = r.U32(), Count = r.U16(), Backlog = r.U16() };
                case EventType.Delivered:
                    return new DeliveredEvent
                    {
                        MailId = r.U32(), DestId = r.U16(), Paid = r.U16(), Player = new EntityId(r.U32()), Kind = r.U16(),
                    };
                case EventType.Misdelivered:
                    return new MisdeliveredEvent
                    {
                        MailId = r.U32(), DestId = r.U16(), Penalty = r.U16(), Player = new EntityId(r.U32()), Kind = r.U16(),
                    };
                case EventType.PhaseChanged:
                    return new PhaseChangedEvent
                    {
                        Shift = r.U8(), Phase = (RunPhase)r.U8(), Tick = r.U32(), PhaseEndTick = r.U32(), Quota = r.I32(), Outcome = (RunOutcome)r.U8(),
                    };
                case EventType.ShiftSummary:
                {
                    var s = new ShiftSummary
                    {
                        Shift = r.U8(), Earned = r.I32(), Quota = r.I32(), QuotaMet = r.Bool(), Bonus = r.I32(),
                    };
                    s.DeliveriesByKind = new ushort[r.U8()];
                    for (int i = 0; i < s.DeliveriesByKind.Length; i++) s.DeliveriesByKind[i] = r.U16();
                    s.Misdeliveries = r.U16();
                    s.LateDeliveries = r.U16();
                    s.Mvp = new EntityId(r.U32());
                    s.MvpDeliveries = r.U16();
                    return new ShiftSummaryEvent { Summary = s };
                }
                case EventType.RunResults:
                {
                    var s = new RunResults
                    {
                        Outcome = (RunOutcome)r.U8(), ShiftsCompleted = r.U8(), TotalEarned = r.I32(), Score = r.I32(),
                        Deliveries = r.I32(), Misdeliveries = r.I32(),
                    };
                    int n = r.U8();
                    for (int i = 0; i < n; i++)
                    {
                        s.Players.Add(new PlayerResult
                        {
                            Player = new EntityId(r.U32()), Deliveries = r.U16(), Misdeliveries = r.U16(), Earned = r.I32(), Xp = r.I32(),
                        });
                    }
                    return new RunResultsEvent { Results = s };
                }
                case EventType.PausedChanged:
                    return new PausedChangedEvent { Paused = r.Bool(), Tick = r.U32() };
                case EventType.Attack:
                    return new AttackEvent
                    {
                        Player = new EntityId(r.U32()), Weapon = (Weapon)r.U8(), X = r.F32(), Z = r.F32(), Yaw = r.U16(), Hits = r.U8(),
                    };
                case EventType.Blast:
                    return new BlastEvent { X = r.F32(), Z = r.F32(), Hits = r.U8() };
                case EventType.EnemyDied:
                    return new EnemyDiedEvent
                    {
                        Enemy = new EntityId(r.U32()), Kind = r.U8(), Mega = r.Bool(), X = r.F32(), Z = r.F32(), Killer = new EntityId(r.U32()),
                    };
                case EventType.LootDropped:
                {
                    var e = new LootDroppedEvent
                    {
                        LootId = r.U32(), X = r.F32(), Z = r.F32(), EnemyKind = r.U8(), Mega = r.Bool(), Coins = r.U16(),
                    };
                    ReadLoot(r, e.Items);
                    return e;
                }
                case EventType.LootTaken:
                {
                    var e = new LootTakenEvent { LootId = r.U32(), Player = new EntityId(r.U32()), Coins = r.U16() };
                    ReadLoot(r, e.Items);
                    e.Gone = r.Bool();
                    return e;
                }
                case EventType.LootGone:
                    return new LootGoneEvent { LootId = r.U32() };
                case EventType.RaidChanged:
                    return new RaidChangedEvent { Raid = (RaidState)r.U8(), Tick = r.U32() };
                case EventType.PlayerDied:
                    return new PlayerDiedEvent { Player = new EntityId(r.U32()), RespawnTick = r.U32(), Killer = r.U8() };
                case EventType.PlayerRespawned:
                    return new PlayerRespawnedEvent { Player = new EntityId(r.U32()), ProtectedUntilTick = r.U32() };
                case EventType.PlayerParked:
                    return new PlayerParkedEvent { Destination = r.U16() };
                case EventType.BagDropped:
                    return new BagDroppedEvent
                    {
                        ContainerId = r.U32(), Owner = new EntityId(r.U32()), X = r.F32(), Z = r.F32(), DespawnTick = r.U32(),
                    };
                case EventType.BagGone:
                    return new BagGoneEvent { ContainerId = r.U32(), Returned = r.Bool(), Mail = r.U16() };
                case EventType.PlayerAway:
                    return new PlayerAwayEvent { Player = new EntityId(r.U32()), UntilTick = r.U32() };
                case EventType.PlayerBack:
                    return new PlayerBackEvent { Player = new EntityId(r.U32()) };
                case EventType.DraftOffer:
                {
                    var e = new DraftOfferEvent();
                    int n = r.U8();
                    for (int i = 0; i < n; i++) e.Perks.Add(r.U8());
                    return e;
                }
                case EventType.PerkPicked:
                    return new PerkPickedEvent { Player = new EntityId(r.U32()), Perk = r.U8(), Card = r.U8(), Auto = r.Bool() };
                case EventType.ShopOffer:
                {
                    var e = new ShopOfferEvent { Shift = r.U8() };
                    int n = r.U8();
                    for (int i = 0; i < n; i++) e.Specials.Add(r.U8());
                    return e;
                }
                case EventType.ShopBought:
                    return new ShopBoughtEvent { Player = new EntityId(r.U32()), Item = r.U8(), Price = r.U16() };
                case EventType.ShopSold:
                    return new ShopSoldEvent { Player = new EntityId(r.U32()), Item = r.U16(), Count = r.U16(), Paid = r.I32() };
                case EventType.NodeHarvested:
                    return new NodeHarvestedEvent { Player = new EntityId(r.U32()), Node = r.U16(), HitsLeft = r.U8(), Item = r.U16(), Count = r.U8() };
                case EventType.NodesRegrown:
                    return new NodesRegrownEvent { All = r.Bool() };
                case EventType.ConstructPlaced:
                    return new ConstructPlacedEvent
                    {
                        Player = new EntityId(r.U32()), Building = r.U8(), X = r.U8(), Y = r.U8(), Rotation = r.U8(), Hp = r.U16(), ContainerId = r.U32(),
                    };
                case EventType.ConstructRemoved:
                    return new ConstructRemovedEvent { X = r.U8(), Y = r.U8(), Player = new EntityId(r.U32()), Destroyed = r.Bool() };
                case EventType.ConstructDamaged:
                    return new ConstructDamagedEvent { X = r.U8(), Y = r.U8(), Hp = r.U16() };
                case EventType.ConstructsCleared:
                    return new ConstructsClearedEvent();
                case EventType.VehicleAdded:
                    return new VehicleAddedEvent
                    {
                        Vehicle = new EntityId(r.U32()), Def = r.U8(), X = r.F32(), Z = r.F32(), Heading = r.F32(), CargoContainer = r.U32(), Buyer = new EntityId(r.U32()),
                    };
                case EventType.VehicleMounted:
                    return new VehicleMountedEvent { Vehicle = new EntityId(r.U32()), Player = new EntityId(r.U32()) };
                case EventType.VehicleParked:
                    return new VehicleParkedEvent { Vehicle = new EntityId(r.U32()), X = r.F32(), Z = r.F32(), Heading = r.F32(), Player = new EntityId(r.U32()) };
                case EventType.VehiclesCleared:
                    return new VehiclesClearedEvent();
                case EventType.KitGiven:
                    return new KitGivenEvent { Player = new EntityId(r.U32()), Kit = r.U8(), Team = r.U8() != 0 };
                default:
                    throw new NetFormatException($"unknown event type {(byte)type}");
            }
        }

        private static void WriteLoot(NetWriter w, List<LootItem> items)
        {
            w.U8((byte)items.Count);
            foreach (var i in items) { w.U16(i.Item); w.U8(i.Count); }
        }

        private static void ReadLoot(NetReader r, List<LootItem> items)
        {
            int n = r.U8();
            for (int i = 0; i < n; i++) items.Add(new LootItem { Item = r.U16(), Count = r.U8() });
        }
    }
}
