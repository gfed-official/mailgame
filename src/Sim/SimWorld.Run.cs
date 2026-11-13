using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// The run state machine (doc 01 §1): Lobby → Prep → Delivery → Payday → next shift's Prep, or
    /// Results once the quota is missed or the last shift is paid, then back to the Lobby.
    /// </summary>
    public sealed partial class SimWorld
    {
        private readonly List<uint> _entryScratch = new List<uint>();
        private uint _emptyTicks;
        private uint _nextJoinOrder = 1;

        /// <summary>Shifts in a run: one per entry of baseQuota.</summary>
        public int ShiftCount => Content.Balance.GetArray("baseQuota").Length;

        /// <summary>Join-in-progress is allowed only in the Lobby and during Prep (doc 06 §5).</summary>
        public bool AcceptsPlayers =>
            Settings.QuickStart || Run.Phase == RunPhase.Lobby || Run.Phase == RunPhase.Prep;

        private void StepRun()
        {
            if (StepEmptyServer()) return;
            switch (Run.Phase)
            {
                case RunPhase.Lobby:
                    if (Settings.QuickStart && _players.Count > 0) BeginRun();
                    break;
                case RunPhase.Prep:
                    if (Tick >= Run.PhaseEndTick || AllReady()) BeginDelivery();
                    break;
                case RunPhase.Delivery:
                    if (Tick >= Run.PhaseEndTick) BeginPayday();
                    break;
                case RunPhase.Payday:
                    if (Tick >= Run.PhaseEndTick) EndPayday();
                    break;
                case RunPhase.Results:
                    if (Run.PhaseEndTick != 0 && Tick >= Run.PhaseEndTick) ReturnToLobby();
                    break;
            }
        }

        /// <summary>
        /// Doc 01 §7: when every player has disconnected the run ends without Results once the
        /// grace period is over. Returns true on the tick that happens.
        /// </summary>
        private bool StepEmptyServer()
        {
            if (_players.Count > 0 || Run.Phase == RunPhase.Lobby)
            {
                _emptyTicks = 0;
                return false;
            }
            if (++_emptyTicks < SimConstants.SecondsToTicks(Content.Balance.GetDouble("emptyServerGraceSeconds"))) return false;
            _emptyTicks = 0;
            ReturnToLobby();
            return true;
        }

        private bool AllReady()
        {
            if (_players.Count == 0) return false;
            foreach (var p in _players) if (!p.Ready) return false;
            return true;
        }

        // --- transitions ----------------------------------------------------------------------

        private void EnterPhase(RunPhase phase, double seconds)
        {
            Run.Phase = phase;
            Run.PhaseStartTick = Tick;
            Run.PhaseEndTick = seconds > 0 ? Tick + SimConstants.SecondsToTicks(seconds) : 0;
            foreach (var p in _players) p.Ready = false;
            Events.Add(new PhaseChangedEvent
            {
                Shift = (byte)Run.Shift, Phase = Run.Phase, PhaseEndTick = Run.PhaseEndTick, Quota = Run.Quota, Outcome = Run.Outcome,
            });
        }

        private void BeginRun()
        {
            Run.RunNumber++;
            Run.Shift = 1;
            Run.ShiftsCompleted = 0;
            Run.Outcome = RunOutcome.None;
            Run.LastSummary = null;
            Run.Results = null;
            Economy.Reset();
            foreach (var p in _players)
            {
                p.Deliveries = 0;
                p.Misdeliveries = 0;
                p.Earned = 0;
                p.ShiftDeliveries = 0;
            }
            BeginShift();
        }

        private void BeginShift()
        {
            if (Settings.QuickStart) BeginDelivery();
            else BeginPrep();
        }

        private void BeginPrep()
        {
            var b = Content.Balance;
            Economy.BeginShift();
            // A preview for the HUD; the binding quota is sampled when Delivery starts.
            Run.Quota = QuotaFormula.Quota(b, Run.Shift, Math.Max(1, _players.Count));
            EnterPhase(RunPhase.Prep, b.GetForShift("prepSeconds", Run.Shift));
        }

        private void BeginDelivery()
        {
            var b = Content.Balance;
            Run.PlayerCount = Math.Max(1, _players.Count);
            Run.Quota = QuotaFormula.Quota(b, Run.Shift, Run.PlayerCount);
            Economy.BeginShift();
            foreach (var p in _players) p.ShiftDeliveries = 0;
            double seconds = b.GetForShift("deliverySeconds", Run.Shift);
            int spawnValue = (int)Math.Round(Run.Quota * Content.MailMix.SpawnOverhead);
            Spawner.BeginShift(Run.Shift, Tick + 1, SimConstants.SecondsToTicks(seconds), spawnValue);
            EnterPhase(RunPhase.Delivery, seconds);
        }

        /// <summary>Doc 01 §2.4: the quota check, the shift bonus and the summary the clients show.</summary>
        private void BeginPayday()
        {
            Spawner.EndShift();
            var summary = new ShiftSummary
            {
                Shift = (byte)Run.Shift,
                Earned = Economy.ShiftEarnings,
                Quota = Run.Quota,
                QuotaMet = Economy.ShiftEarnings >= Run.Quota,
                Misdeliveries = Clamp16(Economy.ShiftMisdeliveries),
                LateDeliveries = Clamp16(Economy.ShiftLateDeliveries),
                DeliveriesByKind = new ushort[Economy.ShiftDeliveriesByKind.Length],
            };
            for (int i = 0; i < summary.DeliveriesByKind.Length; i++)
                summary.DeliveriesByKind[i] = Clamp16(Economy.ShiftDeliveriesByKind[i]);
            foreach (var p in _players)
            {
                if (p.ShiftDeliveries <= summary.MvpDeliveries) continue;
                summary.Mvp = p.Id;
                summary.MvpDeliveries = Clamp16(p.ShiftDeliveries);
            }
            summary.Bonus = Economy.PayShiftBonus();
            if (summary.QuotaMet) Run.ShiftsCompleted++;
            Run.LastSummary = summary;
            Events.Add(new ShiftSummaryEvent { Summary = summary });
            EnterPhase(RunPhase.Payday, Content.Balance.GetDouble("paydaySeconds"));
        }

        private void EndPayday()
        {
            if (!Run.LastSummary!.QuotaMet) BeginResults(RunOutcome.QuotaMissed);
            else if (Run.Shift >= ShiftCount) BeginResults(RunOutcome.Victory);
            else
            {
                Run.Shift++;
                BeginShift();
            }
        }

        private void BeginResults(RunOutcome outcome)
        {
            var b = Content.Balance;
            Run.Outcome = outcome;
            var results = new RunResults
            {
                Outcome = outcome,
                ShiftsCompleted = (byte)Run.ShiftsCompleted,
                TotalEarned = Economy.TotalEarned,
                Score = Economy.TotalEarned,
                Deliveries = Economy.TotalDeliveries,
                Misdeliveries = Economy.TotalMisdeliveries,
            };
            int xp = QuotaFormula.Xp(b, Run.ShiftsCompleted, outcome == RunOutcome.Victory, Economy.TotalDeliveries, _players.Count);
            foreach (var p in _players)
            {
                results.Players.Add(new PlayerResult
                {
                    Player = p.Id, Deliveries = Clamp16(p.Deliveries), Misdeliveries = Clamp16(p.Misdeliveries), Earned = p.Earned, Xp = xp,
                });
            }
            Run.Results = results;
            Events.Add(new RunResultsEvent { Results = results });
            // Results waits for the host; a QuickStart server (tests, soak) moves on by itself.
            EnterPhase(RunPhase.Results, Settings.QuickStart ? b.GetDouble("paydaySeconds") : 0);
        }

        /// <summary>Ends the run: all mail and money are gone, the next run starts from the same seed.</summary>
        private void ReturnToLobby()
        {
            ClearMail(Intake, everything: true);
            foreach (var p in _players)
            {
                ClearMail(p.Hotbar, everything: false);
                ClearMail(p.Inventory, everything: false);
            }
            Spawner.Reset();
            Economy.Reset();
            Run.Shift = 1;
            Run.Quota = 0;
            Run.PlayerCount = 0;
            Run.ShiftsCompleted = 0;
            Run.Outcome = RunOutcome.None;
            Run.LastSummary = null;
            Run.Results = null;
            EnterPhase(RunPhase.Lobby, 0);
        }

        private void ClearMail(GridContainer container, bool everything)
        {
            _entryScratch.Clear();
            foreach (var e in container.Entries)
                if (everything || e.Stack.Kind == StackKind.Mail) _entryScratch.Add(e.EntryId);
            if (_entryScratch.Count == 0) return;
            foreach (uint id in _entryScratch) container.Remove(id);
            CommitContainer(container.Id);
        }

        private void UpdateHostAfterLeave(EntityId left)
        {
            if (Run.Host != left) return;
            Player? next = null;
            foreach (var p in _players)
                if (next == null || p.JoinOrder < next.JoinOrder) next = p;
            Run.Host = next?.Id ?? EntityId.None;
        }

        // --- requests ---------------------------------------------------------------------------

        private OpResult StartRunOp(Player player)
        {
            if (Run.Phase != RunPhase.Lobby) return OpResult.WrongPhase;
            if (player.Id != Run.Host) return OpResult.NotHost;
            BeginRun();
            return OpResult.Ok;
        }

        private OpResult SetReadyOp(Player player, in SimRequest r)
        {
            if (Run.Phase != RunPhase.Lobby && Run.Phase != RunPhase.Prep) return OpResult.WrongPhase;
            player.Ready = r.Count != 0;
            return OpResult.Ok;
        }

        private OpResult ContinueRunOp(Player player)
        {
            if (Run.Phase != RunPhase.Results) return OpResult.WrongPhase;
            if (player.Id != Run.Host) return OpResult.NotHost;
            ReturnToLobby();
            return OpResult.Ok;
        }

        // --- pause ------------------------------------------------------------------------------

        /// <summary>
        /// Doc 01 §1: "In a solo run the host may pause". Any phase; a multiplayer server never
        /// pauses, so the pause also ends when a second player joins or the host leaves.
        /// </summary>
        private OpResult SetPausedOp(Player player, in SimRequest r)
        {
            if (player.Id != Run.Host) return OpResult.NotHost;
            bool pause = r.Count != 0;
            if (pause && _players.Count != 1) return OpResult.NotSolo;
            SetPaused(pause);
            return OpResult.Ok;
        }

        private void SetPaused(bool paused)
        {
            if (Paused == paused) return;
            Paused = paused;
            Events.Add(new PausedChangedEvent { Paused = paused, Tick = Tick });
        }

        /// <summary>
        /// The only thing a paused server does: answer pause requests. Every other request waits
        /// its turn in the queue and is applied on the first tick after the pause.
        /// </summary>
        private void ApplyPauseRequests()
        {
            for (int i = 0; i < _requests.Count && Paused;)
            {
                var pending = _requests[i];
                if (pending.Request.Kind != RequestKind.SetPaused) { i++; continue; }
                _requests.RemoveAt(i);
                var player = GetPlayer(pending.Player);
                if (player == null) continue;
                Events.Add(new RequestResultEvent
                {
                    Audience = player.Id, RequestId = pending.Request.RequestId, Result = SetPausedOp(player, pending.Request),
                });
            }
        }

        private static ushort Clamp16(int value) => (ushort)Math.Clamp(value, 0, ushort.MaxValue);
    }
}
