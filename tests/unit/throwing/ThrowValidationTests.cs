using System.Buffers;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.Inventory;
using PerformativeMail.Sim.Tests.Unit.PlayerCharacter;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Sim.Tests.Unit.ThrowingCatching;

/// <summary>
/// Throwing story 009 (ADR-0015 section 1, ADR-0007, ADR-0014 section 5; GDD T1, T4 "Host validation", Edge Cases): the pure
/// <c>ThrowRelease</c> validation chain, <see cref="ThrowValidator"/>. The first failing check is the reply, check 7 accepts every request
/// at M-1a (E5), check 9 never rejects, and a rejection changes no Grid or Throwing state and raises no event. The facts come from two
/// real fixtures that share slot 0: the full M-1a world (<see cref="PlayersRig"/>: Players, the Throwing flight table, <c>Tat</c> and
/// counters) and an Inventory rig (<see cref="InvAutoPlaceRig"/>, which can place hotbar entries in a tick). The selected slot and the
/// position history are doubles (the production selection, <see cref="PlayersHotbarSelection"/>, is driven by the <c>HotbarSelect</c> op in
/// <c>HotbarSelectTests</c>; the history double holds several samples).
/// </summary>
public class ThrowValidationTests
{
    private const uint Address4 = 4u;
    private const byte SelectedIndex = 3;

    // A valid direction: n = 16 384, elevation 207 permille (story 001's F1 example).
    private const short DirY = 3406;
    private const short DirZ = -16_026;

    // ---------------------------------------------------------------- TC-T4-1: precedence, then single violations

    /// <summary>The request violates check N and every later check that can co-occur; the reply is check N's.</summary>
    [Theory]
    [Trait("Id", "TC-T4-1")]
    [InlineData(1, ThrowReasons.BadRequest)]
    [InlineData(2, (ushort)ThrowReason.StaleEntry)]
    [InlineData(3, (ushort)ThrowReason.Dead)]
    [InlineData(4, (ushort)ThrowReason.Frozen)]
    [InlineData(5, (ushort)ThrowReason.Swimming)]
    [InlineData(6, (ushort)ThrowReason.Unavailable)]
    //// Story 028 (M0, the leaky bucket) adds: [InlineData(7, (ushort)ThrowReason.ThrowCooldown)]
    [InlineData(8, (ushort)ThrowReason.BadDirection)]
    public void ViolatingCheckNAndEveryLaterCheck_RepliesCheckN_AndChangesNothing(int n, ushort expected)
    {
        // Arrange
        var rig = new Rig();
        ThrowReleasePayload p = rig.Valid();
        rig.Violate(n, ref p);
        Snapshot before = rig.Snapshot();

        // Act
        ThrowCheck r = rig.Validate(in p, out ThrowPlan plan);

        // Assert: check N's reply; TC-T4-1 d: no Grid, Players or Throwing state changed, no event, the plan empty, one rejection counted
        Assert.Equal(expected, r.Reason);
        Assert.Equal(default, plan.Entry);
        before.AssertUnchanged(rig.Snapshot());
        Assert.Equal(before.Rejections + 1, rig.Counters[CounterId.ThrowingReleaseRejections]);
    }

