using PerformativeMail.Sim.Mail;

namespace PerformativeMail.Sim.Automation
{
    /// <summary>What a filter of a sorter's or a splitter's output looks at (doc 04 §2.4).</summary>
    public enum FilterKind : byte
    {
        /// <summary>An empty slot: a sorter's output it is on takes nothing, a splitter's anything.</summary>
        None = 0,
        /// <summary>Every address of a street ("Larch Lane").</summary>
        Street = 1,
        /// <summary>Every address of a district.</summary>
        District = 2,
        /// <summary>A kind of mail (letters, parcels…), whatever the address.</summary>
        Kind = 3,
        /// <summary>
        /// One house, by its index in the map's destinations: a stop of a Vehicle Depot's route
        /// (doc 04 §3.2 "a destination address"); no sorter filters by it.
        /// </summary>
        Address = 4,
    }

    /// <summary>
    /// One filter (doc 04 §2.4: district, street, kind; doc 08 §3.5 ConstructState config). On the
    /// wire it is 16 bits, the kind over the value: a street's or district's id, a mail kind's index;
    /// an <see cref="FilterKind.Address"/> is the top bit over 15 bits of a destination's index.
    /// </summary>
    public readonly struct MailFilter : System.IEquatable<MailFilter>
    {
        public readonly FilterKind Kind;
        public readonly ushort Value;

        public MailFilter(FilterKind kind, int value)
        {
            Kind = kind;
            Value = kind switch
            {
                FilterKind.None => 0,
                FilterKind.Address => (ushort)(value & 0x7FFF),
                _ => (byte)value,
            };
        }

        public static readonly MailFilter Empty = default;

        public bool IsEmpty => Kind == FilterKind.None;

        public ushort Encode() => Kind == FilterKind.Address ? (ushort)(0x8000 | Value) : (ushort)(((int)Kind << 8) | Value);

        public static MailFilter Decode(ushort bits)
        {
            if ((bits & 0x8000) != 0) return new MailFilter(FilterKind.Address, bits & 0x7FFF);
            var kind = (FilterKind)(bits >> 8);
            return kind > FilterKind.Kind ? Empty : new MailFilter(kind, bits & 0xFF);
        }

        /// <summary>Whether a piece of mail is caught by it; an empty filter catches nothing.</summary>
        public bool Matches(in MailItem mail) => Kind switch
        {
            FilterKind.Street => mail.Address.Street == Value,
            FilterKind.District => mail.Address.District == Value,
            FilterKind.Kind => mail.Kind == Value,
            _ => false,
        };

        public bool Equals(MailFilter other) => Kind == other.Kind && Value == other.Value;
        public override bool Equals(object? obj) => obj is MailFilter f && Equals(f);
        public override int GetHashCode() => Encode();
        public static bool operator ==(MailFilter a, MailFilter b) => a.Equals(b);
        public static bool operator !=(MailFilter a, MailFilter b) => !a.Equals(b);
    }
}
