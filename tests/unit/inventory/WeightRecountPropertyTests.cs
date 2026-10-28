using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/016 property and load tests (GDD R16, D1; TR-inventory-015): the incremental weight memo kept by
/// <c>InventoryMutator.Place</c> / <c>Remove</c> equals a brute-force recount from the entry pool.
/// <list type="bullet">
/// <item>Over 10 000 seeded random mutator calls (<c>Place</c>, <c>Remove</c>, <c>Transfer</c>, <c>MergeInto</c>, <c>TakeOldest</c>),
/// each in its own tick through the fixture op, over two slots' panels (hotbar, player_inventory, cursor) and a Chest (a
/// <c>player_inventory</c>-def 8 x 2 instance with no owner, built as <see cref="InvEventRig.Chest"/> is), <see cref="IInventoryApi.WeightPoints"/>
/// equals the recount for both slots after every call (and reads 0 for every other slot). Every call kind has a refusal path: the
/// validate step runs <see cref="PlacementRules.Validate"/> for a placement (<c>reserved_slot</c>, <c>out_of_bounds</c>,
/// <c>not_carryable</c>, <c>blocked</c>, a merge verdict), R3's mergeable test and the room for a merge, the count for a take, and the
/// placed state for a remove; a refused call changes nothing.</item>
/// <item>After the memo is scrambled, <see cref="InventoryMutator.RebuildWeight"/> restores the recount for every slot, and is idempotent.</item>
/// </list>
/// The generator is SplitMix64 over <see cref="SplitMix.Mix64"/> (no <c>System.Random</c>), seed <see cref="Seed"/>, so a failure
/// reproduces from the seed and the call number.
/// </summary>
public class WeightRecountPropertyTests
{
    /// <summary>The recorded seed.</summary>
    public const ulong Seed = 0x16;

    private const int Calls = 10_000;

    private static readonly string[] _mailKinds = { "letter", "letter", "letter", "postcard", "small", "medium", "large" };
    private static readonly string[] _itemKinds = { "plank", "fiber", "log", "axe" };

    private readonly ITestOutputHelper _output;

    public WeightRecountPropertyTests(ITestOutputHelper output) => _output = output;

    /// <summary>The mutator calls the driver makes.</summary>
    private enum CallKind
    {
        Place,
        Remove,
        Transfer,
        MergeInto,
        TakeOldest,
    }

    [Fact]
    [Trait("Id", "TR-inventory-015")]
    public void TenThousandSeededMutations_OverTwoSlotsPanelsAndTheChest_WeightPointsEqualsTheRecountAfterEveryCall()
    {
        // Arrange
        var driver = new Driver(Seed);

        // Act
        driver.Run(Calls);
        _output.WriteLine($"seed 0x{Seed:X}: {driver.Describe()}");

        // Assert: the per-call check ran every call; every kind committed and was refused; both slots and the Chest were loaded
        Assert.Equal(Calls, driver.CallsRun);
        Assert.Equal(Calls, driver.Checks);
        foreach (CallKind kind in Enum.GetValues<CallKind>())
        {
            Assert.True(driver.Committed[(int)kind] >= 300, $"{kind} committed {driver.Committed[(int)kind]}");
            Assert.True(driver.Refused[(int)kind] >= 30, $"{kind} refused {driver.Refused[(int)kind]}");
        }

        Assert.True(driver.MaxPoints[0] >= 20, $"slot 0 max P {driver.MaxPoints[0]}");
        Assert.True(driver.MaxPoints[1] >= 20, $"slot 1 max P {driver.MaxPoints[1]}");
        Assert.True(driver.ChestArrivals >= 100, $"chest arrivals {driver.ChestArrivals}");
        Assert.True(driver.CrossSlotTransfers >= 50, $"cross-slot transfers {driver.CrossSlotTransfers}");
        Assert.True(driver.HeavyCommits >= 50, $"calls that moved a Heavy entry {driver.HeavyCommits}");
    }

