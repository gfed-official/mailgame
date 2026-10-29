using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 019: <see cref="IPlayersApi.CheckInteraction"/> (ADR-0014 section 5, GDD F5/P13) and
/// <see cref="IPlayersApi.LeashExceeded"/> on a composed world. The player joins slot 0, so its host position is seeded at the pad
/// spawn point; claims and anchors are placed relative to it. Distances are the GDD's numbers (range 2500 + slack 500, tolerance 1500,
/// chest 1000, stale after 30 ticks, leash 3000).
/// </summary>
public sealed class CheckInteractionTests
{
    private const int ChestMm = 1000;
    private const int LeashMm = 3000;
    private static readonly StringId _ownerReason = new(StringNamespace.Reason, 40);

    private static PlayersRig Joined()
    {
        var rig = PlayersRig.Create();
        rig.TickEmpty(1);
        rig.JoinAndTick(0);
        return rig;
    }

    private static AnchorRef Register(PlayersRig rig, Vec3Mm point, Verb verb = Verb.Open, AnchorFlags flags = AnchorFlags.None, IInteractable? impl = null)
    {
        var anchor = new AnchorRef(new EntityId(16_777_400u + (uint)rig.Players.AnchorCount), 0);
        AnchorDef def = AnchorDef.For(anchor, point, verb);
        def.Flags = flags;
        Assert.Equal(RegisterResult.Ok, rig.Players.RegisterAnchor(in def, impl ?? new CountingInteractable()));
        return anchor;
    }

    private static ClaimedPosition Claim(Vec3Mm feet) => new(feet.X, feet.Y, feet.Z);

    private static Vec3Mm Offset(Vec3Mm p, int dx, int dy = 0, int dz = 0) => new(p.X + dx, p.Y + dy, p.Z + dz);

    // ---------------------------------------------------------------- PCM-P13-1: the first failure wins; read-only

    [Theory]
    [Trait("Id", "PCM-P13-1")]
    [InlineData(LifeState.Dead, false, true, 1u, 0, InteractionCheckKind.Dead)]               // dead + unknown anchor
    [InlineData(LifeState.Frozen, true, false, 1u, 0, InteractionCheckKind.Frozen)]           // frozen + unavailable
    [InlineData(LifeState.Alive, false, true, 1u, 10_000, InteractionCheckKind.NoTarget)]     // unknown anchor + out of range
    [InlineData(LifeState.Alive, true, false, 31u, 0, InteractionCheckKind.Unavailable)]      // unavailable + stale
    [InlineData(LifeState.Alive, true, true, 31u, 10_000, InteractionCheckKind.PositionStale)] // stale + out of range
    [InlineData(LifeState.Alive, true, true, 1u, 0, InteractionCheckKind.Ok)]                 // the same fixture with no failure
    public void CheckInteraction_TwoFailures_ReportsTheFirstInChainOrder(
        LifeState life, bool registered, bool available, uint age, int claimDx, InteractionCheckKind expected)
    {
        // Arrange: the anchor sits at the spawn chest point; an unregistered ref is a fresh id.
        PlayersRig rig = Joined();
        Vec3Mm spawn = rig.Spawn(0);
        var owner = new CountingInteractable { Available = available, UnavailableReason = _ownerReason };
        AnchorRef anchor = registered
            ? Register(rig, Offset(spawn, 0, ChestMm), impl: owner)
            : new AnchorRef(new EntityId(16_777_999u), 0);
        rig.AgeTo(0, age);
        rig.Record(0).Life = life;

        // Act
        InteractionCheck r = rig.Players.CheckInteraction(rig.Actor(0), anchor, Claim(Offset(spawn, claimDx)));

        // Assert
        Assert.Equal(expected, r.Kind);
        Assert.Equal(expected == InteractionCheckKind.Unavailable ? _ownerReason : StringId.None, r.Reason);
    }

