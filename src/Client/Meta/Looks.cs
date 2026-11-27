using System.Collections.Generic;
using System.Numerics;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Client.Meta
{
    /// <summary>
    /// The cosmetics of doc 10 §8 (design choice 174): uniform and cap colours, tints with no gameplay effect. What a
    /// profile has opened — the defaults, those of its Postal Rank (doc 01 §5.2 "15+"), those of its achievements —, the
    /// achievements a finished run earns, and the Lobby's keys that step through what is open, worn as the server is told.
    /// </summary>
    public sealed class Looks
    {
        private readonly ClientRuntime _client;
        private readonly ProfileTracker _profile;

        public Looks(ClientRuntime client, ProfileTracker profile)
        {
            _client = client;
            _profile = profile;
            // What the profile wears goes to every server joined, as soon as there is a world to wear it in.
            client.TickStarting += () =>
            {
                if (_client.State != ClientState.Joined || ReferenceEquals(_client.World, _sentTo)) return;
                if (Send() != 0) _sentTo = _client.World;
            };
        }

        private ClientWorld? _sentTo;

        public static bool IsOpen(CosmeticDef c, int rank, ICollection<string> achievements) =>
            c.Index == 0 || (c.Rank > 0 && rank >= c.Rank) || (c.Achievement.Length > 0 && achievements.Contains(c.Achievement));

        /// <summary>The ids of every cosmetic open to a rank and a list of achievements, uniforms first.</summary>
        public static List<string> Open(ContentDb content, int rank, ICollection<string> achievements)
        {
            var ids = new List<string>();
            foreach (var list in new[] { content.Uniforms, content.Caps })
                foreach (var c in list)
                    if (IsOpen(c, rank, achievements)) ids.Add(c.Id);
            return ids;
        }

        /// <summary>
        /// The achievements a finished run earns that the profile has not (doc 10 §8): first Victory, a Victory with 3
        /// stamps, 1000 lifetime deliveries (the profile's count, already with this run's), and a zero-misdelivery run —
        /// three shifts or more played and nobody in the team misdelivered. A practice run earns none.
        /// </summary>
        public static List<string> Earned(RunResults results, RunMode mode, byte stamps, MetaProfile profile)
        {
            var earned = new List<string>();
            if (mode == RunMode.Practice) return earned;
            void Add(string id, bool ok)
            {
                if (ok && !profile.Achievements.Contains(id)) earned.Add(id);
            }
            bool victory = results.Outcome == RunOutcome.Victory;
            Add(CosmeticDef.FirstVictory, victory);
            Add(CosmeticDef.StampedVictory, victory && BitOperations.PopCount(stamps) >= 3);
            Add(CosmeticDef.Deliveries1000, profile.Deliveries >= 1000);
            Add(CosmeticDef.CleanRun, results.ShiftsCompleted >= 3 && results.Misdeliveries == 0 && results.Deliveries > 0);
            return earned;
        }

        /// <summary>The achievement's name, for the Results' reveal.</summary>
        public static string AchievementName(string id) => id switch
        {
            CosmeticDef.FirstVictory => "first Victory",
            CosmeticDef.StampedVictory => "a Victory with 3 stamps",
            CosmeticDef.Deliveries1000 => "1000 deliveries",
            CosmeticDef.CleanRun => "a run without a misdelivery",
            _ => id,
        };

        private int Rank => _profile.Profile.Rank(HudText.RankXpPerRank(_client.Content));

        /// <summary>The uniform worn: the profile's if it is still open, else the default.</summary>
        public CosmeticDef Uniform => Worn(_client.Content.Uniforms, _profile.Profile.Uniform);
        public CosmeticDef Cap => Worn(_client.Content.Caps, _profile.Profile.Cap);

        private CosmeticDef Worn(IReadOnlyList<CosmeticDef> list, string id)
        {
            foreach (var c in list)
                if (c.Id == id && IsOpen(c, Rank, _profile.Profile.Achievements)) return c;
            return list[0];
        }

        /// <summary>How many of the kind are open to this profile.</summary>
        public int OpenCount(CosmeticKind kind)
        {
            int n = 0;
            foreach (var c in kind == CosmeticKind.Uniform ? _client.Content.Uniforms : _client.Content.Caps)
                if (IsOpen(c, Rank, _profile.Profile.Achievements)) n++;
            return n;
        }

        /// <summary>The Lobby's key: the next open uniform (or cap), worn, saved and sent. False when there is no other.</summary>
        public bool Next(CosmeticKind kind)
        {
            var world = _client.World;
            if (world == null || world.Global.Phase != RunPhase.Lobby) return false;
            var list = kind == CosmeticKind.Uniform ? _client.Content.Uniforms : _client.Content.Caps;
            int from = (kind == CosmeticKind.Uniform ? Uniform : Cap).Index;
            for (int i = 1; i < list.Count; i++)
            {
                var c = list[(from + i) % list.Count];
                if (!IsOpen(c, Rank, _profile.Profile.Achievements)) continue;
                if (kind == CosmeticKind.Uniform) _profile.Profile.Uniform = c.Index == 0 ? "" : c.Id;
                else _profile.Profile.Cap = c.Index == 0 ? "" : c.Id;
                _profile.Save();
                Send();
                return true;
            }
            return false;
        }

        private uint Send()
        {
            var world = _client.World;
            if (world == null || (world.Global.Phase != RunPhase.Lobby && world.Global.Phase != RunPhase.Prep)) return 0;
            return _client.Send(SimRequest.SetLook(0, Uniform.Index, Cap.Index));
        }
    }
}
