using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Net;

// Admission half of the host adapter: the Connected -> Joining -> Admitted state machine (ADR-0011 section 4).
public sealed partial class HostAdapter
{
    /// <summary>
    /// M-1a local admission of the host's own loopback peer: resolves the account through the roster, enqueues the
    /// <c>System</c> <c>PlayerJoin</c> (payload player id from the roster, 0 for a fresh account; the slot) and leaves the peer
    /// <see cref="PeerState.Joining"/>. Call between ticks on the main thread, never from a transport callback. The peer
    /// becomes <see cref="PeerState.Admitted"/> in <see cref="CompleteJoin"/>.
    /// </summary>
    /// <param name="peer">The loopback end's peer handle (<c>LoopbackTransport.Peer</c>).</param>
    /// <param name="slot">The player slot to join into.</param>
    /// <param name="accountKey">The account identity; the M-1a single local account by default.</param>
    /// <returns><see cref="AdmitResult.Ok"/> or the reason nothing changed.</returns>
    /// <example><code>AdmitResult r = host.AdmitLocal(hostEnd.Peer, PlayerSlot.FromValue(0));</code></example>
    public AdmitResult AdmitLocal(PeerHandle peer, PlayerSlot slot, ulong accountKey = LocalAccountKey) =>
        Admit(PeerOrigin.Loopback, peer, slot, accountKey);

    /// <summary>
    /// The admission step shared by <see cref="AdmitLocal"/> and (at M0) the <c>HelloAck{ok}</c> path for remote peers:
    /// <c>Connected</c> (or no row yet) to <c>Joining</c> by enqueueing the <c>System</c> <c>PlayerJoin</c>.
    /// </summary>
    /// <param name="origin">Which transport the handle belongs to.</param>
    /// <param name="peer">The peer handle.</param>
    /// <param name="slot">The player slot to join into.</param>
    /// <param name="accountKey">The account identity.</param>
    /// <returns><see cref="AdmitResult.Ok"/> or the reason nothing changed.</returns>
    /// <exception cref="InvalidOperationException">A remote admission was requested but there is no remote transport (programming error).</exception>
    internal AdmitResult Admit(PeerOrigin origin, PeerHandle peer, PlayerSlot slot, ulong accountKey)
    {
        AdmitResult check = ValidateAdmission(origin, peer, slot, out int index);
        if (check != AdmitResult.Ok)
        {
            return check;
        }

        bool created = index < 0;
        if (created)
        {
            index = Allocate(origin, peer, connectSeen: origin != PeerOrigin.Loopback);
            if (index < 0)
            {
                return AdmitResult.TableFull;
            }
        }

        if (!TryEnqueueJoin(slot, accountKey, out uint joinReqId, out EntityId known))
        {
            if (created)
            {
                _peers[index] = default;
            }

            return AdmitResult.Refused;
        }

        ref PeerEntry entry = ref _peers[index];
        entry.State = PeerState.Joining;
        entry.AccountKey = accountKey;
        entry.Slot = slot;
        entry.PlayerId = EntityId.None;
        entry.JoinReqId = joinReqId;
        entry.JoinPlayerId = known;
        ResetOutbound(index); // a fresh admission starts with empty outbound buffers and counters (TD-062 item 3)
        return AdmitResult.Ok;
    }

    /// <summary>Checks everything about an admission that needs no mutation: remote transport present, slot valid, peer not already joining or admitted, slot free.</summary>
    /// <param name="index">The peer's existing row, or -1 when it has none.</param>
    private AdmitResult ValidateAdmission(PeerOrigin origin, PeerHandle peer, PlayerSlot slot, out int index)
    {
        if (origin == PeerOrigin.Remote && _remote is null)
        {
            throw new InvalidOperationException("There is no remote transport to admit a remote peer on.");
        }

        index = -1;
        if (slot.IsNone || slot.Value >= PlayerSlot.Count)
        {
            return AdmitResult.SlotInvalid;
        }

        index = Find(origin, peer);
        if (index >= 0 && _peers[index].State >= PeerState.Joining)
        {
            return AdmitResult.AlreadyAdmitted;
        }

        for (int i = 0; i < _peers.Length; i++)
        {
            if (_peers[i].State >= PeerState.Joining && _peers[i].Slot == slot)
            {
                return AdmitResult.SlotTaken;
            }
        }

        return AdmitResult.Ok;
    }

    /// <summary>Builds the <c>System</c> <c>PlayerJoin</c> (player id from the roster, 0 for a fresh account) and enqueues it.</summary>
    private bool TryEnqueueJoin(PlayerSlot slot, ulong accountKey, out uint joinReqId, out EntityId known)
    {
        known = _roster.Lookup(accountKey);
        joinReqId = JoinReqIdBase + slot.Value;
        var join = new Op { Kind = OpKind.PlayerJoin, ReqId = joinReqId };
        join.SetPayload(new PlayerJoinPayload { PlayerId = known.Value, Slot = slot.Value });
        return _sim.EnqueueOp(ActorId.System, in join);
    }

    /// <summary>
    /// Finishes a join once the Sim's reply to the adapter's <c>PlayerJoin</c> is delivered (Story 004's <c>Deliver</c> calls
    /// this when it sees the <c>OpReply</c> whose request id is <see cref="JoinReqIdBase"/> plus the slot). On success the peer
    /// maps to <paramref name="playerId"/>, the roster records it and the peer becomes <see cref="PeerState.Admitted"/>; on
    /// failure the peer falls back to <see cref="PeerState.Connected"/> (counted in <see cref="JoinRefusedCount"/>).
    /// Called from inside <c>Deliver</c>, so it only updates the table (no Sim call, no send).
    /// </summary>
    /// <param name="joinReqId">The reply's request id.</param>
    /// <param name="reason">The reply's reason (0 = ok).</param>
    /// <param name="playerId">The id the Sim allocated; required (non-<c>None</c>) when <paramref name="reason"/> is 0.</param>
    /// <returns>True when a joining peer matched the request id.</returns>
    internal bool CompleteJoin(uint joinReqId, ushort reason, EntityId playerId)
    {
        for (int i = 0; i < _peers.Length; i++)
        {
            if (_peers[i].State != PeerState.Joining || _peers[i].JoinReqId != joinReqId)
            {
                continue;
            }

            ref PeerEntry entry = ref _peers[i];
            if (reason == 0 && !playerId.IsNone)
            {
                entry.PlayerId = playerId;
                entry.State = PeerState.Admitted;
                _roster.Record(entry.AccountKey, playerId);
            }
            else
            {
                entry.State = PeerState.Connected;
                entry.Slot = PlayerSlot.None;
                entry.JoinReqId = 0;
                JoinRefusedCount++;
            }

            return true;
        }

        return false;
    }
}
