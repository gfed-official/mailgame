using System.Runtime.InteropServices;
using PerformativeMail.Client.Core.Mirror;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Client.Core.Throwing;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.ThrowingCatching;
using ClientLayerType = PerformativeMail.Sim.ClientLayer.ClientLayer;
using G = PerformativeMail.Sim.Tests.Unit.ThrowingCatching.ThrowGoldenVectors;

namespace PerformativeMail.Sim.Tests.Unit.ThrowingCatching;

/// <summary>
/// Throwing-catching story 017 (ADR-0016 sections 1, 5 and 6; ADR-0015 section 9; ADR-0005 section 3; TR-throwing-016 client half,
/// TR-throwing-021 pure-function half; GDD TC-F2-1 b, TC-VA-1 Sim half): <see cref="FlightMirror"/> fed through
/// <see cref="FlightMirrorConsumer"/> at a real <see cref="MirrorDrainStep"/>, or by <see cref="FlightMirror.Apply"/> directly, and sampled
/// with <see cref="FlightMirror.Sample"/> as the render clock would. Expected positions come from <see cref="Ballistic.PositionAt"/> over
/// the host's launch integers. Greybox simplifications (simplify table, tc-004/007/017): no launch ghost, no RTT corpus, no 0 B gate.
/// </summary>
public class FlightMirrorTests
{
    private const uint LaunchTick = 100;
    private const int Delay = 3; // ADR-0016 section 6: HostTick − 3

    private static readonly Vec3Mm _origin = new(10_250, 1_200, 19_700);
    private static readonly Vec3Mm _vel = new(1_500, 2_911, -13_695);

    // ---------------------------------------------------------------- Mirror from events

    [Fact]
    [Trait("Id", "TR-throwing-016")]
    public void DrainedEvents_AddAndResolveFlights_UnknownResolveIgnored_DuplicateIdempotent_KeptUntilLanded()
    {
        // Arrange: the mirror registered at a real MirrorDrain step
        ThrowSettings settings = G.DefaultSettings();
        var mirror = new FlightMirror(settings, PlayerSlot.None);
        var inbox = new ClientInbox(16);
        var drain = new MirrorDrainStep(inbox);
        drain.Register(new FlightMirrorConsumer(mirror));
        var builder = new ClientLayerBuilder(new ClientHostClock());
        drain.BindTo(builder);
        ClientLayerType layer = builder.Build();
        var contact = new Vec3Mm(11_000, 0, 12_000);

        // Act
        inbox.TryAddEntry(LaunchTick, Launched(7, LaunchTick));
        inbox.TryAddEntry(LaunchTick + 5, Resolved(99, LaunchTick + 5, new Vec3Mm(1, 2, 3)));   // a rejoiner's: never launched here
        inbox.TryAddEntry(LaunchTick + 20, Resolved(7, LaunchTick + 20, contact));
        inbox.TryAddEntry(LaunchTick + 21, Resolved(7, LaunchTick + 30, new Vec3Mm(5, 5, 5))); // duplicate
        layer.Step(true);
        mirror.Sample(LaunchTick + Delay + 19, 500);
        FlightRenderState beforeLanding = mirror[0];
        mirror.Sample(LaunchTick + Delay + 20, 0);
        FlightRenderState landed = mirror[0];
        int countAtLanding = mirror.Count;
        mirror.Sample(LaunchTick + Delay + 21, 0);

        // Assert
        Assert.Equal((FlightPhase.InFlight, 19), (beforeLanding.Frame.Phase, beforeLanding.Frame.AgeTicks));
        Assert.Equal((FlightPhase.Landed, contact, Terminal.Ground), (landed.Frame.Phase, landed.Frame.From, landed.Facts.Terminal));
        Assert.Equal(1, countAtLanding);
        Assert.Equal(0, mirror.Count);
        Assert.Equal(1L, mirror.UnknownResolutions);
    }

    // ---------------------------------------------------------------- Timeline

    [Theory]
    [Trait("Id", "TR-throwing-016")]
    [InlineData(-1, 0, FlightPhase.Hidden, 0, 0)]       // render tick before the launch
    [InlineData(0, 0, FlightPhase.InFlight, 0, 0)]      // age 0: (p0, p1)
    [InlineData(7, 400, FlightPhase.InFlight, 7, 400)]  // inside: (p7, p8) + 0.4
    [InlineData(19, 999, FlightPhase.InFlight, 19, 999)] // last segment: (p19, contact)
    [InlineData(20, 300, FlightPhase.Landed, 20, 0)]    // resolution age
    [InlineData(45, 0, FlightPhase.Landed, 20, 0)]      // past it: the contact, never p(45)
    public void FlightLaunchedAtL_RendersAtHostTickMinus3PlusAlpha_ClampedToItsResolutionAge(
        int renderAge, int alpha, FlightPhase phase, int drawnAge, int drawnAlpha)
    {
        // Arrange: resolved at age 20 at a contact short of p(20)
        ThrowSettings settings = G.DefaultSettings();
        var launch = new Launch(_origin, _vel, settings.G);
        var contact = new Vec3Mm(11_190, 900, 15_000);
        var mirror = new FlightMirror(settings, PlayerSlot.None);
        mirror.Apply(Launched(3, LaunchTick), default);
        mirror.Apply(Resolved(3, LaunchTick + 20, contact), default);

        // Act
        mirror.Sample((uint)(LaunchTick + Delay + renderAge), alpha);
        FlightFrame f = mirror[0].Frame;

        // Assert
        Assert.Equal((phase, drawnAge, drawnAlpha), (f.Phase, f.AgeTicks, f.AlphaPermille));
        if (phase == FlightPhase.InFlight)
        {
            Assert.Equal(Ballistic.PositionAt(in launch, drawnAge), f.From);
            Assert.Equal(drawnAge + 1 == 20 ? contact : Ballistic.PositionAt(in launch, drawnAge + 1), f.To);
        }
        else if (phase == FlightPhase.Landed)
        {
            Assert.Equal((contact, contact), (f.From, f.To));
        }
    }

