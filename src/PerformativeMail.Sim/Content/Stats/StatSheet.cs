using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Stats;

/// <summary>
/// Builds the stat sheets (ADR-0010 section 7). <see cref="Build"/> is a pure function of a <see cref="ContentSet"/> and a
/// <see cref="StatSources"/>: it reads no clock, no random and no static state, and needs no <c>SimWorld</c>, so a client
/// or Presentation can call it with a replicated source list. The result caches, for every
/// <c>(stat, qualifier, sheet)</c> cell, the F1 value and its pre-rounding components, resolved through
/// <see cref="StatResolve"/> (the only place F1 is implemented). Timed effects are not sources; they enter a read as
/// <c>transientAdd</c> (the sheet has no timers).
/// </summary>
/// <example>
/// <code>
/// ContentSet content = BuiltInContent.CreateM1a();
/// var sources = new StatSources([longLegs0, longLegs1, longLegs2]);
/// IStatSheet sheet = StatSheet.Build(content, sources);
/// int v = sheet.Read(Stat.MoveSpeedAddPm, slot: PlayerSlot.FromValue(0), transientAdd: 300);   // 400 (capped)
/// </code>
/// </example>
public static class StatSheet
{
    /// <summary>The "no qualifier" id (0xFFFF): the default of a read, and a modifier that applies to every qualifier.</summary>
    public const ushort NoQualifier = 0xFFFF;

    /// <summary>The number of sheets per stat cell: the team sheet plus one personal sheet per <see cref="PlayerSlot"/>.</summary>
    public const int SheetCount = PlayerSlot.Count + 1;

    /// <summary>
    /// Resolves every cell against <paramref name="sources"/>. Scope rules (CD11, CD15): a team stat reads the team
    /// modifiers only (the slot is ignored); a personal stat reads that slot's modifiers only; a <c>both</c> stat reads the
    /// team modifiers merged with the slot's. A slot with no modifiers reads team and base only. An unqualified modifier
    /// (qualifier <see cref="NoQualifier"/>) on a qualified stat applies to every qualifier; a qualified modifier applies
    /// only to its ordinal. The result does not depend on the order of <paramref name="sources"/>. A modifier that cannot
    /// apply (unknown stat, wrong scope for its owner, an op the stat does not accept, a qualifier on an unqualified stat or
    /// out of range) trips <c>SIM_ASSERT</c> and is skipped otherwise. Base values come from the stat's
    /// <see cref="BaseSource"/>: a constant, a balance key, the container's <c>GridH</c> or the mail kind's <c>MaxStack</c>.
    /// Allocates (it is the rebuild path, not a read). Deterministic.
    /// </summary>
    /// <param name="content">The content set (balance, resolved stat bounds, container and mail-kind tables).</param>
    /// <param name="sources">The source list; may be empty.</param>
    /// <returns>An immutable sheet.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> or <paramref name="sources"/> is null.</exception>
    /// <exception cref="SimAssertException">SIM_ASSERT builds only: an inapplicable modifier, or a cell with more than <see cref="StatResolve.MaxMulFactors"/> Muls.</exception>
    /// <example>
    /// <code>
    /// IStatSheet empty = StatSheet.Build(BuiltInContent.CreateM1a(), new StatSources([]));
    /// int rows = empty.Read(Stat.InventoryRows, playerInventoryIndex.Value);   // 2
    /// </code>
    /// </example>
    public static IStatSheet Build(ContentSet content, StatSources sources) => BuildCache(content, sources);

    /// <summary>The same as <see cref="Build"/>, typed as the sealed cache so the live sheet can call it without an interface hop.</summary>
    internal static StatSheetCache BuildCache(ContentSet content, StatSources sources)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(sources);

        StatLayout[] layouts = CreateLayouts(content, out int cellOrdinals);
        Modifier[] team = Accept(sources.Team, isTeam: true, layouts);
        Modifier[][] slots = new Modifier[PlayerSlot.Count][];
        int widest = team.Length;
        for (int s = 0; s < slots.Length; s++)
        {
            slots[s] = Accept(sources.ForSlot(PlayerSlot.FromValue((byte)s)), isTeam: false, layouts);
            widest = Math.Max(widest, team.Length + slots[s].Length);
        }

        var scratch = new Scratch(widest, ContributionCount(team, slots, layouts));
        StatCell[] cells = new StatCell[cellOrdinals * SheetCount];
        for (int i = 0; i < layouts.Length; i++)
        {
            FillStat(content, StatDefs.Get((Stat)(i + 1)), layouts[i], team, slots, scratch, cells);
        }

