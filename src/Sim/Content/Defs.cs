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
        /// <summary>The mail cannon fires this kind (doc 05 §1.2: a Small Package from the inventory).</summary>
        public bool CannonAmmo { get; init; }
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
        /// <summary>HP one unit gives back when it is used (doc 05 §1.1); 0 = not something to use.</summary>
        public int Heal { get; init; }
        /// <summary>Over how long it heals; 0 = at once.</summary>
        public float HealSeconds { get; init; }
        /// <summary>A tool (doc 08 §2.1): the resources it harvests while it is in the hand; empty for everything else.</summary>
        public IReadOnlyList<string> Harvests { get; init; } = Array.Empty<string>();
        /// <summary>A tool: what it makes of a node's yield.</summary>
        public float YieldMultiplier { get; init; } = 1f;
        /// <summary>A tool: seconds one swing of it takes.</summary>
        public float SwingSeconds { get; init; }

        /// <summary>Drawn in the hand while it is held (an Axe, a Pickaxe, a Shovel).</summary>
        public bool IsTool => Category == "tool";

        public bool HarvestsResource(string resource)
        {
            for (int i = 0; i < Harvests.Count; i++)
                if (Harvests[i] == resource) return true;
            return false;
        }
    }

    /// <summary>Whether a node wants a tool (doc 02 §3.9).</summary>
    public enum NodeTool : byte
    {
        /// <summary>Harvested by hand, whatever is in it.</summary>
        None = 0,
        /// <summary>By hand at <see cref="NodeDef.HandYield"/>, in full with a tool that harvests the resource.</summary>
        Optional = 1,
        /// <summary>Only with a tool that harvests the resource.</summary>
        Required = 2,
    }

    /// <summary>
    /// A resource node (doc 02 §3.9): which of the map's props it is, what a hit of it gives and
    /// how many it takes. The nodes are the trees, bushes and boulders the map is dressed with.
    /// </summary>
    public sealed class NodeDef
    {
        public string Id { get; init; } = "";
        public byte Index { get; init; }
        public string Name { get; init; } = "";
        /// <summary>What a carrier does to it, for the prompt: chop, pick, mine.</summary>
        public string Verb { get; init; } = "";
        /// <summary>The resource type a tool's "harvests" names.</summary>
        public string Resource { get; init; } = "";
        public World.PropKind Prop { get; init; }
        /// <summary>The variants of the prop that are this node; empty = all of them.</summary>
        public IReadOnlyList<int> Variants { get; init; } = Array.Empty<int>();
        public ItemDef Item { get; init; } = null!;
        /// <summary>Units per hit with the right tool, or by hand where no tool is asked for.</summary>
        public int Yield { get; init; }
        public int Hits { get; init; }
        /// <summary>Full again when the next shift begins; otherwise gone for the run.</summary>
        public bool Regrows { get; init; }
        public NodeTool Tool { get; init; }
        /// <summary>The share of the yield bare hands get where a tool is optional.</summary>
        public float HandYield { get; init; } = 1f;

        /// <summary>
        /// Units one hit gives a carrier with <paramref name="held"/> in the hand (null: nothing
        /// that matters); 0 where the node asks for a tool and that is not one.
        /// </summary>
        public int YieldWith(ItemDef? held)
        {
            if (held != null && held.HarvestsResource(Resource)) return Math.Max(1, (int)MathF.Round(Yield * held.YieldMultiplier, MidpointRounding.AwayFromZero));
            if (Tool == NodeTool.Required) return 0;
            return Tool == NodeTool.Optional ? Math.Max(1, (int)MathF.Round(Yield * HandYield, MidpointRounding.AwayFromZero)) : Yield;
        }

        /// <summary>Seconds one swing at it takes with <paramref name="held"/> in the hand.</summary>
        public double SwingSeconds(ItemDef? held, double handSeconds) =>
            held != null && held.HarvestsResource(Resource) && held.SwingSeconds > 0f ? held.SwingSeconds : handSeconds;
    }

    public enum ShopKind : byte { Blueprint = 0, Item = 1, Vehicle = 2 }

    /// <summary>
    /// Something a carrier rides (doc 04 §3.1, doc 10 §1.3): an arcade body with a top speed on
    /// the road and off it, and a cargo grid that rides with it.
    /// </summary>
    public sealed class VehicleDef
    {
        public string Id { get; init; } = "";
        public byte Index { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public ContainerDef Cargo { get; init; } = null!;
        public int Seats { get; init; } = 1;
        public float Hp { get; init; }
        /// <summary>Top speed on paved tiles (the street, the Post Office yard) and on everything else, m/s.</summary>
        public float RoadSpeed { get; init; }
        public float OffRoadSpeed { get; init; }
        /// <summary>m/s² of speed gained towards the top speed, lost when braking against the way it rolls, and lost rolling free.</summary>
        public float Acceleration { get; init; }
        public float Braking { get; init; }
        public float CoastDeceleration { get; init; }
        /// <summary>How fast the front wheel follows the rider's steer, radians a second.</summary>
        public float TurnRate { get; init; }
        /// <summary>The round body it bumps into things with.</summary>
        public float Radius { get; init; }
    }

    /// <summary>A stack a starting kit hands out: so many of an item.</summary>
    public readonly struct KitStack
    {
        public readonly ItemDef Item;
        public readonly int Count;
        public KitStack(ItemDef item, int count) { Item = item; Count = count; }
    }

    /// <summary>
    /// A starting kit (doc 03 §4.4, doc 01 §5.2): chosen per player in the Lobby from those their
    /// Postal Rank has opened; its personal items go into the player's pockets when the run
    /// starts (or when they join in Prep), its team items — the host's kit's — once a run into the
    /// chest by the Post Office, its vehicles by the counter.
    /// </summary>
    public sealed class KitDef
    {
        public string Id { get; init; } = "";
        public byte Index { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        /// <summary>The Postal Rank that opens it.</summary>
        public int Rank { get; init; } = 1;
        public IReadOnlyList<KitStack> Personal { get; init; } = Array.Empty<KitStack>();
        public IReadOnlyList<KitStack> Team { get; init; } = Array.Empty<KitStack>();
        public IReadOnlyList<VehicleDef> TeamVehicles { get; init; } = Array.Empty<VehicleDef>();
    }

    /// <summary>
    /// Something the Post Office shop sells (doc 08 §2.8, doc 10 §3): a blueprint the team owns
    /// from then on, or a stack of an item into the buyer's pockets.
    /// </summary>
    public sealed class ShopItemDef
    {
        public string Id { get; init; } = "";
        public byte Index { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public ShopKind Kind { get; init; }
        public int Price { get; init; }
        /// <summary>What an <see cref="ShopKind.Item"/> entry puts into the pockets, as one stack.</summary>
        public ItemDef? Item { get; init; }
        /// <summary>What a <see cref="ShopKind.Vehicle"/> entry parks by the counter.</summary>
        public VehicleDef? Vehicle { get; init; }
        public int Count { get; init; }
        public int FromShift { get; init; } = 1;
        /// <summary>Not in the fixed stock: on the counter only in a shift whose specials it is rolled into.</summary>
        public bool Rotating { get; init; }
        public bool OncePerRun { get; init; }
        /// <summary>Tagged "special": may be rolled into one of the shift's special slots.</summary>
        public bool Special { get; init; }
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    }

    /// <summary>What a construct does once it stands (doc 08 §2.6 "behaviour"); the ones that are built so far.</summary>
    public enum BuildingBehaviour : byte
    {
        /// <summary>Stands in the way of carriers and raiders alike.</summary>
        Wall = 0,
        /// <summary>A chest: a container anybody beside it may open.</summary>
        Container = 1,
        /// <summary>A wall the carriers walk through.</summary>
        Gate = 2,
        /// <summary>Lies flat on its tile (doc 05 §4 Spike Strip): it stops nobody, and hurts a raider standing on it.</summary>
        Spikes = 3,
        /// <summary>
        /// A post with a bell (doc 05 §4 Alarm Post): stands in the way like a chest; near the
        /// raiders' way in it rings the raid in earlier, and it marks the raiders near it.
        /// </summary>
        Alarm = 4,
        /// <summary>A conveyor belt (doc 04 §2.1): lies flat, carries mail one way at its speed.</summary>
        Belt = 5,
        /// <summary>Doc 04 §2.3: one input from behind, up to three outputs (front, left, right) in turn.</summary>
        Splitter = 6,
        /// <summary>Doc 04 §2.3: up to three inputs (back, left, right) in turn, one output in front.</summary>
        Merger = 7,
    }

    /// <summary>
    /// Something a carrier builds on a tile (doc 04 §1, doc 08 §2.6). One tile each so far; what
    /// it costs is its recipe's.
    /// </summary>
    public sealed class BuildingDef
    {
        public string Id { get; init; } = "";
        public byte Index { get; init; }
        public string Name { get; init; } = "";
        /// <summary>How many ways it can face: 1, 2 or 4.</summary>
        public int Rotations { get; init; } = 4;
        public int Hp { get; init; }
        public BuildingBehaviour Behaviour { get; init; }
        /// <summary>A <see cref="BuildingBehaviour.Container"/>: what it holds.</summary>
        public ContainerDef? Container { get; init; }
        /// <summary>The solid part: half a wall's thickness, a chest's round body.</summary>
        public float Radius { get; init; }
        /// <summary>What is lower than a jump is jumped onto.</summary>
        public float Height { get; init; }
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
        /// <summary>The one recipe that builds it.</summary>
        public RecipeDef Recipe { get; internal set; } = null!;

        /// <summary>Spikes: what a raider standing on the tile loses a second.</summary>
        public float DamagePerSecond { get; init; }
        /// <summary>An alarm: how much earlier the raid is announced while it stands in range of the raiders' way in.</summary>
        public float WarningBonusSeconds { get; init; }
        /// <summary>An alarm: how far round it the raid is in range, and the raiders are marked.</summary>
        public float RevealRadius { get; init; }
        /// <summary>A belt: metres a second (doc 11: Mk1 2, Mk2 4, Mk3 6).</summary>
        public float Speed { get; init; }
        /// <summary>Laid in a line by dragging (doc 04 §1.1: belts, pipes, walls).</summary>
        public bool Drags => Behaviour == BuildingBehaviour.Belt || Behaviour == BuildingBehaviour.Wall;

        /// <summary>Walls and gates stand in a line and join each other; a chest and spikes stand alone.</summary>
        public bool Joins => Behaviour == BuildingBehaviour.Wall || Behaviour == BuildingBehaviour.Gate;
        /// <summary>Whether it stands in anybody's way at all: spikes are walked over, and the raiders' paths go across them.</summary>
        public bool Solid => Behaviour != BuildingBehaviour.Spikes && !Carries;
        /// <summary>Mail rides through it (doc 04 §2): a belt, a splitter, a merger. Low enough to walk over.</summary>
        public bool Carries => Behaviour == BuildingBehaviour.Belt || Behaviour == BuildingBehaviour.Splitter || Behaviour == BuildingBehaviour.Merger;
    }

    /// <summary>One line of a recipe: so many of an item.</summary>
    public readonly struct RecipeInput
    {
        public readonly ItemDef Item;
        public readonly int Count;

        public RecipeInput(ItemDef item, int count) { Item = item; Count = count; }
    }

    /// <summary>
    /// What a construct costs (doc 08 §2.7): the materials placing it takes out of the builder's
    /// pockets, and what has to be so before it can be built at all.
    /// </summary>
    public sealed class RecipeDef
    {
        public string Id { get; init; } = "";
        public BuildingDef Building { get; init; } = null!;
        public IReadOnlyList<RecipeInput> Inputs { get; init; } = Array.Empty<RecipeInput>();
        /// <summary>The shop's blueprint the team has to own (doc 04 §1.3); null: known from the start.</summary>
        public ShopItemDef? Blueprint { get; init; }
        public int UnlockShift { get; init; } = 1;
    }

    /// <summary>One line of an enemy's drop table (doc 05 §2.2).</summary>
    public readonly struct EnemyDrop
    {
        public readonly ItemDef Item;
        /// <summary>0..1000.</summary>
        public readonly int ChancePermille;
        public readonly int Count;

        public EnemyDrop(ItemDef item, int chancePermille, int count) { Item = item; ChancePermille = chancePermille; Count = count; }
    }

    /// <summary>Enemy definition (doc 08 §2.10); the fields the ground melee enemies need.</summary>
    public sealed class EnemyDef
    {
        public string Id { get; init; } = "";
        public byte Index { get; init; }
        public string Name { get; init; } = "";
        public float Hp { get; init; }
        public float Damage { get; init; }
        /// <summary>Seconds between attacks.</summary>
        public float AttackRate { get; init; }
        /// <summary>Reach beyond the enemy's own radius, in metres.</summary>
        public float AttackRange { get; init; }
        public float Speed { get; init; }
        public float AggroRange { get; init; }
        public float Radius { get; init; }
        /// <summary>Size of the body the clients draw, relative to a carrier.</summary>
        public float Scale { get; init; }
        public int Cost { get; init; }
        public int UnlockShift { get; init; }
        public int CoinsMin { get; init; }
        public int CoinsMax { get; init; }
        public IReadOnlyList<EnemyDrop> Drops { get; init; } = Array.Empty<EnemyDrop>();
    }

    /// <summary>Raid schedule and budget (doc 08 §2.11, doc 05 §3).</summary>
    public sealed class WaveDef
    {
        /// <summary>
        /// The radius of an enemy's body in the sim: a Mega is drawn larger, but its body stays
        /// within what the props are indexed for. Server and client test a build tile against it.
        /// </summary>
        public float BodyRadius(EnemyDef def, bool mega) => MathF.Min(def.Radius * (mega ? MegaScale : 1f), World.WorldMap.WalkerReach);

        /// <summary>Per shift, 1-based; 0 = no raid that shift.</summary>
        public double[] BaseBudget { get; init; } = Array.Empty<double>();
        public double WaveScalePerExtraPlayer { get; init; }
        public double SoloMult { get; init; } = 1;
        public int SoloMaxPlayers { get; init; }
        public int Pulses { get; init; } = 1;
        public double PulseIntervalSeconds { get; init; }
        public double FinalePulseMult { get; init; } = 1;
        public double WarningSeconds { get; init; }
        /// <summary>The longest warning there is, perks and alarm posts together; 0 = no cap.</summary>
        public double MaxWarningSeconds { get; init; }
        public double RaidWindowSeconds { get; init; }
        public double MegaChanceBase { get; init; }
        public double MegaChancePerShift { get; init; }
        public float MegaHpMult { get; init; } = 1;
        public float MegaDamageMult { get; init; } = 1;
        public float MegaScale { get; init; } = 1;
        public float MegaSpeedMult { get; init; } = 1;
        public int MegaLootMult { get; init; } = 1;
        public int MaxAlive { get; init; }
        /// <summary>shift → (enemy, weight); a shift without an entry uses the last one before it.</summary>
        public IReadOnlyDictionary<int, IReadOnlyList<KeyValuePair<EnemyDef, int>>> WeightsByShift { get; init; }
            = new Dictionary<int, IReadOnlyList<KeyValuePair<EnemyDef, int>>>();

        public double BudgetFor(int shift) =>
            BaseBudget.Length == 0 ? 0 : BaseBudget[Math.Clamp(shift - 1, 0, BaseBudget.Length - 1)];

        public IReadOnlyList<KeyValuePair<EnemyDef, int>> WeightsFor(int shift)
        {
            for (int s = shift; s >= 1; s--)
                if (WeightsByShift.TryGetValue(s, out var w)) return w;
            return Array.Empty<KeyValuePair<EnemyDef, int>>();
        }
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
