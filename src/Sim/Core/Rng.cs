using System;

namespace PerformativeMail.Sim.Core
{
    /// <summary>PCG32 (XSH-RR 64/32). Deterministic across platforms: integer math only.</summary>
    public struct Pcg32
    {
        private ulong _state;
        private readonly ulong _inc;

        public Pcg32(ulong seed, ulong sequence)
        {
            _state = 0;
            _inc = (sequence << 1) | 1UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = unchecked(old * 6364136223846793005UL + _inc);
            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }
    }

    /// <summary>
    /// Named RNG stream (doc 07 §4.3). Each stream is advanced only by its owning system so that
    /// e.g. mail spawn reproduces for a seed independent of player timing.
    /// </summary>
    public sealed class RngStream
    {
        private Pcg32 _pcg;

        public string Name { get; }

        public RngStream(uint seed, string name)
        {
            Name = name;
            var h = Fnv64.Create();
            h.Add(name);
            _pcg = new Pcg32(seed, h.Value);
        }

        public uint NextUInt() => _pcg.NextUInt();

        /// <summary>Uniform integer in [0, exclusiveMax) without modulo bias.</summary>
        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            uint bound = (uint)exclusiveMax;
            uint threshold = (uint)(-bound) % bound;
            while (true)
            {
                uint r = _pcg.NextUInt();
                if (r >= threshold) return (int)(r % bound);
            }
        }

        /// <summary>Uniform integer in [min, max] inclusive.</summary>
        public int NextRange(int min, int max) => min + NextInt(max - min + 1);

        /// <summary>True with probability permille / 1000.</summary>
        public bool Chance(int permille) => NextInt(1000) < permille;
    }
}
