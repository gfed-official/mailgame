using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 013: the framework availability gate (PCM-P11-2d: filter order Dead, Mounted own seat, Build, Panel,
/// swim; Panel + swimming gives close_panel), the swimming restrictions (PCM-P6-2a/b/c), the two reason keys and the
/// context-change cancel reasons (ADR-0014 sections 2 and 4). Reason ids are made-up fixtures (OQ-013-2).
/// </summary>
public class InteractionGateTests
{
    internal static readonly StringId DryLandId = new(StringNamespace.Run, 901);
    internal static readonly StringId ClosePanelId = new(StringNamespace.Run, 902);

    private static readonly EntityId _vehicle = new(0x0100_0077u);
    private static readonly AnchorRef _ownSeat = new(_vehicle, 1);
    private static readonly AnchorRef _otherSeat = new(_vehicle, 2);

    private static InteractionGate Gate() => new(new FrameworkReasons(DryLandId, ClosePanelId));

    internal static AnchorDef Anchor(Verb verb, AnchorFlags flags = AnchorFlags.None, AnchorRef? at = null)
    {
        AnchorDef d = AnchorDef.For(at ?? new AnchorRef(new EntityId(0x0100_0001u), 0), new Vec3Mm(1000, 0, 1000), verb);
        d.Flags = flags;
        return d;
    }

    internal static CarrierStatus Status(
        BodyContext body = BodyContext.OnFoot,
        bool panel = false,
        bool swimming = false,
        AnchorRef seat = default,
        LifeState life = LifeState.Alive) =>
        new() { Life = life, Body = body, PanelOpen = panel, Swimming = swimming, OccupiedSeat = seat };

    // ---------------------------------------------------------------- PCM-P11-2d

    /// <summary>On foot, panel closed, dry: every anchor passes to the owner.</summary>
    [Theory]
    [Trait("Id", "PCM-P11-2d")]
    [InlineData(Verb.Deliver, AnchorFlags.None)]
    [InlineData(Verb.Open, AnchorFlags.AllowWhileSwimming)]
    [InlineData(Verb.Dismount, AnchorFlags.SeatOf)]
    public void OnFoot_Dry_PanelClosed_IsOk(Verb verb, AnchorFlags flags)
    {
        // Arrange
        AnchorDef a = Anchor(verb, flags);
        CarrierStatus s = Status();

        // Act
        GateResult r = Gate().Evaluate(in a, in s);

        // Assert
        Assert.Equal(GateResult.Ok, r);
    }

    /// <summary>Dead: no candidates, ahead of every later filter (panel open, swimming, own-seat Dismount, AllowWhileSwimming).</summary>
    [Theory]
    [Trait("Id", "PCM-P11-2d")]
    [InlineData(Verb.Deliver, AnchorFlags.None, false, false)]
    [InlineData(Verb.Open, AnchorFlags.AllowWhileSwimming, true, true)]
    [InlineData(Verb.Deconstruct, AnchorFlags.BuildModeOnly, true, false)]
    [InlineData(Verb.Dismount, AnchorFlags.SeatOf, false, true)]
    public void Dead_NoCandidates_BeforeEveryOtherFilter(Verb verb, AnchorFlags flags, bool panel, bool swimming)
    {
        // Arrange
        AnchorDef a = Anchor(verb, flags, _ownSeat);
        CarrierStatus s = Status(BodyContext.Dead, panel, swimming, _ownSeat, LifeState.Dead);

        // Act
        GateResult r = Gate().Evaluate(in a, in s);

        // Assert
        Assert.Equal(GateResult.NoCandidate, r);
    }

    /// <summary>Mounted: the own seat's SeatOf Dismount anchor is the one candidate.</summary>
    [Fact]
    [Trait("Id", "PCM-P11-2d")]
    public void Mounted_OwnSeatSeatOfDismount_IsCandidate()
    {
        // Arrange
        AnchorDef a = Anchor(Verb.Dismount, AnchorFlags.SeatOf, _ownSeat);
        CarrierStatus s = Status(BodyContext.Mounted, seat: _ownSeat);

        // Act
        GateResult r = Gate().Evaluate(in a, in s);

        // Assert
        Assert.Equal(GateResult.Ok, r);
    }

    /// <summary>Mounted fixtures that yield no candidate: another seat, a None seat, a non-Dismount SeatOf, a Dismount without SeatOf, any other anchor.</summary>
    [Theory]
    [Trait("Id", "PCM-P11-2d")]
    [InlineData("another seat's SeatOf Dismount on the same vehicle")]
    [InlineData("OccupiedSeat None")]
    [InlineData("SeatOf Mount with Ref == OccupiedSeat")]
    [InlineData("Dismount without SeatOf")]
    [InlineData("an ordinary Deliver anchor")]
    public void Mounted_AnythingButTheOwnSeatDismount_IsNoCandidate(string fixture)
    {
        // Arrange
        (AnchorDef a, AnchorRef seat) = fixture switch
        {
            "another seat's SeatOf Dismount on the same vehicle" => (Anchor(Verb.Dismount, AnchorFlags.SeatOf, _otherSeat), _ownSeat),
            "OccupiedSeat None" => (Anchor(Verb.Dismount, AnchorFlags.SeatOf, _ownSeat), AnchorRef.None),
            "SeatOf Mount with Ref == OccupiedSeat" => (Anchor(Verb.Mount, AnchorFlags.SeatOf, _ownSeat), _ownSeat),
            "Dismount without SeatOf" => (Anchor(Verb.Dismount, AnchorFlags.None, _ownSeat), _ownSeat),
            _ => (Anchor(Verb.Deliver), _ownSeat),
        };
        CarrierStatus s = Status(BodyContext.Mounted, seat: seat);

        // Act
        GateResult r = Gate().Evaluate(in a, in s);

        // Assert
        Assert.Equal(GateResult.NoCandidate, r);
    }

    /// <summary>A None OccupiedSeat never matches, even a (hypothetical, unregistrable) anchor whose Ref is None.</summary>
    [Fact]
    [Trait("Id", "PCM-P11-2d")]
    public void Mounted_NoneSeat_NeverMatchesANoneRef()
    {
        // Arrange
        AnchorDef a = Anchor(Verb.Dismount, AnchorFlags.SeatOf, AnchorRef.None);
        CarrierStatus s = Status(BodyContext.Mounted, seat: AnchorRef.None);

        // Act
        GateResult r = Gate().Evaluate(in a, in s);

        // Assert
        Assert.Equal(GateResult.NoCandidate, r);
    }

    /// <summary>
    /// Build: only BuildModeOnly anchors are candidates; a non-Build anchor is filtered before Panel and swim; a BuildModeOnly anchor
    /// then reaches swim (dry_land). The Build + panel-open row is an unreachable status (the panel opens on foot only,
    /// building-system.md l.115 B11) kept to pin the defensive Panel filter.
    /// </summary>
    [Theory]
    [Trait("Id", "PCM-P11-2d")]
    [InlineData(Verb.Deconstruct, AnchorFlags.BuildModeOnly, false, false, GateKind.Ok, 0)]
    [InlineData(Verb.Deliver, AnchorFlags.None, false, false, GateKind.NoCandidate, 0)]
    [InlineData(Verb.Open, AnchorFlags.AllowWhileSwimming, false, true, GateKind.NoCandidate, 0)]
    [InlineData(Verb.Deliver, AnchorFlags.None, true, false, GateKind.NoCandidate, 0)]
    [InlineData(Verb.Deconstruct, AnchorFlags.BuildModeOnly, true, false, GateKind.Unavailable, 2)]
    [InlineData(Verb.Deconstruct, AnchorFlags.BuildModeOnly, false, true, GateKind.Unavailable, 1)]
    public void Build_OnlyBuildModeOnlyAnchors(Verb verb, AnchorFlags flags, bool panel, bool swimming, GateKind expected, int reason)
    {
        // Arrange
        AnchorDef a = Anchor(verb, flags);
        CarrierStatus s = Status(BodyContext.Build, panel, swimming);
        StringId expectedReason = reason switch { 1 => DryLandId, 2 => ClosePanelId, _ => StringId.None };

        // Act
        GateResult r = Gate().Evaluate(in a, in s);

        // Assert
        Assert.Equal(expected, r.Kind);
        Assert.Equal(expectedReason, r.Reason);
    }

    /// <summary>Panel open: every focused anchor (Deliver included, AllowWhileSwimming included) is Unavailable(close_panel).</summary>
    [Theory]
    [Trait("Id", "PCM-P11-2d")]
    [InlineData(Verb.Deliver, AnchorFlags.None)]
    [InlineData(Verb.Open, AnchorFlags.AllowWhileSwimming)]
    [InlineData(Verb.Shop, AnchorFlags.None)]
    public void PanelOpen_IsUnavailableClosePanel(Verb verb, AnchorFlags flags)
    {
        // Arrange
        AnchorDef a = Anchor(verb, flags);
        CarrierStatus s = Status(panel: true);

        // Act
        GateResult r = Gate().Evaluate(in a, in s);

        // Assert
        Assert.Equal(GateResult.Unavailable(ClosePanelId), r);
    }

    /// <summary>Panel open and swimming: close_panel wins over dry_land (Manual Delivery MD2 row order).</summary>
    [Fact]
    [Trait("Id", "PCM-P11-2d")]
    public void PanelOpenAndSwimming_IsClosePanel()
    {
        // Arrange
        AnchorDef a = Anchor(Verb.Deliver);
        CarrierStatus s = Status(panel: true, swimming: true);

        // Act
        GateResult r = Gate().Evaluate(in a, in s);

        // Assert
        Assert.Equal(GateKind.Unavailable, r.Kind);
        Assert.Equal(ClosePanelId, r.Reason);
    }

    /// <summary>Mounted own seat with the panel open still reaches the Panel filter (Mounted passes, then Panel).</summary>
    [Fact]
    [Trait("Id", "PCM-P11-2d")]
    public void Mounted_OwnSeat_ThenPanel_IsClosePanel()
    {
        // Arrange
        AnchorDef a = Anchor(Verb.Dismount, AnchorFlags.SeatOf, _ownSeat);
        CarrierStatus s = Status(BodyContext.Mounted, panel: true, seat: _ownSeat);

        // Act
        GateResult r = Gate().Evaluate(in a, in s);

        // Assert
        Assert.Equal(GateResult.Unavailable(ClosePanelId), r);
    }

    /// <summary>Mounted own-seat Dismount while swimming, without AllowWhileSwimming: Mounted passes, then swim gives dry_land.</summary>
    [Fact]
    [Trait("Id", "PCM-P11-2d")]
    public void Mounted_OwnSeat_ThenSwimming_IsDryLand()
    {
        // Arrange
        AnchorDef a = Anchor(Verb.Dismount, AnchorFlags.SeatOf, _ownSeat);
        CarrierStatus s = Status(BodyContext.Mounted, swimming: true, seat: _ownSeat);

        // Act
        GateResult r = Gate().Evaluate(in a, in s);

        // Assert
        Assert.Equal(GateResult.Unavailable(DryLandId), r);
    }

    // ---------------------------------------------------------------- PCM-P6-2a/b/c

    /// <summary>Swimming: every anchor without AllowWhileSwimming is Unavailable(dry_land).</summary>
    [Theory]
    [Trait("Id", "PCM-P6-2c")]
    [InlineData(Verb.Deliver)]
    [InlineData(Verb.Open)]
    [InlineData(Verb.PickUp)]
    [InlineData(Verb.Mount)]
    [InlineData(Verb.Shop)]
    public void Swimming_AnchorWithoutAllowFlag_IsDryLand(Verb verb)
    {
        // Arrange
        AnchorDef a = Anchor(verb);
        CarrierStatus s = Status(swimming: true);

        // Act
        GateResult r = Gate().Evaluate(in a, in s);

        // Assert
        Assert.Equal(GateResult.Unavailable(DryLandId), r);
    }

    /// <summary>Swimming: Open-on-pile and Pick-up-on-Spill-heap (AllowWhileSwimming) stay ok.</summary>
    [Theory]
    [Trait("Id", "PCM-P6-2c")]
    [InlineData(Verb.Open)]
    [InlineData(Verb.PickUp)]
    public void Swimming_AllowWhileSwimming_IsOk(Verb verb)
    {
        // Arrange
        AnchorDef a = Anchor(verb, AnchorFlags.AllowWhileSwimming);
        CarrierStatus s = Status(swimming: true);

        // Act
        GateResult r = Gate().Evaluate(in a, in s);

        // Assert
        Assert.Equal(GateResult.Ok, r);
    }

    /// <summary>The seven refused actions are exactly jump, sprint, throw, build, deliver, tool use and drop.</summary>
    [Fact]
    [Trait("Id", "PCM-P6-2a")]
    public void SwimRefused_IsExactlyTheSevenP6Verbs()
    {
        // Arrange
        SwimVerb[] expected =
        {
            SwimVerb.Jump, SwimVerb.Sprint, SwimVerb.Throw, SwimVerb.Build, SwimVerb.Deliver, SwimVerb.ToolUse, SwimVerb.Drop,
        };

        // Act
        SwimVerb[] refused = Enum.GetValues<SwimVerb>().Where(v => !InteractionGate.VerbAllowedWhileSwimming(v)).ToArray();

        // Assert
        Assert.Equal(expected, refused);
    }

    /// <summary>Hotbar select, the own panel, chat, ping and look remain allowed while swimming.</summary>
    [Theory]
    [Trait("Id", "PCM-P6-2b")]
    [InlineData(SwimVerb.HotbarSelect)]
    [InlineData(SwimVerb.OwnPanel)]
    [InlineData(SwimVerb.Chat)]
    [InlineData(SwimVerb.Ping)]
    [InlineData(SwimVerb.Look)]
    public void SwimAllowedRows_AreAllowed(SwimVerb verb)
    {
        // Act
        bool ok = InteractionGate.VerbAllowedWhileSwimming(verb);

        // Assert
        Assert.True(ok);
    }

    /// <summary>An undeclared action value is refused (conservative), never allowed by accident.</summary>
    [Theory]
    [Trait("Id", "PCM-P6-2a")]
    [InlineData((byte)0)]
    [InlineData((byte)13)]
    [InlineData((byte)255)]
    public void SwimUndeclaredValue_IsRefused(byte raw)
    {
        // Act
        bool ok = InteractionGate.VerbAllowedWhileSwimming((SwimVerb)raw);

        // Assert
        Assert.False(ok);
    }

    // ---------------------------------------------------------------- Reason keys

    /// <summary>The key-name constants are the PCM-UI-1 spellings.</summary>
    [Fact]
    [Trait("Id", "PCM-UI-1")]
    public void ReasonKeyNames_AreThePcmUi1Spellings()
    {
        // Assert
        Assert.Equal("reason.dry_land", FrameworkReasons.DryLandKey);
        Assert.Equal("reason.close_panel", FrameworkReasons.ClosePanelKey);
    }

    /// <summary>The gate reports the injected ids it was given (it never mints one).</summary>
    [Fact]
    [Trait("Id", "PCM-UI-1")]
    public void Gate_UsesTheInjectedIds()
    {
        // Arrange
        var other = new InteractionGate(new FrameworkReasons(new StringId(StringNamespace.Run, 7), new StringId(StringNamespace.Run, 8)));
        AnchorDef a = Anchor(Verb.Deliver);
        CarrierStatus swim = Status(swimming: true);
        CarrierStatus panel = Status(panel: true);

        // Act
        GateResult rs = other.Evaluate(in a, in swim);
        GateResult rp = other.Evaluate(in a, in panel);

        // Assert
        Assert.Equal(new StringId(StringNamespace.Run, 7), rs.Reason);
        Assert.Equal(new StringId(StringNamespace.Run, 8), rp.Reason);
        Assert.Equal(DryLandId, Gate().Reasons.DryLand);
        Assert.Equal(ClosePanelId, Gate().Reasons.ClosePanel);
    }

    /// <summary>Over every body x panel x swimming x flag combination, an Unavailable result holds exactly one key, DryLand or ClosePanel; others hold None.</summary>
    [Fact]
    [Trait("Id", "PCM-P11-2d")]
    public void Unavailable_HoldsExactlyOneNonNoneKey()
    {
        // Arrange
        InteractionGate gate = Gate();
        int unavailable = 0;

        foreach (BodyContext body in Enum.GetValues<BodyContext>())
        {
            for (int bits = 0; bits < 4; bits++)
            {
                for (int flags = 0; flags < 8; flags++)
                {
                    AnchorDef a = Anchor(Verb.Dismount, (AnchorFlags)flags, _ownSeat);
                    CarrierStatus s = Status(body, (bits & 1) != 0, (bits & 2) != 0, _ownSeat);

                    // Act
                    GateResult r = gate.Evaluate(in a, in s);

                    // Assert
                    if (r.Kind == GateKind.Unavailable)
                    {
                        unavailable++;
                        Assert.False(r.Reason.IsNone);
                        Assert.True(r.Reason == DryLandId ^ r.Reason == ClosePanelId);
                    }
                    else
                    {
                        Assert.True(r.Reason.IsNone);
                    }
                }
            }
        }

        Assert.NotEqual(0, unavailable);
    }

    /// <summary>DryLand != ClosePanel (and neither None) is a precondition the gate asserts at construction, in every build.</summary>
    [Fact]
    [Trait("Id", "PCM-UI-1")]
    public void Gate_RejectsEqualOrNoneReasonIds()
    {
        // Act + Assert
        Assert.Throws<ArgumentException>(() => new InteractionGate(new FrameworkReasons(DryLandId, DryLandId)));
        Assert.Throws<ArgumentException>(() => new InteractionGate(new FrameworkReasons(StringId.None, ClosePanelId)));
        Assert.Throws<ArgumentException>(() => new InteractionGate(new FrameworkReasons(DryLandId, StringId.None)));
        Assert.Throws<ArgumentException>(() => new InteractionGate(default));
        Assert.NotEqual(DryLandId, ClosePanelId);
    }

    /// <summary>Ok and NoCandidate carry no key; default is the safe NoCandidate.</summary>
    [Fact]
    [Trait("Id", "PCM-P11-2d")]
    public void OkAndNoCandidate_CarryNoKey_DefaultIsNoCandidate()
    {
        // Assert
        Assert.True(GateResult.Ok.Reason.IsNone);
        Assert.True(GateResult.NoCandidate.Reason.IsNone);
        Assert.Equal(GateResult.NoCandidate, default(GateResult));
    }

    // ---------------------------------------------------------------- ContextCancelReason

    /// <summary>Each context change that cancels a hold returns its reason.</summary>
    [Theory]
    [Trait("Id", "PCM-P12-1")]
    [InlineData("panel opens", CancelReason.Panel)]
    [InlineData("mounts", CancelReason.Mounted)]
    [InlineData("knocked down", CancelReason.Knockdown)]
    [InlineData("frozen (on break)", CancelReason.Dead)]
    [InlineData("despawned", CancelReason.Dead)]
    [InlineData("enters swim depth", CancelReason.Swim)]
    public void ContextChange_IntoPanelMountedDeadSwimOrKnockdown_Cancels(string change, CancelReason expected)
    {
        // Arrange
        CarrierStatus before = Status();
        CarrierStatus after = change switch
        {
            "panel opens" => Status(panel: true),
            "mounts" => Status(BodyContext.Mounted, seat: _ownSeat),
            "knocked down" => Status(BodyContext.Dead, life: LifeState.Dead),
            "frozen (on break)" => Status(BodyContext.Dead, life: LifeState.Frozen),
            "despawned" => Status(BodyContext.Dead, life: LifeState.Despawned),
            _ => Status(swimming: true),
        };

        // Act
        CancelReason r = InteractionGate.ContextCancelReason(in before, in after);

        // Assert
        Assert.Equal(expected, r);
    }

    /// <summary>Walking, non-lethal damage, staying in a context, leaving one, and entering Build never cancel.</summary>
    [Theory]
    [Trait("Id", "PCM-P12-1")]
    [InlineData("walking")]
    [InlineData("non-lethal damage")]
    [InlineData("panel stays open")]
    [InlineData("panel closes")]
    [InlineData("leaves the water")]
    [InlineData("still swimming")]
    [InlineData("enters build mode")]
    public void NonCancellingChanges_ReturnNone(string change)
    {
        // Arrange
        (CarrierStatus before, CarrierStatus after) = change switch
        {
            // Walking and non-lethal damage change nothing the status publishes: the life state stays Alive, the body OnFoot.
            "walking" => (Status(), Status() with { Seq = 1 }),
            "non-lethal damage" => (Status(), Status() with { Seq = 2 }),
            "panel stays open" => (Status(panel: true), Status(panel: true)),
            "panel closes" => (Status(panel: true), Status()),
            "leaves the water" => (Status(swimming: true), Status()),
            "still swimming" => (Status(swimming: true), Status(swimming: true)),
            _ => (Status(), Status(BodyContext.Build)),
        };

        // Act
        CancelReason r = InteractionGate.ContextCancelReason(in before, in after);

        // Assert
        Assert.Equal(CancelReason.None, r);
    }

    /// <summary>
    /// Simultaneous changes on one tick resolve in the documented order Knockdown, Dead, Mounted, Panel, Swim: each row combines
    /// one change with every lower-ranked change that can occur beside it.
    /// </summary>
    [Theory]
    [Trait("Id", "PCM-P12-1")]
    [InlineData("knocked down while the panel opens and swim starts", CancelReason.Knockdown)]
    [InlineData("frozen while the panel opens and swim starts", CancelReason.Dead)]
    [InlineData("mounts while the panel opens and swim starts", CancelReason.Mounted)]
    [InlineData("panel opens as swim starts", CancelReason.Panel)]
    public void SimultaneousChanges_FollowTheDocumentedPrecedence(string change, CancelReason expected)
    {
        // Arrange
        CarrierStatus before = Status();
        CarrierStatus after = change switch
        {
            "knocked down while the panel opens and swim starts" => Status(BodyContext.Dead, panel: true, swimming: true, life: LifeState.Dead),
            "frozen while the panel opens and swim starts" => Status(BodyContext.Dead, panel: true, swimming: true, life: LifeState.Frozen),
            "mounts while the panel opens and swim starts" => Status(BodyContext.Mounted, panel: true, swimming: true, seat: _ownSeat),
            _ => Status(panel: true, swimming: true),
        };

        // Act
        CancelReason r = InteractionGate.ContextCancelReason(in before, in after);

        // Assert
        Assert.Equal(expected, r);
    }

    /// <summary>CancelReason is a byte with the six story-013 values pinned (append-only).</summary>
    [Fact]
    [Trait("Id", "PCM-P12-1")]
    public void CancelReason_ValuesArePinned()
    {
        // Assert
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(CancelReason)));
        Assert.Equal(0, (int)CancelReason.None);
        Assert.Equal(1, (int)CancelReason.Panel);
        Assert.Equal(2, (int)CancelReason.Mounted);
        Assert.Equal(3, (int)CancelReason.Dead);
        Assert.Equal(4, (int)CancelReason.Swim);
        Assert.Equal(5, (int)CancelReason.Knockdown);
    }
}
