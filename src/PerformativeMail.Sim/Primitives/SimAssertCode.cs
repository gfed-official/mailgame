namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Numeric codes passed to <see cref="SimAssert"/> by Sim code. Codes are append-only:
/// a code is never renumbered or reused, so a logged <see cref="SimAssertException.Code"/>
/// identifies the same check forever. Later stories append their own constants here.
/// </summary>
/// <example>
/// <code>
/// SimAssert.That(counter &lt;= EntityId.CounterMask, SimAssertCode.IdCounterRange);
/// </code>
/// </example>
public static class SimAssertCode
{
    /// <summary><see cref="EntityId.Compose"/> was given a counter above 0xFF_FFFF.</summary>
    public const ushort IdCounterRange = 1;

    /// <summary>A player slot value other than 0-7 (or the 0xFF None sentinel) was supplied.</summary>
    public const ushort IdSlotRange = 2;

    /// <summary><see cref="EntryId.Temp"/> was given a counter of 2^20 or more.</summary>
    public const ushort EntryTempCtrRange = 3;

    /// <summary>A real <see cref="EntryId"/> was supplied with bit 31 set.</summary>
    public const ushort EntryRealBit31 = 4;

    /// <summary>A <see cref="DefIndex"/> ordinal above 65 533 (other than the 0xFFFF None sentinel) was supplied.</summary>
    public const ushort DefIndexRange = 5;

    /// <summary>A temp <see cref="EntryId"/> was requested for a None <see cref="PlayerSlot"/>.</summary>
    public const ushort EntryTempSlotNone = 6;

    /// <summary>A raw temp <see cref="EntryId"/> value had non-zero reserved bits 23-30.</summary>
    public const ushort EntryTempLayout = 7;

    /// <summary><see cref="Cents.FromCents"/> was given a value outside +/-2^62.</summary>
    public const ushort MoneyRange = 8;

    /// <summary><see cref="Cents.Add"/> or <see cref="Cents.Sub"/> produced a result outside +/-2^62.</summary>
    public const ushort MoneyOverflow = 9;

    /// <summary><see cref="Cents.MulPermille"/> was given a negative permille ratio (GDD F7: 0 to 10^6).</summary>
    public const ushort MoneyBadPermille = 10;

    /// <summary>A <c>TickTime</c> tick count or remaining-tick value was outside 0 to 2^31 - 1 (negative for remaining).</summary>
    public const ushort TickRange = 11;

    /// <summary><c>TickTime.ContentTicks</c> was given milliseconds outside 0 to 86 400 000.</summary>
    public const ushort MsRange = 12;

    /// <summary><c>RngStream.NextInt</c> was called with a bound of 0 (the draw is still consumed and 0 is returned).</summary>
    public const ushort RngBoundZero = 13;

    /// <summary><c>RngRegistry.Create</c> was given an index outside the name's allowed range (Release counts <c>RngIndexOutOfRange</c> and derives on a spare slot).</summary>
    public const ushort RngIndexRange = 14;

    /// <summary>An owner-filtered RNG view was asked for a stream its owner does not own (Release refuses and counts <c>RngRefused</c>).</summary>
    public const ushort RngOwnerMismatch = 15;

    /// <summary><c>RngRegistry.Create</c> was given a <see cref="Core.StreamName"/> that is not in <see cref="Core.RngNames"/> (for example <c>default</c>).</summary>
    public const ushort RngUnknownName = 16;

    /// <summary><c>TickDriver.Resync</c> was called while the driver was not <c>Running</c> (Release ignores the call).</summary>
    public const ushort TickDriverResyncState = 17;

    /// <summary><c>TickDriver.DropRemaining</c> was given a count outside 0 to <c>TicksRunTotal</c> (Release clamps it).</summary>
    public const ushort TickDriverDropRange = 18;

    /// <summary><c>SimWorld.Tick</c>, <c>Stop</c>, <c>EnqueueOp</c> or <c>IRunControl.RequestStop</c> was called off the owner thread captured at <c>Freeze()</c> (Release: <c>Tick</c> counts <c>BadTick</c> and runs nothing; <c>EnqueueOp</c> is not guarded).</summary>
    public const ushort TickWrongThread = 19;

    /// <summary><c>Op.PayloadAs</c> was called with a type whose size differs from the op's <c>PayloadLength</c> (Release reads the leading bytes, zero past the stored length).</summary>
    public const ushort OpPayloadLength = 20;