    [Theory]
    [Trait("Id", "TC-T4-1")]
    [InlineData("side 0 (malformed)", ThrowReasons.BadRequest)]
    [InlineData("flags set (malformed)", ThrowReasons.BadRequest)]
    [InlineData("27 B payload (malformed)", ThrowReasons.BadRequest)]
    [InlineData("bucket empty (check 7, M-1a stub)", (ushort)0)]
    [InlineData("zero direction (check 8)", (ushort)ThrowReason.BadDirection)]
    [InlineData("pitch above 980 permille (check 8)", (ushort)ThrowReason.BadDirection)]
    public void OnlyOneCheckViolated_RepliesItsReason_AndCheck7AcceptsAtM1a(string violation, ushort expected)
    {
        // Arrange
        var rig = new Rig();
        ThrowReleasePayload p = rig.Valid();
        var op = new Op { Kind = ThrowOpKinds.ThrowRelease, Actor = rig.Actor, ReqId = 9 };
        switch (violation)
        {
            case "side 0 (malformed)":
                p.Side = 0;
                break;
            case "flags set (malformed)":
                p.Flags = 1;
                break;
            case "bucket empty (check 7, M-1a stub)":
                rig.Tat = uint.MaxValue; // T far below TAT - throwBurstTicks: the M0 bucket would refuse
                break;
            case "zero direction (check 8)":
                (p.DirX, p.DirY, p.DirZ) = (0, 0, 0);
                break;
            case "pitch above 980 permille (check 8)":
                (p.DirX, p.DirY, p.DirZ) = (0, 16_100, -2_900); // n = 16 359, elevation 984
                break;
        }

        op.SetPayload(in p);
        if (violation == "27 B payload (malformed)")
        {
            op.PayloadLength = 27;
        }

        // Act
        ThrowCheck r = ThrowValidator.Validate(in op, rig.Tick, rig.Facts, out _);

        // Assert
        Assert.Equal(expected, r.Reason);
    }

    // ---------------------------------------------------------------- TC-T1-1: who can throw

    [Theory]
    [Trait("Id", "TC-T1-1")]
    [InlineData("walking", (ushort)0)]
    [InlineData("sprinting", (ushort)0)]
    [InlineData("airborne", (ushort)0)]
    [InlineData("wading at 899 mm, entered from dry land", (ushort)0)]
    [InlineData("on a support (velocity not inherited)", (ushort)0)]
    [InlineData("spawn-protected", (ushort)0)]
    [InlineData("still swimming at 800 mm", (ushort)ThrowReason.Swimming)]
    [InlineData("dead", (ushort)ThrowReason.Dead)]
    [InlineData("on break", (ushort)ThrowReason.Frozen)]
    [InlineData("mounted", (ushort)ThrowReason.Unavailable)]
    public void CarrierState_ValidatesOrRepliesItsReason_AndNeverTouchesSpawnProtection(string carrier, ushort expected)
    {
        // Arrange: the host learns sprinting and swimming only from the owner's sample bits; the other states live in the record
        var rig = new Rig();
        SampleFlags flags = carrier switch
        {
            "sprinting" => SampleFlags.Sprinting,
            "still swimming at 800 mm" => SampleFlags.Swimming,
            _ => SampleFlags.None,
        };
        rig.Pc.Report(0, rig.Spawn, flags);
        rig.Pc.TickEmpty(1);
        ref PlayerRecord record = ref rig.Pc.Record(0);
        record.ProtectedUntilTick = rig.Tick + 90;
        switch (carrier)
        {
            case "airborne":
                record.Carrier.Loco = LocoState.Airborne;
                record.Carrier.Vy = 4_000;
                break;
            case "wading at 899 mm, entered from dry land":
                record.Carrier.WadeSlowPm = 600;
                break;
            case "on a support (velocity not inherited)":
                record.Carrier.SupportId = 77;
                record.Carrier.Vx = 9_000;
                break;
            case "still swimming at 800 mm":
                record.Carrier.Loco = LocoState.Swimming;
                break;
            case "dead":
                record.Life = LifeState.Dead;
                break;
            case "on break":
                record.Life = LifeState.Frozen;
                break;
            case "mounted":
                record.Carrier.Loco = LocoState.Mounted;
                rig.Players.Mounted = true;
                break;
        }

        uint protectedUntil = record.ProtectedUntilTick;
        Snapshot before = rig.Snapshot();

        // Act
        ThrowCheck r = rig.Validate(rig.Valid(), out _);

        // Assert: the reply, and spawn protection (indeed the whole world) untouched
        Assert.Equal(expected, r.Reason);
        Assert.Equal(protectedUntil, rig.Pc.Record(0).ProtectedUntilTick);
        before.AssertUnchanged(rig.Snapshot());
    }

