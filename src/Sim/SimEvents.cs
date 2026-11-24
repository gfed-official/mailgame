using System.Collections.Generic;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Something that happened this tick and must reach clients reliably (doc 06 §4 strategy E).
    /// <see cref="Audience"/> None = every client; otherwise only that player.
    /// </summary>
    public abstract class SimEvent
    {
        public EntityId Audience;
    }

    public sealed class PlayerJoinedEvent : SimEvent
    {
        public EntityId Player;
        public string Name = "";
        public byte Slot;
    }

    public sealed class PlayerLeftEvent : SimEvent
    {
        public EntityId Player;
    }

    /// <summary>A player lost their connection during a run: they stand where they stood until the tick (doc 06 §6).</summary>
    public sealed class PlayerAwayEvent : SimEvent
    {
        public EntityId Player;
        public uint UntilTick;
    }

    /// <summary>The player who was away has reconnected in time.</summary>
    public sealed class PlayerBackEvent : SimEvent
    {
        public EntityId Player;
    }

    /// <summary>Full container contents, sent when a player opens a container.</summary>
    public sealed class ContainerStateEvent : SimEvent
    {
        public ContainerState State = null!;
    }

    /// <summary>One versioned container mutation, sent to every viewer of the container.</summary>
    public sealed class ContainerDeltaEvent : SimEvent
    {
        public ContainerDelta Delta = null!;
    }

    /// <summary>The server closed the player's external container (walked out of range, container gone).</summary>
    public sealed class ContainerClosedEvent : SimEvent
    {
        public uint ContainerId;
    }

    /// <summary>Verdict on a client request; a rejection rolls back the client's optimistic preview.</summary>
    public sealed class RequestResultEvent : SimEvent
    {
        public uint RequestId;
        public OpResult Result;
    }

    public sealed class MailSpawnedEvent : SimEvent
    {
        /// <summary>Mail ids are sequential, so the batch is [FirstMailId, FirstMailId + Count).</summary>
        public uint FirstMailId;
        public ushort Count;
        public ushort Backlog;
    }

    public sealed class DeliveredEvent : SimEvent
    {
        public uint MailId;
        public ushort DestId;
        public ushort Paid;
        public EntityId Player;
        public ushort Kind;
        /// <summary>Who put it in when no carrier did (<see cref="Player"/> none): a belt or an inserter.</summary>
        public DeliveryMachine Machine;
    }

    /// <summary>What made a delivery that is nobody's (design choice 94 g).</summary>
    public enum DeliveryMachine : byte
    {
        Hand = 0,
        Belt = 1,
        Inserter = 2,
        Pipe = 3,
    }

    public sealed class MisdeliveredEvent : SimEvent
    {
        public uint MailId;
        public ushort DestId;
        public ushort Penalty;
        public EntityId Player;
        public ushort Kind;
    }

    public sealed class PhaseChangedEvent : SimEvent
    {
        public byte Shift;
        public RunPhase Phase;
        /// <summary>The tick the phase began at.</summary>
        public uint Tick;
        /// <summary>0 when the phase has no clock.</summary>
        public uint PhaseEndTick;
        public int Quota;
        public RunOutcome Outcome;
    }

    /// <summary>The solo pause went on or off (doc 01 §1); <see cref="Tick"/> is the tick the server stands on.</summary>
    public sealed class PausedChangedEvent : SimEvent
    {
        public bool Paused;
        public uint Tick;
    }

    /// <summary>Payday payload (doc 01 §2.4), sent just before the PhaseChanged into Payday.</summary>
    public sealed class ShiftSummaryEvent : SimEvent
    {
        public ShiftSummary Summary = null!;
    }

    /// <summary>Results payload (doc 08 §3.2), sent just before the PhaseChanged into Results.</summary>
    public sealed class RunResultsEvent : SimEvent
    {
        public RunResults Results = null!;
    }

    public enum Weapon : byte { Hammer = 0, Cannon = 1 }

    /// <summary>A carrier swung the hammer or fired the mail cannon; clients play it from here.</summary>
    public sealed class AttackEvent : SimEvent
    {
        public EntityId Player;
        public Weapon Weapon;
        public float X, Z;
        public ushort Yaw;
        /// <summary>Hammer: enemies hit.</summary>
        public byte Hits;
    }

    /// <summary>A cannon package went off.</summary>
    public sealed class BlastEvent : SimEvent
    {
        public float X, Z;
        public byte Hits;
    }

    /// <summary>An enemy fell. The snapshots simply stop listing it; this is for the effect and the loot card.</summary>
    public sealed class EnemyDiedEvent : SimEvent
    {
        public EntityId Enemy;
        public byte Kind;
        public bool Mega;
        public float X, Z;
        public EntityId Killer;
    }

    /// <summary>What lies on the ground where an enemy fell.</summary>
    public sealed class LootDroppedEvent : SimEvent
    {
        public uint LootId;
        public float X, Z;
        public byte EnemyKind;
        public bool Mega;
        public ushort Coins;
        public List<LootItem> Items = new List<LootItem>();
    }

    /// <summary>A player took (part of) a loot bundle; <see cref="Gone"/> when nothing is left of it.</summary>
    public sealed class LootTakenEvent : SimEvent
    {
        public uint LootId;
        public EntityId Player;
        public ushort Coins;
        public List<LootItem> Items = new List<LootItem>();
        public bool Gone;
    }

    /// <summary>The loot bundle despawned untaken (or the phase swept it away).</summary>
    public sealed class LootGoneEvent : SimEvent
    {
        public uint LootId;
    }

    public sealed class RaidChangedEvent : SimEvent
    {
        public RaidState Raid;
        /// <summary>Warning: the tick of the first spawn. Fleeing: the tick the last enemy is gone by.</summary>
        public uint Tick;
        /// <summary>The raid's ways in (indices into the map's spawn edges), −1 for none.</summary>
        public sbyte EdgeA = -1, EdgeB = -1;
    }

    public sealed class PlayerDiedEvent : SimEvent
    {
        public EntityId Player;
        public uint RespawnTick;
        /// <summary>Enemy kind that did it.</summary>
        public byte Killer;
    }

    public sealed class PlayerRespawnedEvent : SimEvent
    {
        public EntityId Player;
        /// <summary>The protection they stand up with ends by this tick; 0: none.</summary>
        public uint ProtectedUntilTick;
    }

    /// <summary>A carrier went down with things on them: the bag that lies there now (doc 01 §2.2).</summary>
    public sealed class BagDroppedEvent : SimEvent
    {
        public uint ContainerId;
        public EntityId Owner;
        public float X, Z;
        public uint DespawnTick;
    }

    /// <summary>A bag is gone: emptied by hand, or <see cref="Returned"/> when its time was up.</summary>
    public sealed class BagGoneEvent : SimEvent
    {
        public uint ContainerId;
        public bool Returned;
        /// <summary>Mail items that went back to the Intake.</summary>
        public ushort Mail;
    }

    /// <summary>
    /// The cards of a Draft (doc 01 §2.5) before the <see cref="SimEvent.Audience"/>, as indices
    /// into ContentDb.Perks: the shared offer, sent to each player just before the PhaseChanged
    /// into Draft, or after a <see cref="Reroll"/> that player's own.
    /// </summary>
    public sealed class DraftOfferEvent : SimEvent
    {
        public List<byte> Perks = new List<byte>();
        /// <summary>The player's own new cards: the others' picks stand.</summary>
        public bool Reroll;
        /// <summary>How many rerolls the player has left in the run.</summary>
        public byte RerollsLeft;
    }

    /// <summary>A perk was taken: a personal one is <see cref="Player"/>'s, a team perk everybody's through them.</summary>
    public sealed class PerkPickedEvent : SimEvent
    {
        /// <summary><see cref="Card"/> of a perk that did not come from a Draft card.</summary>
        public const byte NoCard = 255;

        public EntityId Player;
        /// <summary>Index into ContentDb.Perks.</summary>
        public byte Perk;
        /// <summary>The card of the Draft's offer it was.</summary>
        public byte Card = NoCard;
        /// <summary>The Draft's clock ran out and the server chose for them.</summary>
        public bool Auto;
    }

    /// <summary>
    /// The specials of the shift that begins (doc 10 §3), as indices into ContentDb.ShopItems;
    /// sent just before the PhaseChanged that begins the shift.
    /// </summary>
    public sealed class ShopOfferEvent : SimEvent
    {
        public byte Shift;
        public List<byte> Specials = new List<byte>();
    }

    /// <summary>Somebody bought at the shop, out of the team wallet.</summary>
    public sealed class ShopBoughtEvent : SimEvent
    {
        public EntityId Player;
        /// <summary>Index into ContentDb.ShopItems.</summary>
        public byte Item;
        public ushort Price;
    }

    /// <summary>Somebody sold to the shop; the money is the team's.</summary>
    public sealed class ShopSoldEvent : SimEvent
    {
        public EntityId Player;
        /// <summary>Index into ContentDb.Items.</summary>
        public ushort Item;
        public ushort Count;
        public int Paid;
    }

    /// <summary>A swing at a resource node landed (doc 02 §3.9): what it gave, and what the node has left.</summary>
    public sealed class NodeHarvestedEvent : SimEvent
    {
        public EntityId Player;
        /// <summary>Index into WorldMap.Props.</summary>
        public ushort Node;
        public byte HitsLeft;
        /// <summary>Index into ContentDb.Items.</summary>
        public ushort Item;
        public byte Count;
    }

    /// <summary>
    /// Nodes are whole again: those that regrow when a shift begins, or (<see cref="All"/>) every
    /// node when a run begins or ends. Only sent when it changes something.
    /// </summary>
    public sealed class NodesRegrownEvent : SimEvent
    {
        public bool All;
    }

    /// <summary>A carrier built something on a tile (doc 04 §1).</summary>
    public sealed class ConstructPlacedEvent : SimEvent
    {
        public EntityId Player;
        /// <summary>Index into ContentDb.Buildings.</summary>
        public byte Building;
        public byte X, Y;
        public byte Rotation;
        public ushort Hp;
        /// <summary>The container of a chest; 0 for what holds nothing.</summary>
        public uint ContainerId;
    }

    /// <summary>
    /// What stood on a tile is gone: taken down by <see cref="Player"/>, or <see cref="Destroyed"/>
    /// by the raiders, or (<see cref="Player"/> None and not destroyed) cleared away with the run.
    /// </summary>
    public sealed class ConstructRemovedEvent : SimEvent
    {
        public byte X, Y;
        public EntityId Player;
        public bool Destroyed;
    }

    /// <summary>A raider's blow landed on a construct: the HP it has left.</summary>
    public sealed class ConstructDamagedEvent : SimEvent
    {
        public byte X, Y;
        public ushort Hp;
    }

    /// <summary>One item of a <see cref="BeltStateEvent"/>: what a client needs to draw it.</summary>
    public struct BeltItemInfo
    {
        public uint MailId;
        public ushort Kind;
        public uint AddressKey;
        public byte Unit;
        public byte Lane;
        public float Pos;
    }

    /// <summary>
    /// A belt segment whole (doc 06 §4 LaneState): its head tile and every item on it. Sent when
    /// the segment is compiled anew, to whoever joins, and to a client whose checksum was off.
    /// </summary>
    public sealed class BeltStateEvent : SimEvent
    {
        public byte X, Y;
        public uint Tick;
        public readonly List<BeltItemInfo> Items = new List<BeltItemInfo>();
    }

    /// <summary>A piece of mail put on a segment's head (doc 06 §4 LaneInsert): from the Intake's outfeed, a splitter.</summary>
    public sealed class BeltItemAddedEvent : SimEvent
    {
        public byte X, Y;
        public BeltItemInfo Item;
    }

    /// <summary>A piece of mail went from a segment's end onto the head of the segment headed at (X, Y), in its lane.</summary>
    public sealed class BeltItemMovedEvent : SimEvent
    {
        public uint MailId;
        public byte X, Y;
        public byte Lane;
    }

    /// <summary>A piece of mail left the belts (doc 06 §4 LaneRemove): into a mailbox, or back to the Intake.</summary>
    public sealed class BeltItemRemovedEvent : SimEvent
    {
        public uint MailId;
    }

    /// <summary>Every 2 s per segment (doc 06 §4 LaneChecksum): the count and <see cref="Automation.BeltNetwork.Checksum"/>.</summary>
    public sealed class BeltChecksumEvent : SimEvent
    {
        public byte X, Y;
        public ushort Count;
        public uint Hash;
    }

    /// <summary>
    /// The filters of the sorter or splitter built on (X, Y), all of them, output by output (doc 06
    /// §3: ConfigureConstruct's confirmed event to everybody).
    /// </summary>
    public sealed class ConstructConfiguredEvent : SimEvent
    {
        public byte X, Y;
        /// <summary><see cref="Automation.MailFilter.Encode"/> of each.</summary>
        public readonly List<ushort> Filters = new List<ushort>();
        public EntityId Player;
    }

    /// <summary>
    /// What the sorter or splitter on (X, Y) is doing, to whoever has its filters open (doc 06 §3:
    /// "E when viewing"; doc 09 §2.7 "matches N items currently in Intake").
    /// </summary>
    public sealed class SorterStatusEvent : SimEvent
    {
        public byte X, Y;
        public byte Buffered;
        public ushort Sorted, Overflowed;
        /// <summary>Per output (a sorter's last is its overflow): pieces of the Intake's mail it would take.</summary>
        public readonly List<ushort> Matches = new List<ushort>();
    }

    /// <summary>The inserter on (X, Y) moved a piece of mail of a kind (doc 04 §2.5): the clients swing its arm.</summary>
    public sealed class InserterSwungEvent : SimEvent
    {
        public byte X, Y;
        public ushort Kind;
    }

    /// <summary>
    /// A capsule in the pipes (doc 04 §2.2): from the middle of tile (FromX, FromY) — an inlet's
    /// when it is sent, where it is when its way is broken and it goes round, or to a joiner — by
    /// the shortest way to the outlet on (OutX, OutY). A client sends its own capsule of the mail
    /// that way, replacing any it had.
    /// </summary>
    public sealed class PipeCapsuleEvent : SimEvent
    {
        public BeltItemInfo Item;
        public byte FromX, FromY, OutX, OutY;
    }

    /// <summary>A piece of mail left the pipes: put out by its outlet, or back to the Intake.</summary>
    public sealed class PipeCapsuleRemovedEvent : SimEvent
    {
        public uint MailId;
    }

    /// <summary>A carrier pushes the segment headed at (X, Y) (doc 04 §2.1): it runs faster for <see cref="Ticks"/> ticks.</summary>
    public sealed class BeltPushedEvent : SimEvent
    {
        public byte X, Y;
        public ushort Ticks;
        public EntityId Player;
    }

    /// <summary>The run is over or begins anew: nothing the carriers built stands any more.</summary>
    public sealed class ConstructsClearedEvent : SimEvent
    {
    }

    /// <summary>
    /// The server put the player before a mailbox (<see cref="PlaytestFlags.AtMailbox"/>); only the
    /// player hears of it, and turns to look at the mailbox.
    /// </summary>
    public sealed class PlayerParkedEvent : SimEvent
    {
        public ushort Destination;
    }

    /// <summary>
    /// The host picked another map or seed in the Lobby (doc 06 §5): every client builds the map
    /// itself from the id and the seed and checks the hash; the map's chests are new containers.
    /// </summary>
    public sealed class WorldChangedEvent : SimEvent
    {
        public string MapId = "";
        public uint Seed;
        public ulong MapHash;
        public List<uint> ChestContainerIds = new List<uint>();
    }

    /// <summary>
    /// A starting kit was handed out (doc 03 §4.4): a player's personal items into their pockets,
    /// or the team's — the host's kit — into the chest by the Post Office and its vehicles by the counter.
    /// </summary>
    public sealed class KitGivenEvent : SimEvent
    {
        public EntityId Player;
        /// <summary>Index into ContentDb.Kits.</summary>
        public byte Kit;
        public bool Team;
    }

    /// <summary>A vehicle is bought (doc 10 §3) and stands parked by the counter.</summary>
    public sealed class VehicleAddedEvent : SimEvent
    {
        public EntityId Vehicle;
        /// <summary>Index into ContentDb.Vehicles.</summary>
        public byte Def;
        public float X, Z;
        public float Heading;
        public uint CargoContainer;
        public EntityId Buyer;
    }

    /// <summary>A carrier gets into the saddle: the vehicle goes where they go until they get off.</summary>
    public sealed class VehicleMountedEvent : SimEvent
    {
        public EntityId Vehicle;
        public EntityId Player;
    }

    /// <summary>The rider is off (by the jump key, going down, the connection lost): the vehicle stands here.</summary>
    public sealed class VehicleParkedEvent : SimEvent
    {
        public EntityId Vehicle;
        public float X, Z;
        public float Heading;
        /// <summary>Who got off.</summary>
        public EntityId Player;
    }

    /// <summary>A run ends or begins: no vehicle is left.</summary>
    public sealed class VehiclesClearedEvent : SimEvent
    {
    }
}
