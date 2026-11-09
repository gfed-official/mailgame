using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.ThrowingCatching;
using PerformativeMail.Sim.World;
using G = PerformativeMail.Sim.Tests.Unit.ThrowingCatching.ThrowGoldenVectors;

namespace PerformativeMail.Sim.Tests.Unit.ThrowingCatching;

/// <summary>
/// Throwing-catching story 004 (GDD T3, TC-T3-1 a/e, TC-T1-1 b state half; ADR-0016 sections 2, 3 and 7, ADR-0015 section 8): the aim
/// preview <see cref="ThrowPreviewStep"/> at sub-step 4c, stepped through a real <see cref="ClientLayerBuilder"/> layer over doubles of its
/// ports. The world is FX-Flat (terrain y = 0, 32 m) with MB-A's catch volume; the carrier is FX-T (feet (10 000, 0, 20 000), facing −Z,
/// right-handed, a light letter), whose arc descends into MB-A.
/// </summary>
public class ThrowPreviewStepTests
{
    private const uint MbA = 77;
    private const short Pitch6 = 1092;
    private static readonly AabbMm _mbABox = new(new Vec3Mm(9750, 600, 9500), new Vec3Mm(10750, 1400, 10500));
    private static readonly AddressId _address = AddressId.Pack(1, 1, 4);
    private static readonly DefIndex _letter = DefIndex.FromValue(1);

    // ---------------------------------------------------------------- arc, same integers, end classes

    [Fact]
    [Trait("Id", "TC-T3-1")]
    public void FxFlat_PolylineIsPositionAtPerTick_TruncatedAtTheContact_FromThePublishedDir()
    {
        // Arrange
        var rig = new Rig(Flat(0));

        // Act
        rig.Step();

        // Assert: the published dir is the quantiser's, and exactly what LaunchMath consumed for the drawn arc.
        ref readonly ThrowPreviewState s = ref rig.Preview.Current;
        Assert.Equal(G.FxTDir, (s.DirX, s.DirY, s.DirZ));
        Assert.True(LaunchMath.Compute(G.Input(s.DirX, s.DirY, s.DirZ, rig.Settings), out Launch l, out _));
        Vec3Mm[] arc = rig.Preview.Polyline.ToArray();
        Assert.Equal(s.EndAge + 1, arc.Length);
        for (int k = 0; k < s.EndAge; k++)
        {
            Assert.Equal(Ballistic.PositionAt(in l, k), arc[k]);
        }

        Assert.Equal(EndClass.Accept, s.EndClass);
        Assert.Equal(MbA, s.EndKey);
        Assert.Equal(s.EndPoint, arc[^1]);
        Assert.True(_mbABox.Min.Y <= arc[^1].Y && arc[^1].Y <= _mbABox.Max.Y && arc[^1].Z <= _mbABox.Max.Z && arc[^1].Z >= _mbABox.Min.Z);
        Assert.True(arc[^1].Y < arc[^2].Y, "a mailbox is entered descending");
    }

    public enum Scene
    {
        Accept,
        Misdeliver,
        Closed,
        Locked,
        NonMail,
        Ground,
        Water,
        Wall,
        Timeout,
    }

    [Theory]
    [Trait("Id", "TC-T3-1")]
    [InlineData(Scene.Accept, EndClass.Accept, MailRejectReason.None)]
    [InlineData(Scene.Misdeliver, EndClass.Misdeliver, MailRejectReason.None)]
    [InlineData(Scene.Closed, EndClass.Reject, MailRejectReason.Closed)]          // deliveries closed: Reject, never Accept
    [InlineData(Scene.Locked, EndClass.Reject, MailRejectReason.NotOnRoute)]      // district locked
    [InlineData(Scene.NonMail, EndClass.Reject, MailRejectReason.WontFit)]        // a tool into a mailbox
    [InlineData(Scene.Ground, EndClass.Ground, MailRejectReason.None)]
    [InlineData(Scene.Water, EndClass.Water, MailRejectReason.None)]
    [InlineData(Scene.Wall, EndClass.Blocked, MailRejectReason.None)]
    [InlineData(Scene.Timeout, EndClass.Timeout, MailRejectReason.None)]          // maxFlightTicks 30, steep throw
    public void EndClass_ComesFromTheStaticReceiver_OrFromEvaluateForAMailbox(Scene scene, EndClass expected, MailRejectReason reason)
    {
        // Arrange
        WorldTables world = scene switch
        {
            Scene.Water => Flat(-2000),
            Scene.Wall => Flat(0, new AabbMm(new Vec3Mm(9000, 0, 13_500), new Vec3Mm(11_500, 5000, 14_000))),
            _ => Flat(0),
        };
        bool mailbox = scene is Scene.Accept or Scene.Misdeliver or Scene.Closed or Scene.Locked or Scene.NonMail;
        var rig = new Rig(world, withMailbox: mailbox, maxFlightTicks: scene == Scene.Timeout ? 30 : null);
        rig.Mirror.State = scene switch { Scene.Closed => DestState.Closed, Scene.Locked => DestState.Locked, _ => DestState.Open };
        rig.Mirror.Address = scene == Scene.Misdeliver ? AddressId.Pack(1, 1, 6) : _address;
        if (scene == Scene.NonMail)
        {
            rig.Selected.Entry = Tool(rig.Selected.Entry!.Value.Entry);
        }

        if (scene == Scene.Timeout)
        {
            rig.Carrier.AimPitch = ThrowAim.MaxPitchBam;
        }

        // Act
        rig.Step();

        // Assert
        Assert.Equal((expected, reason), (rig.Preview.Current.EndClass, rig.Preview.Current.RejectReason));
        Assert.Equal(1, rig.Preview.Current.Active);
    }

