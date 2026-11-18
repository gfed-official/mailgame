using System;
using System.Collections.Generic;

namespace PerformativeMail.Sim.Perks
{
    /// <summary>
    /// The numbers a perk may change (doc 07 §6: "a closed enum defined in code"). A new stat is a
    /// code change; a perk on a stat that exists is data. Each is resolved over the base named
    /// here; a stat used as a switch has base 0 and is on above 0.
    /// The names are what content/perks/*.json writes; the order is free to change.
    /// </summary>
    public enum Stat : byte
    {
        // --- of one carrier: a team perk changes it for everybody, a personal one for its holder ---
        /// <summary>Walk and sprint speed as a share of the balance speeds; base 1.</summary>
        PlayerSpeed,
        /// <summary>The share of speed lost to what is carried, before the floor.</summary>
        WeightPenalty,
        /// <summary>Seconds a mailbox insert is held.</summary>
        InsertHold,
        /// <summary>Seconds from going down to standing up at the Post Office.</summary>
        RespawnSeconds,
        /// <summary>Switch: the death bag takes the inventory and leaves the hotbar.</summary>
        KeepHotbarOnDeath,
        /// <summary>Speed gained by a correct delivery, as a fraction (0.3 = +30 %); base 0.</summary>
        DeliveryBoost,
        /// <summary>How long that lasts, seconds; base 0.</summary>
        DeliveryBoostSeconds,
        /// <summary>Hammer damage.</summary>
        MeleeDamage,
        /// <summary>Hammer knockback, metres.</summary>
        MeleeKnockback,

        // --- of the team: only a team perk may change these ---
        /// <summary>The shift's quota, ¢.</summary>
        Quota,
        /// <summary>What a piece of stacking mail (letters, postcards) pays, ¢.</summary>
        PaperMailValue,
        /// <summary>What a misdelivery costs, ¢.</summary>
        MisdeliveryPenalty,
        /// <summary>The complaint a misdelivery raises.</summary>
        MisdeliveryComplaint,
        /// <summary>Share of its value mail pays one shift late.</summary>
        LateValueRatio,
        /// <summary>Seconds of warning before a raid.</summary>
        RaidWarningSeconds,
    }

    public static class Stats
    {
        public static readonly int Count = Enum.GetValues(typeof(Stat)).Length;

        private static readonly Dictionary<string, Stat> ByName = BuildNames();

        private static Dictionary<string, Stat> BuildNames()
        {
            var names = new Dictionary<string, Stat>(StringComparer.Ordinal);
            foreach (Stat stat in Enum.GetValues(typeof(Stat))) names[stat.ToString()] = stat;
            return names;
        }

        /// <summary>A team stat has one value for the whole team; a personal perk cannot change it.</summary>
        public static bool IsTeam(Stat stat) => stat >= Stat.Quota;

        /// <summary>The stat a content file names, exactly as the enum spells it.</summary>
        public static bool TryParse(string name, out Stat stat) => ByName.TryGetValue(name, out stat);
    }
}