        int flagWidth = RuleFlagSet.DefinedWidth;
        return new StatSheetCache(layouts, cells, scratch.Contributions, CountFlags(sources.Grants, flagWidth), flagWidth);
    }

    /// <summary>One layout per defined stat: its cell range, scope, ops, rounding and resolved bounds.</summary>
    internal static StatLayout[] CreateLayouts(ContentSet content, out int cellOrdinals)
    {
        var layouts = new StatLayout[StatDefs.Count];
        cellOrdinals = 0;
        for (int i = 0; i < layouts.Length; i++)
        {
            StatDef def = StatDefs.Get((Stat)(i + 1));
            StatBounds bounds = content.ResolvedStatBounds[i];
            int qualifiers = QualifierCountOf(def, content);
            layouts[i] = new StatLayout(cellOrdinals, qualifiers, def.Qualifier != QualifierKind.None, def.Scope, def.Ops, def.Rounding, bounds.Min, bounds.Max);
            cellOrdinals += qualifiers;
        }

        return layouts;
    }

    /// <summary>The live-source count per (sheet, flag): row 0 is the team, row 1 + slot a slot. Invalid grants assert and are skipped.</summary>
    private static int[] CountFlags(ReadOnlySpan<RuleFlagGrant> grants, int width)
    {
        int[] counts = new int[SheetCount * width];
        for (int i = 0; i < grants.Length; i++)
        {
            int f = (int)grants[i].Flag - 1;
            bool ok = grants[i].Owner.IsValid && unchecked((uint)f) < (uint)width;
            SimAssert.That(ok, ContentAssertCode.StatSheetBadFlagGrant);
            if (ok)
            {
                counts[(grants[i].Owner.ListIndex * width) + f]++;
            }
        }

        return counts;
    }

    private static void FillStat(
        ContentSet content, StatDef def, StatLayout layout, Modifier[] team, Modifier[][] slots, Scratch scratch, StatCell[] cells)
    {
        for (int q = 0; q < layout.QualifierCount; q++)
        {
            int baseValue = BaseOf(content, def, q);
            ushort qualifierId = layout.Qualified ? (ushort)q : NoQualifier;
            for (int sheet = 0; sheet < SheetCount; sheet++)
            {
                if (sheet > 0 && layout.Scope == StatScope.Team)
                {
                    continue;
                }

                scratch.Begin();
                if (sheet == 0 || layout.Scope == StatScope.Both)
                {
                    scratch.Collect(team, def.Stat, qualifierId);
                }

                if (sheet > 0)
                {
                    scratch.Collect(slots[sheet - 1], def.Stat, qualifierId);
                }

                StatResolution r = StatResolve.Resolve(
                    def.Rounding, def.SetResolve, layout.Min, layout.Max, baseValue,
                    scratch.AddsSpan, scratch.MulsSpan, scratch.SetsSpan, 0);
                cells[((layout.FirstCell + q) * SheetCount) + sheet] = new StatCell(r, scratch.SourceStart, scratch.ContributionCount - scratch.SourceStart);
            }
        }
    }

    /// <summary>How many qualifier ordinals a stat has: 1 when unqualified, else the size of the qualifier def table.</summary>
    /// <exception cref="InvalidOperationException">The stat row has a qualifier kind this build does not know (a bug in the row).</exception>
    private static int QualifierCountOf(StatDef def, ContentSet content)
    {
        switch (def.Qualifier)
        {
            case QualifierKind.None:
                return 1;
            case QualifierKind.Container:
                return content.Containers.Count;
            case QualifierKind.MailKind:
                return content.MailKinds.Count;
            default:
                throw new InvalidOperationException("Stat #" + ((int)def.Stat).ToString(System.Globalization.CultureInfo.InvariantCulture) + " has a qualifier kind the StatSheet does not know.");
        }
    }

    /// <summary>
    /// The exact number of explain entries the build will write, so the buffer is allocated once at its final size: each
    /// accepted modifier appears once per qualifier it applies to (all of them for an unqualified modifier on a qualified
    /// stat) and once per sheet it feeds (a team modifier on a <c>both</c> stat feeds the team sheet and all 8 personal ones).
    /// </summary>
    private static int ContributionCount(Modifier[] team, Modifier[][] slots, StatLayout[] layouts)
    {
        int total = 0;
        for (int i = 0; i < team.Length; i++)
        {
            StatLayout layout = layouts[(int)team[i].Stat - 1];
            total += FanOut(team[i], layout) * (layout.Scope == StatScope.Both ? SheetCount : 1);
        }

        for (int s = 0; s < slots.Length; s++)
        {
            for (int i = 0; i < slots[s].Length; i++)
            {
                total += FanOut(slots[s][i], layouts[(int)slots[s][i].Stat - 1]);
            }
        }

        return total;
    }

    private static int FanOut(in Modifier m, StatLayout layout) =>
        layout.Qualified && m.Qualifier == NoQualifier ? layout.QualifierCount : 1;

    private static int BaseOf(ContentSet content, StatDef def, int qualifier) => def.Base.Kind switch
    {
        BaseSourceKind.ContainerRows => content.Containers[DefIndex.FromValue((ushort)qualifier)].GridH,
        BaseSourceKind.MailKindMaxStack => content.MailKinds[DefIndex.FromValue((ushort)qualifier)].MaxStack,
        _ => def.ResolveBase(content.Balance),
    };

    /// <summary>Keeps the modifiers a sheet can apply, asserting on each one it cannot; keeps input order.</summary>
    private static Modifier[] Accept(ReadOnlySpan<Modifier> list, bool isTeam, StatLayout[] layouts)
    {
        Modifier[] kept = new Modifier[list.Length];
        int count = 0;
        for (int i = 0; i < list.Length; i++)
        {
            bool ok = Applicable(list[i], isTeam, layouts);
            SimAssert.That(ok, ContentAssertCode.StatSheetBadModifier);
            if (ok)
            {
                kept[count++] = list[i];
            }
        }

        if (count != kept.Length)
        {
            Array.Resize(ref kept, count);
        }

        return kept;
    }

    /// <summary>True when the sheet of the given owner kind can apply the modifier (known stat, scope, op, qualifier).</summary>
    internal static bool Applicable(in Modifier m, bool isTeam, StatLayout[] layouts)
    {
        int index = (int)m.Stat - 1;
        if (unchecked((uint)index) >= (uint)layouts.Length)
        {
            return false;
        }

        StatLayout layout = layouts[index];
        if (isTeam ? layout.Scope == StatScope.Personal : layout.Scope == StatScope.Team)
        {
            return false;
        }

        StatOps op = m.Op switch
        {
            ModifierOp.Add => StatOps.Add,
            ModifierOp.Mul => StatOps.Mul,
            ModifierOp.Set => StatOps.Set,
            _ => StatOps.None,
        };
        if ((layout.Ops & op) == 0)
        {
            return false;
        }

        return layout.Qualified ? m.Qualifier == NoQualifier || m.Qualifier < layout.QualifierCount : m.Qualifier == NoQualifier;
    }

    /// <summary>Build-time working storage for one cell's op lists and the shared explain buffer.</summary>
    private sealed class Scratch
    {
        private readonly int[] _adds;
        private readonly int[] _muls;
        private readonly int[] _sets;
        private int _addCount;
        private int _mulCount;
        private int _setCount;

        public Scratch(int capacity, int contributionCount)
        {
            _adds = new int[capacity];
            _muls = new int[capacity];
            _sets = new int[capacity];
            Contributions = new StatContribution[contributionCount];
        }

        public StatContribution[] Contributions { get; }

        public int ContributionCount { get; private set; }

        public int SourceStart { get; private set; }

        public ReadOnlySpan<int> AddsSpan => new(_adds, 0, _addCount);

        public ReadOnlySpan<int> MulsSpan => new(_muls, 0, _mulCount);

        public ReadOnlySpan<int> SetsSpan => new(_sets, 0, _setCount);

        public void Begin()
        {
            _addCount = 0;
            _mulCount = 0;
            _setCount = 0;
            SourceStart = ContributionCount;
        }

        public void Collect(Modifier[] list, Stat stat, ushort qualifierId)
        {
            for (int i = 0; i < list.Length; i++)
            {
                Modifier m = list[i];
                if (m.Stat != stat || (m.Qualifier != NoQualifier && m.Qualifier != qualifierId))
                {
                    continue;
                }

                switch (m.Op)
                {
                    case ModifierOp.Add:
                        _adds[_addCount++] = m.Value;
                        break;
                    case ModifierOp.Mul:
                        _muls[_mulCount++] = m.Value;
                        break;
                    default:
                        _sets[_setCount++] = m.Value;
                        break;
                }

                Contributions[ContributionCount++] = new StatContribution(m.SourceKind, m.SourceId, m.StackIndex, m.OwnerSlot, m.Qualifier, m.Op, m.Value, m.Lifetime);
            }
        }
    }
}
