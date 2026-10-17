using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>Covers mail-destinations story 012 AC-3 a to g (GDD M-F4-1; F4; TR-mail-003). Deterministic, no IO.</summary>
public class DestinationDrawTests
{
    private const uint Max = uint.MaxValue;

    private static uint R(uint v, uint bound) => DrawTestRig.Raw(v, bound);

    private static (int Pairs, ulong Draws) Pairs(uint raw, int m, int ratio = 300)
    {
        RngStream rng = DrawTestRig.Stub(new ScriptedSource(0, raw));
        int p = DestinationDraw.PairCount(rng, m, ratio);
        return (p, rng.Draws);
    }

    [Fact]
    [Trait("Id", "M-F4-1a")]
    public void NextInt_1000_maps_stub_draws()
    {
        Assert.Equal(0u, DrawTestRig.Stub(new ScriptedSource(0)).NextInt(1000));
        Assert.Equal(500u, DrawTestRig.Stub(new ScriptedSource(0x80000000u)).NextInt(1000));
        Assert.Equal(999u, DrawTestRig.Stub(new ScriptedSource(Max)).NextInt(1000));
    }

    [Fact]
    [Trait("Id", "M-F4-1b")]
    public void M5_pair_rounding_threshold_is_750()
    {
        Assert.Equal((1, 1UL), Pairs(R(749, 1000), 5));
        Assert.Equal((0, 1UL), Pairs(R(750, 1000), 5));
    }

    [Fact]
    [Trait("Id", "M-F4-1c")]
    public void M7_always_one_pair_and_a_second_below_50()
    {
        Assert.Equal((2, 1UL), Pairs(R(49, 1000), 7));
        Assert.Equal((1, 1UL), Pairs(R(50, 1000), 7));
        Assert.Equal((1, 1UL), Pairs(Max, 7));
    }

    [Fact]
    [Trait("Id", "M-F4-1d")]
    public void M0_gives_no_pairs_but_consumes_a_draw()
    {
        Assert.Equal((0, 1UL), Pairs(0, 0));
        RngStream rng = DrawTestRig.Stub(new ScriptedSource(0));
        int r = DestinationDraw.DrawBatch(rng, [], 0, 300, 100, [], []);
        Assert.Equal(0, r);
        Assert.Equal(1UL, rng.Draws);
    }

    [Fact]
    [Trait("Id", "M-F4-1f")]
    public void M1_never_pairs_and_consumes_a_draw()
    {
        Assert.Equal((0, 1UL), Pairs(0, 1));
        Assert.Equal((0, 1UL), Pairs(Max, 1));
    }

    [Fact]
    [Trait("Id", "M-F4-1e")]
    public void Pair_shares_a_street_and_others_use_unused_streets()
    {
        // 18 addresses on d1 streets 1-6 (3 each) plus 3 on d2 street 1 (same street number, different district).
        AddressId[] a = DrawTestRig.Houses(1, 6, 3).Concat(DrawTestRig.Houses(2, 1, 3)).ToArray();
        Assert.Equal(21, a.Length);
        var src = new ScriptedSource(
            0,
            R(0, 1000),                                   // pair draw: 0 < 750 -> 1 pair
            R(0, 21), R(0, 1000),                         // u0: first of pair -> d1 s1 n1
            R(2, 3), R(0, 1000),                          // u1: same street, 3rd house
            R(0, 18), R(99, 1000),                        // u2: unused streets, first -> d1 s2 n1, Cursed (99 < 100)
            R(14, 15), R(100, 1000),                      // u3: unused, last -> d2 s1 n3, not Cursed (100 !< 100)
            R(0, 12), R(0, 1000));                        // u4: unused -> d1 s3 n1
        RngStream rng = DrawTestRig.Stub(src);
        var addr = new AddressId[5];
        var cursed = new bool[5];
        int pairs = DestinationDraw.DrawBatch(rng, a, 5, 300, 100, addr, cursed);
        Assert.Equal(1, pairs);
        Assert.Equal(11UL, rng.Draws);
        Assert.Equal(AddressId.Pack(1, 1, 1), addr[0]);
        Assert.Equal(AddressId.Pack(1, 1, 3), addr[1]);
        Assert.Equal(AddressId.Pack(1, 2, 1), addr[2]);
        Assert.Equal(AddressId.Pack(2, 1, 3), addr[3]);
        Assert.Equal(AddressId.Pack(1, 3, 1), addr[4]);
        Assert.Equal(new[] { true, true, true, false, true }, cursed);   // rolls 0, 0, 99, 100, 0 against a share of 100
    }