    // ---------------------------------------------------------------- TC-E-5 a: the entry changed after aiming

    [Theory]
    [Trait("Id", "TC-E-5")]
    [InlineData("merged (saw 4, now 5)", (ushort)ThrowReason.StaleEntry)]
    [InlineData("split (saw 6, now 5)", (ushort)ThrowReason.StaleEntry)]
    [InlineData("sorted into a new entry (id retired)", (ushort)ThrowReason.StaleEntry)]
    [InlineData("selection moved off it", (ushort)ThrowReason.StaleEntry)]
    [InlineData("replayed seq after its flight consumed it (entry unknown)", (ushort)ThrowReason.StaleEntry)]
    [InlineData("replayed seq while its flight is in the air (entry detached)", ThrowReasons.BadRequest)]
    [InlineData("the cursor entry", ThrowReasons.BadRequest)]
    [InlineData("a container entry", ThrowReasons.BadRequest)]
    [InlineData("another player's hotbar entry", ThrowReasons.BadRequest)]
    public void EntryChangedAfterAiming_IsStaleOrBadRequest_TheSlotKeepsItsContents_AndACounterTicks(string change, ushort expected)
    {
        // Arrange: 5 letters at the selected hotbar index 3 as the aimed entry
        var rig = new Rig();
        InvAutoPlaceRig inv = rig.Inv;
        ThrowReleasePayload p = rig.Valid();
        PlayerSlot a = PlayerSlot.FromValue(0);
        ContainerId hotbarA = inv.Inventory.HotbarOf(a);
        switch (change)
        {
            case "merged (saw 4, now 5)":
                p.Units = 4;
                break;
            case "split (saw 6, now 5)":
                p.Units = 6;
                break;
            case "sorted into a new entry (id retired)":
                ConsumeAll(inv, hotbarA, rig.Entry);
                _ = inv.PlaceMail(inv.HotbarA, "letter", SelectedIndex, 0, Address4, new uint[] { 21, 22, 23, 24, 25 });
                break;
            case "selection moved off it":
                rig.Selection.Index = 0;
                break;
            case "replayed seq after its flight consumed it (entry unknown)":
                ConsumeAll(inv, hotbarA, rig.Entry);
                break;
            case "replayed seq while its flight is in the air (entry detached)":
                Assert.Equal(new DetachResult(default, rig.Entry, 5), IntakeSpawnOutfeedTests.InPhase(inv, () => inv.Inventory.DetachForThrow(a, rig.Entry, 5)));
                break;
            case "the cursor entry":
                p = rig.Valid(inv.Entries[inv.PlaceMail(inv.CursorA, "letter", 0, 0, Address4, new uint[] { 31, 32 })].Id, 2);
                break;
            case "a container entry":
                p = rig.Valid(inv.Entries[inv.PlaceMail(inv.InventoryA, "letter", 0, 0, Address4, new uint[] { 33 })].Id, 1);
                break;
            case "another player's hotbar entry":
                p = rig.Valid(inv.Entries[inv.PlaceMail(inv.HotbarOf(1), "letter", SelectedIndex, 0, Address4, new uint[] { 34 })].Id, 1);
                break;
        }

        Snapshot before = rig.Snapshot();

        // Act
        ThrowCheck r = rig.Validate(in p, out _);

        // Assert
        Assert.Equal(expected, r.Reason);
        before.AssertUnchanged(rig.Snapshot());
        Assert.Equal(before.Rejections + 1, rig.Counters[CounterId.ThrowingReleaseRejections]);
    }

    // ---------------------------------------------------------------- TC-E-5 / TC-E-4 / TC-E-6 a: liveness at the apply tick

