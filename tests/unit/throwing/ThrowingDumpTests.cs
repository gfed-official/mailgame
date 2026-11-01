using System.Buffers;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Sim.Tests.Unit.ThrowingCatching;

/// <summary>
/// Throwing story 008, "Dump" (ADR-0015 section 9, ADR-0006 section 4): the Throwing section writes every stored flight field, the
/// <c>FlightId</c> serial and per-slot <c>Tat</c>, so changing any single one changes the bytes; two tables that reach the same live
/// flights by different churn histories dump identically. The single Throwing dump test.
/// </summary>
public class ThrowingDumpTests
{
    private delegate FlightInit Mutation(FlightInit f);

    public static TheoryData<string> StoredFields => new()
    {
        "serial", "flightId", "entry", "units", "kindDef", "thrower", "classPts", "originX", "originY", "originZ", "velX", "velY", "velZ",
        "launchTick", "thrownPm", "releaseTick", "openAtRelease", "hop", "originClamped", "tat",
    };

    [Theory]
    [MemberData(nameof(StoredFields))]
    [Trait("Id", "TR-throwing-009")]
    public void ChangingOneStoredField_ChangesTheThrowingSection(string field)
    {
        // Arrange: the flight under test plus a filler flight that is resolved and compacted away. Launching the filler first gives
        // the same flight the next FlightId while the serial (2) and every other byte stay the same; launching and resolving a second
        // filler after it moves only the serial (3).
        FlightInit baseline = FlightTableTests.Init(3);
        Mutation change = field switch
        {
            "entry" => f => f with { Entry = EntryId.Real(f.Entry.Value + 1) },
            "units" => f => f with { Units = (byte)(f.Units + 1) },
            "kindDef" => f => f with { KindDef = (ushort)(f.KindDef + 1) },
            "thrower" => f => f with { Thrower = PlayerSlot.FromValue((byte)(f.Thrower.Value + 1)) },
            "classPts" => f => f with { ClassPts = f.ClassPts + 1 },
            "originX" => f => f with { Origin = new Vec3Mm(f.Origin.X + 1, f.Origin.Y, f.Origin.Z) },
            "originY" => f => f with { Origin = new Vec3Mm(f.Origin.X, f.Origin.Y + 1, f.Origin.Z) },
            "originZ" => f => f with { Origin = new Vec3Mm(f.Origin.X, f.Origin.Y, f.Origin.Z + 1) },
            "velX" => f => f with { Vel = new Vec3Mm(f.Vel.X + 1, f.Vel.Y, f.Vel.Z) },
            "velY" => f => f with { Vel = new Vec3Mm(f.Vel.X, f.Vel.Y + 1, f.Vel.Z) },
            "velZ" => f => f with { Vel = new Vec3Mm(f.Vel.X, f.Vel.Y, f.Vel.Z + 1) },
            "launchTick" => f => f with { LaunchTick = f.LaunchTick + 1 },
            "thrownPm" => f => f with { ThrownPm = f.ThrownPm + 1 },
            "releaseTick" => f => f with { ReleaseTick = f.ReleaseTick + 1 },
            "openAtRelease" => f => f with { OpenAtRelease = !f.OpenAtRelease },
            "hop" => f => f with { Hop = (byte)(f.Hop + 1) },
            "originClamped" => f => f with { OriginClamped = !f.OriginClamped },
            _ => f => f,
        };

        // Act
        byte[] before = Section(baseline, fillerFirst: false, fillerAfter: false, tat5: 0);
        byte[] after = Section(change(baseline), fillerFirst: field == "flightId", fillerAfter: field == "serial", tat5: field == "tat" ? 90u : 0u);

        // Assert
        Assert.Equal(before.Length, after.Length);
        Assert.NotEqual(before, after);
    }

    [Fact]
    [Trait("Id", "TR-throwing-009")]
    public void SameLiveFlightsByDifferentChurnHistories_DumpIdentically()
    {
        // Arrange: both tables launch I0..I5 in order (ids 0..5) and resolve I1 and I3.
        FlightInit[] init = Enumerable.Range(0, 6).Select(FlightTableTests.Init).ToArray();
        var tat = new uint[PlayerSlot.Count];

        // History A: all six in the air, then I1 and I3 resolve in one step.
        var a = new FlightTable(160, new SimCounters());
        foreach (FlightInit f in init)
        {
            Assert.True(a.TryAppend(in f, out _));
        }

        a.Remove(3);
        a.Remove(1);
        a.Compact();

        // History B: I1 resolves before I2 launches; I3 resolves and is still awaiting compaction when I5 launches.
        var b = new FlightTable(160, new SimCounters());
        Assert.True(b.TryAppend(in init[0], out _));
        Assert.True(b.TryAppend(in init[1], out _));
        b.Remove(1);
        b.Compact();
        Assert.True(b.TryAppend(in init[2], out _));
        Assert.True(b.TryAppend(in init[3], out int i3));
        Assert.True(b.TryAppend(in init[4], out _));
        b.Remove(i3);
        Assert.True(b.TryAppend(in init[5], out _));

        // Act
        byte[] dumpA = Write(new ThrowingStateContributor(a, tat));
        byte[] dumpB = Write(new ThrowingStateContributor(b, tat));

        // Assert
        Assert.Equal(4, a.LiveFlights);
        Assert.Equal(dumpA, dumpB);
    }

    // ---------------------------------------------------------------- helpers

    private static byte[] Section(FlightInit flight, bool fillerFirst, bool fillerAfter, uint tat5)
    {
        var table = new FlightTable(160, new SimCounters());
        FlightInit filler = FlightTableTests.Init(40);
        FlightInit first = fillerFirst ? filler : flight;
        FlightInit second = fillerFirst ? flight : filler;
        Assert.True(table.TryAppend(in first, out _));
        Assert.True(table.TryAppend(in second, out _));
        table.Remove(fillerFirst ? 0 : 1);
        table.Compact();
        if (fillerAfter)
        {
            Assert.True(table.TryAppend(in filler, out int index));
            table.Remove(index);
            table.Compact();
        }

        var tat = new uint[PlayerSlot.Count];
        tat[5] = tat5;
        return Write(new ThrowingStateContributor(table, tat));
    }

    private static byte[] Write(ThrowingStateContributor contributor)
    {
        var output = new ArrayBufferWriter<byte>();
        var w = new CanonicalWriter(output, new CanonicalBuffer());
        w.BeginSection(contributor.SectionId);
        contributor.Write(ref w);
        w.EndSection();
        return output.WrittenSpan.ToArray();
    }
}