    [Fact]
    [Trait("Id", "TR-inventory-015")]
    public void RebuildWeight_AfterTheMemoIsScrambled_EqualsTheRecountForEverySlot_AndIsIdempotent()
    {
        // Arrange: a mixed state through the mutator (2 000 seeded calls), then the memo of every slot scrambled (a stale load)
        var driver = new Driver(Seed, checkEveryCall: false);
        driver.Run(2_000);
        InvAutoPlaceRig rig = driver.Rig;
        Weight weight = rig.Inventory.Mutator.Weight;
        int[] expected = new int[PlayerSlot.Count];
        for (int s = 0; s < PlayerSlot.Count; s++)
        {
            expected[s] = Recount(rig, s);
            weight.Add((byte)s, 1_000 + (37 * s));
        }

        Assert.True(expected[0] > 0 && expected[1] > 0, $"P {expected[0]}, {expected[1]}");
        Assert.NotEqual(expected[0], rig.Inventory.WeightPoints(PlayerSlot.FromValue(0)));

        // Act + Assert: the rebuild equals the recount for every slot, and a second rebuild changes nothing
        for (int pass = 0; pass < 2; pass++)
        {
            for (int s = 0; s < PlayerSlot.Count; s++)
            {
                rig.Inventory.Mutator.RebuildWeight(PlayerSlot.FromValue((byte)s));
            }

            for (int s = 0; s < PlayerSlot.Count; s++)
            {
                Assert.Equal(expected[s], weight.RawPoints(s));
                Assert.Equal(expected[s], rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)s)));
            }
        }

        // Assert: a none slot is ignored; the incremental hooks resume from the rebuilt memo
        rig.Inventory.Mutator.RebuildWeight(PlayerSlot.None);
        driver.Run(500);
        Assert.Equal(Recount(rig, 0), rig.Inventory.WeightPoints(PlayerSlot.FromValue(0)));
        Assert.Equal(Recount(rig, 1), rig.Inventory.WeightPoints(PlayerSlot.FromValue(1)));
    }

    [Fact]
    [Trait("Id", "TR-inventory-015")]
    public void RebuildWeight_FromAZeroMemo_OverEntriesArrangedOutsideTheMutator_EqualsTheRecount()
    {
        // Arrange: a Large on slot 0's hotbar, a Medium in slot 1's inventory and a letter on its cursor, a Large in the Chest; the
        // memo was never told (the load path: entries restored, memo empty)
        var rig = new InvEventRig();
        ContainerId inventoryB = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(1));
        ContainerId cursorB = rig.Inventory.CursorOf(PlayerSlot.FromValue(1));
        Arrange(rig, rig.HotbarA, "large", 1);
        Arrange(rig, inventoryB, "medium", 0);
        Arrange(rig, cursorB, "letter", 0);
        Arrange(rig, rig.Chest, "large", 0);
        Assert.Equal(0, rig.Inventory.WeightPoints(PlayerSlot.FromValue(0)));
        Assert.Equal(0, rig.Inventory.WeightPoints(PlayerSlot.FromValue(1)));

        // Act
        for (int s = 0; s < PlayerSlot.Count; s++)
        {
            rig.Inventory.Mutator.RebuildWeight(PlayerSlot.FromValue((byte)s));
        }

        // Assert: slot 0 = 8 (the Chest's Large counts for nobody), slot 1 = 3 + 1, every other slot 0; a brute-force recount agrees
        Assert.Equal(8, rig.Inventory.WeightPoints(PlayerSlot.FromValue(0)));
        Assert.Equal(4, rig.Inventory.WeightPoints(PlayerSlot.FromValue(1)));
        for (int s = 0; s < PlayerSlot.Count; s++)
        {
            Assert.Equal(Recount(rig.Host.Content, rig.Inventory, s), rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)s)));
        }
    }

    /// <summary>D1's <c>P</c> of <paramref name="slot"/> by brute force: every live placed entry whose container's owner is the slot, its def's class points.</summary>
    internal static int Recount(ContentSet content, InventorySystem inventory, int slot)
    {
        InventoryBalance knobs = content.Inventory;
        EntryPool entries = inventory.Entries;
        int p = 0;
        for (int i = 0; i < entries.Capacity; i++)
        {
            EntryRecord e = entries[(ushort)i];
            if (!e.IsLive || e.ContainerSlot == EntryRecord.NoContainer || inventory.Containers[e.ContainerSlot].OwnerSlot != slot)
            {
                continue;
            }

            WeightClass c = e.IsMail
                ? content.MailKinds[DefIndex.FromValue(e.MailKind)].WeightClass
                : content.Items[DefIndex.FromValue(e.ItemId)].WeightClass;
            p += c switch
            {
                WeightClass.Light => knobs.WeightPointsLight,
                WeightClass.Medium => knobs.WeightPointsMedium,
                WeightClass.Heavy => knobs.WeightPointsHeavy,
                _ => throw new InvalidOperationException($"entry {i}: class {c} in a counted container"),
            };
        }

        return p;
    }

    private static int Recount(InvAutoPlaceRig rig, int slot) => Recount(rig.Content, rig.Inventory, slot);

    /// <summary>Arranges a 1-unit mail entry of <paramref name="kind"/> at (x, 0) as a 1 x 1 footprint, outside the mutator (no event, no weight).</summary>
    private static void Arrange(InvEventRig rig, ContainerId container, string kind, byte x)
    {
        DefIndex k = PlacementFixture.Index(rig.Host.Content.MailKinds, kind);
        EntryId id = rig.AddMail(container, x, 0, [900u + x]);
        Assert.True(rig.Inventory.Entries.TryGetSlot(id, out ushort slot));
        rig.Inventory.Entries[slot].MailKind = k.Value;
    }

    /// <summary>The seeded call generator, its refusal-aware validate steps and the per-call recount.</summary>
    private sealed class Driver
    {
        private const ulong Gamma = 0x9E37_79B9_7F4A_7C15UL;

        private readonly bool _check;
        private readonly ushort[] _containers;
        private readonly uint[] _taken = new uint[byte.MaxValue];
        private readonly ushort _chest;
        private ulong _state;
        private uint _nextMailId = 1;

        public Driver(ulong seed, bool checkEveryCall = true)
        {
            Rig = new InvAutoPlaceRig();
            var join = new Op { Kind = OpKind.PlayerJoin, ReqId = 2 };
            join.SetPayload(new PlayerJoinPayload { Slot = 1 });
            Assert.True(Rig.Host.World.EnqueueOp(ActorId.System, in join));
            Rig.Host.World.Tick();
            PlayerSlot a = PlayerSlot.FromValue(0);
            PlayerSlot b = PlayerSlot.FromValue(1);
            InventorySystem inv = Rig.Inventory;
            _chest = Rig.CreateGrid(8, 2);
            _containers = new[]
            {
                Rig.SlotOf(inv.HotbarOf(a)), Rig.SlotOf(inv.PlayerInventoryOf(a)), Rig.SlotOf(inv.CursorOf(a)),
                Rig.SlotOf(inv.HotbarOf(b)), Rig.SlotOf(inv.PlayerInventoryOf(b)), Rig.SlotOf(inv.CursorOf(b)),
                _chest,
            };
            Assert.Equal(ContainerHeader.NoOwner, Rig.Header(_chest).OwnerSlot);
            Assert.Equal(1, Rig.Header(_containers[4]).OwnerSlot);
            _state = seed;
            _check = checkEveryCall;
        }

        public InvAutoPlaceRig Rig { get; }

        public int CallsRun { get; private set; }

        public int Checks { get; private set; }

        public int[] Committed { get; } = new int[5];

        public int[] Refused { get; } = new int[5];

        public int[] MaxPoints { get; } = new int[2];

        public int ChestArrivals { get; private set; }

        public int CrossSlotTransfers { get; private set; }

        public int HeavyCommits { get; private set; }

        public string Describe() =>
            string.Join(", ", Enum.GetValues<CallKind>().Select(k => $"{k} {Committed[(int)k]} ok / {Refused[(int)k]} refused"))
            + $"; max P {MaxPoints[0]}, {MaxPoints[1]}; chest arrivals {ChestArrivals}; cross-slot transfers {CrossSlotTransfers}; heavy {HeavyCommits}";

        public void Run(int calls)
        {
            for (int call = 0; call < calls; call++)
            {
                int live = PlacedEntries().Count;
                int roll = Next(100);
                if (live > 45 && roll < 30)
                {
                    roll = 30; // keep the population bounded: bias to Remove
                }

                switch (roll)
                {
                    case < 30:
                        DoPlace();
                        break;
                    case < 45:
                        DoRemove();
                        break;
                    case < 70:
                        DoTransfer();
                        break;
                    case < 85:
                        DoMerge();
                        break;
                    default:
                        DoTake();
                        break;
                }

                if (_check)
                {
                    AssertPointsMatchRecount(call);
                }

                CallsRun++;
            }
        }

        // ---------------------------------------------------------------- generator

        private ulong NextRaw()
        {
            ulong r = SplitMix.Mix64(_state); // Mix64 adds the gamma: the SplitMix64 output for the state before the step
            _state = unchecked(_state + Gamma);
            return r;
        }

        private int Next(int bound) => (int)(NextRaw() % (ulong)bound);

        // ---------------------------------------------------------------- calls

        private ushort NewDetached()
        {
            if (Next(4) == 0)
            {
                string item = _itemKinds[Next(_itemKinds.Length)];
                int effMax = Rig.Item(item).EffMax;
                return Rig.DetachedItem(item, (byte)(1 + Next(effMax)));
            }

            string kind = _mailKinds[Next(_mailKinds.Length)];
            uint address = 1u + (uint)Next(2);
            int max = Rig.Mail(kind, address).EffMax;
            int count = 1 + Next(Math.Min(max, 20));
            uint[] ids = InvAutoPlaceRig.Ids(_nextMailId, count);
            _nextMailId += (uint)count + (uint)Next(3);
            return Rig.DetachedMail(kind, address, ids);
        }

        private void DoPlace()
        {
            ushort entry = NewDetached();
            ushort to = _containers[Next(_containers.Length)];
            Pick(entry, to, Occupancy.Free, false, out int x, out int y, out bool rotated);
            Reply r = Rig.Step(
                (ref InvPlan p) => p.Touch(to) < 0 ? CoreReason.BadRequest : CheckPlacement(entry, to, x, y, rotated, Occupancy.Free, false),
                (ref InvPlan p) => Rig.Inventory.Mutator.Place(ref p, entry, to, (byte)x, (byte)y, rotated));
            if (Count(CallKind.Place, r))
            {
                ChestArrivals += to == _chest ? 1 : 0;
                HeavyCommits += IsHeavy(entry) ? 1 : 0;
            }
            else
            {
                Assert.Equal(EntryRecord.NoContainer, Rig.Entries[entry].ContainerSlot);
                Rig.FreeDetached(entry);
            }
        }

        private void DoRemove()
        {
            List<ushort> placed = PlacedEntries();
            bool stale = placed.Count == 0 || Next(8) == 0;
            ushort entry = stale ? NewDetached() : placed[Next(placed.Count)]; // a stale pick: a detached entry (nothing to remove)
            ushort from = Rig.Entries[entry].ContainerSlot;
            bool heavy = IsHeavy(entry);
            Reply r = Rig.Step(
                (ref InvPlan p) => from == EntryRecord.NoContainer || p.Touch(from) < 0 ? (ushort)InvReason.StaleEntry : (ushort)0,
                (ref InvPlan p) => Rig.Inventory.Mutator.Remove(ref p, entry));
            if (Count(CallKind.Remove, r))
            {
                HeavyCommits += heavy ? 1 : 0;
            }

            Rig.FreeDetached(entry);
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
            bool whole = from == to;
            Pick(entry, to, entry, whole, out int x, out int y, out bool rotated);
            Reply r = Rig.Step(
                (ref InvPlan p) => p.Touch(from) < 0 || p.Touch(to) < 0 ? CoreReason.BadRequest : CheckPlacement(entry, to, x, y, rotated, entry, whole),
                (ref InvPlan p) => Rig.Inventory.Mutator.Transfer(ref p, entry, to, (byte)x, (byte)y, rotated));
            if (Count(CallKind.Transfer, r))
            {
                ChestArrivals += to == _chest && from != _chest ? 1 : 0;
                CrossSlotTransfers += Rig.Header(from).OwnerSlot != Rig.Header(to).OwnerSlot ? 1 : 0;
                HeavyCommits += IsHeavy(entry) && !whole ? 1 : 0;
            }
        }

        private void DoMerge()
        {
            List<ushort> placed = PlacedEntries();
            if (placed.Count == 0)
            {
                return;
            }

            // The source: a placed entry, or (1 in 4) a new detached one; the target: (3 in 4) a same-key stack when one exists
            bool detachedSource = Next(4) == 0;
            ushort source = detachedSource ? NewDetached() : placed[Next(placed.Count)];
            StackKey key = StackKey.Of(in Rig.Entries[source]);
            var sameKey = placed.Where(t => t != source && StackKey.Of(in Rig.Entries[t]) == key).ToList();
            ushort target = sameKey.Count > 0 && Next(4) != 0 ? sameKey[Next(sameKey.Count)] : placed[Next(placed.Count)];
            int units = 1 + Next(Rig.Entries[source].Count);
            ushort from = Rig.Entries[source].ContainerSlot;
            ushort to = Rig.Entries[target].ContainerSlot;
            Reply r = Rig.Step(
                (ref InvPlan p) =>
                {
                    if ((from != EntryRecord.NoContainer && p.Touch(from) < 0) || p.Touch(to) < 0)
                    {
                        return CoreReason.BadRequest;
                    }

                    ushort reason = CheckMerge(source, target, units);
                    if (reason == 0 && Rig.Entries[target].IsMail)
                    {
                        _ = p.AddAcquiredBlock(Rig.Entries[target].Count + units); // the target's run after the union
                    }

                    return reason;
                },
                (ref InvPlan p) => Rig.Inventory.Mutator.MergeInto(ref p, source, target, units));
            _ = Count(CallKind.MergeInto, r);
            if (Rig.Entries[source].IsLive && Rig.Entries[source].ContainerSlot == EntryRecord.NoContainer)
            {
                Rig.FreeDetached(source); // a detached source's remainder (or the whole of a refused one)
            }
        }

        private void DoTake()
        {
            List<ushort> placed = PlacedEntries();
            if (placed.Count == 0)
            {
                return;
            }

            ushort entry = placed[Next(placed.Count)];
            int count = Rig.Entries[entry].Count;
            int k = Next(8) == 0 ? count + 1 : 1 + Next(count); // 1 in 8 asks for more units than the entry holds
            bool heavy = IsHeavy(entry) && k == count;
            ushort from = Rig.Entries[entry].ContainerSlot;
            int took = 0;
            Reply r = Rig.Step(
                (ref InvPlan p) => p.Touch(from) < 0 ? CoreReason.BadRequest : k > Rig.Entries[entry].Count ? (ushort)InvReason.BadCount : (ushort)0,
                (ref InvPlan p) => took = Rig.Inventory.Mutator.TakeOldest(ref p, entry, k, _taken));
            if (Count(CallKind.TakeOldest, r))
            {
                Assert.Equal(k, took);
                HeavyCommits += heavy ? 1 : 0;
            }
        }

        /// <summary>Counts the reply under its kind; true when committed.</summary>
        private bool Count(CallKind kind, in Reply r)
        {
            if (r.IsOk)
            {
                Committed[(int)kind]++;
                return true;
            }

            Refused[(int)kind]++;
            return false;
        }

        private bool IsHeavy(ushort entry)
        {
            EntryRecord e = Rig.Entries[entry];
            return e.IsMail && Rig.Content.MailKinds[DefIndex.FromValue(e.MailKind)].WeightClass == WeightClass.Heavy;
        }

        // ---------------------------------------------------------------- validate steps (read-only)

        /// <summary>R4's placement chain; a merge verdict is refused (<c>Place</c> / <c>Transfer</c> do not merge).</summary>
        private ushort CheckPlacement(ushort entry, ushort to, int x, int y, bool rotated, ushort source, bool whole)
        {
            ContainerHeader h = Rig.Header(to);
            PlacementSubject subject = Rig.SubjectOf(entry);
            PlacementVerdict v = PlacementRules.Validate(
                Rig.Inventory.Occupancy, Rig.Entries, to, in h, Rig.Content.Containers[h.DefIndex], in subject, x, y, rotated, source, whole);
            return !v.IsOk ? (ushort)v.Reason : v.IsMerge ? (ushort)InvReason.Blocked : (ushort)0;
        }

        /// <summary>R3's mergeable test and the target's room: <c>not_allowed</c>, <c>blocked</c> (self) or <c>stack_full</c>.</summary>
        private ushort CheckMerge(ushort source, ushort target, int units)
        {
            if (source == target)
            {
                return (ushort)InvReason.Blocked;
            }

            ref EntryRecord src = ref Rig.Entries[source];
            ref EntryRecord dst = ref Rig.Entries[target];
            PlacementSubject s = Rig.SubjectOf(source);
            ContainerDef def = Rig.Content.Containers[Rig.Header(dst.ContainerSlot).DefIndex];
            if (!PlacementRules.CanMerge(StackKey.Of(in src), StackKey.Of(in dst), s.EffMax, def, s.Category))
            {
                return (ushort)InvReason.NotAllowed;
            }

            return dst.Count + units > Rig.Inventory.Mutator.EffMaxOf(in dst) ? (ushort)InvReason.StackFull : (ushort)0;
        }

        /// <summary>
        /// A target position: half the time a uniformly random cell and rotation (mostly refused), otherwise the first cell from a
        /// random start that the placement chain accepts as free (falling back to the random cell when there is none).
        /// </summary>
        private void Pick(ushort entry, ushort to, ushort source, bool whole, out int x, out int y, out bool rotated)
        {
            ContainerHeader h = Rig.Header(to);
            int cells = h.W * h.H;
            int start = Next(cells);
            rotated = Next(2) == 1;
            x = start % h.W;
            y = start / h.W;
            if (Next(2) == 0)
            {
                return;
            }

            for (int k = 0; k < cells; k++)
            {
                int i = (start + k) % cells;
                if (CheckPlacement(entry, to, i % h.W, i / h.W, rotated, source, whole) == 0)
                {
                    x = i % h.W;
                    y = i / h.W;
                    return;
                }
            }
        }

        // ---------------------------------------------------------------- brute-force view and check

        private List<ushort> PlacedEntries()
        {
            var list = new List<ushort>();
            EntryPool entries = Rig.Entries;
            for (int i = 0; i < entries.Capacity; i++)
            {
                EntryRecord e = entries[(ushort)i];
                if (e.IsLive && e.ContainerSlot != EntryRecord.NoContainer && Array.IndexOf(_containers, e.ContainerSlot) >= 0)
                {
                    list.Add((ushort)i);
                }
            }

            return list;
        }

        private void AssertPointsMatchRecount(int call)
        {
            for (int s = 0; s < PlayerSlot.Count; s++)
            {
                int expected = Recount(Rig, s);
                short actual = Rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)s));
                Assert.True(expected == actual, $"seed 0x{Seed:X} call {call}: slot {s} WeightPoints {actual}, recount {expected}");
                if (s < 2)
                {
                    MaxPoints[s] = Math.Max(MaxPoints[s], expected);
                }
                else
                {
                    Assert.Equal(0, expected);
                }
            }

            Checks++;
        }
    }
}