    /// <summary><c>Op.SetPayload</c> was given a type larger than the 48-byte payload (Release stores nothing and leaves <c>PayloadLength</c> 0).</summary>
    public const ushort OpPayloadTooLarge = 21;

    /// <summary><c>Reply.SetExtra</c> was given a type larger than the 24-byte extra area (Release stores nothing).</summary>
    public const ushort ReplyExtraTooLarge = 22;

    /// <summary><c>Reply.ExtraAs</c> was given a type larger than the 24-byte extra area (Release returns <c>default</c>).</summary>
    public const ushort ReplyExtraRead = 23;

    /// <summary><c>Reply.Reject</c> was given reason 0, which would break <c>Ok &lt;=&gt; Reason == 0</c> (Release substitutes <c>CoreReason.BadRequest</c>).</summary>
    public const ushort ReplyRejectZeroReason = 24;

    /// <summary><c>Reply.Reject</c> was given a reserved Sim Core reason (9 to 63) (Release substitutes <c>CoreReason.BadRequest</c>).</summary>
    public const ushort ReplyRejectReservedReason = 25;

    /// <summary><c>OpQueue</c> was constructed with a capacity that is not a power of two of at least 1 (Release rounds it up to the next power of two).</summary>
    public const ushort OpQueueCapacityNotPow2 = 26;

    /// <summary><c>OpQueue</c> was constructed with a system-op reserve outside 0 to its capacity (Release clamps it).</summary>
    public const ushort OpQueueReserveRange = 27;

    /// <summary><c>OpQueue.PeekAt</c>, <c>BoundaryTick</c> or <c>DropFront</c> was given an index or count outside 0 to <c>Count</c> (Release wraps the index or clamps the count).</summary>
    public const ushort OpQueueIndexRange = 28;

    /// <summary><c>EventLog.TryRaise</c> was given a payload type larger than the 48-byte payload (Release drops the raise and returns false).</summary>
    public const ushort EventPayloadTooLarge = 29;

    /// <summary><c>EventLog.TryRaise</c> was given a payload larger than its kind's declared <c>PayloadLength</c> (Release stores the entry with the declared length, truncating the payload to it).</summary>
    public const ushort EventPayloadExceedsDescriptor = 30;

    /// <summary><c>EventLog.TryRaise</c> was given an <c>OwnerOnly</c> audience with more than one bit set (Release keeps the lowest set bit).</summary>
    public const ushort EventOwnerOnlyAudience = 31;

    /// <summary><c>SimWorld.EnqueueOp</c> was called from inside <c>IEventSink.Deliver</c> (Release refuses it and counts <c>EnqueueDuringFlush</c>).</summary>
    public const ushort EventEnqueueDuringFlush = 32;

    /// <summary><c>SimWorld.Tick</c> was called from inside <c>IEventSink.Deliver</c> (Release counts <c>BadTick</c> and runs nothing).</summary>
    public const ushort EventTickDuringFlush = 33;

    /// <summary>A phase run returned with an event call scope still open, for example a discarded <c>log.BeginCall();</c> (Release resets the scope depth).</summary>
    public const ushort EventScopeLeak = 34;

    /// <summary><c>EventLog.TryRaise</c> was given a payload type that is not the kind's declared <c>PayloadType</c>, so padding or the wrong layout could reach the log (Release stores the bytes under the declared length: truncated to it when longer, zero-padded when shorter).</summary>
    public const ushort EventPayloadTypeMismatch = 35;

    /// <summary>An event was raised while the world was between ticks (Release drops it and counts <c>EventsOutsideTick</c>).</summary>
    public const ushort EventRaiseOutsideTick = 36;

    /// <summary>An event was raised from an <c>ITickEndCheck</c> (Release drops it, counts <c>EventsAtTickEnd</c> and sets <c>Overflow</c>).</summary>
    public const ushort EventRaiseAtTickEnd = 37;

    /// <summary>An event was raised from inside <c>IEventSink.Deliver</c> (Release drops it, counts <c>EventsDuringFlush</c> and sets <c>Overflow</c>).</summary>
    public const ushort EventRaiseDuringFlush = 38;

    /// <summary><c>EventLog.Read</c> was given a cursor whose <c>ReaderId</c> was never bound with <c>BindEventReader</c> (Release returns false).</summary>
    public const ushort EventReaderUnknown = 39;

