using System.Text;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ledger;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Integration.Inventory.Fuzz;

/// <summary>
/// The H-R19-1 invariants (story grid-inventory/018; GDD R19 l.763-772, M-1a subset a-e, h, i), each a separately named assertion
/// whose failure message carries the generator seed, the op index, the tick and the invariant letter. It is the world's
/// <see cref="IOpRecorder"/> (so it runs after every drained op, inside the drain), the automation stand-in's
/// <see cref="IFuzzObserver"/> (after every command) and the world's <see cref="IEventSink"/> (the <c>ItemsLost</c> and expiry records).
/// After every op it checks the O(1) ledger counters against its own model (a, b); every <see cref="FullCheckEvery"/> ops and at the
/// end it runs the brute-force full check (a-e, h); at the end, coverage (i). Reads state only.
/// </summary>
internal sealed class InventoryInvariantChecker : IOpRecorder, IFuzzObserver, IEventSink
{
    /// <summary>Ops between two full checks.</summary>
    // Mail Dev default, approved by Evan 2026-11-01 1:48 PM PT by card click (TD-206 item 3): invariants c, d, e, h (no O(1) counter at M-1a) are checked only in the full check every 100 ops and at the end; a and b after every op.
    public const int FullCheckEvery = 100;

    private readonly ulong _seed;
    private readonly HashSet<uint> _mail = new();
    private readonly long[] _attempts = new long[(int)FuzzOpType.Malformed + 1];
    private readonly long[] _accepted = new long[(int)FuzzOpType.Malformed + 1];
    private readonly List<uint> _ids = new(4096);
    private List<ushort>[] _byContainer = [];
    private readonly List<ushort> _sorted = new(512);
    private EntryOrder? _order;
    private InventoryFuzzWorld? _w;
    private long[] _itemInitial = [];
    private long[] _itemConsumed = [];
    private long[] _itemsLost = [];
    private long[] _itemUnits = [];
    private ushort[] _grid = [];
    private bool _eventOverflow;

    public InventoryInvariantChecker(ulong seed)
    {
        _seed = seed;
    }

    /// <summary>Ops checked so far (drained ops and executed commands).</summary>
    public long OpIndex { get; private set; }

    /// <summary>Full checks run so far.</summary>
    public long FullChecks { get; private set; }

    /// <summary>WorldItems that expired (mail back to the Intake or backlog, non-mail destroyed).</summary>
    public long Expired { get; private set; }

    /// <summary>Item units destroyed (<c>ItemsLost</c> records).</summary>
    public long ItemsLostUnits => _itemsLost.Sum();

    /// <summary>Starts the model from <paramref name="world"/>'s arranged items (no mail yet) and becomes its observer.</summary>
    public void Attach(InventoryFuzzWorld world)
    {
        _w = world;
        world.Automation.Observer = this;
        int defs = world.Host.Content.Items.Count;
        _itemInitial = (long[])world.InitialItemUnits.Clone();
        _itemConsumed = new long[defs];
        _itemsLost = new long[defs];
        _itemUnits = new long[defs];
        _grid = new ushort[world.Inventory.Occupancy.MaxWidth * world.Inventory.Occupancy.MaxRows];
        _order = new EntryOrder(world.Inventory.Entries);
        _byContainer = new List<ushort>[world.Inventory.Containers.Capacity];
        for (int i = 0; i < _byContainer.Length; i++)
        {
            _byContainer[i] = new List<ushort>(64);
        }
    }

    /// <summary>The model's live mail units.</summary>
    public int MailUnits => _mail.Count;

    // ------------------------------------------------------------------ hooks

    public void OnEnqueued(uint boundaryTick, in Op op)
    {
    }

    public void OnDrained(uint applyTick, in Op op, in Reply reply)
    {
        if (op.Kind != FuzzAutomationSystem.CommandKind)
        {
            AfterOp((FuzzOpType)(op.ReqId & InventoryFuzzGenerator.TypeMask), reply.IsOk, reply.Reason);
        }
    }

    public void OnSample(uint receivedTick, PlayerSlot slot, in TransformSample sample)
    {
    }

    public void OnOverflow(uint tick) => Fail('i', "coverage", "the op queue overflowed: ops were lost before they were applied");

    public void MailEntered(uint mailId)
    {
        if (!_mail.Add(mailId))
        {
            Fail('a', "conservation-mail", $"mailId {mailId} entered custody twice");
        }
    }