    [Fact]
    [Trait("Id", "PCM-P13-1")]
    public void CheckInteraction_BetweenTicks_OnEveryPath_LeavesTheDumpUnchanged()
    {
        // Arrange
        PlayersRig rig = Joined();
        Vec3Mm spawn = rig.Spawn(0);
        AnchorRef ok = Register(rig, Offset(spawn, 0, ChestMm));
        AnchorRef refused = Register(rig, Offset(spawn, 500, ChestMm), impl: new CountingInteractable { Available = false, UnavailableReason = _ownerReason });
        string before = DumpHash.Sha256(rig.Sim);

        // Act
        InteractionCheck a = rig.Players.CheckInteraction(rig.Actor(0), ok, Claim(spawn));
        InteractionCheck b = rig.Players.CheckInteraction(rig.Actor(0), ok, Claim(Offset(spawn, 9000)));
        InteractionCheck c = rig.Players.CheckInteraction(rig.Actor(0), refused, Claim(spawn));

        // Assert
        Assert.Equal(InteractionCheckKind.Ok, a.Kind);
        Assert.Equal(InteractionCheckKind.OutOfRange, b.Kind);
        Assert.Equal(InteractionCheckKind.Unavailable, c.Kind);
        Assert.Equal(before, DumpHash.Sha256(rig.Sim));
    }

    // ---------------------------------------------------------------- PCM-F5-1: range, tolerance, 3D chest, staleness

    /// <summary>
    /// The claim is <paramref name="claimDx"/> from the newest sample (the spawn); the anchor is (<paramref name="anchorDx"/>,
    /// <paramref name="anchorDy"/>) from the claim's chest point.
    /// </summary>
    [Theory]
    [Trait("Id", "PCM-F5-1")]
    [InlineData(Verb.Open, 3000, 0, 0, InteractionCheckKind.Ok)]            // 2500 + slack 500
    [InlineData(Verb.Open, 3001, 0, 0, InteractionCheckKind.OutOfRange)]
    [InlineData(Verb.Open, 0, 0, 1500, InteractionCheckKind.Ok)]            // claimToleranceMm from the newest sample
    [InlineData(Verb.Open, 0, 0, 1501, InteractionCheckKind.OutOfRange)]
    [InlineData(Verb.Open, 2291, 1000, 0, InteractionCheckKind.Ok)]         // 2000 above the feet, chest distance 2500 (3D)
    [InlineData(Verb.Open, 2900, 1000, 0, InteractionCheckKind.OutOfRange)] // 2900 flat, but 3067 in 3D
    [InlineData(Verb.Deliver, 3000, 0, 0, InteractionCheckKind.Ok)]         // single RangeMm 2500 (ADR-0014 section 5)
    [InlineData(Verb.Deliver, 3001, 0, 0, InteractionCheckKind.OutOfRange)]
    public void CheckInteraction_Distance_AcceptsWithinRangePlusSlackAndTolerance(Verb verb, int anchorDx, int anchorDy, int claimDx, InteractionCheckKind expected)
    {
        // Arrange
        PlayersRig rig = Joined();
        Vec3Mm claim = Offset(rig.Spawn(0), claimDx);
        AnchorRef anchor = Register(rig, Offset(claim, anchorDx, ChestMm + anchorDy), verb);

        // Act
        InteractionCheck r = rig.Players.CheckInteraction(rig.Actor(0), anchor, Claim(claim));

        // Assert
        Assert.Equal(expected, r.Kind);
    }

    /// <summary>Regression (story 019 review): a client claim with Y near <c>int.MaxValue</c> overflowed <c>claim.Y + chestMm</c> in SIM_ASSERT builds.</summary>
    [Fact]
    [Trait("Id", "PCM-F5-1")]
    public void CheckInteraction_ClaimYAtIntMax_IsOutOfRange_NotAnOverflow()
    {
        // Arrange
        PlayersRig rig = Joined();
        Vec3Mm spawn = rig.Spawn(0);
        AnchorRef anchor = Register(rig, Offset(spawn, 0, ChestMm));

        // Act
        InteractionCheck r = rig.Players.CheckInteraction(rig.Actor(0), anchor, new ClaimedPosition(spawn.X, int.MaxValue, spawn.Z));

        // Assert
        Assert.Equal(InteractionCheckKind.OutOfRange, r.Kind);
    }

    [Theory]
    [Trait("Id", "PCM-F5-1")]
    [InlineData(30u, InteractionCheckKind.Ok)]
    [InlineData(31u, InteractionCheckKind.PositionStale)]
    public void CheckInteraction_TicksSinceTheLastTransform_StaleAfter30(uint age, InteractionCheckKind expected)
    {
        // Arrange: a sample received, then silence.
        PlayersRig rig = Joined();
        Vec3Mm feet = Offset(rig.Spawn(0), 400);
        rig.Report(0, feet);
        rig.TickEmpty(1);
        AnchorRef anchor = Register(rig, Offset(feet, 0, ChestMm));
        rig.AgeTo(0, age);

        // Act
        InteractionCheck r = rig.Players.CheckInteraction(rig.Actor(0), anchor, Claim(feet));

        // Assert
        Assert.Equal(expected, r.Kind);
    }

