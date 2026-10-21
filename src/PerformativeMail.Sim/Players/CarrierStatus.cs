namespace PerformativeMail.Sim.Players;

/// <summary>
/// The carrier status the ClientLayer publishes each client tick (story 020) and the input router reads for its <c>body</c> and
/// <c>holding</c> dimensions (ADR-0012 section 2; ADR-0014 section 7 and erratum 2026-10-20). Values only. Build it with
/// <see cref="Build"/>, which derives <see cref="Body"/> and <see cref="Swimming"/> and normalises <see cref="OccupiedSeat"/>;
/// object initialisers are for test fixtures. Fields are published raw: the router mappings live in <see cref="InputContexts"/>.
/// </summary>
/// <example>
/// <code>
/// CarrierStatus s = CarrierStatus.Build(in record, in record.Carrier, cursorHolding: false, panelOpen: false,
///     buildMode: false, aiming: false, occupiedSeat: AnchorRef.None, seq: 7);
/// CarrierContextFlags f = s.ToContextFlags();
/// </code>
/// </example>
public readonly struct CarrierStatus
{
    /// <summary>The host-owned life state.</summary>
    public LifeState Life { get; init; }

    /// <summary>The body context (from <see cref="BodyContexts.From"/>).</summary>
    public BodyContext Body { get; init; }

    /// <summary>True when the carrier's locomotion mode is <see cref="LocoState.Swimming"/> (PC P6).</summary>
    public bool Swimming { get; init; }

    /// <summary>True when the inventory cursor holds an item (raw; the router reads <see cref="InputContexts.RouterHolding"/>, OnFoot only).</summary>
    public bool CursorHolding { get; init; }

    /// <summary>True when the own panel is open: the one source of the Panel flag (its channel is story 020's decision).</summary>
    public bool PanelOpen { get; init; }

    /// <summary>The <c>InputFrame</c> aim bit, echoed for Sim use (body-yaw lock); the router never sources its aiming from it.</summary>
    public bool Aiming { get; init; }

    /// <summary>The <see cref="AnchorFlags.SeatOf"/> Dismount anchor of the occupied seat; <see cref="AnchorRef.None"/> unless Mounted.</summary>
    public AnchorRef OccupiedSeat { get; init; }

    /// <summary>The publication sequence number.</summary>
    public ushort Seq { get; init; }

    /// <summary>
    /// Builds a status. <see cref="Body"/> comes from <paramref name="record"/>'s life state, <paramref name="carrier"/>'s
    /// locomotion mode and <paramref name="buildMode"/>; <see cref="Swimming"/> from the locomotion mode; the flags are echoed;
    /// <see cref="OccupiedSeat"/> is forced to <see cref="AnchorRef.None"/> whenever the body is not Mounted. Allocation free.
    /// </summary>
    /// <param name="record">The player's record (life state).</param>
    /// <param name="carrier">The carrier state the client steps (locomotion mode); usually the owned carrier's state.</param>
    /// <param name="cursorHolding">True when the inventory cursor is non-empty.</param>
    /// <param name="panelOpen">True when the own panel is open.</param>
    /// <param name="buildMode">True when build mode is on (false at M-1a; see <see cref="BodyContexts.From"/>).</param>
    /// <param name="aiming">The <c>InputFrame</c> aim bit.</param>
    /// <param name="occupiedSeat">The occupied seat's Dismount anchor (Vehicles, M1; <see cref="AnchorRef.None"/> at M-1a).</param>
    /// <param name="seq">The publication sequence number.</param>
    /// <returns>The status.</returns>
    /// <example><code>CarrierStatus s = CarrierStatus.Build(in r, in r.Carrier, false, true, false, false, AnchorRef.None, 1);   // PanelOpen</code></example>
    public static CarrierStatus Build(
        in PlayerRecord record,
        in CarrierState carrier,
        bool cursorHolding,
        bool panelOpen,
        bool buildMode,
        bool aiming,
        AnchorRef occupiedSeat,
        ushort seq)
    {
        BodyContext body = BodyContexts.From(record.Life, carrier.Loco, buildMode);
        return new CarrierStatus
        {
            Life = record.Life,
            Body = body,
            Swimming = carrier.Loco == LocoState.Swimming,
            CursorHolding = cursorHolding,
            PanelOpen = panelOpen,
            Aiming = aiming,
            OccupiedSeat = body == BodyContext.Mounted ? occupiedSeat : AnchorRef.None,
            Seq = seq,
        };
    }

    /// <summary>
    /// The solver's context bits (ADR-0013 section 1; the enum is owned by <see cref="CarrierInput"/>): PanelOpen to Panel, and
    /// Body Dead, Mounted or Build to the bit of the same name. <see cref="PanelOpen"/> is echoed raw: the panel cannot open outside
    /// OnFoot (<see cref="InputContexts.CanOpenPanel"/>), so a Panel bit beside Build, Mounted or Dead only comes from an
    /// inconsistent status and is kept as the more restrictive input.
    /// </summary>
    /// <returns>The flags.</returns>
    /// <example><code>CarrierContextFlags f = status.ToContextFlags();   // Panel for an open panel on foot; Build in build mode</code></example>
    public CarrierContextFlags ToContextFlags()
    {
        CarrierContextFlags f = PanelOpen ? CarrierContextFlags.Panel : CarrierContextFlags.None;
        switch (Body)
        {
            case BodyContext.Dead:
                f |= CarrierContextFlags.Dead;
                break;
            case BodyContext.Mounted:
                f |= CarrierContextFlags.Mounted;
                break;
            case BodyContext.Build:
                f |= CarrierContextFlags.Build;
                break;
            default:
                break;
        }

        return f;
    }
}