    // ---------------------------------------------------------------- TC-T3-1 e: the cache key

    public enum KeyChange
    {
        None,
        Dir,
        Origin,
        DestinationPhase,
        Wallet,
    }

    [Theory]
    [Trait("Id", "TC-T3-1")]
    [InlineData(KeyChange.None, 0)]
    [InlineData(KeyChange.Dir, 1)]
    [InlineData(KeyChange.Origin, 1)]           // a walking carrier changes this every tick
    [InlineData(KeyChange.DestinationPhase, 1)]
    [InlineData(KeyChange.Wallet, 1)]
    public void ChangingOneKeyInput_ReRunsTheQuery_AnUnchangedKeyRunsNothingAndKeepsSeq(KeyChange change, int reruns)
    {
        // Arrange
        var rig = new Rig(Flat(0));
        rig.Step();
        int evaluations = rig.Preview.Evaluations;
        uint seq = rig.Preview.Current.Seq;
        switch (change)
        {
            case KeyChange.Dir: rig.Carrier.AimYaw = 300; break;
            case KeyChange.Origin: rig.Carrier.Feet = new Vec3Mm(G.Feet.X, G.Feet.Y, G.Feet.Z - 50); break;
            case KeyChange.DestinationPhase: rig.Mirror.DestinationVersion++; break;
            case KeyChange.Wallet: rig.Mirror.WalletCents += 12; break;
        }

        // Act
        rig.Step();

        // Assert
        Assert.Equal(evaluations + reruns, rig.Preview.Evaluations);
        Assert.Equal(seq + (uint)reruns, rig.Preview.Current.Seq);
    }

    // ---------------------------------------------------------------- class tracking

    [Fact]
    public void ClassSinceTick_MovesOnlyOnAClassChange_RedSinceTick_MarksTheMisdeliverStart()
    {
        // Arrange: Accept on the first tick (client tick 0).
        var rig = new Rig(Flat(0));
        rig.Step();
        Assert.Equal(EndClass.Accept, rig.Preview.Current.EndClass);

        // Act 1: the destination's address changes (tick 1): Misdeliver.
        rig.Mirror.Address = AddressId.Pack(1, 1, 6);
        rig.Mirror.DestinationVersion++;
        rig.Step();
        (EndClass Class, uint Since, uint Red) red = Snapshot(rig);

        // Act 2: a small yaw change (tick 2) moves the arc but stays Misdeliver.
        rig.Carrier.AimYaw = 40;
        rig.Step();
        (EndClass Class, uint Since, uint Red) geometry = Snapshot(rig);

        // Act 3: aiming down (tick 3) lands on the ground.
        rig.Carrier.AimPitch = ThrowAim.MinPitchBam;
        rig.Step();
        (EndClass Class, uint Since, uint Red) ground = Snapshot(rig);

        // Assert
        Assert.Equal((EndClass.Misdeliver, 1u, 1u), red);
        Assert.Equal((EndClass.Misdeliver, 1u, 1u), geometry);
        Assert.Equal((EndClass.Ground, 3u, 0u), ground);
    }

    // ---------------------------------------------------------------- TC-T1-1 b (state half)

    public enum Selection
    {
        HandsSlot,
        EmptySlot,
        Swimming,
        Mail,
        NonMail,
    }

