namespace PerformativeMail.Sim.Core;

/// <summary>Why composition or freeze failed. Append-only (Stories 017 and 025 add the remaining codes).</summary>
/// <example><code>catch (SimBuildException e) when (e.Code == SimBuildErrorCode.ReservedPhase) { }</code></example>
public enum SimBuildErrorCode : byte
{
    /// <summary>A system tried to bind phase 0 (<see cref="PhaseId.InputsApply"/>) or 13 (<see cref="PhaseId.EventsFlush"/>).</summary>
    ReservedPhase = 1,

    /// <summary>A bind (or a second freeze) happened after <see cref="SimWorldBuilder.Freeze"/>.</summary>
    BindAfterFreeze = 2,

    /// <summary>An <see cref="EventKind"/> was bound twice (Sim Core binds <see cref="EventKind.OpReply"/> itself).</summary>
    DuplicateEventKind = 3,

    /// <summary>An <see cref="EventDescriptor"/> broke a basic rule: undefined class or resync scope over 63 (kind <c>None</c> and payload over 48 bytes have their own codes).</summary>
    InvalidEventDescriptor = 4,

    /// <summary>A second <see cref="IEventSink"/> was bound; a world has at most one.</summary>
    EventSinkAlreadyBound = 5,

    /// <summary>More than <see cref="SimCoreSettings.MaxEventReaders"/> (8) event readers were bound with <see cref="SimWorldBuilder.BindEventReader"/>.</summary>
    ReaderLimit = 6,

    /// <summary>An event reader declaration was invalid: a <c>None</c> reader id, an id bound twice, or a null, empty or <c>None</c>-containing filter.</summary>
    InvalidEventReader = 7,

    /// <summary>A second handler was bound for one <see cref="OpKind"/>, or a second, different <see cref="OpDescriptor"/> was declared for it.</summary>
    DuplicateOpHandler = 8,

    /// <summary>An <see cref="OpKind"/> was declared (<see cref="SimWorldBuilder.DeclareOp"/>, or <see cref="SimWorldBuilder.BindOp"/> with a null handler) but has no handler at <see cref="SimWorldBuilder.Freeze"/>.</summary>
    MissingOpHandler = 9,

    /// <summary>An op or event descriptor declared a payload longer than 48 bytes.</summary>
    PayloadTooLarge = 10,

    /// <summary>An <see cref="OpDescriptor"/> declared a system reason code below <see cref="CoreReason.FirstSystemReason"/> (64).</summary>
    SystemReasonBelow64 = 11,

    /// <summary>An <see cref="OpKind"/> or <see cref="EventKind"/> of 0 (<c>None</c>) was declared.</summary>
    InvalidKind = 12,

    /// <summary>World builder: a tile count passed to <c>SetCorners</c> is outside 1 to <c>WorldConstants.MaxTilesPerAxis</c>.</summary>
    WorldTileCountOutOfRange = 13,

    /// <summary>World builder: the corner span length passed to <c>SetCorners</c> is not <c>(tilesX + 1) * (tilesZ + 1)</c>.</summary>
    WorldCornerCountMismatch = 14,

    /// <summary>World builder: a corner height magnitude exceeds <c>WorldConstants.MaxCornerHeightMm</c>.</summary>
    WorldCornerHeightOutOfRange = 15,

    /// <summary>World builder: <c>Build</c> was called before <c>SetCorners</c>.</summary>
    WorldCornersNotSet = 16,

    /// <summary>World builder: a map bounds coordinate has magnitude over <c>WorldConstants.MaxCoordMm</c>.</summary>
    WorldBoundsOutOfRange = 17,

    /// <summary>World builder: the map bounds do not contain the whole corner grid <c>[0, TilesX * S] x [0, TilesZ * S]</c>.</summary>
    WorldBoundsDoNotContainGrid = 18,

    /// <summary>World builder: a <c>MarkBlocked</c>, <c>MarkRoad</c> or <c>MarkLot</c> tile index is outside the map.</summary>
    WorldTileMarkOutOfRange = 19,

    /// <summary>World builder: a walkability limit passed to <c>Build</c> (slope or swim-enter depth) is negative.</summary>
    WorldInvalidWalkabilityLimit = 20,

    /// <summary>Two state contributors were bound with one <see cref="ISimStateContributor.SectionId"/>, or one used Sim Core's reserved <see cref="SectionIds.SimCore"/> (the canonical dump needs unique section ids).</summary>
    DuplicateSection = 21,

    /// <summary>A state contributor was bound with section id 0, which is invalid (an uninitialised id).</summary>
    InvalidSection = 22,

    /// <summary><c>BindOp</c> got a handler whose <c>Kind</c> differs from the descriptor's kind (it would silently misdispatch at drain).</summary>
    HandlerKindMismatch = 23,

    /// <summary>World builder: <c>AddSolid</c> got a box with <c>Min &gt;= Max</c> on some axis (a solid must have positive extent on X, Y and Z).</summary>
    WorldSolidBoxDegenerate = 24,

    /// <summary>World builder: a static solid's box is not inside the map bounds on X/Z, or a Y coordinate has magnitude over <c>WorldConstants.MaxCoordMm</c>.</summary>
    WorldSolidOutsideBounds = 25,

    /// <summary>World builder: more than 65,535 static solids were added (indices are <c>ushort</c>).</summary>
    WorldTooManySolids = 26,

    /// <summary>World builder: the static solids touch more than <c>StaticSolidGrid.MaxBucketEntries</c> 4 m grid buckets in total.</summary>
    WorldSolidGridTooLarge = 27,

