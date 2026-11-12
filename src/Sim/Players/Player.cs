using System.Collections.Generic;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;

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
        /// <summary>Run totals; cleared when a run starts.</summary>
        public int Deliveries;
        public int Misdeliveries;
        public int Earned;
        public int ShiftDeliveries;
        /// <summary>Ready flag of the Lobby and Prep (doc 01 §1); cleared on every phase change.</summary>
        public bool Ready;
        /// <summary>Order of arrival; the earliest connected player is the host.</summary>
        public uint JoinOrder;

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
