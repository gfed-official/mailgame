using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Run events (doc 10 §6, doc 01 §3.3; design choice 162): from shift 2 on, as each shift begins, at most one event
    /// is rolled for it on the waves' own stream ("waves.events", so the mail and the raids draw what they always did):
    /// <c>runEventChance</c> that one comes, then one by weight of those that may come this shift. It happens at its
    /// trigger — as Prep begins, as Delivery begins, at a moment of Delivery, at Payday — and is live for the rest of the
    /// shift: its modifiers lie on the team's stat sheet with the stamps and the perks, its rules act through the handlers
    /// here (the closed <see cref="RunEventRule"/> set; no event id is read anywhere). The Postal Inspector is no roll: the
    /// complaint meter calls it at Payday for the next shift.
    /// </summary>
    public sealed partial class SimWorld
    {
        private RngStream? _eventRng;
        /// <summary>The event rolled for this shift, fired or still to come; null for none.</summary>
        public RunEventDef? ShiftEvent { get; private set; }
        /// <summary>The events live now, a bit per <see cref="RunEventDef.Index"/>.</summary>
        public byte LiveEvents { get; private set; }
        /// <summary>Events called for the next shift (the Postal Inspector), a bit each.</summary>
        private byte _comingEvents;
        /// <summary>Once-a-run events that have happened this run (Overtime).</summary>
        private byte _eventsUsed;
        /// <summary>A Delivery-triggered event's moment; 0 for none.</summary>
        private uint _eventFireTick;
        /// <summary>Street Festival's street (a street id) while it is live, else -1.</summary>
        public int FestivalStreet { get; private set; } = -1;
        private uint _wildfireEndTick, _wildfireNextTick;
        private readonly List<Construct> _burning = new List<Construct>();

        public bool EventLive(RunEventDef def) => (LiveEvents & (1 << def.Index)) != 0;

        /// <summary>A live event of the run has the rule; the first that has it, else null.</summary>
        public RunEventDef? LiveRule(RunEventRule rule)
        {
            if (LiveEvents == 0) return null;
            foreach (var e in Content.RunEvents)
                if (EventLive(e) && e.Has(rule)) return e;
            return null;
        }

        /// <summary>A run starts or ends: nothing rolled, nothing live, nothing used.</summary>
        private void ResetRunEvents()
        {
            _eventRng = new RngStream(Settings.Seed, "waves.events");
            ShiftEvent = null;
            _comingEvents = 0;
            _eventsUsed = 0;
            _eventFireTick = 0;
            _wildfireEndTick = 0;
            FestivalStreet = -1;
            SetLiveEvents(0);
        }

        private void SetLiveEvents(byte mask)
        {
            if (mask == LiveEvents) return;
            LiveEvents = mask;
            if (!LiveRuleOn(RunEventRule.StreetFestival)) FestivalStreet = -1;
            RebuildTeamSheet();
        }

        private bool LiveRuleOn(RunEventRule rule) => LiveRule(rule) != null;

        /// <summary>
        /// A shift begins: last shift's events are over, what was called for this one (the Inspector) is live, and this
        /// shift's event is rolled.
        /// </summary>
        private void BeginShiftEvents()
        {
            _eventFireTick = 0;
            _wildfireEndTick = 0;
            byte coming = _comingEvents;
            _comingEvents = 0;
            SetLiveEvents(coming);
            foreach (var e in Content.RunEvents)
                if ((coming & (1 << e.Index)) != 0) Events.Add(new RunEventEvent { Event = e.Index, Shift = (byte)Run.Shift, Stage = RunEventStage.Fired });
            ShiftEvent = RollEvent();
        }

        /// <summary>At most one event for this shift: none before shift 2, in the debug world or with events off.</summary>
        private RunEventDef? RollEvent()
        {
            if (!Settings.RunEvents || Run.Mode == RunMode.Practice || Map.Debug != null || Run.Shift < 2 || Content.RunEvents.Count == 0) return null;
            var rng = _eventRng ??= new RngStream(Settings.Seed, "waves.events");
            var open = new List<RunEventDef>();
            int total = 0;
            foreach (var e in Content.RunEvents)
            {
                if (!MayCome(e)) continue;
                open.Add(e);
                total += e.Weight;
            }
            if (Settings.ForcedEvent is { } forced)
            {
                foreach (var e in open)
                    if (e.Id == forced) return e;
                return null;
            }
            if (open.Count == 0 || total <= 0) return null;
            if (!rng.Chance((int)Math.Round(Content.Balance.GetDouble("runEventChance") * 1000.0))) return null;
            int pick = rng.NextInt(total);
            foreach (var e in open)
            {
                if (pick < e.Weight) return e;
                pick -= e.Weight;
            }
            return null;
        }

        private bool MayCome(RunEventDef e)
        {
            if (e.Trigger == RunEventTrigger.Complaint || e.Weight <= 0 || Run.Shift < e.MinShift) return false;
            if (e.OncePerRun && (_eventsUsed & (1 << e.Index)) != 0) return false;
            if (e.NeedsDock && OpenDocks().Count == 0) return false;
            return true;
        }

        /// <summary>The Business Docks open this shift.</summary>
        private List<DestinationDef> OpenDocks()
        {
            var docks = new List<DestinationDef>();
            foreach (var d in Map.Destinations)
                if (d.IsDock && Map.OpensAt(d, DistrictsEarly) <= Run.Shift) docks.Add(d);
            return docks;
        }

        /// <summary>Prep has begun: an event of Prep happens now.</summary>
        private void PrepEvents()
        {
            if (ShiftEvent is { Trigger: RunEventTrigger.Prep } e) FireEvent(e);
        }

        /// <summary>
        /// Delivery has begun (after the street was swept): Lost Parcels told of in Prep lie about now; an event of
        /// Delivery's start happens; one of Delivery gets its moment.
        /// </summary>
        private void DeliveryEvents(uint durationTicks)
        {
            if (LiveRule(RunEventRule.LostParcels) is { } lost) ScatterLostParcels(lost);
            if (ShiftEvent is not { } e) return;
            if (e.Trigger == RunEventTrigger.DeliveryStart) FireEvent(e);
            else if (e.Trigger == RunEventTrigger.Delivery)
            {
                var rng = _eventRng ??= new RngStream(Settings.Seed, "waves.events");
                int from = (int)(durationTicks * Math.Clamp(e.Param("earliest", 0.1), 0.0, 1.0));
                int to = Math.Max(from, (int)(durationTicks * Math.Clamp(e.Param("latest", 0.6), 0.0, 1.0)));
                _eventFireTick = Tick + 1 + (uint)rng.NextRange(from, to);
            }
        }

        /// <summary>Every tick: an event of Delivery whose moment has come, and the wildfire burning.</summary>
        private void StepRunEvents()
        {
            if (Run.Phase != RunPhase.Delivery) return;
            if (_eventFireTick != 0 && Tick >= _eventFireTick && ShiftEvent is { } e)
            {
                _eventFireTick = 0;
                FireEvent(e);
            }
            if (_wildfireEndTick != 0) StepWildfire();
        }

        /// <summary>
        /// The event happens: live from now, its numbers on the team's sheet, and what its rules do at once. The feed says
        /// so (<see cref="RunEventEvent"/>, <see cref="RunEventStage.Fired"/>).
        /// </summary>
        private RunEventEvent FireEvent(RunEventDef e)
        {
            var rng = _eventRng ??= new RngStream(Settings.Seed, "waves.events");
            if (e.OncePerRun) _eventsUsed |= (byte)(1 << e.Index);
            SetLiveEvents((byte)(LiveEvents | (1 << e.Index)));
            var ev = new RunEventEvent { Event = e.Index, Shift = (byte)Run.Shift, Stage = RunEventStage.Fired };
            if (e.Has(RunEventRule.StreetFestival))
            {
                FestivalStreet = PickFestivalStreet(e, rng);
                ev.Street = (byte)(FestivalStreet + 1);
            }
            if (e.Has(RunEventRule.LostParcels)) ev.Amount = (int)e.Param("count");
            if (e.Has(RunEventRule.LostParcels) && Run.Phase == RunPhase.Delivery) ScatterLostParcels(e);
            if (e.Has(RunEventRule.BulkDrop)) ev.Amount = BulkDrop(e);
            if (e.Has(RunEventRule.Wildfire))
            {
                _wildfireEndTick = Tick + SimConstants.SecondsToTicks(e.Param("seconds"));
                _wildfireNextTick = Tick + SimConstants.SecondsToTicks(1.0);
                ev.Amount = (int)e.Param("seconds");
            }
            Events.Add(ev);
            return ev;
        }

        // --- Street Festival ----------------------------------------------------------------------

        /// <summary>A street with a house open this shift that takes the festival's mail; -1 if there is none.</summary>
        private int PickFestivalStreet(RunEventDef e, RngStream rng)
        {
            var streets = new List<int>();
            foreach (var d in Map.Destinations)
            {
                if (d.IsDock || Map.OpensAt(d, DistrictsEarly) > Run.Shift || streets.Contains(d.Street.Id)) continue;
                foreach (var kind in e.Kinds)
                    if (DeliveryRules.KindAccepted(kind, d.Type)) { streets.Add(d.Street.Id); break; }
            }
            if (streets.Count == 0) return -1;
            streets.Sort();
            return streets[rng.NextInt(streets.Count)];
        }

        /// <summary>The street closed to vehicles and how fast they crawl through its crowd; an empty rect while none is.</summary>
        private (TileRect Rect, float Mult) RoadClosure
        {
            get
            {
                if (FestivalStreet < 0 || LiveRule(RunEventRule.StreetFestival) is not { } e || Map.GetStreet(FestivalStreet) is not { } street) return (default, 1f);
                return (street.Rect, (float)e.Param("vehicleSpeed", 0.2));
            }
        }

        /// <summary>
        /// What a delivery pays with the live events: Street Festival's mail for its street pays more. The rules' own
        /// verdict otherwise (and the Inspector's share is on the team's sheet already, <see cref="Stat.DeliveryEarnings"/>).
        /// </summary>
        private DeliveryResult EventPay(DeliveryResult verdict, MailKindDef kind, DestinationDef dest)
        {
            if (verdict.Outcome != DeliveryOutcome.Delivered || FestivalStreet < 0 || dest.Street.Id != FestivalStreet) return verdict;
            if (LiveRule(RunEventRule.StreetFestival) is not { } e || !e.HasKind(kind.Index)) return verdict;
            int paid = (int)Math.Round(verdict.Amount * e.Param("valueMult", 1));
            return new DeliveryResult(verdict.Outcome, paid, verdict.Complaint, verdict.Late);
        }

        // --- Lost Parcels -------------------------------------------------------------------------

        /// <summary>
        /// The Lost Parcels lie on the open streets, each in a bag that is nobody's until Delivery's end (the Mega's
        /// Lost Parcel path, design choice 142): whoever takes one delivers it like any mail; left lying, it goes back to
        /// the Intake with the bag.
        /// </summary>
        private void ScatterLostParcels(RunEventDef e)
        {
            var rng = _eventRng ??= new RngStream(Settings.Seed, "waves.events");
            var kind = e.Kinds[0];
            var streets = new List<StreetDef>();
            foreach (var d in Map.Destinations)
                if (!d.IsDock && Map.OpensAt(d, DistrictsEarly) <= Run.Shift && !streets.Contains(d.Street)) streets.Add(d.Street);
            if (streets.Count == 0) return;
            streets.Sort((a, b) => a.Id.CompareTo(b.Id));
            int count = (int)e.Param("count");
            for (int i = 0; i < count; i++)
            {
                var parcel = Spawner.LostParcel(kind, Math.Max(1, Run.Shift), e.Param("valueMult", 1), rng);
                if (parcel == null) return;
                if (!ParcelSpot(streets, rng, out float x, out float z)) return;
                var item = parcel.Value;
                uint bag = DropAsBag(ItemStack.OfMail(item), x, z, Run.PhaseEndTick);
                if (bag == 0) continue;
                Events.Add(new LostParcelEvent { Enemy = LostParcelEvent.NoEnemy, Kind = item.Kind, Address = item.Address, Value = item.Value, Bag = bag });
            }
        }

        /// <summary>A walkable tile of one of the streets, picked at random; false after a few misses.</summary>
        private bool ParcelSpot(List<StreetDef> streets, RngStream rng, out float x, out float z)
        {
            for (int tries = 0; tries < 24; tries++)
            {
                var rect = streets[rng.NextInt(streets.Count)].Rect;
                if (rect.W <= 0 || rect.H <= 0) continue;
                int tx = rect.X + rng.NextInt(rect.W), tz = rect.Y + rng.NextInt(rect.H);
                if (Map.IsSolid(tx, tz) || Map.IsWater(tx, tz)) continue;
                x = (tx + 0.5f) * SimConstants.TileSize;
                z = (tz + 0.5f) * SimConstants.TileSize;
                return true;
            }
            x = z = 0f;
            return false;
        }

        /// <summary>Mega Migration: a felled Mega drops this many Lost Parcels (1 without it).</summary>
        private int MegaParcels => LiveRule(RunEventRule.MegaParcels) is { } e ? Math.Max(1, (int)e.Param("parcels", 1)) : 1;

        // --- Wildfire -----------------------------------------------------------------------------

        /// <summary>Once a second while it burns: each building it names takes its damage, unless water is near.</summary>
        private void StepWildfire()
        {
            var e = LiveRule(RunEventRule.Wildfire);
            if (e == null || Tick >= _wildfireEndTick)
            {
                EndWildfire(e);
                return;
            }
            if (Tick < _wildfireNextTick) return;
            _wildfireNextTick = Tick + SimConstants.SecondsToTicks(1.0);
            float metres = (float)e.Param("waterMetres", 0);
            float damage = (float)e.Param("damagePerSecond", 1);
            _burning.Clear();
            foreach (var c in Constructs.All)
                if (e.HasBuilding(c.Def) && !NearWater(c.CentreX, c.CentreZ, metres)) _burning.Add(c);
            foreach (var c in _burning) DamageConstruct(c, damage);
        }

        private void EndWildfire(RunEventDef? e)
        {
            _wildfireEndTick = 0;
            if (e == null) return;
            SetLiveEvents((byte)(LiveEvents & ~(1 << e.Index)));
            Events.Add(new RunEventEvent { Event = e.Index, Shift = (byte)Run.Shift, Stage = RunEventStage.Ended });
        }

        /// <summary>Water within <paramref name="metres"/> of the point.</summary>
        private bool NearWater(float x, float z, float metres)
        {
            if (metres <= 0f) return false;
            int r = (int)MathF.Ceiling(metres / SimConstants.TileSize);
            int cx = (int)MathF.Floor(x / SimConstants.TileSize), cz = (int)MathF.Floor(z / SimConstants.TileSize);
            for (int tz = cz - r; tz <= cz + r; tz++)
                for (int tx = cx - r; tx <= cx + r; tx++)
                {
                    if (!Map.IsWater(tx, tz)) continue;
                    float dx = (tx + 0.5f) * SimConstants.TileSize - x, dz = (tz + 0.5f) * SimConstants.TileSize - z;
                    if (dx * dx + dz * dz <= metres * metres) return true;
                }
            return false;
        }

        // --- Bulk Drop ----------------------------------------------------------------------------

        /// <summary>A piece of the event's kind for every Business Dock open this shift, worth more, into the Intake. Returns how many.</summary>
        private int BulkDrop(RunEventDef e)
        {
            var kind = e.Kinds[0];
            int n = 0;
            foreach (var dock in OpenDocks())
            {
                if (!DeliveryRules.KindAccepted(kind, dock.Type)) continue;
                Spawner.SpawnBulk(kind, dock, Math.Max(1, Run.Shift), e.Param("valueMult", 1));
                n++;
            }
            if (n > 0) CommitContainer(Intake.Id);
            return n;
        }

        // --- Payday: Overtime, Neighbourhood Watch, the Postal Inspector ----------------------------

        /// <summary>
        /// Overtime (doc 10 §6): rolled for this shift, once a run, a quota missed by its shortfall or less keeps Delivery
        /// going its seconds more; the next Payday checks the quota again. True if Delivery goes on.
        /// </summary>
        private bool TryOvertime()
        {
            if (ShiftEvent is not { } e || !e.Has(RunEventRule.Overtime) || (_eventsUsed & (1 << e.Index)) != 0) return false;
            int earned = Economy.ShiftEarnings, quota = Run.Quota;
            if (earned >= quota || earned < quota * (1.0 - e.Param("shortfall"))) return false;
            FireEvent(e).Amount = quota - earned;
            Run.PhaseEndTick = Tick + SimConstants.SecondsToTicks(e.Param("seconds"));
            return true;
        }

        /// <summary>
        /// At Payday, before the summary is sent: Neighbourhood Watch pays its bonus (into the wallet, as the shift bonus:
        /// not towards the quota) or says why not; the complaint meter calls the Postal Inspector for the next shift.
        /// Returns the bonus paid.
        /// </summary>
        private int PaydayEvents()
        {
            int paid = 0;
            if (LiveRule(RunEventRule.NeighbourhoodWatch) is { } watch)
            {
                bool clean = Economy.ShiftMisdeliveries == 0 && Economy.ShiftEarnings > 0;
                if (clean) paid = Economy.PayEventBonus((int)Math.Round(Economy.ShiftEarnings * watch.Param("bonus")));
                Events.Add(new RunEventEvent { Event = watch.Index, Shift = (byte)Run.Shift, Stage = clean ? RunEventStage.Paid : RunEventStage.Fizzled, Amount = paid });
            }
            if (!Settings.RunEvents || Run.Mode == RunMode.Practice || Map.Debug != null || Run.Shift >= ShiftCount) return paid;
            foreach (var e in Content.RunEvents)
            {
                if (e.Trigger != RunEventTrigger.Complaint) continue;
                float threshold = TeamStat(Stat.InspectorComplaint, (float)e.Param("complaint"));
                if (Economy.Complaint < threshold) continue;
                _comingEvents |= (byte)(1 << e.Index);
                Events.Add(new RunEventEvent { Event = e.Index, Shift = (byte)Run.Shift, Stage = RunEventStage.Coming, Amount = Economy.Complaint });
            }
            return paid;
        }
    }
}
