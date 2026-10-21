using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.Rules;

/// <summary>
/// Placement validity (GDD R4, R3, R5, R1 <c>reservedSlots</c>; TR-inventory-004/006; ADR-0009 section 2; story grid-inventory/006):
/// the rule-level suffix of the placement chain, <c>reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked /
/// stack_full</c>, in that order. The handler-level prefix (<c>bad_request → not_open → stale_entry → stale_layout</c>) needs the
/// request, the open set and the entry table and is the handlers' (stories 007/008). Pure: reads <see cref="Occupancy"/> and
/// <see cref="EntryPool"/>, writes nothing, allocates nothing, and costs O(footprint rows) bitmap tests plus a cell scan of the
/// rows that are not free. A slots layout (hotbar, cursor) holds one entry per slot whatever its size, so its footprint is 1 x 1;
/// its target index is <c>y * w + x</c>.
/// </summary>
/// <example>
/// <code>
/// var subject = PlacementSubject.Of(content, stats, in entries[cursorEntry]);
/// PlacementVerdict v = PlacementRules.Validate(occupancy, entries, slot, in header, def, in subject, x, y, rotated);
/// if (!v.IsOk) { return Reply.Reject(in op, (ushort)v.Reason); }
/// plan.Placement = new InvPlacement(cursorEntry, slot, (byte)x, (byte)y, rotated);   // v.IsMerge / v.TargetSlot drive Commit
/// </code>
/// </example>
public static class PlacementRules
{
    /// <summary>
    /// Validates placing <paramref name="subject"/> with its top-left at (<paramref name="x"/>, <paramref name="y"/>) in the
    /// container at <paramref name="containerSlot"/>. The verdict is a free placement, a merge into exactly one mergeable stack whose
    /// placement the footprint matches exactly (R4), or the earliest failing reason of the rule-level chain. Out of bounds is decided
    /// before any overlap, overflow-safe for any <c>int</c> position; a zero-size footprint is out of bounds (as in
    /// <see cref="Occupancy"/>). A stack never merges into itself: a <c>merge {A→A}</c> request must be validated with
    /// <paramref name="wholeMove"/> false, which blocks it, whereas <paramref name="wholeMove"/> true onto the source's own cells
    /// yields a free placement (the handler's no-op).
    /// </summary>
    /// <param name="occupancy">The occupancy bitmaps and cell owners.</param>
    /// <param name="entries">The entry pool (read for the overlapped stack).</param>
    /// <param name="containerSlot">The destination's container table slot.</param>
    /// <param name="header">The destination's header (instance <c>w x h</c> and layout).</param>
    /// <param name="def">The destination's def.</param>
    /// <param name="subject">The entry being placed, with <see cref="PlacementSubject.EffMax"/> read at op time.</param>
    /// <param name="x">Top-left column (slots layout: the slot column).</param>
    /// <param name="y">Top-left row.</param>
    /// <param name="rotated">The requested rotation (ignored on a slots layout; canonicalised for a square footprint).</param>
    /// <param name="sourceSlot">The pool slot of the entry being moved, or <see cref="Occupancy.Free"/> for a new entry or one leaving no cells here.</param>
    /// <param name="wholeMove">True for a whole in-container move: the source's own old cells count as free. False for a split-and-place, whose remainder still occupies them.</param>
    /// <returns>The verdict.</returns>
    public static PlacementVerdict Validate(
        Occupancy occupancy,
        EntryPool entries,
        ushort containerSlot,
        in ContainerHeader header,
        ContainerDef def,
        in PlacementSubject subject,
        int x,
        int y,
        bool rotated,
        ushort sourceSlot = Occupancy.Free,
        bool wholeMove = false)
    {
        bool slots = header.Layout == ContainerLayout.Slots;
        if (slots && IsReservedSlot(def, header.W, header.H, x, y))
        {
            return PlacementVerdict.Reject(InvReason.ReservedSlot);
        }

        int fw = 1;
        int fh = 1;
        if (!slots)
        {
            Footprint.Canonicalise(subject.W, subject.H, ref rotated);
            Footprint.Effective(subject.W, subject.H, rotated, out fw, out fh);
        }

        if (x < 0 || y < 0 || fw < 1 || fh < 1 || x > header.W - fw || y > header.H - fh)
        {
            return PlacementVerdict.Reject(InvReason.OutOfBounds);
        }

        InvReason whitelist = InsertionWhitelist.Check(def, in subject);
        if (whitelist != 0)
        {
            return PlacementVerdict.Reject(whitelist);
        }

        OverlapResult overlap = ScanOverlap(occupancy, containerSlot, header.W, x, y, fw, fh, sourceSlot, wholeMove, out ushort owner);
        return overlap switch
        {
            OverlapResult.Free => PlacementVerdict.FreeCells(subject.EffMax),
            OverlapResult.Blocked => PlacementVerdict.Reject(InvReason.Blocked),
            _ => ResolveTarget(entries, owner, def, in subject, x, y, fw, fh, slots),
        };
    }

