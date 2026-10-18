using System.Buffers;
using System.Reflection;
using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 012 (TR-movement-011; ADR-0014 section 1; acceptance ids PCM-P10-1 and PCM-P10-2): the verb catalogue,
/// the anchor registry's registration contract and capacity, the dense 2 m grid against a brute-force scan, the purity of the
/// <see cref="IInteractable"/> port, the registry's dump section and the shared default hold length.
/// </summary>
public class AnchorRegistryTests
{
    private const int CellMm = 2000;
    private static readonly MapBounds _street = new(0, 0, 256_000, 128_000);

    // ---------------------------------------------------------------- helpers

    private static AnchorRegistry NewRegistry(out SimCheckRing checks, out SimCounters counters, int max = 4096)
    {
        checks = new SimCheckRing();
        counters = new SimCounters();
        return new AnchorRegistry(_street, max, 2500, 400, checks, counters);
    }

    private static AnchorRef Ref(uint n, byte slot = 0) => new(new EntityId(0x0100_0000u + n), slot);

    private static AnchorDef Def(uint n, int x, int z, Verb verb = Verb.Open, byte slot = 0) =>
        AnchorDef.For(Ref(n, slot), new Vec3Mm(x, 0, z), verb);

    private static byte[] SectionOf(byte[] dump, ushort id)
    {
        int pos = 6;
        while (pos < dump.Length)
        {
            ushort sectionId = BitConverter.ToUInt16(dump, pos);
            int length = (int)BitConverter.ToUInt32(dump, pos + 2);
            if (sectionId == id)
            {
                return dump.AsSpan(pos + 6, length).ToArray();
            }

            pos += 6 + length;
        }

        Assert.Fail($"Section {id} is not in the dump.");
        return Array.Empty<byte>();
    }

