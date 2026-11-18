namespace PerformativeMail.Sim.Perks
{
    /// <summary>
    /// What a set of perks does to the stats (doc 07 §6, doc 08 §2.9): per stat the product of the
    /// multipliers and the sum of the adders, resolved as base × Π(mul) + Σ(add). Defs are never
    /// changed; the sheet lies over them. The team has one sheet and every carrier their own.
    /// </summary>
    public sealed class StatSheet
    {
        private readonly float[] _mul = new float[Stats.Count];
        private readonly float[] _add = new float[Stats.Count];

        public StatSheet() => Clear();

        public void Clear()
        {
            for (int i = 0; i < _mul.Length; i++)
            {
                _mul[i] = 1f;
                _add[i] = 0f;
            }
        }

        /// <summary>One more stack of a perk.</summary>
        public void Apply(PerkDef perk)
        {
            foreach (var m in perk.Modifiers)
            {
                if (m.Op == ModifierOp.Mul) _mul[(int)m.Stat] *= m.Value;
                else _add[(int)m.Stat] += m.Value;
            }
        }

        public float Resolve(Stat stat, float baseValue) => baseValue * _mul[(int)stat] + _add[(int)stat];

        /// <summary>This sheet and another over the same base: the team's and a carrier's own.</summary>
        public float Resolve(Stat stat, float baseValue, StatSheet other) =>
            baseValue * _mul[(int)stat] * other._mul[(int)stat] + _add[(int)stat] + other._add[(int)stat];
    }
}
