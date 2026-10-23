using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;
using ClientLayerSut = PerformativeMail.Sim.ClientLayer.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>A scripted <see cref="ICarrierFocusSource"/>: the test sets the feet and status before each tick.</summary>
internal sealed class ScriptedFocusSource : ICarrierFocusSource
{
    private CarrierStatus _status = new() { Life = LifeState.Alive, Body = BodyContext.OnFoot };

    public Vec3Mm Feet { get; set; }

    public ref readonly CarrierStatus Status => ref _status;

    public void SetStatus(CarrierStatus status) => _status = status;
}

/// <summary>A pure owner double that counts <c>IsAvailable</c> calls.</summary>
internal sealed class CountingInteractable : IInteractable
{
    public bool Available { get; init; } = true;

    public StringId UnavailableReason { get; init; }

    public int AvailabilityCalls { get; private set; }

    public bool IsAvailable(in InteractionContext c, out StringId reason)
    {
        AvailabilityCalls++;
        reason = Available ? StringId.None : UnavailableReason;
        return Available;
    }

    public bool ShouldCancel(in InteractionLatch latch, in InteractionContext c, out StringId reason)
    {
        reason = StringId.None;
        return false;
    }

    public void WritePrompt(in InteractionContext c, ref PromptArgs args)
    {
    }

    public bool TryBuildRequest(in InteractionLatch latch, in InteractionContext c, ref Op op) => false;

    public void OnPress(in InteractionContext c, ref InteractionLatch latch)
    {
    }
}

/// <summary>
/// A ClientLayer with one <see cref="FocusStep"/> bound at <see cref="PreviewSubStep.Focus"/>. The carrier stands at
/// <see cref="Feet"/> (chest 1000 mm up) looking down −Z (yaw 0) with the FOV 80 cone (635), so <see cref="Ahead"/> places an
/// anchor at chest height <c>distance</c> mm in front, <c>side</c> mm to the right.
/// </summary>
internal sealed class FocusRig
{
    public static readonly Vec3Mm Feet = new(40_000, 0, 40_000);
    public static readonly StringId DryLand = new(StringNamespace.Run, 901);
    public static readonly StringId ClosePanel = new(StringNamespace.Run, 902);

    public FocusRig(int maxAnchors = 64)
    {
        Counters = new SimCounters();
        Anchors = new AnchorRegistry(new MapBounds(0, 0, 80_000, 80_000), maxAnchors, 2500, 400, new SimCheckRing(), Counters);
        Source = new ScriptedFocusSource { Feet = Feet };
        Focus = new FocusStep(Source, Anchors, new InteractionGate(new FrameworkReasons(DryLand, ClosePanel)), PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player), Counters);
        var builder = new ClientLayerBuilder(new NoClock(), Counters);
        builder.BindPreview(PreviewSubStep.Focus, Focus);
        Layer = builder.Build();
        Look(0);
    }

    public SimCounters Counters { get; }

    public AnchorRegistry Anchors { get; }

    public ScriptedFocusSource Source { get; }

    public FocusStep Focus { get; }

    public ClientLayerSut Layer { get; }

    public ref FocusState State => ref Focus.State;

    public static AnchorRef Ref(uint n, byte slot = 0) => new(new EntityId(0x0100_0000u + n), slot);

    public static Vec3Mm Ahead(int distance, int side = 0) => new(Feet.X + side, 1000, Feet.Z - distance);

    public void Look(ushort yaw, short cosMinPm = 635) =>
        Layer.InputWriter.SetState(new InputState { LookYaw = yaw, FocusCosMinPm = cosMinPm });

    public AnchorRef Add(uint n, Vec3Mm at, IInteractable? impl = null, Verb verb = Verb.Open, AnchorFlags flags = AnchorFlags.None, byte slot = 0)
    {
        AnchorDef d = AnchorDef.For(Ref(n, slot), at, verb);
        d.Flags = flags;
        Assert.Equal(RegisterResult.Ok, Anchors.Register(in d, impl ?? new CountingInteractable()));
        return d.Ref;
    }

    public AnchorRef Tick()
    {
        Layer.Step(true);
        return Focus.State.Focused;
    }

    private sealed class NoClock : IHostClock
    {
        public uint EstimatedHostTick => 0;

        public bool IsValid => false;
    }
}

