using System.Collections.Generic;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Perks;

namespace PerformativeMail.Sim.Players
{
    /// <summary>Server-side player: movement state, input queue, body containers.</summary>
    public sealed class Player
    {
        /// <summary>Ticks of position history kept for interact lag compensation (doc 06 §4.3).</summary>
        public const int HistoryLength = 16;
        /// <summary>Inputs buffered beyond this are dropped oldest-first.</summary>
        public const int MaxQueuedInputs = 12;

        public EntityId Id { get; }
        public string Name { get; }
        /// <summary>0-based seat, stable for the session; used for colours and spawn offsets.</summary>
        public byte Slot { get; }
        public GridContainer Hotbar { get; }
        public GridContainer Inventory { get; }

        public PlayerMoveState Move;
        public byte HeldSlot;
        public float SpeedMult = 1f;
        /// <summary>External container this player is viewing, 0 if none.</summary>
        public uint OpenContainer;

        public readonly Queue<PlayerInput> Inputs = new Queue<PlayerInput>();
        public uint LastQueuedSeq;
        public uint LastProcessedSeq;
        public float InputCredit = 1f;
        public float RequestCredit;

        public uint LastDeliverTick;
        public bool HasDelivered;
        public uint LastHarvestTick;
        public bool HasHarvested;
        /// <summary>Run totals; cleared when a run starts.</summary>
        public int Deliveries;
        public int Misdeliveries;
        public int Earned;
        public int ShiftDeliveries;
        /// <summary>Ready flag of the Lobby and Prep (doc 01 §1); cleared on every phase change.</summary>
        public bool Ready;
        /// <summary>Order of arrival; the earliest connected player is the host.</summary>
        public uint JoinOrder;
        /// <summary>Who this is from one connection to the next (doc 06 §6: "same account id"); 0: nobody in particular.</summary>
        public ulong Account;
        /// <summary>
        /// Lost their connection during a run (doc 06 §6): they stand frozen, out of harm's way and
        /// with their pockets on them until this tick, then their bag drops and they are gone. 0: here.
        /// </summary>
        public uint AwayUntilTick;
        public bool Away => AwayUntilTick != 0;
        /// <summary>What the player's Hello asked for (playtest nits 10 and 13).</summary>
        public PlaytestFlags Playtest;
        /// <summary>The Postal Rank the player's Hello gave: which starting kits are open to them.</summary>
        public int Rank = 1;
        /// <summary>The Draft's rerolls this player has used in the run (doc 01 §2.5).</summary>
        public int RerollsUsed;
        /// <summary>The cards of this player's own after a reroll; null: the Draft's shared offer.</summary>
        public List<PerkDef>? Offer;
        /// <summary>The starting kit chosen in the Lobby (doc 03 §4.4); the content's default until then.</summary>
        public Content.KitDef? Kit;
        /// <summary>This run's personal kit items are in the pockets already.</summary>
        public bool KitGiven;
        /// <summary>The shift this player wants runs to start at (<c>--shift=N</c>), 0 = the first as always.</summary>
        public byte PlaytestShift;
        /// <summary>The money (cents) this player wants the team's wallet to hold as a run starts, 0 = none (--playtest-wallet).</summary>
        public int PlaytestWallet;

        /// <summary>Doc 05 §1.1. 0 while dead (<see cref="PlayerMoveState.Dead"/>).</summary>
        public float Hp;
        public float MaxHp;
        public uint LastDamageTick;
        /// <summary>HP still to come from what they ate (Berries heal over seconds), and how much of it per tick.</summary>
        public float HealPool;
        public float HealPerTick;
        /// <summary>Tick a dead player stands up again at the Post Office.</summary>
        public uint RespawnTick;
        /// <summary>Until this tick, or until they hurt a raider, a respawned carrier is not hurt and not gone for (nit 17).</summary>
        public uint ProtectedUntilTick;
        /// <summary>Past that tick the protection holds for as long as they have not left the Post Office's yard.</summary>
        public bool ProtectedInYard;
        /// <summary>
        /// Parked before a mailbox (<see cref="PlaytestFlags.AtMailbox"/>) in a shift with a raid: the
        /// protection holds for as long as they stand within <see cref="PlaytestSpot.GuardRadius"/> of
        /// <see cref="ParkX"/>, <see cref="ParkZ"/> and have not hurt a raider.
        /// </summary>
        public bool ProtectedAtPark;
        public float ParkX, ParkZ;
        public uint HammerReadyTick;
        public uint CannonReadyTick;
        /// <summary>Run totals of the fight; cleared when a run starts.</summary>
        public int Kills;
        public int LootCoins;

        /// <summary>Personal perks of the run (doc 01 §2.5), one entry per stack, and what they do to this carrier's stats.</summary>
        public readonly List<PerkDef> Perks = new List<PerkDef>();
        public readonly StatSheet Stats = new StatSheet();
        /// <summary>Until this tick the speed of a correct delivery lasts (<see cref="Stat.DeliveryBoost"/>); 0: none.</summary>
        public uint BoostUntilTick;
        /// <summary>The card of the Draft under way this carrier has taken; -1: none yet.</summary>
        public sbyte DraftPick = -1;

        public bool Dead => Move.Dead;
        /// <summary>Health as the snapshot carries it: 0 dead, 1..255 alive.</summary>
        public byte HpByte => Dead || MaxHp <= 0f ? (byte)0 : (byte)System.Math.Clamp((int)System.MathF.Ceiling(Hp / MaxHp * 255f), 1, 255);

        private readonly float[] _histX = new float[HistoryLength];
        private readonly float[] _histZ = new float[HistoryLength];
        private int _histHead;
        private int _histCount;

        public Player(EntityId id, string name, byte slot, GridContainer hotbar, GridContainer inventory)
        {
            Id = id; Name = name; Slot = slot; Hotbar = hotbar; Inventory = inventory;
        }

        public bool Owns(GridContainer c) => ReferenceEquals(c, Hotbar) || ReferenceEquals(c, Inventory);

        public void RecordPosition()
        {
            _histX[_histHead] = Move.X;
            _histZ[_histHead] = Move.Z;
            _histHead = (_histHead + 1) % HistoryLength;
            if (_histCount < HistoryLength) _histCount++;
        }

        /// <summary>True if the player is, or was at any of the last <paramref name="ticks"/> ticks, within range of a point.</summary>
        public bool WasWithin(float x, float z, float range, int ticks)
        {
            float rangeSq = range * range;
            float dx = Move.X - x, dz = Move.Z - z;
            if (dx * dx + dz * dz <= rangeSq) return true;
            int n = ticks < _histCount ? ticks : _histCount;
            for (int i = 1; i <= n; i++)
            {
                int idx = (_histHead - i + HistoryLength * 2) % HistoryLength;
                dx = _histX[idx] - x; dz = _histZ[idx] - z;
                if (dx * dx + dz * dz <= rangeSq) return true;
            }
            return false;
        }
    }
}
