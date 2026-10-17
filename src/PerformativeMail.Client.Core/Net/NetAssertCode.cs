namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// Assert codes passed to <c>SimAssert</c> by Client.Core code. The Sim owns codes below 1000;
/// Client.Core uses 1000 and above so the two sets never collide. Append-only: never renumbered
/// or reused.
/// </summary>
public static class NetAssertCode
{
    /// <summary><see cref="ITransport.Poll"/> was re-entered from inside a sink callback.</summary>
    public const ushort PollReentered = 1000;

    /// <summary><c>HostAdapter.PumpInbound</c> was re-entered (for example from a transport sink callback).</summary>
    public const ushort PumpReentered = 1001;

    /// <summary>A transport delivered a message to the host adapter outside <c>PumpInbound</c> (TR-sim-008: callbacks only enqueue).</summary>
    public const ushort InboundOutsidePump = 1002;

    /// <summary>A transport delivered a message from inside another message callback (for example during <c>Send</c>).</summary>
    public const ushort MessageReentered = 1003;

    /// <summary>An enqueued request's <c>reqId</c> could not be added to the in-flight set (cannot happen: checked just before).</summary>
    public const ushort InFlightAddFailed = 1004;

    /// <summary>A Sim <c>OpReply</c> for an in-flight request could not be encoded (handler bug: <c>Result</c> contradicts <c>Reason</c>).</summary>
    public const ushort ReplyCacheEncodeFailed = 1005;

    /// <summary><c>ClientAdapter.PumpInbound</c> was re-entered (for example from a transport sink callback).</summary>
    public const ushort ClientPumpReentered = 1006;

    /// <summary>A transport delivered a message to the client adapter outside <c>PumpInbound</c> (TR-sim-008: callbacks only enqueue).</summary>
    public const ushort ClientInboundOutsidePump = 1007;

    /// <summary>A client batch that passed validation failed to re-read or decode during delivery (a codec changed its answer between the two passes).</summary>
    public const ushort ClientBatchRevalidateFailed = 1008;
}
