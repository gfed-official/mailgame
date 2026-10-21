using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/009 property tests (GDD R8; ADR-0009 section 5; TR-inventory-007): over 10 000 seeded random mutator calls
/// (<c>Place</c>, <c>Remove</c>, <c>Transfer</c>, <c>MergeInto</c>, and AutoPlace through the TEST-ONLY op) on four containers (two
/// grids, the 8 x 2 inventory, the slots hotbar), with the stat sheet constant:
/// <list type="bullet">
/// <item>after every call the StackKey index equals a brute-force row-major scan of the non-full stacks (<c>count &lt; effMax</c>), per
/// container and key, and has no other heads or links;</item>
/// <item>every AutoPlace plan equals a brute-force oracle (cell-scan stacks for PASS M, cell-owner first fit for PASS U / R), PASS M
/// visits at most the summed list lengths of the admitted targets, and PASS U / R count exactly one probe per footprint cell of each
/// anchor scanned (every no-fit scan's anchors plus the found anchor's index, times the footprint area; one per slot);</item>
/// <item>the same sequence with the negative-fit cache disabled gives identical plans, replies and state.</item>
/// </list>
/// The generator is a fixed xorshift (no <c>System.Random</c>), so a failure reproduces from its seed and call number.
/// </summary>
public class StackIndexPropertyTests
{
    private const int Calls = 10_000;
    private const uint Seed = 0x9E37_79B9u;

    private static readonly string[] _mailKinds = { "letter", "letter", "letter", "postcard", "small", "medium" };
    private static readonly string[] _itemKinds = { "plank", "log", "axe" };

    [Fact]
    [Trait("Id", "TR-inventory-007")]
    public void TenThousandRandomCalls_IndexEqualsBruteForceScanOfNonFullStacks_RowMajor_AutoPlaceMatchesTheOracle_ProbesBounded()
    {
        // Arrange
        var driver = new Driver(new InvAutoPlaceRig(), Seed, checkEveryCall: true);

        // Act
        driver.Run(Calls);

        // Assert: the per-call checks ran; the mix exercised every kind of call
        Assert.Equal(Calls, driver.CallsRun);
        Assert.True(driver.Places > 1000, $"places {driver.Places}");
        Assert.True(driver.Removes > 500, $"removes {driver.Removes}");
        Assert.True(driver.Transfers > 500, $"transfers {driver.Transfers}");
        Assert.True(driver.Merges > 300, $"merges {driver.Merges}");
        Assert.True(driver.AutoPlaces > 1000, $"autoplaces {driver.AutoPlaces}");
        Assert.True(driver.AutoPlaceMerges > 100, $"autoplace merges {driver.AutoPlaceMerges}");
        Assert.True(driver.AutoPlaceNoRoom > 50, $"no_room {driver.AutoPlaceNoRoom}");
        Assert.True(driver.MaxListLength >= 3, $"max list {driver.MaxListLength}");
    }

    [Fact]
    [Trait("Id", "TR-inventory-007")]
    public void TheSameTenThousandCallSequence_WithTheNegativeFitCacheDisabled_GivesIdenticalPlansRepliesAndState()
    {
        // Arrange
        var cached = new Driver(new InvAutoPlaceRig(), Seed, checkEveryCall: false);
        var uncachedRig = new InvAutoPlaceRig();
        uncachedRig.Inventory.AutoPlace.NoFitCache.Enabled = false;
        var uncached = new Driver(uncachedRig, Seed, checkEveryCall: false);

        // Act
        cached.Run(Calls);
        uncached.Run(Calls);

        // Assert
        Assert.Equal(uncached.Trace.Count, cached.Trace.Count);
        for (int i = 0; i < cached.Trace.Count; i++)
        {
            Assert.True(uncached.Trace[i] == cached.Trace[i], $"call {i}: cached {cached.Trace[i]} vs uncached {uncached.Trace[i]}");
        }

        Assert.Equal(AutoPlaceTests.StateOf(uncached.Rig), AutoPlaceTests.StateOf(cached.Rig));
        Assert.True(cached.Probes < uncached.Probes, $"cached {cached.Probes} uncached {uncached.Probes}");
    }

