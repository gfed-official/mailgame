using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.World;

/// <summary>
/// Covers <see cref="AddressId"/> packing and order (world-stub story 004 AC-1; ADR-0017 section 1; TR-mail-006):
/// bit layout <c>district &lt;&lt; 26 | street &lt;&lt; 18 | number &lt;&lt; 6 | unit</c>, boundary round-trips, numeric
/// order = (district, street, number, unit) order, <c>default</c> = None, and the explicit equality members.
/// Deterministic: the random tuples come from a fixed-seed <see cref="Pcg32"/>.
/// </summary>
public class AddressIdTests
{
    // The 2^4 boundary tuples run in one test; every failing tuple is reported.
    [Fact]
    public void Pack_BoundaryValues_RoundTripThroughComponents()
    {
        var failures = new List<string>();
        int count = 0;
        foreach (int district in new[] { 1, 63 })
        {
            foreach (int street in new[] { 0, 255 })
            {
                foreach (int number in new[] { 0, 4095 })
                {
                    foreach (int unit in new[] { 0, 63 })
                    {
                        // Act
                        AddressId a = AddressId.Pack(district, street, number, unit);
                        count++;

                        // Assert (collected)
                        uint value = ((uint)district << 26) | ((uint)street << 18) | ((uint)number << 6) | (uint)unit;
                        if (a.District != (uint)district || a.Street != (uint)street || a.Number != (uint)number
                            || a.Unit != (uint)unit || a.Value != value || a.IsNone)
                        {
                            failures.Add($"({district}, {street}, {number}, {unit}): got ({a.District}, {a.Street}, {a.Number}, {a.Unit}) value 0x{a.Value:X8} (expected 0x{value:X8}) IsNone {a.IsNone}");
                        }
                    }
                }
            }
        }

        Assert.Equal(16, count);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Pack_BitLayout_MatchesAdrFieldWidths()
    {
        // Act / Assert
        Assert.Equal(0xFFFF_FFFFu, AddressId.Pack(63, 255, 4095, 63).Value);
        Assert.Equal(1u << 26, AddressId.Pack(1, 0, 0).Value);
        Assert.Equal((1u << 26) | (1u << 18), AddressId.Pack(1, 1, 0).Value);
        Assert.Equal((1u << 26) | (7u << 6), AddressId.Pack(1, 0, 7).Value);
        Assert.Equal(0u, AddressId.Pack(1, 2, 3).Unit); // unit defaults to 0 (none)
        Assert.Equal(63, AddressId.MaxDistrict);
        Assert.Equal(255, AddressId.MaxStreet);
        Assert.Equal(4095, AddressId.MaxNumber);
        Assert.Equal(63, AddressId.MaxUnit);
    }

    [Fact]
    public void Order_TenThousandSeededTuples_SortByValueEqualsTupleSort()
    {
        // Arrange
        var rng = new Pcg32(0xADD2E55UL, 4);
        var tuples = new List<(int D, int S, int N, int U)>(10_000);
        for (int i = 0; i < 10_000; i++)
        {
            // Small ranges on some draws so equal leading components (the tie-breaking cases) are common.
            int d = 1 + (int)(rng.Next32() % ((i & 1) == 0 ? 63u : 2u));
            int s = (int)(rng.Next32() % ((i & 2) == 0 ? 256u : 2u));
            int n = (int)(rng.Next32() % ((i & 4) == 0 ? 4096u : 3u));
            int u = (int)(rng.Next32() % 64u);
            tuples.Add((d, s, n, u));
        }

        // Act
        var byTuple = new List<(int D, int S, int N, int U)>(tuples);
        byTuple.Sort((a, b) =>
        {
            int c = a.D.CompareTo(b.D);
            c = c != 0 ? c : a.S.CompareTo(b.S);
            c = c != 0 ? c : a.N.CompareTo(b.N);
            return c != 0 ? c : a.U.CompareTo(b.U);
        });
        var byValue = new List<uint>(tuples.Count);
        foreach ((int d, int s, int n, int u) in tuples)
        {
            byValue.Add(AddressId.Pack(d, s, n, u).Value);
        }

        byValue.Sort();

        // Assert
        for (int i = 0; i < byTuple.Count; i++)
        {
            (int d, int s, int n, int u) = byTuple[i];
            Assert.Equal(AddressId.Pack(d, s, n, u).Value, byValue[i]);
        }
    }

    [Fact]
    public void Default_IsNone_WithValueZero()
    {
        // Act
        AddressId d = default;

        // Assert
        Assert.Equal(0u, d.Value);
        Assert.True(d.IsNone);
        Assert.True(d == AddressId.None);
        Assert.Equal(0u, d.District); // district 0 is reserved, so no valid packed address is None
    }

    [Fact]
    public void Equality_ExplicitMembers_AgreeOnValue()
    {
        // Arrange
        AddressId a = AddressId.Pack(2, 3, 4, 5);
        AddressId same = new AddressId(a.Value);
        AddressId other = AddressId.Pack(2, 3, 4, 6);

        // Act / Assert
        Assert.True(a.Equals(same));
        Assert.True(a.Equals((object)same));
        Assert.False(a.Equals((object)a.Value));
        Assert.True(a == same);
        Assert.False(a != same);
        Assert.True(a != other);
        Assert.False(a == other);
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.True(a.CompareTo(other) < 0);
    }

    [Fact]
    public void Pack_OutOfRangeComponent_AssertsUnderSimAssert_ElseMasksToFieldWidth()
    {
        // Act
        Exception? thrown = Record.Exception(() => AddressId.Pack(1, 256, 0, 0));

        // Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException e = Assert.IsType<SimAssertException>(thrown);
            Assert.Equal(SimAssertCode.AddressComponentOutOfRange, e.Code);
        }
        else
        {
            Assert.Null(thrown);
            Assert.Equal(AddressId.Pack(1, 0, 0), AddressId.Pack(1, 256, 0, 0)); // street masked; district untouched
        }
    }
}
