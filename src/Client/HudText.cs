using System;
using System.Collections.Generic;
using System.Globalization;
using PerformativeMail.Client.Meta;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client
{
    public enum PromptTone : byte { Neutral, Match, Mismatch }

    /// <summary>Every string the HUD shows, derived from replicated state (doc 09 §2.4). Godot-free so it is testable.</summary>
    public static class HudText
    {
        /// <summary>Integer cents as "$18.20"; negative balances as "-$1.50".</summary>
        public static string Money(int cents)
        {
            int abs = Math.Abs(cents);
            return string.Format(CultureInfo.InvariantCulture, "{0}${1}.{2:00}", cents < 0 ? "-" : "", abs / 100, abs % 100);
        }

        /// <summary>mm:ss, rounded up so the clock reads 00:00 only when the phase is over.</summary>
        public static string Clock(double secondsLeft)
        {
            int s = (int)Math.Ceiling(Math.Max(0.0, secondsLeft));
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", s / 60, s % 60);
        }

        public static string PhaseName(RunPhase phase) => phase.ToString().ToUpperInvariant();

        public static int ShiftCount(ContentDb content) => content.Balance.GetArray("baseQuota").Length;

        /// <summary>"Shift 2 / 5   DELIVERY 03:12"; the phases that wait for people have no shift and no clock.</summary>
        public static string ShiftAndPhase(ClientWorld world)
        {
            var g = world.Global;
            switch (g.Phase)
            {
                case RunPhase.Lobby: return "LOBBY";
                case RunPhase.Results: return g.Outcome == RunOutcome.Victory ? "RESULTS   VICTORY" : "RESULTS   RUN OVER";
            }
            string clock = world.PhaseHasClock ? " " + Clock(world.PhaseSecondsLeft) : "";
            return $"Shift {g.Shift} / {ShiftCount(world.Content)}   {PhaseName(g.Phase)}{clock}";
        }

        public const string GoKey = "[G]";
        public const string PauseKey = "[Esc]";
        public const string PausedTitle = "PAUSED";

        /// <summary>
        /// What the run is waiting for and what the "go" key does about it (doc 01 §1: the host
        /// starts the run, Ready ends Prep early). Empty during Delivery.
        /// </summary>
        public static string PhaseHint(ClientWorld world)
        {
            var g = world.Global;
            int players = world.Players.Count, ready = world.ReadyCount;
            string host = world.Players.TryGetValue(g.Host, out var h) ? h.Name : "the host";
            if (world.Paused) return $"The clock is stopped   ·   {PauseKey} resume";
            switch (g.Phase)
            {
                case RunPhase.Lobby:
                    if (world.IsHost)
                        return $"Press {GoKey} to start the run   ·   {(players == 1 ? "1 player" : $"{players} players")} here"
                            + (players > 1 ? $", {ready} ready" : "");
                    return world.LocalReady
                        ? $"Ready — waiting for {host} to start the run   ·   {GoKey} not ready"
                        : $"Waiting for {host} to start the run   ·   {GoKey} ready";
                case RunPhase.Prep:
                    return world.LocalReady
                        ? $"Ready {ready} / {players} — the shift starts when everyone is   ·   {GoKey} not ready"
                        : $"Mailboxes open when the shift starts   ·   {GoKey} ready ({ready} / {players})";
                case RunPhase.Payday:
                {
                    var s = world.LastSummary;
                    string clock = Clock(world.PhaseSecondsLeft);
                    if (s == null) return "";
                    if (!s.QuotaMet) return $"Quota missed — results in {clock}";
                    return s.Shift >= ShiftCount(world.Content) ? $"Final quota met — results in {clock}" : $"Quota met — shift {s.Shift + 1} in {clock}";
                }
                case RunPhase.Results:
                    return world.IsHost ? $"Press {GoKey} to return to the lobby" : $"Waiting for {host} to return to the lobby";
                default: return "";
            }
        }

        /// <summary>The Payday summary (doc 01 §1 Payday): title first, then one fact per line.</summary>
        public static List<string> SummaryLines(ClientWorld world, ShiftSummary s)
        {
            var lines = new List<string>
            {
                s.QuotaMet ? $"SHIFT {s.Shift} — QUOTA MET" : $"SHIFT {s.Shift} — QUOTA MISSED",
                $"Earned {Money(s.Earned)} of {Money(s.Quota)}",
            };
            var kinds = new List<string>();
            for (int i = 0; i < s.DeliveriesByKind.Length; i++)
            {
                if (s.DeliveriesByKind[i] == 0) continue;
                kinds.Add(CountAndKind(i < world.Content.MailKinds.Count ? world.Content.MailKinds[i].Name : "item", s.DeliveriesByKind[i]));
            }
            lines.Add(kinds.Count == 0 ? "Delivered nothing" : "Delivered " + string.Join(", ", kinds));
            lines.Add($"Misdelivered {s.Misdeliveries}   ·   Late {s.LateDeliveries}");
            if (s.Bonus > 0) lines.Add($"No misdeliveries: bonus +{Money(s.Bonus)}");
            if (!s.Mvp.IsNone) lines.Add($"Most deliveries: {PlayerName(world, s.Mvp)} ({s.MvpDeliveries})");
            return lines;
        }

        /// <summary>The Results screen (doc 01 §1 RunOver / Victory): title, totals, then one line per player.</summary>
        /// <param name="profile">The local player's profile and what this run added to it; adds the Postal Rank line.</param>
        public static List<string> ResultsLines(ClientWorld world, RunResults r, MetaProfile? profile, ProfileGain? gain)
        {
            var lines = ResultsLines(world, r);
            if (profile == null || gain == null) return lines;
            int perRank = RankXpPerRank(world.Content);
            string rank = Rank(profile, perRank);
            lines.Add(gain.Value.RankAfter > gain.Value.RankBefore ? $"RANK UP!   {rank}" : rank);
            return lines;
        }

        public static int RankXpPerRank(ContentDb content) => content.Balance.GetInt("rankXpPerRank");

        /// <summary>"Postal Rank 2   ·   120 / 500 XP": the rank and the XP earned towards the next one (doc 01 §5.1).</summary>
        public static string Rank(MetaProfile profile, int xpPerRank) =>
            $"Postal Rank {profile.Rank(xpPerRank)}   ·   {profile.XpIntoRank(xpPerRank)} / {xpPerRank} XP";

        public static List<string> ResultsLines(ClientWorld world, RunResults r)
        {
            int shifts = ShiftCount(world.Content);
            var lines = new List<string>
            {
                r.Outcome == RunOutcome.Victory ? $"VICTORY — all {shifts} shifts delivered" : $"RUN OVER — quota missed on shift {r.ShiftsCompleted + 1}",
                $"Shifts completed {r.ShiftsCompleted} / {shifts}   ·   Score {Money(r.Score)}",
                $"Delivered {r.Deliveries}   ·   Misdelivered {r.Misdeliveries}",
            };
            foreach (var p in r.Players)
                lines.Add($"{PlayerName(world, p.Player)}:   {p.Deliveries} delivered, {p.Misdeliveries} misdelivered, {Money(p.Earned)}   ·   +{p.Xp} XP");
            return lines;
        }

        public static string PlayerName(ClientWorld world, EntityId id) =>
            world.Players.TryGetValue(id, out var p) ? p.Name : "a player who left";

        /// <summary>Progress against the quota during a shift; the coming quota in Prep; nothing outside a run.</summary>
        public static string Quota(in GlobalState g)
        {
            switch (g.Phase)
            {
                case RunPhase.Lobby:
                case RunPhase.Results: return "";
                case RunPhase.Prep: return $"Quota {Money(g.Quota)}";
                default: return $"Quota {Money(g.ShiftEarnings)} / {Money(g.Quota)}";
            }
        }

        public static string Wallet(in GlobalState g) => $"Wallet {Money(g.Wallet)}";

        /// <summary>"3 Letters" / "1 Small Package".</summary>
        public static string CountAndKind(ContentDb content, ItemStack stack) => CountAndKind(Name(content, stack), stack.Count);

        public static string CountAndKind(string name, int count) => count == 1 ? $"1 {name}" : $"{count} {name}s";

        public static string Name(ContentDb content, ItemStack stack) =>
            stack.Kind == StackKind.Mail ? content.MailKinds[stack.DefIndex].Name : content.Items[stack.DefIndex].Name;

        /// <summary>Full description of a stack for tooltips and lists: "3 Letters for 13 Larch Lane".</summary>
        public static string Stack(ClientWorld world, ItemStack stack) =>
            stack.Kind == StackKind.Mail
                ? $"{CountAndKind(world.Content, stack)} for {world.Map.FormatAddress(stack.Address)}"
                : CountAndKind(world.Content, stack);

        /// <summary>A line of the Intake manifest: "8 Larch Lane  —  3 Letters", with the stack count when there are several.</summary>
        public static string ManifestRow(ClientWorld world, in ManifestRow row)
        {
            string address = world.Map.FormatAddress(row.Address);
            string name = world.Content.MailKinds[row.Kind].Name;
            if (row.Empty) return $"{address}  —  no {name}s left";
            string stacks = row.Stacks > 1 ? $"  ({row.Stacks} stacks)" : "";
            return $"{address}  —  {CountAndKind(name, row.Items)}{stacks}";
        }

        public const string MatchWord = "MATCH";
        public const string MismatchWord = "WRONG ADDRESS";
        public const string ClosedWord = "closed";

        /// <summary>
        /// The interact prompt line. For a delivery it shows the stack's address and the mailbox's
        /// address side by side with the verdict in words; the HUD draws the tick or cross that goes
        /// with <paramref name="tone"/> itself, because the UI font has no ✓ / ✗ glyphs. The game
        /// never hides a mismatch.
        /// </summary>
        public static string Prompt(ClientWorld world, in InteractPrompt p, out PromptTone tone)
        {
            tone = PromptTone.Neutral;
            switch (p.Kind)
            {
                case InteractKind.OpenIntake: return "Open Intake  [E]";
                case InteractKind.CloseIntake: return "Close Intake  [E]";
                case InteractKind.Deliver:
                {
                    var dest = p.Destination!;
                    if (p.MailboxClosed) return $"Mailbox: {dest.Label}   ·   {ClosedWord} — mailboxes take mail only during Delivery";
                    string all = p.MatchingStacks > 0 ? $"   ·   deliver all matching ({p.MatchingStacks})  [F]" : "";
                    if (p.Stack == null) return $"Mailbox: {dest.Label}   ·   select mail (2–8) to deliver{all}";
                    if (!p.KindAccepted)
                    {
                        tone = PromptTone.Mismatch;
                        return $"{Name(world.Content, p.Stack.Stack)} does not fit the mailbox at {dest.Label}";
                    }
                    tone = p.AddressMatches ? PromptTone.Match : PromptTone.Mismatch;
                    string verdict = p.AddressMatches ? MatchWord : MismatchWord;
                    return $"{verdict}   ·   Deliver {Stack(world, p.Stack.Stack)}   |   mailbox: {dest.Label}   [hold E]{all}";
                }
                default: return "";
            }
        }
    }
}
