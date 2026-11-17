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
                    w.U32(e.Player.Value);
                    break;
                case PlayerParkedEvent e:
                    w.U8((byte)EventType.PlayerParked);
                    w.U16(e.Destination);
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
                    return new PlayerRespawnedEvent { Player = new EntityId(r.U32()) };
                case EventType.PlayerParked:
                    return new PlayerParkedEvent { Destination = r.U16() };
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
