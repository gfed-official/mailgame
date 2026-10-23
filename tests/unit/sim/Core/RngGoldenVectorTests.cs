using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Covers TR-sim-027, TR-sim-029, TR-sim-030 and GDD SC-F5-1 / SC-R25-1: ADR-0005 golden vectors V1 to V6
/// asserted verbatim, plus draw counting, reseeding and the zero-allocation guardrail.
/// </summary>
public class RngGoldenVectorTests
{
    private sealed class StubSource : IRngSource
    {
        private readonly uint[] _values;
        private int _next;

        public StubSource(params uint[] values) => _values = values;

        public uint Next32() => _values[_next++];
    }

    internal static RngStream Create(ulong runSeed, in StreamName name, uint index)
        => new(in name, index, RngDerive.Derive(runSeed, in name, index));

    [Fact]
    [Trait("Id", "SC-F5-1")]
    public void V1_Pcg32Srandom42_54_MatchesReferenceDemo()
    {
        var pcg = new Pcg32(42, 54);
        uint[] expected = [0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e];

        foreach (uint e in expected)
        {
            Assert.Equal(e, pcg.Next32());
        }
    }

    [Theory]
    [Trait("Id", "SC-F5-1")]
    [InlineData(0x0000000000000000UL, 0xe220a8397b1dcdafUL)]
    [InlineData(0x0000000000000001UL, 0x910a2dec89025cc1UL)]
    [InlineData(0xffffffffffffffffUL, 0xe4d971771b652c20UL)]
    public void V2_Mix64_MatchesGolden(ulong x, ulong expected)
    {
        Assert.Equal(expected, SplitMix.Mix64(x));
    }

    public static TheoryData<byte[], ulong> V3Rows => new()
    {
        { ""u8.ToArray(), 0xcbf29ce484222325UL },
        { "mail"u8.ToArray(), 0x1f5964a2ce98072eUL },
        { "mail.special"u8.ToArray(), 0x67c7e88750f3703bUL },
        { "waves"u8.ToArray(), 0xe5f7284c8d6e5825UL },
        { "mega"u8.ToArray(), 0x42dfd9a2e2f6fa13UL },
        { "drops"u8.ToArray(), 0x6cbe52c93e8b1bedUL },
        { "perks"u8.ToArray(), 0x4b06f5847096e54aUL },
        { "shop"u8.ToArray(), 0x46dc5e18e28e2409UL },
        { "events"u8.ToArray(), 0xdd63d0cec6bb7d04UL },
        { "worldgen.terrain"u8.ToArray(), 0xa4c307e2ae7e967cUL },
        { "\u00e9"u8.ToArray(), 0x0ac21707b7181e01UL },          // non-ASCII: U+00E9 is C3 A9 in UTF-8
        { "na\u00efve"u8.ToArray(), 0x1e858bc68a6332abUL },       // non-ASCII inside ASCII text
        { new byte[] { 0xff, 0x00, 0x80 }, 0xf920891be415651eUL }, // bytes >= 0x80 and an embedded NUL hash as unsigned bytes
    };

    [Theory]
    [Trait("Id", "SC-F5-1")]
    [MemberData(nameof(V3Rows))]
    public void V3_Fnv1a64_MatchesGolden(byte[] name, ulong expected)
    {
        Assert.Equal(expected, Fnv1a64Hash.Hash(name));
    }

