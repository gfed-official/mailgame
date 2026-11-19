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
    /// The cards of a Draft (doc 01 §2.5), the same for everybody, as indices into
    /// ContentDb.Perks; sent just before the PhaseChanged into Draft.
    /// </summary>
    public sealed class DraftOfferEvent : SimEvent
    {
        public List<byte> Perks = new List<byte>();
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

    /// <summary>
    /// The server put the player before a mailbox (<see cref="PlaytestFlags.AtMailbox"/>); only the
    /// player hears of it, and turns to look at the mailbox.
    /// </summary>
    public sealed class PlayerParkedEvent : SimEvent
    {
        public ushort Destination;
    }
}
