using System.Collections.Generic;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Sim.Net
{
    public enum MsgType : byte
    {
        // client → server
        Hello = 1,
        Inputs = 2,
        Request = 3,
        // server → client
        Welcome = 16,
        Reject = 17,
        Snapshot = 18,
        Events = 19,
    }

    public enum RejectReason : byte
    {
        ProtocolMismatch = 1,
        ServerFull = 2,
        BadHello = 3,
        /// <summary>Join-in-progress is only allowed in the Lobby and during Prep (doc 06 §5).</summary>
        RunInProgress = 4,
    }

    public static class Protocol
    {
        /// <summary>
        /// One line per message layout. Any change to the wire format must change this text, which
        /// changes the protocol hash and makes old builds fail the connect check (doc 06 §2).
        /// </summary>
        private const string Schema =
            "m4.5;Summary:+u8 n,{u16 dest,u16 count};Global:+mode in the flags (practice 4, daily 8),u16 dailyDay;" +
            "Hello:u32 hash,str name,u8 playtest,u8 startShift,u64 account,u8 rank,u32 startWallet;Playtest:noTimer,atMailbox,noQuota,raidNow,packages,materials,allStamps;" +
            "Inputs:u8 n,u16 ack,u16 enemyAck,u32 firstSeq,u16 view,{i8 mx,i8 mz,u16 yaw,u8 buttons,u8 held};" +
            "Buttons:sprint,jump,interact,attack,alt;" +
            "Request:u32 id,u8 kind,u32 container,u32 entry,u32 to,u8 x,u8 y,u8 rot,u16 count,u16 dest,u8 sort;" +
            "Kinds:open,close,move,quick,sort,deliver,start,ready,continue,pause,pick,buy,sell,use,harvest,place,remove,mount,kit,world,reroll,placeLine,beltResync,pushBelt,configure,refuel,ping,operateTurret,rebuildRuin,setStamps;" +
            "Welcome:u32 player,u32 tick,u32 seed,str map,u64 mapHash,u32 intake,u32 raidTick,u32 respawn,u32 protectedUntil,u8 chests,{u32 container},u8 bags,{u32 container,u32 owner,f32 x,f32 z,u32 despawn}," +
            "u16 loot,{u32 id,f32 x,f32 z,u8 kind,u8 mega,u16 coins,u8 n,{u16 item,u8 count}},global,u8 n,{u32 id,str name,u8 slot},state hotbar,state inventory," +
            "u16 perks,{u32 holder,u8 perk},u8 offer,{u8 perk},u8 rerolls,u8 picks,{u32 player,u8 card,u8 perk},u8 specials,{u8 shop},u8 owned,{u8 shop},u16 nodes,{u16 node,u8 hitsLeft}," +
            "u16 constructs,{u8 building,u8 x,u8 y,u8 rot,u16 hp,u32 container,u8 nf,{u16 filter}},u8 vehicles,{u32 id,u8 def,f32 x,f32 z,f32 heading,u32 cargo,u32 rider};" +
            "Reject:u8 reason;" +
            "Snapshot:u32 tick,u32 ack,u8 flags,[own:f32 x,f32 y,f32 z,f32 vy,u8 moveflags,f32 speed,f32 fatigue,f32 hp,[riding:u8 ride,f32 rideSpeed,f32 headX,f32 headZ]]," +
            "u16 playerBase,u8 n,{u32 id,u8 mask,[i8 dx,i8 dy,i8 dz],[i32 xcm,i16 ycm,i32 zcm],[u16 yaw],[u8 held,u8 tool],[u8 flags],[u8 hp],[u8 ride,u8 heading],[u8 kit]}," +
            "[enemies:u16 enemyBase,u8 n,{u16 id,u8 mask,[u8 kindMegaState],[i8 dx,i8 dz],[u16 x2cm,u16 z2cm],[u8 yaw],[u8 hp]}],[global],[npcVehicles:u8 n,{u32 id,i32 xcm,i32 zcm,u8 heading}];" +
            "MoveFlags:grounded,winded,dead,becalmed,riding,jumpHeld,airJumped;PlayerFlags:ready,protected,away;PlayerHp:0 dead,1..255;SnapFlags:own,global,enemies,npcVehicles;" +
            "Global:u8 shift,u8 phase,u32 end,i32 quota,i32 earnings,i32 wallet,u8 complaint,u16 backlog,u16 deliveries,u16 misdeliveries,u8 outcome,u32 host,u8 raid,i8 raidEdgeA,i8 raidEdgeB,u8 playtest,u16 poHp,u8 stamps,u8 events,u8 eventStreet;GlobalPlaytest:clockStopped,quotaWaived;" +
            "Events:u16 n,{u8 type,...};" +
            "Ev:joined,left,cstate,cdelta,cclosed,result,spawned,delivered,misdelivered,phase,summary,results,paused," +
            "attack,blast,enemyDied,lootDropped,lootTaken,lootGone,raid,playerDied,playerRespawned,playerParked,bagDropped,bagGone,playerAway,playerBack,draftOffer,perkPicked,shopOffer,shopBought,shopSold,nodeHarvested,nodesRegrown," +
            "constructPlaced,constructRemoved,constructDamaged,constructsCleared,vehicleAdded,vehicleMounted,vehicleParked,vehiclesCleared,kitGiven,worldChanged,beltState,beltAdded,beltMoved,beltRemoved,beltChecksum,beltPushed,constructConfigured,sorterStatus,inserterSwung;" +
            "DraftOffer:u8 n,{u8 perk},u8 reroll,u8 rerollsLeft;PerkPicked:u32 player,u8 perk,u8 card,u8 auto;" +
            "ShopOffer:u8 shift,u8 n,{u8 shop};ShopBought:u32 player,u8 shop,u16 price;ShopSold:u32 player,u16 item,u16 count,i32 paid;" +
            "NodeHarvested:u32 player,u16 node,u8 hitsLeft,u16 item,u8 count;NodesRegrown:u8 all;" +
            "ConstructPlaced:u32 player,u8 building,u8 x,u8 y,u8 rot,u16 hp,u32 container;ConstructRemoved:u8 x,u8 y,u32 player,u8 destroyed;" +
            "ConstructDamaged:u8 x,u8 y,u16 hp;ConstructsCleared:;Ruin:u8 x,u8 y,u8 building,u8 rot,u8 gone,u8 rebuilt,u32 player;WelcomeRuins:hp0;" +
            "BeltItem:u32 mail,u16 kind,u32 addr,u8 unit,u8 lane,f32 pos;BeltState:u8 x,u8 y,u32 tick,u16 n,{item};BeltAdded:u8 x,u8 y,item;BeltMoved:u32 mail,u8 x,u8 y,u8 lane;BeltRemoved:u32 mail;BeltChecksum:u8 x,u8 y,u16 count,u32 hash;BeltPushed:u8 x,u8 y,u16 ticks,u32 player;ConstructConfigured:u8 x,u8 y,u32 player,u8 n,{u16 filter};SorterStatus:u8 x,u8 y,u8 buffered,u16 sorted,u16 overflowed,u8 n,{u16 matches};InserterSwung:u8 x,u8 y,u16 kind;PipeCapsule:item,u8 fromX,u8 fromY,u8 outX,u8 outY;PipeRemoved:u32 mail;VehicleFuel:u32 vehicle,f32 seconds;Ping:u32 player,f32 x,f32 z,u8 kind,u32 expires;PingKinds:here,deliver,build,danger,materials;MailWaiting:u16 n,{u16 dest,u16 count};TurretFired:u8 x,u8 y,f32 tx,f32 tz;TurretOperated:u8 x,u8 y,u32 player;EnemyShot:u8 kind,f32 fx,f32 fz,f32 tx,f32 tz,u8 atConstruct;EnemyShotKinds:arrow,blast,bomb;ConstructPushed:u8 fromX,u8 fromY,u8 x,u8 y,u8 enemy;PostOfficeDamaged:u16 hp,u8 enemy;Repaired:u8 x,u8 y,u32 player,u8 postOffice;CursedRaid:u16 dest,u32 player,u16 kind,u8 raiders;MegaSighted:u32 enemy,u8 kind,i8 edge;LostParcel:u8 enemy,u16 kind,u32 addr,u8 unit,u16 value,u32 bag;SlingshotFired:u32 player,f32 fx,f32 fz,f32 tx,f32 tz,u8 hit,u8 loaded;SlingHits:none,ground,air;Outcomes:none,victory,quotaMissed,postOfficeLost;VehicleRemoved:u32 vehicle;" +
            "Driver:u8 x,u8 y,u8 state,u32 vehicle,u16 house,u8 houses,u8 at,u16 delivered,u32 player;DriverStates:none,waiting,out,unloading,returning,fleeing,stranded,passenger;" +
            "VehicleAdded:u32 id,u8 def,f32 x,f32 z,f32 heading,u32 cargo,u32 buyer;VehicleMounted:u32 id,u32 player;VehicleParked:u32 id,f32 x,f32 z,f32 heading,u32 player;VehiclesCleared:;KitGiven:u32 player,u8 kit,u8 team;WorldChanged:str map,u32 seed,u64 hash,u8 n,{u32 chest};" +
            "Attack:u32 player,u8 weapon,f32 x,f32 z,u16 yaw,u8 hits;Blast:f32 x,f32 z,u8 hits;" +
            "EnemyDied:u32 enemy,u8 kind,u8 mega,f32 x,f32 z,u32 killer;" +
            "LootDropped:u32 id,f32 x,f32 z,u8 kind,u8 mega,u16 coins,u8 n,{u16 item,u8 count};" +
            "LootTaken:u32 id,u32 player,u16 coins,u8 n,{u16 item,u8 count},u8 gone;LootGone:u32 id;" +
            "Raid:u8 state,u32 tick,i8 edgeA,i8 edgeB;PlayerDied:u32 player,u32 respawn,u8 killer;PlayerRespawned:u32 player,u32 protectedUntil;PlayerParked:u16 dest;" +
            "BagDropped:u32 container,u32 owner,f32 x,f32 z,u32 despawn;BagGone:u32 container,u8 returned,u16 mail;PlayerAway:u32 player,u32 until;PlayerBack:u32 player;" +
            "Phase:u8 shift,u8 phase,u32 tick,u32 end,i32 quota,u8 outcome;Phases:lobby,prep,delivery,payday,draft,results,generating;" +
            "Paused:u8 paused,u32 tick;" +
            "Summary:u8 shift,i32 earned,i32 quota,u8 met,i32 bonus,u8 n,{u16 deliveries},u16 misdeliveries,u16 late,u32 mvp,u16 mvpDeliveries,u32 mvpDamage,u16 damage,u32 mvpBuilt,u16 built,u16 constructsLost;" +
            "Results:u8 outcome,u8 shifts,u8 firstShift,u8 lastShift,u8 quotaWaived,i32 earned,i32 score,i32 deliveries,i32 misdeliveries,i32 constructsLost,u8 n,{u32 id,u16 deliveries,u16 misdeliveries,i32 earned,i32 xp,u16 kills,u16 built,i32 damage};" +
            "Entry:u32 id,u8 x,u8 y,u8 w,u8 h,u8 rot,u8 kind,u16 def,(mail:u32 addr,u8 unit,u16 n,{u32 id,u16 value,u8 spawn,u8 deadline,u8 flags,[priority:u32 due]}|item:i32 count);ContainerState:u32 id,u16 def,u8 extraRows,u32 version,u32 hash,u16 n,{entry};Forecast:u8 shift,u16 n,{u16 street,u16 count};RunEvent:u8 event,u8 shift,u8 stage,u8 street,i32 amount;RunEventStages:fired,ended,paid,fizzled,coming";

        /// <summary>32-bit hash of the message schema plus the content data hash.</summary>
        public static uint Hash(ContentDb content)
        {
            var h = Fnv32.Create();
            h.Add(Schema);
            h.Add(content.ContentHash);
            return h.Value;
        }
    }

    public sealed class HelloMsg
    {
        public uint ProtocolHash;
        public string Name = "";
        /// <summary>Playtest nits 10 and 13: what this player asks the server for, guest or host alike.</summary>
        public PlaytestFlags Playtest;
        /// <summary>The shift this player wants runs to start at, 0 = the first.</summary>
        public byte StartShift;
        /// <summary>
        /// Who this is from one connection to the next (doc 06 §6): the server gives a player who
        /// lost their connection back to whoever says the same within the grace. 0: nobody in particular.
        /// </summary>
        public ulong Account;
        /// <summary>The player's Postal Rank (doc 01 §5.2): which starting kits they may pick.</summary>
        public byte Rank = 1;
        /// <summary>The money (cents) this player wants runs to start with, 0 = none (--playtest-wallet).</summary>
        public uint StartWallet;

        public void Write(NetWriter w)
        {
            w.U8((byte)MsgType.Hello);
            w.U32(ProtocolHash);
            w.Str(Name);
            w.U8((byte)Playtest);
            w.U8(StartShift);
            w.U64(Account);
            w.U8(Rank);
            w.U32(StartWallet);
        }

        public static HelloMsg Read(NetReader r) =>
            new HelloMsg { ProtocolHash = r.U32(), Name = r.Str(), Playtest = (PlaytestFlags)r.U8(), StartShift = r.U8(), Account = r.U64(), Rank = r.U8(), StartWallet = r.U32() };
    }

    /// <summary>
    /// The last few inputs, oldest first (doc 06 §3: each packet repeats the last 3 for loss cover).
    /// They are consecutive, so the packet carries the first one's seq and the tick the newest was
    /// drawn at once (doc 06 §4.2's 8 kbps up: 26 bytes a packet, 6.2 kbps at 30 Hz); each earlier
    /// input was drawn a tick before the next.
    /// </summary>
    public sealed class InputsMsg
    {
        public const int MaxInputs = 3;
        public readonly List<PlayerInput> Inputs = new List<PlayerInput>(MaxInputs);
        /// <summary>
        /// Doc 06 §4.2: the newest snapshot the client has decoded, and the newest with the enemy list in
        /// it (low 16 bits; 0: none) — the baselines the server may delta-code the next ones against.
        /// </summary>
        public ushort Ack, EnemyAck;

        public void Write(NetWriter w)
        {
            w.U8((byte)MsgType.Inputs);
            w.U8((byte)Inputs.Count);
            w.U16(Ack); w.U16(EnemyAck);
            if (Inputs.Count == 0) return;
            for (int i = 1; i < Inputs.Count; i++)
                if (Inputs[i].Seq != Inputs[0].Seq + (uint)i) throw new System.InvalidOperationException("the inputs of a packet are consecutive");
            w.U32(Inputs[0].Seq);
            w.U16(Inputs[Inputs.Count - 1].View);
            foreach (var i in Inputs)
            {
                w.I8(i.MoveX); w.I8(i.MoveZ); w.U16(i.Yaw); w.U8((byte)i.Buttons); w.U8(i.HeldSlot);
            }
        }

        public static InputsMsg Read(NetReader r)
        {
            var m = new InputsMsg();
            int n = r.U8();
            if (n > MaxInputs) throw new NetFormatException("too many inputs in one packet");
            m.Ack = r.U16(); m.EnemyAck = r.U16();
            if (n == 0) return m;
            uint first = r.U32();
            ushort view = r.U16();
            for (int i = 0; i < n; i++)
            {
                ushort drawn = view == 0 ? (ushort)0 : (ushort)(view - (n - 1 - i));
                m.Inputs.Add(new PlayerInput
                {
                    Seq = first + (uint)i, MoveX = r.I8(), MoveZ = r.I8(), Yaw = r.U16(),
                    Buttons = (InputButtons)r.U8(), HeldSlot = r.U8(), View = view != 0 && drawn == 0 ? (ushort)0xFFFF : drawn,
                });
            }
            return m;
        }
    }

    public static class RequestMsg
    {
        public static void Write(NetWriter w, in SimRequest q)
        {
            w.U8((byte)MsgType.Request);
            w.U32(q.RequestId); w.U8((byte)q.Kind); w.U32(q.Container); w.U32(q.EntryId); w.U32(q.ToContainer);
            w.U8(q.X); w.U8(q.Y); w.Bool(q.Rotated); w.U16(q.Count); w.U16(q.DestId); w.U8((byte)q.SortKey);
        }

        public static SimRequest Read(NetReader r) => new SimRequest
        {
            RequestId = r.U32(), Kind = (RequestKind)r.U8(), Container = r.U32(), EntryId = r.U32(), ToContainer = r.U32(),
            X = r.U8(), Y = r.U8(), Rotated = r.Bool(), Count = r.U16(), DestId = r.U16(), SortKey = (SortKey)r.U8(),
        };
    }

    public struct PlayerInfo
    {
        public EntityId Id;
        public string Name;
        public byte Slot;
    }

    /// <summary>A death bag lying in the world, as a joining client is told of it.</summary>
    public struct BagInfo
    {
        public uint ContainerId;
        public EntityId Owner;
        public float X, Z;
        public uint DespawnTick;
    }

    /// <summary>A perk of the run under way, as a joining client is told of it.</summary>
    public struct PerkInfo
    {
        /// <summary>Whose personal perk it is; None for a team perk.</summary>
        public EntityId Holder;
        /// <summary>Index into ContentDb.Perks.</summary>
        public byte Perk;
    }

    /// <summary>Who has taken which card of the Draft under way.</summary>
    public struct DraftPickInfo
    {
        public EntityId Player;
        public byte Card;
        /// <summary>The perk of the card: after a reroll the cards are not the same for everybody.</summary>
        public byte Perk;
    }

    /// <summary>What a resource node that has been swung at has left.</summary>
    public struct NodeInfo
    {
        /// <summary>Index into WorldMap.Props.</summary>
        public ushort Node;
        public byte HitsLeft;
    }

    /// <summary>Something that stands built on a tile, as a joining client is told of it.</summary>
    public struct ConstructInfo
    {
        /// <summary>Index into ContentDb.Buildings.</summary>
        public byte Building;
        public byte X, Y;
        public byte Rotation;
        public ushort Hp;
        public uint ContainerId;
        /// <summary>A sorter's or a splitter's filters (<see cref="Automation.MailFilter.Encode"/>); null for none.</summary>
        public List<ushort>? Filters;
    }

    /// <summary>A vehicle of the run, as a joining client is told of it.</summary>
    public struct VehicleInfo
    {
        public EntityId Id;
        /// <summary>Index into ContentDb.Vehicles.</summary>
        public byte Def;
        public float X, Z;
        public float Heading;
        public uint CargoContainer;
        /// <summary>Who rides it; None while it stands parked.</summary>
        public EntityId Rider;
    }

    /// <summary>Join state (doc 06 §5 step 4), small enough in M0 to be one message.</summary>
    public sealed class WelcomeMsg
    {
        public EntityId PlayerId;
        public uint Tick;
        public uint Seed;
        public string MapId = "";
        public ulong MapHash;
        public uint IntakeContainerId;
        /// <summary>
        /// What somebody who comes into a shift under way has missed the events of: the tick of the
        /// raid's state (<see cref="RaidChangedEvent.Tick"/>), and for a player who is given their
        /// body back, when it stands up and until when it is protected. 0: nothing of the kind.
        /// </summary>
        public uint RaidTick, RespawnTick, ProtectedUntilTick;
        /// <summary>The containers of the map's chests, in the map's order.</summary>
        public readonly List<uint> ChestContainerIds = new List<uint>();
        /// <summary>The bags lying about; later ones come as events.</summary>
        public readonly List<BagInfo> Bags = new List<BagInfo>();
        /// <summary>The loot lying about, each bundle with what is left of it.</summary>
        public readonly List<LootDroppedEvent> Loot = new List<LootDroppedEvent>();
        public GlobalState Global;
        public readonly List<PlayerInfo> Players = new List<PlayerInfo>();
        public ContainerState Hotbar = null!;
        public ContainerState Inventory = null!;
        /// <summary>The perks of the run so far, in the order they were taken; later ones come as events.</summary>
        public readonly List<PerkInfo> Perks = new List<PerkInfo>();
        /// <summary>In a Draft: its cards, and who has taken which.</summary>
        public readonly List<byte> DraftOffer = new List<byte>();
        public readonly List<DraftPickInfo> DraftPicks = new List<DraftPickInfo>();
        /// <summary>The Draft's rerolls the joining player has left in the run.</summary>
        public byte RerollsLeft;
        /// <summary>The shop: the shift's specials and what the team owns of the entries sold once, as indices into ContentDb.ShopItems.</summary>
        public readonly List<byte> ShopSpecials = new List<byte>();
        public readonly List<byte> ShopOwned = new List<byte>();
        /// <summary>The resource nodes that are not as they grew; later swings come as events.</summary>
        public readonly List<NodeInfo> Nodes = new List<NodeInfo>();
        /// <summary>What the carriers have built, in the order they built it; later ones come as events.</summary>
        public readonly List<ConstructInfo> Constructs = new List<ConstructInfo>();
        /// <summary>The vehicles of the run; later ones come as events.</summary>
        public readonly List<VehicleInfo> Vehicles = new List<VehicleInfo>();

        public void Write(NetWriter w)
        {
            w.U8((byte)MsgType.Welcome);
            w.U32(PlayerId.Value); w.U32(Tick); w.U32(Seed); w.Str(MapId); w.U64(MapHash); w.U32(IntakeContainerId);
            w.U32(RaidTick); w.U32(RespawnTick); w.U32(ProtectedUntilTick);
            w.U8((byte)ChestContainerIds.Count);
            foreach (uint id in ChestContainerIds) w.U32(id);
            w.U8((byte)Bags.Count);
            foreach (var b in Bags) { w.U32(b.ContainerId); w.U32(b.Owner.Value); w.F32(b.X); w.F32(b.Z); w.U32(b.DespawnTick); }
            w.U16((ushort)Loot.Count);
            foreach (var l in Loot)
            {
                w.U32(l.LootId); w.F32(l.X); w.F32(l.Z); w.U8(l.EnemyKind); w.Bool(l.Mega); w.U16(l.Coins);
                w.U8((byte)l.Items.Count);
                foreach (var item in l.Items) { w.U16(item.Item); w.U8(item.Count); }
            }
            GlobalCodec.Write(w, Global);
            w.U8((byte)Players.Count);
            foreach (var p in Players) { w.U32(p.Id.Value); w.Str(p.Name); w.U8(p.Slot); }
            ContainerCodec.WriteState(w, Hotbar);
            ContainerCodec.WriteState(w, Inventory);
            w.U16((ushort)Perks.Count);
            foreach (var p in Perks) { w.U32(p.Holder.Value); w.U8(p.Perk); }
            w.U8((byte)DraftOffer.Count);
            foreach (byte perk in DraftOffer) w.U8(perk);
            w.U8(RerollsLeft);
            w.U8((byte)DraftPicks.Count);
            foreach (var p in DraftPicks) { w.U32(p.Player.Value); w.U8(p.Card); w.U8(p.Perk); }
            w.U8((byte)ShopSpecials.Count);
            foreach (byte item in ShopSpecials) w.U8(item);
            w.U8((byte)ShopOwned.Count);
            foreach (byte item in ShopOwned) w.U8(item);
            w.U16((ushort)Nodes.Count);
            foreach (var node in Nodes) { w.U16(node.Node); w.U8(node.HitsLeft); }
            w.U16((ushort)Constructs.Count);
            foreach (var c in Constructs)
            {
                w.U8(c.Building); w.U8(c.X); w.U8(c.Y); w.U8(c.Rotation); w.U16(c.Hp); w.U32(c.ContainerId);
                w.U8((byte)(c.Filters?.Count ?? 0));
                if (c.Filters != null) foreach (ushort f in c.Filters) w.U16(f);
            }
            w.U8((byte)Vehicles.Count);
            foreach (var v in Vehicles) { w.U32(v.Id.Value); w.U8(v.Def); w.F32(v.X); w.F32(v.Z); w.F32(v.Heading); w.U32(v.CargoContainer); w.U32(v.Rider.Value); }
        }

        public static WelcomeMsg Read(NetReader r)
        {
            var m = new WelcomeMsg
            {
                PlayerId = new EntityId(r.U32()), Tick = r.U32(), Seed = r.U32(), MapId = r.Str(), MapHash = r.U64(),
                IntakeContainerId = r.U32(), RaidTick = r.U32(), RespawnTick = r.U32(), ProtectedUntilTick = r.U32(),
            };
            int chests = r.U8();
            for (int i = 0; i < chests; i++) m.ChestContainerIds.Add(r.U32());
            int bags = r.U8();
            for (int i = 0; i < bags; i++)
            {
                m.Bags.Add(new BagInfo
                {
                    ContainerId = r.U32(), Owner = new EntityId(r.U32()), X = r.F32(), Z = r.F32(), DespawnTick = r.U32(),
                });
            }
            int loot = r.U16();
            for (int i = 0; i < loot; i++)
            {
                var l = new LootDroppedEvent { LootId = r.U32(), X = r.F32(), Z = r.F32(), EnemyKind = r.U8(), Mega = r.Bool(), Coins = r.U16() };
                int items = r.U8();
                for (int k = 0; k < items; k++) l.Items.Add(new LootItem { Item = r.U16(), Count = r.U8() });
                m.Loot.Add(l);
            }
            m.Global = GlobalCodec.Read(r);
            int n = r.U8();
            for (int i = 0; i < n; i++)
                m.Players.Add(new PlayerInfo { Id = new EntityId(r.U32()), Name = r.Str(), Slot = r.U8() });
            m.Hotbar = ContainerCodec.ReadState(r);
            m.Inventory = ContainerCodec.ReadState(r);
            int perks = r.U16();
            for (int i = 0; i < perks; i++) m.Perks.Add(new PerkInfo { Holder = new EntityId(r.U32()), Perk = r.U8() });
            int offer = r.U8();
            for (int i = 0; i < offer; i++) m.DraftOffer.Add(r.U8());
            m.RerollsLeft = r.U8();
            int picks = r.U8();
            for (int i = 0; i < picks; i++) m.DraftPicks.Add(new DraftPickInfo { Player = new EntityId(r.U32()), Card = r.U8(), Perk = r.U8() });
            int specials = r.U8();
            for (int i = 0; i < specials; i++) m.ShopSpecials.Add(r.U8());
            int owned = r.U8();
            for (int i = 0; i < owned; i++) m.ShopOwned.Add(r.U8());
            int nodes = r.U16();
            for (int i = 0; i < nodes; i++) m.Nodes.Add(new NodeInfo { Node = r.U16(), HitsLeft = r.U8() });
            int constructs = r.U16();
            for (int i = 0; i < constructs; i++)
            {
                var info = new ConstructInfo
                {
                    Building = r.U8(), X = r.U8(), Y = r.U8(), Rotation = r.U8(), Hp = r.U16(), ContainerId = r.U32(),
                };
                int filters = r.U8();
                if (filters > 0)
                {
                    info.Filters = new List<ushort>(filters);
                    for (int k = 0; k < filters; k++) info.Filters.Add(r.U16());
                }
                m.Constructs.Add(info);
            }
            int vehicles = r.U8();
            for (int i = 0; i < vehicles; i++)
            {
                m.Vehicles.Add(new VehicleInfo
                {
                    Id = new EntityId(r.U32()), Def = r.U8(), X = r.F32(), Z = r.F32(), Heading = r.F32(), CargoContainer = r.U32(), Rider = new EntityId(r.U32()),
                });
            }
            return m;
        }
    }

    public static class RejectMsg
    {
        public static void Write(NetWriter w, RejectReason reason)
        {
            w.U8((byte)MsgType.Reject);
            w.U8((byte)reason);
        }

        public static RejectReason Read(NetReader r) => (RejectReason)r.U8();
    }

    public static class GlobalCodec
    {
        private static GlobalState WithPlaytest(GlobalState g, byte bits)
        {
            g.ClockStopped = (bits & 1) != 0;
            g.QuotaWaived = (bits & 2) != 0;
            g.Mode = (bits & 4) != 0 ? RunMode.Practice : (bits & 8) != 0 ? RunMode.Daily : RunMode.Normal;
            return g;
        }

        public static void Write(NetWriter w, in GlobalState g)
        {
            w.U8(g.Shift); w.U8((byte)g.Phase); w.U32(g.PhaseEndTick); w.I32(g.Quota); w.I32(g.ShiftEarnings);
            w.I32(g.Wallet); w.U8(g.Complaint); w.U16(g.Backlog); w.U16(g.Deliveries); w.U16(g.Misdeliveries);
            w.U8((byte)g.Outcome); w.U32(g.Host.Value); w.U8((byte)g.Raid); w.U8((byte)g.RaidEdgeA); w.U8((byte)g.RaidEdgeB); w.U8((byte)((g.ClockStopped ? 1 : 0) | (g.QuotaWaived ? 2 : 0) | (g.Mode == RunMode.Practice ? 4 : 0) | (g.Mode == RunMode.Daily ? 8 : 0)));
            w.U16(g.PostOfficeHp);
            w.U8(g.Stamps);
            w.U8(g.Events);
            w.U8(g.EventStreet);
            w.U16(g.DailyDay);
        }

        public static GlobalState Read(NetReader r)
        {
            var g = WithPlaytest(new GlobalState
            {
                Shift = r.U8(), Phase = (RunPhase)r.U8(), PhaseEndTick = r.U32(), Quota = r.I32(), ShiftEarnings = r.I32(),
                Wallet = r.I32(), Complaint = r.U8(), Backlog = r.U16(), Deliveries = r.U16(), Misdeliveries = r.U16(),
                Outcome = (RunOutcome)r.U8(), Host = new EntityId(r.U32()), Raid = (RaidState)r.U8(), RaidEdgeA = (sbyte)r.U8(), RaidEdgeB = (sbyte)r.U8(),
            }, r.U8());
            g.PostOfficeHp = r.U16();
            g.Stamps = r.U8();
            g.Events = r.U8();
            g.EventStreet = r.U8();
            g.DailyDay = r.U16();
            return g;
        }
    }

    /// <summary>One player in a snapshot. Position is quantised to 1 cm on the wire (doc 06 §4).</summary>
    public struct PlayerSnap
    {
        public EntityId Id;
        public float X, Y, Z;
        public ushort Yaw;
        public byte HeldSlot;
        /// <summary>The tool in the held slot, as its item index + 1; 0 for none (what the others see in the hand).</summary>
        public byte HeldTool;
        /// <summary>Ready flag of the Lobby and Prep.</summary>
        public bool Ready;
        /// <summary>Stands protected after a respawn (nit 17).</summary>
        public bool Protected;
        /// <summary>Has lost their connection and may still come back (doc 06 §6).</summary>
        public bool Away;
        /// <summary>Health in 255ths; 0 = down.</summary>
        public byte Hp;
        /// <summary>On foot 0; in a saddle the vehicle's index + 1 (<see cref="PlayerMoveState.Ride"/>).</summary>
        public byte Ride;
        /// <summary>The way the ridden vehicle points, a yaw in radians (256 steps on the wire).</summary>
        public float Heading;
        /// <summary>The starting kit picked in the Lobby, an index into ContentDb.Kits.</summary>
        public byte Kit;
    }

    /// <summary>One enemy in a snapshot: 9 bytes (doc 06 §4; 40 of them at 10 Hz stay inside the budget).</summary>
    public struct EnemySnap
    {
        /// <summary>The low 16 bits of the entity counter.</summary>
        public ushort Id;
        public byte Kind;
        public bool Mega;
        public EnemyState State;
        public float X, Z;
        /// <summary>Radians, quantised to 256 steps on the wire.</summary>
        public float Yaw;
        /// <summary>Health in 255ths, never 0: a dead enemy is simply not listed.</summary>
        public byte Hp;
    }

    /// <summary>
    /// 20 Hz unreliable state (doc 06 §4 strategy S). The receiving player's own movement state is
    /// sent unquantised so reconciliation replays from exactly what the server has.
    /// </summary>
    public sealed class SnapshotMsg
    {
        private const byte FlagOwn = 1, FlagGlobal = 2, FlagEnemies = 4, FlagNpcVehicles = 8;
        private const byte PlayerReady = 1, PlayerProtected = 2, PlayerAway = 4;
        private const byte MoveGrounded = 1, MoveWinded = 2, MoveDead = 4, MoveBecalmed = 8, MoveRiding = 16, MoveJumpHeld = 32, MoveAirJumped = 64;
        private const float TwoPi = System.MathF.PI * 2f;

        public uint Tick;
        /// <summary>Seq of the last input the server has simulated for the receiving player.</summary>
        public uint AckSeq;
        public bool HasOwn;
        public PlayerMoveState Own;
        public float OwnSpeedMult = 1f;
        public float OwnHp;
        public readonly List<PlayerSnap> Players = new List<PlayerSnap>();
        /// <summary>The enemy list rides every other snapshot. When present it is complete: who is not in it is gone.</summary>
        public bool HasEnemies;
        public readonly List<EnemySnap> Enemies = new List<EnemySnap>();
        public bool HasGlobal;
        public GlobalState Global;
        /// <summary>The vehicles NPC drivers drive (doc 06 §3 "same as vehicles without driver prediction"): where each is, every snapshot while any is.</summary>
        public readonly List<(uint Id, float X, float Z, float Heading)> NpcVehicles = new List<(uint, float, float, float)>();

        /// <summary>
        /// Doc 06 §4.2 (design choice 146): the snapshot the players are delta-coded against and the one
        /// the enemies are (full ticks; 0: none, all whole), with what that client was sent in them.
        /// </summary>
        public uint PlayerBaseTick, EnemyBaseTick;
        public IReadOnlyDictionary<uint, PlayerQ>? PlayerBase;
        public IReadOnlyDictionary<ushort, EnemyQ>? EnemyBase;
        /// <summary>As read: the players and enemies as the wire had them, the next baseline of this client.</summary>
        public readonly List<PlayerQ> PlayersQ = new List<PlayerQ>();
        public readonly List<EnemyQ> EnemiesQ = new List<EnemyQ>();
        /// <summary>As read: a delta against a baseline this client does not have; nothing of it may be used.</summary>
        public bool Undecodable;

        public static PlayerQ Quantise(in PlayerSnap p) => new PlayerQ
        {
            Id = p.Id.Value, X = ToCm(p.X), Y = (short)ToCm(p.Y), Z = ToCm(p.Z), Yaw = p.Yaw, HeldSlot = p.HeldSlot, HeldTool = p.HeldTool,
            Flags = (byte)((p.Ready ? PlayerReady : 0) | (p.Protected ? PlayerProtected : 0) | (p.Away ? PlayerAway : 0)),
            Hp = p.Hp, Ride = p.Ride, Heading = ToAngle(p.Heading), Kit = p.Kit,
        };

        public static PlayerSnap Of(in PlayerQ q) => new PlayerSnap
        {
            Id = new EntityId(q.Id), X = q.X / 100f, Y = q.Y / 100f, Z = q.Z / 100f, Yaw = q.Yaw, HeldSlot = q.HeldSlot, HeldTool = q.HeldTool,
            Ready = (q.Flags & PlayerReady) != 0, Protected = (q.Flags & PlayerProtected) != 0, Away = (q.Flags & PlayerAway) != 0,
            Hp = q.Hp, Ride = q.Ride, Heading = q.Heading / 256f * TwoPi, Kit = q.Kit,
        };

        public static EnemyQ Quantise(in EnemySnap e) => new EnemyQ
        {
            Id = e.Id, Packed = (byte)((e.Kind & 0x0F) | (e.Mega ? 0x10 : 0) | ((byte)e.State << 5)), X = To2Cm(e.X), Z = To2Cm(e.Z), Yaw = ToAngle(e.Yaw), Hp = e.Hp,
        };

        public static EnemySnap Of(in EnemyQ q) => new EnemySnap
        {
            Id = q.Id, Kind = (byte)(q.Packed & 0x0F), Mega = (q.Packed & 0x10) != 0, State = (EnemyState)(q.Packed >> 5),
            X = q.X * 0.02f, Z = q.Z * 0.02f, Yaw = q.Yaw / 256f * TwoPi, Hp = q.Hp,
        };

        public void Write(NetWriter w)
        {
            w.U8((byte)MsgType.Snapshot);
            w.U32(Tick);
            w.U32(AckSeq);
            w.U8((byte)((HasOwn ? FlagOwn : 0) | (HasGlobal ? FlagGlobal : 0) | (HasEnemies ? FlagEnemies : 0) | (NpcVehicles.Count > 0 ? FlagNpcVehicles : 0)));
            if (HasOwn)
            {
                // The saddle's four only while there is something in them (13 bytes of 49 on foot).
                bool riding = Own.Ride != 0 || Own.RideSpeed != 0f || Own.HeadX != 0f || Own.HeadZ != 0f;
                w.F32(Own.X); w.F32(Own.Y); w.F32(Own.Z); w.F32(Own.VelY);
                w.U8((byte)((Own.Grounded ? MoveGrounded : 0) | (Own.Winded ? MoveWinded : 0) | (Own.Dead ? MoveDead : 0) | (Own.Becalmed ? MoveBecalmed : 0) | (riding ? MoveRiding : 0)
                    | (Own.JumpHeld ? MoveJumpHeld : 0) | (Own.AirJumped ? MoveAirJumped : 0)));
                w.F32(OwnSpeedMult); w.F32(Own.Fatigue); w.F32(OwnHp);
                if (riding) { w.U8(Own.Ride); w.F32(Own.RideSpeed); w.F32(Own.HeadX); w.F32(Own.HeadZ); }
            }
            w.U16(PlayerBase != null ? (ushort)PlayerBaseTick : (ushort)0);
            w.U8((byte)Players.Count);
            foreach (var p in Players)
            {
                var q = Quantise(p);
                SnapshotDelta.WritePlayer(w, q, PlayerBase != null && PlayerBase.TryGetValue(q.Id, out var b) ? b : null);
            }
            if (HasEnemies)
            {
                w.U16(EnemyBase != null ? (ushort)EnemyBaseTick : (ushort)0);
                w.U8((byte)Enemies.Count);
                foreach (var e in Enemies)
                {
                    var q = Quantise(e);
                    SnapshotDelta.WriteEnemy(w, q, EnemyBase != null && EnemyBase.TryGetValue(q.Id, out var b) ? b : null);
                }
            }
            if (HasGlobal) GlobalCodec.Write(w, Global);
            if (NpcVehicles.Count > 0)
            {
                w.U8((byte)System.Math.Min(255, NpcVehicles.Count));
                for (int i = 0; i < System.Math.Min(255, NpcVehicles.Count); i++)
                {
                    var v = NpcVehicles[i];
                    w.U32(v.Id); w.I32(ToCm(v.X)); w.I32(ToCm(v.Z)); w.U8(ToAngle(v.Heading));
                }
            }
        }

        public static SnapshotMsg Read(NetReader r, ISnapshotBaselines? baselines = null)
        {
            var m = new SnapshotMsg { Tick = r.U32(), AckSeq = r.U32() };
            byte flags = r.U8();
            m.HasOwn = (flags & FlagOwn) != 0;
            m.HasGlobal = (flags & FlagGlobal) != 0;
            m.HasEnemies = (flags & FlagEnemies) != 0;
            if (m.HasOwn)
            {
                m.Own = new PlayerMoveState { X = r.F32(), Y = r.F32(), Z = r.F32(), VelY = r.F32() };
                byte move = r.U8();
                m.Own.Grounded = (move & MoveGrounded) != 0;
                m.Own.Winded = (move & MoveWinded) != 0;
                m.Own.Dead = (move & MoveDead) != 0;
                m.Own.Becalmed = (move & MoveBecalmed) != 0;
                m.Own.JumpHeld = (move & MoveJumpHeld) != 0;
                m.Own.AirJumped = (move & MoveAirJumped) != 0;
                m.OwnSpeedMult = r.F32();
                m.Own.Fatigue = r.F32();
                m.OwnHp = r.F32();
                if ((move & MoveRiding) != 0) { m.Own.Ride = r.U8(); m.Own.RideSpeed = r.F32(); m.Own.HeadX = r.F32(); m.Own.HeadZ = r.F32(); }
            }
            m.PlayerBaseTick = SnapshotDelta.Expand(r.U16(), m.Tick);
            IReadOnlyDictionary<uint, PlayerQ>? players = null;
            if (m.PlayerBaseTick != 0 && (baselines == null || !baselines.TryPlayers(m.PlayerBaseTick, out players))) m.Undecodable = true;
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                if (!SnapshotDelta.ReadPlayer(r, players, out var q)) m.Undecodable = true;
                m.PlayersQ.Add(q);
                m.Players.Add(Of(q));
            }
            if (m.HasEnemies)
            {
                m.EnemyBaseTick = SnapshotDelta.Expand(r.U16(), m.Tick);
                IReadOnlyDictionary<ushort, EnemyQ>? enemies = null;
                if (m.EnemyBaseTick != 0 && (baselines == null || !baselines.TryEnemies(m.EnemyBaseTick, out enemies))) m.Undecodable = true;
                n = r.U8();
                for (int i = 0; i < n; i++)
                {
                    if (!SnapshotDelta.ReadEnemy(r, enemies, out var q)) m.Undecodable = true;
                    m.EnemiesQ.Add(q);
                    m.Enemies.Add(Of(q));
                }
            }
            if (m.HasGlobal) m.Global = GlobalCodec.Read(r);
            if ((flags & FlagNpcVehicles) != 0)
            {
                n = r.U8();
                for (int i = 0; i < n; i++) m.NpcVehicles.Add((r.U32(), r.I32() / 100f, r.I32() / 100f, r.U8() / 256f * TwoPi));
            }
            return m;
        }

        private static byte ToAngle(float radians) => (byte)((int)System.MathF.Round(radians / TwoPi * 256f) & 0xFF);
        private static int ToCm(float metres) => (int)System.MathF.Round(metres * 100f);
        private static ushort To2Cm(float metres) => (ushort)System.Math.Clamp((int)System.MathF.Round(metres * 50f), 0, ushort.MaxValue);
    }

    public static class EventsMsg
    {
        public static void Write(NetWriter w, List<SimEvent> events)
        {
            w.U8((byte)MsgType.Events);
            w.U16((ushort)events.Count);
            foreach (var e in events) EventCodec.Write(w, e);
        }

        public static List<SimEvent> Read(NetReader r)
        {
            int n = r.Count16(2);
            var list = new List<SimEvent>(n);
            for (int i = 0; i < n; i++) list.Add(EventCodec.Read(r));
            return list;
        }
    }
}