    [Theory]
    [Trait("Id", "TC-E-5")]
    [Trait("Id", "TC-E-6")]
    [InlineData(LifeState.Dead, (ushort)ThrowReason.Dead)]       // a (fixture) death
    [InlineData(LifeState.Frozen, (ushort)ThrowReason.Frozen)]   // a disconnect: on break
    public void ReleaseAppliedWhileAlive_Validates_TheSameReleaseOneTickAfterTheChange_IsRefused(LifeState after, ushort expected)
    {
        // Arrange
        var rig = new Rig();
        ThrowReleasePayload p = rig.Valid();

        // Act
        ThrowCheck alive = rig.Validate(in p, out _);
        rig.Pc.Record(0).Life = after;
        rig.Pc.TickEmpty(1);
        ThrowCheck later = rig.Validate(in p, out _);

        // Assert
        Assert.True(alive.IsOk);
        Assert.Equal(expected, later.Reason);
    }

    // ---------------------------------------------------------------- TC-T4-1 b: claim-and-corroborate never rejects

    [Theory]
    [Trait("Id", "TC-T4-1")]
    [InlineData("1499 mm from an older sample", false)]
    [InlineData("1500 mm (3D) from the newest", false)]
    [InlineData("1501 mm from the newest", true)]
    [InlineData("no history sample", true)]
    public void ClaimedFeet_WithinToleranceOfASample_AreUsed_ElseTheHostPosition_AndNeverRejected(string claim, bool clamped)
    {
        // Arrange: history [older, newest]; the host's last known position is used when there is no sample at all
        var rig = new Rig();
        Vec3Mm newest = rig.Spawn;
        var older = new Vec3Mm(newest.X + 4_000, newest.Y, newest.Z);
        var lastKnown = new Vec3Mm(newest.X - 7_000, newest.Y, newest.Z);
        rig.History.Samples.AddRange(new[] { older, newest });
        rig.History.LastKnown = lastKnown;
        Vec3Mm feet = claim switch
        {
            "1499 mm from an older sample" => new Vec3Mm(older.X + 1_499, older.Y, older.Z),
            "1500 mm (3D) from the newest" => new Vec3Mm(newest.X + 900, newest.Y + 1_200, newest.Z),
            "1501 mm from the newest" => new Vec3Mm(newest.X, newest.Y, newest.Z + 1_501),
            _ => newest,
        };
        if (claim == "no history sample")
        {
            rig.History.Samples.Clear();
        }

        ThrowReleasePayload p = rig.Valid();
        (p.FeetX, p.FeetY, p.FeetZ) = (feet.X, feet.Y, feet.Z);
        Vec3Mm expectedFeet = !clamped ? feet : claim == "no history sample" ? lastKnown : newest;

        // Act
        ThrowCheck r = rig.Validate(in p, out ThrowPlan plan);

        // Assert
        Assert.True(r.IsOk);
        Assert.Equal(expectedFeet, plan.FeetUsed);
        Assert.Equal(clamped, plan.OriginClamped);
        Assert.Equal(0L, rig.Counters[CounterId.ThrowingReleaseRejections]);
    }

    [Theory]
    [Trait("Id", "TC-T4-1")]
    [InlineData(0, 30u, false)]      // the claim at the pad, position 30 ticks old: fresh, corroborated
    [InlineData(1_501, 0u, true)]    // fresh, 1501 mm off: the newest sample
    [InlineData(0, 31u, true)]       // stale beyond 30 ticks: no sample, the last known position
    public void ClaimedFeet_ThroughPlayersHistory_AThrowerWhoNeverReportedIsNeverRejected(int claimDx, uint age, bool clamped)
    {
        // Arrange: the production adapter over IPlayersApi; slot 0 never sent a sample, so its host position is the pad seed
        var rig = new Rig();
        rig.Corroborator = new PlayersClaimCorroborator(rig.Players);
        rig.Pc.AgeTo(0, age);
        ThrowReleasePayload p = rig.Valid();
        p.FeetX += claimDx;

        // Act
        ThrowCheck r = rig.Validate(in p, out ThrowPlan plan);

        // Assert
        Assert.True(r.IsOk);
        Assert.Equal(clamped, plan.OriginClamped);
        Assert.Equal(clamped ? rig.Spawn : new Vec3Mm(p.FeetX, p.FeetY, p.FeetZ), plan.FeetUsed);
    }