    [Fact]
    [Trait("Id", "M-F4-1e")]
    public void Falls_back_to_all_addresses_once_every_street_is_used()
    {
        AddressId[] a = [AddressId.Pack(1, 1, 1), AddressId.Pack(1, 1, 2), AddressId.Pack(1, 2, 1)];
        var src = new ScriptedSource(
            0,
            R(0, 1000),                      // ratio 0 -> no pairs (0 < 0 is false)
            R(0, 3), R(0, 1000),             // u0 -> d1 s1 n1
            R(0, 1), R(0, 1000),             // u1: unused = d1 s2 (1 address) -> d1 s2 n1
            R(2, 3), R(0, 1000),             // u2: none unused -> all 3 addresses, index 2
            R(1, 3), R(0, 1000));            // u3: all 3, index 1
        RngStream rng = DrawTestRig.Stub(src);
        var addr = new AddressId[4];
        int pairs = DestinationDraw.DrawBatch(rng, a, 4, 0, 0, addr, new bool[4]);
        Assert.Equal(0, pairs);
        Assert.Equal(9UL, rng.Draws);
        Assert.Equal(a[0], addr[0]);
        Assert.Equal(a[2], addr[1]);
        Assert.Equal(a[2], addr[2]);
        Assert.Equal(a[1], addr[3]);
    }

    [Fact]
    [Trait("Id", "M-F4-1g")]
    public void Fuzz_paired_share_is_30_percent_over_10000_batches()
    {
        AddressId[] a = DrawTestRig.Houses(1, 6, 3);
        RngStream rng = DrawTestRig.Seeded(DrawTestRig.FuzzSeed, 1);
        var addr = new AddressId[5];
        var cursed = new bool[5];
        int paired = 0;
        for (int i = 0; i < 10_000; i++)
        {
            DestinationDraw.DrawBatch(rng, a, 5, 300, 0, addr, cursed);
            for (int u = 0; u < 5; u++)
            {
                int same = 0;
                for (int v = 0; v < 5; v++)
                {
                    same += addr[v].Street == addr[u].Street ? 1 : 0;
                }

                paired += same >= 2 ? 1 : 0;
            }
        }

        // 30 % +/- 1.5 pp of 50,000 units.
        Assert.InRange(paired, 14_250, 15_750);
        Assert.Equal(10_000UL * 11, rng.Draws);
    }

    [Fact]
    public void Invalid_inputs_return_status_and_draw_nothing()
    {
        RngStream rng = DrawTestRig.Stub(new ScriptedSource(0));
        AddressId[] a = DrawTestRig.Houses(1, 2, 2);
        Assert.Equal(DestinationDrawStatus.InvalidInput, DestinationDraw.DrawBatch(rng, [], 3, 300, 0, new AddressId[3], new bool[3]));
        Assert.Equal(DestinationDrawStatus.InvalidInput, DestinationDraw.DrawBatch(rng, a, -1, 300, 0, new AddressId[3], new bool[3]));
        Assert.Equal(DestinationDrawStatus.OutputTooSmall, DestinationDraw.DrawBatch(rng, a, 3, 300, 0, new AddressId[2], new bool[3]));
        Assert.Equal(0UL, rng.Draws);
    }

    [Fact]
    public void Shuffle_makes_n_minus_1_draws_and_is_a_permutation()
    {
        var items = Enumerable.Range(0, 10).ToArray();
        RngStream rng = DrawTestRig.Seeded(DrawTestRig.FuzzSeed, 2);
        Assert.Equal(9, UnitShuffle.Shuffle(rng, items.AsSpan()));
        Assert.Equal(9UL, rng.Draws);
        Assert.Equal(Enumerable.Range(0, 10), items.OrderBy(x => x));
        RngStream none = DrawTestRig.Stub(new ScriptedSource(0));
        UnitShuffle.Shuffle(none, Array.Empty<int>().AsSpan());
        UnitShuffle.Shuffle(none, new int[1].AsSpan());
        Assert.Equal(0UL, none.Draws);
        Assert.Equal(0, UnitShuffle.DrawCount(0));
    }

    [Fact]
    public void Shuffle_with_zero_stub_draws_rotates_deterministically()
    {
        // NextInt(i+1) = 0 for every i: swap item i with item 0, from the end down.
        var items = new[] { 1, 2, 3, 4 };
        UnitShuffle.Shuffle(DrawTestRig.Stub(new ScriptedSource(0)), items.AsSpan());
        Assert.Equal(new[] { 2, 3, 4, 1 }, items);
    }
}