/// <summary>
/// Player Character story 014 (ADR-0014 section 2; GDD P11, F4): <see cref="FocusStep"/>'s dwell, latch (Mail Lead planning default
/// D2, pending Evan, TD-164), context filter, tie-break and determinism.
/// </summary>
public class FocusStepTests
{
    private static CarrierStatus Status(BodyContext body = BodyContext.OnFoot, bool panel = false, bool swimming = false, AnchorRef seat = default) =>
        new() { Life = body == BodyContext.Dead ? LifeState.Dead : LifeState.Alive, Body = body, PanelOpen = panel, Swimming = swimming, OccupiedSeat = seat };

    [Fact]
    [Trait("Id", "PCM-P11-2a")]
    public void Challenger_TakesFocusOnItsFourthLeadingTick_NoIncumbentIsImmediate()
    {
        // Arrange: A alone (incumbent score 260 at 2000 mm); then B dead ahead at 1000 mm (160) out-scores it.
        var rig = new FocusRig();
        AnchorRef a = rig.Add(1, FocusRig.Ahead(2000));
        Assert.Equal(a, rig.Tick());   // no incumbent: focused on its first tick
        AnchorRef b = rig.Add(2, FocusRig.Ahead(1000));

        // Act
        AnchorRef[] focused = { rig.Tick(), rig.Tick(), rig.Tick(), rig.Tick() };

        // Assert
        Assert.Equal(new[] { a, a, a, b }, focused);
        Assert.Equal((AnchorRef.None, 0), (rig.State.LeaderRef, rig.State.LeadTicks));
    }

    [Fact]
    [Trait("Id", "PCM-P11-2a")]
    public void LeadCounter_ResetsOnALeaderChange_AndWhenTheChallengerStopsLeading()
    {
        // Arrange
        var rig = new FocusRig();
        rig.Add(1, FocusRig.Ahead(2000));
        rig.Tick();
        AnchorRef b = rig.Add(2, FocusRig.Ahead(1500));
        rig.Tick();
        rig.Tick();
        Assert.Equal((b, 2), (rig.State.LeaderRef, rig.State.LeadTicks));

        // Act + Assert: a better C takes the lead from 1.
        AnchorRef c = rig.Add(3, FocusRig.Ahead(1000));
        rig.Tick();
        Assert.Equal((c, 1), (rig.State.LeaderRef, rig.State.LeadTicks));

        // Act + Assert: B and C fall behind the carrier, beyond nearMm: no challenger leads.
        rig.Anchors.Move(b, new Vec3Mm(FocusRig.Feet.X, 1000, FocusRig.Feet.Z + 1500));
        rig.Anchors.Move(c, new Vec3Mm(FocusRig.Feet.X, 1000, FocusRig.Feet.Z + 1000));
        rig.Tick();
        Assert.Equal((AnchorRef.None, 0), (rig.State.LeaderRef, rig.State.LeadTicks));
    }

    [Fact]
    [Trait("Id", "PCM-P11-1")]
    [Trait("Id", "PCM-P11-2a")]
    public void Incumbent_KeptAt2900_DroppedAt2901ForTheBestRemainingOnThatTick()
    {
        // Arrange: A focused; B (2600 mm, out of range) cannot challenge; C at 2453 mm, 1000 mm aside (444), is worse than A at 2900 (404).
        var rig = new FocusRig();
        AnchorRef a = rig.Add(1, FocusRig.Ahead(1000));
        rig.Tick();
        rig.Add(2, FocusRig.Ahead(2600));
        AnchorRef c = rig.Add(3, FocusRig.Ahead(2240, side: 1000));

        // Act + Assert
        rig.Anchors.Move(a, FocusRig.Ahead(2900));
        Assert.Equal(a, rig.Tick());
        rig.Anchors.Move(a, FocusRig.Ahead(2901));
        Assert.Equal(c, rig.Tick());
    }