    /// <summary>A second <c>IPlayerJoinListener</c> was bound; a world has at most one (Player Character binds it).</summary>
    PlayerJoinListenerAlreadyBound = 28,

    /// <summary>World builder: a lot or street address component is out of range (district 1 to 63, street 0 to 255, number 0 to 4095, unit 0 to 63; ADR-0026 section 6).</summary>
    AddressOutOfRange = 29,

    /// <summary>World builder: two lots have the same <c>AddressId</c> (ADR-0026 section 6).</summary>
    DuplicateAddress = 30,

    /// <summary>World builder: two mailboxes on one street break Mail R3 (same side closer than 6,000 mm, or opposite sides staggered less than 3,000 mm along the street).</summary>
    WorldMailboxSpacingTooSmall = 31,

    /// <summary>World builder: a lot is malformed (face not one of the four axis directions, no street registered for its district and street, face point on the street centre axis, a point or tile outside the map, or a house solid that is out of range, not <c>SolidKind.House</c> or shared with another lot).</summary>
    WorldLotInvalid = 32,

    /// <summary>World builder: a street is malformed (footprint with Min &gt;= Max on X or Z, axis not X or Z, or the same district and street id registered twice).</summary>
    WorldStreetInvalid = 33,

    /// <summary>World builder: a display string is null, the world string table is full (65,535), or a lot or street references a <c>StringId</c> that is not in the world string table.</summary>
    WorldDisplayStringInvalid = 34,

    /// <summary>World builder: lots were added but <c>Build</c> was called without an <c>EntityIds</c>, so their Destination ids cannot be allocated.</summary>
    WorldLotsNeedEntityIds = 35,

    /// <summary>World builder: the <c>EntityIds</c> refused a Destination id (illegal outside <c>Constructed</c>, or the class is exhausted; Release only, where the allocator returns None).</summary>
    WorldDestinationIdRefused = 36,

    /// <summary>World builder: <c>Build(EntityIds, int, int)</c> (the composition path) was called but <c>SetPostOffice</c> never was (ADR-0026 section 7).</summary>
    WorldPostOfficeNotSet = 37,

    /// <summary>World builder: <c>SetPostOffice</c> was called more than once (ADR-0026 section 7: one Post Office).</summary>
    WorldPostOfficeSetTwice = 38,

    /// <summary>World builder: a Post Office anchor (door, intake or pad centre) is outside the map bounds on X/Z, or its |Y| exceeds <c>WorldConstants.MaxCoordMm</c>.</summary>
    WorldPostOfficeAnchorOutsideBounds = 39,

    /// <summary>World builder: a spawn-pad slot lies on a tile that is not <c>TileFlags.Walkable</c> (blocked, too steep, or outside the height grid).</summary>
    WorldPadSlotNotWalkable = 40,

    /// <summary>World builder: a spawn-pad slot lies inside a static solid (closed box on X, Y and Z).</summary>
    WorldPadSlotInsideSolid = 41,

    /// <summary>World builder: a spawn-pad slot has a water depth at or above the swim-enter depth passed to <c>Build</c>.</summary>
    WorldPadSlotTooDeep = 42,

    /// <summary>World builder: a Post Office was set but the parameterless <c>Build()</c> was used; it has no walkability limits, so the pad cannot be validated.</summary>
    WorldPostOfficeNeedsWalkabilityLimits = 43,

    /// <summary>Composition: the profile's <c>SystemGraph</c> has a dependency cycle (ADR-0003 section 3; checked by <c>SimComposition.Build</c> before anything is constructed).</summary>
    DependencyCycle = 44,

    /// <summary>Composition: a system asked for an <c>I…Api</c> that <c>SystemGraph</c> does not declare as its dependency, whose provider is not built before it, or for an injectable it may not receive (<c>MailIds</c> outside Mail, <c>IRunControl</c> outside Run Structure).</summary>
    UndeclaredDependency = 45,

    /// <summary>Composition: one <c>SystemId</c> appears twice in a profile.</summary>
    DuplicateSystem = 46,

    /// <summary>Players: the world has no Post Office / spawn pad (<c>IWorldQuery.PostOffice</c> is <c>default</c>), so a joining carrier has nowhere to spawn.</summary>
    PlayersWorldHasNoPostOffice = 47,

    /// <summary>Players: a <c>player.*</c> value that is a divisor, capacity or capsule size is below 1 (see <c>PlayerSettings.FromBalance</c>).</summary>
    PlayerSettingsInvalid = 48,
}

/// <summary>
/// A composition-time error. The only exception the Sim throws by design: thrown only while building or freezing,
/// never from <see cref="SimWorld.Tick"/> (ADR-0003 section 3).
/// </summary>
/// <example>
/// <code>
/// try { builder.Bind(PhaseId.InputsApply, phase); }
/// catch (SimBuildException e) { Console.WriteLine(e.Code); }   // ReservedPhase
/// </code>
/// </example>
public sealed class SimBuildException : Exception
{
    /// <summary>Creates an exception with an empty message.</summary>
    public SimBuildException()
        : this(default(SimBuildErrorCode), string.Empty)
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public SimBuildException(string message)
        : this(default(SimBuildErrorCode), message)
    {
    }

    /// <summary>Creates an exception with a message and inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public SimBuildException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception carrying a code.</summary>
    /// <param name="code">What went wrong.</param>
    /// <param name="message">The message.</param>
    public SimBuildException(SimBuildErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>What went wrong.</summary>
    public SimBuildErrorCode Code { get; }
}