    private static HoldSettings DefaultHoldSettings() =>
        HoldSettings.FromPlayerSettings(PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player));

    /// <summary>A pure test double: answers from fields, records nothing, touches no shared state.</summary>
    private class FakeInteractable : IInteractable
    {
        public StringId UnavailableReason { get; init; }

        public bool Available { get; init; } = true;

        public virtual bool IsAvailable(in InteractionContext c, out StringId reason)
        {
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

    private sealed class Box
    {
        public int Value;
    }

    /// <summary>A test double that breaks the purity rule: IsAvailable bumps a captured counter.</summary>
    private sealed class MutatingFake : FakeInteractable
    {
        private readonly Box _box;

        public MutatingFake(Box box)
        {
            _box = box;
        }

        public override bool IsAvailable(in InteractionContext c, out StringId reason)
        {
            _box.Value++;
            return base.IsAvailable(in c, out reason);
        }
    }

    /// <summary>
    /// Test-only decorator (story 012 "PureHookGuard"): hashes the owner's test state before and after each hook and throws when it
    /// changed, so a double that mutates through a captured reference is caught.
    /// </summary>
    private sealed class PureHookGuard : IInteractable
    {
        private readonly IInteractable _inner;
        private readonly Func<int> _stateHash;

        public PureHookGuard(IInteractable inner, Func<int> stateHash)
        {
            _inner = inner;
            _stateHash = stateHash;
        }

        public bool IsAvailable(in InteractionContext c, out StringId reason)
        {
            int before = _stateHash();
            bool r = _inner.IsAvailable(in c, out reason);
            Verify(before, nameof(IsAvailable));
            return r;
        }

        public int GetHoldTicks(in InteractionLatch latch, in InteractionContext c)
        {
            int before = _stateHash();
            int r = _inner.GetHoldTicks(in latch, in c);
            Verify(before, nameof(GetHoldTicks));
            return r;
        }

        public bool ShouldCancel(in InteractionLatch latch, in InteractionContext c, out StringId reason)
        {
            int before = _stateHash();
            bool r = _inner.ShouldCancel(in latch, in c, out reason);
            Verify(before, nameof(ShouldCancel));
            return r;
        }

        public void WritePrompt(in InteractionContext c, ref PromptArgs args)
        {
            int before = _stateHash();
            _inner.WritePrompt(in c, ref args);
            Verify(before, nameof(WritePrompt));
        }

        public bool TryBuildRequest(in InteractionLatch latch, in InteractionContext c, ref Op op)
        {
            int before = _stateHash();
            bool r = _inner.TryBuildRequest(in latch, in c, ref op);
            Verify(before, nameof(TryBuildRequest));
            return r;
        }

        public void OnPress(in InteractionContext c, ref InteractionLatch latch)
        {
            int before = _stateHash();
            _inner.OnPress(in c, ref latch);
            Verify(before, nameof(OnPress));
        }

        private void Verify(int before, string hook)
        {
            if (_stateHash() != before)
            {
                throw new InvalidOperationException("IInteractable." + hook + " mutated the owner's state.");
            }
        }
    }

    // ---------------------------------------------------------------- PCM-P10-1

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void Register_RangeUnset_StoresTheDefault2500_AndExplicitRangeIsKept()
    {
        // Arrange
        AnchorRegistry registry = NewRegistry(out _, out _);
        AnchorDef unset = Def(1, 5000, 5000);
        AnchorDef explicitRange = Def(2, 6000, 6000);
        explicitRange.RangeMm = 3200;

        // Act
        RegisterResult a = registry.Register(in unset, new FakeInteractable());
        RegisterResult b = registry.Register(in explicitRange, new FakeInteractable());

        // Assert
        Assert.Equal(0, unset.RangeMm);   // the caller's struct is untouched; the registry stores the resolved value
        Assert.Equal((RegisterResult.Ok, RegisterResult.Ok), (a, b));
        Assert.True(registry.TryGet(Ref(1), out int i1));
        Assert.True(registry.TryGet(Ref(2), out int i2));
        Assert.Equal(2500, registry.DefAt(i1).RangeMm);
        Assert.Equal(3200, registry.DefAt(i2).RangeMm);
        Assert.Equal(3200, registry.MaxRegisteredRangeMm);
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void Register_SecondVerbOnOneAnchorInOneContext_IsDuplicateVerb_RecordsACheck_AndKeepsTheFirst()
    {
        // Arrange (no #if DEBUG: the rule holds in every build)
        AnchorRegistry registry = NewRegistry(out SimCheckRing checks, out SimCounters counters);
        AnchorDef first = Def(7, 4000, 4000, Verb.Open, slot: 3);
        AnchorDef second = Def(7, 9000, 9000, Verb.Deliver, slot: 3);
        Assert.Equal(RegisterResult.Ok, registry.Register(in first, new FakeInteractable()));
        IInteractable firstImpl = registry.ImplAt(0);

        // Act
        RegisterResult result = registry.Register(in second, new FakeInteractable());

        // Assert
        Assert.Equal(RegisterResult.DuplicateVerb, result);
        Assert.Equal(1, checks.Count);
        Assert.Equal((ushort)CheckCode.AnchorDuplicateVerb, checks[0].Code);
        Assert.Equal((0x0100_0007L, 3L), (checks[0].A, checks[0].B));
        Assert.Equal(1, registry.Count);
        Assert.True(registry.TryGet(Ref(7, 3), out int index));
        Assert.Equal(Verb.Open, registry.DefAt(index).Verb);
        Assert.Equal(new Vec3Mm(4000, 0, 4000), registry.DefAt(index).Point);
        Assert.Same(firstImpl, registry.ImplAt(index));
        Assert.Equal(0, counters[CounterId.AnchorRegistryFull]);
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void Register_BuildModeOnlyOnTheSameRef_IsADifferentContext_ButTwiceThereIsADuplicate()
    {
        // Arrange
        AnchorRegistry registry = NewRegistry(out SimCheckRing checks, out _);
        AnchorDef world = Def(5, 4000, 4000, Verb.Open);
        AnchorDef build = Def(5, 4000, 4000, Verb.Configure);
        build.Flags = AnchorFlags.BuildModeOnly;

        // Act
        RegisterResult w = registry.Register(in world, new FakeInteractable());
        RegisterResult b = registry.Register(in build, new FakeInteractable());
        RegisterResult again = registry.Register(in build, new FakeInteractable());

        // Assert
        Assert.Equal((RegisterResult.Ok, RegisterResult.Ok, RegisterResult.DuplicateVerb), (w, b, again));
        Assert.Equal(2, registry.Count);
        Assert.True(registry.TryGet(Ref(5), AnchorRegistry.ContextWorld, out int wi));
        Assert.True(registry.TryGet(Ref(5), AnchorRegistry.ContextBuild, out int bi));
        Assert.Equal((Verb.Open, Verb.Configure), (registry.DefAt(wi).Verb, registry.DefAt(bi).Verb));
        Assert.Equal(1, checks.Count);
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void IsAvailable_OnTheFake_ReturnsOk_OrAReasonStringId()
    {
        // Arrange
        var reason = new StringId(StringNamespace.Run, 9);
        IInteractable ok = new FakeInteractable();
        IInteractable no = new FakeInteractable { Available = false, UnavailableReason = reason };
        var context = new InteractionContext { Tick = 3 };

        // Act
        bool okResult = ok.IsAvailable(in context, out StringId okReason);
        bool noResult = no.IsAvailable(in context, out StringId noReason);

        // Assert
        Assert.True(okResult);
        Assert.True(okReason.IsNone);
        Assert.False(noResult);
        Assert.Equal(reason, noReason);
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void Verb_HasNoReviveMember_AndRevivesIsNotASpelling()
    {
        // Assert
        Assert.False(VerbSpelling.TryParse("revive"u8, out Verb v));
        Assert.Equal(default, v);
        Assert.False(VerbSpelling.TryParse("Revive"u8, out _));
        Assert.DoesNotContain(Enum.GetNames<Verb>(), n => n.Contains("revive", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(13, Enum.GetValues<Verb>().Length);
    }

    [Theory]
    [Trait("Id", "PCM-P10-1")]
    [InlineData((byte)0)]
    [InlineData((byte)14)]
    [InlineData((byte)255)]
    public void Register_VerbOutsideTheCatalogue_IsInvalidArgument(byte raw)
    {
        // Arrange
        AnchorRegistry registry = NewRegistry(out _, out _);
        AnchorDef def = Def(1, 1000, 1000);
        def.Verb = (Verb)raw;

        // Act
        RegisterResult r = registry.Register(in def, new FakeInteractable());

        // Assert
        Assert.Equal(RegisterResult.InvalidArgument, r);
        Assert.Equal(0, registry.Count);
    }

    // ---------------------------------------------------------------- PCM-P10-2

    [Theory]
    [Trait("Id", "PCM-P10-2")]
    [InlineData(Verb.Deliver, InteractMode.Hold, 400, (byte)2)]
    [InlineData(Verb.Open, InteractMode.Tap, 0, (byte)1)]
    [InlineData(Verb.PickUp, InteractMode.Tap, 0, (byte)1)]
    [InlineData(Verb.Mount, InteractMode.Tap, 0, (byte)1)]
    [InlineData(Verb.Operate, InteractMode.Tap, 0, (byte)0)]
    [InlineData(Verb.Configure, InteractMode.Tap, 0, (byte)0)]
    [InlineData(Verb.Shop, InteractMode.Tap, 0, (byte)0)]
    [InlineData(Verb.Announce, InteractMode.Tap, 0, (byte)0)]
    [InlineData(Verb.Dismount, InteractMode.Tap, 0, (byte)0)]
    [InlineData(Verb.Catch, InteractMode.Channel, 0, (byte)0)]
    [InlineData(Verb.Push, InteractMode.Channel, 0, (byte)0)]
    [InlineData(Verb.Rebuild, InteractMode.Hold, 400, (byte)0)]
    [InlineData(Verb.Deconstruct, InteractMode.Hold, 400, (byte)0)]
    public void VerbCatalogue_DefaultsMatchThePcmP10_2Table_AndAnchorDefForAppliesThem(Verb verb, InteractMode mode, int holdBaseMs, byte priority)
    {
        // Act
        AnchorDef def = AnchorDef.For(Ref(1), new Vec3Mm(10, 20, 30), verb);

        // Assert
        Assert.True(VerbCatalogue.IsDefined(verb));
        Assert.Equal(mode, VerbCatalogue.DefaultMode(verb));
        Assert.Equal(holdBaseMs, VerbCatalogue.DefaultHoldBaseMs(verb));
        Assert.Equal(priority, VerbCatalogue.DefaultPriority(verb));
        Assert.Equal((verb, mode, holdBaseMs, priority), (def.Verb, def.Mode, def.HoldBaseMs, def.Priority));
        Assert.Equal(0, def.RangeMm);
        Assert.Equal(new Vec3Mm(10, 20, 30), def.Point);
        Assert.Equal(Ref(1), def.Ref);
    }

    [Fact]
    [Trait("Id", "PCM-P10-2")]
    public void AllowWhileSwimming_DefaultsOff_AndIsHonouredWhenSetExplicitly()
    {
        // Arrange
        AnchorRegistry registry = NewRegistry(out _, out _);
        foreach (Verb verb in Enum.GetValues<Verb>())
        {
            Assert.Equal(AnchorFlags.None, AnchorDef.For(Ref(1), default, verb).Flags);
        }

        AnchorDef open = Def(1, 3000, 3000, Verb.Open);
        open.Flags = AnchorFlags.AllowWhileSwimming;
        AnchorDef plain = Def(2, 3000, 3000, Verb.PickUp);

        // Act
        Assert.Equal(RegisterResult.Ok, registry.Register(in open, new FakeInteractable()));
        Assert.Equal(RegisterResult.Ok, registry.Register(in plain, new FakeInteractable()));

        // Assert
        Assert.True(registry.TryGet(Ref(1), out int i1));
        Assert.True(registry.TryGet(Ref(2), out int i2));
        Assert.Equal(AnchorFlags.AllowWhileSwimming, registry.DefAt(i1).Flags);
        Assert.Equal(AnchorFlags.None, registry.DefAt(i2).Flags);
    }

    // ---------------------------------------------------------------- registration contract

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void Register_StoresEveryFieldVerbatim()
    {
        // Arrange
        AnchorRegistry registry = NewRegistry(out _, out _);
        var def = new AnchorDef
        {
            Ref = Ref(11, 4),
            Point = new Vec3Mm(12_345, -678, 90_123),
            Verb = Verb.Rebuild,
            Mode = InteractMode.Hold,
            HoldBaseMs = 900,
            RangeMm = 1800,
            Priority = 3,
            Flags = AnchorFlags.AllowWhileSwimming | AnchorFlags.SeatOf,
        };
        var impl = new FakeInteractable();

        // Act
        RegisterResult r = registry.Register(in def, impl);

        // Assert
        Assert.Equal(RegisterResult.Ok, r);
        Assert.True(registry.TryGet(Ref(11, 4), out int index));
        ref readonly AnchorDef stored = ref registry.DefAt(index);
        Assert.Equal(def.Ref, stored.Ref);
        Assert.Equal(def.Point, stored.Point);
        Assert.Equal((def.Verb, def.Mode, def.HoldBaseMs, def.RangeMm, def.Priority, def.Flags),
            (stored.Verb, stored.Mode, stored.HoldBaseMs, stored.RangeMm, stored.Priority, stored.Flags));
        Assert.Same(impl, registry.ImplAt(index));
        Assert.Equal(AnchorRegistry.ContextWorld, registry.ContextMaskAt(index));
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void Unregister_FreesTheSlot_AndTheSameRefCanBeRegisteredAgain()
    {
        // Arrange
        AnchorRegistry registry = NewRegistry(out _, out _, max: 2);
        AnchorDef a = Def(1, 1000, 1000);
        AnchorDef b = Def(2, 2000, 2000);
        AnchorDef c = Def(3, 3000, 3000);
        Assert.Equal(RegisterResult.Ok, registry.Register(in a, new FakeInteractable()));
        Assert.Equal(RegisterResult.Ok, registry.Register(in b, new FakeInteractable()));
        Assert.Equal(RegisterResult.Full, registry.Register(in c, new FakeInteractable()));

        // Act
        RegisterResult removed = registry.Unregister(Ref(1));
        RegisterResult again = registry.Register(in a, new FakeInteractable());
        RegisterResult third = registry.Register(in c, new FakeInteractable());

        // Assert
        Assert.Equal(RegisterResult.Ok, removed);
        Assert.Equal(RegisterResult.Ok, again);
        Assert.Equal(RegisterResult.Full, third);
        Assert.Equal(2, registry.Count);
        Assert.True(registry.TryGet(Ref(1), out _));
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void Move_ReBucketsTheAnchor_TheQueryFindsItAtTheNewPointAndNotTheOld()
    {
        // Arrange: well apart so the 2-cell radius (2500 + 400 slack = 2 cells) cannot see both.
        AnchorRegistry registry = NewRegistry(out _, out _);
        var oldPoint = new Vec3Mm(10_000, 0, 10_000);
        var newPoint = new Vec3Mm(200_000, 0, 100_000);
        AnchorDef def = Def(1, 10_000, 10_000);
        Assert.Equal(RegisterResult.Ok, registry.Register(in def, new FakeInteractable()));
        var buffer = new int[16];

        // Act
        RegisterResult moved = registry.Move(Ref(1), in newPoint);

        // Assert
        Assert.Equal(RegisterResult.Ok, moved);
        Assert.Equal(0, registry.QueryCells(in oldPoint, buffer));
        Assert.Equal(1, registry.QueryCells(in newPoint, buffer));
        Assert.Equal(newPoint, registry.DefAt(buffer[0]).Point);
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void Register_InvalidArguments_AreRejected_AndChangeNothing()
    {
        // Arrange
        AnchorRegistry registry = NewRegistry(out SimCheckRing checks, out SimCounters counters);
        AnchorDef ok = Def(1, 1000, 1000);
        AnchorDef p4 = ok;
        p4.Priority = 4;
        AnchorDef negRange = ok;
        negRange.RangeMm = -1;
        AnchorDef negHold = ok;
        negHold.HoldBaseMs = -1;
        AnchorDef mode0 = ok;
        mode0.Mode = default;
        AnchorDef mode4 = ok;
        mode4.Mode = (InteractMode)4;
        AnchorDef flags = ok;
        flags.Flags = (AnchorFlags)8;
        AnchorDef none = ok;
        none.Ref = AnchorRef.None;

        // Act + Assert
        Assert.Equal(RegisterResult.InvalidArgument, registry.Register(in ok, null!));
        Assert.Equal(RegisterResult.InvalidArgument, registry.Register(in p4, new FakeInteractable()));
        Assert.Equal(RegisterResult.InvalidArgument, registry.Register(in negRange, new FakeInteractable()));
        Assert.Equal(RegisterResult.InvalidArgument, registry.Register(in negHold, new FakeInteractable()));
        Assert.Equal(RegisterResult.InvalidArgument, registry.Register(in mode0, new FakeInteractable()));
        Assert.Equal(RegisterResult.InvalidArgument, registry.Register(in mode4, new FakeInteractable()));
        Assert.Equal(RegisterResult.InvalidArgument, registry.Register(in flags, new FakeInteractable()));
        Assert.Equal(RegisterResult.InvalidArgument, registry.Register(in none, new FakeInteractable()));
        Assert.Equal(0, registry.Count);
        Assert.Equal(0, checks.Count);
        Assert.Equal(0, counters[CounterId.AnchorRegistryFull]);

        // A priority of 3 is the largest legal value.
        AnchorDef p3 = ok;
        p3.Priority = 3;
        Assert.Equal(RegisterResult.Ok, registry.Register(in p3, new FakeInteractable()));
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void UnregisterAndMove_OfAnUnknownRef_ReturnUnknownAnchor()
    {
        // Arrange
        AnchorRegistry registry = NewRegistry(out _, out _);
        var p = new Vec3Mm(1, 2, 3);

        // Act + Assert
        Assert.Equal(RegisterResult.UnknownAnchor, registry.Unregister(Ref(99)));
        Assert.Equal(RegisterResult.UnknownAnchor, registry.Move(Ref(99), in p));
        AnchorDef def = Def(1, 1000, 1000);
        Assert.Equal(RegisterResult.Ok, registry.Register(in def, new FakeInteractable()));
        Assert.Equal(RegisterResult.UnknownAnchor, registry.Unregister(Ref(1, 1)));   // same entity, other slot
        Assert.Equal(RegisterResult.Ok, registry.Unregister(Ref(1)));
        Assert.Equal(RegisterResult.UnknownAnchor, registry.Unregister(Ref(1)));
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void AnchorRef_OrdersByEntityThenSlot_AndNoneIsDefault()
    {
        // Assert
        Assert.True(Ref(1, 5).CompareTo(Ref(2, 0)) < 0);
        Assert.True(Ref(2, 1).CompareTo(Ref(2, 0)) > 0);
        Assert.Equal(0, Ref(2, 1).CompareTo(Ref(2, 1)));
        Assert.True(AnchorRef.None.IsNone);
        Assert.False(Ref(1).IsNone);
        Assert.Equal(default, AnchorRef.None);
    }

    // ---------------------------------------------------------------- capacity

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void Register_The4097thWithMax4096_IsFull_CountsIt_AndLeavesTheRegistryUnchanged()
    {
        // Arrange
        AnchorRegistry registry = NewRegistry(out _, out SimCounters counters);
        for (uint i = 0; i < 4096; i++)
        {
            AnchorDef d = Def(i + 1, (int)(i % 128 * CellMm), (int)(i / 128 % 64 * CellMm));
            Assert.Equal(RegisterResult.Ok, registry.Register(in d, new FakeInteractable()));
        }

        // Act
        AnchorDef extra = Def(5000, 100, 100);
        RegisterResult r = registry.Register(in extra, new FakeInteractable());

        // Assert
        Assert.Equal(RegisterResult.Full, r);
        Assert.Equal(1, counters[CounterId.AnchorRegistryFull]);
        Assert.Equal(4096, registry.Count);
        Assert.False(registry.TryGet(Ref(5000), out _));
        Assert.True(registry.TryGet(Ref(4096), out _));
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void Register_The9thWithMax8_IsFull()
    {
        // Arrange
        AnchorRegistry registry = NewRegistry(out _, out SimCounters counters, max: 8);
        for (uint i = 0; i < 8; i++)
        {
            AnchorDef d = Def(i + 1, (int)(i * CellMm), 0);
            Assert.Equal(RegisterResult.Ok, registry.Register(in d, new FakeInteractable()));
        }

        // Act
        AnchorDef ninth = Def(9, 0, 0);
        RegisterResult r = registry.Register(in ninth, new FakeInteractable());

        // Assert
        Assert.Equal(RegisterResult.Full, r);
        Assert.Equal(1, counters[CounterId.AnchorRegistryFull]);
        Assert.Equal(8, registry.Count);
        Assert.Equal(8, registry.Capacity);
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void PlayersSystem_SizesTheRegistryFromPlayerSettings_AndFullCountsOnTheWorldCounters()
    {
        // Arrange
        var checks = new SimCheckRing();
        var counters = new SimCounters();
        PlayerSettings eight = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player) with { MaxAnchors = 8 };
        var players = new PlayersSystem(WorldTablesFixture.Pad(), eight, checks, counters);

        // Act
        for (uint i = 0; i < 8; i++)
        {
            AnchorDef d = Def(i + 1, (int)(i * CellMm), 0);
            Assert.Equal(RegisterResult.Ok, players.RegisterAnchor(in d, new FakeInteractable()));
        }

        AnchorDef ninth = Def(9, 0, 0);
        RegisterResult r = players.RegisterAnchor(in ninth, new FakeInteractable());

        // Assert
        Assert.Equal(RegisterResult.Full, r);
        Assert.Equal(1, counters[CounterId.AnchorRegistryFull]);
        Assert.Equal(8, players.AnchorCount);
        Assert.Equal(4096, PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player).MaxAnchors);
        Assert.Equal(4096, new PlayersSystem(WorldTablesFixture.Pad(), BuiltInContent.CreateM1a().Player).Anchors.Capacity);
    }

    // ---------------------------------------------------------------- spatial index

    private static int TestCellX(long x) => (int)Math.Clamp(x / CellMm, 0, 127);

    private static int TestCellZ(long z) => (int)Math.Clamp(z / CellMm, 0, 63);

    private static (int X, int Z)[] RandomPoints(uint seed, int count, bool someOutside)
    {
        var rng = new Pcg32(seed, 54UL);
        var points = new (int X, int Z)[count];
        for (int i = 0; i < count; i++)
        {
            int x = (int)(rng.Next32() % 256_000u);
            int z = (int)(rng.Next32() % 128_000u);
            if (someOutside && i % 40 == 0)
            {
                x = (int)(rng.Next32() % 300_000u) - 20_000;   // -20 000 to 280 000: outside on both ends
                z = (int)(rng.Next32() % 160_000u) - 16_000;
            }

            points[i] = (x, z);
        }

        return points;
    }

    private static AnchorRegistry Populate(AnchorRegistry registry, (int X, int Z)[] points, int[] order)
    {
        foreach (int i in order)
        {
            AnchorDef d = Def((uint)i + 1, points[i].X, points[i].Z);
            d.RangeMm = i % 7 == 0 ? 4800 : 0;   // a few long-reach anchors raise the query radius
            Assert.Equal(RegisterResult.Ok, registry.Register(in d, new FakeInteractable()));
        }

        return registry;
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void QueryCells_ReturnsExactlyTheAnchorsInsideTheCellSquare_ComparedWithABruteForceScan()
    {
        // Arrange: 2 000 seeded anchors, a few outside the bounds (clamped into the edge cells).
        (int X, int Z)[] points = RandomPoints(0xA11C_0001u, 2000, someOutside: true);
        AnchorRegistry registry = Populate(NewRegistry(out _, out _), points, Enumerable.Range(0, 2000).ToArray());
        Assert.Equal((128, 64), (registry.CellsX, registry.CellsZ));
        int radius = (4800 + 400 + CellMm - 1) / CellMm;
        Assert.Equal(radius, registry.QueryRadiusCells);
        (int X, int Z)[] queries = RandomPoints(0xA11C_0002u, 50, someOutside: true);
        var buffer = new int[4096];

        foreach ((int qx, int qz) in queries)
        {
            // Act
            int n = registry.QueryCells(new Vec3Mm(qx, 0, qz), buffer);
            int[] actual = buffer.AsSpan(0, n).ToArray().OrderBy(i => i).ToArray();

            // Assert: the set equals a scan over every registered anchor, using the test's own cell arithmetic.
            int cx = TestCellX(qx);
            int cz = TestCellZ(qz);
            var expected = new List<int>();
            for (int i = 0; i < registry.Count; i++)
            {
                (int X, int Z) p = points[(int)(registry.DefAt(i).Ref.Entity.Value - 0x0100_0001u)];
                if (Math.Abs(TestCellX(p.X) - cx) <= radius && Math.Abs(TestCellZ(p.Z) - cz) <= radius)
                {
                    expected.Add(i);
                }
            }

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void QueryCells_ResultSetDoesNotDependOnInsertionOrder()
    {
        // Arrange: the same 600 anchors registered in ascending and in a seeded shuffled order.
        (int X, int Z)[] points = RandomPoints(0xA11C_0003u, 600, someOutside: true);
        int[] ascending = Enumerable.Range(0, 600).ToArray();
        int[] shuffled = Enumerable.Range(0, 600).ToArray();
        var rng = new Pcg32(0xA11C_0004UL, 54UL);
        for (int i = shuffled.Length - 1; i > 0; i--)
        {
            int j = (int)(rng.Next32() % (uint)(i + 1));
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        AnchorRegistry a = Populate(NewRegistry(out _, out _), points, ascending);
        AnchorRegistry b = Populate(NewRegistry(out _, out _), points, shuffled);
        var bufferA = new int[4096];
        var bufferB = new int[4096];

        foreach ((int qx, int qz) in RandomPoints(0xA11C_0005u, 30, someOutside: true))
        {
            // Act
            int na = a.QueryCells(new Vec3Mm(qx, 0, qz), bufferA);
            int nb = b.QueryCells(new Vec3Mm(qx, 0, qz), bufferB);
            AnchorRef[] refsA = bufferA.AsSpan(0, na).ToArray().Select(i => a.DefAt(i).Ref).OrderBy(r => r).ToArray();
            AnchorRef[] refsB = bufferB.AsSpan(0, nb).ToArray().Select(i => b.DefAt(i).Ref).OrderBy(r => r).ToArray();

            // Assert
            Assert.Equal(refsA, refsB);
        }
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void QueryCells_IteratesAscendingCells_ThenAscendingIndexWithinACell_WithoutSorting()
    {
        // Arrange: dense cluster so many cells and several anchors per cell are returned; re-registrations reuse low slots.
        AnchorRegistry registry = NewRegistry(out _, out _);
        var rng = new Pcg32(0xA11C_0006UL, 54UL);
        for (uint i = 0; i < 400; i++)
        {
            AnchorDef d = Def(i + 1, 40_000 + (int)(rng.Next32() % 8000u), 40_000 + (int)(rng.Next32() % 8000u));
            Assert.Equal(RegisterResult.Ok, registry.Register(in d, new FakeInteractable()));
        }

        for (uint i = 1; i <= 400; i += 3)
        {
            Assert.Equal(RegisterResult.Ok, registry.Unregister(Ref(i)));
        }

        for (uint i = 1; i <= 100; i += 3)
        {
            AnchorDef d = Def(1000 + i, 40_000 + (int)(rng.Next32() % 8000u), 40_000 + (int)(rng.Next32() % 8000u));
            Assert.Equal(RegisterResult.Ok, registry.Register(in d, new FakeInteractable()));
        }

        var buffer = new int[4096];

        // Act
        int n = registry.QueryCells(new Vec3Mm(44_000, 0, 44_000), buffer);

        // Assert
        Assert.True(n > 100);
        for (int k = 1; k < n; k++)
        {
            int previousCell = registry.CellIndexOf(registry.DefAt(buffer[k - 1]).Point);
            int cell = registry.CellIndexOf(registry.DefAt(buffer[k]).Point);
            Assert.True(previousCell <= cell, $"result {k}: cell {cell} follows cell {previousCell}");
            if (previousCell == cell)
            {
                Assert.True(buffer[k - 1] < buffer[k], "within a cell the anchor index ascends");
            }
        }
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void QueryCells_PointsOutsideTheBounds_ClampIntoTheEdgeCells()
    {
        // Arrange
        AnchorRegistry registry = NewRegistry(out _, out _);
        AnchorDef low = Def(1, -50_000, -50_000);
        AnchorDef high = Def(2, 1_000_000, 900_000);
        Assert.Equal(RegisterResult.Ok, registry.Register(in low, new FakeInteractable()));
        Assert.Equal(RegisterResult.Ok, registry.Register(in high, new FakeInteractable()));
        var buffer = new int[8];

        // Act + Assert
        Assert.Equal(0, registry.CellIndexOf(low.Point));
        Assert.Equal((128 * 64) - 1, registry.CellIndexOf(high.Point));
        Assert.Equal(new Vec3Mm(-50_000, 0, -50_000), registry.DefAt(0).Point);   // the stored point is verbatim
        Assert.Equal(1, registry.QueryCells(new Vec3Mm(0, 0, 0), buffer));
        Assert.Equal(1, registry.QueryCells(new Vec3Mm(256_000, 0, 128_000), buffer));
        Assert.Equal(0, registry.QueryCells(new Vec3Mm(128_000, 0, 64_000), buffer));
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void QueryCells_WithAShortResultSpan_KeepsTheFirstAndCountsTheOverflowOnce()
    {
        // Arrange: 20 anchors in one cell.
        AnchorRegistry registry = NewRegistry(out _, out SimCounters counters);
        for (uint i = 0; i < 20; i++)
        {
            AnchorDef d = Def(i + 1, 5000, 5000);
            Assert.Equal(RegisterResult.Ok, registry.Register(in d, new FakeInteractable()));
        }

        var buffer = new int[5];
        var big = new int[64];

        // Act
        int n = registry.QueryCells(new Vec3Mm(5000, 0, 5000), buffer);
        int all = registry.QueryCells(new Vec3Mm(5000, 0, 5000), big);

        // Assert
        Assert.Equal(5, n);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, buffer);
        Assert.Equal(20, all);
        Assert.Equal(1, counters[CounterId.AnchorQueryOverflow]);
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void Grid_DimensionsAreCeilDivOfTheBoundsBy2000()
    {
        // Arrange
        var odd = new AnchorRegistry(new MapBounds(-1000, -3000, 4001, 1), 4, 2500, 400, new SimCheckRing(), new SimCounters());
        var tiny = new AnchorRegistry(new MapBounds(0, 0, 0, 0), 4, 2500, 400, new SimCheckRing(), new SimCounters());

        // Assert
        Assert.Equal((3, 2), (odd.CellsX, odd.CellsZ));   // 5001 / 2000 -> 3, 3001 / 2000 -> 2
        Assert.Equal((1, 1), (tiny.CellsX, tiny.CellsZ));
        Assert.Equal(1, new AnchorRegistry(_street, 4, 2500, 400, new SimCheckRing(), new SimCounters()).QueryRadiusCells);   // CeilDiv(0 + 400, 2000)
    }

    // ---------------------------------------------------------------- hook purity

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void IInteractable_EveryParameterIsInRefOutOrAValueType_NeverAClass()
    {
        // Arrange
        MethodInfo[] methods = typeof(IInteractable).GetMethods();

        // Assert
        Assert.Equal(
            new[] { "GetHoldTicks", "IsAvailable", "OnPress", "ShouldCancel", "TryBuildRequest", "WritePrompt" },
            methods.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        foreach (MethodInfo m in methods)
        {
            foreach (ParameterInfo p in m.GetParameters())
            {
                Type t = p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType;
                Assert.True(t.IsValueType, $"{m.Name}({p.Name}: {p.ParameterType}) must be an in/ref/out value or a value type");
                Assert.True(p.ParameterType.IsByRef || p.ParameterType.IsValueType, $"{m.Name}({p.Name})");
            }

            Assert.True(m.ReturnType.IsValueType || m.ReturnType == typeof(void), $"{m.Name} returns {m.ReturnType}");
        }
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void InteractionContext_HoldsOnlyValueFields_AndNoBool()
    {
        foreach (Type t in new[] { typeof(InteractionContext), typeof(InteractionLatch), typeof(AnchorDef), typeof(AnchorRef) })
        {
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.True(f.FieldType.IsValueType, $"{t.Name}.{f.Name} is a reference type");
                Assert.NotEqual(typeof(bool), f.FieldType);
            }
        }
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void PureHookGuard_CatchesAMutatingDouble_AndPassesThePureOne()
    {
        // Arrange
        var box = new Box();
        var pure = new PureHookGuard(new FakeInteractable(), () => box.Value);
        var mutating = new PureHookGuard(new MutatingFake(box), () => box.Value);
        var context = new InteractionContext { Tick = 1 };
        var latch = default(InteractionLatch);
        var args = default(PromptArgs);
        var op = default(Op);

        // Act: every hook on the pure double runs clean.
        _ = pure.IsAvailable(in context, out _);
        _ = pure.GetHoldTicks(in latch, in context);
        _ = pure.ShouldCancel(in latch, in context, out _);
        pure.WritePrompt(in context, ref args);
        _ = pure.TryBuildRequest(in latch, in context, ref op);
        pure.OnPress(in context, ref latch);

        // Assert: the mutating double bumps the captured counter inside IsAvailable and the guard throws.
        Assert.Equal(0, box.Value);
        var ex = Assert.Throws<InvalidOperationException>(() => mutating.IsAvailable(in context, out _));
        Assert.Contains(nameof(IInteractable.IsAvailable), ex.Message);
        Assert.Equal(1, box.Value);
    }

    // ---------------------------------------------------------------- default hold length

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void GetHoldTicks_WithoutAnOverride_IsTheSharedHoldEff_12TicksFor400Over400()
    {
        // Arrange
        HoldSettings settings = DefaultHoldSettings();
        var context = new InteractionContext { HoldBaseMs = 400, HoldSettingMs = 400, HoldSettings = settings };
        var latch = default(InteractionLatch);
        IInteractable impl = new FakeInteractable();   // does not override GetHoldTicks

        // Act
        int ticks = impl.GetHoldTicks(in latch, in context);

        // Assert
        Assert.Equal(Hold.HoldTicks(400, 0, 400, in settings), ticks);
        Assert.Equal(12, ticks);
        var slow = new InteractionContext { HoldBaseMs = 400, HoldSettingMs = 800, HoldSettings = settings };
        Assert.Equal(24, impl.GetHoldTicks(in latch, in slow));
    }

    // ---------------------------------------------------------------- layout

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void DeclaredStructs_HaveTheDocumentedSizes()
    {
        Assert.Equal(12, Unsafe.SizeOf<ClaimedPosition>());
        Assert.Equal(ClaimedPosition.SizeBytes, Unsafe.SizeOf<ClaimedPosition>());
        Assert.Equal(32, Unsafe.SizeOf<PromptArgs>());
        Assert.Equal(16, Unsafe.SizeOf<OwnerPayload16>());
        Assert.Equal(8, Unsafe.SizeOf<AnchorRef>());
        Assert.Equal(48, Unsafe.SizeOf<InteractionLatch>());   // 8 + 1 + 1 (+2) + 4 + 4 + 12 + 16
        Assert.Equal(36, Unsafe.SizeOf<AnchorDef>());          // 8 + 12 + 1 + 1 (+2) + 4 + 4 + 1 + 1 (+2)
    }

    // ---------------------------------------------------------------- dump (through the composed world)

    private static byte[] AnchorTail(params (AnchorDef Def, int Range)[] anchors)
    {
        var output = new ArrayBufferWriter<byte>();
        var w = new CanonicalWriter(output, new CanonicalBuffer());
        w.BeginSection(1);
        w.WriteU32(0);   // no players joined
        w.WriteU32((uint)anchors.Length);
        foreach ((AnchorDef d, int range) in anchors)
        {
            w.WriteU32(d.Ref.Entity.Value);
            w.WriteU8(d.Ref.Slot);
            w.WriteI32(d.Point.X);
            w.WriteI32(d.Point.Y);
            w.WriteI32(d.Point.Z);
            w.WriteU8((byte)d.Verb);
            w.WriteU8((byte)d.Mode);
            w.WriteI32(d.HoldBaseMs);
            w.WriteI32(range);
            w.WriteU8(d.Priority);
            w.WriteU8((byte)d.Flags);
        }

        w.EndSection();
        return output.WrittenSpan[6..].ToArray();
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void Dump_ThroughTheComposedWorld_WritesTheAnchorsInIndexOrder_FieldByField()
    {
        // Arrange
        PlayersRig rig = PlayersRig.Create();
        Assert.Equal(4096, rig.Players.Anchors.Capacity);
        AnchorDef a = Def(30, 4000, 5000, Verb.Deliver);
        AnchorDef b = Def(10, 6000, 7000, Verb.Open, slot: 2);
        b.Flags = AnchorFlags.AllowWhileSwimming;
        b.RangeMm = 3000;
        AnchorDef c = Def(20, 8000, 9000, Verb.Catch);
        c.Flags = AnchorFlags.BuildModeOnly;

        // Act
        foreach (AnchorDef d in new[] { a, b, c })
        {
            Assert.Equal(RegisterResult.Ok, rig.Players.RegisterAnchor(in d, new FakeInteractable()));
        }

        byte[] section = SectionOf(DumpHash.Bytes(rig.Sim), SectionIds.Players);

        // Assert: registration order (not entity order), the resolved range, no object reference.
        Assert.Equal(3, rig.Players.AnchorCount);
        Assert.Equal(AnchorTail((a, 2500), (b, 3000), (c, 2500)), section);
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void Dump_ChangesWhenAnAnchorIsMovedOrRemoved_AndReturnsWhenRestored()
    {
        // Arrange
        PlayersRig rig = PlayersRig.Create();
        AnchorDef a = Def(1, 4000, 5000);
        AnchorDef b = Def(2, 6000, 7000);
        Assert.Equal(RegisterResult.Ok, rig.Players.RegisterAnchor(in a, new FakeInteractable()));
        Assert.Equal(RegisterResult.Ok, rig.Players.RegisterAnchor(in b, new FakeInteractable()));
        byte[] baseline = DumpHash.Bytes(rig.Sim);

        // Act
        Assert.Equal(RegisterResult.Ok, rig.Players.MoveAnchor(Ref(2), new Vec3Mm(6001, 0, 7000)));
        byte[] moved = DumpHash.Bytes(rig.Sim);
        Assert.Equal(RegisterResult.Ok, rig.Players.MoveAnchor(Ref(2), new Vec3Mm(6000, 0, 7000)));
        byte[] restored = DumpHash.Bytes(rig.Sim);
        Assert.Equal(RegisterResult.Ok, rig.Players.UnregisterAnchor(Ref(1)));
        byte[] removed = DumpHash.Bytes(rig.Sim);

        // Assert
        Assert.NotEqual(baseline, moved);
        Assert.Equal(baseline, restored);
        Assert.NotEqual(baseline, removed);
        Assert.Equal(AnchorTail((b, 2500)), SectionOf(removed, SectionIds.Players));
    }
}

/// <summary>Registry queries, edits and the dump allocate nothing once built (TD-008: optimised builds only).</summary>
[Collection(NonParallelCollection.Name)]
public sealed class AnchorRegistryAllocationTests
{
    /// <summary>2 000 registered anchors: 100 queries, a move, an unregister and re-register, and the dump write allocate 0 B.</summary>
    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void TwoThousandAnchors_QueriesEditsAndTheDump_AllocateZeroBytes()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(AnchorRegistry).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        // Arrange
        var registry = new AnchorRegistry(new MapBounds(0, 0, 256_000, 128_000), 4096, 2500, 400, new SimCheckRing(), new SimCounters());
        var impl = new NullInteractable();
        var rng = new Pcg32(0xA11C_00A1UL, 54UL);
        for (uint i = 0; i < 2000; i++)
        {
            AnchorDef d = AnchorDef.For(
                new AnchorRef(new EntityId(0x0100_0000u + i + 1), 0),
                new Vec3Mm((int)(rng.Next32() % 256_000u), 0, (int)(rng.Next32() % 128_000u)),
                Verb.Open);
            Assert.Equal(RegisterResult.Ok, registry.Register(in d, impl));
        }

        var results = new int[4096];
        var output = new ArrayBufferWriter<byte>(1 << 20);
        var scratch = new CanonicalBuffer();
        AnchorDef again = AnchorDef.For(new AnchorRef(new EntityId(0x0100_0001u), 0), new Vec3Mm(1000, 0, 1000), Verb.Open);
        long sink = 0;

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("100 queries + edit cycle + dump", () =>
        {
            for (int i = 0; i < 100; i++)
            {
                sink += registry.QueryCells(new Vec3Mm((i * 2411) % 256_000, 0, (i * 1327) % 128_000), results);
            }

            _ = registry.Move(again.Ref, new Vec3Mm(5000, 0, 5000));
            _ = registry.Unregister(again.Ref);
            _ = registry.Register(in again, impl);

            output.ResetWrittenCount();
            var w = new CanonicalWriter(output, scratch);
            w.BeginSection(1);
            registry.Dump(ref w);
            w.EndSection();
        });

        Assert.NotEqual(0, sink);
    }

    private sealed class NullInteractable : IInteractable
    {
        public bool IsAvailable(in InteractionContext c, out StringId reason)
        {
            reason = StringId.None;
            return true;
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
}