    // ---------------------------------------------------------------- TC-F2-1 b: fast-forward

    [Fact]
    [Trait("Id", "TC-F2-1b")]
    public void ThrowLaunchedSixTicksLate_RendersAtItsCurrentAgeOnTheSameFrame()
    {
        // Arrange: the event is drained 6 ticks after the frame that should first have shown it
        ThrowSettings settings = G.DefaultSettings();
        var launch = new Launch(_origin, _vel, settings.G);
        var mirror = new FlightMirror(settings, PlayerSlot.None);
        uint hostTick = LaunchTick + Delay + 6;

        // Act
        mirror.Apply(Launched(1, LaunchTick), default);
        mirror.Sample(hostTick, 250);

        // Assert
        FlightFrame f = mirror[0].Frame;
        Assert.Equal((FlightPhase.InFlight, 6, 250), (f.Phase, f.AgeTicks, f.AlphaPermille));
        Assert.Equal((Ballistic.PositionAt(in launch, 6), Ballistic.PositionAt(in launch, 7)), (f.From, f.To));
    }

    [Fact]
    [Trait("Id", "TC-F2-1b")]
    public void ThousandRandomFlights_FromDecodedThrowLaunchedAlone_MatchTheHostEndpointAtEveryAge()
    {
        // Arrange: the host's launch integers, sent through the wire codec; the host evaluates F2 with its own throw.g
        ThrowSettings settings = G.DefaultSettings();
        var codec = new ThrowLaunchedCodec();
        var random = new Random(170_017);
        Span<byte> wire = stackalloc byte[64];
        int max = settings.MaxFlightTicks;

        for (int n = 0; n < 1_000; n++)
        {
            var origin = new Vec3Mm(random.Next(-200_000, 200_000), random.Next(0, 20_000), random.Next(-200_000, 200_000));
            var vel = new Vec3Mm(random.Next(-20_000, 20_000), random.Next(-10_000, 20_000), random.Next(-20_000, 20_000));
            uint launchTick = (uint)random.Next(0, 1_000_000);
            var host = new Launch(origin, vel, settings.G);
            EventEntry sent = Launched((ushort)n, launchTick, origin, vel);
            int len = codec.Encode(in sent, wire);
            EventEntry received = default;
            received.Kind = ThrowEventKinds.ThrowLaunched;
            Assert.True(codec.TryDecode(wire[..len], ref received, out _));
            var mirror = new FlightMirror(settings, PlayerSlot.None);
            mirror.Apply(in received, default);

            // Act + Assert
            for (int k = 0; k < max; k++)
            {
                mirror.Sample((uint)(launchTick + Delay + k), 0);
                FlightFrame f = mirror[0].Frame;
                Assert.Equal(Ballistic.PositionAt(in host, k), f.From);
                Assert.Equal(Ballistic.PositionAt(in host, k + 1), f.To);
            }
        }
    }

    // ---------------------------------------------------------------- Spin