    // ---------------------------------------------------------------- Output plan

    [Fact]
    [Trait("Id", "TC-T4-1")]
    public void ValidRelease_ReturnsThePlanStory010Commits_WithTheSlotsCurrentTat()
    {
        // Arrange: a left-handed release; the slot's Tat is already 1234 (story 028 writes it at M0)
        var rig = new Rig();
        rig.Tat = 1_234;
        ThrowReleasePayload p = rig.Valid();
        p.Side = -1;

        // Act
        ThrowCheck r = rig.Validate(in p, out ThrowPlan plan);

        // Assert
        Assert.True(r.IsOk);
        Assert.Equal(
            (PlayerSlot.FromValue(0), rig.Entry, (byte)5, rig.Spawn, false, (short)0, DirY, DirZ, (sbyte)-1, 1_234u),
            (plan.Thrower, plan.Entry, plan.Units, plan.FeetUsed, plan.OriginClamped, plan.DirX, plan.DirY, plan.DirZ, plan.Side, plan.TatNext));
    }

    // ---------------------------------------------------------------- liveFlights: flight_cap

    [Fact]
    [Trait("Id", "TC-T4-1")]
    public void With160FlightsLive_The161stReleaseIsTheOnlyOneRefused_FlightCap_AndCounted()
    {
        // Arrange: each accepted release launches one flight (story 010's append, done here by the test)
        var rig = new Rig();
        ThrowReleasePayload p = rig.Valid();
        var refused = new List<int>();
        ushort lastReason = 0;

        // Act
        for (int i = 0; i < 161; i++)
        {
            ThrowCheck r = rig.Validate(in p, out _);
            if (r.IsOk)
            {
                FlightInit init = FlightTableTests.Init(i);
                Assert.True(rig.Throwing.Flights.TryAppend(in init, out _));
            }
            else
            {
                refused.Add(i);
                lastReason = r.Reason;
            }
        }

        // Assert
        Assert.Equal(new[] { 160 }, refused);
        Assert.Equal((ushort)ThrowReason.FlightCap, lastReason);
        Assert.Equal(160, rig.Throwing.LiveFlights);
        Assert.Equal(1L, rig.Counters[CounterId.ThrowingFlightCapRefusals]);
    }

    // ---------------------------------------------------------------- helpers

    private static void ConsumeAll(InvAutoPlaceRig inv, ContainerId hotbar, EntryId entry)
    {
        bool consumed = IntakeSpawnOutfeedTests.InPhase(inv, () =>
        {
            bool all = true;
            for (int u = 0; u < 5; u++)
            {
                all &= inv.Inventory.ConsumeUnit(new EntryRef(hotbar, entry), ConsumeReason.Delivered, inv.PlayerA);
            }

            return all;
        });
        Assert.True(consumed);
        Assert.False(inv.Entries.TryGetSlot(entry, out _));
    }

    /// <summary>The two worlds' canonical dumps, event-log counts and the rejection counter at one moment.</summary>
    private readonly record struct Snapshot(byte[] World, byte[] Grid, int WorldEvents, int GridEvents, long Rejections)
    {
        public void AssertUnchanged(Snapshot after)
        {
            Assert.Equal(World, after.World);   // Players, the Throwing section (flights, serial, Tat) and the RNG positions
            Assert.Equal(Grid, after.Grid);     // every container's version, cells and entry counts
            Assert.Equal(WorldEvents, after.WorldEvents);
            Assert.Equal(GridEvents, after.GridEvents);
        }
    }

