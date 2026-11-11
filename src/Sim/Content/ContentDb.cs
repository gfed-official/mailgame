using System;
using System.Collections.Generic;
using System.Text.Json;
using PerformativeMail.Sim.Core;
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

        private ContentDb() { }

        public MailKindDef MailKind(string id) => _mailKindsById[id];
        public MailKindDef? FindMailKind(string id) => _mailKindsById.TryGetValue(id, out var d) ? d : null;
        public DestinationTypeDef DestinationType(string id) => _destTypesById[id];
        public ContainerDef Container(string id) => _containersById[id];
        public ItemDef Item(string id) => _itemsById[id];
        public WorldMap Map(string id) => _maps[id];

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
                        var def = new ItemDef
                        {
                            Id = J.Str(ctx, e, "id"),
                            Index = (ushort)list.Count,
                            Name = J.Str(ctx, e, "name"),
                            Category = J.Str(ctx, e, "category"),
                            GridW = grid[0],
                            GridH = grid[1],
                            MaxStack = J.Int(ctx, e, "maxStack"),
                            WeightClass = ParseWeight(ctx, J.Str(ctx, e, "weightClass")),
                            SellPrice = J.Int(ctx, e, "sellPrice", 0),
                            BuyPrice = J.Int(ctx, e, "buyPrice", 0),
                        };
                        if (def.GridW <= 0 || def.GridH <= 0) ctx.Error($"'{def.Id}': grid sizes must be positive");
                        if (def.MaxStack <= 0) ctx.Error($"'{def.Id}': maxStack must be positive");
                        if (!AddUnique(ctx, db._itemsById, def.Id, def)) continue;
                        list.Add(def);
                    }
                }
                db.Items = list;
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
                    var map = MapLoader.Load(ctx, text, db._destTypesById, streetNames);
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
