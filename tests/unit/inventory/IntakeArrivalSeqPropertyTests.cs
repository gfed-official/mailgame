using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Intake;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/012 property test of the Intake's <c>arrivalSeq</c> (GDD R14 "no Intake entry's arrivalSeq ever decreases",
/// H-R14-3; owner ruling Evan 2026-10-21 12:28 PT: a merge onto an Intake stack restamps it with the next <c>arrivalSeq</c>, a move
/// inside the Intake keeps it). A seeded <see cref="Pcg32"/> drives a few thousand steps of <c>Spawn</c>, <c>ReturnToIntake</c>, merges
/// onto Intake stacks (detached and in-Intake sources), moves inside the Intake, <c>TakeUnit</c> and removals; after every step:
/// <list type="bullet">
/// <item><c>NextArrivalSeq</c> never decreases;</item>
/// <item>every live Intake entry's <c>arrivalSeq</c> is non-zero, below <c>NextArrivalSeq</c> and unique;</item>
/// <item>the FIFO's live nodes are exactly the live entries, one each, in strictly increasing <c>arrivalSeq</c>, and the list holds at
/// most twice its live entries with no node leaked from the pool;</item>
/// <item>no entry's <c>arrivalSeq</c> decreases while it stays in the Intake;</item>
/// <item>a merged target holds the previous <c>NextArrivalSeq</c> (it leaves last), a moved entry keeps its seq, and a take serves the
/// lowest mailId of the lowest-seq live entry.</item>
/// </list>
/// </summary>
public class IntakeArrivalSeqPropertyTests
{
    private const int Steps = 4_000;

    private static readonly uint[] _addresses = { 4u, 4u, 4u, 9u };

    [Theory]
    [InlineData(0x0012_0012UL)]
    [InlineData(0xC0FF_EE01UL)]
    public void SeededRandomIntakeSequence_ArrivalSeqNeverDecreases_IsUniqueBelowNext_AndTheFifoServesItInOrder(ulong seed)
    {
        // Arrange
        var driver = new Driver(new InvAutoPlaceRig(), seed);

        // Act
        driver.Run(Steps);

        // Assert: the per-step checks ran; the mix exercised every kind of step
        Assert.Equal(Steps, driver.StepsRun);
        Assert.True(driver.Spawns > 300, $"spawns {driver.Spawns}");
        Assert.True(driver.Returns > 150, $"returns {driver.Returns}");
        Assert.True(driver.DetachedMerges > 300, $"detached merges {driver.DetachedMerges}");
        Assert.True(driver.IntakeMerges > 150, $"in-Intake merges {driver.IntakeMerges}");
        Assert.True(driver.Moves > 300, $"moves {driver.Moves}");
        Assert.True(driver.Takes > 300, $"takes {driver.Takes}");
        Assert.True(driver.Removes > 150, $"removes {driver.Removes}");
        Assert.True(driver.MaxTombstones >= 5, $"tombstones never piled up: at most {driver.MaxTombstones}");
    }

    private sealed class Driver
    {
        private const int MaxDetached = 24;

        private readonly InvAutoPlaceRig _rig;
        private readonly OutfeedFifo _fifo;
        private readonly DefIndex _letter;
        private readonly List<ushort> _detached = new();
        private readonly Dictionary<uint, uint> _seqById = new();
        private Pcg32 _rng;
        private uint _nextMailId = 1;
        private readonly int _effMaxLetter;
        private uint _lastNext;

        public Driver(InvAutoPlaceRig rig, ulong seed)
        {
            _rig = rig;
            _fifo = rig.Inventory.Mutator.Outfeed;
            _letter = rig.KindIndex("letter");
            _rng = new Pcg32(seed, 12);
            _lastNext = rig.Header(rig.Intake).NextArrivalSeq;
            _effMaxLetter = rig.Mail("letter", 4u).EffMax;
        }

        public int StepsRun { get; private set; }

        public int Spawns { get; private set; }

        public int Returns { get; private set; }

        public int DetachedMerges { get; private set; }

        public int IntakeMerges { get; private set; }

        public int Moves { get; private set; }

        public int Takes { get; private set; }

        public int Removes { get; private set; }

        public int MaxTombstones { get; private set; }

        private ushort Intake => _rig.Intake;

        public void Run(int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                int live = _rig.Header(Intake).EntryCount;
                int roll = Next(100);
                if (live > 220)
                {
                    roll = 60 + Next(40); // drain: takes and removals only, so the Intake never fills
                }

                if (roll < 15)
                {
                    Spawn(step);
                }
                else if (roll < 25)
                {
                    Return(step);
                }
                else if (roll < 40)
                {
                    MergeDetached(step);
                }
                else if (roll < 50)
                {
                    MergeInsideIntake(step);
                }
                else if (roll < 60)
                {
                    Move(step);
                }
                else if (roll < 85)
                {
                    Take(step);
                }
                else
                {
                    Remove(step);
                }

                Check(step);
                StepsRun++;
            }
        }

        private int Next(int bound) => (int)(_rng.Next32() % (uint)bound);

        private uint Address() => _addresses[Next(_addresses.Length)];