    /// <summary>A <c>SegmentIntervals</c> quadratic form (circle, sphere, cylinder) was called with |R| above 65 536 mm or a segment whose |D|^2 exceeds 2^31 - 1 mm^2 (Release returns no hit without evaluating the form).</summary>
    public const ushort GeometryBroadPhase = 40;

    /// <summary><c>HeightfieldMarch.FirstContact</c> was given a segment with a horizontal or vertical extent above 65 536 mm (Release returns no contact without marching).</summary>
    public const ushort GeometryMarchRange = 41;

    /// <summary>An <c>EventLog</c> was created with a reader filter that is empty, contains kind 0 or is not sorted ascending (the builder guarantees all three).</summary>
    public const ushort EventReaderFilterInvalid = 42;

    /// <summary><c>EntityIds.Allocate</c> or <c>MailIds.Allocate</c> was called outside <c>Tick()</c> (other than Destination or Prop while <c>Constructed</c>) or off the owner thread (Release returns None/0 and counts <c>IdAllocIllegal</c>).</summary>
    public const ushort IdAllocIllegal = 43;

    /// <summary><c>EntityIds.Allocate</c> was given a reserved class (0x00, 0x09 to 0xEF) or a test class (0xF0 to 0xFF) on an <c>EntityIds</c> built without <c>allowTestClasses</c> (Release returns None and writes an <c>IdReservedClass</c> check record).</summary>
    public const ushort IdReservedClass = 44;

    /// <summary>An entity class counter reached 2^24 - 1 or the mail counter reached 2^32 - 1 (Release returns None/0 and writes an <c>IdExhausted</c> check record; the counter never wraps).</summary>
    public const ushort IdExhausted = 45;

    /// <summary>A <c>CanonicalWriter</c> was misused: a field written outside a section, a nested or unmatched <c>BeginSection</c>/<c>EndSection</c>, a header inside a section, or a section over the maximum array length (Release drops the call).</summary>
    public const ushort DumpWriterMisuse = 46;

    /// <summary>An owner-thread-only <c>SimWorld</c> member other than <c>Tick</c> (<c>EnqueueOp</c>, <c>Stop</c>, <c>TryWriteDump</c>, the stop port) was called off the thread that froze the world (<c>Tick</c> keeps <c>TickWrongThread</c>).</summary>
    public const ushort WrongThread = 47;

    /// <summary>A handler's <c>Reply</c> broke <c>Result == Ok</c> iff <c>Reason == 0</c> when the drain appended it (Release counts <c>ReplyInconsistent</c>, normalises <c>Result</c> from <c>Reason</c> and still appends).</summary>
    public const ushort ReplyResultReasonMismatch = 48;

    /// <summary><c>SimCoreSettings.SampleRingCapacity</c> is not a power of two in 1 to 65536 (the ring rounds it up and clamps it, and asserts under <c>SIM_ASSERT</c>).</summary>
    public const ushort SampleRingCapacityNotPow2 = 49;

    /// <summary>A <c>PlayerJoin</c> or <c>PlayerLeave</c> handler's slot-table mutation was refused after validation passed (a Core invariant break).</summary>
    public const ushort PlayerSlotMutationFailed = 50;

    /// <summary><c>AddressId.Pack</c> got a component out of range (district 1 to 63, street 0 to 255, number 0 to 4095, unit 0 to 63); Release masks each component to its field width.</summary>
    public const ushort AddressComponentOutOfRange = 51;

    /// <summary>Players was told a player joined a slot that already holds a record (the slot table refused it first; a Core invariant break). Release replaces the record.</summary>
    public const ushort PlayersJoinSlotOccupied = 52;

    /// <summary>Players was told a player left a slot whose record holds a different player id or none (a Core invariant break). Release clears the slot anyway.</summary>
    public const ushort PlayersLeaveMismatch = 53;

    /// <summary>A <c>SerialU8/U16/U32.IsNewer</c> comparison saw two values exactly half the range apart (neither newer nor older; ADR-0004 section 5). Release returns false.</summary>
    public const ushort SerialHalfRange = 54;

    /// <summary><c>InputLatch.PushEdge</c> got an edge whose <c>ArrivalSeq</c> is older than the previously pushed one (the router must push in arrival order; Release latches it as given).</summary>
    public const ushort InputEdgeOutOfOrder = 55;

    /// <summary><c>RouterStateInfo.Pack</c> got a layer value outside its enum's range (Release returns <c>RouterStateInfo.InvalidId</c>).</summary>
    public const ushort RouterStateLayerRange = 56;
}
