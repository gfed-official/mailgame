using System;
using System.Collections.Generic;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Net;
using PerformativeMail.Sim.Perks;
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
        /// <summary>The tool in their hand, as its item index + 1; 0 for none.</summary>
        public byte HeldTool { get; private set; }
        /// <summary>The starting kit picked in the Lobby, an index into ContentDb.Kits.</summary>
        public byte Kit { get; private set; }
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
        /// <summary>Their personal perks of the run, one entry per stack, in the order they were taken.</summary>
        public List<PerkDef> Perks { get; } = new List<PerkDef>();
        /// <summary>The card of the Draft under way they have taken; -1: none yet.</summary>
        public int DraftPick { get; internal set; } = -1;
        /// <summary>The perk of that card: after a reroll the cards are not the same for everybody.</summary>
        public PerkDef? DraftPerk { get; internal set; }
        /// <summary>On foot 0; in a saddle the vehicle's index + 1, as of the newest snapshot.</summary>
        public byte Ride { get; private set; }
        /// <summary>The way the vehicle they ride points, a yaw, as of the newest snapshot.</summary>
        public float Heading { get; private set; }

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
            HeldTool = snap.HeldTool;
            Kit = snap.Kit;
            Ready = snap.Ready;
            Protected = snap.Protected;
            Away = snap.Away;
            Ride = snap.Ride;
            Heading = snap.Heading;
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
    /// A vehicle of the run (doc 04 §3.1) as the client knows it: where it was parked, or whose
    /// saddle it is under; a ridden one is drawn where its rider is.
    /// </summary>
    public sealed class ClientVehicle
    {
        public EntityId Id;
        public VehicleDef Def = null!;
        public float X, Z;
        /// <summary>The way it points while parked, a yaw (0 looks along −Z).</summary>
        public float Heading;
        public uint CargoContainer;
        /// <summary>Who rides it; None while it stands parked.</summary>
        public EntityId Rider;
        /// <summary>Seconds of driving in its tank as the server last said (counted down here while the local player drives it).</summary>
        public float Fuel;
        /// <summary>A depot's NPC driver drives it (doc 04 §3.2): where it is comes with every snapshot.</summary>
        public bool NpcDriving;
        /// <summary>A hired driver is in it: at the wheel, or beside a carrier who took it.</summary>
        public bool NpcAboard;
        public bool Ridden => !Rider.IsNone;
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
        public float LocalMaxHp { get; private set; }
        /// <summary>Server tick of the snapshot in which the local health last dropped.</summary>
        public uint LocalHurtTick { get; internal set; }
        /// <summary>While the local player is down: the tick they stand up again.</summary>
        public uint LocalRespawnTick { get; internal set; }
        public double RespawnSecondsLeft =>
            Local.Dead ? Math.Max(0.0, SimConstants.TicksToSeconds((long)LocalRespawnTick - (long)Math.Floor(ServerTick))) : 0.0;

        /// <summary>The tick the local player's protection after a respawn ends by (their own first blow ends it sooner).</summary>
        public uint LocalProtectedUntilTick { get; internal set; }
        /// <summary>Where a playtest last parked the local player before their mailbox (<see cref="HasPark"/>).</summary>
        public float ParkX { get; internal set; }
        public float ParkZ { get; internal set; }
        public bool HasPark { get; internal set; }
        /// <summary>True while the local player stands where the playtest parked them (the server's guard around it).</summary>
        public bool LocalAtPark
        {
            get
            {
                if (!HasPark || !HasLocal) return false;
                float dx = Local.X - ParkX, dz = Local.Z - ParkZ;
                return dx * dx + dz * dz <= PlaytestSpot.GuardRadius * PlaytestSpot.GuardRadius;
            }
        }
        /// <summary>True while nothing hurts the local player and no raider goes for them.</summary>
        public bool LocalProtected => HasLocal && !Local.Dead && Players.TryGetValue(LocalId, out var me) && me.Protected;
        public double ProtectedSecondsLeft =>
            LocalProtected ? Math.Max(0.0, SimConstants.TicksToSeconds((long)LocalProtectedUntilTick - (long)Math.Floor(ServerTick))) : 0.0;

        /// <summary>How many of the enemies stand within an Alarm Post's reach (doc 05 §4): marked for everybody.</summary>
        public int MarkedEnemies
        {
            get
            {
                int n = 0;
                if (Constructs.All.Count == 0) return 0;
                foreach (var e in Enemies.Values)
                    if (RaidRules.Revealed(Constructs, e.X, e.Z)) n++;
                return n;
            }
        }

        /// <summary>Enemies of the newest enemy list, by the low 16 bits of their id.</summary>
        public Dictionary<ushort, ClientEnemy> Enemies { get; } = new Dictionary<ushort, ClientEnemy>();
        /// <summary>Loot bundles on the ground.</summary>
        public Dictionary<uint, ClientLoot> Loot { get; } = new Dictionary<uint, ClientLoot>();
        /// <summary>Death bags lying about, by container id.</summary>
        public Dictionary<uint, ClientBag> Bags { get; } = new Dictionary<uint, ClientBag>();
        /// <summary>The vehicles of the run, by id.</summary>
        public Dictionary<EntityId, ClientVehicle> Vehicles { get; } = new Dictionary<EntityId, ClientVehicle>();

        /// <summary>The vehicle the local player sits on, null on foot.</summary>
        public ClientVehicle? LocalRide
        {
            get
            {
                if (!Local.Riding) return null;
                foreach (var v in Vehicles.Values) if (v.Rider == LocalId) return v;
                return null;
            }
        }
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
            Nodes = new NodeField(content, map);
            Constructs = new ConstructField(map);
        }

        public GridContainer? OpenContainer => OpenContainerId != 0 ? Prediction.View(OpenContainerId) : null;

        /// <summary>
        /// The host picked another world in the Lobby: this map and seed from now on, its nodes
        /// whole, nothing built, nothing parked or lying about, its chests the containers named.
        /// </summary>
        internal void ReplaceMap(WorldMap map, uint seed, IReadOnlyList<uint> chestContainerIds)
        {
            foreach (uint id in ChestContainerIds) Containers.Remove(id);
            Map = map;
            Seed = seed;
            ChestContainerIds = new List<uint>(chestContainerIds).ToArray();
            Nodes = new NodeField(Content, map);
            Constructs = new ConstructField(map);
            Vehicles.Clear();
            Bags.Clear();
            Loot.Clear();
            Enemies.Clear();
        }

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

        // --- perks (doc 01 §2.5) ----------------------------------------------------------------

        /// <summary>The team perks of the run, in the order they were taken.</summary>
        public List<PerkDef> TeamPerks { get; } = new List<PerkDef>();
        /// <summary>The cards of the Draft under way, the same for everybody; empty outside it.</summary>
        public List<PerkDef> DraftOffer { get; } = new List<PerkDef>();

        private static readonly List<PerkDef> NoPerks = new List<PerkDef>();
        private readonly StatSheet _teamStats = new StatSheet();
        private readonly StatSheet _localStats = new StatSheet();

        /// <summary>The local player's personal perks.</summary>
        public IReadOnlyList<PerkDef> LocalPerks => Players.TryGetValue(LocalId, out var me) ? me.Perks : NoPerks;
        /// <summary>The card the local player has taken in the Draft under way; -1: none yet.</summary>
        public int LocalDraftPick => Players.TryGetValue(LocalId, out var me) ? me.DraftPick : -1;

        /// <summary>A stat as the local player has it: the team's perks and their own over the base, as the server reckons it.</summary>
        public float LocalStat(Stat stat, float baseValue) => _teamStats.Resolve(stat, baseValue, _localStats);

        /// <summary>Whether a player may take the perk, by the rules of the server's SimWorld.CanTake.</summary>
        public OpResult CanTake(ClientPlayer player, PerkDef perk)
        {
            if (perk.Team)
            {
                if (TeamPerks.Contains(perk)) return OpResult.PerkTaken;
                foreach (var other in Players.Values)
                    if (perk.ConflictsWithAny(other.Perks)) return OpResult.PerkExcluded;
            }
            else
            {
                if (perk.StacksIn(player.Perks) >= perk.MaxStacks) return OpResult.PerkMaxed;
                if (perk.ConflictsWithAny(player.Perks)) return OpResult.PerkExcluded;
            }
            return perk.ConflictsWithAny(TeamPerks) ? OpResult.PerkExcluded : OpResult.Ok;
        }

        /// <summary>What stands between the local player and a card of the Draft; Ok: it is theirs to take.</summary>
        public OpResult CardState(int card)
        {
            if (card < 0 || card >= DraftOffer.Count || !Players.TryGetValue(LocalId, out var me)) return OpResult.BadRequest;
            return me.DraftPick >= 0 ? OpResult.AlreadyPicked : CanTake(me, DraftOffer[card]);
        }

        /// <summary>The first card the local player could still take; -1 if there is none, or they have chosen.</summary>
        public int FirstOpenCard
        {
            get
            {
                for (int i = 0; i < DraftOffer.Count; i++)
                    if (CardState(i) == OpResult.Ok) return i;
                return -1;
            }
        }

        /// <summary>The Draft's cards lie before the local player and one of them is still theirs to take: a click or a key takes it.</summary>
        public bool ChoosingCard => Global.Phase == RunPhase.Draft && FirstOpenCard >= 0;

        /// <summary>After a hot reload: the most health from the balance again, the perks' sheets from their new values.</summary>
        internal void ContentReloaded()
        {
            LocalMaxHp = Content.Balance.GetFloat("playerHp");
            _teamStats.Clear();
            foreach (var perk in TeamPerks) _teamStats.Apply(perk);
            _localStats.Clear();
            foreach (var perk in LocalPerks) _localStats.Apply(perk);
        }

        internal void AddPerk(EntityId holder, PerkDef perk)
        {
            if (perk.Team)
            {
                if (TeamPerks.Contains(perk)) return;
                TeamPerks.Add(perk);
                _teamStats.Apply(perk);
                return;
            }
            if (!Players.TryGetValue(holder, out var player)) return;
            player.Perks.Add(perk);
            if (holder == LocalId) _localStats.Apply(perk);
        }

        /// <summary>The Draft is over, or another begins: no cards, nobody has chosen.</summary>
        internal void ClearDraft()
        {
            DraftOffer.Clear();
            foreach (var p in Players.Values)
            {
                p.DraftPick = -1;
                p.DraftPerk = null;
            }
        }

        /// <summary>The Draft's rerolls the local player has left in the run, as the server last said (doc 01 §2.5).</summary>
        public int RerollsLeft { get; internal set; }

        /// <summary>The local player may ask for new cards: a Draft is on, they have not chosen, and a reroll is left.</summary>
        public bool CanReroll => Global.Phase == RunPhase.Draft && DraftOffer.Count > 0 && LocalDraftPick < 0 && RerollsLeft > 0;

        /// <summary>A run ends without perks.</summary>
        internal void ClearPerks()
        {
            TeamPerks.Clear();
            _teamStats.Clear();
            _localStats.Clear();
            foreach (var p in Players.Values) p.Perks.Clear();
            ClearDraft();
        }

        // --- the shop (doc 03 §4.4, doc 09 §2.8) --------------------------------------------------

        /// <summary>The shift's specials, in the order of the catalogue.</summary>
        public List<ShopItemDef> ShopSpecials { get; } = new List<ShopItemDef>();
        /// <summary>What the team has bought of the entries sold once per run.</summary>
        public List<ShopItemDef> ShopOwned { get; } = new List<ShopItemDef>();
        /// <summary>The local player stands at the counter with its cards up. Theirs alone: the server keeps no such state.</summary>
        public bool ShopOpen { get; internal set; }
        /// <summary>Purchases on their way: the card waits for the server's word.</summary>
        internal List<ShopItemDef> ShopPending { get; } = new List<ShopItemDef>();

        private readonly List<ShopItemDef> _shelf = new List<ShopItemDef>();

        /// <summary>The counter serves right now: the map has one, and it is Prep or Payday.</summary>
        public bool ShopServes => Map.PostOffice.HasShop && ShopRules.IsOpen(Global.Phase);

        /// <summary>
        /// What lies on the counter this shift, in the order of the catalogue: the fixed stock and
        /// the rotating entries among the specials. The card's number key is its place here.
        /// </summary>
        public IReadOnlyList<ShopItemDef> ShopShelf
        {
            get
            {
                _shelf.Clear();
                foreach (var def in Content.ShopItems)
                    if (!def.KitOnly && (!def.Rotating || ShopSpecials.Contains(def))) _shelf.Add(def);
                return _shelf;
            }
        }

        public bool IsSpecial(ShopItemDef def) => ShopSpecials.Contains(def);
        public bool IsBuying(ShopItemDef def) => ShopPending.Contains(def);

        /// <summary>What the entry costs this shift, as the server reckons it.</summary>
        public int ShopPrice(ShopItemDef def) => ShopRules.Price(def, IsSpecial(def), ShopRules.DiscountPermille(Content.Balance));

        /// <summary>
        /// What stands between the local player and an entry, by the rules of the server's BuyOp:
        /// Ok means a request to buy it would be granted as things stand.
        /// </summary>
        public OpResult ShopState(ShopItemDef def)
        {
            if (!ShopServes) return OpResult.WrongPhase;
            var verdict = ShopRules.Check(def, Global.Shift, IsSpecial(def), ShopOwned.Contains(def));
            if (verdict != OpResult.Ok) return verdict;
            if (def.Requires != null && !ShopOwned.Contains(def.Requires)) return OpResult.NeedsBlueprint;
            int same = 0;
            foreach (var v in Vehicles.Values) if (v.Def == def.Vehicle) same++;
            verdict = ShopRules.VehicleCheck(def, same, PresentCount);
            if (verdict != OpResult.Ok) return verdict;
            if (Global.Wallet < ShopPrice(def)) return OpResult.CantAfford;
            if (def.Kind != ShopKind.Item || def.Item == null || HotbarId == 0) return OpResult.Ok;
            bool hotbarFirst = ShopRules.HotbarFirst(def.Item);
            var first = hotbarFirst ? Hotbar : Inventory;
            var second = hotbarFirst ? Inventory : Hotbar;
            return ShopRules.Fits(Content, first, second, def.Item, def.Count) ? OpResult.Ok : OpResult.DoesNotFit;
        }

        /// <summary>
        /// The stack in a hotbar slot when it is something to use (doc 05 §1.1: a Bandage, Berries)
        /// and the server knows it by name already; else null.
        /// </summary>
        public GridEntry? UsableAt(int slot)
        {
            if (HotbarId == 0 || slot <= 0) return null;
            var hotbar = Hotbar;
            var entry = slot < hotbar.Cols ? hotbar.EntryAt(slot, 0) : null;
            if (entry == null || entry.Stack.Kind != StackKind.Item || InventoryPrediction.IsPrivate(entry)) return null;
            return Content.Items[entry.Stack.DefIndex].Heal > 0 ? entry : null;
        }

        /// <summary>A run ends owning nothing, and the next draws its own specials.</summary>
        internal void ClearShop()
        {
            ShopSpecials.Clear();
            ShopOwned.Clear();
            ShopPending.Clear();
            ShopOpen = false;
        }

        // --- the filters of sorters and splitters (doc 04 §2.3, §2.4, doc 09 §2.7) -----------------

        /// <summary>When each inserter last moved a piece (doc 04 §2.5), by tile index, as a server tick: its arm swings from it.</summary>
        public Dictionary<int, (double Tick, ushort Kind)> InserterSwings { get; } = new Dictionary<int, (double, ushort)>();

        /// <summary>When each turret last fired (doc 05 §4), by tile index, as a server tick, and where it aimed: its gun turns there and the shot is drawn.</summary>
        public Dictionary<int, (double Tick, float X, float Z)> TurretShots { get; } = new Dictionary<int, (double, float, float)>();

        /// <summary>What each Vehicle Depot's driver does, as the server last said, by the depot's tile index.</summary>
        public Dictionary<int, DriverEvent> Drivers { get; } = new Dictionary<int, DriverEvent>();

        /// <summary>The team's pings standing now (doc 09 §2.6), oldest first; gone by their expiry tick.</summary>
        public List<PingEvent> Pings { get; } = new List<PingEvent>();
        /// <summary>A ping a carrier keeps at most at once: a fourth takes the place of their oldest.</summary>
        public const int PingsEach = 3;

        /// <summary>Drops the pings whose time is up.</summary>
        public void PrunePings() => Pings.RemoveAll(p => ServerTick >= p.ExpiresTick);

        public void AddPing(PingEvent e)
        {
            int mine = 0;
            foreach (var p in Pings) if (p.Player == e.Player) mine++;
            if (mine >= PingsEach) Pings.Remove(Pings.Find(p => p.Player == e.Player)!);
            Pings.Add(e);
        }

        /// <summary>The map's Mail chip: pieces waiting for each house in the Intake and the storage, as the server last said.</summary>
        public Dictionary<ushort, int> MailWaiting { get; } = new Dictionary<ushort, int>();

        public DriverEvent? DriverAt(Construct depot) =>
            Drivers.TryGetValue(depot.Tz * Map.Cols + depot.Tx, out var d) && d.State != DriverState.None ? d : null;

        /// <summary>The sorter or splitter whose filters the local player has up; null for none.</summary>
        public Construct? FilterTarget { get; internal set; }
        /// <summary>What the server last said of it (while the filters are up); null before the first word.</summary>
        public SorterStatusEvent? FilterStatus { get; internal set; }
        /// <summary>Filters asked for and not yet confirmed: output × slot → the filter.</summary>
        internal Dictionary<int, MailFilter> FilterPending { get; } = new Dictionary<int, MailFilter>();

        /// <summary>
        /// What an output of <paramref name="built"/> may be set to, in the order the panel steps
        /// through them (doc 04 §2.4: "a UI listing only unlocked addresses"): nothing, then for a
        /// sorter or a pipe outlet the streets and districts open by this shift, then the kinds of
        /// mail; a splitter and an inserter filter by kind only.
        /// </summary>
        public List<MailFilter> FilterChoices(Construct built)
        {
            var choices = new List<MailFilter> { MailFilter.Empty };
            if (built.Def.IsPort)
            {
                // A captain's stops (doc 04 §3.3): the destinations a boat can lie at — the docks — open by this shift.
                int now = Math.Max(1, (int)Global.Shift);
                float reach = Content.Balance.GetFloat("dockZoneReach");
                foreach (var d in Map.Destinations)
                    if (Map.OpensAt(d) <= now && WaterNav.Berth(Map, Constructs, d, reach, out _, out _)) choices.Add(new MailFilter(FilterKind.Address, d.Id));
                return choices;
            }
            if (built.Def.HasConsole)
            {
                // A route's stops (doc 04 §3.2): the houses open by this shift, then their streets and districts.
                int open = Math.Max(1, (int)Global.Shift);
                foreach (var d in Map.Destinations)
                    if (DistrictOpen(d.Address.District, open)) choices.Add(new MailFilter(FilterKind.Address, d.Id));
                foreach (var street in Map.Streets)
                    if (DistrictOpen(street.District, open)) choices.Add(new MailFilter(FilterKind.Street, street.Id));
                foreach (var d in Map.Districts)
                    if (d.UnlockShift <= open) choices.Add(new MailFilter(FilterKind.District, d.Id));
                return choices;
            }
            if (built.Def.FiltersAddresses)
            {
                int shift = Math.Max(1, (int)Global.Shift);
                foreach (var street in Map.Streets)
                    if (DistrictOpen(street.District, shift)) choices.Add(new MailFilter(FilterKind.Street, street.Id));
                foreach (var d in Map.Districts)
                    if (d.UnlockShift <= shift) choices.Add(new MailFilter(FilterKind.District, d.Id));
            }
            foreach (var kind in Content.MailKinds) choices.Add(new MailFilter(FilterKind.Kind, kind.Index));
            return choices;
        }

        private bool DistrictOpen(int district, int shift)
        {
            foreach (var d in Map.Districts)
                if (d.Id == district) return d.UnlockShift <= shift;
            // A map without districts has every street open.
            return true;
        }

        /// <summary>An output's filter as the panel shows it: the one on its way, else the confirmed one.</summary>
        public MailFilter ShownFilter(Construct built, int output, int slot) =>
            FilterPending.TryGetValue(output * 16 + slot, out var pending) ? pending : built.Filter(output, slot);

        // --- resource nodes (doc 02 §3.9) --------------------------------------------------------

        /// <summary>What the map's trees, bushes and boulders have left, as the server last said.</summary>
        public NodeField Nodes { get; private set; }

        /// <summary>
        /// What stands between the local player and a hit on a node with the selected hotbar slot
        /// in the hand, by the rules of the server's HarvestOp, distance aside: Ok means a swing
        /// would land as things stand.
        /// </summary>
        public OpResult NodeState(int node, int heldSlot)
        {
            var def = Nodes.Def(node);
            if (def == null || HotbarId == 0) return OpResult.BadRequest;
            if (!HarvestRules.IsOpen(Global.Phase)) return OpResult.WrongPhase;
            return HarvestRules.Check(Content, def, Nodes.HitsLeft(node), HarvestRules.Held(Content, Hotbar, heldSlot), Hotbar, Inventory);
        }

        // --- building by hand (doc 04 §1) ---------------------------------------------------------

        /// <summary>What the carriers have built, as the server last said.</summary>
        public ConstructField Constructs { get; private set; }

        /// <summary>
        /// The belts as this client sees them (doc 07 §8 visual belt sim): compiled from
        /// <see cref="Constructs"/> as the server compiles them, the items moved here each tick and
        /// handed on or taken off only by the server's events.
        /// </summary>
        public Sim.Automation.BeltNetwork Belts
        {
            get
            {
                if (_belts == null || !ReferenceEquals(_beltsField, Constructs))
                {
                    _belts = new Sim.Automation.BeltNetwork(Content, Map, Constructs);
                    _beltsField = Constructs;
                }
                _belts.Compile();
                return _belts;
            }
        }

        private Sim.Automation.BeltNetwork? _belts;
        private ConstructField? _beltsField;

        /// <summary>
        /// The pipes as this client sees them: compiled from <see cref="Constructs"/> as the server
        /// compiles them, capsules sent and taken off by the server's events, moved here each tick.
        /// </summary>
        public Sim.Automation.PipeNetwork Pipes
        {
            get
            {
                if (_pipes == null || !ReferenceEquals(_pipesField, Constructs))
                {
                    _pipes = new Sim.Automation.PipeNetwork(Content, Map, Constructs);
                    _pipesField = Constructs;
                }
                _pipes.Compile();
                return _pipes;
            }
        }

        private Sim.Automation.PipeNetwork? _pipes;
        private ConstructField? _pipesField;
        /// <summary>Capsules the server sent that this client's pipes had no way for.</summary>
        public int PipeMismatches { get; internal set; }
        /// <summary>Checksums of the server's that did not match this client's segments, and the resyncs asked for.</summary>
        public int BeltMismatches { get; internal set; }

        /// <summary>A belt item as the server sent it.</summary>
        public Sim.Automation.BeltItem BeltItemOf(in BeltItemInfo info)
        {
            var mail = new Sim.Mail.MailItem { Id = info.MailId, Kind = info.Kind, Address = new AddressId(info.AddressKey, info.Unit) };
            float length = info.Kind < Content.MailKinds.Count ? Content.MailKinds[info.Kind].BeltLength : 0.5f;
            return new Sim.Automation.BeltItem { Mail = mail, Lane = info.Lane, Pos = info.Pos, Length = length };
        }

        /// <summary>What stood on the tile of the last ConstructRemoved, for whoever hears of it after it is gone.</summary>
        public Construct? LastRemoved { get; internal set; }

        /// <summary>
        /// What stands between the local player and a recipe, wherever they would build it, by the
        /// rules of the server's PlaceConstructOp: Ok means the team may build it and the pockets
        /// hold what it takes.
        /// </summary>
        public OpResult RecipeState(RecipeDef recipe)
        {
            if (HotbarId == 0) return OpResult.BadRequest;
            if (!BuildRules.IsOpen(Global.Phase)) return OpResult.WrongPhase;
            var verdict = BuildRules.CheckRecipe(recipe, Global.Shift, recipe.Blueprint == null || ShopOwned.Contains(recipe.Blueprint) || Unlimited);
            if (verdict != OpResult.Ok) return verdict;
            return Unlimited || BuildRules.HasMaterials(recipe, Hotbar, Inventory) ? OpResult.Ok : OpResult.MissingMaterials;
        }

        /// <summary>The debug world's toggle (design choice 119): every blueprint owned, no materials taken, as on the server.</summary>
        public bool Unlimited => Map.Debug is { Unlimited: true };

        /// <summary>
        /// What stands between the local player and a recipe built on a tile, in the order the
        /// server asks: Ok means a request to build it there would be granted as things stand.
        /// </summary>
        public OpResult PlaceState(RecipeDef recipe, int tx, int tz, int rotation = 0)
        {
            var state = RecipeState(recipe);
            if (state != OpResult.Ok && state != OpResult.MissingMaterials) return state;
            var tile = BuildRules.CheckPlacement(Map, Constructs, recipe.Building, tx, tz, rotation);
            if (tile != OpResult.Ok) return tile;
            if (!BuildRules.InRange(tx, tz, Local.X, Local.Z, Content.Balance.GetFloat("buildRange"))) return OpResult.OutOfRange;
            var def = recipe.Building;
            if (def.IsVehicleDepot && VehicleDepotCount() >= VehicleDepotLimit) return OpResult.DepotLimit;
            // What lies flat is laid under the feet.
            if (def.Solid)
                for (int i = 0; i < def.TileCount; i++)
                {
                    var (x, z) = Construct.TileOf(tx, tz, rotation, i % def.Length, i / def.Length);
                    if (TileOccupied(x, z)) return OpResult.Occupied;
                }
            return state;
        }

        /// <summary>Doc 04 §3.2 "one depot per 2 players rounded up", of those in the game as the server's PresentCount.</summary>
        public int VehicleDepotLimit
        {
            get
            {
                int present = 0;
                foreach (var p in Players.Values) if (!p.Away) present++;
                return Math.Max(1, (present + 1) / 2);
            }
        }

        public int VehicleDepotCount()
        {
            int n = 0;
            foreach (var c in Constructs.All) if (c.Def.IsVehicleDepot) n++;
            return n;
        }

        /// <summary>
        /// A drag line's tiles (doc 04 §1.1) and what each would come to, as the server's
        /// PlaceLineOp goes: every tile by itself, and the materials spent from the start of the
        /// line on, so the tiles past what the pockets hold are MissingMaterials. False (no tiles)
        /// for a line that is no row or column, or too long.
        /// </summary>
        public bool LineStates(RecipeDef recipe, int x0, int z0, int x1, int z1, List<(int Tx, int Tz, OpResult State)> states)
        {
            states.Clear();
            var tiles = new List<(int, int)>();
            if (!BuildRules.LineTiles(x0, z0, x1, z1, tiles)) return false;
            int afford = int.MaxValue;
            if (HotbarId != 0 && !Unlimited)
                foreach (var input in recipe.Inputs)
                    afford = Math.Min(afford, input.Count <= 0 ? int.MaxValue : BuildRules.Have(Hotbar, Inventory, input.Item) / input.Count);
            int spent = 0;
            foreach (var (tx, tz) in tiles)
            {
                var state = PlaceState(recipe, tx, tz);
                if (state == OpResult.Ok && spent >= afford) state = OpResult.MissingMaterials;
                if (state == OpResult.Ok) spent++;
                states.Add((tx, tz, state));
            }
            return true;
        }

        /// <summary>True if a carrier or a raider this client knows of stands on the tile or reaches onto it.</summary>
        private bool TileOccupied(int tx, int tz)
        {
            float radius = Content.Balance.GetFloat("playerRadius");
            if (!Local.Dead && BuildRules.Touches(tx, tz, Local.X, Local.Z, radius)) return true;
            foreach (var p in Players.Values)
            {
                if (p.Id == LocalId || p.Dead || !p.Interpolate(ServerTick, out float x, out _, out float z, out _)) continue;
                if (BuildRules.Touches(tx, tz, x, z, radius)) return true;
            }
            foreach (var e in Enemies.Values)
                if (BuildRules.Touches(tx, tz, e.X, e.Z, Content.Waves.BodyRadius(e.Def, e.Mega))) return true;
            return false;
        }

        /// <summary>The part of its materials a construct would give back if it were taken down now.</summary>
        public double RefundRatio => BuildRules.RefundRatio(Content.Balance, Global.Phase);

        /// <summary>
        /// What stands between the local player and taking a construct down, by the rules of the
        /// server's RemoveConstructOp. What a chest nobody has open holds is the server's to say.
        /// </summary>
        public OpResult RemoveState(Construct built)
        {
            if (HotbarId == 0) return OpResult.BadRequest;
            if (!BuildRules.IsOpen(Global.Phase)) return OpResult.WrongPhase;
            if (!BuildRules.InRange(built.Tx, built.Tz, Local.X, Local.Z, Content.Balance.GetFloat("buildRange"))) return OpResult.OutOfRange;
            if (built.ContainerId != 0 && Containers.TryGetValue(built.ContainerId, out var chest) && chest.EntryCount > 0) return OpResult.NotEmpty;
            return BuildRules.RefundFits(Content, built.Def.Recipe, RefundRatio, built.Hp, Inventory, Hotbar) ? OpResult.Ok : OpResult.DoesNotFit;
        }

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
