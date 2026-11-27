using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Mail
{
    /// <summary>
    /// Generates mail into the PO Intake (doc 01 §3.2, doc 03 §1.3): value-budgeted batches on a
    /// jittered timer, mixed by value share, with overflow held as backlog.
    /// </summary>
    public sealed class MailSpawner
    {
        private const long Scale = 1000; // budget is tracked in milli-¢ so shares do not lose value to rounding

        private readonly ContentDb _content;
        private readonly WorldMap _map;
        private readonly GridContainer _intake;
        private readonly EntryIdAllocator _entryIds;
        private readonly uint _seed;
        private RngStream _spawnRng;
        private RngStream _addressRng;

        private readonly long[] _accrued;
        private readonly Queue<MailItem> _backlog = new Queue<MailItem>();
        private readonly List<DestinationDef> _eligible = new List<DestinationDef>();
        private readonly List<DestinationDef> _streak = new List<DestinationDef>();
        private readonly HashSet<int> _batchStreets = new HashSet<int>();

        private uint _nextMailId = 1;
        private bool _active;
        private int _shift;
        private uint _startTick;
        private int _batchCount;
        private int _nextBatch;
        private uint _nextBatchTick;
        private long _batchBudget;
        private uint _backlogTimer;
        private uint _intakeVersionAtLastDrain = uint.MaxValue;

        public int BacklogCount => _backlog.Count;
        public uint MailSpawnedTotal => _nextMailId - 1;
        public int BatchesPerShift => _batchCount;

        /// <summary>
        /// Doc 05 §6 (the Cursed Mail stamp): the part of the mail spawned cursed, in permille (0: none —
        /// and then nothing is drawn from its stream, so a run without it spawns what it always did),
        /// and what a cursed item's value is multiplied by.
        /// </summary>
        public int CursedPermille { get; set; }
        public float CursedValueMult { get; set; } = 1f;
        private RngStream _curseRng;

        /// <summary>
        /// Doc 10 §4.3 Priority Post (rule flag PriorityPost): the part of the mail spawned Priority, permille (0:
        /// none, and nothing is drawn from its stream), what its value is multiplied by, and the ticks it has.
        /// Cursed mail is never Priority too.
        /// </summary>
        public int PriorityPermille { get; set; }
        public float PriorityValueMult { get; set; } = 1f;
        public uint PriorityTicks { get; set; }
        private RngStream _priorityRng;
        private uint _now;

        /// <summary>A new item Priority or not, its value raised and its time set if it is.</summary>
        private void Prioritize(ref MailItem item)
        {
            if (PriorityPermille <= 0 || (item.Flags & MailFlags.Cursed) != 0 || !_priorityRng.Chance(PriorityPermille)) return;
            item.Flags |= MailFlags.Priority;
            item.DueTick = _now + PriorityTicks;
            item.Value = (ushort)Math.Min(ushort.MaxValue, Math.Round(item.Value * (double)PriorityValueMult));
        }

        /// <summary>A new item cursed or not, its value raised if it is.</summary>
        private void Curse(ref MailItem item)
        {
            if (CursedPermille <= 0 || !_curseRng.Chance(CursedPermille)) return;
            item.Flags |= MailFlags.Cursed;
            item.Value = (ushort)Math.Min(ushort.MaxValue, Math.Round(item.Value * (double)CursedValueMult));
        }

        public MailSpawner(ContentDb content, WorldMap map, GridContainer intake, EntryIdAllocator entryIds, uint seed)
        {
            _content = content;
            _map = map;
            _intake = intake;
            _entryIds = entryIds;
            _seed = seed;
            _spawnRng = new RngStream(seed, "mail.spawn");
            _addressRng = new RngStream(seed, "mail.address");
            _curseRng = new RngStream(seed, "mail.curse");
            _priorityRng = new RngStream(seed, "mail.priority");
            _accrued = new long[content.MailKinds.Count];
        }

        /// <summary>
        /// Back to the state of a fresh run: no schedule, no backlog, RNG streams restarted, so the
        /// same seed spawns the same mail again (doc 01 §5.3). Mail ids keep counting up.
        /// </summary>
        public void Reset()
        {
            _spawnRng = new RngStream(_seed, "mail.spawn");
            _addressRng = new RngStream(_seed, "mail.address");
            _curseRng = new RngStream(_seed, "mail.curse");
            _priorityRng = new RngStream(_seed, "mail.priority");
            Array.Clear(_accrued, 0, _accrued.Length);
            _backlog.Clear();
            _active = false;
            _backlogTimer = 0;
            _intakeVersionAtLastDrain = uint.MaxValue;
        }

        /// <summary>Starts a Delivery phase worth <paramref name="spawnValue"/> ¢ of mail.</summary>
        public void BeginShift(int shift, uint startTick, uint durationTicks, int spawnValue)
        {
            var mix = _content.MailMix;
            uint interval = SimConstants.SecondsToTicks(mix.BatchIntervalSeconds);
            _shift = shift;
            _startTick = startTick;
            _batchCount = Math.Max(1, (int)(durationTicks / interval));
            _batchBudget = spawnValue * Scale / _batchCount;
            _nextBatch = 0;
            Array.Clear(_accrued, 0, _accrued.Length);
            _active = true;
            ScheduleNextBatch();
        }

        public void EndShift() => _active = false;

        /// <summary>
        /// One tick. Returns the number of items generated this tick (0 on most ticks); complaint
        /// points owed for standing backlog are added to <paramref name="backlogComplaint"/>.
        /// </summary>
        public int Step(uint tick, ref int backlogComplaint)
        {
            _now = tick;
            int spawned = 0;
            if (_active && _nextBatch < _batchCount && tick >= _nextBatchTick)
            {
                spawned = SpawnBatch();
                _nextBatch++;
                ScheduleNextBatch();
            }

            if (_backlog.Count > 0)
            {
                if (_intake.Version != _intakeVersionAtLastDrain || _intake.HasUncommittedChanges) DrainBacklog();
                uint interval = SimConstants.SecondsToTicks(_content.Balance.GetDouble("complaintBacklogIntervalSeconds"));
                if (_backlog.Count > 0 && ++_backlogTimer >= interval)
                {
                    _backlogTimer = 0;
                    backlogComplaint += _content.Balance.GetInt("complaintBacklogIncrement");
                }
            }
            else _backlogTimer = 0;
            return spawned;
        }

        /// <summary>
        /// One piece of a kind for a destination, priced as a batch would price it, into the Intake
        /// (or the backlog): the debug world's steady feed instead of the batches.
        /// </summary>
        public MailItem SpawnOne(MailKindDef kind, DestinationDef dest, int shift)
        {
            var mix = _content.MailMix;
            double shiftMult = 1.0 + mix.ShiftMultiplierPerShift * (shift - 1);
            double distMult = 1.0 + mix.DistanceMultiplierPerDistrict * (dest.Address.District - 1);
            var item = new MailItem
            {
                Id = _nextMailId++,
                Kind = kind.Index,
                Address = dest.Address,
                Value = (ushort)Math.Min(ushort.MaxValue, Math.Round(kind.BaseValue * distMult * shiftMult)),
                SpawnShift = (byte)shift,
                DeadlineShift = (byte)(shift + kind.DeadlineOffsetShifts),
            };
            Curse(ref item);
            Prioritize(ref item);
            ReturnToIntake(item);
            return item;
        }

        /// <summary>
        /// Doc 05 §2.1's Lost Parcel: a piece of the kind for a random unlocked address that takes
        /// it, priced as a batch would price it times <paramref name="valueMult"/>, not placed
        /// anywhere; null when no unlocked address takes the kind. The address comes from the
        /// caller's stream, so the mail's own streams spawn what they always did.
        /// </summary>
        public MailItem? LostParcel(MailKindDef kind, int shift, double valueMult, RngStream rng)
        {
            CollectEligible(kind);
            if (_eligible.Count == 0) return null;
            var dest = _eligible[rng.NextInt(_eligible.Count)];
            var mix = _content.MailMix;
            double shiftMult = 1.0 + mix.ShiftMultiplierPerShift * (shift - 1);
            double distMult = 1.0 + mix.DistanceMultiplierPerDistrict * (dest.Address.District - 1);
            return new MailItem
            {
                Id = _nextMailId++,
                Kind = kind.Index,
                Address = dest.Address,
                Value = (ushort)Math.Min(ushort.MaxValue, Math.Round(kind.BaseValue * distMult * shiftMult * valueMult)),
                SpawnShift = (byte)shift,
                DeadlineShift = (byte)(shift + kind.DeadlineOffsetShifts),
                Flags = MailFlags.Lost,
            };
        }

        /// <summary>Puts an existing item back into the Intake (or the backlog), e.g. when its carrier leaves.</summary>
        public void ReturnToIntake(MailItem item)
        {
            if (_backlog.Count > 0 || !InventoryOps.AutoPlace(_content, _intake, ItemStack.OfMail(item), _entryIds))
                _backlog.Enqueue(item);
        }

        private void ScheduleNextBatch()
        {
            var mix = _content.MailMix;
            double jitter = mix.BatchJitterSeconds;
            int jitterTicks = (int)SimConstants.SecondsToTicks(jitter);
            int offset = jitterTicks > 0 ? _spawnRng.NextRange(-jitterTicks, jitterTicks) : 0;
            long at = _startTick + (long)_nextBatch * SimConstants.SecondsToTicks(mix.BatchIntervalSeconds) + offset;
            _nextBatchTick = (uint)Math.Max(_startTick, at);
        }

        private int SpawnBatch()
        {
            var mix = _content.MailMix;
            if (!mix.Shares.TryGetValue(_shift, out var shares)) return 0;
            double shiftMult = 1.0 + mix.ShiftMultiplierPerShift * (_shift - 1);
            _batchStreets.Clear();
            int spawned = 0;

            // Accrue this batch's budget per kind; a kind with nowhere to go hands its share to the
            // largest kind that has a destination (doc 03 §1.3: Cargo → Large).
            long orphaned = 0;
            MailKindDef? largest = null;
            foreach (var share in shares)
            {
                var kind = _content.MailKind(share.Key);
                long budget = (long)Math.Round(_batchBudget * share.Value);
                // So does a kind whose share of the whole shift buys not one piece (a Cargo crate is
                // worth a big share of a small team's shift; design choice 114): the value is not lost.
                bool affordable = budget * _batchCount >= (long)Math.Round(kind.BaseValue * shiftMult) * Scale;
                if (!HasDestination(kind) || !affordable) { orphaned += budget; continue; }
                _accrued[kind.Index] += budget;
                if (largest == null || kind.GridW * kind.GridH > largest.GridW * largest.GridH) largest = kind;
            }
            if (largest != null) _accrued[largest.Index] += orphaned;

            foreach (var share in shares)
            {
                var kind = _content.MailKind(share.Key);
                CollectEligible(kind);
                if (_eligible.Count == 0) continue;
                long minValue = (long)Math.Round(kind.BaseValue * shiftMult) * Scale;
                while (_accrued[kind.Index] >= minValue)
                {
                    var dest = PickDestination();
                    double distMult = 1.0 + mix.DistanceMultiplierPerDistrict * (dest.Address.District - 1);
                    int value = (int)Math.Min(ushort.MaxValue, Math.Round(kind.BaseValue * distMult * shiftMult));
                    _accrued[kind.Index] -= value * Scale;
                    _batchStreets.Add(dest.Street.Id);

                    var item = new MailItem
                    {
                        Id = _nextMailId++,
                        Kind = kind.Index,
                        Address = dest.Address,
                        Value = (ushort)value,
                        SpawnShift = (byte)_shift,
                        DeadlineShift = (byte)(_shift + kind.DeadlineOffsetShifts),
                    };
                    Curse(ref item);
                    Prioritize(ref item);
                    ReturnToIntake(item);
                    spawned++;
                }
            }
            return spawned;
        }

        /// <summary>
        /// About how many pieces a Delivery of <paramref name="shift"/> worth <paramref name="spawnValue"/> brings each
        /// street (doc 10 §4.3 Forecast Desk): each kind's share of the value over its price, spread evenly over the
        /// houses open to it. An estimate, drawing nothing from the streams. Streets with none are left out.
        /// </summary>
        public Dictionary<int, double> Forecast(int shift, int spawnValue)
        {
            var streets = new Dictionary<int, double>();
            var mix = _content.MailMix;
            if (spawnValue <= 0 || !mix.Shares.TryGetValue(shift, out var shares)) return streets;
            int was = _shift;
            _shift = shift;
            double shiftMult = 1.0 + mix.ShiftMultiplierPerShift * (shift - 1);
            foreach (var share in shares)
            {
                var kind = _content.MailKind(share.Key);
                CollectEligible(kind);
                if (_eligible.Count == 0) continue;
                double price = 0;
                foreach (var d in _eligible) price += kind.BaseValue * (1.0 + mix.DistanceMultiplierPerDistrict * (d.Address.District - 1)) * shiftMult;
                price /= _eligible.Count;
                if (price <= 0) continue;
                double each = spawnValue * share.Value / price / _eligible.Count;
                foreach (var d in _eligible) streets[d.Street.Id] = (streets.TryGetValue(d.Street.Id, out double n) ? n : 0) + each;
            }
            _shift = was;
            return streets;
        }

        private bool HasDestination(MailKindDef kind)
        {
            foreach (var d in _map.Destinations)
                if (IsUnlocked(d) && DeliveryRules.KindAccepted(kind, d.Type)) return true;
            return false;
        }

        private void CollectEligible(MailKindDef kind)
        {
            _eligible.Clear();
            foreach (var d in _map.Destinations)
                if (IsUnlocked(d) && DeliveryRules.KindAccepted(kind, d.Type)) _eligible.Add(d);
        }

        private bool IsUnlocked(DestinationDef d) => _map.OpensAt(d, DistrictsEarly) <= _shift;

        /// <summary>Shifts before the map's that a district takes mail (doc 10 §4.3 Zoning Board); set by the world.</summary>
        public int DistrictsEarly { get; set; }

        /// <summary>Uniform over unlocked addresses, except that streetStreakRatio of picks reuse a street already in the batch.</summary>
        private DestinationDef PickDestination()
        {
            int streakPermille = (int)Math.Round(_content.MailMix.StreetStreakRatio * 1000.0);
            if (_batchStreets.Count > 0 && _addressRng.Chance(streakPermille))
            {
                _streak.Clear();
                foreach (var d in _eligible)
                    if (_batchStreets.Contains(d.Street.Id)) _streak.Add(d);
                if (_streak.Count > 0) return _streak[_addressRng.NextInt(_streak.Count)];
            }
            return _eligible[_addressRng.NextInt(_eligible.Count)];
        }

        private void DrainBacklog()
        {
            while (_backlog.Count > 0)
            {
                var stack = ItemStack.OfMail(_backlog.Peek());
                if (!InventoryOps.AutoPlace(_content, _intake, stack, _entryIds)) break;
                _backlog.Dequeue();
            }
            _intakeVersionAtLastDrain = _intake.Version;
        }
    }
}