        private uint[] FreshIds(int n)
        {
            uint[] ids = InvAutoPlaceRig.Ids(_nextMailId, n);
            _nextMailId += (uint)n;
            return ids;
        }

        private List<ushort> LiveIntakeEntries()
        {
            var live = new List<ushort>();
            EntryPool pool = _rig.Entries;
            for (int s = 0; s < pool.Capacity; s++)
            {
                if (pool[(ushort)s].IsLive && pool[(ushort)s].ContainerSlot == Intake)
                {
                    live.Add((ushort)s);
                }
            }

            return live;
        }

        private void Spawn(int step)
        {
            uint[] ids = FreshIds(1 + Next(3));
            MailId[] mail = ids.Select(i => new MailId(i)).ToArray();
            uint address = Address();
            SpawnResult r = IntakeSpawnOutfeedTests.InPhase(
                _rig, () => _rig.Inventory.Spawn(_rig.Inventory.IntakeContainer, _letter, address, 0, mail, MergePolicy.NoMerge));
            Assert.True(r.Placed == ids.Length, $"step {step}: spawn placed {r.Placed} of {ids.Length}");
            Spawns++;
        }

        private void Return(int step)
        {
            ushort entry;
            if (_detached.Count > 0 && Next(4) != 0)
            {
                int at = Next(_detached.Count);
                entry = _detached[at];
                _detached.RemoveAt(at);
            }
            else
            {
                entry = _rig.DetachedMail("letter", Address(), FreshIds(1 + Next(4)));
            }

            uint next = _rig.Header(Intake).NextArrivalSeq;
            Assert.True(
                IntakeSpawnOutfeedTests.InPhase(_rig, () => _rig.Inventory.ReturnToIntake(entry, ActorId.System, InvCause.Expired)),
                $"step {step}: return refused");
            Assert.True(_rig.Entries[entry].ArrivalSeq == next, $"step {step}: returned entry seq {_rig.Entries[entry].ArrivalSeq}, expected {next}");
            Returns++;
        }

        private void MergeDetached(int step)
        {
            List<ushort> live = LiveIntakeEntries().Where(e => _rig.Entries[e].Count < _effMaxLetter).ToList();
            if (live.Count == 0)
            {
                Spawn(step);
                return;
            }

            ushort target = live[Next(live.Count)];
            int room = _effMaxLetter - _rig.Entries[target].Count;
            ushort source = _rig.DetachedMail("letter", _rig.Entries[target].AddressId, FreshIds(1 + Next(Math.Min(room, 3))));
            int units = _rig.Entries[source].Count;
            Merge(step, source, target, units);
            DetachedMerges++;
        }

        private void MergeInsideIntake(int step)
        {
            List<ushort> live = LiveIntakeEntries();
            var pairs = new List<(ushort Source, ushort Target)>();
            foreach (ushort t in live)
            {
                foreach (ushort s in live)
                {
                    if (s != t && _rig.Entries[s].AddressId == _rig.Entries[t].AddressId && _rig.Entries[t].Count < _effMaxLetter)
                    {
                        pairs.Add((s, t));
                    }
                }
            }

            if (pairs.Count == 0)
            {
                Spawn(step);
                return;
            }

            (ushort source, ushort target) = pairs[Next(pairs.Count)];
            int max = Math.Min(_rig.Entries[source].Count, _effMaxLetter - _rig.Entries[target].Count);
            Merge(step, source, target, 1 + Next(max));
            IntakeMerges++;
        }

