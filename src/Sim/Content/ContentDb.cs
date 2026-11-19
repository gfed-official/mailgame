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
    public sealed class ContentDb
    {
        public IReadOnlyList<MailKindDef> MailKinds { get; private set; } = Array.Empty<MailKindDef>();
        public IReadOnlyList<DestinationTypeDef> DestinationTypes { get; private set; } = Array.Empty<DestinationTypeDef>();
        public IReadOnlyList<ContainerDef> Containers { get; private set; } = Array.Empty<ContainerDef>();
        public IReadOnlyList<ItemDef> Items { get; private set; } = Array.Empty<ItemDef>();
        public IReadOnlyList<NodeDef> Nodes { get; private set; } = Array.Empty<NodeDef>();
        public IReadOnlyList<EnemyDef> Enemies { get; private set; } = Array.Empty<EnemyDef>();
        public IReadOnlyList<PerkDef> Perks { get; private set; } = Array.Empty<PerkDef>();
        public IReadOnlyList<ShopItemDef> ShopItems { get; private set; } = Array.Empty<ShopItemDef>();
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
        private readonly Dictionary<string, PerkDef> _perksById = new Dictionary<string, PerkDef>();
        private readonly Dictionary<string, ShopItemDef> _shopById = new Dictionary<string, ShopItemDef>();
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
        public ShopItemDef? FindShopItem(string id) => _shopById.TryGetValue(id, out var d) ? d : null;
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
                foreach (string file in source.ListJson("items"))
                {
                    if (file == "items/containers.json") continue;
                    var ctx = new JsonCtx(file, errors);
                    foreach (var e in ReadArray(ctx, Read(file)))
                    {
                        int[] grid = J.IntArray(ctx, e, "grid", 2);
                        string id = J.Str(ctx, e, "id");
                        IReadOnlyList<string> harvests = Array.Empty<string>();
                        float yieldMultiplier = 1f, swingSeconds = 0f;
                        if (e.TryGetProperty("tool", out var tool))
                        {
                            if (tool.ValueKind != JsonValueKind.Object) ctx.Error($"'{id}': 'tool' must be an object");
                            else
                            {
                                harvests = J.StrArray(ctx, tool, "harvests");
                                yieldMultiplier = (float)J.Num(ctx, tool, "yieldMultiplier", 1);
                                swingSeconds = (float)J.Num(ctx, tool, "swingTime");
                                if (harvests.Count == 0) ctx.Error($"'{id}': a tool harvests something");
                                if (yieldMultiplier <= 0f || swingSeconds <= 0f) ctx.Error($"'{id}': tool.yieldMultiplier and tool.swingTime must be > 0");
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
                        };
                        if (harvests.Count > 0 && def.Category != "tool") ctx.Error($"'{def.Id}': only an item of category 'tool' has a 'tool' block");
                        if (def.GridW <= 0 || def.GridH <= 0) ctx.Error($"'{def.Id}': grid sizes must be positive");
                        if (def.MaxStack <= 0) ctx.Error($"'{def.Id}': maxStack must be positive");
                        if (def.SellPrice < 0 || def.BuyPrice < 0) ctx.Error($"'{def.Id}': prices must not be negative");
                        if (def.Heal < 0 || def.HealSeconds < 0) ctx.Error($"'{def.Id}': heal and healSeconds must not be negative");
                        if (def.Heal == 0 && def.HealSeconds > 0) ctx.Error($"'{def.Id}': healSeconds without heal");
                        if (!AddUnique(ctx, db._itemsById, def.Id, def)) continue;
                        list.Add(def);
                    }
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
                            default: ctx.Error($"'{id}': prop must be 'tree', 'bush' or 'boulder', not '{propName}'"); continue;
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
                        var def = new EnemyDef
                        {
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
                    if (root.TryGetProperty("weightsByShift", out var byShift) && byShift.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var s in byShift.EnumerateObject())
                        {
                            if (!int.TryParse(s.Name, out int shift) || shift < 1) { ctx.Error($"weightsByShift: '{s.Name}' is not a shift number"); continue; }
                            var entries = new List<KeyValuePair<EnemyDef, int>>();
                            if (s.Value.ValueKind != JsonValueKind.Object) { ctx.Error($"weightsByShift {shift}: must be an object of enemy weights"); continue; }
                            foreach (var p in s.Value.EnumerateObject())
                            {
                                if (!db._enemiesById.TryGetValue(p.Name, out var enemy)) { ctx.Error($"weightsByShift {shift}: unknown enemy '{p.Name}'"); continue; }
                                if (p.Value.ValueKind != JsonValueKind.Number || !p.Value.TryGetInt32(out int weight) || weight < 0)
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
                        }
                    }
                    else ctx.Error("missing 'weightsByShift' object");

                    float megaHp = 1, megaDmg = 1, megaScale = 1, megaSpeed = 1;
                    int megaLoot = 1;
                    if (root.TryGetProperty("mega", out var mega) && mega.ValueKind == JsonValueKind.Object)
                    {
                        megaHp = (float)J.Num(ctx, mega, "hpMult");
                        megaDmg = (float)J.Num(ctx, mega, "dmgMult");
                        megaScale = (float)J.Num(ctx, mega, "scale");
                        megaSpeed = (float)J.Num(ctx, mega, "speedMult");
                        megaLoot = J.Int(ctx, mega, "lootMult", 1);
                        if (megaHp <= 0 || megaDmg <= 0 || megaScale <= 0 || megaSpeed <= 0 || megaLoot < 1) ctx.Error("mega multipliers must be positive");
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
                        WarningSeconds = J.Num(ctx, root, "warningSeconds"),
                        RaidWindowSeconds = J.Num(ctx, root, "raidWindowSeconds"),
                        MegaChanceBase = J.Num(ctx, root, "megaChanceBase", 0),
                        MegaChancePerShift = J.Num(ctx, root, "megaChancePerShift", 0),
                        MegaHpMult = megaHp, MegaDamageMult = megaDmg, MegaScale = megaScale, MegaSpeedMult = megaSpeed, MegaLootMult = megaLoot,
                        MaxAlive = J.Int(ctx, root, "maxAlive"),
                        WeightsByShift = weights,
                    };
                    if (def.Pulses < 1) ctx.Error("pulses must be >= 1");
                    if (def.PulseIntervalSeconds <= 0) ctx.Error("pulseIntervalSeconds must be > 0");
                    if (def.WarningSeconds < 0 || def.RaidWindowSeconds <= 0) ctx.Error("warningSeconds must be >= 0 and raidWindowSeconds > 0");
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

            // --- shop ----------------------------------------------------------------------------
            {
                var list = new List<ShopItemDef>();
                foreach (string file in source.ListJson("shop"))
                {
                    var ctx = new JsonCtx(file, errors);
                    foreach (var e in ReadArray(ctx, Read(file)))
                    {
                        string id = J.Str(ctx, e, "id");
                        string kindName = J.Str(ctx, e, "kind");
                        var kind = ShopKind.Item;
                        ItemDef? item = null;
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
                            // Doc 08 §2.8 names three more kinds; nothing can honour them yet (no vehicle, no hire, no Post Office HP).
                            case "vehicle":
                            case "hire":
                            case "service":
                                ctx.Error($"'{id}': kind '{kindName}' is not sold yet");
                                break;
                            default:
                                ctx.Error($"'{id}': unknown kind '{kindName}'");
                                break;
                        }

                        int fromShift = 1;
                        bool rotating = false;
                        if (e.TryGetProperty("availability", out var avail))
                        {
                            if (avail.ValueKind != JsonValueKind.Object) ctx.Error($"'{id}': 'availability' must be an object");
                            else
                            {
                                fromShift = J.Int(ctx, avail, "fromShift", 1);
                                string slot = J.Str(ctx, avail, "slot", "fixed");
                                rotating = slot == "rotating";
                                if (!rotating && slot != "fixed") ctx.Error($"'{id}': availability.slot must be 'fixed' or 'rotating', not '{slot}'");
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
                            Count = count,
                            FromShift = fromShift,
                            Rotating = rotating,
                            OncePerRun = J.Bool(ctx, e, "oncePerRun", kind == ShopKind.Blueprint),
                            Special = tags.Contains("special"),
                            Tags = tags,
                        };
                        if (def.Price <= 0 || def.Price > ushort.MaxValue) ctx.Error($"'{id}': price must be in 1..{ushort.MaxValue}");
                        if (def.FromShift < 1) ctx.Error($"'{id}': availability.fromShift must be >= 1");
                        // Doc 04 §1.3: a blueprint is bought once per run and is the whole team's.
                        if (kind == ShopKind.Blueprint && !def.OncePerRun) ctx.Error($"'{id}': a blueprint is bought once per run");
                        if (def.Rotating && !def.Special) ctx.Error($"'{id}': a rotating entry must be tagged 'special', or it is never on the counter");
                        // Bought and sold straight back must never pay: not at the list price, not as a special.
                        if (item != null && count >= 1 && item.SellPrice > 0 && db.Balance.Has("shopSpecialDiscount")
                            && Run.ShopRules.Price(def, def.Special, Run.ShopRules.DiscountPermille(db.Balance)) < item.SellPrice * count)
                            ctx.Error($"'{id}': costs less than the shop pays for {count} '{item.Id}' ({item.SellPrice * count} ¢)");
                        // The wire names a shop entry in one byte.
                        if (list.Count >= byte.MaxValue) { ctx.Error($"'{id}': more than {byte.MaxValue} shop entries"); continue; }
                        if (!AddUnique(ctx, db._shopById, def.Id, def)) continue;
                        list.Add(def);
                    }
                }
                db.ShopItems = list;
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
                }
            }

            db.ContentHash = hash.Value;
            return db;
        }

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
            "draftSeconds", "draftCards", "draftRarityWeights", "draftRareWeightPerShift",
            "shopSpecialSlots", "shopSpecialDiscount", "shopRange", "serverShopRange", "harvestSwingSeconds",
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
