using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// Stage 3 of the content load (ADR-0010 section 4, GDD CD21): turns the stage 2 <see cref="ParsedDef"/>s into the five def
/// records. Per def type, and only when every file of that type passed stages 1 and 2 (otherwise the type is not built and
/// its table key is a failed input, so the rules that declare it are skipped and counted):
/// <list type="number">
/// <item>more than <see cref="DefIdAssigner.MaxDefsPerType"/> defs (raw count) is CV-GEN-09 and the type is left empty;</item>
/// <item>an id defined twice is one CV-GEN-01 naming every copy as <c>path:line</c>; no copy is kept and the type is a failed
/// input (as <see cref="ContentPipeline"/> treats built-in duplicates);</item>
/// <item>the survivors get their ordinal <c>DefIndex</c> from <see cref="DefIdAssigner"/> before any record is built, so by-id
/// references resolve to the index the pipeline will assign (decision log 2026-10-17); every reference is also passed on as a
/// <see cref="PendingReference"/>, so a missing or wrong-type target is CV-GEN-01 at stage 4, exactly as for built-in content;</item>
/// <item>absent fields take their <see cref="FieldSpec"/> default. Units were converted by stage 2: a <c>…Seconds</c> field is
/// already milliseconds, and that is the whole time conversion (OQ-018-1: defs keep ms, consumers convert to ticks).</item>
/// </list>
/// An unresolved reference is stored as <see cref="DefIndex.None"/>. Stateless; load-time only (allocates).
/// </summary>
/// <example>
/// <code>
/// BuiltDefs defs = Builder.Build(new[] { itemsResult, containersResult });
/// </code>
/// </example>
public static class Builder
{
    private static readonly ImmutableArray<DefType> _typeOrder = ImmutableArray.Create(
        DefType.Item, DefType.Container, DefType.MailKind, DefType.DestinationType, DefType.MailMix);

    private static readonly ImmutableArray<DefType> _recordOrder = ImmutableArray.Create(
        DefType.Container, DefType.Item, DefType.MailKind, DefType.DestinationType, DefType.MailMix);

    /// <summary>Builds every def type present in <paramref name="files"/>; a type with no file is empty and not failed.</summary>
    /// <param name="files">The stage 2 results, in manifest order.</param>
    /// <returns>The records, references, failed inputs and the findings of stages 1 to 3.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> is null.</exception>
    public static BuiltDefs Build(IReadOnlyList<SchemaResult> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return new Pass(files).Run();
    }

    private sealed class Pass
    {
        private readonly IReadOnlyList<SchemaResult> _files;
        private readonly List<ContentDiagnostic> _found = new();
        private readonly List<FailedInput> _failed = new();
        private readonly List<PendingReference> _refs = new();
        private readonly Dictionary<DefType, List<ParsedDef>> _kept = new();
        private readonly Dictionary<DefType, Dictionary<string, DefIndex>> _index = new();

        public Pass(IReadOnlyList<SchemaResult> files)
        {
            _files = files;
        }

        public BuiltDefs Run()
        {
            foreach (SchemaResult file in _files)
            {
                _found.AddRange(file.Diagnostics);
            }

            foreach (DefType type in _typeOrder)
            {
                Prepare(type);
            }

            return new BuiltDefs(
                Records(DefType.Container, Container),
                Records(DefType.Item, Item),
                Records(DefType.MailKind, MailKind),
                Records(DefType.DestinationType, Destination),
                Records(DefType.MailMix, Mix),
                _refs.ToImmutableArray(),
                _failed.ToImmutableArray(),
                _found.ToImmutableArray(),
                DefRecords());
        }

        /// <summary>The built defs' records, in the order <c>ContentValidationInput</c> lists built-in defs (type, then DefIndex).</summary>
        private ImmutableArray<DefRecord> DefRecords()
        {
            ImmutableArray<DefRecord>.Builder b = ImmutableArray.CreateBuilder<DefRecord>();
            foreach (DefType type in _recordOrder)
            {
                if (_kept.TryGetValue(type, out List<ParsedDef>? defs))
                {
                    foreach (ParsedDef d in defs)
                    {
                        b.Add(new DefRecord(new DefRef(type, d.Id!), d.Start, d.Suppress));
                    }
                }
            }

            return b.ToImmutable();
        }

