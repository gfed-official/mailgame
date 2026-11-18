using System.Collections.Generic;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// The disconnect grace (doc 06 §6): a player who loses their connection during a run stays in
    /// it for disconnectGraceSeconds, frozen, unhurt and with their pockets on them. Whoever
    /// connects with the same account id in that time is that player again; otherwise the body
    /// drops a death bag and is gone.
    /// </summary>
    public sealed partial class SimWorld
    {
        private readonly List<Player> _awayScratch = new List<Player>();

        /// <summary>The players who are connected: the ones who are counted, waited for and asked.</summary>
        public int PresentCount
        {
            get
            {
                int n = 0;
                foreach (var p in _players)
                    if (!p.Away) n++;
                return n;
            }
        }

        /// <summary>
        /// A player's connection is gone. In the Lobby they have left; during a run they are kept,
        /// away, and their seat with them. Returns true if they are kept.
        /// </summary>
        public bool Disconnect(EntityId id)
        {
            var p = GetPlayer(id);
            if (p == null) return false;
            if (Run.Phase == RunPhase.Lobby)
            {
                RemovePlayer(id);
                return false;
            }
            if (p.Away) return true;
            CloseExternal(p, notify: false);
            p.Inputs.Clear();
            p.Ready = false;
            p.AwayUntilTick = Tick + SimConstants.SecondsToTicks(Content.Balance.GetDouble("disconnectGraceSeconds"));
            _requests.RemoveAll(r => r.Player == id);
            foreach (var e in _enemies)
                if (e.Target == id) e.Target = EntityId.None;
            UpdateHost();
            // Whoever paused is not there to resume.
            SetPaused(false);
            Events.Add(new PlayerAwayEvent { Player = id, UntilTick = p.AwayUntilTick });
            return true;
        }

        /// <summary>The player who is away and said this account id when they joined; null if there is none.</summary>
        public Player? FindAway(ulong account)
        {
            if (account == 0) return null;
            foreach (var p in _players)
                if (p.Away && p.Account == account) return p;
            return null;
        }

        /// <summary>
        /// The player is connected again and goes on where they stood. Somebody who comes back
        /// into a Payday or the Results is told what they were not there to hear.
        /// </summary>
        public void Reconnect(Player p)
        {
            if (!p.Away) return;
            p.AwayUntilTick = 0;
            // The new connection counts its inputs from the start.
            p.Inputs.Clear();
            p.LastQueuedSeq = p.LastProcessedSeq = 0;
            p.InputCredit = 1f;
            UpdateHost();
            Events.Add(new PlayerBackEvent { Player = p.Id });
            if (Run.Phase == RunPhase.Payday && Run.LastSummary != null)
                Events.Add(new ShiftSummaryEvent { Audience = p.Id, Summary = Run.LastSummary });
            if (Run.Phase == RunPhase.Results && Run.Results != null)
                Events.Add(new RunResultsEvent { Audience = p.Id, Results = Run.Results });
        }

        /// <summary>Whose grace is over drops what they carry in a bag where they stand, and is gone.</summary>
        private void StepAway()
        {
            _awayScratch.Clear();
            foreach (var p in _players)
                if (p.Away && Tick >= p.AwayUntilTick) _awayScratch.Add(p);
            foreach (var p in _awayScratch)
            {
                DropBag(p);
                RemovePlayer(p.Id);
            }
        }

        private void RemoveAway()
        {
            _awayScratch.Clear();
            foreach (var p in _players)
                if (p.Away) _awayScratch.Add(p);
            foreach (var p in _awayScratch) RemovePlayer(p.Id);
        }
    }
}