    [Fact]
    [Trait("Id", "TR-throwing-021")]
    public void Spin_IsTheSameOnTwoPeers_SinglesTurnTwiceAsFastAsBundles_AndStopsAtContact()
    {
        // Arrange: peer A drains the launch on time, peer B 6 ticks late; flight 4 a single, flight 5 a bundle, both resolved at age 30
        ThrowSettings settings = G.DefaultSettings();
        var a = new FlightMirror(settings, PlayerSlot.None);
        var b = new FlightMirror(settings, PlayerSlot.None);
        a.Apply(Launched(4, LaunchTick, units: 1), default);
        a.Apply(Launched(5, LaunchTick, units: 3), default);
        a.Sample(LaunchTick + Delay, 0);
        ushort single0 = a[0].SpinBam;
        ushort bundle0 = a[1].SpinBam;
        foreach (FlightMirror m in new[] { a, b })
        {
            if (m == b)
            {
                m.Apply(Launched(4, LaunchTick, units: 1), default);
                m.Apply(Launched(5, LaunchTick, units: 3), default);
            }

            m.Apply(Resolved(4, LaunchTick + 30, default), default);
            m.Apply(Resolved(5, LaunchTick + 30, default), default);
        }

        // Act: 7.5 ticks = 0.25 s in, both peers on the same frame
        a.Sample(LaunchTick + Delay + 7, 500);
        b.Sample(LaunchTick + Delay + 7, 500);
        (ushort Single, ushort Bundle) quarterA = (a[0].SpinBam, a[1].SpinBam);
        (ushort Single, ushort Bundle) quarterB = (b[0].SpinBam, b[1].SpinBam);
        a.Sample(LaunchTick + Delay + 30, 0);
        ushort singleAtContact = a[0].SpinBam;
        var c = new FlightMirror(settings, PlayerSlot.None);
        c.Apply(Launched(4, LaunchTick, units: 1), default);
        c.Apply(Resolved(4, LaunchTick + 30, default), default);
        c.Sample(LaunchTick + Delay + 80, 900);

        // Assert: a quarter second is half a turn for a single (2 rev/s) and a quarter for a bundle (1 rev/s)
        Assert.Equal(quarterA, quarterB);
        Assert.Equal((ushort)(single0 + 32_768), quarterA.Single);
        Assert.Equal((ushort)(bundle0 + 16_384), quarterA.Bundle);
        Assert.Equal(single0, singleAtContact);   // 1 s = two full turns, then stopped
        Assert.Equal(singleAtContact, c[0].SpinBam);
    }

    // ---------------------------------------------------------------- Trail verdict

    [Fact]
    [Trait("Id", "TC-VA-1")]
    public void LaunchVerdict_IsTheClassAtReleaseForOwnFlights_SkipsRefusedReleases_NoneForRemotes_NeverChanges()
    {
        // Arrange: this client is slot 0; it sends Accept, a refused Reject, then Misdeliver; slot 1 also throws
        ThrowSettings settings = G.DefaultSettings();
        var mirror = new FlightMirror(settings, PlayerSlot.FromValue(0));
        mirror.NoteOwnRelease(Record(1, EndClass.Accept));
        mirror.NoteOwnRelease(Record(2, EndClass.Reject));
        mirror.NoteOwnRelease(Record(3, EndClass.Misdeliver));

        // Act
        mirror.Apply(Launched(10, LaunchTick, thrower: 0), default);
        mirror.ApplyReply(new Reply { Kind = ThrowOpKinds.ThrowRelease, ReqId = 2, Reason = (ushort)ThrowReason.ThrowCooldown, Result = ReplyResult.Rejected });
        mirror.Apply(Launched(11, LaunchTick + 2, thrower: 1), default);
        mirror.Apply(Launched(12, LaunchTick + 2, thrower: 0), default);
        mirror.Sample(LaunchTick + Delay + 4, 0);
        EndClass[] atLaunch = Verdicts(mirror);
        mirror.Apply(Resolved(12, LaunchTick + 8, default, Terminal.MailboxReject), default);   // e.g. the box closed mid-flight
        mirror.Sample(LaunchTick + Delay + 8, 0);

        // Assert
        Assert.Equal(new[] { EndClass.Accept, EndClass.None, EndClass.Misdeliver }, atLaunch);
        Assert.Equal(atLaunch, Verdicts(mirror));
    }

    // ---------------------------------------------------------------- helpers

    private static EndClass[] Verdicts(FlightMirror m)
    {
        var v = new EndClass[m.Count];
        for (int i = 0; i < m.Count; i++)
        {
            v[i] = m[i].Facts.LaunchVerdict;
        }

        return v;
    }

    private static ReleaseRecord Record(uint seq, EndClass cls) => new(seq, seq, cls, 0, default, 0, 0, 0);

    private static EventEntry Launched(ushort id, uint launchTick, byte units = 1, byte thrower = 0) =>
        Launched(id, launchTick, _origin, _vel, units, thrower);

    private static EventEntry Launched(ushort id, uint launchTick, Vec3Mm origin, Vec3Mm vel, byte units = 1, byte thrower = 0)
    {
        var p = new ThrowLaunched
        {
            FlightId = id, Thrower = thrower, EntryId = 1_000u + id, Units = units, KindDef = 2,
            OriginX = origin.X, OriginY = origin.Y, OriginZ = origin.Z, VelX = vel.X, VelY = vel.Y, VelZ = vel.Z, LaunchTick = launchTick,
        };
        return Entry(ThrowEventKinds.ThrowLaunched, in p, 40);
    }

    private static EventEntry Resolved(ushort id, uint tick, Vec3Mm contact, Terminal terminal = Terminal.Ground)
    {
        var p = new FlightResolved { FlightId = id, Terminal = terminal, ContactX = contact.X, ContactY = contact.Y, ContactZ = contact.Z, Tick = tick };
        return Entry(ThrowEventKinds.FlightResolved, in p, 32);
    }

    private static EventEntry Entry<T>(EventKind kind, in T payload, byte length)
        where T : unmanaged
    {
        var e = new EventEntry { Kind = kind, Class = ReplicationClass.Replicated, PayloadLength = length };
        Span<byte> bytes = e.Payload;
        MemoryMarshal.Write(bytes, in payload);
        return e;
    }
}
