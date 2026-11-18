using System;
using System.Collections.Generic;

namespace PerformativeMail.Sim.Perks
{
    public enum PerkCategory : byte { Carrier = 0, Facility = 1, Postal = 2, Defense = 3 }

    public enum PerkRarity : byte { Common = 0, Uncommon = 1, Rare = 2 }

    public enum ModifierOp : byte { Mul = 0, Add = 1 }

    public readonly struct StatModifier
    {
        public readonly Stat Stat;
        public readonly ModifierOp Op;
        public readonly float Value;

        public StatModifier(Stat stat, ModifierOp op, float value) { Stat = stat; Op = op; Value = value; }
    }

    /// <summary>Perk definition (doc 08 §2.9). Immutable after load.</summary>
    public sealed class PerkDef
    {
        public string Id { get; init; } = "";
        /// <summary>Position in ContentDb.Perks; what the wire carries.</summary>
        public byte Index { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public PerkCategory Category { get; init; }
        /// <summary>Team: applied once for everybody, unique per run. Personal: its holder's, and may stack.</summary>
        public bool Team { get; init; }
        public PerkRarity Rarity { get; init; }
        public IReadOnlyList<StatModifier> Modifiers { get; init; } = Array.Empty<StatModifier>();
        /// <summary>Offered only for a shift of at least this number.</summary>
        public int ShiftMin { get; init; } = 1;
        /// <summary>Offered only from this Postal Rank.</summary>
        public int RankMin { get; init; } = 1;
        /// <summary>Perk ids this one never shares a run with.</summary>
        public IReadOnlyList<string> Excludes { get; init; } = Array.Empty<string>();
        public int MaxStacks { get; init; } = 1;
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

        public bool ExcludesPerk(PerkDef other)
        {
            for (int i = 0; i < Excludes.Count; i++)
                if (Excludes[i] == other.Id) return true;
            return false;
        }

        /// <summary>Exclusion holds in both directions, whichever of the two names the other.</summary>
        public bool ConflictsWith(PerkDef other) => ExcludesPerk(other) || other.ExcludesPerk(this);

        /// <summary>True if any other perk of <paramref name="held"/> and this one exclude each other.</summary>
        public bool ConflictsWithAny(IReadOnlyList<PerkDef> held)
        {
            for (int i = 0; i < held.Count; i++)
                if (!ReferenceEquals(held[i], this) && ConflictsWith(held[i])) return true;
            return false;
        }

        /// <summary>How often this perk is among <paramref name="held"/>.</summary>
        public int StacksIn(IReadOnlyList<PerkDef> held)
        {
            int n = 0;
            for (int i = 0; i < held.Count; i++)
                if (ReferenceEquals(held[i], this)) n++;
            return n;
        }
    }
}
