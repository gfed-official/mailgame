using System;
using System.Collections.Generic;

namespace PerformativeMail.Sim.Content
{
    /// <summary>
    /// Flat key/value balance constants (doc 08 §2.14). Values are numbers or arrays of numbers.
    /// Missing keys throw: a tunable the code needs must exist in content/balance.json.
    /// </summary>
    public sealed class Balance
    {
        private readonly Dictionary<string, double[]> _values;

        public Balance(Dictionary<string, double[]> values) { _values = values; }

        public bool Has(string key) => _values.ContainsKey(key);
        public IEnumerable<string> Keys => _values.Keys;

        public double GetDouble(string key)
        {
            if (!_values.TryGetValue(key, out var v) || v.Length != 1)
                throw new KeyNotFoundException($"balance.json: missing scalar '{key}'");
            return v[0];
        }

        public float GetFloat(string key) => (float)GetDouble(key);
        public int GetInt(string key) => (int)Math.Round(GetDouble(key));

        public double[] GetArray(string key)
        {
            if (!_values.TryGetValue(key, out var v))
                throw new KeyNotFoundException($"balance.json: missing array '{key}'");
            return v;
        }

        /// <summary>Per-shift array lookup (shift is 1-based; clamps to the last entry).</summary>
        public double GetForShift(string key, int shift)
        {
            var arr = GetArray(key);
            int i = Math.Clamp(shift - 1, 0, arr.Length - 1);
            return arr[i];
        }
    }
}
