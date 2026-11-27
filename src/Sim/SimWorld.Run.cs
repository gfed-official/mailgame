using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// The run state machine (doc 01 §1): Lobby → Generating → Prep → Delivery → Payday → Draft →
    /// next shift's Prep, or Results once the quota is missed or the last shift is paid, then back
    /// to the Lobby.
    /// </summary>
    public sealed partial class SimWorld
    {
        private readonly List<uint> _entryScratch = new List<uint>();
        private uint _emptyTicks;
        private uint _nextJoinOrder = 1;

        /// <summary>Shifts in a run: one per entry of baseQuota.</summary>
        public int ShiftCount => RunModes.ShiftCount(Content, Run.Mode);

        /// <summary>Join-in-progress is allowed only in the Lobby and during Prep (doc 06 §5).</summary>
        public bool AcceptsPlayers =>
            Settings.QuickStart || Run.Phase == RunPhase.Lobby || Run.Phase == RunPhase.Generating || Run.Phase == RunPhase.Prep;

        private void StepRun()
        {
            if (StepEmptyServer()) return;
            switch (Run.Phase)
            {
                case RunPhase.Lobby:
                    if (Settings.QuickStart && _players.Count > 0) BeginRun();
                    break;
                case RunPhase.Generating:
                    // Who has not drawn the world by the deadline catches up in Prep.
                    if (Tick >= Run.PhaseEndTick || AllReady()) BeginShift();
                    break;
                case RunPhase.Prep:
                    if (Tick >= Run.PhaseEndTick || AllReady()) BeginDelivery();
                    break;
                case RunPhase.Delivery:
                    if (ClockStopped) HoldShiftClock();
                    else if (Tick >= Run.PhaseEndTick) BeginPayday();
                    break;
                case RunPhase.Payday:
                    if (Tick >= Run.PhaseEndTick) EndPayday();
                    break;
                case RunPhase.Draft:
                    if (Tick >= Run.PhaseEndTick || DraftSettled()) EndDraft();
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
            if (PresentCount > 0 || Run.Phase == RunPhase.Lobby)
            {
                _emptyTicks = 0;
                return false;
            }
            if (++_emptyTicks < SimConstants.SecondsToTicks(Content.Balance.GetDouble("emptyServerGraceSeconds"))) return false;
            _emptyTicks = 0;
            ReturnToLobby();
            return true;
        }

        /// <summary>Everybody who is here is ready; who is away is not waited for.</summary>
        private bool AllReady()
        {
            if (PresentCount == 0) return false;
            foreach (var p in _players) if (!p.Ready && !p.Away) return false;
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
                Shift = (byte)Run.Shift, Phase = Run.Phase, Tick = Tick, PhaseEndTick = Run.PhaseEndTick, Quota = Run.Quota, Outcome = Run.Outcome,
            });
        }

        private void BeginRun()
        {
            SetUpRun();
            BeginShift();
        }

        /// <summary>
        /// Doc 01 §1 "Generating": the run is set up as the host starts it — the world as new, the
        /// kits handed out — and the clients say when they have it drawn (their Ready); then Prep.
        /// </summary>
        private void BeginGenerating()
        {
            SetUpRun();
            EnterPhase(RunPhase.Generating, Content.Balance.GetDouble("generatingSeconds"));
        }

        /// <summary>A fresh run: the wallet, the perks, the shop, the nodes, what was built and the kits.</summary>
        private void SetUpRun()
        {
            Run.RunNumber++;
            Run.Shift = StartShift;
            Run.FirstShift = Run.Shift;
            ResetPostOffice();
            Run.ShiftsCompleted = 0;
            Run.Outcome = RunOutcome.None;
            Run.LastSummary = null;
            Run.Results = null;
            Economy.Reset();
            Economy.TopUp(StartWallet);
            ResetCombatRng();
            ResetRunEvents();
            _runConstructsLost = 0;
            ClearPerks();
            SetPerkPoolRank();
            ClearShop();
            RegrowNodes(all: true);
            ClearConstructs();
            ClearVehicles();
            GiveKits();
            MoorFerries();
            BuildShowcase();
            foreach (var p in _players)
            {
                p.Deliveries = 0;
                p.Misdeliveries = 0;
                p.Earned = 0;
                p.ShiftDeliveries = 0;
                p.Kills = 0;
                p.LootCoins = 0;
                p.Damage = 0;
                p.Built = 0;
                p.RepairPaid.Clear();
            }
        }

        private void BeginShift()
        {
            RegrowNodes(all: false);
            RollShopSpecials();
            BeginShiftEvents();
            // Doc 01 §2.4's callouts count what is done from the shift's Prep on.
            _shiftConstructsLost = 0;
            _shiftMisdeliveredAt.Clear();
            foreach (var p in _players)
            {
                p.ShiftDamage = 0;
                p.ShiftBuilt = 0;
            }
            if (Settings.QuickStart) BeginDelivery();
            else BeginPrep();
        }

        private void BeginPrep()
        {
            var b = Content.Balance;
            ClearCombat();
            RaiseInsuredRuins();
            Economy.BeginShift();
            // A preview for the HUD; the binding quota is sampled when Delivery starts.
            Run.Quota = TeamQuota(QuotaFormula.Quota(b, Run.Shift, QuotaHeads(Math.Max(1, PresentCount))));
            EnterPhase(RunPhase.Prep, b.GetForShift("prepSeconds", Run.Shift));
            PrepEvents();
            SendForecast();
            StockPlaytesters();
        }

        private void BeginDelivery()
        {
            var b = Content.Balance;
            ClearCombat();
            // Doc 06 §6: who is away does not count from here on, and nobody is counted again before the next shift.
            Run.PlayerCount = Math.Max(1, PresentCount);
            int quota = QuotaFormula.Quota(b, Run.Shift, QuotaHeads(Run.PlayerCount));
            Run.Quota = TeamQuota(quota);
            Economy.BeginShift();
            foreach (var p in _players) p.ShiftDeliveries = 0;
            // Doc 01 §6 Rush Hour: the stamps' seconds on the balance's, never under half a minute.
            double seconds = Math.Max(30.0, TeamStat(Stat.DeliverySeconds, (float)b.GetForShift("deliverySeconds", Run.Shift)));
            // As much mail as the quota of the formula asks for: a perk that lowers the quota does not thin the mail.
            // The debug world feeds the Intake a piece at a time instead (StepDebugWorld).
            int spawnValue = Map.Debug != null ? 0 : (int)Math.Round(quota * Content.MailMix.SpawnOverhead * TeamStat(Stat.SpawnValue, 1f));
            Spawner.DistrictsEarly = DistrictsEarly;
            ApplyPriorityPost();
            Spawner.BeginShift(Run.Shift, Tick + 1, SimConstants.SecondsToTicks(seconds), spawnValue);
            EnterPhase(RunPhase.Delivery, seconds);
            DeliveryEvents(SimConstants.SecondsToTicks(seconds));
            _earlyRaidDone = false;
            ScheduleRaid();
            ParkPlaytesters();
            StockPlaytesters();
        }

        /// <summary>Doc 01 §2.4: the quota check, the shift bonus and the summary the clients show.</summary>
        private void BeginPayday()
        {
            // Doc 10 §6 Overtime: a quota missed by a little, Delivery goes on.
            if (TryOvertime()) return;
            if (_wildfireEndTick != 0) EndWildfire(LiveRule(PerformativeMail.Sim.Content.RunEventRule.Wildfire));
            Spawner.EndShift();
            EndRaid();
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
                if (p.ShiftDeliveries > summary.MvpDeliveries)
                {
                    summary.Mvp = p.Id;
                    summary.MvpDeliveries = Clamp16(p.ShiftDeliveries);
                }
                if (p.ShiftDamage > summary.MvpDamageDealt)
                {
                    summary.MvpDamage = p.Id;
                    summary.MvpDamageDealt = Clamp16(p.ShiftDamage);
                }
                if (p.ShiftBuilt > summary.MvpBuiltCount)
                {
                    summary.MvpBuilt = p.Id;
                    summary.MvpBuiltCount = Clamp16(p.ShiftBuilt);
                }
            }
            summary.ConstructsLost = Clamp16(_shiftConstructsLost);
            // Most first; of as many, the one gone wrong first (a stable sort).
            var wrong = new List<KeyValuePair<ushort, int>>(_shiftMisdeliveredAt);
            var order = new List<int>();
            for (int i = 0; i < wrong.Count; i++) order.Add(i);
            order.Sort((a, b) => wrong[b].Value != wrong[a].Value ? wrong[b].Value.CompareTo(wrong[a].Value) : a.CompareTo(b));
            int shown = Math.Min(order.Count, ShiftSummary.MaxMisdeliveredAt);
            summary.MisdeliveredAt = new ushort[shown];
            summary.MisdeliveredCounts = new ushort[shown];
            for (int i = 0; i < shown; i++)
            {
                summary.MisdeliveredAt[i] = wrong[order[i]].Key;
                summary.MisdeliveredCounts[i] = Clamp16(wrong[order[i]].Value);
            }
            summary.Bonus = Economy.PayShiftBonus() + PaydayEvents();
            if (summary.QuotaMet) Run.ShiftsCompleted++;
            Run.LastSummary = summary;
            Events.Add(new ShiftSummaryEvent { Summary = summary });
            EnterPhase(RunPhase.Payday, Content.Balance.GetDouble("paydaySeconds"));
        }

        private void EndPayday()
        {
            bool met = Run.LastSummary!.QuotaMet;
            if (!met && !QuotaWaived) BeginResults(RunOutcome.QuotaMissed);
            else if (Run.Shift >= ShiftCount) BeginResults(met ? RunOutcome.Victory : RunOutcome.QuotaMissed);
            else if (!BeginDraft())
            {
                Run.Shift++;
                BeginShift();
            }
        }

        private void BeginResults(RunOutcome outcome)
        {
            var b = Content.Balance;
            ClearCombat();
            Run.Outcome = outcome;
            var results = new RunResults
            {
                Outcome = outcome,
                ShiftsCompleted = (byte)Run.ShiftsCompleted,
                FirstShift = (byte)Run.FirstShift,
                LastShift = (byte)Run.Shift,
                QuotaWaived = QuotaWaived,
                TotalEarned = Economy.TotalEarned,
                Score = StampedScore(Economy.TotalEarned),
                Deliveries = Economy.TotalDeliveries,
                Misdeliveries = Economy.TotalMisdeliveries,
                ConstructsLost = _runConstructsLost,
            };
            // Doc 09 §6: a practice run "does not award XP".
            int xp = Run.Mode == RunMode.Practice ? 0 : StampedXp(QuotaFormula.Xp(b, Run.ShiftsCompleted, outcome == RunOutcome.Victory, Economy.TotalDeliveries, _players.Count));
            foreach (var p in _players)
            {
                results.Players.Add(new PlayerResult
                {
                    Player = p.Id, Deliveries = Clamp16(p.Deliveries), Misdeliveries = Clamp16(p.Misdeliveries), Earned = p.Earned, Xp = xp,
                    Kills = Clamp16(p.Kills), Built = Clamp16(p.Built), Damage = p.Damage,
                });
            }
            Run.Results = results;
            Events.Add(new RunResultsEvent { Results = results });
            // Results waits for the host; a QuickStart server (tests, soak) moves on by itself.
            EnterPhase(RunPhase.Results, Settings.QuickStart ? b.GetDouble("paydaySeconds") : 0);
        }

        /// <summary>Ends the run: all mail, loot and money are gone, the next run starts from the same seed.</summary>
        private void ReturnToLobby()
        {
            // The run they could have come back to is over.
            RemoveAway();
            ClearCombat();
            ResetPostOffice();
            ClearMail(Intake, everything: true);
            // A run starts with empty chests, as it starts with empty pockets.
            foreach (var chest in _mapChests) ClearMail(chest, everything: true);
            foreach (var p in _players)
            {
                ClearMail(p.Hotbar, everything: true);
                ClearMail(p.Inventory, everything: true);
            }
            Spawner.Reset();
            ApplyStamps();
            Economy.Reset();
            ResetRunEvents();
            ClearPerks();
            ClearShop();
            RegrowNodes(all: true);
            ClearConstructs();
            ClearVehicles();
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

        /// <summary>
        /// The host is whoever has been here longest. A host who leaves or loses their connection
        /// hands the run on; with nobody here to take it, it stays with the one who is away.
        /// </summary>
        private void UpdateHost()
        {
            var host = GetPlayer(Run.Host);
            if (host != null && !host.Away) return;
            Player? next = null;
            foreach (var p in _players)
                if (!p.Away && (next == null || p.JoinOrder < next.JoinOrder)) next = p;
            if (next != null) Run.Host = next.Id;
            else if (host == null) Run.Host = EntityId.None;
        }

        // --- requests ---------------------------------------------------------------------------

        private OpResult StartRunOp(Player player)
        {
            if (Run.Phase != RunPhase.Lobby) return OpResult.WrongPhase;
            if (player.Id != Run.Host) return OpResult.NotHost;
            if (Settings.Generating) BeginGenerating();
            else BeginRun();
            return OpResult.Ok;
        }

        private OpResult SetReadyOp(Player player, in SimRequest r)
        {
            if (Run.Phase != RunPhase.Lobby && Run.Phase != RunPhase.Generating && Run.Phase != RunPhase.Prep) return OpResult.WrongPhase;
            // Sent by the client by itself once its world is drawn: in Prep it would mean a Ready nobody pressed.
            if (r.Count == SimRequest.WorldDrawnCount && Run.Phase != RunPhase.Generating) return OpResult.WrongPhase;
            player.Ready = r.Count != 0;
            return OpResult.Ok;
        }

        private OpResult ContinueRunOp(Player player)
        {
            // A stopped clock never ends the shift; the host does, with the same key.
            bool stoppedShift = Run.Phase == RunPhase.Delivery && ClockStopped;
            if (Run.Phase != RunPhase.Results && !stoppedShift) return OpResult.WrongPhase;
            if (player.Id != Run.Host) return OpResult.NotHost;
            if (stoppedShift) BeginPayday();
            else ReturnToLobby();
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