    /// <summary>One AutoPlace call's observable outcome.</summary>
    private readonly record struct Outcome(int Call, ushort Reason, string Merges, string Placement, int Unplaced);

    /// <summary>The seeded call generator and its checks.</summary>
    private sealed class Driver
    {
        private readonly bool _check;
        private readonly ushort[] _containers;
        private uint _state;
        private uint _nextMailId = 1;

        public Driver(InvAutoPlaceRig rig, uint seed, bool checkEveryCall)
        {
            Rig = rig;
            _state = seed;
            _check = checkEveryCall;
            _containers = new[] { rig.CreateGrid(8, 4), rig.CreateGrid(6, 3), rig.InventoryA, rig.HotbarA };
        }

        public InvAutoPlaceRig Rig { get; }

        public List<Outcome> Trace { get; } = new();

        public int CallsRun { get; private set; }

        public int Places { get; private set; }

        public int Removes { get; private set; }

        public int Transfers { get; private set; }

        public int Merges { get; private set; }

        public int AutoPlaces { get; private set; }

        public int AutoPlaceMerges { get; private set; }

        public int AutoPlaceNoRoom { get; private set; }

        public int MaxListLength { get; private set; }

        public long Probes => Rig.Counters[CounterId.InventoryPlacementProbes];

        public void Run(int calls)
        {
            for (int call = 0; call < calls; call++)
            {
                int live = PlacedEntries().Count;
                int roll = Next(100);
                if (live > 70 && roll < 40)
                {
                    roll = 30; // keep the population bounded: bias to Remove
                }

                if (roll < 25)
                {
                    DoPlace();
                }
                else if (roll < 40)
                {
                    DoRemove();
                }
                else if (roll < 60)
                {
                    DoTransfer();
                }
                else if (roll < 75)
                {
                    DoMerge();
                }
                else
                {
                    DoAutoPlace(call);
                }

                if (call % 1000 == 999)
                {
                    Rig.Inventory.Mutator.RebuildIndex(_containers[Next(_containers.Length)]); // a rebuild is idempotent
                }

                if (_check)
                {
                    AssertIndexMatchesScan(call);
                }

                CallsRun++;
            }
        }

        private uint NextRaw()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        private int Next(int bound) => (int)(NextRaw() % (uint)bound);

        // ---------------------------------------------------------------- calls

        private ushort NewDetached(int maxCount)
        {
            if (Next(4) == 0)
            {
                string item = _itemKinds[Next(_itemKinds.Length)];
                int effMax = Rig.Item(item).EffMax;
                return Rig.DetachedItem(item, (byte)(1 + Next(Math.Min(effMax, maxCount))));
            }

            string kind = _mailKinds[Next(_mailKinds.Length)];
            uint address = 1u + (uint)Next(3);
            int max = Rig.Mail(kind, address).EffMax;
            int count = 1 + Next(Math.Min(max, maxCount));
            uint[] ids = InvAutoPlaceRig.Ids(_nextMailId, count);
            _nextMailId += (uint)count + (uint)Next(3); // gaps make several runs per stack after merges
            return Rig.DetachedMail(kind, address, ids);
        }

        private void DoPlace()
        {
            ushort entry = NewDetached(20);
            ushort container = _containers[Next(_containers.Length)];
            if (!TryFindFit(entry, container, Next(2) == 1, out int x, out int y, out bool rotated))
            {
                Rig.FreeDetached(entry);
                return;
            }

            Rig.PlaceDetached(entry, container, x, y, rotated);
            Places++;
        }

        private void DoRemove()
        {
            List<ushort> placed = PlacedEntries();
            if (placed.Count == 0)
            {
                return;
            }

            ushort entry = placed[Next(placed.Count)];
            Reply r = Rig.Step(
                (ref InvPlan p) => p.Touch(Rig.Entries[entry].ContainerSlot) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => Rig.Inventory.Mutator.Remove(ref p, entry));
            Assert.True(r.IsOk);
            Rig.FreeDetached(entry);
            Removes++;
        }

