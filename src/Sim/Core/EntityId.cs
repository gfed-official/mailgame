using System;

namespace PerformativeMail.Sim.Core
{
    public enum EntityClass : byte
    {
        None = 0,
        Player = 1,
        Vehicle = 2,
        Construct = 3,
        Enemy = 4,
        WorldItem = 5,
    }

    /// <summary>uint32 id: high byte is the entity class, low 24 bits a per-class counter (doc 07 §4.2).</summary>
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public readonly uint Value;

        public EntityId(uint value) { Value = value; }
        public EntityId(EntityClass cls, uint counter) { Value = ((uint)cls << 24) | (counter & 0x00FFFFFF); }

        public EntityClass Class => (EntityClass)(Value >> 24);
        public uint Counter => Value & 0x00FFFFFF;
        public bool IsNone => Value == 0;

        public static readonly EntityId None = new EntityId(0);

        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => (int)Value;
        public static bool operator ==(EntityId a, EntityId b) => a.Value == b.Value;
        public static bool operator !=(EntityId a, EntityId b) => a.Value != b.Value;
        public override string ToString() => $"{Class}#{Counter}";
    }

    /// <summary>Allocates ids per class; ids are never reused within a run.</summary>
    public sealed class EntityIdAllocator
    {
        private readonly uint[] _next = new uint[8];

        public EntityId Allocate(EntityClass cls)
        {
            uint counter = ++_next[(int)cls];
            if (counter > 0x00FFFFFF) throw new InvalidOperationException($"Entity id space exhausted for {cls}");
            return new EntityId(cls, counter);
        }
    }
}