    /// <summary>
    /// R3's mergeable test for two <b>distinct</b> entries: equal <see cref="StackKey"/>, <c>effMax &gt; 1</c>, and the target
    /// container's <c>allowedCategories</c> admit the category. Fullness is separate (a full mergeable target is <c>stack_full</c>).
    /// </summary>
    /// <param name="moving">The moving entry's key.</param>
    /// <param name="target">The target stack's key.</param>
    /// <param name="effMax">The kind's effective max stack, read at op time.</param>
    /// <param name="container">The target container's def.</param>
    /// <param name="category">The entries' category.</param>
    /// <returns>True when the two may merge.</returns>
    public static bool CanMerge(StackKey moving, StackKey target, int effMax, ContainerDef container, ItemCategory category) =>
        moving == target && effMax > 1 && InsertionWhitelist.Admits(container, category);

    /// <summary>True when (<paramref name="x"/>, <paramref name="y"/>) is an in-range slot listed in <see cref="ContainerDef.ReservedSlots"/>.</summary>
    /// <param name="def">The container's def.</param>
    /// <param name="w">The container's columns.</param>
    /// <param name="h">The container's rows.</param>
    /// <param name="x">The slot column.</param>
    /// <param name="y">The slot row.</param>
    /// <returns>True when reserved.</returns>
    public static bool IsReservedSlot(ContainerDef def, int w, int h, int x, int y)
    {
        if (def.ReservedSlots.IsDefaultOrEmpty || x < 0 || y < 0 || x >= w || y >= h)
        {
            return false;
        }

        int index = y * w + x;
        foreach (int reserved in def.ReservedSlots)
        {
            if (reserved == index)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// R4 overlap scan of the in-bounds footprint (<paramref name="fw"/> x <paramref name="fh"/> at (<paramref name="x"/>,
    /// <paramref name="y"/>)): <see cref="OverlapResult.Free"/> when no covered cell is owned (the source's own cells count as free
    /// on a whole move), <see cref="OverlapResult.Single"/> when every covered cell is owned by one entry other than the source,
    /// else <see cref="OverlapResult.Blocked"/> (two or more entries, a partial overlap, or a stack onto itself). Skips free rows
    /// with one bitmap test.
    /// </summary>
    /// <param name="occupancy">The occupancy bitmaps and cell owners.</param>
    /// <param name="containerSlot">The destination's container table slot.</param>
    /// <param name="w">The destination's columns.</param>
    /// <param name="x">Top-left column.</param>
    /// <param name="y">Top-left row.</param>
    /// <param name="fw">Effective footprint width.</param>
    /// <param name="fh">Effective footprint height.</param>
    /// <param name="sourceSlot">The pool slot of the entry being moved, or <see cref="Occupancy.Free"/>.</param>
    /// <param name="wholeMove">True when the source's own old cells count as free.</param>
    /// <param name="owner">The single owner on <see cref="OverlapResult.Single"/>; otherwise not meaningful.</param>
    /// <returns>The scan result.</returns>
    private static OverlapResult ScanOverlap(
        Occupancy occupancy, ushort containerSlot, int w, int x, int y, int fw, int fh, ushort sourceSlot, bool wholeMove, out ushort owner)
    {
        owner = Occupancy.Free;
        int ownedCells = 0;
        ulong mask = fw == 64 ? ulong.MaxValue : ((1UL << fw) - 1UL) << x;
        for (int r = 0; r < fh; r++)
        {
            if ((occupancy.RowBits(containerSlot, y + r) & mask) == 0UL)
            {
                continue;
            }

            for (int c = 0; c < fw; c++)
            {
                ushort cell = occupancy.OwnerAt(containerSlot, w, x + c, y + r);
                if (cell == Occupancy.Free || (wholeMove && cell == sourceSlot))
                {
                    continue;
                }

                if (owner != Occupancy.Free && cell != owner)
                {
                    return OverlapResult.Blocked;   // two or more entries
                }

                owner = cell;
                ownedCells++;
            }
        }

        if (ownedCells == 0)
        {
            return OverlapResult.Free;
        }

        return ownedCells != fw * fh || owner == sourceSlot ? OverlapResult.Blocked : OverlapResult.Single;   // partial overlap, or a stack onto itself
    }

    /// <summary>
    /// R4/R3 target resolution once <see cref="ScanOverlap"/> found one owner covering the whole footprint: <c>blocked</c> unless
    /// the target's placement matches the footprint exactly and <see cref="CanMerge"/> holds; then <c>stack_full</c> when the
    /// target holds <see cref="PlacementSubject.EffMax"/> or more, else the merge-into verdict.
    /// </summary>
    /// <param name="entries">The entry pool.</param>
    /// <param name="owner">The overlapped stack's pool slot.</param>
    /// <param name="def">The destination's def.</param>
    /// <param name="subject">The entry being placed.</param>
    /// <param name="x">Top-left column.</param>
    /// <param name="y">Top-left row.</param>
    /// <param name="fw">Effective footprint width.</param>
    /// <param name="fh">Effective footprint height.</param>
    /// <param name="slots">True for a slots layout (every footprint 1 x 1).</param>
    /// <returns>The verdict.</returns>
    private static PlacementVerdict ResolveTarget(
        EntryPool entries, ushort owner, ContainerDef def, in PlacementSubject subject, int x, int y, int fw, int fh, bool slots)
    {
        ref EntryRecord target = ref entries[owner];
        int tw = 1;
        int th = 1;
        if (!slots)
        {
            Footprint.Effective(target.W, target.H, target.Rotated, out tw, out th);
        }

        if (target.X != x || target.Y != y || tw != fw || th != fh
            || !CanMerge(subject.Key, StackKey.Of(in target), subject.EffMax, def, subject.Category))
        {
            return PlacementVerdict.Reject(InvReason.Blocked);
        }

        if (target.Count >= subject.EffMax)
        {
            return new PlacementVerdict(InvReason.StackFull, false, owner, subject.EffMax);
        }

        return new PlacementVerdict(0, true, owner, subject.EffMax);
    }

    /// <summary>The outcome of <see cref="ScanOverlap"/>.</summary>
    private enum OverlapResult : byte
    {
        /// <summary>No covered cell is owned.</summary>
        Free,

        /// <summary>Every covered cell is owned by one entry other than the source.</summary>
        Single,

        /// <summary>Two or more entries, a partial overlap, or a stack onto itself.</summary>
        Blocked,
    }
}

/// <summary>
/// What is being placed, resolved once per op (story grid-inventory/006): the <see cref="StackKey"/>, the unrotated def footprint,
/// the R5 traits and <see cref="EffMax"/> — <c>IStatSheet.Read(MailMaxStack, kind)</c> for mail, <c>ItemDef.MaxStack</c> for items —
/// read at op time and never cached across ticks (ADR-0010 section 7, CD20).
/// </summary>
/// <example>
/// <code>
/// PlacementSubject s = PlacementSubject.ForMail(letterDef, letterIndex, 17u, 0, 3, sheet);   // s.EffMax == 20 at M-1a
/// </code>
/// </example>
public readonly struct PlacementSubject
{
    /// <summary>The stacking key.</summary>
    public readonly StackKey Key;

    /// <summary>
    /// Unrotated footprint width (def <c>gridW</c>). The narrowing to <c>byte</c> is lossless: the content parser bounds a def's
    /// <c>grid</c> to 1..255 (field spec), so a loaded def never truncates.
    /// </summary>
    public readonly byte W;

    /// <summary>Unrotated footprint height (def <c>gridH</c>); bounded 1..255 like <see cref="W"/>.</summary>
    public readonly byte H;

    /// <summary>The category (<see cref="ItemCategory.Mail"/> for mail).</summary>
    public readonly ItemCategory Category;

    /// <summary>The def's <c>carryByHand</c>.</summary>
    public readonly bool CarryByHand;

    /// <summary>The def's weight class.</summary>
    public readonly WeightClass WeightClass;

    /// <summary>
    /// The effective max stack at op time. Mail: <c>StatDefs</c> clamps <c>MailMaxStack</c> to 1..255 (entry <c>count</c> is a
    /// <c>uint8</c>), and CV-GRD-01 rejects content whose <c>maxStack</c> times the worst-case Mul exceeds 255. Item: the def's
    /// <c>maxStack</c>, which CV-GRD-04 bounds below at 1; its only upper bound is the field spec's 65 535 — no rule caps an item
    /// <c>maxStack</c> at 255 (all M-1a items are at most 20).
    /// </summary>
    public readonly int EffMax;

    /// <summary>Creates a subject from resolved values.</summary>
    /// <param name="key">The stacking key.</param>
    /// <param name="w">Unrotated width.</param>
    /// <param name="h">Unrotated height.</param>
    /// <param name="category">The category.</param>
    /// <param name="carryByHand">The def's <c>carryByHand</c>.</param>
    /// <param name="weightClass">The weight class.</param>
    /// <param name="effMax">The effective max stack.</param>
    public PlacementSubject(StackKey key, byte w, byte h, ItemCategory category, bool carryByHand, WeightClass weightClass, int effMax)
    {
        Key = key;
        W = w;
        H = h;
        Category = category;
        CarryByHand = carryByHand;
        WeightClass = weightClass;
        EffMax = effMax;
    }

    /// <summary>
    /// A mail subject; <see cref="EffMax"/> is read from <paramref name="stats"/> now (<c>StatDefs</c> clamps it 1..255). The kind's
    /// footprint is bounded 1..255 by the content parser (field spec) and its <c>maxStack</c> by CV-GRD-04 (at least 1) and
    /// CV-GRD-01 (worst case at most 255).
    /// </summary>
    /// <param name="kind">The mail kind's def.</param>
    /// <param name="kindIndex">The kind's def ordinal (the <c>MailMaxStack</c> qualifier).</param>
    /// <param name="addressId">The opaque packed address.</param>
    /// <param name="flags">The raw flags byte.</param>
    /// <param name="stats">The stat sheet.</param>
    /// <returns>The subject.</returns>
    public static PlacementSubject ForMail(MailKindDef kind, DefIndex kindIndex, uint addressId, byte flags, IStatSheet stats) =>
        new(StackKey.ForMail(kindIndex.Value, addressId, flags), (byte)kind.GridW, (byte)kind.GridH, ItemCategory.Mail,
            kind.CarryByHand, kind.WeightClass, stats.Read(Stat.MailMaxStack, kindIndex.Value));

    /// <summary>
    /// An item subject; <see cref="EffMax"/> is the def's <c>maxStack</c> (at least 1 by CV-GRD-04; not clamped here, see
    /// <see cref="EffMax"/>). The footprint is bounded 1..255 by the content parser (field spec).
    /// </summary>
    /// <param name="item">The item's def.</param>
    /// <param name="itemIndex">The item's def ordinal.</param>
    /// <returns>The subject.</returns>
    public static PlacementSubject ForItem(ItemDef item, DefIndex itemIndex) =>
        new(StackKey.ForItem(itemIndex.Value), (byte)item.GridW, (byte)item.GridH, item.Category,
            item.CarryByHand, item.WeightClass, item.MaxStack);

    /// <summary>The subject of a stored entry, resolving its def from <paramref name="content"/>.</summary>
    /// <param name="content">The content set.</param>
    /// <param name="stats">The stat sheet (read for mail).</param>
    /// <param name="entry">The entry.</param>
    /// <returns>The subject.</returns>
    public static PlacementSubject Of(ContentSet content, IStatSheet stats, in EntryRecord entry)
    {
        if (entry.IsMail)
        {
            DefIndex kind = DefIndex.FromValue(entry.MailKind);
            return ForMail(content.MailKinds[kind], kind, entry.AddressId, entry.Flags, stats);
        }

        DefIndex item = DefIndex.FromValue(entry.ItemId);
        return ForItem(content.Items[item], item);
    }
}

/// <summary>
/// The rule-level placement verdict (story grid-inventory/006), returned instead of widening <c>InvPlacement</c>: the handler copies
/// the position into <c>InvPlan</c> and keeps <see cref="IsMerge"/> / <see cref="TargetSlot"/> for Commit. How many units a merge
/// takes (<c>min(count, effMax − target.count)</c>) is the handler's (story 008).
/// </summary>
/// <example>
/// <code>
/// if (v.IsOk &amp;&amp; v.IsMerge) { int room = v.EffMax - entries[v.TargetSlot].Count; }
/// </code>
/// </example>
public readonly struct PlacementVerdict
{
    /// <summary>0 when the placement is valid, else the earliest failing rule-level reason.</summary>
    public readonly InvReason Reason;

    /// <summary>True when the valid placement is a merge into <see cref="TargetSlot"/>; false for free cells.</summary>
    public readonly bool IsMerge;

    /// <summary>The merge target's pool slot (also the full stack on <see cref="InvReason.StackFull"/>), else <see cref="Occupancy.Free"/>.</summary>
    public readonly ushort TargetSlot;

    /// <summary>The subject's effective max stack read for this op (set on an ok and a <see cref="InvReason.StackFull"/> verdict; 0 on every other reject).</summary>
    public readonly int EffMax;

    /// <summary>Creates a verdict.</summary>
    /// <param name="reason">0 or the reason.</param>
    /// <param name="isMerge">True for a merge.</param>
    /// <param name="targetSlot">The target stack's slot or <see cref="Occupancy.Free"/>.</param>
    /// <param name="effMax">The effective max stack.</param>
    public PlacementVerdict(InvReason reason, bool isMerge, ushort targetSlot, int effMax)
    {
        Reason = reason;
        IsMerge = isMerge;
        TargetSlot = targetSlot;
        EffMax = effMax;
    }

    /// <summary>True when the placement is valid.</summary>
    public bool IsOk => Reason == 0;

    /// <summary>A rejection with no target.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The verdict.</returns>
    public static PlacementVerdict Reject(InvReason reason) => new(reason, false, Occupancy.Free, 0);

    /// <summary>A valid placement onto free cells.</summary>
    /// <param name="effMax">The effective max stack.</param>
    /// <returns>The verdict.</returns>
    public static PlacementVerdict FreeCells(int effMax) => new(0, false, Occupancy.Free, effMax);
}
