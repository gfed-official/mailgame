using System;
using System.Globalization;
using System.IO;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Client.Meta
{
    /// <summary>What the last finished run did to the profile, for the Results screen.</summary>
    public struct ProfileGain
    {
        public int Xp;
        public int RankBefore, RankAfter;
    }

    /// <summary>
    /// Feeds the local player's <see cref="MetaProfile"/> from what the server reports and saves it
    /// at every Payday and at Results (doc 07 §7.1): own deliveries and misdeliveries as they happen,
    /// and at Results the XP the server awarded (doc 01 §5.1), the run counters and the run's line.
    /// </summary>
    public sealed class ProfileTracker
    {
        public MetaProfile Profile { get; }
        public IProfileStore Store => _store;
        /// <summary>Set while the Results of a run are showing.</summary>
        public ProfileGain? LastGain { get; private set; }
        /// <summary>Why the last save failed; null when it worked.</summary>
        public string? SaveProblem { get; private set; }

        private readonly ClientRuntime _client;
        private readonly IProfileStore _store;
        private readonly Func<DateTime> _clock;

        public ProfileTracker(ClientRuntime client, MetaProfile profile, IProfileStore store, Func<DateTime>? clock = null)
        {
            _client = client;
            Profile = profile;
            _store = store;
            _clock = clock ?? (() => DateTime.Now);
            client.EventReceived += OnEvent;
        }

        /// <summary>
        /// The stored profile, or a new one named <paramref name="defaultName"/>. A file that cannot be
        /// read is set aside (not overwritten) and reported in <paramref name="problem"/>.
        /// </summary>
        public static MetaProfile LoadOrCreate(IProfileStore store, string defaultName, out string? problem)
        {
            problem = null;
            try
            {
                string? json = store.Read();
                if (json != null) return MetaProfile.FromJson(json);
            }
            catch (Exception e) when (e is FormatException || e is IOException || e is UnauthorizedAccessException)
            {
                problem = e.Message;
                try { store.SetAside(); }
                catch (Exception aside) when (aside is IOException || aside is UnauthorizedAccessException) { problem += "; " + aside.Message; }
            }
            return MetaProfile.Create(defaultName);
        }

        private void OnEvent(SimEvent ev)
        {
            var world = _client.World;
            if (world == null) return;
            switch (ev)
            {
                case DeliveredEvent e when e.Player == world.LocalId:
                    Profile.Deliveries++;
                    break;
                case MisdeliveredEvent e when e.Player == world.LocalId:
                    Profile.Misdeliveries++;
                    break;
                case ShiftSummaryEvent _:
                    Save();
                    break;
                case RunResultsEvent e:
                    ApplyResults(world, e.Results);
                    Save();
                    break;
                case PhaseChangedEvent e when e.Phase != RunPhase.Results:
                    LastGain = null;
                    break;
            }
        }

        private void ApplyResults(ClientWorld world, RunResults results)
        {
            int xp = -1;
            foreach (var p in results.Players)
                if (p.Player == world.LocalId) xp = p.Xp;
            // Not in the results: this player was not part of the run.
            if (xp < 0) return;

            int perRank = HudText.RankXpPerRank(world.Content);
            int before = Profile.Rank(perRank);
            Profile.RankXp += xp;
            Profile.Runs++;
            if (results.Outcome == RunOutcome.Victory) Profile.Wins++;
            if (results.Score > Profile.BestScore) Profile.BestScore = results.Score;
            Profile.AddRun(new RecentRun
            {
                SeedString = world.Seed.ToString(CultureInfo.InvariantCulture),
                ShiftsCompleted = results.ShiftsCompleted,
                Victory = results.Outcome == RunOutcome.Victory,
                Score = results.Score,
                Players = results.Players.Count,
                Date = _clock().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            });
            LastGain = new ProfileGain { Xp = xp, RankBefore = before, RankAfter = Profile.Rank(perRank) };
        }

        /// <summary>A failed save must not stop the game: the profile stays in memory and the next save tries again.</summary>
        public void Save()
        {
            try
            {
                _store.Write(Profile.ToJson());
                SaveProblem = null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                SaveProblem = e.Message;
            }
        }
    }
}
