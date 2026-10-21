using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 013, Published CarrierStatus and Life state AC: the builder (body, swimming, flag echoes, Aiming,
/// OccupiedSeat normalisation), <see cref="CarrierStatus.ToContextFlags"/>, the router body / holding mapping and every
/// <see cref="LifeState"/> value to <see cref="BodyContext"/>.
/// </summary>
public class CarrierStatusTests
{
    private static readonly AnchorRef _seat = new(new EntityId(0x0100_0042u), 1);

    private static PlayerRecord Record(LifeState life, LocoState loco = LocoState.Grounded) =>
        new() { PlayerId = new EntityId(0x0200_0001u), Life = life, Carrier = new CarrierState { Loco = loco } };

    private static CarrierStatus Build(LifeState life, LocoState loco, bool buildMode = false, bool aiming = false, AnchorRef seat = default)
    {
        PlayerRecord r = Record(life, loco);
        return CarrierStatus.Build(in r, in r.Carrier, cursorHolding: false, panelOpen: false, buildMode, aiming, seat, seq: 0);
    }

    /// <summary>Every life state: only Alive has a body mode; Joining, Dead, DeadAway, Frozen and Despawned are Dead.</summary>
    [Theory]
    [Trait("Id", "PCM-P14-1")]
    [InlineData(LifeState.Joining, BodyContext.Dead)]
    [InlineData(LifeState.Alive, BodyContext.OnFoot)]
    [InlineData(LifeState.Dead, BodyContext.Dead)]
    [InlineData(LifeState.DeadAway, BodyContext.Dead)]
    [InlineData(LifeState.Frozen, BodyContext.Dead)]
    [InlineData(LifeState.Despawned, BodyContext.Dead)]
    public void EveryLifeState_MapsToBodyContext(LifeState life, BodyContext expected)
    {
        // Act
        BodyContext grounded = BodyContexts.From(life, LocoState.Grounded, buildMode: false);
        BodyContext mountedBuild = BodyContexts.From(life, LocoState.Mounted, buildMode: true);

        // Assert
        Assert.Equal(expected, grounded);
        Assert.Equal(life == LifeState.Alive ? BodyContext.Mounted : BodyContext.Dead, mountedBuild);
    }

    /// <summary>The theory above covers every LifeState member, and BodyContext keeps exactly the four ADR-0012 values in router order.</summary>
    [Fact]
    [Trait("Id", "PCM-P14-1")]
    public void Enums_SixLifeStates_FourBodyContextsInRouterOrder()
    {
        // Assert
        Assert.Equal(6, Enum.GetValues<LifeState>().Length);
        Assert.Equal(new[] { BodyContext.OnFoot, BodyContext.Build, BodyContext.Mounted, BodyContext.Dead }, Enum.GetValues<BodyContext>());
        Assert.Equal(0, (int)BodyContext.OnFoot);
        Assert.Equal(3, (int)BodyContext.Dead);
    }

    /// <summary>Alive: Mounted locomotion gives Mounted (over build mode), build mode gives Build, otherwise OnFoot (swimming too).</summary>
    [Theory]
    [Trait("Id", "PCM-P14-1")]
    [InlineData(LocoState.Grounded, false, BodyContext.OnFoot)]
    [InlineData(LocoState.Airborne, false, BodyContext.OnFoot)]
    [InlineData(LocoState.Swimming, false, BodyContext.OnFoot)]
    [InlineData(LocoState.Grounded, true, BodyContext.Build)]
    [InlineData(LocoState.Mounted, false, BodyContext.Mounted)]
    [InlineData(LocoState.Mounted, true, BodyContext.Mounted)]
    public void Alive_BodyFromCarrierMode(LocoState loco, bool buildMode, BodyContext expected)
    {
        // Act
        CarrierStatus s = Build(LifeState.Alive, loco, buildMode);

        // Assert
        Assert.Equal(expected, s.Body);
    }

    /// <summary>The builder echoes life, cursor, panel and seq, and derives Swimming from LocoState.Swimming only.</summary>
    [Fact]
    [Trait("Id", "PCM-P6-2b")]
    public void Build_EchoesInputs_SwimmingFromLoco()
    {
        // Arrange
        PlayerRecord r = Record(LifeState.Alive, LocoState.Swimming);
        var wading = new CarrierState { Loco = LocoState.Grounded, Flags = CarrierFlags.Wading };

        // Act
        CarrierStatus s = CarrierStatus.Build(in r, in r.Carrier, cursorHolding: true, panelOpen: true, buildMode: false, aiming: false, AnchorRef.None, seq: 513);
        CarrierStatus dry = CarrierStatus.Build(in r, in wading, cursorHolding: false, panelOpen: false, buildMode: false, aiming: false, AnchorRef.None, seq: 0);

        // Assert
        Assert.Equal(LifeState.Alive, s.Life);
        Assert.True(s.Swimming);
        Assert.True(s.CursorHolding);
        Assert.True(s.PanelOpen);
        Assert.Equal((ushort)513, s.Seq);
        Assert.False(dry.Swimming);
    }

    /// <summary>The builder takes body mode from the carrier argument, not the record's copy (the owned carrier on a client).</summary>
    [Fact]
    [Trait("Id", "PCM-P14-1")]
    public void Build_UsesTheCarrierArgumentForLoco()
    {
        // Arrange
        PlayerRecord r = Record(LifeState.Alive, LocoState.Grounded);
        var owned = new CarrierState { Loco = LocoState.Mounted };

        // Act
        CarrierStatus s = CarrierStatus.Build(in r, in owned, false, false, false, false, _seat, 0);

        // Assert
        Assert.Equal(BodyContext.Mounted, s.Body);
        Assert.Equal(_seat, s.OccupiedSeat);
    }

    /// <summary>Aiming is a straight echo of the aim bit, in every body.</summary>
    [Theory]
    [Trait("Id", "PCM-P14-1")]
    [InlineData(LifeState.Alive, LocoState.Grounded, true)]
    [InlineData(LifeState.Alive, LocoState.Grounded, false)]
    [InlineData(LifeState.Alive, LocoState.Mounted, true)]
    [InlineData(LifeState.Dead, LocoState.Grounded, true)]
    public void Build_AimingIsEchoed(LifeState life, LocoState loco, bool aiming)
    {
        // Act
        CarrierStatus s = Build(life, loco, aiming: aiming);

        // Assert
        Assert.Equal(aiming, s.Aiming);
    }

    /// <summary>OccupiedSeat survives only when Mounted; every other body forces AnchorRef.None.</summary>
    [Theory]
    [Trait("Id", "PCM-P11-2d")]
    [InlineData(LifeState.Alive, LocoState.Mounted, false, true)]
    [InlineData(LifeState.Alive, LocoState.Grounded, false, false)]
    [InlineData(LifeState.Alive, LocoState.Swimming, false, false)]
    [InlineData(LifeState.Alive, LocoState.Grounded, true, false)]
    [InlineData(LifeState.Dead, LocoState.Mounted, false, false)]
    [InlineData(LifeState.Frozen, LocoState.Mounted, false, false)]
    public void Build_OccupiedSeat_NormalisedToNoneUnlessMounted(LifeState life, LocoState loco, bool buildMode, bool kept)
    {
        // Act
        CarrierStatus s = Build(life, loco, buildMode, seat: _seat);

        // Assert
        Assert.Equal(kept ? _seat : AnchorRef.None, s.OccupiedSeat);
        Assert.Equal(kept, s.Body == BodyContext.Mounted);
    }

    /// <summary>ToContextFlags: PanelOpen to Panel; Body Dead, Mounted, Build to the bit of that name; OnFoot to none.</summary>
    [Theory]
    [Trait("Id", "PCM-P14-1")]
    [InlineData(BodyContext.OnFoot, false, CarrierContextFlags.None)]
    [InlineData(BodyContext.OnFoot, true, CarrierContextFlags.Panel)]
    [InlineData(BodyContext.Build, false, CarrierContextFlags.Build)]
    [InlineData(BodyContext.Build, true, CarrierContextFlags.Build | CarrierContextFlags.Panel)]
    [InlineData(BodyContext.Mounted, false, CarrierContextFlags.Mounted)]
    [InlineData(BodyContext.Dead, false, CarrierContextFlags.Dead)]
    [InlineData(BodyContext.Dead, true, CarrierContextFlags.Dead | CarrierContextFlags.Panel)]
    public void ToContextFlags_MapsPanelAndBody(BodyContext body, bool panel, CarrierContextFlags expected)
    {
        // Arrange
        var s = new CarrierStatus { Life = LifeState.Alive, Body = body, PanelOpen = panel };

        // Act
        CarrierContextFlags actual = s.ToContextFlags();

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Router body is the status body; router holding is the cursor flag except Dead and Mounted, which are never holding.</summary>
    [Theory]
    [Trait("Id", "PCM-P14-1")]
    [InlineData(BodyContext.OnFoot, true, true)]
    [InlineData(BodyContext.OnFoot, false, false)]
    [InlineData(BodyContext.Build, true, true)]
    [InlineData(BodyContext.Mounted, true, false)]
    [InlineData(BodyContext.Dead, true, false)]
    [InlineData(BodyContext.Dead, false, false)]
    public void RouterMapping_DeadAndMountedAreNeverHolding(BodyContext body, bool cursor, bool expectedHolding)
    {
        // Arrange
        var s = new CarrierStatus { Life = LifeState.Alive, Body = body, CursorHolding = cursor };

        // Act
        BodyContext routerBody = InputContexts.RouterBody(in s);
        bool routerHolding = InputContexts.RouterHolding(in s);

        // Assert
        Assert.Equal(body, routerBody);
        Assert.Equal(expectedHolding, routerHolding);
        Assert.Equal(cursor, s.CursorHolding); // the published field stays raw
    }
}