    private sealed class Rig
    {
        public Rig()
        {
            Pc = PlayersRig.Create();
            Pc.TickEmpty(1);
            Pc.JoinAndTick(0);
            Inv = new InvAutoPlaceRig(joinPlayerB: true);
            Entry = Inv.Entries[Inv.PlaceMail(Inv.HotbarA, "letter", SelectedIndex, 0, Address4, new uint[] { 11, 12, 13, 14, 15 })].Id;
            Players = new MountablePlayers(Pc.Players);
            Throwing = Pc.Host.Systems.OfType<ThrowingSystem>().Single();
            History.Samples.Add(Spawn);
            History.LastKnown = Spawn;
        }

        public PlayersRig Pc { get; }

        public InvAutoPlaceRig Inv { get; }

        public EntryId Entry { get; }

        public MountablePlayers Players { get; }

        public ThrowingSystem Throwing { get; }

        public FixedSelection Selection { get; } = new() { Index = SelectedIndex };

        public SampleHistory History { get; } = new();

        /// <summary>The corroborator in the facts: the multi-sample <see cref="History"/> double unless a test sets the production adapter.</summary>
        public IClaimCorroborator? Corroborator { get; set; }

        public ActorId Actor => Pc.Actor(0);

        public Vec3Mm Spawn => Pc.Spawn(0);

        public uint Tick => Pc.Sim.CurrentTick;

        public SimCounters Counters => Pc.Sim.Counters;

        public uint Tat
        {
            get => Throwing.Tat(PlayerSlot.FromValue(0));
            set => Throwing.Tat(PlayerSlot.FromValue(0)) = value;
        }

        public ThrowHostFacts Facts => new(
            Inv.Inventory, Players, Pc.Host.Services.Slots, Selection, Corroborator ?? History, Pc.Players.Settings.ClaimToleranceMm, Throwing.Flights, Throwing.TatClocks, Counters);

        public ThrowReleasePayload Valid() => Valid(Entry, 5);

        public ThrowReleasePayload Valid(EntryId entry, byte units) => new()
        {
            EntryId = entry.Value, Units = units, Side = 1, FeetX = Spawn.X, FeetY = Spawn.Y, FeetZ = Spawn.Z, DirX = 0, DirY = DirY, DirZ = DirZ,
        };

        public ThrowCheck Validate(in ThrowReleasePayload p, out ThrowPlan plan) => ThrowValidator.Validate(in p, Actor, Tick, Facts, out plan);

        /// <summary>Violates check <paramref name="n"/> and every later check that can co-occur with it (9 never rejects; 7 is a stub).</summary>
        public void Violate(int n, ref ThrowReleasePayload p)
        {
            // 5 first: the swimming bit arrives with a sample, which the Players step stores only for an Alive player.
            if (n <= 5)
            {
                Pc.Report(0, Spawn, SampleFlags.Swimming);
                Pc.TickEmpty(1);
            }

            if (n == 1)
            {
                p = Valid(Inv.Entries[Inv.PlaceMail(Inv.InventoryA, "letter", 0, 0, Address4, new uint[] { 41, 42 })].Id, 1); // 1: not in the hotbar; 2: count 2 != 1
            }

            if (n <= 2)
            {
                p.Units = 4;              // 2: count 5 != units 4
                Selection.Index = 0;      // 2: not the selected slot
            }

            if (n <= 4)
            {
                Pc.Record(0).Life = n == 4 ? LifeState.Frozen : LifeState.DeadAway; // DeadAway is dead (3) and away (4)
            }

            if (n <= 6)
            {
                Players.Mounted = true;   // 6
            }

            if (n <= 7)
            {
                Tat = uint.MaxValue;      // 7: the M0 bucket would refuse
            }

            if (n <= 8)
            {
                (p.DirX, p.DirY, p.DirZ) = (0, 0, 0); // 8
            }

            (p.FeetX, p.FeetY, p.FeetZ) = (Spawn.X + 50_000, Spawn.Y, Spawn.Z); // 9: uncorroborated (never a rejection)
            for (int i = 0; i < Throwing.Flights.Capacity; i++)                    // 10: every flight slot in use
            {
                FlightInit init = FlightTableTests.Init(i);
                Assert.True(Throwing.Flights.TryAppend(in init, out _));
            }
        }

