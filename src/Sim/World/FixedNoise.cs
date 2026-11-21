namespace PerformativeMail.Sim.World
{
    /// <summary>
    /// Noise for world generation in integers only (doc 02 §2: "noise is an in-repo implementation
    /// in fixed point", so every platform builds the same island): value noise on an integer
    /// lattice, smoothstep between the corners in 16.16 fixed point, and octaves of it summed.
    /// Values run from 0 to 65535.
    /// </summary>
    public static class FixedNoise
    {
        public const int One = 65536;

        private static uint Hash(uint seed, int x, int y)
        {
            uint h = seed ^ unchecked((uint)x * 0x9E3779B1u) ^ unchecked((uint)y * 0x85EBCA77u);
            h ^= h >> 15;
            h = unchecked(h * 0x2C1B3C6Du);
            h ^= h >> 12;
            h = unchecked(h * 0x297A2D39u);
            h ^= h >> 15;
            return h & 0xFFFF;
        }

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        /// <summary>3t² − 2t³ of a 16.16 fraction.</summary>
        private static long Smooth(long t) => (t * t >> 16) * (3 * One - 2 * t) >> 16;

        /// <summary>One octave: the lattice every <paramref name="cell"/> tiles.</summary>
        public static int Value(uint seed, int x, int y, int cell)
        {
            int gx = FloorDiv(x, cell), gy = FloorDiv(y, cell);
            long sx = Smooth(((long)(x - gx * cell) << 16) / cell), sy = Smooth(((long)(y - gy * cell) << 16) / cell);
            long a = Hash(seed, gx, gy), b = Hash(seed, gx + 1, gy), c = Hash(seed, gx, gy + 1), d = Hash(seed, gx + 1, gy + 1);
            long top = a + ((b - a) * sx >> 16), bottom = c + ((d - c) * sx >> 16);
            return (int)(top + ((bottom - top) * sy >> 16));
        }

        /// <summary>Octaves from <paramref name="cell"/> down, each half the size and half the weight of the last.</summary>
        public static int Fbm(uint seed, int x, int y, int cell, int octaves)
        {
            long sum = 0, weights = 0;
            int weight = 1 << octaves;
            for (int i = 0; i < octaves && cell >= 1; i++, cell /= 2, weight /= 2)
            {
                sum += (long)Value(seed + (uint)i * 7919u, x, y, cell) * weight;
                weights += weight;
            }
            return (int)(sum / weights);
        }
    }
}