    [Theory]
    [Trait("Id", "TC-T1-1b")]
    [InlineData(Selection.HandsSlot, NotThrowable.HandsSlot)]
    [InlineData(Selection.EmptySlot, NotThrowable.EmptySlot)]
    [InlineData(Selection.Swimming, NotThrowable.Swimming)]
    [InlineData(Selection.Mail, NotThrowable.None)]
    [InlineData(Selection.NonMail, NotThrowable.None)]
    public void SelectedSlotAndCarrier_DecideActiveOrNotThrowable(Selection selection, NotThrowable expected)
    {
        // Arrange
        var rig = new Rig(Flat(0));
        switch (selection)
        {
            case Selection.HandsSlot: rig.Selected.Index = 0; break;
            case Selection.EmptySlot: rig.Selected.Entry = null; break;
            case Selection.Swimming: rig.Carrier.Swimming = true; break;
            case Selection.NonMail: rig.Selected.Entry = Tool(rig.Selected.Entry!.Value.Entry); break;
        }

        // Act
        rig.Step();

        // Assert
        ref readonly ThrowPreviewState s = ref rig.Preview.Current;
        bool active = expected == NotThrowable.None;
        Assert.Equal(expected, s.NotThrowable);
        Assert.Equal(active ? 1 : 0, s.Active);
        Assert.Equal(active, rig.Preview.Polyline.Length > 0);
    }

    // ---------------------------------------------------------------- entry latch

    public enum EntryChange
    {
        None,
        Count,
        Id,
        Vanished,
    }

    [Theory]
    [InlineData(EntryChange.None, 0)]
    [InlineData(EntryChange.Count, 1)]
    [InlineData(EntryChange.Id, 1)]
    [InlineData(EntryChange.Vanished, 1)]
    public void EntryChangedAfterAimStart_IsStale_AndTheLatchedEntryIsPublished(EntryChange change, byte stale)
    {
        // Arrange: aim starts on entry 41 x 5.
        var rig = new Rig(Flat(0));
        rig.Step();
        switch (change)
        {
            case EntryChange.Count: rig.Selected.Entry = Mail(EntryId.Real(41), 4); break;
            case EntryChange.Id: rig.Selected.Entry = Mail(EntryId.Real(42), 5); break;
            case EntryChange.Vanished: rig.Selected.Entry = null; break;
        }

        // Act
        rig.Step();

        // Assert
        ref readonly ThrowPreviewState s = ref rig.Preview.Current;
        Assert.Equal(stale, s.EntryStale);
        Assert.Equal((EntryId.Real(41), (byte)5), (s.Entry, s.Units));
    }

    // ---------------------------------------------------------------- predicted origin

    [Theory]
    [InlineData((sbyte)0, 10_250)]    // no profile: the +1 default (FX-T golden origin x)
    [InlineData((sbyte)-1, 9750)]     // left-handed
    public void Origin_IsF1FromTheCarrierFeet_AndTheProfileHand(sbyte side, int originX)
    {
        // Arrange
        var rig = new Rig(Flat(0), side: side == 0 ? null : side);
        rig.Carrier.Feet = G.Feet;

        // Act
        rig.Step();

        // Assert
        Assert.Equal(G.Feet, rig.Preview.Current.Feet);
        Assert.Equal(new Vec3Mm(originX, 1200, 19_700), rig.Preview.Polyline[0]);
    }

    // ---------------------------------------------------------------- sub-step order

    [Fact]
    public void BindTo_RunsTheStepAt4c_AfterTheDeliverPreviewAndBeforeEdgeResolve()
    {
        // Arrange: probes at 4b and 4d read the preview's Seq (0 before its first run).
        var rig = new Rig(Flat(0), bind: false);
        var seen = new List<(string Step, uint Seq)>();
        var builder = new ClientLayerBuilder(new Clock());
        builder.BindPreview(PreviewSubStep.EdgeResolve, new Probe("4d", rig.Preview, seen));
        rig.Preview.BindTo(builder);
        builder.BindPreview(PreviewSubStep.DeliverPreview, new Probe("4b", rig.Preview, seen));
        ClientLayer.ClientLayer layer = builder.Build();

        // Act
        layer.Step(true);

        // Assert
        Assert.Equal(new[] { ("4b", 0u), ("4d", 1u) }, seen);
    }

    // ---------------------------------------------------------------- helpers

    private static (EndClass, uint, uint) Snapshot(Rig rig) =>
        (rig.Preview.Current.EndClass, rig.Preview.Current.ClassSinceTick, rig.Preview.Current.RedSinceTick);

    private static SelectedEntryFacts Mail(EntryId id, byte units) =>
        new(3, id, units, true, new UnitFacts(_letter, ItemCategory.Mail, _address, 12, 0, MailFlags.None), WeightClass.Light, _letter);

    private static SelectedEntryFacts Tool(EntryId id) =>
        new(3, id, 1, false, new UnitFacts(DefIndex.FromValue(9), ItemCategory.Tool, AddressId.None, 0, 0, MailFlags.None), WeightClass.Light, DefIndex.FromValue(9));