    public static TheoryData<ulong, byte[], uint, ulong, ulong, ulong, uint[], uint[]> V4Rows => new()
    {
        { 1UL, "mail"u8.ToArray(), 2U, 0x120344c795c4b10eUL, 0x7572d87e0532cc6eUL, 0xb564478f6f06f42bUL,
            [0x915ab962, 0xc3f6d39c, 0x5d7b280d, 0x37138e0e, 0x1bf738c4], [5, 7, 3, 2, 1] },
        { 1UL, "mail"u8.ToArray(), 1U, 0x71928a09cee23ed8UL, 0xb36582a2df730e15UL, 0x3a5a6685d7fc12e3UL,
            [0xd73dddb2, 0x5db76833, 0xbf9328bd, 0x09777055, 0x30103e80], [8, 3, 7, 0, 1] },
        { 1UL, "mail.special"u8.ToArray(), 2U, 0x569a97cfc8b20ef4UL, 0x0f50dae756575348UL, 0xd313692e46864483UL,
            [0x750d9336, 0x666b05a2, 0xd99f6ef9, 0x46e06292, 0x0a60bcd6], [4, 4, 8, 2, 0] },
        { 0UL, "perks"u8.ToArray(), 0U, 0x717d64703c6111feUL, 0x9df2891a81d4a474UL, 0x657fd083a7a917d7UL,
            [0xb99897ca, 0x9f395f21, 0x20feb700, 0x180af528, 0x3ebc0465], [7, 6, 1, 0, 2] },
        { 0xdeadbeefcafef00dUL, "waves"u8.ToArray(), 0U, 0xd89cad0d820c7269UL, 0xdc009665b53eddaaUL, 0x6777a27c2a10746fUL,
            [0x02859650, 0xaecda658, 0x2c4199a8, 0xce36ca27, 0x9e237e0c], [0, 6, 1, 8, 6] },
        { 1UL, "events"u8.ToArray(), 5U, 0x95b59375007b3007UL, 0xfc16d19ae40cf14aUL, 0x0ea626dc54adc689UL,
            [0x05bba7d5, 0x54c6df50, 0x0c424a61, 0xa19bb66d, 0x73e8a584], [0, 3, 0, 6, 4] },
    };

    [Theory]
    [Trait("Id", "SC-F5-1")]
    [MemberData(nameof(V4Rows))]
    public void V4_DeriveSeedAndFirstDraws_MatchGolden(
        ulong runSeed, byte[] name, uint index, ulong derive, ulong state, ulong inc, uint[] draws, uint[] ints)
    {
        ulong derived = RngDerive.Derive(runSeed, name, index);
        Assert.Equal(derive, derived);

        // State and inc right after srandom, read from the generator before any draw.
        Pcg32 seeded = RngDerive.Seed(derived);
        Assert.Equal(state, seeded.State);
        Assert.Equal(inc, seeded.Inc);

        var raw = new RngStream(default, index, derived);
        Assert.Equal(state, raw.State);
        Assert.Equal(inc, raw.Inc);
        for (int i = 0; i < draws.Length; i++)
        {
            Assert.Equal(draws[i], raw.Next32());
        }

        var bounded = new RngStream(default, index, derived);
        for (int i = 0; i < ints.Length; i++)
        {
            Assert.Equal(ints[i], bounded.NextInt(10));
        }

        Assert.Equal((ulong)draws.Length, raw.Draws);
        Assert.Equal((ulong)ints.Length, bounded.Draws);
    }

    [Fact]
    [Trait("Id", "SC-F5-1")]
    public void V4_Row1_InitStateAndInitSeq_MatchGolden()
    {
        ulong s = RngDerive.Derive(1, "mail"u8, 2);

        Assert.Equal(0x56c4414fc45fe704UL, SplitMix.Mix64(s));
        Assert.Equal(0xdab223c7b7837a15UL, SplitMix.Mix64(s ^ 0xDA3E39CB94B95BDBUL));
    }

    [Fact]
    [Trait("Id", "SC-F5-1")]
    public void V4_DeriveWithPrecomputedHash_EqualsDeriveWithName()
    {
        ulong hash = Fnv1a64Hash.Hash("mail"u8);

        Assert.Equal(RngDerive.Derive(1, "mail"u8, 2), RngDerive.Derive(1, hash, 2));
    }

    [Fact]
    [Trait("Id", "SC-F5-1")]
    public void V5_LongRun_StateAndNextOutputMatchGolden()
    {
        RngStream s = Create(1, in RngNames.Mail, 2);
        for (int i = 0; i < 1000; i++)
        {
            s.Next32();
        }

        Assert.Equal(0xcac3e44cb2a5fa56UL, s.State);
        Assert.Equal(0x3d6fc4acU, s.Next32());
        Assert.Equal(1001UL, s.Draws);
    }

    [Theory]
    [Trait("Id", "SC-R25-1")]
    [InlineData(0x80000000U, 10U, 5U)]
    [InlineData(0xFFFFFFFFU, 10U, 9U)]
    [InlineData(0x00000000U, 10U, 0U)]
    [InlineData(0xFFFFFFFFU, 0xFFFFFFFFU, 0xFFFFFFFEU)]
    [InlineData(0x12345678U, 65535U, 4660U)]
    public void V6_NextInt_MatchesGolden(uint raw, uint bound, uint expected)
    {
        var s = new RngStream(new StubSource(raw));

        Assert.Equal(expected, s.NextInt(bound));
        Assert.Equal(1UL, s.Draws);
    }

