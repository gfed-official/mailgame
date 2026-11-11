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
                    w.U8(e.Shift); w.U8((byte)e.Phase); w.U32(e.PhaseEndTick); w.I32(e.Quota);
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
                    return new PhaseChangedEvent { Shift = r.U8(), Phase = (RunPhase)r.U8(), PhaseEndTick = r.U32(), Quota = r.I32() };
                default:
                    throw new NetFormatException($"unknown event type {(byte)type}");
            }
        }
    }
}
