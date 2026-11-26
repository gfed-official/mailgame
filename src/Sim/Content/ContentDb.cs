using System;
using System.Collections.Generic;
using System.Text.Json;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Content
{
    public sealed class ContentException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public ContentException(IReadOnlyList<string> errors)
            : base("Content validation failed:\n  " + string.Join("\n  ", errors))
        {
            Errors = errors;
        }
    }

    /// <summary>
    /// All loaded content as immutable defs (doc 07 §6). Loading validates against the chapter 08 rules;
    /// the same code path backs boot-time validation and tools/ContentValidator.
    /// </summary>
    public sealed partial class ContentDb
    {
        public IReadOnlyList<MailKindDef> MailKinds { get; private set; } = Array.Empty<MailKindDef>();
        public IReadOnlyList<DestinationTypeDef> DestinationTypes { get; private set; } = Array.Empty<DestinationTypeDef>();
        public IReadOnlyList<ContainerDef> Containers { get; private set; } = Array.Empty<ContainerDef>();
        public IReadOnlyList<ItemDef> Items { get; private set; } = Array.Empty<ItemDef>();
        public IReadOnlyList<NodeDef> Nodes { get; private set; } = Array.Empty<NodeDef>();
        public IReadOnlyList<EnemyDef> Enemies { get; private set; } = Array.Empty<EnemyDef>();
        public IReadOnlyList<PerkDef> Perks { get; private set; } = Array.Empty<PerkDef>();
        public IReadOnlyList<ShopItemDef> ShopItems { get; private set; } = Array.Empty<ShopItemDef>();
        /// <summary>What carriers ride (doc 10 §1.3).</summary>
        public IReadOnlyList<VehicleDef> Vehicles { get; private set; } = Array.Empty<VehicleDef>();
        /// <summary>The starting kits (doc 03 §4.4), the default first.</summary>
        public IReadOnlyList<KitDef> Kits { get; private set; } = Array.Empty<KitDef>();
        /// <summary>The kit a player has until they choose another: Land, open from rank 1.</summary>
        public KitDef DefaultKit => Kits[0];
        /// <summary>The Postage Stamps (doc 01 §6, doc 08 §2.12), at most <see cref="StampDef.MaxStamps"/>; a run's are a bit mask of their indices.</summary>
        public IReadOnlyList<StampDef> Stamps { get; private set; } = Array.Empty<StampDef>();
        /// <summary>What can be built, in the order of the build bar.</summary>
        public IReadOnlyList<BuildingDef> Buildings { get; private set; } = Array.Empty<BuildingDef>();
        public IReadOnlyList<RecipeDef> Recipes { get; private set; } = Array.Empty<RecipeDef>();
        /// <summary>The recipes by their key of Build mode: each group's recipes in their order (<see cref="RecipeDef.Group"/>).</summary>
        public const int MaxRecipeKeys = 12;
        public IReadOnlyList<IReadOnlyList<RecipeDef>> RecipeKeys { get; private set; } = Array.Empty<IReadOnlyList<RecipeDef>>();
        public WaveDef Waves { get; private set; } = new WaveDef();
        public MailMixDef MailMix { get; private set; } = new MailMixDef();
        public Balance Balance { get; private set; } = new Balance(new Dictionary<string, double[]>());
        public IReadOnlyList<string> StreetNames { get; private set; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, WorldMap> Maps => _maps;

        /// <summary>FNV-1a 32 over every loaded content file (path + normalised text).</summary>
        public uint ContentHash { get; private set; }

        private readonly Dictionary<string, MailKindDef> _mailKindsById = new Dictionary<string, MailKindDef>();
        private readonly Dictionary<string, DestinationTypeDef> _destTypesById = new Dictionary<string, DestinationTypeDef>();
        private readonly Dictionary<string, ContainerDef> _containersById = new Dictionary<string, ContainerDef>();
        private readonly Dictionary<string, ItemDef> _itemsById = new Dictionary<string, ItemDef>();
        private readonly Dictionary<string, WorldMap> _maps = new Dictionary<string, WorldMap>();
        private readonly Dictionary<string, EnemyDef> _enemiesById = new Dictionary<string, EnemyDef>();
        private readonly Dictionary<string, StampDef> _stampsById = new Dictionary<string, StampDef>();
        private readonly Dictionary<string, PerkDef> _perksById = new Dictionary<string, PerkDef>();
        private readonly Dictionary<string, VehicleDef> _vehiclesById = new Dictionary<string, VehicleDef>();
        private readonly Dictionary<string, KitDef> _kitsById = new Dictionary<string, KitDef>();
        private readonly Dictionary<string, ShopItemDef> _shopById = new Dictionary<string, ShopItemDef>();
        private readonly Dictionary<string, BuildingDef> _buildingsById = new Dictionary<string, BuildingDef>();
        private readonly Dictionary<(PropKind, int), NodeDef> _nodesByProp = new Dictionary<(PropKind, int), NodeDef>();

        private ContentDb() { }

        public MailKindDef MailKind(string id) => _mailKindsById[id];
        public MailKindDef? FindMailKind(string id) => _mailKindsById.TryGetValue(id, out var d) ? d : null;
        public DestinationTypeDef DestinationType(string id) => _destTypesById[id];
        public ContainerDef Container(string id) => _containersById[id];
        public ItemDef Item(string id) => _itemsById[id];
        public EnemyDef Enemy(string id) => _enemiesById[id];
        public PerkDef Perk(string id) => _perksById[id];
        public ShopItemDef ShopItem(string id) => _shopById[id];
        public VehicleDef Vehicle(string id) => _vehiclesById[id];
        public KitDef Kit(string id) => _kitsById[id];
        public StampDef Stamp(string id) => _stampsById[id];
        public ShopItemDef? FindShopItem(string id) => _shopById.TryGetValue(id, out var d) ? d : null;
        public BuildingDef Building(string id) => _buildingsById[id];
        public WorldMap Map(string id) => _maps[id];

        /// <summary>The resource node a prop of the map is (doc 02 §3.9); null for what is only scenery.</summary>
        public NodeDef? NodeFor(in Prop prop) => _nodesByProp.TryGetValue((prop.Kind, prop.Variant % Scenery.Variants), out var def) ? def : null;

        /// <summary>Loads and validates; throws <see cref="ContentException"/> listing every problem.</summary>
        public static ContentDb Load(IContentSource source)
        {
            var db = TryLoad(source, out var errors);
            if (errors.Count > 0) throw new ContentException(errors);
            return db;
        }

        /// <summary>Loads from the content/ directory found by walking up from <paramref name="startDir"/>.</summary>
        public static ContentDb LoadFromDirectoryTree(string startDir)
        {
            string? root = DirectoryContentSource.FindContentRoot(startDir);
            if (root == null) throw new ContentException(new[] { $"no content/ directory found above '{startDir}'" });
            return Load(new DirectoryContentSource(root));
        }

        public static ContentDb TryLoad(IContentSource source, out List<string> errors)
        {
            var errorList = new List<string>();
            errors = errorList;
            var db = new ContentDb();
            var hash = Fnv32.Create();

            string? Read(string path, bool required = true)
            {
                string? text = source.ReadText(path);
                if (text == null)
                {
                    if (required) errorList.Add($"{path}: file not found");
                    return null;
                }
                hash.Add(path);
                hash.Add(text.Replace("\r\n", "\n"));
                return text;
            }

            // --- destination types -------------------------------------------------------------
            {
                var ctx = new JsonCtx("mail/destinations.json", errors);
                var list = new List<DestinationTypeDef>();
                foreach (var e in ReadArray(ctx, Read(ctx.File)))
                {
                    var def = new DestinationTypeDef
                    {
                        Id = J.Str(ctx, e, "id"),
                        Index = (byte)list.Count,
                        InsertRate = (float)J.Num(ctx, e, "insertRate"),
                        ManualInsertHold = (float)J.Num(ctx, e, "manualInsertHold"),
                        MaxAutomatedFeeders = J.Int(ctx, e, "maxAutomatedFeeders", 1),
                        RequiresUnit = J.Bool(ctx, e, "requiresUnit", false),
                        RequiresVehicleZone = J.Bool(ctx, e, "requiresVehicleZone", false),
                    };
                    if (def.InsertRate <= 0) ctx.Error($"'{def.Id}': insertRate must be > 0");
                    if (def.ManualInsertHold < 0) ctx.Error($"'{def.Id}': manualInsertHold must be >= 0");
                    if (!AddUnique(ctx, db._destTypesById, def.Id, def)) continue;
                    list.Add(def);
                }
                db.DestinationTypes = list;
            }

            // --- mail kinds ----------------------------------------------------------------------
            {
                var ctx = new JsonCtx("mail/kinds.json", errors);
                var list = new List<MailKindDef>();
                foreach (var e in ReadArray(ctx, Read(ctx.File)))
                {
                    int[] grid = J.IntArray(ctx, e, "grid", 2);
                    var def = new MailKindDef
                    {
                        Id = J.Str(ctx, e, "id"),
                        Index = (ushort)list.Count,
                        Name = J.Str(ctx, e, "name"),
                        GridW = grid[0],
                        GridH = grid[1],
                        MaxStack = J.Int(ctx, e, "maxStack"),
                        BaseValue = J.Int(ctx, e, "baseValue"),
                        WeightClass = ParseWeight(ctx, J.Str(ctx, e, "weightClass")),
                        CarryByHand = J.Bool(ctx, e, "carryByHand", true),
                        BeltLanes = J.Int(ctx, e, "beltLanes", 1),
                        BeltLength = (float)J.Num(ctx, e, "beltLength", 0.5),
                        DeadlineOffsetShifts = J.Int(ctx, e, "deadlineOffsetShifts", 0),
                        AcceptedBy = J.StrArray(ctx, e, "acceptedBy"),
                        ComplaintOnMisdelivery = J.Int(ctx, e, "complaintOnMisdelivery", 0),
                        UnlockShift = J.Int(ctx, e, "unlockShift", 1),
                        CannonAmmo = J.Bool(ctx, e, "cannonAmmo", false),
                    };
                    if (def.GridW <= 0 || def.GridH <= 0) ctx.Error($"'{def.Id}': grid sizes must be positive");
                    if (def.MaxStack <= 0) ctx.Error($"'{def.Id}': maxStack must be positive");
                    if (def.BaseValue < 0 || def.BaseValue > ushort.MaxValue) ctx.Error($"'{def.Id}': baseValue out of range");
                    foreach (string dest in def.AcceptedBy)
                        if (!db._destTypesById.ContainsKey(dest))
                            ctx.Error($"'{def.Id}': acceptedBy references unknown destination type '{dest}'");
                    if (!AddUnique(ctx, db._mailKindsById, def.Id, def)) continue;
                    list.Add(def);
                }
                db.MailKinds = list;
            }

            // --- containers and items ------------------------------------------------------------
            {
                var ctx = new JsonCtx("items/containers.json", errors);
                var list = new List<ContainerDef>();
                foreach (var e in ReadArray(ctx, Read(ctx.File)))
                {
                    int[] grid = J.IntArray(ctx, e, "grid", 2);
                    var reserved = new List<(int X, int Y)>();
                    if (e.TryGetProperty("reservedCells", out var reservedEl) && reservedEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cell in reservedEl.EnumerateArray())
                        {
                            double[] xy = J.NumArrayOf(ctx, cell, "reservedCells[]", 2);
                            if (xy[0] < 0 || xy[1] < 0 || xy[0] >= grid[0] || xy[1] >= grid[1]) ctx.Error("reservedCells entry outside the grid");
                            else reserved.Add(((int)xy[0], (int)xy[1]));
                        }
                    }
                    var def = new ContainerDef
                    {
                        HandCarried = J.Bool(ctx, e, "handCarried", false),
                        ReservedCells = reserved,
                        Id = J.Str(ctx, e, "id"),
                        Index = (ushort)list.Count,
                        Cols = grid[0],
                        Rows = grid[1],
                        View = J.Str(ctx, e, "view", grid[0] * grid[1] > 160 ? "manifest" : "grid"),
                        BeltAccess = J.Str(ctx, e, "beltAccess", "none"),
                        AllowedCategories = J.Has(e, "allowedCategories") ? J.StrArray(ctx, e, "allowedCategories") : null,
                    };
                    if (def.Cols <= 0 || def.Rows <= 0 || def.Cols > 255 || def.Rows > 255)
                        ctx.Error($"'{def.Id}': grid sizes must be in 1..255");
                    if (!AddUnique(ctx, db._containersById, def.Id, def)) continue;
                    list.Add(def);
                }
                db.Containers = list;
            }
            {
                var list = new List<ItemDef>();
                var ammoOf = new Dictionary<ItemDef, (string Id, JsonCtx Ctx)>();
                foreach (string file in source.ListJson("items"))
                {
                    if (file == "items/containers.json") continue;
                    var ctx = new JsonCtx(file, errors);
                    foreach (var e in ReadArray(ctx, Read(file)))
                    {
                        int[] grid = J.IntArray(ctx, e, "grid", 2);
                        string id = J.Str(ctx, e, "id");
                        IReadOnlyList<string> harvests = Array.Empty<string>();
                        float yieldMultiplier = 1f, swingSeconds = 0f, repairs = 0f, repairCostRatio = 0f;
                        float shotDamage = 0f, shotCooldown = 0f, shotRange = 0f;
                        int shotsPerAmmo = 0;
                        string? ammoId = null;
                        bool toolBlock = false;
                        if (e.TryGetProperty("tool", out var tool))
                        {
                            if (tool.ValueKind != JsonValueKind.Object) ctx.Error($"'{id}': 'tool' must be an object");
                            else
                            {
                                toolBlock = true;
                                harvests = J.StrArray(ctx, tool, "harvests", optional: true);
                                yieldMultiplier = (float)J.Num(ctx, tool, "yieldMultiplier", 1);
                                swingSeconds = (float)J.Num(ctx, tool, "swingTime");
                                // Doc 05 §4 Repair Hammer: "Repairs 50 HP/s to a construct; consumes materials at 25% of build cost per full HP bar".
                                repairs = (float)J.Num(ctx, tool, "repairs", 0);
                                repairCostRatio = (float)J.Num(ctx, tool, "repairCostRatio", 0);
                                // Doc 05 §1.2 Slingshot: "12 | 0.8 s | 25 m | Shop 200 ¢; uses Stone as ammo (1 Stone = 5 shots)".
                                if (tool.TryGetProperty("shoots", out var shoots))
                                {
                                    if (shoots.ValueKind != JsonValueKind.Object) ctx.Error($"'{id}': tool.shoots must be an object");
                                    else
                                    {
                                        shotDamage = (float)J.Num(ctx, shoots, "damage");
                                        shotCooldown = (float)J.Num(ctx, shoots, "cooldown");
                                        shotRange = (float)J.Num(ctx, shoots, "range");
                                        ammoId = J.Str(ctx, shoots, "ammo");
                                        shotsPerAmmo = J.Int(ctx, shoots, "shotsPerAmmo");
                                        if (shotDamage <= 0f || shotCooldown <= 0f || shotRange <= 0f || shotsPerAmmo < 1 || shotsPerAmmo > 255)
                                            ctx.Error($"'{id}': tool.shoots needs damage, cooldown and range > 0 and shotsPerAmmo in 1..255");
                                    }
                                }
                                if (harvests.Count == 0 && repairs <= 0f && shotDamage <= 0f) ctx.Error($"'{id}': a tool harvests something, repairs or shoots");
                                if (yieldMultiplier <= 0f || swingSeconds <= 0f) ctx.Error($"'{id}': tool.yieldMultiplier and tool.swingTime must be > 0");
                                if (repairs < 0f || repairCostRatio < 0f || (repairs == 0f && repairCostRatio > 0f)) ctx.Error($"'{id}': tool.repairs and tool.repairCostRatio must not be negative, nor a cost without repairs");
                            }
                        }
                        var def = new ItemDef
                        {
                            Id = id,
                            Index = (ushort)list.Count,
                            Name = J.Str(ctx, e, "name"),
                            Category = J.Str(ctx, e, "category"),
                            GridW = grid[0],
                            GridH = grid[1],
                            MaxStack = J.Int(ctx, e, "maxStack"),
                            WeightClass = ParseWeight(ctx, J.Str(ctx, e, "weightClass")),
                            SellPrice = J.Int(ctx, e, "sellPrice", 0),
                            BuyPrice = J.Int(ctx, e, "buyPrice", 0),
                            Heal = J.Int(ctx, e, "heal", 0),
                            HealSeconds = (float)J.Num(ctx, e, "healSeconds", 0),
                            Harvests = harvests,
                            YieldMultiplier = yieldMultiplier,
                            SwingSeconds = swingSeconds,
                            RepairPerSecond = repairs,
                            RepairCostRatio = repairCostRatio,
                            ShotDamage = shotDamage,
                            ShotCooldown = shotCooldown,
                            ShotRange = shotRange,
                            ShotsPerAmmo = shotsPerAmmo,
                        };
                        if (ammoId != null) ammoOf[def] = (ammoId, ctx);
                        if (toolBlock && def.Category != "tool") ctx.Error($"'{def.Id}': only an item of category 'tool' has a 'tool' block");
                        if (def.GridW <= 0 || def.GridH <= 0) ctx.Error($"'{def.Id}': grid sizes must be positive");
                        if (def.MaxStack <= 0) ctx.Error($"'{def.Id}': maxStack must be positive");
                        if (def.SellPrice < 0 || def.BuyPrice < 0) ctx.Error($"'{def.Id}': prices must not be negative");
                        if (def.Heal < 0 || def.HealSeconds < 0) ctx.Error($"'{def.Id}': heal and healSeconds must not be negative");
                        if (def.Heal == 0 && def.HealSeconds > 0) ctx.Error($"'{def.Id}': healSeconds without heal");
                        if (!AddUnique(ctx, db._itemsById, def.Id, def)) continue;
                        list.Add(def);
                    }
                }
                foreach (var pair in ammoOf)
                {
                    if (db._itemsById.TryGetValue(pair.Value.Id, out var ammo) && ammo.Category != "tool") pair.Key.Ammo = ammo;
                    else pair.Value.Ctx.Error($"'{pair.Key.Id}': tool.shoots.ammo '{pair.Value.Id}' is no item it can be loaded with");
                }
                db.Items = list;
            }

            // --- resource nodes --------------------------------------------------------------------
            {
                var list = new List<NodeDef>();
                var byId = new Dictionary<string, NodeDef>();
                foreach (string file in source.ListJson("nodes"))
                {
                    var ctx = new JsonCtx(file, errors);
                    foreach (var e in ReadArray(ctx, Read(file)))
                    {
                        string id = J.Str(ctx, e, "id");
                        string itemId = J.Str(ctx, e, "item");
                        if (!db._itemsById.TryGetValue(itemId, out var item)) { ctx.Error($"'{id}': gives unknown item '{itemId}'"); continue; }
                        string propName = J.Str(ctx, e, "prop");
                        PropKind prop;
                        switch (propName)
                        {
                            case "tree": prop = PropKind.Tree; break;
                            case "bush": prop = PropKind.Bush; break;
                            case "boulder": prop = PropKind.Boulder; break;
                            case "sand_pile": prop = PropKind.SandPile; break;
                            default: ctx.Error($"'{id}': prop must be 'tree', 'bush', 'boulder' or 'sand_pile', not '{propName}'"); continue;
                        }
                        string toolName = J.Str(ctx, e, "tool", "none");
                        var tool = toolName == "required" ? NodeTool.Required : toolName == "optional" ? NodeTool.Optional : NodeTool.None;
                        if (tool == NodeTool.None && toolName != "none") ctx.Error($"'{id}': tool must be 'none', 'optional' or 'required', not '{toolName}'");
                        int[] variants = J.Has(e, "variants") ? J.IntArray(ctx, e, "variants") : Array.Empty<int>();
                        var def = new NodeDef
                        {
                            Id = id,
                            Index = (byte)list.Count,
                            Name = J.Str(ctx, e, "name"),
                            Verb = J.Str(ctx, e, "verb"),
                            Resource = J.Str(ctx, e, "resource"),
                            Prop = prop,
                            Variants = variants,
                            Item = item,
                            Yield = J.Int(ctx, e, "yield"),
                            Hits = J.Int(ctx, e, "hits"),
                            Regrows = J.Bool(ctx, e, "regrows", false),
                            Tool = tool,
                            HandYield = (float)J.Num(ctx, e, "handYield", 1),
                        };
                        // One hit is one stack at most, and the wire carries both in a byte.
                        if (def.Yield < 1 || def.Yield > Math.Min(item.MaxStack, byte.MaxValue)) ctx.Error($"'{id}': yield must be in 1..{Math.Min(item.MaxStack, byte.MaxValue)}");
                        if (def.Hits < 1 || def.Hits > byte.MaxValue) ctx.Error($"'{id}': hits must be in 1..{byte.MaxValue}");
                        if (def.HandYield <= 0f || def.HandYield > 1f) ctx.Error($"'{id}': handYield must be in (0, 1]");
                        if (J.Has(e, "handYield") && tool != NodeTool.Optional) ctx.Error($"'{id}': handYield is for a node whose tool is 'optional'");
                        foreach (int variant in variants)
                            if (variant < 0 || variant >= Scenery.Variants) ctx.Error($"'{id}': variants must be in 0..{Scenery.Variants - 1}");
                        if (tool != NodeTool.None)
                        {
                            bool harvested = false;
                            foreach (var candidate in db.Items) harvested |= candidate.HarvestsResource(def.Resource);
                            if (!harvested) ctx.Error($"'{id}': no tool harvests '{def.Resource}'");
                        }
                        for (int variant = 0; variant < Scenery.Variants; variant++)
                        {
                            if (variants.Length > 0 && Array.IndexOf(variants, variant) < 0) continue;
                            var key = (prop, variant);
                            if (db._nodesByProp.TryGetValue(key, out var other)) ctx.Error($"'{id}': {propName} variant {variant} is '{other.Id}' already");
                            else db._nodesByProp[key] = def;
                        }
                        if (!AddUnique(ctx, byId, def.Id, def)) continue;
                        list.Add(def);
                    }
                }
                db.Nodes = list;
                // A tool for a resource nothing gives is a card on the counter that does nothing.
                foreach (var item in db.Items)
                    foreach (string resource in item.Harvests)
                    {
                        bool given = false;
                        foreach (var node in list) given |= node.Resource == resource;
                        if (!given) errors.Add($"items: '{item.Id}' harvests '{resource}', which no node gives");
                    }
            }

            // --- mail mix ------------------------------------------------------------------------
            {
                var ctx = new JsonCtx("mail/mix.json", errors);
                string? text = Read(ctx.File);
                using var doc = text == null ? null : J.Parse(ctx, text);
                if (doc != null && doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var root = doc.RootElement;
                    var shares = new Dictionary<int, IReadOnlyList<KeyValuePair<string, double>>>();
                    if (root.TryGetProperty("shifts", out var shiftsEl) && shiftsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in shiftsEl.EnumerateArray())
                        {
                            int shift = J.Int(ctx, e, "shift");
                            var entries = new List<KeyValuePair<string, double>>();
                            double sum = 0;
                            if (e.TryGetProperty("shares", out var sharesEl) && sharesEl.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var p in sharesEl.EnumerateObject())
                                {
                                    if (!db._mailKindsById.ContainsKey(p.Name)) ctx.Error($"shift {shift}: unknown mail kind '{p.Name}'");
                                    if (p.Value.ValueKind != JsonValueKind.Number) { ctx.Error($"shift {shift}: share '{p.Name}' must be a number"); continue; }
                                    double share = p.Value.GetDouble();
                                    if (share < 0) ctx.Error($"shift {shift}: share '{p.Name}' is negative");
                                    entries.Add(new KeyValuePair<string, double>(p.Name, share));
                                    sum += share;
                                }
                            }
                            else ctx.Error($"shift {shift}: missing 'shares' object");
                            if (Math.Abs(sum - 1.0) > 0.001) ctx.Error($"shift {shift}: shares sum to {sum:0.####}, expected 1.0 ± 0.001");
                            if (shares.ContainsKey(shift)) ctx.Error($"duplicate shift {shift}");
                            shares[shift] = entries;
                        }
                    }
                    else ctx.Error("missing 'shifts' array");
                    if (!shares.ContainsKey(1)) ctx.Error("mix must define shift 1");

                    db.MailMix = new MailMixDef
                    {
                        Shares = shares,
                        StreetStreakRatio = J.Num(ctx, root, "streetStreakRatio"),
                        BatchIntervalSeconds = J.Num(ctx, root, "batchIntervalSeconds"),
                        BatchJitterSeconds = J.Num(ctx, root, "batchJitterSeconds"),
                        SpawnOverhead = J.Num(ctx, root, "spawnOverhead"),
                        DistanceMultiplierPerDistrict = J.Num(ctx, root, "distanceMultiplierPerDistrict"),
                        ShiftMultiplierPerShift = J.Num(ctx, root, "shiftMultiplierPerShift"),
                        LateValueRatio = J.Num(ctx, root, "lateValueRatio"),
                        MisdeliveryPenaltyRatio = J.Num(ctx, root, "misdeliveryPenaltyRatio"),
                    };
                    if (db.MailMix.BatchIntervalSeconds <= 0) ctx.Error("batchIntervalSeconds must be > 0");
                    if (db.MailMix.BatchJitterSeconds < 0 || db.MailMix.BatchJitterSeconds * 2 >= db.MailMix.BatchIntervalSeconds)
                        ctx.Error("batchJitterSeconds must be >= 0 and less than half the batch interval");
                }
                else if (doc != null) ctx.Error("root must be an object");
            }

            // --- balance -------------------------------------------------------------------------
            {
                var ctx = new JsonCtx("balance.json", errors);
                string? text = Read(ctx.File);
                var values = new Dictionary<string, double[]>();
                using var doc = text == null ? null : J.Parse(ctx, text);
                if (doc != null && doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        if (p.Value.ValueKind == JsonValueKind.Number) values[p.Name] = new[] { p.Value.GetDouble() };
                        else if (p.Value.ValueKind == JsonValueKind.Array) values[p.Name] = J.NumArrayOf(ctx, p.Value, p.Name);
                        else ctx.Error($"'{p.Name}' must be a number or an array of numbers");
                    }
                }
                else if (doc != null) ctx.Error("root must be an object");
                db.Balance = new Balance(values);
                foreach (string key in RequiredBalanceKeys)
                    if (!values.ContainsKey(key)) ctx.Error($"missing required key '{key}'");
            }

            // --- enemies and waves ---------------------------------------------------------------
            {
                var list = new List<EnemyDef>();
                foreach (string file in source.ListJson("enemies"))
                {
                    var ctx = new JsonCtx(file, errors);
                    foreach (var e in ReadArray(ctx, Read(file)))
                    {
                        int[] coins = J.Has(e, "coins") ? J.IntArray(ctx, e, "coins", 2) : new int[2];
                        var drops = new List<EnemyDrop>();
                        string id = J.Str(ctx, e, "id");
                        if (e.TryGetProperty("drops", out var dropsEl) && dropsEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var d in dropsEl.EnumerateArray())
                            {
                                if (d.ValueKind != JsonValueKind.Object) { ctx.Error($"'{id}': drops must be objects"); continue; }
                                string item = J.Str(ctx, d, "item");
                                double chance = J.Num(ctx, d, "chance");
                                int count = J.Int(ctx, d, "count", 1);
                                if (!db._itemsById.TryGetValue(item, out var itemDef)) { ctx.Error($"'{id}': drop references unknown item '{item}'"); continue; }
                                if (chance < 0 || chance > 1) ctx.Error($"'{id}': drop chance of '{item}' must be in 0..1");
                                if (count < 1 || count > 255) ctx.Error($"'{id}': drop count of '{item}' must be in 1..255");
                                drops.Add(new EnemyDrop(itemDef, (int)Math.Round(chance * 1000), count));
                            }
                        }
                        var traits = EnemyTraits.None;
                        foreach (string t in J.StrArray(ctx, e, "traits", optional: true))
                        {
                            int bit = Array.IndexOf(EnemyDef.TraitNames, t);
                            if (bit < 0) ctx.Error($"'{id}': unknown trait '{t}' (the set is {string.Join(", ", EnemyDef.TraitNames)})");
                            else traits |= (EnemyTraits)(1 << bit);
                        }
                        var priority = new List<EnemyTarget>();
                        foreach (string t in J.StrArray(ctx, e, "targetPriority", optional: true))
                        {
                            int k = Array.IndexOf(EnemyDef.TargetNames, t);
                            if (k < 0) ctx.Error($"'{id}': unknown target '{t}' in targetPriority (the set is {string.Join(", ", EnemyDef.TargetNames)})");
                            else if (priority.Contains((EnemyTarget)k)) ctx.Error($"'{id}': '{t}' is twice in targetPriority");
                            else priority.Add((EnemyTarget)k);
                        }
                        if (priority.Count == 0) priority.Add(EnemyTarget.Player);
                        float keepDistance = 0f, blastRadius = 0f, structureDamage = 0f, provokeRange = 0f, provokeSeconds = 0f, altitude = 0f, bombRadius = 0f, pickaxeMult = 1f;
                        if (e.TryGetProperty("traitParams", out var traitParams))
                        {
                            if (traitParams.ValueKind != JsonValueKind.Object) ctx.Error($"'{id}': traitParams must be an object");
                            else
                            {
                                foreach (var param in traitParams.EnumerateObject())
                                    if (Array.IndexOf(TraitParamNames, param.Name) < 0) ctx.Error($"'{id}': unknown trait param '{param.Name}'");
                                keepDistance = (float)J.Num(ctx, traitParams, "keepDistance", 0);
                                blastRadius = (float)J.Num(ctx, traitParams, "blastRadius", 0);
                                structureDamage = (float)J.Num(ctx, traitParams, "structureDamage", 0);
                                provokeRange = (float)J.Num(ctx, traitParams, "provokeRange", 0);
                                provokeSeconds = (float)J.Num(ctx, traitParams, "provokeSeconds", 0);
                                altitude = (float)J.Num(ctx, traitParams, "altitude", 0);
                                bombRadius = (float)J.Num(ctx, traitParams, "bombRadius", 0);
                                pickaxeMult = (float)J.Num(ctx, traitParams, "pickaxeMult", 1);
                            }
                        }
                        var def = new EnemyDef
                        {
                            Traits = traits,
                            TargetPriority = priority,
                            KeepDistance = keepDistance,
                            BlastRadius = blastRadius,
                            StructureDamage = structureDamage,
                            ProvokeRange = provokeRange,
                            ProvokeSeconds = provokeSeconds,
                            Altitude = altitude,
                            BombRadius = bombRadius,
                            PickaxeMult = pickaxeMult,
                            Id = id,
                            Index = (byte)list.Count,
                            Name = J.Str(ctx, e, "name"),
                            Hp = (float)J.Num(ctx, e, "hp"),
                            Damage = (float)J.Num(ctx, e, "damage"),
                            AttackRate = (float)J.Num(ctx, e, "attackRate"),
                            AttackRange = (float)J.Num(ctx, e, "attackRange"),
                            Speed = (float)J.Num(ctx, e, "speed"),
                            AggroRange = (float)J.Num(ctx, e, "aggroRange", 15),
                            Radius = (float)J.Num(ctx, e, "radius", 0.4),
                            Scale = (float)J.Num(ctx, e, "scale", 1),
                            Cost = J.Int(ctx, e, "cost"),
                            UnlockShift = J.Int(ctx, e, "unlockShift", 1),
                            CoinsMin = coins[0],
                            CoinsMax = coins[1],
                            Drops = drops,
                        };
                        if (def.Hp <= 0) ctx.Error($"'{def.Id}': hp must be > 0");
                        if (def.Damage < 0) ctx.Error($"'{def.Id}': damage must be >= 0");
                        if (def.AttackRate <= 0) ctx.Error($"'{def.Id}': attackRate must be > 0");
                        if (def.AttackRange <= 0) ctx.Error($"'{def.Id}': attackRange must be > 0");
                        if (def.Speed <= 0) ctx.Error($"'{def.Id}': speed must be > 0");
                        if (def.Radius <= 0 || def.Radius > WorldMap.WalkerReach)
                            ctx.Error($"'{def.Id}': radius must be in (0, {WorldMap.WalkerReach}] (the widest body the props are indexed for)");
                        if (def.Scale <= 0) ctx.Error($"'{def.Id}': scale must be > 0");
                        if (def.Cost <= 0) ctx.Error($"'{def.Id}': cost must be > 0");
                        // Doc 05 §2 Archer: "Ranged 15 m. Keeps 10 m distance".
                        if (def.Ranged && (def.KeepDistance <= 0 || def.KeepDistance >= def.AttackRange))
                            ctx.Error($"'{def.Id}': a ranged enemy needs traitParams.keepDistance in (0, attackRange)");
                        if (!def.Ranged && def.KeepDistance != 0) ctx.Error($"'{def.Id}': keepDistance is for a ranged enemy");
                        // Doc 05 §2 Wall Breaker: "150 to walls/gates/belts, 20 otherwise; explodes (r = 1.5 m)".
                        if (def.Suicide != (def.BlastRadius > 0f) || def.Suicide != (def.StructureDamage > 0f))
                            ctx.Error($"'{def.Id}': a suicide enemy, and only one, has traitParams.blastRadius and traitParams.structureDamage > 0");
                        // Doc 05 §2 Giant: "Ignores players unless attacked by one within 5 m for 3 s".
                        if (def.Siege != (def.ProvokeRange > 0f) || def.Siege != (def.ProvokeSeconds > 0f))
                            ctx.Error($"'{def.Id}': a siege enemy, and only one, has traitParams.provokeRange and traitParams.provokeSeconds > 0");
                        // Doc 05 §2 Balloon: "30 AoE r=2 m … Airborne (6 m)".
                        if (def.Airborne != (def.Altitude > 0f) || def.Airborne != (def.BombRadius > 0f))
                            ctx.Error($"'{def.Id}': an airborne enemy, and only one, has traitParams.altitude and traitParams.bombRadius > 0");
                        // Only those that walk up and strike go for the Post Office.
                        if (priority.Contains(EnemyTarget.PostOffice) && (def.Ranged || def.Suicide || def.Airborne))
                            ctx.Error($"'{def.Id}': a ranged, suicide or airborne enemy cannot have 'po' in its targetPriority");
                        if (def.PickaxeMult < 1f) ctx.Error($"'{def.Id}': traitParams.pickaxeMult must be at least 1");
                        if (def.AggroRange < def.AttackRange) ctx.Error($"'{def.Id}': aggroRange must be at least its attackRange");
                        if (def.CoinsMin < 0 || def.CoinsMax < def.CoinsMin) ctx.Error($"'{def.Id}': coins must be [min, max] with 0 <= min <= max");
                        // The snapshot packs the kind into four bits.
                        if (list.Count >= 16) { ctx.Error($"'{def.Id}': more than 16 enemy kinds"); continue; }
                        if (!AddUnique(ctx, db._enemiesById, def.Id, def)) continue;
                        list.Add(def);
                    }
                }
                db.Enemies = list;
            }
            {
                var ctx = new JsonCtx("waves/waves.json", errors);
                string? text = Read(ctx.File, required: db.Enemies.Count > 0);
                using var doc = text == null ? null : J.Parse(ctx, text);
                if (doc != null && doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var root = doc.RootElement;
                    var weights = new Dictionary<int, IReadOnlyList<KeyValuePair<EnemyDef, int>>>();
                    var maxes = new Dictionary<int, IReadOnlyDictionary<EnemyDef, int>>();
                    var leadsByShift = new Dictionary<int, IReadOnlyList<KeyValuePair<EnemyDef, int>>>();
                    if (root.TryGetProperty("weightsByShift", out var byShift) && byShift.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var s in byShift.EnumerateObject())
                        {
                            if (!int.TryParse(s.Name, out int shift) || shift < 1) { ctx.Error($"weightsByShift: '{s.Name}' is not a shift number"); continue; }
                            var entries = new List<KeyValuePair<EnemyDef, int>>();
                            var caps = new Dictionary<EnemyDef, int>();
                            var leads = new List<KeyValuePair<EnemyDef, int>>();
                            if (s.Value.ValueKind != JsonValueKind.Object) { ctx.Error($"weightsByShift {shift}: must be an object of enemy weights"); continue; }
                            foreach (var p in s.Value.EnumerateObject())
                            {
                                if (!db._enemiesById.TryGetValue(p.Name, out var enemy)) { ctx.Error($"weightsByShift {shift}: unknown enemy '{p.Name}'"); continue; }
                                // A weight, or { "weight": w, "max": m, "lead": l } for an enemy a raid sends at most m of
                                // (doc 05 §3.2 Tank) and buys l of before its pulses roll (design choice 141).
                                var w = p.Value;
                                if (w.ValueKind == JsonValueKind.Object)
                                {
                                    int max = int.MaxValue;
                                    if (w.TryGetProperty("max", out _))
                                    {
                                        max = J.Int(ctx, w, "max");
                                        if (max < 1) ctx.Error($"weightsByShift {shift}: max of '{p.Name}' must be at least 1");
                                        else caps[enemy] = max;
                                    }
                                    if (w.TryGetProperty("lead", out _))
                                    {
                                        int lead = J.Int(ctx, w, "lead");
                                        if (lead < 1 || lead > max) ctx.Error($"weightsByShift {shift}: lead of '{p.Name}' must be at least 1 and at most its max");
                                        else leads.Add(new KeyValuePair<EnemyDef, int>(enemy, lead));
                                    }
                                    w = w.TryGetProperty("weight", out var inner) ? inner : default;
                                }
                                if (w.ValueKind != JsonValueKind.Number || !w.TryGetInt32(out int weight) || weight < 0)
                                {
                                    ctx.Error($"weightsByShift {shift}: weight of '{p.Name}' must be a non-negative integer");
                                    continue;
                                }
                                // Doc 08 §5: a wave may only use enemies that are unlocked by then.
                                if (weight > 0 && enemy.UnlockShift > shift)
                                    ctx.Error($"weightsByShift {shift}: '{p.Name}' unlocks on shift {enemy.UnlockShift}");
                                if (weight > 0) entries.Add(new KeyValuePair<EnemyDef, int>(enemy, weight));
                            }
                            weights[shift] = entries;
                            if (caps.Count > 0) maxes[shift] = caps;
                            // Dearest first: the Tank leads before the Giants.
                            leads.Sort((a, b) => b.Key.Cost.CompareTo(a.Key.Cost));
                            leadsByShift[shift] = leads;
                        }
                    }
                    else ctx.Error("missing 'weightsByShift' object");

                    float megaHp = 1, megaDmg = 1, megaScale = 1, megaSpeed = 1;
                    int megaLoot = 1;
                    MailKindDef? parcelKind = null;
                    double parcelValueMult = 1;
                    var megaItems = new List<EnemyDrop>();
                    if (root.TryGetProperty("mega", out var mega) && mega.ValueKind == JsonValueKind.Object)
                    {
                        megaHp = (float)J.Num(ctx, mega, "hpMult");
                        megaDmg = (float)J.Num(ctx, mega, "dmgMult");
                        megaScale = (float)J.Num(ctx, mega, "scale");
                        megaSpeed = (float)J.Num(ctx, mega, "speedMult");
                        megaLoot = J.Int(ctx, mega, "lootMult", 1);
                        if (megaHp <= 0 || megaDmg <= 0 || megaScale <= 0 || megaSpeed <= 0 || megaLoot < 1) ctx.Error("mega multipliers must be positive");
                        // Doc 05 §2.1 "Guaranteed drop on death: a Lost Parcel (Medium Package with a random
                        // unlocked address, value ×3) plus 2 Iron Ingots".
                        if (mega.TryGetProperty("lostParcel", out var parcel) && parcel.ValueKind == JsonValueKind.Object)
                        {
                            string kindId = J.Str(ctx, parcel, "kind");
                            parcelKind = db.FindMailKind(kindId);
                            if (parcelKind == null) ctx.Error($"mega.lostParcel: unknown mail kind '{kindId}'");
                            parcelValueMult = J.Num(ctx, parcel, "valueMult", 1);
                            if (parcelValueMult <= 0) ctx.Error("mega.lostParcel.valueMult must be positive");
                            if (parcel.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
                                foreach (var it in items.EnumerateObject())
                                {
                                    if (!db._itemsById.TryGetValue(it.Name, out var itemDef)) { ctx.Error($"mega.lostParcel: unknown item '{it.Name}'"); continue; }
                                    if (it.Value.ValueKind != JsonValueKind.Number || !it.Value.TryGetInt32(out int count) || count < 1 || count > 255)
                                    {
                                        ctx.Error($"mega.lostParcel: the count of '{it.Name}' must be in 1..255");
                                        continue;
                                    }
                                    megaItems.Add(new EnemyDrop(itemDef, 1000, count));
                                }
                        }
                    }
                    else ctx.Error("missing 'mega' object");

                    var def = new WaveDef
                    {
                        BaseBudget = J.NumArray(ctx, root, "baseBudget"),
                        WaveScalePerExtraPlayer = J.Num(ctx, root, "waveScalePerExtraPlayer"),
                        SoloMult = J.Num(ctx, root, "soloMult", 1),
                        SoloMaxPlayers = J.Int(ctx, root, "soloMaxPlayers", 0),
                        Pulses = J.Int(ctx, root, "pulses"),
                        PulseIntervalSeconds = J.Num(ctx, root, "pulseIntervalSeconds"),
                        FinalePulseMult = J.Num(ctx, root, "finalePulseMult", 1),
                        LeadBudgetRatio = J.Num(ctx, root, "leadBudgetRatio", 0),
                        WarningSeconds = J.Num(ctx, root, "warningSeconds"),
                        MaxWarningSeconds = J.Num(ctx, root, "maxWarningSeconds", 0),
                        RaidWindowSeconds = J.Num(ctx, root, "raidWindowSeconds"),
                        MegaChanceBase = J.Num(ctx, root, "megaChanceBase", 0),
                        MegaChancePerShift = J.Num(ctx, root, "megaChancePerShift", 0),
                        MegaHpMult = megaHp, MegaDamageMult = megaDmg, MegaScale = megaScale, MegaSpeedMult = megaSpeed, MegaLootMult = megaLoot,
                        LostParcelKind = parcelKind, LostParcelValueMult = parcelValueMult, MegaItems = megaItems,
                        MaxAlive = J.Int(ctx, root, "maxAlive"),
                        WeightsByShift = weights,
                        MaxByShift = maxes,
                        LeadsByShift = leadsByShift,
                    };
                    if (def.Pulses < 1) ctx.Error("pulses must be >= 1");
                    if (def.PulseIntervalSeconds <= 0) ctx.Error("pulseIntervalSeconds must be > 0");
                    if (def.LeadBudgetRatio != 0 && def.LeadBudgetRatio < 1) ctx.Error("leadBudgetRatio must be 0 (off) or at least 1");
                    if (def.WarningSeconds < 0 || def.RaidWindowSeconds <= 0) ctx.Error("warningSeconds must be >= 0 and raidWindowSeconds > 0");
                    if (def.MaxWarningSeconds != 0 && def.MaxWarningSeconds < def.WarningSeconds) ctx.Error("maxWarningSeconds must be 0 (no cap) or at least warningSeconds");
                    // The snapshot counts enemies in a byte.
                    if (def.MaxAlive < 1 || def.MaxAlive > 255) ctx.Error("maxAlive must be in 1..255");
                    foreach (double budget in def.BaseBudget) if (budget < 0) ctx.Error("baseBudget must not be negative");
                    for (int shift = 1; shift <= def.BaseBudget.Length; shift++)
                        if (def.BaseBudget[shift - 1] > 0 && def.WeightsFor(shift).Count == 0)
                            ctx.Error($"shift {shift} has a raid budget but no enemy weights");
                    db.Waves = def;
                }
                else if (doc != null) ctx.Error("root must be an object");
            }

            // --- perks ---------------------------------------------------------------------------
            {
                var list = new List<PerkDef>();
                var files = new List<string>();
                foreach (string file in source.ListJson("perks"))
                {
                    var ctx = new JsonCtx(file, errors);
                    foreach (var e in ReadArray(ctx, Read(file)))
                    {
                        string id = J.Str(ctx, e, "id");
                        string scope = J.Str(ctx, e, "scope");
                        bool team = scope == "team";
                        if (!team && scope != "personal") ctx.Error($"'{id}': unknown scope '{scope}'");

                        var modifiers = new List<StatModifier>();
                        if (e.TryGetProperty("modifiers", out var modsEl) && modsEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var m in modsEl.EnumerateArray())
                            {
                                if (m.ValueKind != JsonValueKind.Object) { ctx.Error($"'{id}': modifiers must be objects"); continue; }
                                string statName = J.Str(ctx, m, "stat");
                                string op = J.Str(ctx, m, "op");
                                double value = J.Num(ctx, m, "value");
                                if (!Stats.TryParse(statName, out var stat)) { ctx.Error($"'{id}': unknown stat '{statName}'"); continue; }
                                if (op != "mul" && op != "add") { ctx.Error($"'{id}': modifier op must be 'mul' or 'add', not '{op}'"); continue; }
                                if (!team && Stats.IsTeam(stat)) ctx.Error($"'{id}': a personal perk cannot change the team stat {stat}");
                                if (op == "mul" && value < 0) ctx.Error($"'{id}': the multiplier on {stat} must not be negative");
                                modifiers.Add(new StatModifier(stat, op == "mul" ? ModifierOp.Mul : ModifierOp.Add, (float)value));
                            }
                        }
                        else ctx.Error($"'{id}': missing 'modifiers' array");
                        if (modifiers.Count == 0) ctx.Error($"'{id}': a perk without a modifier does nothing");

                        // Doc 08 §2.9 names three more closed sets. Nothing is in them yet (no rule flag is
                        // implemented, nothing can be crafted or built), so whatever a perk names from them is unknown.
                        foreach (string rule in J.StrArray(ctx, e, "rules", optional: true)) ctx.Error($"'{id}': unknown rule flag '{rule}'");
                        if (e.TryGetProperty("unlocks", out var unlocksEl))
                        {
                            if (unlocksEl.ValueKind != JsonValueKind.Array) ctx.Error($"'{id}': 'unlocks' must be an array");
                            else
                                foreach (var u in unlocksEl.EnumerateArray())
                                    ctx.Error($"'{id}': unlocks references unknown recipe '{(u.ValueKind == JsonValueKind.Object ? J.Str(ctx, u, "recipe") : u.ToString())}'");
                        }
                        int shiftMin = 1, rankMin = 1;
                        if (e.TryGetProperty("prerequisites", out var preEl))
                        {
                            if (preEl.ValueKind != JsonValueKind.Object) ctx.Error($"'{id}': 'prerequisites' must be an object");
                            else
                            {
                                shiftMin = J.Int(ctx, preEl, "shiftMin", 1);
                                rankMin = J.Int(ctx, preEl, "rankMin", 1);
                                foreach (string built in J.StrArray(ctx, preEl, "builtAny", optional: true))
                                    ctx.Error($"'{id}': prerequisite builtAny references unknown building '{built}'");
                            }
                        }

                        var def = new PerkDef
                        {
                            Id = id,
                            Index = (byte)list.Count,
                            Name = J.Str(ctx, e, "name"),
                            Description = J.Str(ctx, e, "description"),
                            Category = ParsePerkCategory(ctx, id, J.Str(ctx, e, "category")),
                            Team = team,
                            Rarity = ParsePerkRarity(ctx, id, J.Str(ctx, e, "rarity")),
                            Modifiers = modifiers,
                            ShiftMin = shiftMin,
                            RankMin = rankMin,
                            Excludes = J.StrArray(ctx, e, "excludes", optional: true),
                            MaxStacks = J.Int(ctx, e, "maxStacks", 1),
                            Tags = J.StrArray(ctx, e, "tags", optional: true),
                        };
                        if (def.ShiftMin < 1 || def.RankMin < 1) ctx.Error($"'{id}': prerequisites shiftMin and rankMin must be >= 1");
                        if (def.MaxStacks < 1) ctx.Error($"'{id}': maxStacks must be >= 1");
                        // Doc 01 §4.2: team perks are unique per run.
                        if (team && def.MaxStacks > 1) ctx.Error($"'{id}': a team perk is held once, maxStacks must be 1");
                        // The wire names a perk in one byte, and 255 is "none".
                        if (list.Count >= byte.MaxValue) { ctx.Error($"'{id}': more than {byte.MaxValue} perks"); continue; }
                        if (!AddUnique(ctx, db._perksById, def.Id, def)) continue;
                        list.Add(def);
                        files.Add(file);
                    }
                }
                for (int i = 0; i < list.Count; i++)
                {
                    foreach (string other in list[i].Excludes)
                    {
                        if (other == list[i].Id) errors.Add($"{files[i]}: '{list[i].Id}': excludes itself");
                        else if (!db._perksById.ContainsKey(other)) errors.Add($"{files[i]}: '{list[i].Id}': excludes references unknown perk '{other}'");
                    }
                }
                db.Perks = list;
            }

            // --- vehicles --------------------------------------------------------------------------
            {
                var list = new List<VehicleDef>();
                foreach (string file in source.ListJson("vehicles"))
                {
                    var ctx = new JsonCtx(file, errors);
                    foreach (var e in ReadArray(ctx, Read(file)))
                    {
                        string id = J.Str(ctx, e, "id");
                        string cargoId = J.Str(ctx, e, "cargo");
                        if (!db._containersById.TryGetValue(cargoId, out var cargo)) { ctx.Error($"'{id}': unknown cargo container '{cargoId}'"); continue; }
                        var def = new VehicleDef
                        {
                            Id = id,
                            Index = (byte)list.Count,
                            Name = J.Str(ctx, e, "name"),
                            Description = J.Str(ctx, e, "description", ""),
                            Cargo = cargo,
                            Seats = J.Int(ctx, e, "seats", 1),
                            Hp = (float)J.Num(ctx, e, "hp"),
                            RoadSpeed = (float)J.Num(ctx, e, "roadSpeed", 0),
                            OffRoadSpeed = (float)J.Num(ctx, e, "offRoadSpeed", 0),
                            WaterSpeed = (float)J.Num(ctx, e, "waterSpeed", 0),
                            Acceleration = (float)J.Num(ctx, e, "acceleration"),
                            Braking = (float)J.Num(ctx, e, "braking"),
                            CoastDeceleration = (float)J.Num(ctx, e, "coastDeceleration"),
                            TurnRate = (float)(J.Num(ctx, e, "turnRateDegrees") * Math.PI / 180.0),
                            Radius = (float)J.Num(ctx, e, "radius"),
                            Length = (float)J.Num(ctx, e, "length", 2.0 * J.Num(ctx, e, "radius")),
                            FuelSecondsPerCan = (float)J.Num(ctx, e, "fuelSecondsPerCan", 0),
                            TankSeconds = (float)J.Num(ctx, e, "tankSeconds", 0),
                            LoadingFace = J.Bool(ctx, e, "loadingFace", false),
                        };
                        // The rider is the driver (doc 04 §3.1); a second seat waits for passengers and NPC drivers.
                        if (def.Seats != 1 && def.Seats != 2) ctx.Error($"'{id}': seats must be 1 or 2");
                        if (def.Length < 2f * def.Radius - 1e-4f || def.Length > 6f) ctx.Error($"'{id}': length must be in [2 x radius, 6]");
                        if (def.FuelSecondsPerCan < 0f || (def.FuelSecondsPerCan > 0f ? def.TankSeconds < def.FuelSecondsPerCan : def.TankSeconds != 0f))
                            ctx.Error($"'{id}': a vehicle that burns fuel has fuelSecondsPerCan > 0 and tankSeconds >= it; one that does not, neither");
                        if (def.Hp <= 0f) ctx.Error($"'{id}': hp must be > 0");
                        if (def.OnWater ? def.RoadSpeed != 0f || def.OffRoadSpeed != 0f : def.OffRoadSpeed <= 0f || def.RoadSpeed < def.OffRoadSpeed || def.WaterSpeed < 0f)
                            ctx.Error($"'{id}': speeds must be 0 < offRoadSpeed <= roadSpeed, or a boat's waterSpeed > 0 alone");
                        if (def.Acceleration <= 0f || def.Braking <= 0f || def.CoastDeceleration <= 0f) ctx.Error($"'{id}': acceleration, braking and coastDeceleration must be > 0");
                        if (def.TurnRate <= 0f) ctx.Error($"'{id}': turnRateDegrees must be > 0");
                        if (def.Radius <= 0f || def.Radius > 0.5f) ctx.Error($"'{id}': radius must be in (0, 0.5]");
                        if (cargo.HandCarried) ctx.Error($"'{id}': its cargo '{cargoId}' is a hand-carried container");
                        // The player's movement state names the ridden vehicle in a byte, 0 for none.
                        if (list.Count >= byte.MaxValue - 1) { ctx.Error($"'{id}': too many vehicles"); continue; }
                        if (!AddUnique(ctx, db._vehiclesById, def.Id, def)) continue;
                        list.Add(def);
                    }
                }
                db.Vehicles = list;
            }

            var kitBlueprints = new List<(KitDef Kit, string Blueprint, JsonCtx Ctx)>();
            // --- kits ----------------------------------------------------------------------------
            {
                var list = new List<KitDef>();
                foreach (string file in source.ListJson("kits"))
                {
                    var ctx = new JsonCtx(file, errors);
                    foreach (var e in ReadArray(ctx, Read(file)))
                    {
                        string id = J.Str(ctx, e, "id");
                        List<KitStack> Stacks(string key)
                        {
                            var stacks = new List<KitStack>();
                            if (!e.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
                            {
                                ctx.Error($"'{id}': missing '{key}' array");
                                return stacks;
                            }
                            foreach (var s in arr.EnumerateArray())
                            {
                                string itemId = J.Str(ctx, s, "item");
                                int count = J.Int(ctx, s, "count");
                                if (!db._itemsById.TryGetValue(itemId, out var item)) { ctx.Error($"'{id}': {key} names unknown item '{itemId}'"); continue; }
                                if (count < 1 || count > item.MaxStack) { ctx.Error($"'{id}': {key} '{itemId}' count must be 1..{item.MaxStack} (one stack)"); continue; }
                                stacks.Add(new KitStack(item, count));
                            }
                            return stacks;
                        }
                        var vehicles = new List<VehicleDef>();
                        foreach (string vehicleId in J.StrArray(ctx, e, "teamVehicles", optional: true))
                        {
                            if (db._vehiclesById.TryGetValue(vehicleId, out var vehicle)) vehicles.Add(vehicle);
                            else ctx.Error($"'{id}': teamVehicles names unknown vehicle '{vehicleId}'");
                        }
                        var def = new KitDef
                        {
                            Id = id,
                            Index = (byte)list.Count,
                            Name = J.Str(ctx, e, "name"),
                            Description = J.Str(ctx, e, "description", ""),
                            Rank = J.Int(ctx, e, "rank", 1),
                            Personal = Stacks("personal"),
                            Team = Stacks("team"),
                            TeamVehicles = vehicles,
                        };
                        foreach (string bp in J.StrArray(ctx, e, "teamBlueprints", optional: true)) kitBlueprints.Add((def, bp, ctx));
                        if (def.Rank < 1) ctx.Error($"'{id}': rank must be >= 1");
                        if (list.Count == 0 && def.Rank != 1) ctx.Error($"'{id}': the first kit is the default and must be open from rank 1");
                        if (list.Count >= byte.MaxValue) { ctx.Error($"'{id}': too many kits"); continue; }
                        if (!AddUnique(ctx, db._kitsById, def.Id, def)) continue;
                        list.Add(def);
                    }
                }
                if (list.Count == 0) errors.Add("kits: no starting kit (doc 03 §4.4 needs the default Land kit)");
                db.Kits = list;
            }

            // --- postage stamps (doc 01 §6, doc 08 §2.12) ---------------------------------------
            {
                var list = new List<StampDef>();
                foreach (string file in source.ListJson("stamps"))
                {
                    var ctx = new JsonCtx(file, errors);
                    foreach (var e in ReadArray(ctx, Read(file)))
                    {
                        string id = J.Str(ctx, e, "id");
                        var rules = new List<StampRule>();
                        foreach (string rule in J.StrArray(ctx, e, "rules", optional: true))
                        {
                            switch (rule)
                            {
                                case "cursed_mail": rules.Add(StampRule.CursedMail); break;
                                default: ctx.Error($"'{id}': unknown rule '{rule}' (known: cursed_mail)"); break;
                            }
                        }
                        float chance = 0f, valueMult = 1f;
                        if (e.TryGetProperty("params", out var ps) && ps.ValueKind == JsonValueKind.Object)
                        {
                            chance = (float)J.Num(ctx, ps, "chance", 0);
                            valueMult = (float)J.Num(ctx, ps, "valueMult", 1);
                        }
                        var raid = new List<(EnemyDef, int)>();
                        if (e.TryGetProperty("miniRaid", out var mr) && mr.ValueKind == JsonValueKind.Array)
                            foreach (var m in mr.EnumerateArray())
                            {
                                string enemyId = J.Str(ctx, m, "enemy");
                                int count = J.Int(ctx, m, "count");
                                if (!db._enemiesById.TryGetValue(enemyId, out var enemy)) { ctx.Error($"'{id}': miniRaid names unknown enemy '{enemyId}'"); continue; }
                                if (count < 1) { ctx.Error($"'{id}': miniRaid '{enemyId}' count must be >= 1"); continue; }
                                raid.Add((enemy, count));
                            }
                        var def = new StampDef
                        {
                            Id = id,
                            Index = (byte)list.Count,
                            Name = J.Str(ctx, e, "name"),
                            Description = J.Str(ctx, e, "description", ""),
                            Tier = J.Int(ctx, e, "tier", 1),
                            ScoreMult = (float)J.Num(ctx, e, "scoreMult"),
                            Rules = rules,
                            CursedChance = chance,
                            CursedValueMult = valueMult,
                            MiniRaid = raid,
                        };
                        if (def.ScoreMult < 1f) ctx.Error($"'{id}': scoreMult must be >= 1");
                        if (def.Tier < 1 || def.Tier > 3) ctx.Error($"'{id}': tier must be 1..3");
                        bool cursed = rules.Contains(StampRule.CursedMail);
                        if (cursed && (chance <= 0f || chance > 1f || valueMult < 1f || raid.Count == 0)) ctx.Error($"'{id}': cursed_mail wants params.chance in (0, 1], params.valueMult >= 1 and a miniRaid");
                        if (!cursed && (chance > 0f || raid.Count > 0)) ctx.Error($"'{id}': params.chance and miniRaid are cursed_mail's");
                        if (list.Count >= StampDef.MaxStamps) { ctx.Error($"'{id}': too many stamps (at most {StampDef.MaxStamps})"); continue; }
                        if (!AddUnique(ctx, db._stampsById, def.Id, def)) continue;
                        list.Add(def);
                    }
                }
                db.Stamps = list;
            }

            // --- shop ----------------------------------------------------------------------------
            {
                var list = new List<ShopItemDef>();
                var requiresOf = new List<(ShopItemDef Def, string Blueprint, JsonCtx Ctx)>();
                foreach (string file in source.ListJson("shop"))
                {
                    var ctx = new JsonCtx(file, errors);
                    foreach (var e in ReadArray(ctx, Read(file)))
                    {
                        string id = J.Str(ctx, e, "id");
                        string kindName = J.Str(ctx, e, "kind");
                        var kind = ShopKind.Item;
                        ItemDef? item = null;
                        VehicleDef? vehicle = null;
                        int count = 0;
                        bool hasGrants = e.TryGetProperty("grants", out var grants) && grants.ValueKind == JsonValueKind.Object;
                        if (!hasGrants) ctx.Error($"'{id}': missing 'grants' object");
                        switch (kindName)
                        {
                            case "blueprint":
                                kind = ShopKind.Blueprint;
                                // A blueprint card is the blueprint: recipes name it by the card's id (doc 08 §2.7).
                                if (hasGrants && J.Str(ctx, grants, "blueprint") != id) ctx.Error($"'{id}': a blueprint grants itself, 'grants.blueprint' must be '{id}'");
                                break;
                            case "item":
                                if (!hasGrants) break;
                                string itemId = J.Str(ctx, grants, "item");
                                count = J.Int(ctx, grants, "count", 1);
                                if (!db._itemsById.TryGetValue(itemId, out item)) ctx.Error($"'{id}': grants unknown item '{itemId}'");
                                // One purchase is one stack: it fits a free cell or it does not.
                                else if (count < 1 || count > item.MaxStack) ctx.Error($"'{id}': count must be in 1..{item.MaxStack} (one stack of '{itemId}')");
                                break;
                            case "vehicle":
                                kind = ShopKind.Vehicle;
                                if (!hasGrants) break;
                                string vehicleId = J.Str(ctx, grants, "vehicle");
                                if (!db._vehiclesById.TryGetValue(vehicleId, out vehicle)) ctx.Error($"'{id}': grants unknown vehicle '{vehicleId}'");
                                break;
                            // Doc 08 §2.8 names two more kinds; nothing can honour them yet (no hire, no Post Office HP).
                            case "hire":
                            case "service":
                                ctx.Error($"'{id}': kind '{kindName}' is not sold yet");
                                break;
                            default:
                                ctx.Error($"'{id}': unknown kind '{kindName}'");
                                break;
                        }

                        int fromShift = 1;
                        bool rotating = false, kitOnly = false;
                        if (e.TryGetProperty("availability", out var avail))
                        {
                            if (avail.ValueKind != JsonValueKind.Object) ctx.Error($"'{id}': 'availability' must be an object");
                            else
                            {
                                fromShift = J.Int(ctx, avail, "fromShift", 1);
                                string slot = J.Str(ctx, avail, "slot", "fixed");
                                rotating = slot == "rotating";
                                kitOnly = slot == "kit";
                                if (!rotating && !kitOnly && slot != "fixed") ctx.Error($"'{id}': availability.slot must be 'fixed', 'rotating' or 'kit', not '{slot}'");
                            }
                        }
                        var tags = J.StrArray(ctx, e, "tags", optional: true);
                        var def = new ShopItemDef
                        {
                            Id = id,
                            Index = (byte)list.Count,
                            Name = J.Str(ctx, e, "name"),
                            Description = J.Str(ctx, e, "description", ""),
                            Kind = kind,
                            Price = J.Int(ctx, e, "price"),
                            Item = item,
                            Vehicle = vehicle,
                            Count = count,
                            FromShift = fromShift,
                            Rotating = rotating,
                            KitOnly = kitOnly,
                            OncePerRun = J.Bool(ctx, e, "oncePerRun", kind == ShopKind.Blueprint),
                            Special = tags.Contains("special"),
                            Tags = tags,
                        };
                        if (def.Price <= 0 || def.Price > ushort.MaxValue) ctx.Error($"'{id}': price must be in 1..{ushort.MaxValue}");
                        if (def.FromShift < 1) ctx.Error($"'{id}': availability.fromShift must be >= 1");
                        // Doc 04 §1.3: a blueprint is bought once per run and is the whole team's.
                        if (kind == ShopKind.Blueprint && !def.OncePerRun) ctx.Error($"'{id}': a blueprint is bought once per run");
                        if (def.KitOnly && kind != ShopKind.Blueprint) ctx.Error($"'{id}': only a blueprint is given by a kit alone (availability.slot 'kit')");
                        if (def.Rotating && !def.Special) ctx.Error($"'{id}': a rotating entry must be tagged 'special', or it is never on the counter");
                        // Bought and sold straight back must never pay: not at the list price, not as a special.
                        if (item != null && count >= 1 && item.SellPrice > 0 && db.Balance.Has("shopSpecialDiscount")
                            && Run.ShopRules.Price(def, def.Special, Run.ShopRules.DiscountPermille(db.Balance)) < item.SellPrice * count)
                            ctx.Error($"'{id}': costs less than the shop pays for {count} '{item.Id}' ({item.SellPrice * count} gold)");
                        // The wire names a shop entry in one byte.
                        if (list.Count >= byte.MaxValue) { ctx.Error($"'{id}': more than {byte.MaxValue} shop entries"); continue; }
                        if (!AddUnique(ctx, db._shopById, def.Id, def)) continue;
                        string requires = J.Str(ctx, e, "requires", "");
                        if (requires.Length > 0) requiresOf.Add((def, requires, ctx));
                        list.Add(def);
                    }
                }
                // Doc 10 §3 "Requires bp_truck": a blueprint the counter sells.
                foreach (var (def, blueprint, ctx) in requiresOf)
                {
                    if (db._shopById.TryGetValue(blueprint, out var bp) && bp.Kind == ShopKind.Blueprint && bp != def) def.Requires = bp;
                    else ctx.Error($"'{def.Id}': requires '{blueprint}', which is not a blueprint the shop sells");
                }
                db.ShopItems = list;
                foreach (var (kit, blueprint, ctx) in kitBlueprints)
                {
                    if (db._shopById.TryGetValue(blueprint, out var bp) && bp.Kind == ShopKind.Blueprint) kit.TeamBlueprints.Add(bp);
                    else ctx.Error($"'{kit.Id}': teamBlueprints names '{blueprint}', which is no blueprint");
                }
            }

            // --- buildings and their recipes ------------------------------------------------------
            {
                var list = new List<BuildingDef>();
                var recipeOf = new Dictionary<string, string>();
                JsonCtx? lastCtx = null;
                foreach (string file in source.ListJson("buildings"))
                {
                    var ctx = lastCtx = new JsonCtx(file, errors);
                    foreach (var e in ReadArray(ctx, Read(file)))
                    {
                        string id = J.Str(ctx, e, "id");
                        string behaviourName = J.Str(ctx, e, "behaviour");
                        BuildingBehaviour behaviour;
                        switch (behaviourName)
                        {
                            case "wall": behaviour = BuildingBehaviour.Wall; break;
                            case "container": behaviour = BuildingBehaviour.Container; break;
                            case "gate": behaviour = BuildingBehaviour.Gate; break;
                            case "spikes": behaviour = BuildingBehaviour.Spikes; break;
                            case "alarm": behaviour = BuildingBehaviour.Alarm; break;
                            case "belt": behaviour = BuildingBehaviour.Belt; break;
                            case "splitter": behaviour = BuildingBehaviour.Splitter; break;
                            case "merger": behaviour = BuildingBehaviour.Merger; break;
                            case "sorter": behaviour = BuildingBehaviour.Sorter; break;
                            case "inserter": behaviour = BuildingBehaviour.Inserter; break;
                            case "pipe": behaviour = BuildingBehaviour.Pipe; break;
                            case "vehicle_depot": behaviour = BuildingBehaviour.VehicleDepot; break;
                            case "pier": behaviour = BuildingBehaviour.Pier; break;
                            case "boat": behaviour = BuildingBehaviour.Boat; break;
                            case "port": behaviour = BuildingBehaviour.Port; break;
                            case "pump": behaviour = BuildingBehaviour.Pump; break;
                            case "turret": behaviour = BuildingBehaviour.Turret; break;
                            // Doc 08 §2.6 names many more; nothing can honour them yet (no belts, no machines, no turrets).
                            default: ctx.Error($"'{id}': behaviour '{behaviourName}' is not built yet"); continue;
                        }
                        int[] footprint = J.IntArray(ctx, e, "footprint", 2);
                        bool onStreet = false, onWater = false;
                        if (e.TryGetProperty("placement", out var placement) && placement.ValueKind == JsonValueKind.Object)
                        {
                            onStreet = J.Bool(ctx, placement, "onStreet", false);
                            onWater = J.Bool(ctx, placement, "onWater", false);
                        }
                        // Doc 04 §3.3: piers and boats go on the water, and only they.
                        bool watery = behaviour == BuildingBehaviour.Pier || behaviour == BuildingBehaviour.Boat;
                        if (onWater != watery) ctx.Error($"'{id}': a pier or a boat, and only they, have placement.onWater");
                        VehicleDef? boat = null;
                        if (behaviour == BuildingBehaviour.Boat)
                        {
                            string vehicleId = J.Str(ctx, e, "vehicle");
                            if (!db._vehiclesById.TryGetValue(vehicleId, out boat) || !boat.OnWater) ctx.Error($"'{id}': 'vehicle' must name a boat, not '{vehicleId}'");
                        }
                        else if (J.Has(e, "vehicle")) ctx.Error($"'{id}': only a boat placement names a vehicle");
                        ContainerDef? container = null;
                        if (behaviour == BuildingBehaviour.Container || behaviour == BuildingBehaviour.Pump)
                        {
                            string containerId = J.Str(ctx, e, "container");
                            if (!db._containersById.TryGetValue(containerId, out container)) ctx.Error($"'{id}': unknown container '{containerId}'");
                        }
                        else if (J.Has(e, "container")) ctx.Error($"'{id}': only a building whose behaviour is 'container' or 'pump' has a container");
                        bool hasParams = e.TryGetProperty("params", out var pars) && pars.ValueKind == JsonValueKind.Object;
                        if (!hasParams) ctx.Error($"'{id}': missing 'params' object");
                        var def = new BuildingDef
                        {
                            Id = id,
                            Index = (byte)list.Count,
                            Name = J.Str(ctx, e, "name"),
                            Rotations = J.Int(ctx, e, "rotations", 4),
                            Hp = J.Int(ctx, e, "hp"),
                            Behaviour = behaviour,
                            Container = container,
                            Radius = hasParams ? (float)J.Num(ctx, pars, "radius") : 0.2f,
                            Height = hasParams ? (float)J.Num(ctx, pars, "height") : 1f,
                            DamagePerSecond = hasParams ? (float)J.Num(ctx, pars, "damagePerSecond", 0) : 0f,
                            WarningBonusSeconds = hasParams ? (float)J.Num(ctx, pars, "warningBonusSeconds", 0) : 0f,
                            RevealRadius = hasParams ? (float)J.Num(ctx, pars, "revealRadius", 0) : 0f,
                            Speed = hasParams ? (float)J.Num(ctx, pars, "speed", 0) : 0f,
                            Tags = J.StrArray(ctx, e, "tags", optional: true),
                            Length = footprint.Length > 1 ? footprint[1] : 1,
                            Width = footprint.Length > 0 ? footprint[0] : 1,
                            FilterSlots = hasParams ? J.Int(ctx, pars, "filterSlotsPerOutput", 1) : 1,
                            Throughput = hasParams ? (float)J.Num(ctx, pars, "throughputPerSecond", 0) : 0f,
                            BufferSize = hasParams ? J.Int(ctx, pars, "bufferSize", 0) : 0,
                            Rate = hasParams ? (float)J.Num(ctx, pars, "rate", 0) : 0f,
                            Reach = hasParams ? J.Int(ctx, pars, "reach", 1) : 1,
                            PipeRole = behaviour != BuildingBehaviour.Pipe ? PipeRole.None : (hasParams ? J.Str(ctx, pars, "role") : "") switch
                            {
                                "pipe" => PipeRole.Pipe,
                                "junction" => PipeRole.Junction,
                                "inlet" => PipeRole.Inlet,
                                "outlet" => PipeRole.Outlet,
                                "underground" => PipeRole.Underground,
                                _ => PipeRole.None,
                            },
                            Level = hasParams ? J.Int(ctx, pars, "level", 0) : 0,
                            Ramp = hasParams && J.Bool(ctx, pars, "ramp", false),
                            OnStreet = onStreet,
                            OnWater = onWater,
                            Vehicle = boat,
                            RouteStops = hasParams ? J.Int(ctx, pars, "routeStops", 0) : 0,
                            HireCost = hasParams ? J.Int(ctx, pars, "npcHireCost", 0) : 0,
                            CycleSeconds = hasParams ? (float)J.Num(ctx, pars, "cycleSeconds", 0) : 0f,
                            ShotDamage = hasParams ? (float)J.Num(ctx, pars, "shotDamage", 0) : 0f,
                            ShotSeconds = hasParams ? (float)J.Num(ctx, pars, "shotSeconds", 0) : 0f,
                            ShotRange = hasParams ? (float)J.Num(ctx, pars, "range", 0) : 0f,
                            OperatedRate = hasParams ? (float)J.Num(ctx, pars, "operatedRate", 0) : 0f,
                            WallBreakerResist = hasParams ? (float)J.Num(ctx, pars, "wallBreakerResist", 0) : 0f,
                            RuinRebuildRatio = (float)J.Num(ctx, e, "ruinRebuildRatio", 0.5),
                        };
                        // Doc 05 §5, doc 08 §2.6: a ruin is rebuilt for a part of the recipe, never more than all of it.
                        if (def.RuinRebuildRatio < 0f || def.RuinRebuildRatio > 1f) ctx.Error($"'{id}': ruinRebuildRatio must be in [0, 1]");
                        // Doc 08 §2.6 "wall / gate: wallBreakerResist (0–1)".
                        if (def.WallBreakerResist < 0f || def.WallBreakerResist > 1f || (def.WallBreakerResist != 0f && behaviour != BuildingBehaviour.Wall && behaviour != BuildingBehaviour.Gate))
                            ctx.Error($"'{id}': params.wallBreakerResist is for a wall or a gate, in [0, 1]");
                        // Doc 05 §4: a turret fires so hard, so often, so far; nothing else fires.
                        bool turret = behaviour == BuildingBehaviour.Turret;
                        if (turret ? def.ShotDamage <= 0f || def.ShotSeconds <= 0f || def.ShotRange <= 0f || def.OperatedRate < 1f || def.Width != 1 || def.Length != 1
                            : def.ShotDamage != 0f || def.ShotSeconds != 0f || def.ShotRange != 0f || def.OperatedRate != 0f)
                            ctx.Error($"'{id}': a turret, and only a turret, has params.shotDamage, params.shotSeconds and params.range > 0 and params.operatedRate >= 1 (and a footprint of [1, 1])");
                        // Doc 10 §2.3: a pump fills a can in so many seconds, and only a pump has a cycle.
                        if ((behaviour == BuildingBehaviour.Pump) != (def.CycleSeconds > 0f) || (behaviour == BuildingBehaviour.Pump && (def.Width != 1 || def.Length != 1)))
                            ctx.Error($"'{id}': a pump, and only a pump, has params.cycleSeconds > 0 (and a footprint of [1, 1])");
                        // Doc 04 §2.1: a ramp is two tiles long and runs up or down; an elevated belt one level up crosses a street.
                        bool sorter = behaviour == BuildingBehaviour.Sorter;
                        bool square = sorter || behaviour == BuildingBehaviour.Container;
                        bool vehicleDepot = behaviour == BuildingBehaviour.VehicleDepot;
                        if (behaviour == BuildingBehaviour.Port ? def.Width != 3 || def.Length != 4 || def.Rotations != 4
                            : behaviour == BuildingBehaviour.Pier ? def.Width != 1 || def.Length != 3
                            : vehicleDepot ? def.Width != 3 || def.Length != 3
                            : square && def.Width == 2 ? def.Length != 2 : def.Width != 1 || (def.Length != 1 && !(def.Ramp && def.Length == 2)) || (sorter && def.Width != 2))
                            ctx.Error($"'{id}': a footprint of [1, 1] (a ramp: [1, 2], a sorter: [2, 2], a container [1, 1] or [2, 2], a vehicle depot [3, 3], a pier [1, 3], a port [3, 4])");
                        // Doc 04 §3.2: a route of a few stops, kept as 16-bit filters; a driver hired for a price.
                        if (vehicleDepot || behaviour == BuildingBehaviour.Port ? def.RouteStops < 1 || def.RouteStops > 9 || def.HireCost <= 0 || def.Rotations != 4 : def.RouteStops != 0 || def.HireCost != 0)
                            ctx.Error($"'{id}': a vehicle depot or a port, and only they, have params.routeStops in 1..9, params.npcHireCost > 0 (and 4 rotations)");
                        // Doc 04 §2.4: a sorter looks at its mail at a rate, out of a buffer.
                        if (sorter && (def.Throughput <= 0f || def.BufferSize < 1 || def.BufferSize > 64 || def.Rotations != 4))
                            ctx.Error($"'{id}': a sorter needs params.throughputPerSecond > 0, params.bufferSize in 1..64 and 4 rotations");
                        if (!sorter && (def.Throughput != 0f || def.BufferSize != 0)) ctx.Error($"'{id}': only a sorter has params.throughputPerSecond and params.bufferSize");
                        bool inserter = behaviour == BuildingBehaviour.Inserter;
                        if (inserter ? def.Rate <= 0f || def.Rotations != 4 : def.Rate != 0f) ctx.Error($"'{id}': an inserter, and only an inserter, has params.rate > 0 (and 4 rotations)");
                        if (inserter ? def.Reach != 1 && def.Reach != 2 : def.Reach != 1) ctx.Error($"'{id}': params.reach is an inserter's, 1 or 2");
                        if (def.FilterSlots < 1 || def.FilterSlots > 3 || (def.FilterSlots != 1 && !sorter)) ctx.Error($"'{id}': params.filterSlotsPerOutput is 1 (a sorter's up to 3)");
                        if (def.Ramp && (behaviour != BuildingBehaviour.Belt || def.Length != 2 || def.Rotations != 8 || def.Level != 0)) ctx.Error($"'{id}': a ramp is a belt of footprint [1, 2], 8 rotations and level 0");
                        if (def.Level != 0 && (behaviour != BuildingBehaviour.Belt || def.Level != 1)) ctx.Error($"'{id}': only a belt is raised, by one level");
                        if (def.OnStreet && def.Level != 1 && def.PipeRole != PipeRole.Underground) ctx.Error($"'{id}': only an elevated belt and an underground pipe are built on a street");
                        if (behaviour == BuildingBehaviour.Pipe && (def.PipeRole == PipeRole.None || def.Rotations != 4))
                            ctx.Error($"'{id}': a pipe needs params.role (pipe, junction, inlet, outlet or underground) and 4 rotations");
                        if (behaviour == BuildingBehaviour.Spikes && def.DamagePerSecond <= 0f) ctx.Error($"'{id}': spikes need params.damagePerSecond > 0");
                        if (behaviour != BuildingBehaviour.Spikes && def.DamagePerSecond != 0f) ctx.Error($"'{id}': only spikes do damage (params.damagePerSecond)");
                        if (behaviour == BuildingBehaviour.Alarm && (def.WarningBonusSeconds <= 0f || def.RevealRadius <= 0f)) ctx.Error($"'{id}': an alarm needs params.warningBonusSeconds > 0 and params.revealRadius > 0");
                        if (behaviour != BuildingBehaviour.Alarm && (def.WarningBonusSeconds != 0f || def.RevealRadius != 0f)) ctx.Error($"'{id}': only an alarm warns and reveals (params.warningBonusSeconds, params.revealRadius)");
                        if (def.Carries && (def.Speed <= 0f || def.Rotations != (def.Ramp ? 8 : 4))) ctx.Error($"'{id}': a {behaviourName} needs params.speed > 0 and 4 rotations");
                        if (!def.Carries && def.Speed != 0f) ctx.Error($"'{id}': only a belt, a splitter or a merger has params.speed");
                        if (def.Rotations != 1 && def.Rotations != 2 && def.Rotations != 4 && !(def.Ramp && def.Rotations == 8)) ctx.Error($"'{id}': rotations must be 1, 2 or 4");
                        // The wire carries a construct's HP in 16 bits.
                        if (def.Hp < 1 || def.Hp > ushort.MaxValue) ctx.Error($"'{id}': hp must be in 1..{ushort.MaxValue}");
                        // It stands within its own tile, and a walker beside the tile does not touch it.
                        float maxRadius = Math.Min(def.Width, def.Length) * SimConstants.TileSize * 0.5f - 0.5f;
                        if (def.Radius <= 0f || def.Radius > maxRadius) ctx.Error($"'{id}': params.radius must be in (0, {maxRadius.ToString(System.Globalization.CultureInfo.InvariantCulture)}]");
                        if (def.Height <= 0f) ctx.Error($"'{id}': params.height must be > 0");
                        // The wire names a building in one byte.
                        if (list.Count >= byte.MaxValue) { ctx.Error($"'{id}': more than {byte.MaxValue} buildings"); continue; }
                        if (!AddUnique(ctx, db._buildingsById, def.Id, def)) continue;
                        recipeOf[def.Id] = J.Str(ctx, e, "recipe");
                        list.Add(def);
                    }
                }
                db.Buildings = list;

                var recipes = new List<RecipeDef>();
                var recipesById = new Dictionary<string, RecipeDef>();
                foreach (string file in source.ListJson("recipes"))
                {
                    var ctx = new JsonCtx(file, errors);
                    foreach (var e in ReadArray(ctx, Read(file)))
                    {
                        string id = J.Str(ctx, e, "id");
                        // Doc 08 §2.7: there is no item crafting, and what is built stands at once.
                        if (!e.TryGetProperty("produces", out var produces) || produces.ValueKind != JsonValueKind.Object) { ctx.Error($"'{id}': missing 'produces' object"); continue; }
                        if (J.Has(produces, "item")) { ctx.Error($"'{id}': a recipe produces a building, never an item"); continue; }
                        if (J.Num(ctx, e, "craftSeconds", 0) > 0) ctx.Error($"'{id}': craftSeconds must be 0, placement is instant");
                        string buildingId = J.Str(ctx, produces, "building");
                        if (!db._buildingsById.TryGetValue(buildingId, out var building)) { ctx.Error($"'{id}': produces unknown building '{buildingId}'"); continue; }
                        var inputs = new List<RecipeInput>();
                        if (!e.TryGetProperty("inputs", out var inputsEl) || inputsEl.ValueKind != JsonValueKind.Array) ctx.Error($"'{id}': missing 'inputs' array");
                        else
                        {
                            foreach (var input in inputsEl.EnumerateArray())
                            {
                                if (input.ValueKind != JsonValueKind.Object) { ctx.Error($"'{id}': inputs must be objects"); continue; }
                                string itemId = J.Str(ctx, input, "item");
                                int count = J.Int(ctx, input, "count");
                                if (!db._itemsById.TryGetValue(itemId, out var item)) { ctx.Error($"'{id}': unknown input item '{itemId}'"); continue; }
                                if (count < 1 || count > byte.MaxValue) ctx.Error($"'{id}': the count of '{itemId}' must be in 1..{byte.MaxValue}");
                                bool twice = false;
                                foreach (var other in inputs) twice |= other.Item == item;
                                if (twice) ctx.Error($"'{id}': '{itemId}' is an input twice");
                                else inputs.Add(new RecipeInput(item, count));
                            }
                            if (inputs.Count == 0) ctx.Error($"'{id}': a recipe needs at least one input");
                        }
                        ShopItemDef? blueprint = null;
                        if (J.Has(e, "blueprint"))
                        {
                            string blueprintId = J.Str(ctx, e, "blueprint");
                            if (!db._shopById.TryGetValue(blueprintId, out blueprint) || blueprint.Kind != ShopKind.Blueprint)
                            {
                                ctx.Error($"'{id}': '{blueprintId}' is not a blueprint the shop sells");
                                blueprint = null;
                            }
                        }
                        var def = new RecipeDef
                        {
                            Id = id,
                            Building = building,
                            Inputs = inputs,
                            Blueprint = blueprint,
                            UnlockShift = J.Int(ctx, e, "unlockShift", 1),
                            Group = J.Has(e, "group") ? J.Str(ctx, e, "group") : id,
                        };
                        if (def.UnlockShift < 1) ctx.Error($"'{id}': unlockShift must be >= 1");
                        if (!AddUnique(ctx, recipesById, def.Id, def)) continue;
                        if (building.Recipe != null) { ctx.Error($"'{id}': '{buildingId}' is built by '{building.Recipe.Id}' already"); continue; }
                        if (recipeOf[buildingId] != id) { ctx.Error($"'{id}': '{buildingId}' names '{recipeOf[buildingId]}' as its recipe"); continue; }
                        building.Recipe = def;
                        recipes.Add(def);
                    }
                }
                db.Recipes = recipes;
                var keys = new List<List<RecipeDef>>();
                var keyOf = new Dictionary<string, int>();
                foreach (var recipe in recipes)
                {
                    if (!keyOf.TryGetValue(recipe.Group, out int key)) { key = keyOf[recipe.Group] = keys.Count; keys.Add(new List<RecipeDef>()); }
                    recipe.Key = key;
                    keys[key].Add(recipe);
                }
                db.RecipeKeys = keys.ConvertAll(k => (IReadOnlyList<RecipeDef>)k);
                // 1–9, 0, minus and equals.
                if (keys.Count > MaxRecipeKeys) new JsonCtx("recipes", errors).Error($"{keys.Count} keys of Build mode: at most {MaxRecipeKeys} (group the variants of a building)");
                foreach (var building in list)
                    if (building.Recipe == null) (lastCtx ?? new JsonCtx("buildings", errors)).Error($"'{building.Id}': its recipe '{recipeOf[building.Id]}' is not in content/recipes");
            }

            // --- streets -------------------------------------------------------------------------
            {
                var ctx = new JsonCtx("streets.json", errors);
                string? text = Read(ctx.File);
                using var doc = text == null ? null : J.Parse(ctx, text);
                if (doc != null && doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var names = J.StrArray(ctx, doc.RootElement, "names");
                    var seen = new HashSet<string>();
                    foreach (string n in names)
                        if (!seen.Add(n)) ctx.Error($"duplicate street name '{n}'");
                    db.StreetNames = names;
                }
                else if (doc != null) ctx.Error("root must be an object");
            }

            // --- maps (M0: hand-authored static maps) --------------------------------------------
            {
                var streetNames = new HashSet<string>(db.StreetNames);
                foreach (string file in source.ListJson("maps"))
                {
                    var ctx = new JsonCtx(file, errors);
                    string? text = Read(file);
                    if (text == null) continue;
                    var map = MapLoader.Load(ctx, text, db._destTypesById, streetNames, db._containersById);
                    if (map == null) continue;
                    if (db._maps.ContainsKey(map.Id)) ctx.Error($"duplicate map id '{map.Id}'");
                    else db._maps[map.Id] = map;
                    // The debug world's showcase names buildings and vehicles of the content (design choice 119).
                    if (map.Debug == null) continue;
                    foreach (var c in map.Debug.Constructs)
                        if (!db._buildingsById.ContainsKey(c.Building)) ctx.Error($"debug: unknown building '{c.Building}'");
                    foreach (var v in map.Debug.Vehicles)
                        if (!db._vehiclesById.ContainsKey(v.Vehicle)) ctx.Error($"debug: unknown vehicle '{v.Vehicle}'");
                }
            }

            db.ContentHash = hash.Value;
            return db;
        }

        /// <summary>The traitParams an enemy may have (doc 08 §2.10).</summary>
        private static readonly string[] TraitParamNames = { "keepDistance", "blastRadius", "structureDamage", "provokeRange", "provokeSeconds", "altitude", "bombRadius", "pickaxeMult" };

        /// <summary>Balance keys the code reads; a missing one is a validation error rather than a runtime crash.</summary>
        public static readonly string[] RequiredBalanceKeys =
        {
            "baseQuota", "playerScaleExponent", "deliverySeconds", "walletFloor",
            "complaintMax", "complaintDecayPerSecond", "complaintBacklogIncrement", "complaintBacklogIntervalSeconds",
            "complaintLateDelivery", "walkSpeed", "sprintSpeed", "jumpHeight", "gravity", "playerRadius",
            "weightPointsLight", "weightPointsMedium", "weightPointsHeavy", "weightSpeedPerPoint", "weightSpeedFloor",
            "interactRange", "serverInteractRange", "interactRewindSeconds", "maxPlayers",
            "playerHp", "hpRegenPerSecond", "hpRegenDelaySeconds", "respawnSeconds",
            "staminaSeconds", "staminaRegenSeconds", "staminaRecoverFraction",
            "hammerDamage", "hammerCooldownSeconds", "hammerRange", "hammerArcDegrees", "hammerKnockback",
            "cannonDamage", "cannonBlastRadius", "cannonFuseRadius", "cannonCooldownSeconds", "cannonRange", "cannonSpeed",
            "enemyWindupSeconds", "enemyFleeSeconds", "enemyFleeSpeedMult", "lootDespawnSeconds", "lootPickupRange",
            "deathBagDespawnSeconds", "respawnProtectionSeconds", "respawnYardMetres", "raidStandoffMetres", "enemyLeashMult", "enemyProvokedSeconds",
            "maxAttackersPerCarrier", "attackerWaitMetres", "disconnectGraceSeconds",
            "generatingSeconds", "draftSeconds", "draftCards", "draftRarityWeights", "draftRareWeightPerShift", "rerollsPerRun", "rerollSecondRank",
            "shopSpecialSlots", "shopSpecialDiscount", "shopRange", "serverShopRange", "harvestSwingSeconds",
            "buildRange", "serverBuildRange", "deconstructHoldSeconds", "deconstructRefundPrep", "deconstructRefundDelivery",
            "beltPushMultiplier", "beltPushSeconds", "pipeSpeed", "pipeSpacing", "pipeOutletRate", "pipeOutletCapacity",
            "npcSpeedRatio", "npcHireShiftSolo", "npcHireShiftGroup", "npcDepartWaitSeconds", "npcStopReach", "npcFleeRadius",
        };

        private static IEnumerable<JsonElement> ReadArray(JsonCtx ctx, string? text)
        {
            if (text == null) yield break;
            using var doc = J.Parse(ctx, text);
            if (doc == null) yield break;
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                ctx.Error("root must be an array of definitions");
                yield break;
            }
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) { ctx.Error("definitions must be objects"); continue; }
                yield return e;
            }
        }

        private static bool AddUnique<T>(JsonCtx ctx, Dictionary<string, T> dict, string id, T def)
        {
            if (string.IsNullOrEmpty(id)) { ctx.Error("definition with empty id"); return false; }
            if (dict.ContainsKey(id)) { ctx.Error($"duplicate id '{id}'"); return false; }
            dict[id] = def;
            return true;
        }

        private static PerkCategory ParsePerkCategory(JsonCtx ctx, string id, string s)
        {
            switch (s)
            {
                case "carrier": return PerkCategory.Carrier;
                case "facility": return PerkCategory.Facility;
                case "postal": return PerkCategory.Postal;
                case "defense": return PerkCategory.Defense;
                default: ctx.Error($"'{id}': unknown category '{s}'"); return PerkCategory.Carrier;
            }
        }

        private static PerkRarity ParsePerkRarity(JsonCtx ctx, string id, string s)
        {
            switch (s)
            {
                case "common": return PerkRarity.Common;
                case "uncommon": return PerkRarity.Uncommon;
                case "rare": return PerkRarity.Rare;
                default: ctx.Error($"'{id}': unknown rarity '{s}'"); return PerkRarity.Common;
            }
        }

        private static WeightClass ParseWeight(JsonCtx ctx, string s)
        {
            switch (s)
            {
                case "light": return WeightClass.Light;
                case "medium": return WeightClass.Medium;
                case "heavy": return WeightClass.Heavy;
                case "bulk": return WeightClass.Bulk;
                default: ctx.Error($"unknown weightClass '{s}'"); return WeightClass.Light;
            }
        }
    }
}