        /// <summary>Gates the type on stages 1 and 2, checks the count and duplicates, and pre-assigns the ordinals.</summary>
        private void Prepare(DefType type)
        {
            var defs = new List<ParsedDef>();
            foreach (SchemaResult file in _files)
            {
                if (file.Type != type)
                {
                    continue;
                }

                // Approved by Evan 2026-10-28 6:09 AM PT (TD-176): this returns before DropDuplicates, so a duplicate id where either copy also has
                // a schema error shows only that error; its CV-GEN-01 appears once the type is clean.
                if (!file.Ok)
                {
                    _failed.Add(new FailedInput(KeyOf(type), DefRef.None, file.Path));
                    return;
                }

                defs.AddRange(file.Defs);
            }

            if (defs.Count > DefIdAssigner.MaxDefsPerType)
            {
                TooMany(type, defs);
                return;
            }

            List<ParsedDef> survivors = DropDuplicates(type, defs);
            DefAssignment assignment = DefIdAssigner.Assign(IdsOf(survivors));
            var placed = new ParsedDef[survivors.Count];
            var index = new Dictionary<string, DefIndex>(StringComparer.Ordinal);
            for (int i = 0; i < placed.Length; i++)
            {
                DefIndex at = assignment.IndexOfInput[i];
                placed[at.Value] = survivors[i];
                index[survivors[i].Id!] = at;
            }

            _kept[type] = new List<ParsedDef>(placed);
            _index[type] = index;
        }

        private void TooMany(DefType type, List<ParsedDef> defs)
        {
            DefAssignment verdict = DefIdAssigner.Assign(IdsOf(defs));   // count-only TooManyDefs, the single source of the limit
            ParsedDef first = defs[DefIdAssigner.MaxDefsPerType];       // Approved by Evan 2026-10-28 6:09 AM PT (TD-176): located at the first def over the limit
            _found.Add(new ContentDiagnostic(
                GeneralRuleIds.TooManyDefs, RuleSeverity.Error, first.Start, DefRef.None,
                verdict.Count.ToString(CultureInfo.InvariantCulture) + " defs",
                "at most " + DefIdAssigner.MaxDefsPerType.ToString(CultureInfo.InvariantCulture) + " defs per type"));
            _failed.Add(new FailedInput(KeyOf(type), DefRef.None, first.Start.Path));
        }

        private List<ParsedDef> DropDuplicates(DefType type, List<ParsedDef> defs)
        {
            var copies = new Dictionary<string, List<ParsedDef>>(StringComparer.Ordinal);
            var repeated = new List<string>();   // by the position of the second copy
            foreach (ParsedDef d in defs)
            {
                string id = d.Id!;
                if (!copies.TryGetValue(id, out List<ParsedDef>? list))
                {
                    copies[id] = new List<ParsedDef> { d };
                    continue;
                }

                list.Add(d);
                if (list.Count == 2)
                {
                    repeated.Add(id);
                }
            }

            foreach (string id in repeated)
            {
                _found.Add(DuplicateFinding(type, id, copies[id]));
            }

            if (repeated.Count > 0)
            {
                _failed.Add(new FailedInput(KeyOf(type), DefRef.None, copies[repeated[0]][0].Start.Path));
            }

            return defs.FindAll(d => copies[d.Id!].Count == 1);
        }

        // Approved by Evan 2026-10-28 6:09 AM PT (TD-176): the location form for file-backed findings is `path:line` (the line of each copy's '{').
        private static ContentDiagnostic DuplicateFinding(DefType type, string id, List<ParsedDef> copies)
        {
            var where = new string[copies.Count];
            for (int k = 0; k < where.Length; k++)
            {
                where[k] = copies[k].Start.Path + ":" + copies[k].Start.Line.ToString(CultureInfo.InvariantCulture);
            }

            return new ContentDiagnostic(
                GeneralRuleIds.IdsAndReferences, RuleSeverity.Error, copies[0].Start, new DefRef(type, id),
                "id '" + id + "' defined at " + string.Join(" and ", where), "an id unique within its def type (neither def is kept)");
        }

        private static string[] IdsOf(List<ParsedDef> defs)
        {
            var ids = new string[defs.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = defs[i].Id ?? string.Empty;
            }

            return ids;
        }

        private ImmutableArray<T> Records<T>(DefType type, Func<ParsedDef, Fields, T> build)
        {
            if (!_kept.TryGetValue(type, out List<ParsedDef>? defs))
            {
                return ImmutableArray<T>.Empty;
            }

            ImmutableArray<FieldSpec> rows = ContentSchema.Rows(type);
            ImmutableArray<T>.Builder b = ImmutableArray.CreateBuilder<T>(defs.Count);
            foreach (ParsedDef d in defs)
            {
                b.Add(build(d, new Fields(rows, d.Fields)));
            }

            return b.MoveToImmutable();
        }

        // ------------------------------------------------------------ records

        private static ItemDef Item(ParsedDef d, Fields f) => new()
        {
            Id = d.Id!,
            Name = f.Text("name"),
            Category = (ItemCategory)f.Enum("category"),
            GridW = f.ListInt("grid", 0),
            GridH = f.ListInt("grid", 1),
            MaxStack = f.Int("maxStack"),
            WeightClass = (WeightClass)f.Enum("weightClass"),
            SellPriceCents = f.Long("sellPrice"),
            HasBuyPrice = f.Has("buyPrice"),
            BuyPriceCents = f.Long("buyPrice"),
            CarryByHand = f.Bool("carryByHand"),
            EquipSlot = (EquipSlot)f.Enum("equipSlot"),
            Tags = f.Texts("tags"),
        };