        public Snapshot Snapshot() => new(Dump(Pc.Sim), Dump(Inv.Host.World), Pc.Sim.Events.Count, Inv.Host.World.Events.Count, Counters[CounterId.ThrowingReleaseRejections]);

        private static byte[] Dump(SimWorld world)
        {
            var output = new ArrayBufferWriter<byte>();
            Assert.True(world.TryWriteDump(output));
            return output.WrittenSpan.ToArray();
        }
    }

    /// <summary>The selected hotbar slot, settable without a <c>HotbarSelect</c> op.</summary>
    private sealed class FixedSelection : IHotbarSelection
    {
        public byte Index { get; set; }

        public byte SelectedIndex(PlayerSlot slot) => Index;
    }

    /// <summary>A multi-sample history: corroborated by any sample within the tolerance (3D, inclusive); the newest is the last.</summary>
    private sealed class SampleHistory : IClaimCorroborator
    {
        public List<Vec3Mm> Samples { get; } = [];

        public Vec3Mm LastKnown { get; set; }

        public Corroboration Check(PlayerSlot slot, in Vec3Mm claim, int toleranceMm, out Vec3Mm newest)
        {
            if (Samples.Count == 0)
            {
                newest = LastKnown;
                return Corroboration.NoHistory;
            }

            newest = Samples[^1];
            foreach (Vec3Mm s in Samples)
            {
                long dx = (long)claim.X - s.X;
                long dy = (long)claim.Y - s.Y;
                long dz = (long)claim.Z - s.Z;
                if ((dx * dx) + (dy * dy) + (dz * dz) <= (long)toleranceMm * toleranceMm)
                {
                    return Corroboration.Corroborated;
                }
            }

            return Corroboration.Clamped;
        }
    }

    /// <summary>The real Players API, except that the carrier can be mounted (there is no seat or vehicle at M-1a).</summary>
    private sealed class MountablePlayers : IPlayersApi
    {
        private readonly IPlayersApi _inner;

        public MountablePlayers(IPlayersApi inner)
        {
            _inner = inner;
        }

        public bool Mounted { get; set; }

        public LifeState GetLifeState(PlayerSlot slot) => _inner.GetLifeState(slot);

        public RegisterResult RegisterAnchor(in AnchorDef def, IInteractable impl) => _inner.RegisterAnchor(in def, impl);

        public RegisterResult UnregisterAnchor(AnchorRef anchor) => _inner.UnregisterAnchor(anchor);

        public RegisterResult MoveAnchor(AnchorRef anchor, in Vec3Mm point) => _inner.MoveAnchor(anchor, in point);

        public InteractionCheck CheckInteraction(ActorId actor, AnchorRef anchor, in ClaimedPosition claim) => _inner.CheckInteraction(actor, anchor, in claim);

        public bool LeashExceeded(PlayerSlot slot, in Vec3Mm anchorPoint, int leashMm) => _inner.LeashExceeded(slot, in anchorPoint, leashMm);

        public bool IsSwimming(PlayerSlot slot) => _inner.IsSwimming(slot);

        public bool IsMounted(PlayerSlot slot) => Mounted || _inner.IsMounted(slot);

        public ClaimCorroboration CorroborateClaim(PlayerSlot slot, in ClaimedPosition claim, int toleranceMm, out Vec3Mm newest) =>
            _inner.CorroborateClaim(slot, in claim, toleranceMm, out newest);

        public byte SelectedHotbarIndex(PlayerSlot slot) => _inner.SelectedHotbarIndex(slot);
    }
}