    [Fact]
    [Trait("Id", "PCM-P11-2c")]
    public void Latch_HoldsTheIncumbentAgainstChallengersRangeAndContext_ThenTheDwellRestarts()
    {
        // Arrange: A focused, then a better B.
        var rig = new FocusRig();
        AnchorRef a = rig.Add(1, FocusRig.Ahead(2000));
        rig.Tick();
        AnchorRef b = rig.Add(2, FocusRig.Ahead(1000));
        rig.State.Latched = true;

        // Act + Assert: a 30-tick challenger lead never counts.
        for (int t = 0; t < 30; t++)
        {
            Assert.Equal(a, rig.Tick());
            Assert.Equal(0, rig.State.LeadTicks);
        }

        // Act + Assert: latch clear, B needs 4 fresh leading ticks.
        rig.State.Latched = false;
        Assert.Equal(new[] { a, a, a, b }, new[] { rig.Tick(), rig.Tick(), rig.Tick(), rig.Tick() });

        // Act + Assert: latched B at 2901 mm, then with Body Dead, stays focused.
        rig.State.Latched = true;
        rig.Anchors.Move(b, FocusRig.Ahead(2901));
        Assert.Equal(b, rig.Tick());
        rig.Source.SetStatus(Status(BodyContext.Dead));
        Assert.Equal(b, rig.Tick());

        // Act + Assert: the latch clears while Dead: dropped at once.
        rig.State.Latched = false;
        Assert.Equal(AnchorRef.None, rig.Tick());
    }

    [Fact]
    [Trait("Id", "PCM-P11-2d")]
    public void ContextFilter_NoCandidateExcludes_DeadMountedOwnSeatBuildOnly()
    {
        // Arrange: a near Open, a farther own-seat Dismount, another seat's Dismount, and a Build-only Deconstruct.
        var rig = new FocusRig();
        AnchorRef open = rig.Add(1, FocusRig.Ahead(900));
        AnchorRef seat = rig.Add(2, FocusRig.Ahead(1800), verb: Verb.Dismount, flags: AnchorFlags.SeatOf, slot: 1);
        rig.Add(3, FocusRig.Ahead(1000), verb: Verb.Dismount, flags: AnchorFlags.SeatOf, slot: 1);
        AnchorRef build = rig.Add(4, FocusRig.Ahead(2000), verb: Verb.Deconstruct, flags: AnchorFlags.BuildModeOnly);

        // Act + Assert
        Assert.Equal(open, rig.Tick());
        rig.Source.SetStatus(Status(BodyContext.Mounted, seat: seat));
        Assert.Equal(seat, rig.Tick());
        rig.Source.SetStatus(Status(BodyContext.Build));
        Assert.Equal(build, rig.Tick());
        rig.Source.SetStatus(Status(BodyContext.Mounted));
        Assert.Equal(AnchorRef.None, rig.Tick());
        rig.Source.SetStatus(Status(BodyContext.Dead));
        Assert.Equal(AnchorRef.None, rig.Tick());
    }

