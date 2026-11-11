using System;

namespace PerformativeMail.Sim.World
{
    /// <summary>
    /// Canonical address {districtId}:{streetId}:{number}[:{unit}] (doc 02 §3.6). Unit 0 = no unit.
    /// </summary>
    public readonly struct AddressId : IEquatable<AddressId>, IComparable<AddressId>
    {
        /// <summary>district (8 bits) | street (8 bits) | number (16 bits).</summary>
        public readonly uint Key;
        public readonly byte Unit;

        public AddressId(int district, int street, int number, int unit = 0)
        {
            Key = ((uint)(district & 0xFF) << 24) | ((uint)(street & 0xFF) << 16) | (uint)(number & 0xFFFF);
            Unit = (byte)unit;
        }

        public AddressId(uint key, byte unit) { Key = key; Unit = unit; }

        public int District => (int)(Key >> 24);
        public int Street => (int)((Key >> 16) & 0xFF);
        public int Number => (int)(Key & 0xFFFF);

        public bool Equals(AddressId other) => Key == other.Key && Unit == other.Unit;
        public override bool Equals(object? obj) => obj is AddressId other && Equals(other);
        public override int GetHashCode() => (int)(Key * 31 + Unit);
        public static bool operator ==(AddressId a, AddressId b) => a.Equals(b);
        public static bool operator !=(AddressId a, AddressId b) => !a.Equals(b);

        public int CompareTo(AddressId other)
        {
            int c = Key.CompareTo(other.Key);
            return c != 0 ? c : Unit.CompareTo(other.Unit);
        }

        public override string ToString() =>
            Unit == 0 ? $"{District}:{Street}:{Number}" : $"{District}:{Street}:{Number}:{Unit}";
    }
}
