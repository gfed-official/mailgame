using System.Reflection;
using System.Text;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Covers TR-sim-028, TR-sim-029 and GDD SC-R24-1 (registry half): <see cref="RngNames"/> contents and golden
/// hashes (AC-2), <see cref="RngRegistry"/> create/re-create/out-of-range behaviour and the zero-allocation
/// guardrail (AC-1), and owner-filtered <see cref="IRngStreams"/> views plus live-stream enumeration (AC-3).
/// The Debug (SIM_ASSERT) and Release branches are selected by <see cref="TestBuildExpectation.SimAssertsExpected"/>.
/// All seeds are fixed constants; nothing here touches time, I/O or global state.
/// </summary>
public class RngRegistryTests
{
    private const ulong RunSeed = 1;

    internal static (RngRegistry Registry, SimCounters Counters) NewRegistry(ulong runSeed = RunSeed)
    {
        var counters = new SimCounters();
        return (new RngRegistry(runSeed, counters), counters);
    }

    private static uint[] Draw(RngStream stream, int count)
    {
        var values = new uint[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = stream.Next32();
        }

        return values;
    }

    private static IEnumerable<(StreamName Name, uint Index)> AllKeys()
    {
        for (int i = 0; i < RngNames.Count; i++)
        {
            StreamName name = RngNames.ByOrdinal(i);
            for (uint index = name.MinIndex; index <= name.MaxIndex; index++)
            {
                yield return (name, index);
            }
        }
    }