    /// <summary>16 x 16 tiles of 2000 mm at <paramref name="heightMm"/> (FX-Flat at 0, deep water at −2000), plus optional solids.</summary>
    private static WorldTables Flat(int heightMm, params AabbMm[] solids)
    {
        var corners = new int[17 * 17];
        Array.Fill(corners, heightMm);
        var b = new WorldTablesBuilder();
        b.SetCorners(16, 16, corners);
        foreach (AabbMm box in solids)
        {
            b.AddSolid(EntityId.None, box, SolidKind.House);
        }

        return b.Build();
    }

    private sealed class Rig
    {
        public Rig(WorldTables world, bool withMailbox = true, int? maxFlightTicks = null, sbyte? side = null, bool bind = true)
        {
            Settings = maxFlightTicks is int m ? G.Settings(b => b with { MaxFlightTicks = m }) : G.DefaultSettings();
            var receivers = new ReceiverViewBuilder(world, new Volumes(withMailbox), new SimCounters());
            IThrowerProfile? profile = side is sbyte s ? new Profile(s) : null;
            Preview = new ThrowPreviewStep(
                Carrier, Selected, Mirror, profile, receivers, new ReceiverRanks(G.DefaultBalance), Settings, G.DefaultBalance.LoftDeg, new ChestOriginProbe(world));
            if (bind)
            {
                var builder = new ClientLayerBuilder(new Clock());
                Preview.BindTo(builder);
                Layer = builder.Build();
            }
        }

        public ThrowSettings Settings { get; }

        public CarrierDouble Carrier { get; } = new();

        public SelectedDouble Selected { get; } = new();

        public MirrorDouble Mirror { get; } = new();

        public ThrowPreviewStep Preview { get; }

        private ClientLayer.ClientLayer? Layer { get; }

        public void Step() => Layer!.Step(true);
    }

    internal sealed class CarrierDouble : IThrowCarrierSource
    {
        private CarrierStatus _status;

        public Vec3Mm Feet { get; set; } = G.Feet;

        public ushort BodyYaw => 0;

        public ushort AimYaw { get; set; }

        public short AimPitch { get; set; } = Pitch6;

        public bool Swimming { get; set; }

        public ref readonly CarrierStatus Status
        {
            get
            {
                _status = new CarrierStatus { Life = LifeState.Alive, Aiming = true, Swimming = Swimming };
                return ref _status;
            }
        }
    }

    internal sealed class SelectedDouble : ISelectedEntryView
    {
        public byte Index { get; set; } = 3;

        public SelectedEntryFacts? Entry { get; set; } = Mail(EntryId.Real(41), 5);

        public byte SelectedIndex => Index;

        public bool TryGet(out SelectedEntryFacts facts)
        {
            facts = Entry ?? default;
            return Entry is not null;
        }
    }

    /// <summary>MB-A's destination; the state goes through Mail's effective-state helper, as the client mirror's facts do.</summary>
    internal sealed class MirrorDouble : IThrowPreviewMirror
    {
        public DestState State { get; set; } = DestState.Open;

        public AddressId Address { get; set; } = _address;

        public uint DestinationVersion { get; set; }

        public long WalletCents { get; set; } = 500;

        public Cents Wallet
        {
            get
            {
                var sink = default(SimCounterSink);
                return Cents.FromCents(WalletCents, ref sink);
            }
        }

        public bool TryGetDestination(uint destinationKey, uint actionStartTick, uint applyTick, out DestinationFacts facts)
        {
            DestState effective = DeliveriesStateRules.Effective(
                State != DestState.Locked, State != DestState.Closed, 0, actionStartTick, applyTick, 90, 0u);
            facts = new DestinationFacts(new EntityId(destinationKey), Address, DefIndex.FromValue(0), effective, 1UL << _letter.Value, false);
            return destinationKey == MbA;
        }
    }

    private sealed class Profile(sbyte side) : IThrowerProfile
    {
        public sbyte Side => side;

        public int ThrowRangePermille => 1000;
    }

    private sealed class Volumes(bool withMailbox) : ICatchVolumeSource
    {
        public int Count => withMailbox ? 1 : 0;

        public EntityId DestinationAt(int ordinal) => new(MbA);

        public AabbMm CatchVolumeAt(int ordinal) => _mbABox;
    }

    private sealed class Clock : IHostClock
    {
        public uint EstimatedHostTick => 0;

        public bool IsValid => false;
    }

    private sealed class Probe(string name, ThrowPreviewStep preview, List<(string, uint)> seen) : IClientStep
    {
        public void Step(in ClientStepContext ctx) => seen.Add((name, preview.Current.Seq));
    }
}