    [Fact]
    [Trait("Id", "PCM-P11-2d")]
    public void Unavailable_StillWinsDimmed_AndOnlyTheWinnersOwnerIsAsked()
    {
        // Arrange: the winner's owner says no; two worse candidates in range.
        var notNow = new StringId(StringNamespace.Run, 950);
        var winner = new CountingInteractable { Available = false, UnavailableReason = notNow };
        var others = new CountingInteractable();
        var rig = new FocusRig();
        AnchorRef a = rig.Add(1, FocusRig.Ahead(900), winner);
        rig.Add(2, FocusRig.Ahead(1500), others);
        rig.Add(3, FocusRig.Ahead(2000), others);

        // Act + Assert: owner reason; the gate's Panel and swim reasons win without asking the owner.
        Assert.Equal(a, rig.Tick());
        Assert.Equal((false, notNow), (rig.State.Available, rig.State.Reason));
        rig.Source.SetStatus(Status(panel: true));
        Assert.Equal(a, rig.Tick());
        Assert.Equal((false, FocusRig.ClosePanel), (rig.State.Available, rig.State.Reason));
        rig.Source.SetStatus(Status(swimming: true));
        Assert.Equal(a, rig.Tick());
        Assert.Equal((false, FocusRig.DryLand), (rig.State.Available, rig.State.Reason));
        Assert.Equal((1, 0), (winner.AvailabilityCalls, others.AvailabilityCalls));
    }

    [Fact]
    public void TieBreak_LowestEntityThenSlot_Over32ShuffledRegistrationOrders()
    {
        // Arrange: 8 refs at one point (equal scores); the winner is entity 3, slot 0.
        AnchorRef[] refs =
        {
            FocusRig.Ref(9), FocusRig.Ref(3, 2), FocusRig.Ref(5), FocusRig.Ref(3, 1),
            FocusRig.Ref(4), FocusRig.Ref(3, 0), FocusRig.Ref(7), FocusRig.Ref(6),
        };
        var rng = new Pcg32(0x7_1EB_0014UL, 14UL);

        for (int shuffle = 0; shuffle < 32; shuffle++)
        {
            for (int i = refs.Length - 1; i > 0; i--)
            {
                int j = (int)(rng.Next32() % (uint)(i + 1));
                (refs[i], refs[j]) = (refs[j], refs[i]);
            }

            var rig = new FocusRig();
            foreach (AnchorRef r in refs)
            {
                rig.Add(r.Entity.Value - 0x0100_0000u, FocusRig.Ahead(1200), slot: r.Slot);
            }

            // Act + Assert
            Assert.Equal(FocusRig.Ref(3, 0), rig.Tick());
        }
    }

    [Fact]
    public void TwoFreshSteps_OverTheSame300TickScript_GiveIdenticalStatePerTick()
    {
        (AnchorRef, AnchorRef, int)[] first = Run();
        (AnchorRef, AnchorRef, int)[] second = Run();

        Assert.Equal(first, second);
        Assert.True(first.Select(s => s.Item1).Distinct().Count() > 3, "the script should move focus between several anchors");
    }

    /// <summary>The carrier walks a circle and turns while 40 anchors sit on a ring; the panel opens on every 50th tick.</summary>
    private static (AnchorRef, AnchorRef, int)[] Run()
    {
        var rig = new FocusRig();
        for (uint i = 0; i < 40; i++)
        {
            var angle = (ushort)(i * 1638);
            int x = FocusRig.Feet.X + (TrigQ15.Cos(angle) * 3000 / TrigQ15.One);
            int z = FocusRig.Feet.Z + (TrigQ15.Sin(angle) * 3000 / TrigQ15.One);
            rig.Add(i + 1, new Vec3Mm(x, 1000 + (int)(i % 3 * 200), z));
        }

        var seen = new (AnchorRef, AnchorRef, int)[300];
        for (int t = 0; t < 300; t++)
        {
            ushort phase = (ushort)(t * 218);
            rig.Source.Feet = new Vec3Mm(FocusRig.Feet.X + (TrigQ15.Cos(phase) * 1200 / TrigQ15.One), 0, FocusRig.Feet.Z + (TrigQ15.Sin(phase) * 1200 / TrigQ15.One));
            rig.Source.SetStatus(Status(panel: t % 50 == 0));
            rig.Look((ushort)(t * 397));
            rig.Tick();
            seen[t] = (rig.State.Focused, rig.State.LeaderRef, rig.State.LeadTicks);
        }

        return seen;
    }
}
