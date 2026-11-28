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
        WorldChanged = 44,
        BeltState = 45,
        BeltItemAdded = 46,
        BeltItemMoved = 47,
        BeltItemRemoved = 48,
        BeltChecksum = 49,
        BeltPushed = 50,
        ConstructConfigured = 51,
        SorterStatus = 52,
        InserterSwung = 53,
        PipeCapsule = 54,
        PipeCapsuleRemoved = 55,
        VehicleFuel = 56,
        VehicleRemoved = 57,
        Driver = 58,
        Ping = 59,
        MailWaiting = 60,
        TurretFired = 61,
        TurretOperated = 62,
        Ruin = 63,
        EnemyShot = 64,
        ConstructPushed = 65,
        PostOfficeDamaged = 66,
        Repaired = 67,
        CursedRaid = 68,
        MegaSighted = 69,
        LostParcel = 70,
        SlingshotFired = 71,
        Forecast = 72,
        RunEvent = 73,
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
                    w.U32(e.MailId); w.U16(e.DestId); w.U16(e.Paid); w.U32(e.Player.Value); w.U16(e.Kind); w.U8((byte)e.Machine);
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
                    w.U32(s.MvpDamage.Value); w.U16(s.MvpDamageDealt); w.U32(s.MvpBuilt.Value); w.U16(s.MvpBuiltCount); w.U16(s.ConstructsLost);
                    w.U8((byte)s.MisdeliveredAt.Length);
                    for (int i = 0; i < s.MisdeliveredAt.Length; i++) { w.U16(s.MisdeliveredAt[i]); w.U16(s.MisdeliveredCounts[i]); }
                    break;
                }
                case RunResultsEvent e:
                {
                    var s = e.Results;
                    w.U8((byte)EventType.RunResults);
                    w.U8((byte)s.Outcome); w.U8(s.ShiftsCompleted); w.U8(s.FirstShift); w.U8(s.LastShift); w.U8(s.QuotaWaived ? (byte)1 : (byte)0);
                    w.I32(s.TotalEarned); w.I32(s.Score);
                    w.I32(s.Deliveries); w.I32(s.Misdeliveries); w.I32(s.ConstructsLost);
                    w.U8((byte)s.Players.Count);
                    foreach (var p in s.Players)
                    {
                        w.U32(p.Player.Value); w.U16(p.Deliveries); w.U16(p.Misdeliveries); w.I32(p.Earned); w.I32(p.Xp);
                        w.U16(p.Kills); w.U16(p.Built); w.I32(p.Damage);
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
                    w.U8((byte)e.Raid); w.U32(e.Tick); w.U8((byte)e.EdgeA); w.U8((byte)e.EdgeB);
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
                    w.Bool(e.Reroll);
                    w.U8(e.RerollsLeft);
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
                    w.U32(e.Player.Value); w.U8(e.Building); w.U16(e.X); w.U16(e.Y); w.U8(e.Rotation); w.U16(e.Hp); w.U32(e.ContainerId);
                    break;
                case ConstructRemovedEvent e:
                    w.U8((byte)EventType.ConstructRemoved);
                    w.U16(e.X); w.U16(e.Y); w.U32(e.Player.Value); w.Bool(e.Destroyed);
                    break;
                case ConstructDamagedEvent e:
                    w.U8((byte)EventType.ConstructDamaged);
                    w.U16(e.X); w.U16(e.Y); w.U16(e.Hp);
                    break;
                case ConstructsClearedEvent _:
                    w.U8((byte)EventType.ConstructsCleared);
                    break;
                case BeltStateEvent e:
                    w.U8((byte)EventType.BeltState);
                    w.U16(e.X); w.U16(e.Y); w.U32(e.Tick); w.U16((ushort)e.Items.Count);
                    foreach (var it in e.Items) WriteBeltItem(w, it);
                    break;
                case BeltItemAddedEvent e:
                    w.U8((byte)EventType.BeltItemAdded);
                    w.U16(e.X); w.U16(e.Y); WriteBeltItem(w, e.Item);
                    break;
                case BeltItemMovedEvent e:
                    w.U8((byte)EventType.BeltItemMoved);
                    w.U32(e.MailId); w.U16(e.X); w.U16(e.Y); w.U8(e.Lane);
                    break;
                case BeltItemRemovedEvent e:
                    w.U8((byte)EventType.BeltItemRemoved);
                    w.U32(e.MailId);
                    break;
                case BeltChecksumEvent e:
                    w.U8((byte)EventType.BeltChecksum);
                    w.U16(e.X); w.U16(e.Y); w.U16(e.Count); w.U32(e.Hash);
                    break;
                case BeltPushedEvent e:
                    w.U8((byte)EventType.BeltPushed);
                    w.U16(e.X); w.U16(e.Y); w.U16(e.Ticks); w.U32(e.Player.Value);
                    break;
                case ConstructConfiguredEvent e:
                    w.U8((byte)EventType.ConstructConfigured);
                    w.U16(e.X); w.U16(e.Y); w.U32(e.Player.Value); w.U8((byte)e.Filters.Count);
                    foreach (ushort f in e.Filters) w.U16(f);
                    break;
                case PipeCapsuleEvent e:
                    w.U8((byte)EventType.PipeCapsule);
                    WriteBeltItem(w, e.Item); w.U16(e.FromX); w.U16(e.FromY); w.U16(e.OutX); w.U16(e.OutY);
                    break;
                case PipeCapsuleRemovedEvent e:
                    w.U8((byte)EventType.PipeCapsuleRemoved);
                    w.U32(e.MailId);
                    break;
                case VehicleRemovedEvent e:
                    w.U8((byte)EventType.VehicleRemoved);
                    w.U32(e.Vehicle.Value);
                    break;
                case DriverEvent e:
                    w.U8((byte)EventType.Driver);
                    w.U16(e.X); w.U16(e.Y); w.U8((byte)e.State); w.U32(e.Vehicle.Value); w.U16(e.House); w.U8(e.Houses); w.U8(e.At); w.U16(e.Delivered); w.U32(e.Player.Value); w.Bool(e.ToPort); w.U16(e.PortX); w.U16(e.PortY);
                    break;
                case TurretFiredEvent e:
                    w.U8((byte)EventType.TurretFired);
                    w.U16(e.X); w.U16(e.Y); w.F32(e.TargetX); w.F32(e.TargetZ);
                    break;
                case RuinEvent e:
                    w.U8((byte)EventType.Ruin);
                    w.U16(e.X); w.U16(e.Y); w.U8(e.Building); w.U8(e.Rotation); w.Bool(e.Gone); w.Bool(e.Rebuilt); w.U32(e.Player.Value);
                    break;
                case EnemyShotEvent e:
                    w.U8((byte)EventType.EnemyShot);
                    w.U8((byte)e.Kind); w.F32(e.FromX); w.F32(e.FromZ); w.F32(e.ToX); w.F32(e.ToZ); w.Bool(e.AtConstruct);
                    break;
                case PostOfficeDamagedEvent e:
                    w.U8((byte)EventType.PostOfficeDamaged);
                    w.U16(e.Hp); w.U8(e.Enemy);
                    break;
                case CursedRaidEvent e:
                    w.U8((byte)EventType.CursedRaid);
                    w.U16(e.DestId); w.U32(e.Player.Value); w.U16(e.Kind); w.U8(e.Raiders);
                    break;
                case MegaSightedEvent e:
                    w.U8((byte)EventType.MegaSighted);
                    w.U32(e.Enemy.Value); w.U8(e.Kind); w.U8((byte)e.Edge);
                    break;
                case SlingshotFiredEvent e:
                    w.U8((byte)EventType.SlingshotFired);
                    w.U32(e.Player.Value); w.F32(e.FromX); w.F32(e.FromZ); w.F32(e.ToX); w.F32(e.ToZ); w.U8((byte)e.Hit); w.U8(e.Loaded);
                    break;
                case LostParcelEvent e:
                    w.U8((byte)EventType.LostParcel);
                    w.U8(e.Enemy); w.U16(e.Kind); w.U32(e.Address.Key); w.U8(e.Address.Unit); w.U16(e.Value); w.U32(e.Bag);
                    break;
                case RepairedEvent e:
                    w.U8((byte)EventType.Repaired);
                    w.U16(e.X); w.U16(e.Y); w.U32(e.Player.Value); w.Bool(e.PostOffice);
                    break;
                case ConstructPushedEvent e:
                    w.U8((byte)EventType.ConstructPushed);
                    w.U16(e.FromX); w.U16(e.FromY); w.U16(e.X); w.U16(e.Y); w.U8(e.Enemy);
                    break;
                case TurretOperatedEvent e:
                    w.U8((byte)EventType.TurretOperated);
                    w.U16(e.X); w.U16(e.Y); w.U32(e.Player.Value);
                    break;
                case RunEventEvent e:
                    w.U8((byte)EventType.RunEvent);
                    w.U8(e.Event); w.U8(e.Shift); w.U8((byte)e.Stage); w.U8(e.Street); w.I32(e.Amount);
                    break;
                case ForecastEvent e:
                    w.U8((byte)EventType.Forecast);
                    w.U8(e.Shift);
                    w.U16((ushort)e.Streets.Count);
                    foreach (var (street, count) in e.Streets) { w.U16(street); w.U16(count); }
                    break;
                case MailWaitingEvent e:
                    w.U8((byte)EventType.MailWaiting);
                    w.U16((ushort)e.Houses.Count);
                    foreach (var (dest, count) in e.Houses) { w.U16(dest); w.U16(count); }
                    break;
                case PingEvent e:
                    w.U8((byte)EventType.Ping);
                    w.U32(e.Player.Value); w.F32(e.X); w.F32(e.Z); w.U8((byte)e.Kind); w.U32(e.ExpiresTick);
                    break;
                case VehicleFuelEvent e:
                    w.U8((byte)EventType.VehicleFuel);
                    w.U32(e.Vehicle.Value); w.F32(e.Seconds);
                    break;
                case InserterSwungEvent e:
                    w.U8((byte)EventType.InserterSwung);
                    w.U16(e.X); w.U16(e.Y); w.U16(e.Kind);
                    break;
                case SorterStatusEvent e:
                    w.U8((byte)EventType.SorterStatus);
                    w.U16(e.X); w.U16(e.Y); w.U8(e.Buffered); w.U16(e.Sorted); w.U16(e.Overflowed); w.U8((byte)e.Matches.Count);
                    foreach (ushort m in e.Matches) w.U16(m);
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
                case WorldChangedEvent e:
                    w.U8((byte)EventType.WorldChanged);
                    w.Str(e.MapId);
                    w.U32(e.Seed);
                    w.U64(e.MapHash);
                    w.U8((byte)e.ChestContainerIds.Count);
                    foreach (uint id in e.ChestContainerIds) w.U32(id);
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
                        MailId = r.U32(), DestId = r.U16(), Paid = r.U16(), Player = new EntityId(r.U32()), Kind = r.U16(), Machine = (DeliveryMachine)r.U8(),
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
                    s.MvpDamage = new EntityId(r.U32());
                    s.MvpDamageDealt = r.U16();
                    s.MvpBuilt = new EntityId(r.U32());
                    s.MvpBuiltCount = r.U16();
                    s.ConstructsLost = r.U16();
                    int wrong = System.Math.Min((int)r.U8(), ShiftSummary.MaxMisdeliveredAt);
                    s.MisdeliveredAt = new ushort[wrong];
                    s.MisdeliveredCounts = new ushort[wrong];
                    for (int i = 0; i < wrong; i++) { s.MisdeliveredAt[i] = r.U16(); s.MisdeliveredCounts[i] = r.U16(); }
                    return new ShiftSummaryEvent { Summary = s };
                }
                case EventType.RunResults:
                {
                    var s = new RunResults
                    {
                        Outcome = (RunOutcome)r.U8(), ShiftsCompleted = r.U8(), FirstShift = r.U8(), LastShift = r.U8(), QuotaWaived = r.U8() != 0,
                        TotalEarned = r.I32(), Score = r.I32(),
                        Deliveries = r.I32(), Misdeliveries = r.I32(), ConstructsLost = r.I32(),
                    };
                    int n = r.U8();
                    for (int i = 0; i < n; i++)
                    {
                        s.Players.Add(new PlayerResult
                        {
                            Player = new EntityId(r.U32()), Deliveries = r.U16(), Misdeliveries = r.U16(), Earned = r.I32(), Xp = r.I32(),
                            Kills = r.U16(), Built = r.U16(), Damage = r.I32(),
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
                    return new RaidChangedEvent { Raid = (RaidState)r.U8(), Tick = r.U32(), EdgeA = (sbyte)r.U8(), EdgeB = (sbyte)r.U8() };
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
                    e.Reroll = r.Bool();
                    e.RerollsLeft = r.U8();
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
                        Player = new EntityId(r.U32()), Building = r.U8(), X = r.U16(), Y = r.U16(), Rotation = r.U8(), Hp = r.U16(), ContainerId = r.U32(),
                    };
                case EventType.ConstructRemoved:
                    return new ConstructRemovedEvent { X = r.U16(), Y = r.U16(), Player = new EntityId(r.U32()), Destroyed = r.Bool() };
                case EventType.ConstructDamaged:
                    return new ConstructDamagedEvent { X = r.U16(), Y = r.U16(), Hp = r.U16() };
                case EventType.ConstructsCleared:
                    return new ConstructsClearedEvent();
                case EventType.BeltState:
                {
                    var e = new BeltStateEvent { X = r.U16(), Y = r.U16(), Tick = r.U32() };
                    int n = r.U16();
                    for (int i = 0; i < n; i++) e.Items.Add(ReadBeltItem(r));
                    return e;
                }
                case EventType.BeltItemAdded:
                    return new BeltItemAddedEvent { X = r.U16(), Y = r.U16(), Item = ReadBeltItem(r) };
                case EventType.BeltItemMoved:
                    return new BeltItemMovedEvent { MailId = r.U32(), X = r.U16(), Y = r.U16(), Lane = r.U8() };
                case EventType.BeltItemRemoved:
                    return new BeltItemRemovedEvent { MailId = r.U32() };
                case EventType.BeltChecksum:
                    return new BeltChecksumEvent { X = r.U16(), Y = r.U16(), Count = r.U16(), Hash = r.U32() };
                case EventType.BeltPushed:
                    return new BeltPushedEvent { X = r.U16(), Y = r.U16(), Ticks = r.U16(), Player = new EntityId(r.U32()) };
                case EventType.ConstructConfigured:
                {
                    var e = new ConstructConfiguredEvent { X = r.U16(), Y = r.U16(), Player = new EntityId(r.U32()) };
                    int n = r.U8();
                    for (int i = 0; i < n; i++) e.Filters.Add(r.U16());
                    return e;
                }
                case EventType.PipeCapsule:
                    return new PipeCapsuleEvent { Item = ReadBeltItem(r), FromX = r.U16(), FromY = r.U16(), OutX = r.U16(), OutY = r.U16() };
                case EventType.PipeCapsuleRemoved:
                    return new PipeCapsuleRemovedEvent { MailId = r.U32() };
                case EventType.VehicleRemoved:
                    return new VehicleRemovedEvent { Vehicle = new EntityId(r.U32()) };
                case EventType.Driver:
                    return new DriverEvent
                    {
                        X = r.U16(), Y = r.U16(), State = (DriverState)r.U8(), Vehicle = new EntityId(r.U32()), House = r.U16(), Houses = r.U8(), At = r.U8(),
                        Delivered = r.U16(), Player = new EntityId(r.U32()), ToPort = r.Bool(), PortX = r.U16(), PortY = r.U16(),
                    };
                case EventType.TurretFired:
                    return new TurretFiredEvent { X = r.U16(), Y = r.U16(), TargetX = r.F32(), TargetZ = r.F32() };
                case EventType.Ruin:
                    return new RuinEvent { X = r.U16(), Y = r.U16(), Building = r.U8(), Rotation = r.U8(), Gone = r.Bool(), Rebuilt = r.Bool(), Player = new EntityId(r.U32()) };
                case EventType.EnemyShot:
                    return new EnemyShotEvent { Kind = (EnemyShotKind)r.U8(), FromX = r.F32(), FromZ = r.F32(), ToX = r.F32(), ToZ = r.F32(), AtConstruct = r.Bool() };
                case EventType.PostOfficeDamaged:
                    return new PostOfficeDamagedEvent { Hp = r.U16(), Enemy = r.U8() };
                case EventType.CursedRaid:
                    return new CursedRaidEvent { DestId = r.U16(), Player = new EntityId(r.U32()), Kind = r.U16(), Raiders = r.U8() };
                case EventType.MegaSighted:
                    return new MegaSightedEvent { Enemy = new EntityId(r.U32()), Kind = r.U8(), Edge = (sbyte)r.U8() };
                case EventType.SlingshotFired:
                    return new SlingshotFiredEvent { Player = new EntityId(r.U32()), FromX = r.F32(), FromZ = r.F32(), ToX = r.F32(), ToZ = r.F32(), Hit = (SlingHit)r.U8(), Loaded = r.U8() };
                case EventType.LostParcel:
                    return new LostParcelEvent { Enemy = r.U8(), Kind = r.U16(), Address = new PerformativeMail.Sim.World.AddressId(r.U32(), r.U8()), Value = r.U16(), Bag = r.U32() };
                case EventType.Repaired:
                    return new RepairedEvent { X = r.U16(), Y = r.U16(), Player = new EntityId(r.U32()), PostOffice = r.Bool() };
                case EventType.ConstructPushed:
                    return new ConstructPushedEvent { FromX = r.U16(), FromY = r.U16(), X = r.U16(), Y = r.U16(), Enemy = r.U8() };
                case EventType.TurretOperated:
                    return new TurretOperatedEvent { X = r.U16(), Y = r.U16(), Player = new EntityId(r.U32()) };
                case EventType.RunEvent:
                    return new RunEventEvent { Event = r.U8(), Shift = r.U8(), Stage = (RunEventStage)r.U8(), Street = r.U8(), Amount = r.I32() };
                case EventType.Forecast:
                {
                    var e = new ForecastEvent { Shift = r.U8() };
                    int n = r.U16();
                    for (int i = 0; i < n; i++) e.Streets.Add((r.U16(), r.U16()));
                    return e;
                }
                case EventType.MailWaiting:
                {
                    var e = new MailWaitingEvent();
                    int n = r.U16();
                    for (int i = 0; i < n; i++) e.Houses.Add((r.U16(), r.U16()));
                    return e;
                }
                case EventType.Ping:
                    return new PingEvent { Player = new EntityId(r.U32()), X = r.F32(), Z = r.F32(), Kind = (PingKind)r.U8(), ExpiresTick = r.U32() };
                case EventType.VehicleFuel:
                    return new VehicleFuelEvent { Vehicle = new EntityId(r.U32()), Seconds = r.F32() };
                case EventType.InserterSwung:
                    return new InserterSwungEvent { X = r.U16(), Y = r.U16(), Kind = r.U16() };
                case EventType.SorterStatus:
                {
                    var e = new SorterStatusEvent { X = r.U16(), Y = r.U16(), Buffered = r.U8(), Sorted = r.U16(), Overflowed = r.U16() };
                    int n = r.U8();
                    for (int i = 0; i < n; i++) e.Matches.Add(r.U16());
                    return e;
                }
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
                case EventType.WorldChanged:
                {
                    var e = new WorldChangedEvent { MapId = r.Str(), Seed = r.U32(), MapHash = r.U64() };
                    int n = r.U8();
                    for (int i = 0; i < n; i++) e.ChestContainerIds.Add(r.U32());
                    return e;
                }
                case EventType.KitGiven:
                    return new KitGivenEvent { Player = new EntityId(r.U32()), Kit = r.U8(), Team = r.U8() != 0 };
                default:
                    throw new NetFormatException($"unknown event type {(byte)type}");
            }
        }

        private static void WriteBeltItem(NetWriter w, in BeltItemInfo it)
        {
            w.U32(it.MailId); w.U16(it.Kind); w.U32(it.AddressKey); w.U8(it.Unit); w.U8(it.Lane); w.F32(it.Pos);
        }

        private static BeltItemInfo ReadBeltItem(NetReader r) =>
            new BeltItemInfo { MailId = r.U32(), Kind = r.U16(), AddressKey = r.U32(), Unit = r.U8(), Lane = r.U8(), Pos = r.F32() };

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