        private static ContainerDef Container(ParsedDef d, Fields f) => new()
        {
            Id = d.Id!,
            Layout = (ContainerLayout)f.Enum("layout"),
            HasGrid = f.Has("grid"),
            GridW = f.ListInt("grid", 0),
            GridH = f.ListInt("grid", 1),
            View = (ContainerView)f.Enum("view"),
            BeltAccess = (BeltAccess)f.Enum("beltAccess"),
            HasAllowedCategories = f.Has("allowedCategories"),
            AllowedCategories = f.Items("allowedCategories").Select(v => (ItemCategory)v.Integer).ToImmutableArray(),
            HandCarried = f.Bool("handCarried"),
            AcceptsInsert = f.Bool("acceptsInsert"),
            ReservedSlots = f.Items("reservedSlots").Select(v => checked((int)v.Integer)).ToImmutableArray(),
        };

        private MailKindDef MailKind(ParsedDef d, Fields f)
        {
            var from = new DefRef(DefType.MailKind, d.Id!);
            return new MailKindDef
            {
                Id = d.Id!,
                Name = f.Text("name"),
                GridW = f.ListInt("grid", 0),
                GridH = f.ListInt("grid", 1),
                MaxStack = f.Int("maxStack"),
                BaseValueCents = f.Long("baseValue"),
                WeightClass = (WeightClass)f.Enum("weightClass"),
                CarryByHand = f.Bool("carryByHand"),
                BeltLanes = f.Int("beltLanes"),
                BeltLengthMm = f.Int("beltLengthMetres"),
                DeadlineOffsetShifts = f.Int("deadlineOffsetShifts"),
                AcceptedBy = RefList(from, f, "acceptedBy", DefType.DestinationType),
                ComplaintOnMisdeliveryMilliPoints = f.Int("complaintOnMisdeliveryPoints"),
                UnlockShift = f.Int("unlockShift"),
                FallbackKind = f.Field("fallbackKind") is ParsedField fb
                    ? Resolve(from, fb.Location, "fallbackKind", DefType.MailKind, fb.Value.Text ?? string.Empty)
                    : DefIndex.None,
            };
        }

        private DestinationTypeDef Destination(ParsedDef d, Fields f)
        {
            Fields volume = f.Nested("catchVolume");
            return new DestinationTypeDef
            {
                Id = d.Id!,
                Accepts = RefList(new DefRef(DefType.DestinationType, d.Id!), f, "accepts", DefType.MailKind),
                InsertRateMilliPerSec = f.Int("insertRate"),
                ManualInsertHoldMs = f.Int("manualInsertHoldSeconds"),
                MaxAutomatedFeeders = f.Int("maxAutomatedFeeders"),
                RequiresUnit = f.Bool("requiresUnit"),
                CatchVolume = new CatchVolume(
                    volume.Int("widthMetres"), volume.Int("heightMetres"), volume.Int("depthMetres"),
                    volume.Int("centreHeightMetres"), volume.Int("yawDeg")),
            };
        }

        private MailMixDef Mix(ParsedDef d, Fields f) => new()
        {
            Id = d.Id!,
            Shifts = Shifts(new DefRef(DefType.MailMix, d.Id!), f),
            BatchIntervalMs = f.Int("batchIntervalSeconds"),
            BatchJitterMs = f.Int("batchJitterSeconds"),
            TailGuardMs = f.Int("tailGuardSeconds"),
            OpeningRampBatches = f.Int("openingRampBatches"),
            SpawnOverheadPm = f.Int("spawnOverhead"),
            StreetStreakRatioPm = f.Int("streetStreakRatio"),
            RaidSpawnRateRatioPm = f.Int("raidSpawnRateRatio"),
            DistanceStepRatioPm = f.Int("distanceStepRatio"),
            ShiftStepRatioPm = f.Int("shiftStepRatio"),
        };

        private ImmutableArray<ShiftMix> Shifts(DefRef from, Fields f)
        {
            ImmutableArray<FieldSpec> shiftRows = f.Row("shifts").Children;
            ImmutableArray<ParsedValue> shifts = f.Items("shifts");
            ImmutableArray<ShiftMix>.Builder b = ImmutableArray.CreateBuilder<ShiftMix>(shifts.Length);
            for (int i = 0; i < shifts.Length; i++)
            {
                var shift = new Fields(shiftRows, shifts[i].Fields);
                string field = "shifts[" + i.ToString(CultureInfo.InvariantCulture) + "].shares";
                ImmutableArray<ParsedEntry> entries = shift.Field("shares")?.Value.Entries ?? ImmutableArray<ParsedEntry>.Empty;
                ImmutableArray<MailShare>.Builder shares = ImmutableArray.CreateBuilder<MailShare>(entries.Length);
                foreach (ParsedEntry e in entries)
                {
                    shares.Add(new MailShare(Resolve(from, e.Location, field, DefType.MailKind, e.Key), checked((int)e.Value)));
                }

                b.Add(new ShiftMix(shift.Int("shift"), shares.MoveToImmutable()));
            }

            return b.MoveToImmutable();
        }

