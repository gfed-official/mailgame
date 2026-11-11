using System;
using System.Collections.Generic;

namespace PerformativeMail.Sim.Content
{
    public enum WeightClass : byte { Light = 0, Medium = 1, Heavy = 2, Bulk = 3 }

    /// <summary>Mail kind definition (doc 08 §2.2). Immutable after load.</summary>
    public sealed class MailKindDef
    {
        public string Id { get; init; } = "";
        public ushort Index { get; init; }
        public string Name { get; init; } = "";
        public int GridW { get; init; }
        public int GridH { get; init; }
        public int MaxStack { get; init; }
        public int BaseValue { get; init; }
        public WeightClass WeightClass { get; init; }
        public bool CarryByHand { get; init; }
        public int BeltLanes { get; init; }
        public float BeltLength { get; init; }
        public int DeadlineOffsetShifts { get; init; }
        public IReadOnlyList<string> AcceptedBy { get; init; } = Array.Empty<string>();
        public int ComplaintOnMisdelivery { get; init; }
        public int UnlockShift { get; init; }
    }

    /// <summary>Destination type definition (doc 08 §2.4).</summary>
    public sealed class DestinationTypeDef
    {
        public string Id { get; init; } = "";
        public byte Index { get; init; }
        public float InsertRate { get; init; }
        public float ManualInsertHold { get; init; }
        public int MaxAutomatedFeeders { get; init; }
        public bool RequiresUnit { get; init; }
        public bool RequiresVehicleZone { get; init; }
    }

    /// <summary>Container definition (doc 08 §2.5). grid is [cols, rows].</summary>
    public sealed class ContainerDef
    {
        public string Id { get; init; } = "";
        public ushort Index { get; init; }
        public int Cols { get; init; }
        public int Rows { get; init; }
        public string View { get; init; } = "grid";
        public string BeltAccess { get; init; } = "none";
        /// <summary>null = any category.</summary>
        public IReadOnlyList<string>? AllowedCategories { get; init; }
        /// <summary>True for containers on a player's body (hotbar, inventory, backpack): only hand-carryable items fit.</summary>
        public bool HandCarried { get; init; }
        /// <summary>Cells that never hold items, as (x, y). The hotbar reserves slot 1 for "hands" (doc 03 §3.3).</summary>
        public IReadOnlyList<(int X, int Y)> ReservedCells { get; init; } = Array.Empty<(int, int)>();

        public bool Allows(string category)
        {
            if (AllowedCategories == null) return true;
            for (int i = 0; i < AllowedCategories.Count; i++)
                if (AllowedCategories[i] == category) return true;
            return false;
        }
    }

    /// <summary>Non-mail item definition (doc 08 §2.1). M0 only needs the inventory-facing fields.</summary>
    public sealed class ItemDef
    {
        public string Id { get; init; } = "";
        public ushort Index { get; init; }
        public string Name { get; init; } = "";
        public string Category { get; init; } = "material";
        public int GridW { get; init; }
        public int GridH { get; init; }
        public int MaxStack { get; init; }
        public WeightClass WeightClass { get; init; }
        public int SellPrice { get; init; }
        public int BuyPrice { get; init; }
    }

    /// <summary>Mail mix and spawn tunables (doc 08 §2.3).</summary>
    public sealed class MailMixDef
    {
        /// <summary>shift (1-based) → (mail kind id, value share).</summary>
        public IReadOnlyDictionary<int, IReadOnlyList<KeyValuePair<string, double>>> Shares { get; init; }
            = new Dictionary<int, IReadOnlyList<KeyValuePair<string, double>>>();
        public double StreetStreakRatio { get; init; }
        public double BatchIntervalSeconds { get; init; }
        public double BatchJitterSeconds { get; init; }
        public double SpawnOverhead { get; init; }
        public double DistanceMultiplierPerDistrict { get; init; }
        public double ShiftMultiplierPerShift { get; init; }
        public double LateValueRatio { get; init; }
        public double MisdeliveryPenaltyRatio { get; init; }
    }
}
