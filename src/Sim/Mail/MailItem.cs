using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Mail
{
    public static class MailFlags
    {
        public const byte Cursed = 1 << 0;
    }

    /// <summary>One deliverable item (doc 03 §1.2). Lives inside a container stack, never loose.</summary>
    public struct MailItem
    {
        /// <summary>Unique per run, allocated sequentially by the server.</summary>
        public uint Id;
        /// <summary>MailKindDef.Index.</summary>
        public ushort Kind;
        public AddressId Address;
        /// <summary>¢ at spawn, already multiplied by distance and shift multipliers.</summary>
        public ushort Value;
        public byte SpawnShift;
        public byte DeadlineShift;
        public byte Flags;
    }
}