    [Fact]
    [Trait("Id", "SC-R25-1")]
    public void V6_NextIntZeroSequence_ConsumesOneDrawPerCall()
    {
        var s = new RngStream(new StubSource(0x80000000U, 0xFFFFFFFFU, 0U, 0xFFFFFFFFU, 0x11111111U));

        Assert.Equal(5U, s.NextInt(10));
        Assert.Equal(1UL, s.Draws);
        Assert.Equal(9U, s.NextInt(10));
        Assert.Equal(2UL, s.Draws);
        Assert.Equal(0U, s.NextInt(10));
        Assert.Equal(3UL, s.Draws);

        if (TestBuildExpectation.SimAssertsExpected)
        {
            var ex = Assert.Throws<SimAssertException>(() => s.NextInt(0));
            Assert.Equal(SimAssertCode.RngBoundZero, ex.Code);
        }
        else
        {
            Assert.Equal(0U, s.NextInt(0));
        }

        // The draw is consumed even when the assert fires.
        Assert.Equal(4UL, s.Draws);
    }

    [Fact]
    [Trait("Id", "SC-R25-1")]
    public void Draws_IncrementsForEveryCall()
    {
        RngStream s = Create(7, in RngNames.Shop, 0);
        Assert.Equal(0UL, s.Draws);

        s.Next32();
        Assert.Equal(1UL, s.Draws);
        s.NextInt(6);
        Assert.Equal(2UL, s.Draws);
        s.NextInt(1);
        Assert.Equal(3UL, s.Draws);
    }

    [Fact]
    [Trait("Id", "SC-F5-1")]
    public void Reseed_ResetsDrawsAndRestartsSequence()
    {
        ulong derived = RngDerive.Derive(1, "mail"u8, 2);
        var s = new RngStream(in RngNames.Mail, 2, derived);
        for (int i = 0; i < 17; i++)
        {
            s.Next32();
        }

        s.Reseed(derived);

        Assert.Equal(0UL, s.Draws);
        Assert.Equal(0x915ab962U, s.Next32());
        Assert.Equal(0xc3f6d39cU, s.Next32());
        Assert.Equal(0x5d7b280dU, s.Next32());
    }

    [Fact]
    [Trait("Id", "SC-R25-1")]
    public void NextInt_BoundOne_AlwaysReturnsZeroAndConsumesOneDraw()
    {
        var s = new RngStream(new StubSource(0U, 0x80000000U, 0xFFFFFFFFU));

        Assert.Equal(0U, s.NextInt(1));
        Assert.Equal(0U, s.NextInt(1));
        Assert.Equal(0U, s.NextInt(1));
        Assert.Equal(3UL, s.Draws);
    }

    [Fact]
    [Trait("Id", "SC-F5-1")]
    public void DefaultPcg32_EmitsConstantZero()
    {
        // default(Pcg32) has inc 0 (not odd): documented as unusable, pinned here so the note stays true.
        var pcg = default(Pcg32);

        Assert.Equal(0UL, pcg.Inc);
        Assert.Equal(0U, pcg.Next32());
        Assert.Equal(0U, pcg.Next32());
        Assert.Equal(0UL, pcg.State);
    }
}

/// <summary>
/// Zero-allocation guardrail for <see cref="RngGoldenVectorTests"/> (TR-sim-030), in the non-parallel collection so
/// runtime warm-up on other test threads cannot land in the measured window.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class RngGoldenVectorAllocationTests
{
    [Fact]
    [Trait("Id", "SC-F5-1")]
    public void Draws_AllocateZeroBytesAfterWarmUp()
    {
        // Arrange
        RngStream s = RngGoldenVectorTests.Create(1, in RngNames.Mail, 2);
        uint sink = 0;

        // Act + Assert (0 B)
        int runs = AllocAssert.Zero("100,000 Next32 + NextInt(10) draws", () =>
        {
            for (int i = 0; i < 100_000; i++)
            {
                sink += s.Next32() + s.NextInt(10);
            }
        });

        // Assert
        Assert.Equal(200_000UL * (ulong)runs, s.Draws);
        GC.KeepAlive(sink); // keep the loop observable
    }
}