    // ---------------------------------------------------------------- Leash (Grid Inventory H-R10-1)

    [Theory]
    [Trait("Id", "H-R10-1")]
    [InlineData(2990, 1u, false)]   // 2.99 m stays open
    [InlineData(3000, 1u, false)]   // strict: exactly the leash stays open
    [InlineData(3001, 1u, true)]
    [InlineData(3010, 1u, true)]    // 3.01 m closes
    [InlineData(9000, 31u, false)]  // a stale position never closes
    public void LeashExceeded_NewestChestDistance_IsStrictAndNeverOnAStalePosition(int distanceMm, uint age, bool expected)
    {
        // Arrange
        PlayersRig rig = Joined();
        Vec3Mm anchorPoint = Offset(rig.Spawn(0), distanceMm, ChestMm);
        rig.AgeTo(0, age);

        // Act
        bool exceeded = rig.Players.LeashExceeded(PlayerSlot.FromValue(0), in anchorPoint, LeashMm);

        // Assert
        Assert.Equal(expected, exceeded);
    }

    // ---------------------------------------------------------------- Unavailable on host state

    [Theory]
    [Trait("Id", "PC019-UNAV-1")]
    [InlineData(SampleFlags.Swimming, AnchorFlags.None, false)]
    [InlineData(SampleFlags.Swimming, AnchorFlags.AllowWhileSwimming, true)]
    [InlineData(SampleFlags.Sprinting, AnchorFlags.None, true)]
    public void CheckInteraction_StoredSwimmingBit_GatesAnchorsWithoutAllowWhileSwimming(SampleFlags sampleFlags, AnchorFlags anchorFlags, bool accepted)
    {
        // Arrange: the stored sample carries the flag; the owner would accept.
        PlayersRig rig = Joined();
        Vec3Mm feet = rig.Spawn(0);
        rig.Report(0, feet, sampleFlags);
        rig.TickEmpty(1);
        var owner = new CountingInteractable();
        AnchorRef anchor = Register(rig, Offset(feet, 0, ChestMm), flags: anchorFlags, impl: owner);

        // Act
        InteractionCheck r = rig.Players.CheckInteraction(rig.Actor(0), anchor, Claim(feet));

        // Assert: the gate runs first, so a gated anchor never reaches the owner.
        Assert.Equal(accepted ? InteractionCheck.Of(InteractionCheckKind.Ok) : InteractionCheck.Unavailable(PlayerReasons.DryLand), r);
        Assert.Equal(accepted ? 1 : 0, owner.AvailabilityCalls);
    }

    [Fact]
    [Trait("Id", "PC019-UNAV-1")]
    public void CheckInteraction_OwnerUnavailable_ReturnsItsReason_WithTheHostContext()
    {
        // Arrange
        PlayersRig rig = Joined();
        Vec3Mm feet = Offset(rig.Spawn(0), -300);
        rig.Report(0, feet);
        rig.TickEmpty(1);
        var owner = new CountingInteractable { Available = false, UnavailableReason = _ownerReason };
        AnchorRef anchor = Register(rig, Offset(feet, 1000, ChestMm), impl: owner);

        // Act
        InteractionCheck r = rig.Players.CheckInteraction(rig.Actor(0), anchor, Claim(feet));

        // Assert: host state only — chest of the newest sample, the content hold default, the registration's mask, not the host.
        Assert.Equal(InteractionCheck.Unavailable(_ownerReason), r);
        InteractionContext c = owner.LastContext;
        Assert.Equal(Offset(feet, 0, ChestMm), c.ChestMm);
        Assert.Equal(HoldSettings.DefaultHoldDefaultMs, c.HoldSettingMs);
        Assert.Equal(AnchorRegistry.ContextWorld, c.ContextMask);
        Assert.Equal(0, c.IsHost);
        Assert.Equal(PlayerSlot.FromValue(0), c.Actor);
        Assert.Equal(anchor, c.Anchor);
    }
}
