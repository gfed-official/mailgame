using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// The host's choice of world in the Lobby (doc 01 §1 "Seed … chosen", §5.3, doc 06 §5 step
    /// 1, doc 09 §2.2): which map — the test street, the seed's own Small Island town, or from Postal Rank 3 its Large
    /// Island — and which seed. The server builds the new map and swaps it in where the old one stood: the
    /// Post Office's Intake stays the same container, the map's chests are new ones, nodes and
    /// what was built start afresh (there is nothing in the Lobby to keep), and every carrier
    /// stands on the new pad. The clients are told the map's id, seed and hash and build it
    /// themselves.
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>The worlds the Lobby offers, in the order the world key steps through them.</summary>
        public static readonly IReadOnlyList<string> WorldChoices = new[] { "test_street", WorldGenerator.SmallIsland, LargeIslandGenerator.LargeIsland };

        /// <summary>The Postal Rank a world asks of the host (doc 01 §5.2: the Large Island at rank 3); 1 for the rest.</summary>
        public static int WorldRank(ContentDb content, string mapId) =>
            mapId == LargeIslandGenerator.LargeIsland ? content.Balance.GetInt("largeIslandRank") : 1;

        /// <summary>A host of this rank may pick the world (any with --playtest-unlocks).</summary>
        public static bool WorldOpen(ContentDb content, string mapId, int rank, bool allUnlocks) => allUnlocks || rank >= WorldRank(content, mapId);

        /// <summary>The index of the map played now in <see cref="WorldChoices"/>; −1 for a map not offered there.</summary>
        public int WorldChoice
        {
            get
            {
                for (int i = 0; i < WorldChoices.Count; i++) if (WorldChoices[i] == Map.Id) return i;
                return -1;
            }
        }

        private OpResult SetWorldOp(Player p, in SimRequest r)
        {
            if (Run.Phase != RunPhase.Lobby) return OpResult.WrongPhase;
            if (p.Id != Run.Host) return OpResult.NotHost;
            switch ((RunMode)r.X)
            {
                case RunMode.Practice:
                    return EnterMode(RunMode.Practice, WorldChoices[0], RunModes.PracticeSeed(Content), 0, 0);
                case RunMode.Daily:
                    ushort day = RunModes.Day(Settings.UtcNow());
                    return EnterMode(RunMode.Daily, WorldGenerator.SmallIsland, RunModes.DailySeed(day), RunModes.DailyStamps(Content), day);
                case RunMode.Normal:
                    break;
                default:
                    return OpResult.BadRequest;
            }
            if (r.Count >= WorldChoices.Count) return OpResult.BadRequest;
            // Another world or seed is a run of the host's own again; the practice's or the day's stamps go with it.
            if (Run.Mode != RunMode.Normal)
            {
                Run.Mode = RunMode.Normal;
                Run.DailyDay = 0;
                SetStamps(0);
            }
            string mapId = WorldChoices[r.Count];
            if (!WorldGenerator.Generates(mapId) && !Content.Maps.ContainsKey(mapId)) return OpResult.BadRequest;
            // Doc 01 §5.2: an archetype the host's Postal Rank has not opened.
            if (!WorldOpen(Content, mapId, p.Rank, (p.Playtest & PlaytestFlags.AllUnlocks) != 0)) return OpResult.RankTooLow;
            if (mapId == Map.Id && (r.EntryId == Settings.Seed || !WorldGenerator.Generates(mapId)))
            {
                // A file's map is the same whatever the seed; the seed still sets the mail and the raids.
                if (r.EntryId != Settings.Seed) ChangeWorld(Map, r.EntryId);
                return OpResult.Ok;
            }
            WorldMap map;
            try { map = WorldGenerator.Resolve(Content, mapId, r.EntryId); }
            catch (InvalidOperationException) { return OpResult.BadRequest; }
            ChangeWorld(map, r.EntryId);
            return OpResult.Ok;
        }

        /// <summary>
        /// Doc 09 §6's practice run (the test street, the practice seed, no stamps) or doc 01 §5.3's Daily Seed (the Small
        /// Island from the UTC day's seed, the daily stamps whatever the host's rank).
        /// </summary>
        private OpResult EnterMode(RunMode mode, string mapId, uint seed, byte stamps, ushort day)
        {
            WorldMap map;
            try { map = Map.Id == mapId && Settings.Seed == seed ? Map : WorldGenerator.Resolve(Content, mapId, seed); }
            catch (InvalidOperationException) { return OpResult.BadRequest; }
            Run.Mode = mode;
            Run.DailyDay = day;
            SetStamps(stamps);
            ChangeWorld(map, seed);
            return OpResult.Ok;
        }

        /// <summary>Puts the world onto another map and seed. Only between runs, in the Lobby.</summary>
        public void ChangeWorld(WorldMap map, uint seed)
        {
            foreach (var p in _players)
                if (p.OpenContainer != 0) CloseExternal(p, notify: true);
            Settings.MapId = map.Id;
            Settings.Seed = seed;
            Map = map;

            foreach (var chest in _mapChests) _containers.Remove(chest.Id);
            _mapChests.Clear();
            foreach (var chest in Map.Chests)
                _mapChests.Add(CreateContainer(chest.Container.Id, Core.EntityId.None, chest.X, chest.Z).Container);
            var intake = _containers[Intake.Id];
            intake.X = Map.PostOffice.IntakeX;
            intake.Z = Map.PostOffice.IntakeZ;
            Spawner = new MailSpawner(Content, Map, Intake, _entryIds, seed);
            ApplyStamps();
            Nodes = new NodeField(Content, Map);
            Constructs = new ConstructField(Map);
            ResetCombatRng();
            ClearPerks();
            ClearShop();

            foreach (var p in _players)
            {
                SeatPosition(p.Slot, out float x, out float z);
                p.Move = new PlayerMoveState
                {
                    X = x, Z = z, Y = Map.HeightAt(x, z), Grounded = true, Yaw = PlayerInput.QuantizeYaw(Map.PostOffice.SpawnYaw),
                };
            }
            var ev = new WorldChangedEvent { MapId = map.Id, Seed = seed, MapHash = map.MapHash };
            foreach (var chest in _mapChests) ev.ChestContainerIds.Add(chest.Id);
            Events.Add(ev);
        }
    }
}
