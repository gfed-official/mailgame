using System;
using System.Collections.Generic;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Net;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client
{
    /// <summary>A player as the client knows it: identity plus a short buffer of snapshot samples to interpolate.</summary>
    public sealed class ClientPlayer
    {
        private const int BufferSize = 8;

        private struct Sample
        {
            public uint Tick;
            public float X, Y, Z;
            public ushort Yaw;
        }

        public EntityId Id { get; }
        public string Name { get; }
        public byte Slot { get; }
        public byte HeldSlot { get; private set; }
        /// <summary>Ready flag of the Lobby and Prep, from the newest snapshot.</summary>
        public bool Ready { get; internal set; }
        public bool HasSamples => _count > 0;
        /// <summary>Health in 255ths from the newest snapshot; 0 = down.</summary>
        public byte Hp { get; private set; } = 255;
        public bool Dead => Hp == 0;
        /// <summary>Stands protected after a respawn, as of the newest snapshot (nit 17).</summary>
        public bool Protected { get; private set; }
        /// <summary>Has lost their connection and stands where they stood (doc 06 §6), as of the newest snapshot or event.</summary>
        public bool Away { get; internal set; }
        /// <summary>While away: the tick they are gone for good by.</summary>
        public uint AwayUntilTick { get; internal set; }
        /// <summary>Server tick of the snapshot in which the health last dropped (the hit flash).</summary>
        public uint HurtTick { get; private set; }
        /// <summary>Server tick of this player's last hammer swing or cannon shot, and which it was.</summary>
        public uint AttackTick { get; internal set; }
        public Weapon AttackWeapon { get; internal set; }
        /// <summary>Tick the player stands up again, while down.</summary>
        public uint RespawnTick { get; internal set; }

        private readonly Sample[] _samples = new Sample[BufferSize];
        private int _head;
        private int _count;

        public ClientPlayer(EntityId id, string name, byte slot)
        {
            Id = id; Name = name; Slot = slot;
        }

        public void Push(uint tick, in PlayerSnap snap)
        {
            // Snapshots are unreliable and unordered: keep only ones newer than the newest held.
            if (_count > 0 && tick <= _samples[(_head - 1 + BufferSize) % BufferSize].Tick) return;
            // A respawn is a jump to the Post Office, not a walk: nothing to interpolate from.
            if (Hp == 0 && snap.Hp != 0) _count = 0;
            if (snap.Hp < Hp) HurtTick = tick;
            Hp = snap.Hp;
            _samples[_head] = new Sample { Tick = tick, X = snap.X, Y = snap.Y, Z = snap.Z, Yaw = snap.Yaw };
            _head = (_head + 1) % BufferSize;
            if (_count < BufferSize) _count++;
            HeldSlot = snap.HeldSlot;
            Ready = snap.Ready;
            Protected = snap.Protected;
            Away = snap.Away;
        }

        /// <summary>Interpolated pose at a (fractional) server tick; clamps to the oldest / newest sample.</summary>
        public bool Interpolate(double tick, out float x, out float y, out float z, out float yawRadians)
        {
            x = y = z = yawRadians = 0f;
            if (_count == 0) return false;
            Sample newer = _samples[(_head - 1 + BufferSize) % BufferSize];
            Sample older = newer;
            for (int i = 1; i <= _count; i++)
            {
                var s = _samples[(_head - i + BufferSize) % BufferSize];
                older = s;
                if (s.Tick <= tick) break;
                newer = s;
            }
            float t = newer.Tick == older.Tick ? 0f : (float)Math.Clamp((tick - older.Tick) / (newer.Tick - older.Tick), 0.0, 1.0);
            x = older.X + (newer.X - older.X) * t;
            y = older.Y + (newer.Y - older.Y) * t;
            z = older.Z + (newer.Z - older.Z) * t;
            // Shortest-arc yaw: the difference of two u16 angles wraps correctly as a signed 16-bit value.
            short dyaw = (short)(newer.Yaw - older.Yaw);
            yawRadians = PlayerInput.YawToRadians((ushort)(older.Yaw + (int)(dyaw * t)));
            return true;
        }
    }

    /// <summary>An enemy as the client knows it: the newest snapshot's facts plus a few samples to interpolate.</summary>
    public sealed class ClientEnemy
    {
        private const int BufferSize = 6;

        private struct Sample
        {
            public uint Tick;
            public float X, Z, Yaw;
        }

        public ushort Id { get; }
        public EnemyDef Def { get; }
        public bool Mega { get; }
        public EnemyState State { get; private set; }
        /// <summary>Health in 255ths.</summary>
        public byte Hp { get; private set; } = 255;
        /// <summary>Server tick of the snapshot in which the health last dropped (the hit flash).</summary>
        public uint HurtTick { get; private set; }
        /// <summary>Server tick of the snapshot that first showed the current state (the windup's start).</summary>
        public uint StateTick { get; private set; }
        public uint LastTick { get; private set; }
        /// <summary>The newest known position, uninterpolated.</summary>
        public float X { get; private set; }
        public float Z { get; private set; }

        private readonly Sample[] _samples = new Sample[BufferSize];
        private int _head;
        private int _count;

        public ClientEnemy(ushort id, EnemyDef def, bool mega)
        {
            Id = id; Def = def; Mega = mega;
        }

        public void Push(uint tick, in EnemySnap snap)
        {
            if (_count > 0 && tick <= LastTick) return;
            if (_count > 0 && snap.Hp < Hp) HurtTick = tick;
            if (_count == 0 || snap.State != State) StateTick = tick;
            Hp = snap.Hp;
            State = snap.State;
            LastTick = tick;
            X = snap.X; Z = snap.Z;
            _samples[_head] = new Sample { Tick = tick, X = snap.X, Z = snap.Z, Yaw = snap.Yaw };
            _head = (_head + 1) % BufferSize;
            if (_count < BufferSize) _count++;
        }

        /// <summary>Interpolated pose at a (fractional) server tick; clamps to the oldest / newest sample.</summary>
        public void Interpolate(double tick, out float x, out float z, out float yawRadians)
        {
            Sample newer = _samples[(_head - 1 + BufferSize) % BufferSize];
            Sample older = newer;
            for (int i = 1; i <= _count; i++)
            {
                var s = _samples[(_head - i + BufferSize) % BufferSize];
                older = s;
                if (s.Tick <= tick) break;
                newer = s;
            }
            float t = newer.Tick == older.Tick ? 0f : (float)Math.Clamp((tick - older.Tick) / (newer.Tick - older.Tick), 0.0, 1.0);
            x = older.X + (newer.X - older.X) * t;
            z = older.Z + (newer.Z - older.Z) * t;
            float dyaw = MathF.IEEERemainder(newer.Yaw - older.Yaw, MathF.PI * 2f);
            yawRadians = older.Yaw + dyaw * t;
        }
    }

    /// <summary>Loot on the ground, from the drop event until it is taken or gone.</summary>
    public sealed class ClientLoot
    {
        public uint Id;
        public float X, Z;
        public byte EnemyKind;
        public bool Mega;
        public int Coins;
        public readonly List<LootItem> Items = new List<LootItem>();
        /// <summary>Server tick (the client's estimate) at which it fell.</summary>
        public double DroppedTick;
    }

    /// <summary>A death bag lying in the world (doc 01 §2.2): a container anyone in reach can open.</summary>
    public sealed class ClientBag
    {
        public uint ContainerId;
        /// <summary>Whose it was; None once they have left.</summary>
        public EntityId Owner;
        public float X, Z;
        public uint DespawnTick;
    }

    /// <summary>
    /// The client's presentation model (doc 07 §5): the subset of sim state this client knows.
    /// Written only by <see cref="ClientRuntime"/>; views and UI read it.
    /// </summary>
    public sealed class ClientWorld
    {
        public ContentDb Content { get; }
        public WorldMap Map { get; internal set; }
        public EntityId LocalId { get; internal set; }
        public uint Seed { get; internal set; }

        /// <summary>Every player in the game including the local one, and whoever is away and may come back.</summary>
        public Dictionary<EntityId, ClientPlayer> Players { get; } = new Dictionary<EntityId, ClientPlayer>();
        /// <summary>Whom the last <see cref="PlayerLeftEvent"/> took out of <see cref="Players"/>, for who tells of it.</summary>
        public ClientPlayer? LastLeft { get; internal set; }

        /// <summary>Predicted movement state of the local player.</summary>
        public PlayerMoveState Local;
        public bool HasLocal { get; internal set; }
        public float LocalSpeedMult { get; internal set; } = 1f;
        /// <summary>The local player's health, exact (other players' comes in 255ths).</summary>
        public float LocalHp { get; internal set; }
        public float LocalMaxHp { get; }
        /// <summary>Server tick of the snapshot in which the local health last dropped.</summary>
        public uint LocalHurtTick { get; internal set; }
        /// <summary>While the local player is down: the tick they stand up again.</summary>
        public uint LocalRespawnTick { get; internal set; }
        public double RespawnSecondsLeft =>
            Local.Dead ? Math.Max(0.0, SimConstants.TicksToSeconds((long)LocalRespawnTick - (long)Math.Floor(ServerTick))) : 0.0;

        /// <summary>The tick the local player's protection after a respawn ends by (their own first blow ends it sooner).</summary>
        public uint LocalProtectedUntilTick { get; internal set; }
        /// <summary>True while nothing hurts the local player and no raider goes for them.</summary>
        public bool LocalProtected => HasLocal && !Local.Dead && Players.TryGetValue(LocalId, out var me) && me.Protected;
        public double ProtectedSecondsLeft =>
            LocalProtected ? Math.Max(0.0, SimConstants.TicksToSeconds((long)LocalProtectedUntilTick - (long)Math.Floor(ServerTick))) : 0.0;

        /// <summary>Enemies of the newest enemy list, by the low 16 bits of their id.</summary>
        public Dictionary<ushort, ClientEnemy> Enemies { get; } = new Dictionary<ushort, ClientEnemy>();
        /// <summary>Loot bundles on the ground.</summary>
        public Dictionary<uint, ClientLoot> Loot { get; } = new Dictionary<uint, ClientLoot>();
        /// <summary>Death bags lying about, by container id.</summary>
        public Dictionary<uint, ClientBag> Bags { get; } = new Dictionary<uint, ClientBag>();
        public RaidState Raid => Global.Raid;
        /// <summary>Warning: the tick of the first spawn. Fleeing: the tick the last raider is gone by.</summary>
        public uint RaidTick { get; internal set; }
        /// <summary>Seconds until the raid's first pulse, while the warning is up.</summary>
        public double RaidSecondsLeft =>
            Raid == RaidState.Warning ? Math.Max(0.0, SimConstants.TicksToSeconds((long)RaidTick - (long)Math.Floor(ServerTick))) : 0.0;
        /// <summary>Kills and looted coins of the local player this run (from the events).</summary>
        public int LocalKills { get; internal set; }
        public int LocalLootCoins { get; internal set; }

        /// <summary>
        /// Confirmed mirrors of the containers this client is allowed to see: body containers plus the
        /// open one. They change only by server deltas and are hash-checked on every version.
        /// </summary>
        public Dictionary<uint, GridContainer> Containers { get; } = new Dictionary<uint, GridContainer>();
        /// <summary>Requests on their way, shown as if they had already succeeded.</summary>
        public InventoryPrediction Prediction { get; }
        public uint HotbarId { get; internal set; }
        public uint InventoryId { get; internal set; }
        /// <summary>The hotbar as the player sees it: confirmed state plus the moves still on their way.</summary>
        public GridContainer Hotbar => Prediction.View(HotbarId)!;
        public GridContainer Inventory => Prediction.View(InventoryId)!;
        public uint IntakeContainerId { get; internal set; }
        /// <summary>The containers of the map's chests, in the order of <see cref="WorldMap.Chests"/>.</summary>
        public IReadOnlyList<uint> ChestContainerIds { get; internal set; } = Array.Empty<uint>();
        /// <summary>The external container being viewed, 0 if none.</summary>
        public uint OpenContainerId { get; internal set; }

        public GlobalState Global { get; internal set; }
        /// <summary>The server tick <see cref="Global"/>'s phase clock was last read at.</summary>
        public uint GlobalTick { get; internal set; }
        /// <summary>Estimated current server tick (fractional), advanced locally between snapshots.</summary>
        public double ServerTick { get; internal set; }

        public ClientWorld(ContentDb content, WorldMap map)
        {
            Content = content;
            Map = map;
            LocalHp = LocalMaxHp = content.Balance.GetFloat("playerHp");
            Prediction = new InventoryPrediction(content, this);
        }

        public GridContainer? OpenContainer => OpenContainerId != 0 ? Prediction.View(OpenContainerId) : null;

        /// <summary>Seconds until a bag is gone and its mail back in the Intake.</summary>
        public double BagSecondsLeft(ClientBag bag) =>
            Math.Max(0.0, SimConstants.TicksToSeconds((long)bag.DespawnTick - (long)Math.Floor(ServerTick)));

        /// <summary>The local player's own bag, the one with the least time left; null if none lies about.</summary>
        public ClientBag? LocalBag
        {
            get
            {
                ClientBag? first = null;
                foreach (var bag in Bags.Values)
                    if (bag.Owner == LocalId && (first == null || bag.DespawnTick < first.DespawnTick)) first = bag;
                return first;
            }
        }

        /// <summary>
        /// Seconds left on the phase clock, derived from ticks (doc 07 §4.1). A stopped shift clock
        /// (playtest nit 10) keeps what it had when the server last said so.
        /// </summary>
        public double PhaseSecondsLeft =>
            Math.Max(0.0, SimConstants.TicksToSeconds((long)Global.PhaseEndTick - (ClockStopped ? GlobalTick : (long)Math.Floor(ServerTick))));

        /// <summary>The Delivery clock stands because a player asked for it (<see cref="PlaytestFlags.NoTimer"/>).</summary>
        public bool ClockStopped => Global.ClockStopped && Global.Phase == RunPhase.Delivery;
        /// <summary>A missed quota does not end the run because a player asked for it (<see cref="PlaytestFlags.NoQuota"/>).</summary>
        public bool QuotaWaived => Global.QuotaWaived;

        /// <summary>False in the phases that wait for people instead of a clock (Lobby, Results).</summary>
        public bool PhaseHasClock => Global.PhaseEndTick != 0;

        /// <summary>The newest Payday summary of the current run; null in the Lobby.</summary>
        public ShiftSummary? LastSummary { get; internal set; }
        /// <summary>The run's results while in Results; null otherwise.</summary>
        public RunResults? Results { get; internal set; }

        /// <summary>Solo pause (doc 01 §1): the server's tick, and with it this client's, stands still.</summary>
        public bool Paused { get; internal set; }

        public bool IsHost => !LocalId.IsNone && Global.Host == LocalId;
        /// <summary>Only the host of a solo run can pause it.</summary>
        public bool CanPause => IsHost && Players.Count == 1;
        public bool LocalReady => Players.TryGetValue(LocalId, out var p) && p.Ready;

        public int ReadyCount
        {
            get
            {
                int n = 0;
                foreach (var p in Players.Values) if (p.Ready) n++;
                return n;
            }
        }

        /// <summary>The players who are connected: the ones Prep waits for.</summary>
        public int PresentCount
        {
            get
            {
                int n = 0;
                foreach (var p in Players.Values) if (!p.Away) n++;
                return n;
            }
        }
    }
}