        private ImmutableArray<DefIndex> RefList(DefRef from, Fields f, string name, DefType target)
        {
            ImmutableArray<ParsedValue> ids = f.Items(name);
            SourceLocation where = f.Field(name)?.Location ?? default;
            ImmutableArray<DefIndex>.Builder b = ImmutableArray.CreateBuilder<DefIndex>(ids.Length);
            for (int i = 0; i < ids.Length; i++)
            {
                string field = name + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                b.Add(Resolve(from, where, field, target, ids[i].Text ?? string.Empty));
            }

            return b.MoveToImmutable();
        }

        /// <summary>
        /// The pre-assigned index of <paramref name="id"/> in <paramref name="target"/>, or None when the target type was not built
        /// or has no such id; either way the reference is passed on, and stage 4 reports a missing or wrong-type target (CV-GEN-01).
        /// </summary>
        private DefIndex Resolve(DefRef from, SourceLocation where, string field, DefType target, string id)
        {
            // Approved by Evan 2026-10-28 6:09 AM PT (TD-176): an unresolved target is stored as DefIndex.None, so stage 4's CV-GEN-01 can report one bad
            // list reference twice (the pending clause naming the id, and the index clause naming "none").
            _refs.Add(new PendingReference(from, where, field, target, id));
            return _index.TryGetValue(target, out Dictionary<string, DefIndex>? ids) && ids.TryGetValue(id, out DefIndex at)
                ? at
                : DefIndex.None;
        }

        private static string KeyOf(DefType type) => type switch
        {
            DefType.Container => ContentInputKeys.Containers,
            DefType.Item => ContentInputKeys.Items,
            DefType.MailKind => ContentInputKeys.MailKinds,
            DefType.DestinationType => ContentInputKeys.DestinationTypes,
            _ => ContentInputKeys.MailMixes,
        };
    }

    /// <summary>The fields of one parsed object, read by name with the <see cref="FieldSpec"/> default applied when absent.</summary>
    private readonly struct Fields
    {
        private readonly ImmutableArray<FieldSpec> _rows;
        private readonly ImmutableArray<ParsedField> _fields;

        public Fields(ImmutableArray<FieldSpec> rows, ImmutableArray<ParsedField> fields)
        {
            _rows = rows;
            _fields = fields;
        }

        public FieldSpec Row(string name) => _rows[RowOf(name)];

        public ParsedField? Field(string name)
        {
            int row = RowOf(name);
            foreach (ParsedField f in _fields)
            {
                if (f.Row == row)
                {
                    return f;
                }
            }

            return null;
        }

        public bool Has(string name) => Field(name) is not null;

        public long Long(string name) => Field(name)?.Value.Integer ?? Row(name).Default ?? 0;

        public int Int(string name) => checked((int)Long(name));

        public bool Bool(string name) => Long(name) != 0;

        public int Enum(string name)
        {
            if (Field(name) is ParsedField f)
            {
                return (int)f.Value.Integer;
            }

            FieldSpec row = Row(name);
            return row.DefaultSpelling is string s && EnumSets.TryParse(row.EnumSet ?? string.Empty, Encoding.UTF8.GetBytes(s), out int v) ? v : 0;
        }

        public string Text(string name) => Field(name)?.Value.Text ?? string.Empty;

        public ImmutableArray<ParsedValue> Items(string name) => Field(name)?.Value.Items ?? ImmutableArray<ParsedValue>.Empty;

        public int ListInt(string name, int position)
        {
            ImmutableArray<ParsedValue> items = Items(name);
            return position < items.Length ? checked((int)items[position].Integer) : 0;
        }

        public ImmutableArray<string> Texts(string name) => Items(name).Select(v => v.Text ?? string.Empty).ToImmutableArray();

        public Fields Nested(string name) =>
            new(Row(name).Children, Field(name)?.Value.Fields ?? ImmutableArray<ParsedField>.Empty);

        private int RowOf(string name)
        {
            for (int r = 0; r < _rows.Length; r++)
            {
                if (string.Equals(_rows[r].Name, name, StringComparison.Ordinal))
                {
                    return r;
                }
            }

            throw new ArgumentException("No field '" + name + "' in the table.", nameof(name));
        }
    }
}