    // ---------------------------------------------------------------- AC-1

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac1_MailShift1And2_ProduceDifferentSequences()
    {
        var (registry, _) = NewRegistry();

        uint[] shift1 = Draw(registry.Create(in RngNames.Mail, 1), 8);
        uint[] shift2 = Draw(registry.Create(in RngNames.Mail, 2), 8);

        Assert.NotEqual(shift1, shift2);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac1_CreateMail_FirstDrawsMatchAdr0005GoldenV4()
    {
        var (registry, _) = NewRegistry();

        Assert.Equal(
            new uint[] { 0xd73dddb2, 0x5db76833, 0xbf9328bd, 0x09777055, 0x30103e80 },
            Draw(registry.Create(in RngNames.Mail, 1), 5));
        Assert.Equal(
            new uint[] { 0x915ab962, 0xc3f6d39c, 0x5d7b280d, 0x37138e0e, 0x1bf738c4 },
            Draw(registry.Create(in RngNames.Mail, 2), 5));
        Assert.Equal(
            new uint[] { 0x750d9336, 0x666b05a2, 0xd99f6ef9, 0x46e06292, 0x0a60bcd6 },
            Draw(registry.Create(in RngNames.MailSpecial, 2), 5));
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac1_CreateSameKeyTwice_ReturnsSameHandleResetsDrawsAndReplays()
    {
        var (registry, counters) = NewRegistry();

        RngStream first = registry.Create(in RngNames.Mail, 1);
        uint[] firstRun = Draw(first, 6);
        Assert.Equal(6UL, first.Draws);

        RngStream second = registry.Create(in RngNames.Mail, 1);

        Assert.Same(first, second);
        Assert.Equal(0UL, second.Draws);
        Assert.Equal(firstRun, Draw(second, 6));
        Assert.Equal(6UL, first.Draws);
        Assert.Equal(0L, counters[CounterId.RngIndexOutOfRange]);
        Assert.Equal(0L, counters[CounterId.RngRefused]);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac1_RegistryReportsExactlyOneLiveEntryPerKey()
    {
        var (registry, _) = NewRegistry();
        Assert.Equal(0, registry.LiveCount);
        Assert.False(registry.IsLive(in RngNames.Mail, 1));

        registry.Create(in RngNames.Mail, 1);
        registry.Create(in RngNames.Mail, 1);
        registry.Create(in RngNames.Mail, 1);

        Assert.Equal(1, registry.LiveCount);
        Assert.True(registry.IsLive(in RngNames.Mail, 1));
        Assert.False(registry.IsLive(in RngNames.Mail, 2));
        Assert.False(registry.IsLive(in RngNames.MailSpecial, 1));
        var live = new RngStream[registry.HandleCount];
        Assert.Equal(1, registry.CopyLive(live));
        Assert.Equal((RngNames.Mail.Fnv, 1U), (live[0].Name.Fnv, live[0].Index));
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac1_StaleReferenceDrawsTheFreshSequenceAfterRecreate()
    {
        var (registry, _) = NewRegistry();
        RngStream held = registry.Create(in RngNames.Waves, 0);
        uint expectedFirst = held.Next32();
        held.Next32();

        registry.Create(in RngNames.Waves, 0);

        Assert.Equal(expectedFirst, held.Next32());
        Assert.Equal(1UL, held.Draws);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac1_HandleCarriesNameAndIndex()
    {
        var (registry, _) = NewRegistry();

        RngStream s = registry.Create(in RngNames.Events, 4);

        Assert.Equal(RngNames.Events.Fnv, s.Name.Fnv);
        Assert.Equal(RngNames.Events.Ordinal, s.Name.Ordinal);
        Assert.Equal(4U, s.Index);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac1_EveryKeyHasItsOwnPreallocatedHandle()
    {
        var (registry, _) = NewRegistry();
        var handles = new List<RngStream>();

        foreach (var (name, index) in AllKeys())
        {
            handles.Add(registry.Create(in name, index));
        }

        Assert.Equal(21, handles.Count);
        Assert.Equal(21, registry.HandleCount);
        Assert.Equal(21, registry.LiveCount);
        Assert.Equal(21, handles.Distinct().Count());

        // Re-creating returns the very same objects (preallocated at composition, never replaced).
        int i = 0;
        foreach (var (name, index) in AllKeys())
        {
            Assert.Same(handles[i++], registry.Create(in name, index));
        }

        Assert.Equal(21, registry.LiveCount);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac1_RecreatingOneKey_DoesNotDisturbOtherStreams()
    {
        var (registry, _) = NewRegistry();
        RngStream mail1 = registry.Create(in RngNames.Mail, 1);
        RngStream shop = registry.Create(in RngNames.Shop, 0);
        mail1.Next32();
        shop.Next32();
        ulong shopState = shop.State;

        registry.Create(in RngNames.Mail, 1);

        Assert.Equal(shopState, shop.State);
        Assert.Equal(1UL, shop.Draws);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac1_SameSeedGivesIdenticalWorlds_DifferentSeedDiffers()
    {
        var (a, _) = NewRegistry(RunSeed);
        var (b, _) = NewRegistry(RunSeed);
        var (c, _) = NewRegistry(2);

        uint[] fromA = Draw(a.Create(in RngNames.Perks, 0), 6);

        Assert.Equal(fromA, Draw(b.Create(in RngNames.Perks, 0), 6));
        Assert.NotEqual(fromA, Draw(c.Create(in RngNames.Perks, 0), 6));
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac1_Registries_AreIndependentPerWorld()
    {
        var (a, _) = NewRegistry();
        var (b, _) = NewRegistry();

        RngStream sa = a.Create(in RngNames.Shop, 0);
        RngStream sb = b.Create(in RngNames.Shop, 0);
        sa.Next32();

        Assert.NotSame(sa, sb);
        Assert.Equal(0UL, sb.Draws);
        Assert.Equal(1, b.LiveCount);
    }

    [Theory]
    [Trait("Id", "SC-R24-1")]
    [InlineData(0U)]
    [InlineData(6U)]
    [InlineData(uint.MaxValue)]
    public void Ac1_MailIndexOutOfRange_AssertsInDebugElseUsesSpareSlotAndCountsOnce(uint badIndex)
    {
        var (registry, counters) = NewRegistry();
        RngStream shift1 = registry.Create(in RngNames.Mail, 1);
        shift1.Next32();

        if (TestBuildExpectation.SimAssertsExpected)
        {
            var ex = Assert.Throws<SimAssertException>(() => registry.Create(in RngNames.Mail, badIndex));
            Assert.Equal(SimAssertCode.RngIndexRange, ex.Code);
            Assert.Equal(1L, counters[CounterId.RngIndexOutOfRange]);
        }
        else
        {
            RngStream spare = registry.Create(in RngNames.Mail, badIndex);

            Assert.Equal(1L, counters[CounterId.RngIndexOutOfRange]);
            Assert.NotSame(shift1, spare);
            Assert.Equal(badIndex, spare.Index);
            Assert.Equal(RngNames.Mail.Ordinal, spare.Name.Ordinal);
            // Derived anyway, from the real (name, index) seed.
            var expected = new RngStream(in RngNames.Mail, badIndex, RngDerive.Derive(RunSeed, in RngNames.Mail, badIndex));
            Assert.Equal(Draw(expected, 4), Draw(spare, 4));
        }

        // Either way the spare is never a live registry entry and real streams are untouched.
        Assert.Equal(1, registry.LiveCount);
        Assert.False(registry.IsLive(in RngNames.Mail, badIndex));
        Assert.Equal(1UL, shift1.Draws);
        Assert.Equal(0L, counters[CounterId.RngRefused]);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac1_IndexJustOutsideEveryRange_IsOutOfRange_AndInsideIsNot()
    {
        foreach (var (name, index) in AllKeys())
        {
            var (registry, counters) = NewRegistry();

            registry.Create(in name, index);

            Assert.Equal(0L, counters[CounterId.RngIndexOutOfRange]);
            Assert.True(registry.IsLive(in name, index));
        }

        if (TestBuildExpectation.SimAssertsExpected)
        {
            return; // the Release-only continuation below needs the assert compiled out
        }

        for (int i = 0; i < RngNames.Count; i++)
        {
            StreamName name = RngNames.ByOrdinal(i);
            var (registry, counters) = NewRegistry();

            registry.Create(in name, name.MaxIndex + 1);
            Assert.Equal(1L, counters[CounterId.RngIndexOutOfRange]);

            if (name.MinIndex > 0)
            {
                registry.Create(in name, name.MinIndex - 1);
                Assert.Equal(2L, counters[CounterId.RngIndexOutOfRange]);
            }
        }
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac1_OutOfRange_ReleaseRotatesSparesWithoutAffectingRealStreams()
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            return; // asserts instead of using spares; covered by the theory above
        }

        var (registry, counters) = NewRegistry();
        RngStream real = registry.Create(in RngNames.Mail, 1);
        real.Next32();

        for (int i = 0; i < RngRegistry.SpareCount * 3; i++)
        {
            RngStream spare = registry.Create(in RngNames.Mail, 0);
            Assert.NotSame(real, spare);
            spare.Next32();
        }

        Assert.Equal(RngRegistry.SpareCount * 3, (int)counters[CounterId.RngIndexOutOfRange]);
        Assert.Equal(1UL, real.Draws);
        Assert.Equal(1, registry.LiveCount);
    }

    internal static uint ExerciseCreate(RngRegistry registry, IRngStreams mailView, IRngStreams shopView)
    {
        uint sink = 0;
        sink += registry.Create(in RngNames.WorldgenTerrain, 0).Next32();
        sink += mailView.Create(in RngNames.Mail, 1).Next32();
        sink += mailView.Create(in RngNames.MailSpecial, 5).Next32();
        sink += shopView.Create(in RngNames.Shop, 0).Next32();
        sink += registry.Create(in RngNames.Events, 3).NextInt(10);
        return sink;
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac1_UnregisteredName_AssertsInDebugElseRefusedAndCounted()
    {
        var (registry, counters) = NewRegistry();
        StreamName forged = default;

        if (TestBuildExpectation.SimAssertsExpected)
        {
            var ex = Assert.Throws<SimAssertException>(() => registry.Create(in forged, 0));
            Assert.Equal(SimAssertCode.RngUnknownName, ex.Code);
        }
        else
        {
            RngStream detached = registry.Create(in forged, 0);
            Assert.NotNull(detached);
        }

        Assert.Equal(1L, counters[CounterId.RngRefused]);
        Assert.Equal(0, registry.LiveCount);
        Assert.False(registry.IsLive(in forged, 0));
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac1_IsLive_IsFalseForUnregisteredNameAndOutOfRangeIndex()
    {
        var (registry, _) = NewRegistry();
        registry.Create(in RngNames.Waves, 0);
        StreamName forged = default;

        Assert.True(registry.IsLive(in RngNames.Waves, 0));
        Assert.False(registry.IsLive(in RngNames.Waves, 1));
        Assert.False(registry.IsLive(in RngNames.Mail, 0));
        Assert.False(registry.IsLive(in forged, 0));
    }

    // ---------------------------------------------------------------- AC-2

    public static TheoryData<string, int, SystemId, uint, uint, ulong> V1Table => new()
    {
        { "worldgen.terrain", 0, SystemId.WorldGeneration, 0U, 0U, 0xa4c307e2ae7e967cUL },
        { "mail", 1, SystemId.MailDestinations, 1U, 5U, 0x1f5964a2ce98072eUL },
        { "mail.special", 2, SystemId.MailDestinations, 1U, 5U, 0x67c7e88750f3703bUL },
        { "waves", 3, SystemId.EnemyAiWaves, 0U, 0U, 0xe5f7284c8d6e5825UL },
        { "mega", 4, SystemId.EnemyAiWaves, 0U, 0U, 0x42dfd9a2e2f6fa13UL },
        { "drops", 5, SystemId.HealthDamage, 0U, 0U, 0x6cbe52c93e8b1bedUL },
        { "perks", 6, SystemId.Perks, 0U, 0U, 0x4b06f5847096e54aUL },
        { "shop", 7, SystemId.Economy, 0U, 0U, 0x46dc5e18e28e2409UL },
        { "events", 8, SystemId.StampsRunEvents, 1U, 5U, 0xdd63d0cec6bb7d04UL },
    };

    [Theory]
    [Trait("Id", "SC-R24-1")]
    [MemberData(nameof(V1Table))]
    public void Ac2_RngNames_DeclaresAdr0005V1TableVerbatim(
        string expectedName, int ordinal, SystemId owner, uint min, uint max, ulong fnv)
    {
        StreamName name = RngNames.ByOrdinal(ordinal);

        Assert.Equal(expectedName, Encoding.UTF8.GetString(name.Name));
        Assert.Equal(ordinal, name.Ordinal);
        Assert.Equal(owner, name.Owner);
        Assert.Equal(min, name.MinIndex);
        Assert.Equal(max, name.MaxIndex);
        Assert.Equal((int)(max - min) + 1, name.IndexCount);
        Assert.Equal(fnv, name.Fnv);
        Assert.Equal(Fnv1a64Hash.Hash(Encoding.UTF8.GetBytes(expectedName)), name.Fnv);
        Assert.True(RngNames.IsRegistered(in name));
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac2_RngNames_HasExactlyTheTableEntries_AsPublicStaticReadonlyFields()
    {
        FieldInfo[] fields = typeof(RngNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.FieldType == typeof(StreamName))
            .ToArray();

        Assert.Equal(RngNames.Count, V1Table.Count());
        Assert.Equal(RngNames.Count, fields.Length);
        Assert.All(fields, f => Assert.True(f.IsInitOnly, f.Name + " must be static readonly"));
        var ordinals = new List<int>();
        foreach (FieldInfo f in fields)
        {
            var value = (StreamName)f.GetValue(null)!;
            Assert.Equal(value.Fnv, RngNames.ByOrdinal(value.Ordinal).Fnv);
            ordinals.Add(value.Ordinal);
        }

        Assert.Equal(Enumerable.Range(0, RngNames.Count), ordinals.OrderBy(o => o));

        // No other mutable static state on the registry types.
        foreach (Type t in new[] { typeof(RngNames), typeof(RngRegistry), typeof(StreamName), typeof(RngOwnerView) })
        {
            foreach (FieldInfo f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                Assert.True(f.IsLiteral || f.IsInitOnly, t.Name + "." + f.Name + " is mutable static state");
            }
        }
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac2_KeyCount_IsTwentyOne()
    {
        int keys = 0;
        for (int i = 0; i < RngNames.Count; i++)
        {
            keys += RngNames.ByOrdinal(i).IndexCount;
        }

        Assert.Equal(21, keys);
        Assert.Equal(21, AllKeys().Count());
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac2_AllFnvValues_ArePairwiseDistinctAndNonZero()
    {
        var seen = new HashSet<ulong>();

        for (int i = 0; i < RngNames.Count; i++)
        {
            StreamName name = RngNames.ByOrdinal(i);
            Assert.NotEqual(0UL, name.Fnv);
            Assert.True(seen.Add(name.Fnv), "duplicate Fnv for ordinal " + i);
            Assert.Equal(Fnv1a64Hash.Hash(name.Name), name.Fnv);
        }

        Assert.Equal(RngNames.Count, seen.Count);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac2_NamesAreLowercaseAsciiDotSeparated_AndOwnersAreRealOwners()
    {
        for (int i = 0; i < RngNames.Count; i++)
        {
            StreamName name = RngNames.ByOrdinal(i);
            foreach (byte b in name.Name)
            {
                Assert.True((b >= (byte)'a' && b <= (byte)'z') || b == (byte)'.', "bad byte in name ordinal " + i);
            }

            Assert.NotEqual(SystemId.None, name.Owner);
            Assert.NotEqual(SystemId.Throwing, name.Owner); // Throwing has no entry (throwNoDice)
            Assert.True(name.MinIndex <= name.MaxIndex);
        }
    }

    [Theory]
    [Trait("Id", "SC-R24-1")]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(0xdeadbeefcafef00dUL)]
    [InlineData(ulong.MaxValue)]
    public void Ac2_DeriveWithStreamName_EqualsByteSpanOverload_ForEveryNameAndIndex(ulong seed)
    {
        foreach (var (name, index) in AllKeys())
        {
            Assert.Equal(RngDerive.Derive(seed, name.Name, index), RngDerive.Derive(seed, in name, index));
        }
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac2_DeriveWithStreamName_MatchesAdr0005GoldenV4()
    {
        Assert.Equal(0x120344c795c4b10eUL, RngDerive.Derive(1, in RngNames.Mail, 2));
        Assert.Equal(0x71928a09cee23ed8UL, RngDerive.Derive(1, in RngNames.Mail, 1));
        Assert.Equal(0x569a97cfc8b20ef4UL, RngDerive.Derive(1, in RngNames.MailSpecial, 2));
        Assert.Equal(0x717d64703c6111feUL, RngDerive.Derive(0, in RngNames.Perks, 0));
        Assert.Equal(0xd89cad0d820c7269UL, RngDerive.Derive(0xdeadbeefcafef00dUL, in RngNames.Waves, 0));
        Assert.Equal(0x95b59375007b3007UL, RngDerive.Derive(1, in RngNames.Events, 5));
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac2_ByOrdinalOutOfRange_AndDefaultName_AreNotRegistered()
    {
        StreamName none = default;

        Assert.False(RngNames.IsRegistered(in none));
        Assert.False(RngNames.IsRegistered(RngNames.ByOrdinal(-1)));
        Assert.False(RngNames.IsRegistered(RngNames.ByOrdinal(RngNames.Count)));
        Assert.Equal(0, none.Name.Length);
        Assert.Equal(0, none.IndexCount);
        Assert.Equal(0UL, none.Fnv);
        Assert.Equal(SystemId.None, none.Owner);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac2_StreamName_CannotBeConstructedOutsideTheSimAssembly()
    {
        Assert.Empty(typeof(StreamName).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.True(typeof(StreamName).IsValueType);
        Assert.True(typeof(StreamName).GetCustomAttributes(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute), false).Length > 0);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac2_RngDeriveHashOverloadAndSeed_AreNotPublic()
    {
        Assert.Null(typeof(RngDerive).GetMethod(nameof(RngDerive.Derive), BindingFlags.Public | BindingFlags.Static, new[] { typeof(ulong), typeof(ulong), typeof(uint) }));
        Assert.Null(typeof(RngDerive).GetMethod("Seed", BindingFlags.Public | BindingFlags.Static));
    }

    // ---------------------------------------------------------------- AC-3

    public static TheoryData<SystemId, string[]> OwnerToNames => new()
    {
        { SystemId.WorldGeneration, new[] { "worldgen.terrain" } },
        { SystemId.MailDestinations, new[] { "mail", "mail.special" } },
        { SystemId.EnemyAiWaves, new[] { "waves", "mega" } },
        { SystemId.HealthDamage, new[] { "drops" } },
        { SystemId.Perks, new[] { "perks" } },
        { SystemId.Economy, new[] { "shop" } },
        { SystemId.StampsRunEvents, new[] { "events" } },
    };

    private static bool Owns(SystemId owner, StreamName name) => name.Owner == owner;

    [Theory]
    [Trait("Id", "SC-R24-1")]
    [MemberData(nameof(OwnerToNames))]
    public void Ac3_View_CreateSucceedsForOwnedNames_ReturningTheRegistryHandle(SystemId owner, string[] ownedNames)
    {
        var (registry, counters) = NewRegistry();
        IRngStreams view = registry.ViewFor(owner);
        var created = new List<string>();

        foreach (var (name, index) in AllKeys().Where(k => Owns(owner, k.Name)))
        {
            RngStream viaView = view.Create(in name, index);
            Assert.Same(viaView, registry.Create(in name, index));
            Assert.True(registry.IsLive(in name, index));
            created.Add(Encoding.UTF8.GetString(name.Name));
        }

        Assert.Equal(ownedNames.OrderBy(n => n), created.Distinct().OrderBy(n => n));
        Assert.Equal(0L, counters[CounterId.RngRefused]);
        Assert.Equal(0L, counters[CounterId.RngIndexOutOfRange]);
    }

    [Theory]
    [Trait("Id", "SC-R24-1")]
    [MemberData(nameof(OwnerToNames))]
    public void Ac3_View_AnotherOwnersName_AssertsInDebugElseRefusedAndCounted(SystemId owner, string[] ownedNames)
    {
        _ = ownedNames;
        var (registry, counters) = NewRegistry();
        IRngStreams view = registry.ViewFor(owner);
        long expectedRefusals = 0;

        foreach (var (name, index) in AllKeys().Where(k => !Owns(owner, k.Name)))
        {
            expectedRefusals++;
            if (TestBuildExpectation.SimAssertsExpected)
            {
                var ex = Assert.Throws<SimAssertException>(() => view.Create(in name, index));
                Assert.Equal(SimAssertCode.RngOwnerMismatch, ex.Code);
            }
            else
            {
                RngStream refused = view.Create(in name, index);
                Assert.NotNull(refused);
            }

            Assert.False(registry.IsLive(in name, index));
        }

        Assert.Equal(expectedRefusals, counters[CounterId.RngRefused]);
        Assert.Equal(0, registry.LiveCount);
        Assert.Equal(0L, counters[CounterId.RngIndexOutOfRange]);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac3_RefusedRequest_NeverTouchesTheOwnersRealStream()
    {
        var (registry, counters) = NewRegistry();
        RngStream real = registry.ViewFor(SystemId.EnemyAiWaves).Create(in RngNames.Waves, 0);
        real.Next32();
        real.Next32();
        ulong state = real.State;
        IRngStreams intruder = registry.ViewFor(SystemId.Economy);

        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Throws<SimAssertException>(() => intruder.Create(in RngNames.Waves, 0));
        }
        else
        {
            RngStream refused = intruder.Create(in RngNames.Waves, 0);
            Assert.NotSame(real, refused);
            refused.Next32();
        }

        Assert.Equal(2UL, real.Draws);
        Assert.Equal(state, real.State);
        Assert.Equal(1L, counters[CounterId.RngRefused]);
        Assert.Equal(1, registry.LiveCount);
    }

    [Theory]
    [Trait("Id", "SC-R24-1")]
    [InlineData(SystemId.Throwing)]
    [InlineData(SystemId.None)]
    [InlineData((SystemId)200)]
    public void Ac3_ThrowingNoneAndUndefinedOwner_ViewExposesNoStream(SystemId owner)
    {
        var (registry, counters) = NewRegistry();
        IRngStreams view = registry.ViewFor(owner);
        long keys = 0;

        foreach (var (name, index) in AllKeys())
        {
            keys++;
            if (TestBuildExpectation.SimAssertsExpected)
            {
                var ex = Assert.Throws<SimAssertException>(() => view.Create(in name, index));
                Assert.Equal(SimAssertCode.RngOwnerMismatch, ex.Code);
            }
            else
            {
                Assert.NotNull(view.Create(in name, index));
            }
        }

        Assert.Equal(keys, counters[CounterId.RngRefused]);
        Assert.Equal(0, registry.LiveCount);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac3_ThrowingAndNoneViews_RefuseEvenAnUnregisteredName()
    {
        var (registry, counters) = NewRegistry();
        StreamName forged = default;

        foreach (SystemId id in new[] { SystemId.Throwing, SystemId.None })
        {
            IRngStreams view = registry.ViewFor(id);
            if (TestBuildExpectation.SimAssertsExpected)
            {
                Assert.Throws<SimAssertException>(() => view.Create(in forged, 0));
            }
            else
            {
                Assert.NotNull(view.Create(in forged, 0));
            }
        }

        Assert.Equal(2L, counters[CounterId.RngRefused]);
        Assert.Equal(0, registry.LiveCount);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac3_EveryRegisteredNameHasExactlyOneOwnerWhoseViewCanCreateIt()
    {
        var (registry, _) = NewRegistry();

        for (int i = 0; i < RngNames.Count; i++)
        {
            StreamName name = RngNames.ByOrdinal(i);
            int owners = 0;
            foreach (SystemId id in Enum.GetValues<SystemId>())
            {
                if (registry.ViewFor(id) is RngOwnerView v && v.Owner == id && id == name.Owner)
                {
                    owners++;
                    Assert.Same(registry.Create(in name, name.MinIndex), v.Create(in name, name.MinIndex));
                }
            }

            Assert.Equal(1, owners);
        }
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac3_ViewFor_ReturnsTheSameInstanceAndReportsItsOwner()
    {
        var (registry, _) = NewRegistry();

        foreach (SystemId id in Enum.GetValues<SystemId>())
        {
            IRngStreams view = registry.ViewFor(id);
            Assert.Same(view, registry.ViewFor(id));
            Assert.Equal(id, ((RngOwnerView)view).Owner);
        }

        Assert.Equal(SystemId.None, ((RngOwnerView)registry.ViewFor((SystemId)200)).Owner);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac3_View_OutOfRangeIndexOfOwnedName_CountsIndexCounterNotRefusal()
    {
        var (registry, counters) = NewRegistry();
        IRngStreams view = registry.ViewFor(SystemId.StampsRunEvents);

        if (TestBuildExpectation.SimAssertsExpected)
        {
            var ex = Assert.Throws<SimAssertException>(() => view.Create(in RngNames.Events, 0));
            Assert.Equal(SimAssertCode.RngIndexRange, ex.Code);
        }
        else
        {
            Assert.NotNull(view.Create(in RngNames.Events, 0));
        }

        Assert.Equal(1L, counters[CounterId.RngIndexOutOfRange]);
        Assert.Equal(0L, counters[CounterId.RngRefused]);
        Assert.Equal(0, registry.LiveCount);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac3_CopyLive_EnumeratesInDeclarationOrderThenAscendingIndex()
    {
        var (registry, _) = NewRegistry();
        // Create in a deliberately scrambled order.
        registry.Create(in RngNames.Events, 5);
        registry.Create(in RngNames.Shop, 0);
        registry.Create(in RngNames.Mail, 3);
        registry.Create(in RngNames.MailSpecial, 1);
        registry.Create(in RngNames.Mail, 1);
        registry.Create(in RngNames.WorldgenTerrain, 0);
        registry.Create(in RngNames.Events, 1);
        registry.Create(in RngNames.Mail, 3);

        var live = new RngStream[registry.LiveCount];
        int written = registry.CopyLive(live);

        Assert.Equal(7, written);
        var order = live.Select(s => (s.Name.Ordinal, s.Index)).ToArray();
        Assert.Equal(
            new (ushort, uint)[]
            {
                (RngNames.WorldgenTerrain.Ordinal, 0), (RngNames.Mail.Ordinal, 1), (RngNames.Mail.Ordinal, 3),
                (RngNames.MailSpecial.Ordinal, 1), (RngNames.Shop.Ordinal, 0),
                (RngNames.Events.Ordinal, 1), (RngNames.Events.Ordinal, 5),
            },
            order);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac3_CopyLive_AllKeysLive_IsStrictlyIncreasingByOrdinalThenIndex()
    {
        var (registry, _) = NewRegistry();
        foreach (var (name, index) in AllKeys().Reverse())
        {
            registry.Create(in name, index);
        }

        var live = new RngStream[registry.HandleCount];
        Assert.Equal(21, registry.CopyLive(live));

        for (int i = 1; i < live.Length; i++)
        {
            bool increasing = live[i].Name.Ordinal > live[i - 1].Name.Ordinal
                || (live[i].Name.Ordinal == live[i - 1].Name.Ordinal && live[i].Index > live[i - 1].Index);
            Assert.True(increasing, "not increasing at " + i);
        }
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac3_NewRegistry_HasNoLiveStreams_AndCopyLiveWritesNothing()
    {
        var (registry, _) = NewRegistry();
        var live = new RngStream[registry.HandleCount];

        Assert.Equal(0, registry.LiveCount);
        Assert.Equal(0, registry.CopyLive(live));
        Assert.All(live, Assert.Null);
    }
}

/// <summary>
/// Zero-allocation guardrail for <see cref="RngRegistryTests"/> (AC-1, AC-3), in the non-parallel collection so
/// runtime warm-up on other test threads cannot land in a measured window.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class RngRegistryAllocationTests
{
    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac1_Create_AllocatesZeroBytesAfterComposition()
    {
        // Arrange
        var (registry, counters) = RngRegistryTests.NewRegistry();
        IRngStreams mailView = registry.ViewFor(SystemId.MailDestinations);
        IRngStreams shopView = registry.ViewFor(SystemId.Economy);
        uint sink = 0;

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("5,000 rounds of Create through registry and owner views", () =>
        {
            for (int i = 0; i < 5_000; i++)
            {
                sink += RngRegistryTests.ExerciseCreate(registry, mailView, shopView);
            }
        });

        // Assert
        Assert.Equal(0L, counters[CounterId.RngIndexOutOfRange]);
        GC.KeepAlive(sink);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac1_OutOfRangeAndRefusedPaths_AllocateZeroBytesInRelease()
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            return; // these paths throw under SIM_ASSERT
        }

        // Arrange
        var (registry, _) = RngRegistryTests.NewRegistry();
        IRngStreams throwing = registry.ViewFor(SystemId.Throwing);
        uint sink = 0;

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("5,000 out-of-range and refused-owner Creates", () =>
        {
            for (int i = 0; i < 5_000; i++)
            {
                sink += registry.Create(in RngNames.Mail, 0).Next32() + throwing.Create(in RngNames.Mail, 1).Next32();
            }
        });

        // Assert
        GC.KeepAlive(sink);
    }

    [Fact]
    [Trait("Id", "SC-R24-1")]
    public void Ac3_CopyLive_ShortOrEmptyDestination_StopsWithoutThrowing_AndNeverAllocates()
    {
        // Arrange
        var (registry, _) = RngRegistryTests.NewRegistry();
        registry.Create(in RngNames.Mail, 1);
        registry.Create(in RngNames.Mail, 2);
        registry.Create(in RngNames.Shop, 0);

        // Act + Assert: short and empty destinations stop without throwing.
        Assert.Equal(0, registry.CopyLive(Span<RngStream>.Empty));
        var two = new RngStream[2];
        Assert.Equal(2, registry.CopyLive(two));
        Assert.Equal(2U, two[1].Index);

        // Act + Assert (0 B)
        var big = new RngStream[8];
        _ = AllocAssert.Zero("1,000 CopyLive calls", () =>
        {
            for (int i = 0; i < 1_000; i++)
            {
                registry.CopyLive(big);
            }
        });
    }
}
