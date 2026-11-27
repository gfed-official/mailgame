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
        /// <summary>Jumps a carrier may make off the ground; base 0 (doc 10 §4.1 Bunny Hop: 1).</summary>
        AirJumps,
        /// <summary>The speed of a vehicle pedalled on land (the bike), as a share of its def's; base 1.</summary>
        BikeSpeed,
        /// <summary>Rows added to the carrier's inventory grid (2×8 → 3×8); base 0 (doc 10 §4.1 Big Pockets).</summary>
        InventoryRows,
        /// <summary>Rows added to the cargo grid of the bikes, while any carrier holds it; base 0 (Bike Courier).</summary>
        BikeCargoRows,

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
        /// <summary>The speed of what is on a belt, as a share of the belt's own; base 1.</summary>
        BeltSpeed,
        /// <summary>An inserter's swings a second, as a share of its def's; base 1.</summary>
        InserterRate,
        /// <summary>A turret's damage a shot, as a share of its def's; base 1.</summary>
        TurretDamage,
        /// <summary>Every construct's most HP, as a share of its def's; base 1.</summary>
        ConstructHp,
        /// <summary>The value of the mail a Delivery spawns, as a share of the quota's; base 1.</summary>
        SpawnValue,
        /// <summary>Shifts earlier than the map says that a district opens; base 0.</summary>
        DistrictsEarly,
        /// <summary>The rows of a built chest's and depot's grid, as a share of its def's; base 1 (doc 10 §4.2 Bulk Depot).</summary>
        StorageRows,
        /// <summary>What a stack of letters or postcards holds, as a multiple of its kind's; base 1 (doc 10 §4.3 Bulk Mailer).</summary>
        PaperMailStack,
        /// <summary>The filter slots of a sorter's output a carrier may set; base the sorter's open ones (doc 10 §4.2 Extra Labels).</summary>
        SorterFilterSlots,
        /// <summary>A sorter's pieces a second, as a share of its def's; base 1 (doc 10 §4.2 Postmaster's Eye).</summary>
        SorterThroughput,
        /// <summary>Seconds of the Delivery phase, over the balance's for the shift (doc 01 §6 Rush Hour: −30).</summary>
        DeliverySeconds,
        /// <summary>The chance a raider comes as a Mega, as a share of the waves' (doc 01 §6 Megamail: ×2).</summary>
        MegaChance,
        /// <summary>How fast the complaint meter decays, as a share of the balance's (doc 01 §6 Postal Audit: ×0.5).</summary>
        ComplaintDecay,
        /// <summary>Players more than are here that the quota is reckoned for (doc 01 §6 Skeleton Crew: playerScale(n+2)).</summary>
        QuotaPlayers,
        /// <summary>A land vehicle's speed on a road, as a share of its def's (doc 01 §6 No Roads: dirt paths, ×0.6).</summary>
        VehicleRoadSpeed,
        /// <summary>What a delivery pays, as a share (doc 10 §6 Postal Inspector: next shift ×0.9); base 1.</summary>
        DeliveryEarnings,
        /// <summary>The complaint at Payday that calls the Postal Inspector, over its event's (doc 01 §6 Postal Audit: 75 → 50).</summary>
        InspectorComplaint,
    }

    /// <summary>
    /// What a perk switches on rather than changes by a number (ADR-0010 "RuleFlag": on while at least one perk held
    /// has it; doc 08 §2.9 "rules"). A new flag is a code change and its handler; a perk with a flag that exists is
    /// data. The names are what content/perks/*.json writes, in snake_case.
    /// </summary>
    public enum RuleFlag : byte
    {
        /// <summary>Doc 10 §4.4 Insured: a construct broken in a shift stands again, free, when the next Prep begins.</summary>
        InsuredRebuild,
        /// <summary>Doc 10 §4.2 Presort: the Intake's outfeed puts the mail on the belt a street at a time.</summary>
        Presort,
        /// <summary>Doc 10 §4.3 Priority Post: some mail spawns Priority, worth more if delivered in time.</summary>
        PriorityPost,
        /// <summary>Doc 10 §4.1 Local Knowledge (personal): the HUD points to the mailbox of the mail in hand.</summary>
        LocalKnowledge,
        /// <summary>Doc 10 §4.2 Shared Build: building takes from the Post Office's depot too, anywhere on the map.</summary>
        SharedBuild,
        /// <summary>Doc 10 §4.3 Forecast Desk: every Prep, the coming Delivery's mail by street (the Extended Forecast, free).</summary>
        ForecastDesk,
    }

    public static class RuleFlags
    {
        public static readonly int Count = Enum.GetValues(typeof(RuleFlag)).Length;

        private static readonly Dictionary<string, RuleFlag> ByName = new Dictionary<string, RuleFlag>(StringComparer.Ordinal)
        {
            ["insured_rebuild"] = RuleFlag.InsuredRebuild,
            ["presort"] = RuleFlag.Presort,
            ["priority_post"] = RuleFlag.PriorityPost,
            ["local_knowledge"] = RuleFlag.LocalKnowledge,
            ["shared_build"] = RuleFlag.SharedBuild,
            ["forecast_desk"] = RuleFlag.ForecastDesk,
        };

        /// <summary>The flag a content file names.</summary>
        public static bool TryParse(string name, out RuleFlag flag) => ByName.TryGetValue(name, out flag);

        /// <summary>The names content may write, for an error.</summary>
        public static string Known => string.Join(", ", ByName.Keys);

        /// <summary>A team flag is the whole team's; a personal perk cannot hold it.</summary>
        public static bool IsTeam(RuleFlag flag) => flag != RuleFlag.LocalKnowledge;
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
