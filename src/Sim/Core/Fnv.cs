using System.Text;

namespace PerformativeMail.Sim.Core
{
    /// <summary>FNV-1a helpers used for the protocol hash, content hash, map hash and container hashes.</summary>
    public struct Fnv32
    {
        public const uint Offset = 2166136261;
        private const uint Prime = 16777619;

        public uint Value;

        public static Fnv32 Create() => new Fnv32 { Value = Offset };

        public void Add(byte b) { Value = (Value ^ b) * Prime; }

        public void Add(uint v)
        {
            Add((byte)v); Add((byte)(v >> 8)); Add((byte)(v >> 16)); Add((byte)(v >> 24));
        }

        public void Add(int v) => Add((uint)v);

        public void Add(string s)
        {
            foreach (byte b in Encoding.UTF8.GetBytes(s)) Add(b);
            Add((byte)0xFF);
        }

        public static uint Of(string s)
        {
            var h = Create();
            h.Add(s);
            return h.Value;
        }
    }

    public struct Fnv64
    {
        public const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public ulong Value;

        public static Fnv64 Create() => new Fnv64 { Value = Offset };

        public void Add(byte b) { Value = (Value ^ b) * Prime; }

        public void Add(uint v)
        {
            Add((byte)v); Add((byte)(v >> 8)); Add((byte)(v >> 16)); Add((byte)(v >> 24));
        }

        public void Add(int v) => Add((uint)v);

        public void Add(string s)
        {
            foreach (byte b in Encoding.UTF8.GetBytes(s)) Add(b);
            Add((byte)0xFF);
        }
    }
}
