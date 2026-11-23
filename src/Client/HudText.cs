using System;
using System.Collections.Generic;
using System.Globalization;
using PerformativeMail.Client.Meta;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Net;
using PerformativeMail.Sim.Perks;
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
                case RunPhase.Generating: return "GENERATING";
                case RunPhase.Results: return g.Outcome == RunOutcome.Victory ? "RESULTS   VICTORY" : "RESULTS   RUN OVER";
            }
            string clock = world.PhaseHasClock ? " " + Clock(world.PhaseSecondsLeft) : "";
            if (world.ClockStopped) clock += " " + ClockStoppedTag;
            return $"Shift {g.Shift} / {ShiftCount(world.Content)}   {PhaseName(g.Phase)}{clock}";
        }

        /// <summary>After the time of a shift whose clock a playtest flag has stopped (nit 10).</summary>
        public const string ClockStoppedTag = "(stopped)";
        /// <summary>In the Payday hint of a missed quota that a playtest flag keeps from ending the run (nit 13).</summary>
        public const string QuotaWaivedTag = "playtest: the run goes on";

        public const string GoKey = "[G]";
        /// <summary>The host's keys for the world in the Lobby. Doc 09 §4 has none; V steps through the worlds, N draws a new seed.</summary>
        public const string WorldKey = "[V]", SeedKey = "[N]";

        /// <summary>
        /// The seed as a string to share (doc 01 §5.3, "PM1-SMALL-7F3A9C21"; no Postage Stamps yet):
        /// the map's tag and the seed in hex.
        /// </summary>
        public static string SeedString(string mapId, uint seed)
        {
            string tag = mapId == WorldGenerator.SmallIsland ? "SMALL" : mapId.Split('_')[0].ToUpperInvariant();
            return $"PM1-{tag}-{seed:X8}";
        }

        /// <summary>The world's name for the Lobby: "Small Island", or a map file's own name.</summary>
        public static string WorldName(WorldMap map) => map.Id == WorldGenerator.SmallIsland ? "Small Island" : map.Name;

        /// <summary>The Lobby's world plate: "Small Island · PM1-SMALL-0000002A".</summary>
        public static string WorldTracker(ClientWorld world) => $"{WorldName(world.Map)} · {SeedString(world.Map.Id, world.Seed)}";

        /// <summary>
        /// The loading panel of the Generating phase (doc 01 §1, doc 09 "loading screen"): the
        /// world, then a line per carrier — whose client has it drawn —, then how long the run
        /// waits at most.
        /// </summary>
        public static List<string> GeneratingLines(ClientWorld world)
        {
            var map = world.Map;
            int districts = map.Districts.Count;
            var lines = new List<string>
            {
                "GENERATING",
                $"{WorldName(map)} · {SeedString(map.Id, world.Seed)}   ·   {map.Destinations.Count} houses, {districts} district{(districts == 1 ? "" : "s")}",
            };
            var players = new List<ClientPlayer>(world.Players.Values);
            players.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            foreach (var p in players)
            {
                if (p.Away) continue;
                string who = p.Id == world.LocalId ? $"{p.Name} (you)" : p.Name;
                lines.Add(p.Ready ? $"{who}   ·   ready" : $"{who}   ·   building the world…");
            }
            lines.Add($"The first Prep starts when everyone has the world, in {Clock(world.PhaseSecondsLeft)} at the latest");
            return lines;
        }

        /// <summary>How full the loading bar is: the share of the carriers here whose world is drawn.</summary>
        public static float GeneratingProgress(ClientWorld world)
        {
            int players = world.PresentCount;
            return players > 0 ? Math.Clamp(world.ReadyCount / (float)players, 0f, 1f) : 0f;
        }

        /// <summary>The Lobby's kit plate: "Kit: Land · [K]".</summary>
        public static string KitTracker(ClientWorld world) => $"Kit: {LocalKit(world).Name}   ·   {KitKey}";

        /// <summary>The host's keys in the Lobby.</summary>
        public static string WorldKeys => $"{WorldKey} world   ·   {SeedKey} new seed";

        /// <summary>The feed's line when the host has picked another world.</summary>
        public static string WorldChanged(ClientWorld world, WorldChangedEvent e)
        {
            string who = world.IsHost ? "You" : PlayerName(world, world.Global.Host);
            int houses = world.Map.Destinations.Count;
            return $"{who} chose {WorldName(world.Map)} · {SeedString(e.MapId, e.Seed)}   ·   {houses} houses, {world.Map.Districts.Count} district{(world.Map.Districts.Count == 1 ? "" : "s")}";
        }

        /// <summary>Picks the next starting kit in the Lobby. Doc 09 §4 has no key for it; K for kit.</summary>
        public const string KitKey = "[K]";

        /// <summary>The kit the local player has picked, as the snapshots say.</summary>
        public static KitDef LocalKit(ClientWorld world) =>
            world.Players.TryGetValue(world.LocalId, out var me) && me.Kit < world.Content.Kits.Count ? world.Content.Kits[me.Kit] : world.Content.DefaultKit;

        /// <summary>"Axe, Fiber ×4".</summary>
        public static string KitStacks(IReadOnlyList<KitStack> stacks)
        {
            var parts = new List<string>();
            foreach (var s in stacks) parts.Add(s.Count == 1 ? s.Item.Name : $"{s.Item.Name} ×{s.Count}");
            return string.Join(", ", parts);
        }

        /// <summary>What a kit holds, for the Lobby: "Axe, Fiber ×4 for you · Log ×10 and a Bike for the team".</summary>
        public static string KitContents(KitDef kit)
        {
            string team = KitStacks(kit.Team);
            foreach (var v in kit.TeamVehicles) team += (team.Length > 0 ? " and a " : "a ") + v.Name;
            return $"{KitStacks(kit.Personal)} for you" + (team.Length > 0 ? $"   ·   {team} for the team" : "");
        }

        public static string KitChosen(KitDef kit) => $"Your kit: {kit.Name} — {KitContents(kit)}";

        public static string KitRefused(OpResult result) => result switch
        {
            OpResult.RankTooLow => "That kit opens at a higher Postal Rank",
            OpResult.WrongPhase => "Kits are picked in the Lobby",
            _ => "",
        };

        /// <summary>The kits there are and at which rank each opens: "Land · Sea (rank 2) · Pipes (rank 10)".</summary>
        public static string KitsOpen(ContentDb content, int rank)
        {
            var parts = new List<string>();
            foreach (var k in content.Kits) parts.Add(k.Rank <= rank ? k.Name : $"{k.Name} (rank {k.Rank})");
            return string.Join(" · ", parts);
        }

        /// <summary>The line for a kit handed out; empty for another player's personal items.</summary>
        public static string KitGiven(ClientWorld world, KitGivenEvent e)
        {
            if (e.Kit >= world.Content.Kits.Count) return "";
            var kit = world.Content.Kits[e.Kit];
            if (!e.Team)
                return e.Player == world.LocalId && kit.Personal.Count > 0 ? $"Your {kit.Name} kit is in your pockets: {KitStacks(kit.Personal)}" : "";
            var parts = new List<string>();
            string whose = e.Player == world.LocalId ? "your" : $"{PlayerName(world, e.Player)}'s";
            if (kit.Team.Count > 0)
                parts.Add($"{KitStacks(kit.Team)} in {(world.Map.Chests.Count > 0 ? "the chest by the Post Office" : (e.Player == world.LocalId ? "your pockets" : $"{PlayerName(world, e.Player)}'s pockets"))}");
            foreach (var v in kit.TeamVehicles) parts.Add($"a {v.Name} by the counter");
            return parts.Count == 0 ? "" : $"The team's kit ({whose} {kit.Name} kit): {string.Join("   ·   ", parts)}";
        }
        public const string PauseKey = "[Esc]";
        public const string PausedTitle = "PAUSED";
        /// <summary>Esc with nothing open and others in the run: nothing stops, only the mouse is let go.</summary>
        public const string MouseReleased = "Mouse let go   ·   a click or " + PauseKey + " takes the view back";

        /// <summary>
        /// What the run is waiting for and what the "go" key does about it (doc 01 §1: the host
        /// starts the run, Ready ends Prep early). Empty during Delivery, unless a playtest flag has
        /// stopped its clock: then only the host's "go" key ends the shift.
        /// </summary>
        public static string PhaseHint(ClientWorld world)
        {
            var g = world.Global;
            // Who has lost their connection is not waited for.
            int players = world.PresentCount, ready = world.ReadyCount;
            string host = world.Players.TryGetValue(g.Host, out var h) ? h.Name : "the host";
            if (world.Paused) return $"The clock is stopped   ·   {PauseKey} resume";
            switch (g.Phase)
            {
                case RunPhase.Lobby:
                {
                    string kit = $"   ·   {KitKey} kit: {LocalKit(world).Name}";
                    if (world.IsHost)
                        return $"Press {GoKey} to start the run   ·   {(players == 1 ? "1 player" : $"{players} players")} here"
                            + (players > 1 ? $", {ready} ready" : "") + kit;
                    return world.LocalReady
                        ? $"Ready — waiting for {host} to start the run   ·   {GoKey} not ready{kit}"
                        : $"Waiting for {host} to start the run   ·   {GoKey} ready{kit}";
                }
                case RunPhase.Generating:
                    return world.LocalReady
                        ? $"The world is ready here   ·   waiting for {players - ready} of {players}"
                        : $"Building {WorldName(world.Map)}";
                case RunPhase.Prep:
                    return world.LocalReady
                        ? $"Ready {ready} / {players} — the shift starts when everyone is   ·   {GoKey} not ready"
                        : $"Mailboxes open when the shift starts   ·   {GoKey} ready ({ready} / {players})";
                case RunPhase.Payday:
                {
                    var s = world.LastSummary;
                    string clock = Clock(world.PhaseSecondsLeft);
                    if (s == null) return "";
                    bool last = s.Shift >= ShiftCount(world.Content);
                    // The counter serves the team where it stands, for as long as the summary is up.
                    string shop = world.ShopServes ? $"   ·   {ShopKey} shop" : "";
                    if (!s.QuotaMet && world.QuotaWaived && !last) return $"Quota missed — {QuotaWaivedTag}   ·   shift {s.Shift + 1} in {clock}{shop}";
                    if (!s.QuotaMet) return $"Quota missed — results in {clock}";
                    return s.Shift >= ShiftCount(world.Content) ? $"Final quota met — results in {clock}" : $"Quota met — shift {s.Shift + 1} in {clock}{shop}";
                }
                case RunPhase.Draft:
                {
                    if (world.DraftOffer.Count == 0) return "";
                    int waiting = 0;
                    foreach (var p in world.Players.Values)
                        if (!p.Away && p.DraftPick < 0) waiting++;
                    int pick = world.LocalDraftPick;
                    if (pick >= 0 && pick < world.DraftOffer.Count)
                    {
                        string took = $"You took {world.DraftOffer[pick].Name}";
                        return waiting > 0 ? $"{took}   ·   waiting for {waiting} more" : took;
                    }
                    string keys = "";
                    for (int i = 0; i < world.DraftOffer.Count; i++)
                        if (world.CardState(i) == OpResult.Ok) keys += " " + CardKey(i);
                    if (keys.Length == 0) return $"No card is open to you   ·   shift {g.Shift + 1} in {Clock(world.PhaseSecondsLeft)}";
                    string newCards = world.CanReroll ? $"   ·   {RerollKey} new cards" : "";
                    return $"Take a perk:{keys}{newCards}   ·   {players - waiting} / {players} have chosen";
                }
                case RunPhase.Results:
                    return world.IsHost ? $"Press {GoKey} to return to the lobby" : $"Waiting for {host} to return to the lobby";
                case RunPhase.Delivery when world.ClockStopped:
                    return world.IsHost
                        ? $"Playtest: the shift clock is stopped   ·   {GoKey} ends the shift"
                        : $"Playtest: the shift clock is stopped   ·   {host} ends the shift";
                default: return "";
            }
        }

        public const string HostLostTitle = "HOST LOST";
        /// <summary>Doc 06 §5: no host migration; what a run has paid in XP so far is already saved at each Payday.</summary>
        public const string HostLostHint = "The host left the game   ·   shifts you finished keep their XP";

        /// <summary>
        /// Why the connection ended, for the player: refused with a reason, never answered, or lost
        /// after joining ("Host lost", doc 06 §5).
        /// </summary>
        public static string ConnectionEnded(ClientRuntime client, bool hadJoined)
        {
            if (client.State == ClientState.Rejected)
            {
                switch (client.RejectReason)
                {
                    case RejectReason.ProtocolMismatch: return "Could not join: this build does not match the host's";
                    case RejectReason.ServerFull: return "Could not join: the game is full";
                    case RejectReason.RunInProgress: return "Could not join: a shift is in progress (join in the Lobby or during Prep)";
                    default: return "Could not join: the host refused the connection";
                }
            }
            if (client.DisconnectDetail != null) return $"Could not join: {client.DisconnectDetail}";
            return hadJoined ? "Host lost" : "Could not join: no answer from the host";
        }

        // --- inventory screen (doc 09 §2.5) -------------------------------------------------------

        public const string RotateKey = "[R]";
        public const string SortByAddress = "Sort by address";
        public const string SortBySize = "Sort by size";

        /// <summary>The controls of the inventory screen, under the player's own grids.</summary>
        public static string InventoryHint(bool externalOpen, bool dragging, bool shopOpen = false)
        {
            if (dragging) return $"Drop on a cell   ·   {RotateKey} turns it   ·   right-click puts it back";
            if (shopOpen) return "Click sells a stack   ·   Ctrl+click sells one of it   ·   drag to move";
            string across = externalOpen ? "Click sends a stack across" : "Click swaps a stack between hotbar and inventory";
            return $"Drag to move   ·   Ctrl+drag takes half   ·   {across}";
        }

        /// <summary>Why a move did not happen, said the way a player would (shown where the hint is while it matters).</summary>
        public static string MoveRefused(OpResult result)
        {
            switch (result)
            {
                case OpResult.DoesNotFit: return "No room there";
                case OpResult.NotAllowed: return "That cannot go in there";
                case OpResult.NotOpen:
                case OpResult.OutOfRange:
                case OpResult.UnknownContainer: return "Too far away";
                case OpResult.UnknownEntry: return "That is no longer there";
                case OpResult.RateLimited: return "Too fast — slow down";
                default: return "That cannot be moved now";
            }
        }

        /// <summary>"Chest" for "chest", "Bike cargo" for "bike_cargo": the title over an open container.</summary>
        public static string ContainerTitle(ContainerDef def) => Capitalised(def.Id.Replace('_', ' '));

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
                case RunPhase.Generating:
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

        // --- the fight of the concept video: health, the raid, loot --------------------------------

        /// <summary>"Raider", "Mega Horned Giant"; "Something" for a kind this build does not know.</summary>
        public static string EnemyName(ContentDb content, int kind, bool mega)
        {
            string name = kind >= 0 && kind < content.Enemies.Count ? content.Enemies[kind].Name : "Something";
            return mega ? "Mega " + name : name;
        }

        /// <summary>The number beside the health bar: whole points, and 0 only when down.</summary>
        public static string Health(float hp) => ((int)Math.Ceiling(Math.Max(0f, hp))).ToString(CultureInfo.InvariantCulture);

        /// <summary>The warning of a raid by the first way in, the east end of the first street (doc 05 §3.1's example).</summary>
        public const string RaidWarning = "Raid incoming from the east";

        /// <summary>
        /// Where the raid comes from (doc 05 §3.1 "Raid incoming from the east"): its first way in,
        /// and the second its later pulses come by too; with <paramref name="streets"/> the street
        /// each comes along.
        /// </summary>
        public static string RaidFrom(WorldMap map, int first, int second, bool streets)
        {
            var edges = map.SpawnEdges;
            if (edges.Count == 0) return "the east";
            var a = edges[first >= 0 && first < edges.Count ? first : 0];
            string text = streets ? a.Name : $"the {a.Direction}";
            if (second < 0 || second >= edges.Count || second == a.Index) return text;
            var b = edges[second];
            if (!streets && b.Direction == a.Direction) return text;
            return streets ? $"{text}, later also {b.Name}" : $"{text} and the {b.Direction}";
        }

        /// <summary>The banner's words for the raid on its way: "Raid incoming from the east and the north".</summary>
        public static string RaidIncoming(ClientWorld world) =>
            $"Raid incoming from {RaidFrom(world.Map, world.Global.RaidEdgeA, world.Global.RaidEdgeB, streets: false)}";

        /// <summary>The line across the view while a raid gathers, and when it breaks off.</summary>
        public static string RaidBanner(ClientWorld world)
        {
            switch (world.Raid)
            {
                case RaidState.Warning: return $"{RaidIncoming(world)}   ·   {Clock(world.RaidSecondsLeft)}";
                case RaidState.Fleeing: return "The raiders are running for the gate";
                default: return "";
            }
        }

        /// <summary>The raid's row among the trackers: the countdown, then how many are left.</summary>
        public static string RaidTracker(ClientWorld world)
        {
            switch (world.Raid)
            {
                case RaidState.Warning: return $"Raid in {Clock(world.RaidSecondsLeft)}";
                case RaidState.Active:
                {
                    int marked = world.MarkedEnemies;
                    return marked > 0
                        ? $"Raid   ·   {CountAndKind("raider", world.Enemies.Count)} left   ·   {marked.ToString(CultureInfo.InvariantCulture)} by the alarm"
                        : $"Raid   ·   {CountAndKind("raider", world.Enemies.Count)} left";
                }
                case RaidState.Fleeing: return "Raid   ·   fleeing";
                default: return "";
            }
        }

        /// <summary>While the local player is down: how long until they stand up at the Post Office.</summary>
        public static string Down(ClientWorld world)
        {
            if (!world.HasLocal || !world.Local.Dead) return "";
            int s = (int)Math.Ceiling(world.RespawnSecondsLeft);
            return s > 0 ? $"DOWN   ·   back on your feet in {s}" : "DOWN";
        }

        /// <summary>
        /// Just back on their feet in a fight: how long nothing can touch the local player. Past the
        /// seconds it is the yard that keeps them, for as long as they stay in it with raiders about.
        /// </summary>
        public static string Protected(ClientWorld world)
        {
            if (!world.LocalProtected) return "";
            int s = (int)Math.Ceiling(world.ProtectedSecondsLeft);
            if (s > 0) return $"PROTECTED   ·   {s} s, or until you hit a raider";
            return world.LocalAtPark ? "PROTECTED at the mailbox   ·   until you walk off or hit a raider" : "PROTECTED in the yard   ·   until you leave it or hit a raider";
        }

        /// <summary>The name over a carrier's head; one who has lost their connection is marked as long as they may come back.</summary>
        public static string NameTag(ClientPlayer player) => player.Away ? $"{player.Name} (away)" : player.Name;

        /// <summary>The event feed's line when a carrier loses their connection during a run (doc 06 §6).</summary>
        public static string WentAway(ClientWorld world, ClientPlayer player)
        {
            double left = SimConstants.TicksToSeconds((long)player.AwayUntilTick - (long)Math.Floor(world.ServerTick));
            // To the nearest second: the tick the client reckons with is an estimate, and 02:01 of two minutes is wrong.
            return $"{player.Name} lost the connection — {Clock(Math.Round(Math.Max(0.0, left)))} to come back";
        }

        /// <summary>"your bag", "Cass's bag"; "an unclaimed bag" once whoever dropped it has left.</summary>
        public static string BagName(ClientWorld world, EntityId owner)
        {
            if (!owner.IsNone && owner == world.LocalId) return "your bag";
            return world.Players.TryGetValue(owner, out var p) ? $"{p.Name}'s bag" : "an unclaimed bag";
        }

        /// <summary>The title over an open container: a bag is named after whoever dropped it.</summary>
        public static string ContainerTitle(ClientWorld world, GridContainer container) =>
            world.Bags.TryGetValue(container.Id, out var bag) ? Capitalised(BagName(world, bag.Owner)) : ContainerTitle(container.Def);

        /// <summary>The tracker row while the local player's bag lies in the world: how long it will.</summary>
        public static string BagTracker(ClientWorld world)
        {
            var bag = world.LocalBag;
            return bag == null ? "" : $"Your bag   ·   gone in {Clock(world.BagSecondsLeft(bag))}";
        }

        public static string Capitalised(string text) =>
            text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        // --- the Draft (doc 09 §2.10) and the perks of the run -------------------------------------

        /// <summary>The key that takes a card: "[1]".</summary>
        public static string CardKey(int card) => card == 9 ? "[0]" : card == 10 ? "[-]" : card == 11 ? "[=]" : $"[{(card + 1).ToString(CultureInfo.InvariantCulture)}]";

        public const string RerollKey = RotateKey;

        /// <summary>Under the cards (doc 09 §2.10 "reroll button with remaining count"); empty once the card is chosen.</summary>
        public static string RerollLine(ClientWorld world)
        {
            if (world.Global.Phase != RunPhase.Draft || world.DraftOffer.Count == 0 || world.LocalDraftPick >= 0) return "";
            return world.RerollsLeft > 0
                ? $"{RerollKey} New cards   ·   {Rerolls(world.RerollsLeft)} left this run"
                : "No rerolls left this run";
        }

        private static string Rerolls(int n) => n == 1 ? "1 reroll" : $"{n.ToString(CultureInfo.InvariantCulture)} rerolls";

        public static string Rerolled(int left) => $"You drew new cards   ·   {Rerolls(left)} left this run";

        public static string RerollRefused(OpResult result) => result switch
        {
            OpResult.NoRerolls => "No rerolls left this run",
            OpResult.NoNewCards => "No other cards are left to draw",
            OpResult.AlreadyPicked => "You have taken your card",
            _ => "",
        };

        /// <summary>Over the cards: what is chosen, and before which shift.</summary>
        public static string DraftTitle(ClientWorld world) => $"DRAFT   ·   one perk each before shift {world.Global.Shift + 1}";

        /// <summary>"POSTAL   ·   UNCOMMON": the category badge, and the rarity the card's frame is coloured by.</summary>
        public static string PerkBadge(PerkDef perk) =>
            $"{perk.Category.ToString().ToUpperInvariant()}   ·   {perk.Rarity.ToString().ToUpperInvariant()}";

        /// <summary>Whose the perk will be, how often it stacks, and what it never shares a run with.</summary>
        public static string PerkScope(ContentDb content, PerkDef perk)
        {
            string scope = perk.Team ? "Team perk" : perk.MaxStacks > 1 ? $"Personal perk   ·   stacks to {perk.MaxStacks}" : "Personal perk";
            string never = "";
            foreach (var other in content.Perks)
                if (!ReferenceEquals(other, perk) && perk.ConflictsWith(other)) never += (never.Length > 0 ? ", " : "") + other.Name;
            return never.Length > 0 ? $"{scope}   ·   never with {never}" : scope;
        }

        /// <summary>
        /// What has become of a card: who took it (picks are seen by everybody as they are made),
        /// or why it is not open to the local player, or how much of it they hold already.
        /// </summary>
        public static string CardNote(ClientWorld world, int card)
        {
            if (card < 0 || card >= world.DraftOffer.Count) return "";
            var perk = world.DraftOffer[card];
            var takers = new List<string>();
            // By the perk: after a reroll somebody else's card of the same number may be another.
            foreach (var p in world.Players.Values)
                if (p.DraftPick >= 0 && ReferenceEquals(p.DraftPerk, perk) && p.Id != world.LocalId) takers.Add(p.Name);
            takers.Sort(StringComparer.Ordinal);
            if (world.LocalDraftPick == card) takers.Insert(0, "you");
            if (takers.Count > 0)
            {
                string who = string.Join(", ", takers);
                return perk.Team ? $"Taken for the team by {who}" : $"Taken by {who}";
            }
            switch (world.CardState(card))
            {
                case OpResult.PerkTaken: return "The team has it";
                case OpResult.PerkMaxed: return "You hold all you can of it";
                case OpResult.PerkExcluded:
                {
                    var with = HeldConflict(world, perk);
                    return with != null ? $"Not with {with.Name}" : "Not with a perk already held";
                }
            }
            int held = perk.Team ? 0 : perk.StacksIn(world.LocalPerks);
            return held > 0 ? $"You hold {held} of {perk.MaxStacks}" : "";
        }

        /// <summary>The perk already in the run that keeps <paramref name="perk"/> from the local player.</summary>
        private static PerkDef? HeldConflict(ClientWorld world, PerkDef perk)
        {
            foreach (var held in world.TeamPerks)
                if (perk.ConflictsWith(held)) return held;
            foreach (var held in world.LocalPerks)
                if (perk.ConflictsWith(held)) return held;
            if (!perk.Team) return null;
            foreach (var p in world.Players.Values)
                foreach (var held in p.Perks)
                    if (perk.ConflictsWith(held)) return held;
            return null;
        }

        /// <summary>The lines of a card from the top: key and name, badge, what it does, scope, and the note (empty if none).</summary>
        public static string[] DraftCard(ClientWorld world, int card)
        {
            var perk = world.DraftOffer[card];
            return new[]
            {
                $"{CardKey(card)}  {perk.Name}",
                PerkBadge(perk),
                perk.Description,
                PerkScope(world.Content, perk),
                CardNote(world, card),
            };
        }

        /// <summary>Everything the Draft panel says, top to bottom, for the smoke; empty where there is no Draft.</summary>
        public static string DraftText(ClientWorld world)
        {
            if (world.Global.Phase != RunPhase.Draft || world.DraftOffer.Count == 0) return "";
            var lines = new List<string> { DraftTitle(world) };
            for (int i = 0; i < world.DraftOffer.Count; i++)
                foreach (string line in DraftCard(world, i))
                    if (line.Length > 0) lines.Add(line);
            string reroll = RerollLine(world);
            if (reroll.Length > 0) lines.Add(reroll);
            string perks = Perks(world);
            if (perks.Length > 0) lines.Add(perks);
            return string.Join("\n", lines);
        }

        /// <summary>The perks of the run so far: "Team: Union Rep   ·   You: Long Legs x2"; empty while there are none.</summary>
        public static string Perks(ClientWorld world)
        {
            string team = PerkNames(world.TeamPerks), own = PerkNames(world.LocalPerks);
            if (team.Length > 0 && own.Length > 0) return $"Team: {team}   ·   You: {own}";
            return team.Length > 0 ? $"Team: {team}" : own.Length > 0 ? $"You: {own}" : "";
        }

        /// <summary>The tracker row of the run's perks: how many the team holds and how many are the local player's own.</summary>
        public static string PerkTracker(ClientWorld world)
        {
            int team = world.TeamPerks.Count, own = world.LocalPerks.Count;
            if (team + own == 0) return "";
            return $"Perks   ·   team {team}   ·   yours {own}";
        }

        /// <summary>Names in the order they were taken, a stacked perk once with its count.</summary>
        private static string PerkNames(IReadOnlyList<PerkDef> perks)
        {
            string names = "";
            for (int i = 0; i < perks.Count; i++)
            {
                var perk = perks[i];
                bool seen = false;
                for (int j = 0; j < i && !seen; j++) seen = ReferenceEquals(perks[j], perk);
                if (seen) continue;
                int stacks = perk.StacksIn(perks);
                names += (names.Length > 0 ? ", " : "") + (stacks > 1 ? $"{perk.Name} {Count(stacks)}" : perk.Name);
            }
            return names;
        }

        /// <summary>The event feed's line for a perk taken, or given by the Draft's clock.</summary>
        public static string PerkPicked(ClientWorld world, EntityId player, PerkDef perk, bool auto)
        {
            bool me = player == world.LocalId;
            string who = me ? "You" : world.Players.TryGetValue(player, out var p) ? p.Name : "Someone";
            string verb = !auto ? "took" : me ? "were given" : "was given";
            return perk.Team ? $"{who} {verb} {perk.Name} for the team" : $"{who} {verb} {perk.Name}";
        }

        // --- hot reload (doc 07 §6: F6 in a debug build) ---------------------------------------------

        public const string HotReloadNotHost = "Content reload is the host's (F6)";
        public const string HotReloadWithGuests = "Content not reloaded: guests are in the game, their content could not follow";

        public static string HotReloaded(ContentDb content) => $"Content reloaded · hash {content.ContentHash:x8}";

        /// <summary>The first thing in the way, and how many more there are (the log has them all).</summary>
        public static string HotReloadRefused(IReadOnlyList<string> problems)
        {
            string first = problems.Count > 0 ? problems[0] : "unknown";
            return problems.Count > 1 ? $"Content not reloaded: {first} (+{problems.Count - 1} more)" : $"Content not reloaded: {first}";
        }

        // --- the shop (doc 09 §2.8) and what is used from the hand ---------------------------------

        public const string UseKey = "[Q]";
        public const string SpecialWord = "SPECIAL";

        /// <summary>Over the cards: whose money it is, and how much of it there is.</summary>
        public static string ShopTitle(ClientWorld world) => $"SHOP   ·   team wallet {Money(world.Global.Wallet)}";

        /// <summary>"$0.80"; a special says so, and what it was before.</summary>
        public static string ShopPriceLine(ClientWorld world, ShopItemDef def)
        {
            int price = world.ShopPrice(def);
            if (!world.IsSpecial(def)) return Money(price);
            return price < def.Price ? $"{SpecialWord}   ·   {Money(price)}  (was {Money(def.Price)})" : $"{SpecialWord}   ·   {Money(price)}";
        }

        /// <summary>What has become of a card: on its way, or why it is not to be had; a blueprint says whose it will be.</summary>
        public static string ShopNote(ClientWorld world, ShopItemDef def)
        {
            if (world.IsBuying(def)) return "On its way";
            switch (world.ShopState(def))
            {
                case OpResult.ShopLocked: return $"From shift {def.FromShift}";
                case OpResult.AlreadyOwned: return "The team owns it";
                case OpResult.CantAfford: return "Not enough money";
                case OpResult.DoesNotFit: return "No room in your pockets";
                case OpResult.WrongPhase: return "The counter is closed";
                case OpResult.OnePerCarrier: return $"A {def.Name} for each of you already";
            }
            if (def.Kind == ShopKind.Vehicle) return "One for each carrier   ·   parked by the counter";
            return def.OncePerRun ? "Once per run, for the whole team" : "";
        }

        /// <summary>The lines of a card from the top: key and name, price, what it is, and the note (empty if none).</summary>
        public static string[] ShopCard(ClientWorld world, ShopItemDef def, int place)
        {
            return new[]
            {
                $"{CardKey(place)}  {def.Name}",
                ShopPriceLine(world, def),
                def.Description,
                ShopNote(world, def),
            };
        }

        /// <summary>Under the cards: how to buy, how to sell, how to leave.</summary>
        /// <summary>The shop key (doc 09 §4 has none, and B is Build mode there): C, for the counter.</summary>
        public const string ShopKey = "[C]";
        /// <summary>The jump key gets off a vehicle (a bike does not jump).</summary>
        public const string GetOffKey = "[Space]";
        public const string ShopHint = "A number key or a click buys   ·   click a stack of yours to sell it   ·   " + ShopKey + " or [Esc] closes";

        /// <summary>Everything the shop panel says, top to bottom, for the smoke; empty while its cards are not up.</summary>
        public static string ShopText(ClientWorld world)
        {
            if (!world.ShopOpen) return "";
            var lines = new List<string> { ShopTitle(world) };
            var shelf = world.ShopShelf;
            for (int i = 0; i < shelf.Count; i++)
                foreach (string line in ShopCard(world, shelf[i], i))
                    if (line.Length > 0) lines.Add(line);
            lines.Add(ShopHint);
            return string.Join("\n", lines);
        }

        /// <summary>Why the counter, or the hand, did not do what was asked.</summary>
        public static string ShopRefused(OpResult result)
        {
            switch (result)
            {
                case OpResult.CantAfford: return "Not enough money in the wallet";
                case OpResult.DoesNotFit: return "No room in your pockets";
                case OpResult.ShopLocked: return "The shop does not sell that yet";
                case OpResult.AlreadyOwned: return "The team owns it already";
                case OpResult.OnePerCarrier: return "The team has one for each of you already";
                case OpResult.NoParking: return "No room to park it by the counter";
                case OpResult.WrongPhase: return "The counter is closed";
                case OpResult.OutOfRange: return "Too far from the counter";
                case OpResult.NotSellable: return "The shop does not buy that";
                case OpResult.FullHealth: return "You are at full health";
                case OpResult.NotUsable: return "That is not something to use";
                case OpResult.Dead: return "Not while you are down";
                case OpResult.RateLimited: return "Too fast — slow down";
                default: return "That cannot be done now";
            }
        }

        /// <summary>The event feed's line for a purchase: the money was everybody's.</summary>
        public static string ShopBought(ClientWorld world, EntityId player, ShopItemDef def, int price)
        {
            bool me = player == world.LocalId;
            string who = me ? "You" : world.Players.TryGetValue(player, out var p) ? p.Name : "Someone";
            return $"{who} bought {def.Name}  -{Money(price)}";
        }

        /// <summary>The event feed's line for a sale.</summary>
        public static string ShopSold(ClientWorld world, EntityId player, int item, int count, int paid)
        {
            bool me = player == world.LocalId;
            string who = me ? "You" : world.Players.TryGetValue(player, out var p) ? p.Name : "Someone";
            string what = CountAndKind(item >= 0 && item < world.Content.Items.Count ? world.Content.Items[item].Name : "item", count);
            return $"{who} sold {what}  +{Money(paid)}";
        }

        // --- harvesting (doc 02 §3.9) -----------------------------------------------------------

        /// <summary>The tool that gets the most out of a node; null where none is made for it.</summary>
        public static ItemDef? HarvestTool(ContentDb content, NodeDef node)
        {
            foreach (var item in content.Items)
                if (item.HarvestsResource(node.Resource)) return item;
            return null;
        }

        /// <summary>"an Axe", "a Pickaxe".</summary>
        public static string WithArticle(string name) =>
            name.Length > 0 && "AEIOUaeiou".IndexOf(name[0]) >= 0 ? $"an {name}" : $"a {name}";

        /// <summary>The event feed's line for what a carrier has got out of the land, hit after hit.</summary>
        public static string Gathered(ClientWorld world, EntityId player, int item, int count)
        {
            bool me = player == world.LocalId;
            string who = me ? "You" : world.Players.TryGetValue(player, out var p) ? p.Name : "Someone";
            string what = item >= 0 && item < world.Content.Items.Count ? world.Content.Items[item].Name : "item";
            return $"{who} gathered {what} {Count(count)}";
        }

        /// <summary>Why a swing at a node came to nothing, where the prompt had not said so already.</summary>
        public static string HarvestRefused(OpResult result)
        {
            switch (result)
            {
                case OpResult.NeedsTool: return "That takes a tool in the hand";
                case OpResult.Depleted: return "There is nothing left of it";
                case OpResult.DoesNotFit: return "No room in your pockets";
                case OpResult.WrongPhase: return "Not now: the land is worked in Prep and during Delivery";
                case OpResult.OutOfRange: return "Too far away";
                case OpResult.Dead: return "Not while you are down";
                default: return "";
            }
        }

        // --- building by hand (doc 04 §1, doc 09 §2.7) --------------------------------------------

        /// <summary>A chest of the map, or one the carriers built.</summary>
        private static string ChestTitle(in InteractPrompt p) =>
            p.Chest != null ? ContainerTitle(p.Chest.Container) : p.Built?.Def.Container != null ? ContainerTitle(p.Built.Def.Container) : "Chest";

        /// <summary>The keys of Build mode, over the hotbar while it is up.</summary>
        public const string BuildKeys = "[LMB] build / drag  [R] turn  [wheel] tier  [hold RMB] remove  [Q] pick  [B] done";

        /// <summary>A recipe on the bar: "[1] Wooden Wall · Log x3"; one of a group says which of how many the wheel is on: "[7] Fast Belt 2/2 · …".</summary>
        public static string RecipeCard(RecipeDef recipe, int key, IReadOnlyList<RecipeDef>? group = null)
        {
            string of = "";
            if (group != null && group.Count > 1)
            {
                int at = 0;
                for (int i = 0; i < group.Count; i++) if (group[i] == recipe) at = i;
                of = $" {(at + 1).ToString(CultureInfo.InvariantCulture)}/{group.Count.ToString(CultureInfo.InvariantCulture)}";
            }
            return $"[{KeyName(key)}] {recipe.Building.Name}{of}   ·   {Inputs(recipe.Inputs, 1.0)}";
        }

        /// <summary>The name of a number key counted from 0: 1–9, 0, -, =.</summary>
        public static string KeyName(int key) => key < 9 ? (key + 1).ToString(CultureInfo.InvariantCulture) : key == 9 ? "0" : key == 10 ? "-" : "=";

        /// <summary>"Log x3", "Log x3, Rope x1": what a recipe takes, or the part of it that comes back.</summary>
        public static string Inputs(IReadOnlyList<RecipeInput> inputs, double part, int hp = 1, int maxHp = 1)
        {
            var parts = new List<string>();
            var counts = BuildRules.RefundCounts(inputs, part, hp, maxHp);
            for (int i = 0; i < inputs.Count; i++)
                if (counts[i] > 0) parts.Add($"{inputs[i].Item.Name} {Count(counts[i])}");
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }

        /// <summary>"Plank x5, Iron Ingot x5": what so many of a recipe take.</summary>
        public static string Times(IReadOnlyList<RecipeInput> inputs, int times)
        {
            var parts = new List<string>();
            foreach (var input in inputs) parts.Add($"{input.Item.Name} {Count(input.Count * times)}");
            return string.Join(", ", parts);
        }

        /// <summary>"Missing 2 Log": what the pockets lack of a recipe.</summary>
        public static string Missing(ClientWorld world, RecipeDef recipe)
        {
            var parts = new List<string>();
            foreach (var input in recipe.Inputs)
            {
                int lack = input.Count - BuildRules.Have(world.Hotbar, world.Inventory, input.Item);
                if (lack > 0) parts.Add($"{lack.ToString(CultureInfo.InvariantCulture)} {input.Item.Name}");
            }
            return "Missing " + string.Join(", ", parts);
        }

        /// <summary>Why a recipe cannot go on a tile (doc 09 §2.7: "On street", "Missing 2 Iron Ingot").</summary>
        public static string PlaceReason(ClientWorld world, RecipeDef recipe, OpResult state)
        {
            switch (state)
            {
                case OpResult.OnStreet: return "On the street: it stays open for the mail";
                case OpResult.Blocked: return "Something stands there";
                case OpResult.Occupied: return "Somebody is in the way";
                case OpResult.OutOfRange: return "Too far away";
                case OpResult.MissingMaterials: return Missing(world, recipe);
                case OpResult.NeedsBlueprint: return $"Needs the {recipe.Blueprint?.Name ?? "blueprint"} from the shop";
                case OpResult.RecipeLocked: return $"From shift {recipe.UnlockShift.ToString(CultureInfo.InvariantCulture)}";
                case OpResult.WrongPhase: return BuildClosed;
                case OpResult.Dead: return "Not while you are down";
                default: return "Cannot be built here";
            }
        }

        public const string BuildClosed = "Building is done in Prep and during Delivery";

        /// <summary>The prompt line in Build mode: the ghost's verdict, or what taking down what is looked at gives back.</summary>
        public static string BuildPrompt(ClientWorld world, in BuildGhost g, out PromptTone tone)
        {
            tone = PromptTone.Neutral;
            var recipe = g.Recipe;
            if (recipe == null) return "";
            switch (g.Aim)
            {
                case BuildAim.Place when g.Line != null && g.Line.Count > 1:
                {
                    // Doc 04 §1.1: a drag line, laid when the button is let go.
                    int ok = g.LineOk, n = g.Line.Count;
                    string name = recipe.Building.Name + (ok == 1 ? "" : "s");
                    if (ok == 0)
                    {
                        tone = PromptTone.Mismatch;
                        return $"{recipe.Building.Name}   ·   {PlaceReason(world, recipe, g.Line[0].State)}";
                    }
                    tone = PromptTone.Match;
                    string of = ok == n ? "" : $" of {n.ToString(CultureInfo.InvariantCulture)}";
                    return $"Lay {ok.ToString(CultureInfo.InvariantCulture)}{of} {name}   ·   {Times(recipe.Inputs, ok)}{OutfeedNote(world, recipe.Building, g.Tx, g.Tz, g.Rotation)}   [let go]";
                }
                case BuildAim.Place:
                    if (g.State == OpResult.Ok)
                    {
                        tone = PromptTone.Match;
                        return $"Build {recipe.Building.Name}   ·   {Inputs(recipe.Inputs, 1.0)}{AlarmNote(world, recipe.Building, g.Tx, g.Tz)}{OutfeedNote(world, recipe.Building, g.Tx, g.Tz, g.Rotation)}{RampNote(recipe.Building, g.Rotation)}   [LMB]";
                    }
                    tone = PromptTone.Mismatch;
                    return $"{recipe.Building.Name}   ·   {PlaceReason(world, recipe, g.State)}{RampNote(recipe.Building, g.Rotation)}";
                case BuildAim.Construct:
                {
                    var built = g.Target!;
                    string name = built.Def.Name;
                    string hp = built.Hp < built.Def.Hp ? $"   ·   {built.Hp.ToString(CultureInfo.InvariantCulture)}/{built.Def.Hp.ToString(CultureInfo.InvariantCulture)} HP" : "";
                    string back = Inputs(built.Def.Recipe.Inputs, world.RefundRatio, built.Hp, built.Def.Hp);
                    switch (g.State)
                    {
                        case OpResult.Ok: return $"Take down {name}   ·   {back} back{hp}   [hold RMB]";
                        case OpResult.NotEmpty: tone = PromptTone.Mismatch; return $"{name}   ·   empty it first to take it down";
                        case OpResult.DoesNotFit: tone = PromptTone.Mismatch; return $"{name}   ·   no room in your pockets for {back}";
                        case OpResult.OutOfRange: tone = PromptTone.Mismatch; return $"{name}   ·   too far away to take down{hp}";
                        default: tone = PromptTone.Mismatch; return $"{name}{hp}";
                    }
                }
                default:
                    return $"Build {recipe.Building.Name}   ·   look at open ground";
            }
        }

        /// <summary>What an Alarm Post would do on the tile: ring the raid in early, or only mark the raiders.</summary>
        /// <summary>
        /// A belt on the Intake's outfeed tile (doc 04 §2.6): " · fed by the Intake" when it faces
        /// away from the counter, otherwise how to make it so.
        /// </summary>
        public static string OutfeedNote(ClientWorld world, BuildingDef def, int tx, int tz, int rotation)
        {
            if (def.Behaviour != BuildingBehaviour.Belt || !def.Rideable) return "";
            var belts = world.Belts;
            if (belts.OutfeedTile < 0 || tx != belts.OutfeedX || tz != belts.OutfeedZ) return "";
            return (rotation & 3) == Sim.Automation.BeltNetwork.OutfeedRotation ? "   ·   fed by the Intake" : "   ·   [R] to face away from the Intake: fed by it";
        }

        /// <summary>A Belt Ramp's ghost (doc 04 §2.1): which way it runs, up a level or down one; R turns it round and then over.</summary>
        public static string RampNote(BuildingDef def, int rotation)
        {
            if (!def.Ramp) return "";
            return rotation < 4 ? "   ·   up a level the way it faces" : "   ·   down a level the way it faces";
        }

        public static string AlarmNote(ClientWorld world, BuildingDef def, int tx, int tz)
        {
            if (def.Behaviour != BuildingBehaviour.Alarm) return "";
            string seconds = ((int)Math.Round(def.WarningBonusSeconds)).ToString(CultureInfo.InvariantCulture);
            return RaidRules.AlarmInRange(world.Map, def, tx, tz, world.Global.Shift)
                ? $"   ·   rings the raid in {seconds} s early"
                : "   ·   too far from the raiders' ways in to ring early";
        }

        /// <summary>The feed's line as the raid is announced: with the warning the Alarm Posts gave, if any rang.</summary>
        public static string RaidAnnounced(ClientWorld world)
        {
            int posts = 0;
            foreach (var c in world.Constructs.All)
                if (RaidRules.AlarmInRange(world.Map, c.Def, c.Tx, c.Tz, world.Global.Shift)) posts++;
            var waves = world.Content.Waves;
            string head = $"Raid incoming from {RaidFrom(world.Map, world.Global.RaidEdgeA, world.Global.RaidEdgeB, streets: true)}";
            return RaidAnnounced(head, posts, world.RaidSecondsLeft, world.LocalStat(Stat.RaidWarningSeconds, (float)waves.WarningSeconds));
        }

        /// <summary>
        /// The raid's line with <paramref name="posts"/> Alarm Posts in range: they are named only
        /// when the warning is longer than the one the team has without them (a raid called at
        /// once by the playtest's <c>--raid</c> owes them nothing).
        /// </summary>
        public static string RaidAnnounced(string head, int posts, double secondsLeft, double warningWithoutPosts)
        {
            if (posts == 0 || secondsLeft < warningWithoutPosts + 0.5) return head;
            string seconds = ((int)Math.Round(secondsLeft)).ToString(CultureInfo.InvariantCulture);
            return $"{head}   ·   {seconds} s of warning with {(posts == 1 ? "the Alarm Post" : $"{posts.ToString(CultureInfo.InvariantCulture)} Alarm Posts")}";
        }

        /// <summary>Why a build or a take-down came to nothing, where the prompt had not said so already.</summary>
        public static string BuildRefused(OpResult result)
        {
            switch (result)
            {
                case OpResult.Occupied: return "Somebody stepped in the way";
                case OpResult.Blocked: return "Something stands there now";
                case OpResult.MissingMaterials: return "You do not have the materials";
                case OpResult.NotEmpty: return "Empty the chest first";
                case OpResult.DoesNotFit: return "No room in your pockets";
                case OpResult.OutOfRange: return "Too far away";
                case OpResult.WrongPhase: return BuildClosed;
                default: return "";
            }
        }

        /// <summary>The shift a house's district opens in (doc 01 §1.2): its mail comes from then on.</summary>
        public static int DistrictOpensAt(WorldMap map, DestinationDef dest)
        {
            foreach (var d in map.Districts)
                if (d.Id == dest.Address.District) return d.UnlockShift;
            return 1;
        }

        public static string OpensInShift(int shift) => $"opens in shift {shift.ToString(CultureInfo.InvariantCulture)}";

        /// <summary>
        /// The feed's line as a shift's Prep begins on a map of several districts: the district that
        /// opens with it, how many houses it adds and on which streets; empty when none does.
        /// </summary>
        public static string DistrictOpened(WorldMap map, int shift)
        {
            if (map.Districts.Count < 2) return "";
            foreach (var district in map.Districts)
            {
                if (district.UnlockShift != shift) continue;
                var streets = new List<string>();
                int houses = 0;
                foreach (var d in map.Destinations)
                {
                    if (d.Address.District != district.Id) continue;
                    houses++;
                    if (!streets.Contains(d.Street.Name)) streets.Add(d.Street.Name);
                }
                string on = streets.Count == 0 ? "" : $" on {JoinAnd(streets)}";
                int open = 0;
                foreach (var d in map.Destinations) if (DistrictOpensAt(map, d) <= shift) open++;
                return shift == 1
                    ? $"{district.Name}: {houses} houses{on} take mail   ·   another district opens every shift"
                    : $"{district.Name} opens: {houses} more houses{on}   ·   {open} in all";
            }
            return "";
        }

        private static string JoinAnd(List<string> items) =>
            items.Count == 1 ? items[0] : string.Join(", ", items.GetRange(0, items.Count - 1)) + " and " + items[items.Count - 1];

        /// <summary>The event feed's line for a vehicle bought: where it stands.</summary>
        public static string VehicleAdded(VehicleDef def) => $"The {def.Name} stands by the counter   ·   look at it and [E] to ride";

        /// <summary>The event feed's line for somebody getting on.</summary>
        public static string Mounted(ClientWorld world, EntityId player, VehicleDef def)
        {
            if (player == world.LocalId) return $"You are on the {def.Name}   ·   {GetOffKey} gets off   ·   [E] opens its rack";
            string who = world.Players.TryGetValue(player, out var p) ? p.Name : "Someone";
            return $"{who} got on a {def.Name}";
        }

        /// <summary>Why the saddle was not had.</summary>
        public static string MountRefused(OpResult result)
        {
            switch (result)
            {
                case OpResult.Ridden: return "Somebody is on it";
                case OpResult.OutOfRange: return "Too far away to get on";
                case OpResult.AlreadyRiding: return "You are in a saddle already";
                case OpResult.Dead: return "Not while you are down";
                case OpResult.BadRequest: return "It is not there any more";
                default: return "";
            }
        }

        /// <summary>The event feed's line for a blueprint the team has bought: what it can build now.</summary>
        public static string BlueprintBought(ClientWorld world, ShopItemDef blueprint)
        {
            var names = new List<string>();
            foreach (var recipe in world.Content.Recipes)
                if (recipe.Blueprint == blueprint) names.Add(recipe.Building.Name);
            return names.Count == 0 ? $"The team owns the {blueprint.Name}" : $"{blueprint.Name}: the team can build {string.Join(" and ", names)} now   ·   [B]";
        }

        /// <summary>The event feed's line for what was built.</summary>
        public static string Built(ClientWorld world, EntityId player, BuildingDef def, int count = 1)
        {
            bool me = player == world.LocalId;
            string who = me ? "You" : world.Players.TryGetValue(player, out var p) ? p.Name : "Someone";
            return count == 1 ? $"{who} built {WithArticle(def.Name)}" : $"{who} built {count.ToString(CultureInfo.InvariantCulture)} {def.Name}s";
        }

        /// <summary>The event feed's line for what is gone: taken down, or broken by the raiders.</summary>
        public static string Unbuilt(ClientWorld world, EntityId player, BuildingDef def, bool destroyed)
        {
            if (destroyed) return $"The raiders broke {WithArticle(def.Name)}";
            bool me = player == world.LocalId;
            string who = me ? "You" : world.Players.TryGetValue(player, out var p) ? p.Name : "Someone";
            return $"{who} took down {WithArticle(def.Name)}";
        }

        /// <summary>The event feed's line for the specials of the shift that begins; empty when there are none.</summary>
        public static string ShopSpecials(ClientWorld world)
        {
            string names = "";
            foreach (var def in world.ShopSpecials) names += (names.Length > 0 ? ", " : "") + def.Name;
            return names.Length > 0 ? $"On special at the shop: {names}" : "";
        }

        /// <summary>The name of the stack in the hand; what can be used says with which key.</summary>
        public static string Held(ClientWorld world, ItemStack stack)
        {
            string name = Stack(world, stack);
            return stack.Kind == StackKind.Item && world.Content.Items[stack.DefIndex].Heal > 0 ? $"{name}   ·   {UseKey} use" : name;
        }

        /// <summary>The tracker row of what is waiting: mail not yet delivered and the complaint meter.</summary>
        public static string Status(in GlobalState g) => $"Backlog {g.Backlog}   ·   Complaint {g.Complaint}";

        /// <summary>The tracker row of the shift's work so far.</summary>
        public static string Tally(in GlobalState g) => $"Delivered {g.Deliveries}   ·   Misdelivered {g.Misdeliveries}";

        /// <summary>What the two triggers do, beside the hotbar while there is something to hit.</summary>
        public static string Weapons(int cannonAmmo) =>
            cannonAmmo > 0 ? $"[LMB] hammer   ·   [RMB] mail cannon x{cannonAmmo}" : "[LMB] hammer   ·   [RMB] mail cannon: no Small Package";

        /// <summary>"x12" on a stack and on a loot card.</summary>
        public static string Count(int count) => "x" + count.ToString(CultureInfo.InvariantCulture);

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
                case InteractKind.OpenChest: return $"Open {ChestTitle(p)}  [E]";
                case InteractKind.CloseChest: return $"Close {ChestTitle(p)}  [E]";
                case InteractKind.OpenBag: return $"Open {BagName(world, p.Bag!.Owner)}  [E]   ·   gone in {Clock(world.BagSecondsLeft(p.Bag))}";
                case InteractKind.CloseBag: return $"Close {BagName(world, p.Bag!.Owner)}  [E]   ·   gone in {Clock(world.BagSecondsLeft(p.Bag))}";
                case InteractKind.OpenShop:
                    return p.ShopClosed ? $"Shop   ·   {ClosedWord} — the counter serves in Prep and at Payday" : "Open Shop  [E]";
                case InteractKind.CloseShop: return "Close Shop  [E]";
                case InteractKind.Mount: return $"Ride {p.Vehicle!.Def.Name}  [E]";
                case InteractKind.OpenRack: return $"Open {ContainerTitle(p.Vehicle!.Def.Cargo)}  [E]   ·   {GetOffKey} get off";
                case InteractKind.CloseRack: return $"Close {ContainerTitle(p.Vehicle!.Def.Cargo)}  [E]   ·   {GetOffKey} get off";
                case InteractKind.Harvest:
                {
                    var node = p.Node!;
                    var tool = HarvestTool(world.Content, node);
                    if (p.NodeState == OpResult.NeedsTool)
                    {
                        tone = PromptTone.Mismatch;
                        return $"{node.Name}   ·   needs {WithArticle(tool?.Name ?? "tool")} in the hand";
                    }
                    string gives = $"{node.Item.Name} {Count(p.NodeYield)}";
                    if (p.NodeState == OpResult.DoesNotFit)
                    {
                        tone = PromptTone.Mismatch;
                        return $"{node.Name}   ·   no room in your pockets for {gives}";
                    }
                    // Bare hands on a tree: it can be done, and it can be done better.
                    string better = tool != null && p.NodeYield < node.Yield ? $" (more with {WithArticle(tool.Name)})" : "";
                    string left = p.NodeHitsLeft == 1 ? "1 hit left" : $"{p.NodeHitsLeft} hits left";
                    return $"{Capitalised(node.Verb)} {node.Name}   ·   {gives} a hit{better}   ·   {left}   [hold E]";
                }
                case InteractKind.PushBelt:
                {
                    var def = p.Built!.Def;
                    float mult = world.Belts.PushMultiplier;
                    if (p.Pushing)
                    {
                        tone = PromptTone.Match;
                        return $"Pushing the {def.Name}   ·   {(def.Speed * mult).ToString("0.0", CultureInfo.InvariantCulture)} m/s";
                    }
                    return $"Push the {def.Name}   ·   ×{mult.ToString("0.0", CultureInfo.InvariantCulture)} while held   [hold E]";
                }
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