    public void UnitConsumed(int row, uint mailId)
    {
        if (row == UnitLedger.MailRow)
        {
            if (!_mail.Remove(mailId))
            {
                Fail('a', "conservation-mail", $"consumed mailId {mailId} was not live");
            }
        }
        else
        {
            _itemConsumed[row - 1]++;
        }
    }

    public void CommandExecuted(FuzzOpType type, in FuzzCommandPayload command, bool accepted) => AfterOp(type, accepted, 0);

    public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow)
    {
        _eventOverflow |= overflow.Overflow;
        foreach (ref readonly EventEntry e in entries)
        {
            if (e.Kind == InvEventKinds.ItemsLost)
            {
                ItemsLostPayload p = e.PayloadAs<ItemsLostPayload>();
                _itemsLost[p.ItemId] += p.Count;
            }
            else if (e.Kind == InvEventKinds.WorldItemDespawned && e.PayloadAs<WorldItemDespawnedPayload>().Cause == (byte)InvCause.Expired)
            {
                Expired++;
            }
        }
    }

    /// <summary>After each tick: the production tick-end check found nothing, and the ledger's destroyed units are the ItemsLost records (b).</summary>
    public void AfterTick()
    {
        InventoryFuzzWorld w = World;
        if (w.Host.Services.Counters[CounterId.InventoryConservationMismatches] != 0)
        {
            Fail(FirstMismatchRow() == UnitLedger.MailRow ? 'a' : 'b', "conservation", "the tick-end conservation check reported a mismatch");
        }

        if (_eventOverflow)
        {
            Fail('b', "conservation-items", "the event log overflowed, so ItemsLost cannot be reconciled");
        }

        UnitLedger ledger = w.Inventory.Ledger;
        for (int d = 0; d < _itemsLost.Length; d++)
        {
            if (ledger.Destroyed(d + 1) != _itemsLost[d])
            {
                Fail('b', "conservation-items", $"item def {d}: ledger destroyed {ledger.Destroyed(d + 1)} != ItemsLost units {_itemsLost[d]}");
            }
        }
    }

    // ------------------------------------------------------------------ checks

    private InventoryFuzzWorld World => _w ?? throw new InvalidOperationException("Attach the checker first.");

    private void AfterOp(FuzzOpType type, bool accepted, ushort reason)
    {
        OpIndex++;
        if (type == FuzzOpType.Malformed)
        {
            if (accepted || reason != CoreReason.BadRequest)
            {
                Fail('i', "coverage", $"a malformed op was answered {(accepted ? "ok" : "reason " + reason)}, not bad_request");
            }
        }
        else if (type != FuzzOpType.Setup)
        {
            _attempts[(int)type]++;
            _accepted[(int)type] += accepted ? 1 : 0;
        }

        CheckCounters();
        if (OpIndex % FullCheckEvery == 0)
        {
            FullCheck();
        }
    }

    /// <summary>The O(1) counters after every op: the ledger rows against the model (a, b).</summary>
    private void CheckCounters()
    {
        UnitLedger ledger = World.Inventory.Ledger;
        long live = ledger.LiveSum(UnitLedger.MailRow);
        if (live != _mail.Count || ledger.Expected(UnitLedger.MailRow) != _mail.Count || ledger.Destroyed(UnitLedger.MailRow) != 0)
        {
            Fail('a', "conservation-mail", $"ledger live {live}, expected {ledger.Expected(UnitLedger.MailRow)}, destroyed {ledger.Destroyed(UnitLedger.MailRow)}; model {_mail.Count} (initial + spawned - consumed)");
        }

        for (int row = 0; row < ledger.Rows; row++)
        {
            for (int s = 0; s < UnitLedger.LiveStateCount; s++)
            {
                if (ledger.Live(row, (LedgerState)s) < 0)
                {
                    Fail(row == UnitLedger.MailRow ? 'a' : 'b', "conservation", $"row {row} state {(LedgerState)s} is negative");
                }
            }

            if (row == UnitLedger.MailRow)
            {
                continue;
            }

            long expected = _itemInitial[row - 1] - _itemConsumed[row - 1] - ledger.Destroyed(row);
            if (ledger.LiveSum(row) != expected || ledger.Expected(row) != expected)
            {
                Fail('b', "conservation-items", $"item def {row - 1}: ledger live {ledger.LiveSum(row)}, expected {ledger.Expected(row)}; model {expected} (initial + granted - consumed - destroyed)");
            }
        }
    }

    /// <summary>The brute-force full check: every live entry and container recounted from storage (a-e, h).</summary>
    public void FullCheck()
    {
        FullChecks++;
        InventoryFuzzWorld w = World;
        InventorySystem inv = w.Inventory;
        EntryPool pool = inv.Entries;
        MailIdArena arena = inv.MailIds;
        ContainerTable containers = inv.Containers;
        _ids.Clear();
        Array.Clear(_itemUnits);
        for (int c = 0; c < containers.LiveCount; c++)
        {
            _byContainer[c].Clear();
        }

        for (int s = 0; s < pool.Capacity; s++)
        {
            ref readonly EntryRecord e = ref pool[(ushort)s];
            if (!e.IsLive)
            {
                continue;
            }

            int effMax = EffMax(in e);
            if (e.Count < 1 || e.Count > effMax)
            {
                Fail('c', "bounds", $"entry {e.Id.Value} has count {e.Count}, outside 1..{effMax}");
            }

            if (e.IsMail)
            {
                ReadOnlySpan<uint> run = arena.Read(in e.MailIds);
                if (run.Length != e.Count)
                {
                    Fail('a', "conservation-mail", $"entry {e.Id.Value} holds {run.Length} mailIds for count {e.Count}");
                }

                foreach (uint id in run)
                {
                    _ids.Add(id);
                }
            }
            else
            {
                _itemUnits[e.ItemId] += e.Count;
            }

            if (e.ContainerSlot != EntryRecord.NoContainer)
            {
                _byContainer[e.ContainerSlot].Add((ushort)s);
            }
        }

        CheckMailMultiset();
        CheckItemUnits(inv.Ledger);
        for (ushort c = 0; c < containers.LiveCount; c++)
        {
            CheckContainer(inv, c);
        }

        CheckIndexes(inv);
    }

    private void CheckMailMultiset()
    {
        _ids.Sort();
        for (int i = 1; i < _ids.Count; i++)
        {
            if (_ids[i] == _ids[i - 1])
            {
                Fail('a', "conservation-mail", $"mailId {_ids[i]} is held twice");
            }
        }

        if (_ids.Count != _mail.Count)
        {
            Fail('a', "conservation-mail", $"{_ids.Count} mailIds are held, the model has {_mail.Count} (initial + spawned - consumed)");
        }

        foreach (uint id in _ids)
        {
            if (!_mail.Contains(id))
            {
                Fail('a', "conservation-mail", $"mailId {id} is held but never entered custody (or was consumed)");
            }
        }
    }

    private void CheckItemUnits(UnitLedger ledger)
    {
        for (int d = 0; d < _itemUnits.Length; d++)
        {
            long expected = _itemInitial[d] - _itemConsumed[d] - ledger.Destroyed(d + 1);
            if (_itemUnits[d] != expected)
            {
                Fail('b', "conservation-items", $"item def {d}: {_itemUnits[d]} units held, expected {expected} (initial + granted - consumed - ItemsLost)");
            }
        }
    }

    /// <summary>In-bounds (e) and no-overlap (d) of every placed entry, rasterised from the entries and compared with the occupancy grid and the header counts.</summary>
    private void CheckContainer(InventorySystem inv, ushort c)
    {
        ContainerHeader h = inv.Containers[c];
        List<ushort> list = _byContainer[c];
        Span<ushort> grid = _grid.AsSpan(0, h.W * h.H);
        grid.Fill(Occupancy.Free);
        foreach (ushort s in list)
        {
            ref readonly EntryRecord e = ref inv.Entries[s];
            int fw = 1;
            int fh = 1;
            if (h.Layout != ContainerLayout.Slots)
            {
                Footprint.Effective(e.W, e.H, e.Rotated, out fw, out fh);
            }

            if (e.X + fw > h.W || e.Y + fh > h.H)
            {
                Fail('e', "in-bounds", $"entry {e.Id.Value} at ({e.X},{e.Y}) {fw}x{fh} leaves container {h.Id.Value} ({h.W}x{h.H})");
            }

            for (int y = e.Y; y < e.Y + fh; y++)
            {
                for (int x = e.X; x < e.X + fw; x++)
                {
                    ref ushort cell = ref grid[(y * h.W) + x];
                    if (cell != Occupancy.Free)
                    {
                        Fail('d', "no-overlap", $"entries {inv.Entries[cell].Id.Value} and {e.Id.Value} both cover ({x},{y}) of container {h.Id.Value}");
                    }

                    cell = s;
                }
            }
        }

        int free = 0;
        for (int y = 0; y < h.H; y++)
        {
            for (int x = 0; x < h.W; x++)
            {
                ushort expected = grid[(y * h.W) + x];
                free += expected == Occupancy.Free ? 1 : 0;
                if (inv.Occupancy.OwnerAt(c, h.W, x, y) != expected)
                {
                    Fail('d', "no-overlap", $"container {h.Id.Value} cell ({x},{y}): occupancy owner {inv.Occupancy.OwnerAt(c, h.W, x, y)}, entries say {expected}");
                }
            }
        }

        if (h.FreeCells != free || h.EntryCount != list.Count)
        {
            Fail('d', "no-overlap", $"container {h.Id.Value}: header free {h.FreeCells} / entries {h.EntryCount}, recount {free} / {list.Count}");
        }
    }

    /// <summary>Index consistency (h): <c>PeekOutfeed</c>, <c>CountItem</c> and the StackKey index against brute-force scans.</summary>
    private void CheckIndexes(InventorySystem inv)
    {
        InventoryFuzzWorld w = World;
        CheckOutfeed(inv, inv.IntakeContainer);
        CheckOutfeed(inv, w.Shared);
        StackIndex index = inv.Mutator.StackIndex;
        int linked = 0;
        Span<ContainerId> one = stackalloc ContainerId[1];
        for (ushort c = 0; c < inv.Containers.LiveCount; c++)
        {
            List<ushort> list = _byContainer[c];
            one[0] = inv.Containers[c].Id;
            for (int d = 0; d < _itemUnits.Length; d++)
            {
                int brute = 0;
                foreach (ushort s in list)
                {
                    brute += !inv.Entries[s].IsMail && inv.Entries[s].ItemId == d ? inv.Entries[s].Count : 0;
                }

                int counted = inv.CountItem(one, DefIndex.FromValue((ushort)d));
                if (counted != brute)
                {
                    Fail('h', "index-consistency", $"CountItem(container {one[0].Value}, def {d}) = {counted}, brute force {brute}");
                }
            }

            // The StackKey index: per key, the non-full entries in row-major order (y, then x).
            _sorted.Clear();
            _sorted.AddRange(list);
            _sorted.Sort(_order);
            for (int i = 0; i < _sorted.Count;)
            {
                StackKey key = StackKey.Of(in inv.Entries[_sorted[i]]);
                ushort at = index.Head(c, key);
                for (; i < _sorted.Count && StackKey.Of(in inv.Entries[_sorted[i]]) == key; i++)
                {
                    ushort s = _sorted[i];
                    if (inv.Entries[s].Count >= EffMax(in inv.Entries[s]))
                    {
                        continue;
                    }

                    if (at != s)
                    {
                        Fail('h', "index-consistency", $"container {one[0].Value} key 0x{key.Raw:X}: the StackKey index lists slot {at}, brute force (non-full, row-major) slot {s}");
                    }

                    linked++;
                    at = index.Next(at);
                }

                if (at != StackIndex.None)
                {
                    Fail('h', "index-consistency", $"container {one[0].Value} key 0x{key.Raw:X}: the StackKey index lists slot {at} past the last non-full entry");
                }
            }
        }

        if (index.LinkedCount != linked)
        {
            Fail('h', "index-consistency", $"the StackKey index links {index.LinkedCount} entries, brute force finds {linked} non-full placed entries");
        }
    }

    private void CheckOutfeed(InventorySystem inv, ContainerId id)
    {
        _ = inv.Containers.TryGetSlot(id, out ushort c);
        bool hasOutfeed = id == inv.IntakeContainer;
        ushort head = Occupancy.Free;
        foreach (ushort s in _byContainer[c])
        {
            if (head == Occupancy.Free || inv.Entries[s].ArrivalSeq < inv.Entries[head].ArrivalSeq)
            {
                head = s;
            }
        }

        bool found = inv.PeekOutfeed(id, Selector.Fifo, out UnitRef u);
        if (!hasOutfeed || head == Occupancy.Free)
        {
            if (found)
            {
                Fail('h', "index-consistency", $"PeekOutfeed(container {id.Value}) found mailId {u.Mail.Value}, brute force finds no outfeed unit");
            }

            return;
        }

        ref readonly EntryRecord e = ref inv.Entries[head];
        uint oldest = inv.MailIds.Read(in e.MailIds)[0];
        if (!found || u.Mail.Value != oldest || u.Entry != e.Id)
        {
            Fail('h', "index-consistency", $"PeekOutfeed(Intake) = {(found ? $"mailId {u.Mail.Value} of entry {u.Entry.Value}" : "none")}, brute force mailId {oldest} of entry {e.Id.Value} (lowest arrivalSeq)");
        }
    }

    private int EffMax(in EntryRecord e)
    {
        ContentSet content = World.Host.Content;
        int max = e.IsMail ? content.MailKinds[DefIndex.FromValue(e.MailKind)].MaxStack : content.Items[DefIndex.FromValue(e.ItemId)].MaxStack;
        return Math.Min(max, byte.MaxValue);
    }

    private int FirstMismatchRow()
    {
        SimCheckRing ring = World.Host.Services.CheckRing;
        int row = -1;
        for (int i = 0; i < ring.Count; i++)
        {
            if (ring[i].Code == (ushort)CheckCode.InventoryConservationMismatch)
            {
                row = (int)(ring[i].A & 0xFF_FFFF); // the newest record: the counter was 0 until this tick
            }
        }

        return row;
    }

    // ------------------------------------------------------------------ end of run

    /// <summary>Coverage (i): at least 5 % of each well-formed op type's attempts were accepted, at least half the ops named the shared chest, and some WorldItem expired.</summary>
    public void CheckCoverage(InventoryFuzzGenerator generator)
    {
        for (var t = FuzzOpType.Pick; t < FuzzOpType.Malformed; t++)
        {
            if (_attempts[(int)t] == 0 || _accepted[(int)t] * 20 < _attempts[(int)t])
            {
                Fail('i', "coverage", $"{t}: {_accepted[(int)t]} of {_attempts[(int)t]} attempts accepted (needs >= 5 %)");
            }
        }

        if (generator.SharedTargeted * 2 < generator.Generated)
        {
            Fail('i', "coverage", $"only {generator.SharedTargeted} of {generator.Generated} ops named the shared chest (needs >= 50 %)");
        }

        if (Expired == 0)
        {
            Fail('i', "coverage", "no WorldItem expired");
        }
    }

    /// <summary>The run's figures, for the test output.</summary>
    public string Report(InventoryFuzzGenerator generator)
    {
        var sb = new StringBuilder();
        sb.Append($"seed 0x{_seed:X16}: {OpIndex} ops checked, {FullChecks} full checks, {MailUnits} mail units live, {Expired} WorldItems expired, {ItemsLostUnits} item units lost\n");
        sb.Append($"shared chest named by {generator.SharedTargeted} of {generator.Generated} fuzz ops ({100.0 * generator.SharedTargeted / Math.Max(1, generator.Generated):F1} %)\n");
        for (var t = FuzzOpType.Pick; t < FuzzOpType.Malformed; t++)
        {
            sb.Append($"  {t,-16} {_accepted[(int)t],8} / {_attempts[(int)t],8} accepted ({100.0 * _accepted[(int)t] / Math.Max(1, _attempts[(int)t]):F1} %)\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// The first failure, kept because a check that fails inside a tick throws through <c>SimWorld.Tick</c>, which in Release swallows
    /// the exception and faults the world (the run loop reports it from here).
    /// </summary>
    public string? FirstFailure { get; private set; }

    private void Fail(char letter, string name, string detail)
    {
        string message = $"H-R19-1 seed=0x{_seed:X16} op={OpIndex} tick={_w?.World.CurrentTick} invariant {letter} ({name}): {detail}";
        FirstFailure ??= message;
        Assert.Fail(message);
    }

    /// <summary>Entries by StackKey, then row-major position (y, then x).</summary>
    private sealed class EntryOrder : IComparer<ushort>
    {
        private readonly EntryPool _pool;

        public EntryOrder(EntryPool pool) => _pool = pool;

        public int Compare(ushort a, ushort b)
        {
            ref readonly EntryRecord x = ref _pool[a];
            ref readonly EntryRecord y = ref _pool[b];
            int byKey = StackKey.Of(in x).Raw.CompareTo(StackKey.Of(in y).Raw);
            return byKey != 0 ? byKey : ((x.Y << 8) | x.X).CompareTo((y.Y << 8) | y.X);
        }
    }
}