        private void Merge(int step, ushort source, ushort target, int units)
        {
            uint next = _rig.Header(Intake).NextArrivalSeq;
            uint sourceSeq = _rig.Entries[source].ArrivalSeq;
            bool sourceStays = _rig.Entries[source].Count > units;
            Reply r = _rig.Step(
                (ref InvPlan p) => p.Touch(_rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => _rig.Inventory.Mutator.MergeInto(ref p, source, target, units));

            // the ruling: the target takes the previous NextArrivalSeq (it now leaves last); a partly merged in-Intake source keeps its seq
            Assert.True(r.IsOk, $"step {step}: merge refused");
            Assert.True(_rig.Entries[target].ArrivalSeq == next, $"step {step}: merged target seq {_rig.Entries[target].ArrivalSeq}, expected {next}");
            Assert.True(_rig.Header(Intake).NextArrivalSeq == next + 1, $"step {step}: NextArrivalSeq {_rig.Header(Intake).NextArrivalSeq} after a merge, expected {next + 1}");
            if (sourceStays && sourceSeq != 0)
            {
                Assert.True(_rig.Entries[source].ArrivalSeq == sourceSeq, $"step {step}: the partly merged source was restamped");
            }
        }

        private void Move(int step)
        {
            List<ushort> live = LiveIntakeEntries();
            if (live.Count == 0)
            {
                Spawn(step);
                return;
            }

            ushort entry = live[Next(live.Count)];
            int x;
            int y;
            do
            {
                x = Next(_rig.Header(Intake).W);
                y = Next(_rig.Header(Intake).H);
            }
            while (_rig.EntryAt(Intake, x, y) != Occupancy.Free);

            uint seq = _rig.Entries[entry].ArrivalSeq;
            uint next = _rig.Header(Intake).NextArrivalSeq;
            Reply r = _rig.Step(
                (ref InvPlan p) => p.Touch(_rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => _rig.Inventory.Mutator.Transfer(ref p, entry, _rig.Intake, (byte)x, (byte)y, false));
            Assert.True(r.IsOk, $"step {step}: move refused");
            Assert.True(_rig.Entries[entry].ArrivalSeq == seq, $"step {step}: a move inside the Intake changed the seq");
            Assert.True(_rig.Header(Intake).NextArrivalSeq == next, $"step {step}: a move inside the Intake bumped NextArrivalSeq");
            Moves++;
        }

        private void Take(int step)
        {
            List<ushort> live = LiveIntakeEntries();
            bool expectUnit = live.Count > 0;
            uint expectedMail = 0;
            if (expectUnit)
            {
                ushort oldest = live.OrderBy(e => _rig.Entries[e].ArrivalSeq).First();
                expectedMail = _rig.IdsOf(oldest)[0];
            }

            UnitRef u = default;
            bool took = IntakeSpawnOutfeedTests.InPhase(
                _rig, () => _rig.Inventory.TakeUnit(_rig.Inventory.IntakeContainer, Selector.Fifo, _rig.PlayerA, out u));
            Assert.True(took == expectUnit, $"step {step}: TakeUnit returned {took} with {live.Count} live entries");
            Assert.True(u.Mail.Value == expectedMail, $"step {step}: took mailId {u.Mail.Value}, expected {expectedMail} (lowest-seq entry)");
            Takes++;
        }

        private void Remove(int step)
        {
            List<ushort> live = LiveIntakeEntries();
            if (live.Count == 0)
            {
                Spawn(step);
                return;
            }

            ushort entry = live[Next(live.Count)];
            Reply r = _rig.Step(
                (ref InvPlan p) => p.Touch(_rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => _rig.Inventory.Mutator.Remove(ref p, entry));
            Assert.True(r.IsOk, $"step {step}: remove refused");
            Assert.True(_rig.Entries[entry].ArrivalSeq == 0u, $"step {step}: a removed entry kept its seq");
            if (_detached.Count < MaxDetached)
            {
                _detached.Add(entry); // returned later with a fresh seq (same entryId: its old node is stale)
            }
            else
            {
                _rig.FreeDetached(entry);
            }

            Removes++;
        }

        private void Check(int step)
        {
            ContainerHeader h = _rig.Header(Intake);

            // NextArrivalSeq never decreases
            Assert.True(h.NextArrivalSeq >= _lastNext, $"step {step}: NextArrivalSeq fell from {_lastNext} to {h.NextArrivalSeq}");
            _lastNext = h.NextArrivalSeq;

            // every live Intake entry: seq non-zero, below NextArrivalSeq, unique; no seq decreased while it stayed
            List<ushort> live = LiveIntakeEntries();
            var seqs = new HashSet<uint>();
            var now = new Dictionary<uint, uint>();
            foreach (ushort e in live)
            {
                EntryRecord r = _rig.Entries[e];
                Assert.True(r.ArrivalSeq != 0u && r.ArrivalSeq < h.NextArrivalSeq, $"step {step}: entry {r.Id.Value} seq {r.ArrivalSeq}, next {h.NextArrivalSeq}");
                Assert.True(seqs.Add(r.ArrivalSeq), $"step {step}: seq {r.ArrivalSeq} is held twice");
                if (_seqById.TryGetValue(r.Id.Value, out uint before))
                {
                    Assert.True(r.ArrivalSeq >= before, $"step {step}: entry {r.Id.Value} seq fell from {before} to {r.ArrivalSeq} inside the Intake");
                }

                now[r.Id.Value] = r.ArrivalSeq;
            }

            _seqById.Clear();
            foreach (KeyValuePair<uint, uint> kv in now)
            {
                _seqById[kv.Key] = kv.Value;
            }

            // the FIFO: live nodes are exactly the live entries, one each, strictly ascending; bounded; nothing leaked from the pool
            var served = new HashSet<ushort>();
            uint last = 0;
            int nodes = 0;
            for (uint node = h.OutfeedHead; node != OutfeedFifo.NoNode; nodes++)
            {
                node = _fifo.ReadNode(Intake, node, out uint seq, out ushort slot, out bool isLive);
                if (!isLive)
                {
                    continue;
                }

                Assert.True(seq > last, $"step {step}: FIFO serves seq {seq} after {last}");
                Assert.True(served.Add(slot), $"step {step}: entry slot {slot} has two live nodes");
                last = seq;
            }

            Assert.True(served.SetEquals(live), $"step {step}: FIFO live nodes {served.Count} vs live entries {live.Count}");
            Assert.Equal(_fifo.NodeCount(Intake), nodes);
            Assert.True(nodes <= 2 * live.Count, $"step {step}: {nodes} nodes for {live.Count} live entries");
            Assert.Equal(_fifo.Capacity, _fifo.FreeCount + nodes);
            MaxTombstones = Math.Max(MaxTombstones, nodes - live.Count);
        }
    }
}