        private void DoTransfer()
        {
            List<ushort> placed = PlacedEntries();
            if (placed.Count == 0)
            {
                return;
            }

            ushort entry = placed[Next(placed.Count)];
            ushort from = Rig.Entries[entry].ContainerSlot;
            ushort to = _containers[Next(_containers.Length)];
            if (!TryFindFit(entry, to, Next(2) == 1, out int x, out int y, out bool rotated))
            {
                return;
            }

            Reply r = Rig.Step(
                (ref InvPlan p) => p.Touch(from) < 0 || p.Touch(to) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => Rig.Inventory.Mutator.Transfer(ref p, entry, to, (byte)x, (byte)y, rotated));
            Assert.True(r.IsOk);
            Transfers++;
        }

        private void DoMerge()
        {
            List<ushort> placed = PlacedEntries();
            if (placed.Count < 2)
            {
                return;
            }

            ushort source = placed[Next(placed.Count)];
            StackKey key = StackKey.Of(in Rig.Entries[source]);
            int effMax = Rig.Inventory.Mutator.EffMaxOf(in Rig.Entries[source]);
            var candidates = placed.Where(t => t != source && StackKey.Of(in Rig.Entries[t]) == key && Rig.Entries[t].Count < effMax).ToList();
            if (candidates.Count == 0)
            {
                return;
            }

            ushort target = candidates[Next(candidates.Count)];
            int room = effMax - Rig.Entries[target].Count;
            int units = 1 + Next(Math.Min(room, Rig.Entries[source].Count));
            ushort from = Rig.Entries[source].ContainerSlot;
            ushort to = Rig.Entries[target].ContainerSlot;
            Reply r = Rig.Step(
                (ref InvPlan p) => p.Touch(from) < 0 || p.Touch(to) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => Rig.Inventory.Mutator.MergeInto(ref p, source, target, units));
            Assert.True(r.IsOk);
            Merges++;
        }

        private void DoAutoPlace(int call)
        {
            bool placedSource = Next(3) == 0 && PlacedEntries().Count > 0;
            ushort source = placedSource ? PlacedEntries()[Next(PlacedEntries().Count)] : NewDetached(20);
            PlacementSubject subject = Rig.SubjectOf(source);
            int units = Next(4) == 0 ? 1 + Next(Rig.Entries[source].Count) : Rig.Entries[source].Count;
            int targetCount = 1 + Next(3);
            var targets = new ushort[targetCount];
            for (int i = 0; i < targetCount; i++)
            {
                targets[i] = _containers[Next(_containers.Length)];
            }

            targets = targets.Distinct().ToArray();
            MergePolicy policy = Next(5) == 0 ? MergePolicy.NoMerge : MergePolicy.Merge;
            PlaceOrder order = Next(3) == 0 ? PlaceOrder.TargetFirst : PlaceOrder.PassFirst;
            OraclePlan expected = _check ? Oracle(subject, units, source, targets, policy, order) : default;
            int listLengths = _check && policy == MergePolicy.Merge ? targets.Sum(t => AutoPlaceTests.ListOf(Rig, t, subject.Key).Count) : 0;
            long m0 = Rig.Counters[CounterId.InventoryMergeCandidates];
            long p0 = Rig.Counters[CounterId.InventoryPlacementProbes];

            ushort reason = Rig.AutoPlace(in subject, units, source, policy, order, targets);
            AutoPlacePlan plan = Rig.LastPlan;
            string merges = string.Join(",", Enumerable.Range(0, plan.MergeCount).Select(i => $"{plan.Merge(i).TargetEntrySlot}:{plan.Merge(i).Units}"));
            InvPlacement pl = plan.Placement;
            string placement = pl.IsSet ? $"{pl.ContainerSlot}@{pl.X},{pl.Y},{pl.Rotated}" : "-";
            Trace.Add(new Outcome(call, reason, merges, placement, plan.Unplaced));
            AutoPlaces++;
            AutoPlaceMerges += plan.MergeCount;
            AutoPlaceNoRoom += reason == (ushort)InvReason.NoRoom ? 1 : 0;

            if (_check)
            {
                Assert.True(expected.Reason == reason, $"call {call}: reason {reason}, oracle {expected.Reason}");
                if (reason == 0 || reason == (ushort)InvReason.NoRoom)
                {
                    Assert.True(expected.Merges == merges, $"call {call}: merges {merges}, oracle {expected.Merges}");
                    Assert.True(expected.Placement == placement, $"call {call}: placement {placement}, oracle {expected.Placement}");
                    Assert.Equal(expected.Unplaced, plan.Unplaced);
                }

                long visits = Rig.Counters[CounterId.InventoryMergeCandidates] - m0;
                Assert.True(visits <= listLengths, $"call {call}: {visits} PASS M visits > list lengths {listLengths}");
                Assert.True(visits >= expected.MergeVisits, $"call {call}: {visits} PASS M visits < oracle {expected.MergeVisits}");
                Assert.True(visits <= expected.MergeVisits, $"call {call}: {visits} PASS M visits > oracle {expected.MergeVisits}");
                Assert.Equal(ExpectedProbes(in plan), Rig.Counters[CounterId.InventoryPlacementProbes] - p0);
            }

            if (Rig.Entries[source].IsLive && Rig.Entries[source].ContainerSlot == EntryRecord.NoContainer)
            {
                Rig.FreeDetached(source);
            }
        }

