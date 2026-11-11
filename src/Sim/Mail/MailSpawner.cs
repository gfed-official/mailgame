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
        private readonly RngStream _spawnRng;
        private readonly RngStream _addressRng;

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

        public MailSpawner(ContentDb content, WorldMap map, GridContainer intake, EntryIdAllocator entryIds, uint seed)
        {
            _content = content;
            _map = map;
            _intake = intake;
            _entryIds = entryIds;
            _spawnRng = new RngStream(seed, "mail.spawn");
            _addressRng = new RngStream(seed, "mail.address");
            _accrued = new long[content.MailKinds.Count];
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
                if (!HasDestination(kind)) { orphaned += budget; continue; }
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
                    ReturnToIntake(item);
                    spawned++;
                }
            }
            return spawned;
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

        private bool IsUnlocked(DestinationDef d)
        {
            foreach (var district in _map.Districts)
                if (district.Id == d.Address.District) return district.UnlockShift <= _shift;
            return false;
        }

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