        // ---------------------------------------------------------------- brute-force views

        private List<ushort> PlacedEntries()
        {
            var list = new List<ushort>();
            foreach (ushort c in _containers)
            {
                list.AddRange(TopLefts(c));
            }

            return list;
        }

        /// <summary>The container's entries in row-major order of their top-left cells, by cell scan.</summary>
        private List<ushort> TopLefts(ushort container)
        {
            var list = new List<ushort>();
            ContainerHeader h = Rig.Header(container);
            for (int y = 0; y < h.H; y++)
            {
                for (int x = 0; x < h.W; x++)
                {
                    ushort e = Rig.EntryAt(container, x, y);
                    if (e != Occupancy.Free)
                    {
                        list.Add(e);
                    }
                }
            }

            return list;
        }

        private bool CellsFree(ushort container, int x, int y, int fw, int fh, ushort self)
        {
            ContainerHeader h = Rig.Header(container);
            if (x < 0 || y < 0 || x + fw > h.W || y + fh > h.H)
            {
                return false;
            }

            for (int r = 0; r < fh; r++)
            {
                for (int c = 0; c < fw; c++)
                {
                    ushort owner = Rig.Inventory.Occupancy.OwnerAt(container, h.W, x + c, y + r);
                    if (owner != Occupancy.Free && owner != self)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool TryFindFit(ushort entry, ushort container, bool rotated, out int foundX, out int foundY, out bool foundRotated)
        {
            ContainerHeader h = Rig.Header(container);
            EntryRecord e = Rig.Entries[entry];
            bool slots = h.Layout == ContainerLayout.Slots;
            int fw = slots ? 1 : rotated ? e.H : e.W;
            int fh = slots ? 1 : rotated ? e.W : e.H;
            ushort self = e.ContainerSlot == container ? entry : Occupancy.Free;
            int start = Next(h.W * h.H);
            for (int k = 0; k < h.W * h.H; k++)
            {
                int i = (start + k) % (h.W * h.H);
                if (CellsFree(container, i % h.W, i / h.W, fw, fh, self))
                {
                    foundX = i % h.W;
                    foundY = i / h.W;
                    foundRotated = rotated && !slots;
                    return true;
                }
            }

            foundX = 0;
            foundY = 0;
            foundRotated = false;
            return false;
        }

        private void AssertIndexMatchesScan(int call)
        {
            int links = 0;
            int heads = 0;
            foreach (ushort c in _containers)
            {
                var expected = new Dictionary<ulong, List<ushort>>();
                var keyOrder = new List<ulong>();
                foreach (ushort e in TopLefts(c))
                {
                    ref EntryRecord r = ref Rig.Entries[e];
                    if (r.Count >= Rig.Inventory.Mutator.EffMaxOf(in r))
                    {
                        Assert.False(Rig.Index.Contains(e), $"call {call}: full entry {e} is linked");
                        continue;
                    }

                    ulong key = StackKey.Of(in r).Raw;
                    if (!expected.TryGetValue(key, out List<ushort>? list))
                    {
                        expected[key] = list = new List<ushort>();
                        keyOrder.Add(key);
                    }

                    list.Add(e);
                }

                foreach (ulong key in keyOrder)
                {
                    List<ushort> actual = AutoPlaceTests.ListOf(Rig, c, KeyOf(expected[key][0]));
                    Assert.True(expected[key].SequenceEqual(actual), $"call {call}: container {c} key {key:X}: index [{string.Join(",", actual)}] vs scan [{string.Join(",", expected[key])}]");
                    links += actual.Count;
                    MaxListLength = Math.Max(MaxListLength, actual.Count);
                }

                heads += keyOrder.Count;
            }

            Assert.Equal(links, Rig.Index.LinkedCount);
            Assert.Equal(heads, Rig.Index.HeadCount);
        }

        private StackKey KeyOf(ushort entry) => StackKey.Of(in Rig.Entries[entry]);

        // ---------------------------------------------------------------- oracle

        /// <summary>The oracle's plan; <see cref="MergeVisits"/> is the list nodes PASS M walks (the non-full same-key stacks it reaches before the units run out).</summary>
        private readonly record struct OraclePlan(ushort Reason, string Merges, string Placement, int Unplaced, long MergeVisits);

        /// <summary>GDD R8 by brute force: stacks from a row-major cell scan, free cells from cell owners. No index, no cache, no masks.</summary>
        private OraclePlan Oracle(in PlacementSubject s, int units, ushort source, ushort[] targets, MergePolicy policy, PlaceOrder order)
        {
            var admitted = new bool[targets.Length];
            bool any = false;
            bool carry = false;
            for (int i = 0; i < targets.Length; i++)
            {
                InvReason v = InsertionWhitelist.Check(Rig.Content.Containers[Rig.Header(targets[i]).DefIndex], in s);
                admitted[i] = v == 0;
                any |= admitted[i];
                carry |= v == InvReason.NotCarryable;
            }

            if (!any)
            {
                return new OraclePlan((ushort)(carry ? InvReason.NotCarryable : InvReason.NotAllowed), string.Empty, "-", units, 0);
            }

            int sourceCount = Rig.Entries[source].Count;
            int remaining = units;
            var merges = new List<string>();
            string placement = "-";
            bool merging = policy == MergePolicy.Merge && s.EffMax > 1;
            int placeUnits = 0;
            long visits = 0;
            PlacementSubject subject = s;

            int PlacementUnits() => units == sourceCount && remaining <= Math.Max(1, subject.EffMax) ? remaining : 0;

            void PassM(ushort t)
            {
                ContainerDef def = Rig.Content.Containers[Rig.Header(t).DefIndex];
                foreach (ushort e in TopLefts(t))
                {
                    if (remaining == 0)
                    {
                        return;
                    }

                    ref EntryRecord r = ref Rig.Entries[e];
                    if (StackKey.Of(in r).Raw == subject.Key.Raw && r.Count < Rig.Inventory.Mutator.EffMaxOf(in r))
                    {
                        visits++; // a node of the key's non-full list (the brute-force list AssertIndexMatchesScan checks)
                    }

                    if (e == source || r.Count >= subject.EffMax || !PlacementRules.CanMerge(subject.Key, StackKey.Of(in r), subject.EffMax, def, subject.Category))
                    {
                        continue;
                    }

                    int take = Math.Min(remaining, subject.EffMax - r.Count);
                    merges.Add($"{e}:{take}");
                    remaining -= take;
                }
            }

            bool PassFree(ushort t, bool rotated)
            {
                ContainerHeader h = Rig.Header(t);
                if (h.Layout == ContainerLayout.Slots)
                {
                    if (rotated)
                    {
                        return false;
                    }

                    ContainerDef def = Rig.Content.Containers[h.DefIndex];
                    for (int i = 1; i < h.W * h.H; i++)
                    {
                        if (!PlacementRules.IsReservedSlot(def, h.W, h.H, i % h.W, i / h.W) && CellsFree(t, i % h.W, i / h.W, 1, 1, Occupancy.Free))
                        {
                            placement = $"{t}@{i % h.W},{i / h.W},False";
                            return true;
                        }
                    }

                    return false;
                }

                if (rotated && subject.W == subject.H)
                {
                    return false;
                }

                int fw = rotated ? subject.H : subject.W;
                int fh = rotated ? subject.W : subject.H;
                for (int y = 0; y + fh <= h.H; y++)
                {
                    for (int x = 0; x + fw <= h.W; x++)
                    {
                        if (CellsFree(t, x, y, fw, fh, Occupancy.Free))
                        {
                            placement = $"{t}@{x},{y},{rotated}";
                            return true;
                        }
                    }
                }

                return false;
            }

            bool TryFree(ushort t, bool rotated)
            {
                int n = PlacementUnits();
                if (remaining == 0 || placement != "-" || n == 0 || !PassFree(t, rotated))
                {
                    return false;
                }

                placeUnits = n;
                remaining -= n;
                return true;
            }

            if (order == PlaceOrder.PassFirst)
            {
                for (int i = 0; i < targets.Length && merging; i++)
                {
                    if (admitted[i])
                    {
                        PassM(targets[i]);
                    }
                }

                for (int r = 0; r < 2; r++)
                {
                    for (int i = 0; i < targets.Length; i++)
                    {
                        if (admitted[i])
                        {
                            _ = TryFree(targets[i], r == 1);
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < targets.Length && remaining > 0 && placement == "-"; i++)
                {
                    if (!admitted[i])
                    {
                        continue;
                    }

                    if (merging)
                    {
                        PassM(targets[i]);
                    }

                    _ = TryFree(targets[i], false) || TryFree(targets[i], true);
                }
            }

            int moved = units - remaining;
            return new OraclePlan(moved == 0 ? (ushort)InvReason.NoRoom : (ushort)0, string.Join(",", merges), placement, remaining, visits);
        }

        /// <summary>
        /// The cells the plan's scans examined: every no-fit scan's anchors and the found anchor's index in its scan, each anchor
        /// times its footprint area (a slot is one cell).
        /// </summary>
        private long ExpectedProbes(in AutoPlacePlan plan)
        {
            long probes = 0;
            for (int i = 0; i < plan.NoFitCount; i++)
            {
                AutoPlaceNoFit f = plan.NoFit(i);
                ContainerHeader h = Rig.Header(f.ContainerSlot);
                if (h.Layout == ContainerLayout.Slots)
                {
                    probes += UsableSlotsUpTo(h, (h.W * h.H) - 1);
                    continue;
                }

                int fw = f.Rotated ? f.H : f.W;
                int fh = f.Rotated ? f.W : f.H;
                probes += Math.Max(0, h.W - fw + 1) * Math.Max(0, h.H - fh + 1) * fw * fh;
            }

            InvPlacement p = plan.Placement;
            if (p.IsSet)
            {
                ContainerHeader h = Rig.Header(p.ContainerSlot);
                if (h.Layout == ContainerLayout.Slots)
                {
                    probes += UsableSlotsUpTo(h, (p.Y * h.W) + p.X);
                }
                else
                {
                    PlacementSubject s = Rig.Handler.Subject;
                    int fw = p.Rotated ? s.H : s.W;
                    probes += ((p.Y * (h.W - fw + 1)) + p.X + 1) * s.W * s.H;
                }
            }

            return probes;
        }

        private int UsableSlotsUpTo(ContainerHeader h, int lastIndex)
        {
            ContainerDef def = Rig.Content.Containers[h.DefIndex];
            int n = 0;
            for (int i = 1; i <= lastIndex; i++)
            {
                n += PlacementRules.IsReservedSlot(def, h.W, h.H, i % h.W, i / h.W) ? 0 : 1;
            }

            return n;
        }
    }
}
