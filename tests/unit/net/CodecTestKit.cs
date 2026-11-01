using System.Runtime.InteropServices;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Sim.Tests.Unit.Net;

/// <summary>
/// Shared fixtures for the op codec tests (story network-adapter-loopback/002). One place builds the frozen world and the
/// registry, so the round-trip Theory and the fuzz test follow whatever is registered here: a feature epic adds its codec
/// to <see cref="AllCodecs"/> (and its descriptor reaches the world through the normal builder path) and gets Theory and fuzz
/// coverage with no other test edit.
/// </summary>
internal static class CodecTestKit
{
    /// <summary>A descriptor with codec id 0: a kind that exists in the Sim but is not wire-reachable.</summary>
    public static readonly OpDescriptor NoWireDescriptor = OpDescriptor.Empty(OpKind.FreezePlayer, systemOnly: true, wireCodecId: 0, wireSize: 0);

    /// <summary>Every codec the adapter registers. Feature epics append here.</summary>
    public static IOpCodec[] AllCodecs() => [.. SimCoreCodecs.Create(), .. ThrowingCodecs.CreateOps()];

    /// <summary>Builds a frozen world with the Sim Core player ops, a no-wire op, Throwing's <c>ThrowRelease</c> and any extra descriptors (stub handlers).</summary>
    public static SimWorld BuildWorld(params OpDescriptor[] extra) => BuildWorldWithEvents(extra);

    /// <summary>As <see cref="BuildWorld"/> with extra event descriptors bound.</summary>
    public static SimWorld BuildWorldWithEvents(OpDescriptor[] extra, params EventDescriptor[] events)
    {
        var builder = new SimWorldBuilder();
        for (int i = 0; i < events.Length; i++)
        {
            builder.BindEventKind(events[i]);
        }

        builder.BindOp(CorePlayerOps.JoinDescriptor, new StubHandler(OpKind.PlayerJoin));
        builder.BindOp(CorePlayerOps.LeaveDescriptor, new StubHandler(OpKind.PlayerLeave));
        builder.BindOp(NoWireDescriptor, new StubHandler(NoWireDescriptor.Kind));
        builder.BindOp(ThrowDescriptors.ThrowRelease, new StubHandler(ThrowOpKinds.ThrowRelease));
        for (int i = 0; i < extra.Length; i++)
        {
            builder.BindOp(extra[i], new StubHandler(extra[i].Kind));
        }

        return builder.Freeze();
    }

    /// <summary>The registry over <see cref="BuildWorld"/> and <see cref="AllCodecs"/>.</summary>
    public static OpCodecRegistry BuildRegistry()
    {
        SimWorld world = BuildWorld();
        return new OpCodecRegistry(world.OpDescriptors, AllCodecs());
    }

    /// <summary>The 64 raw bytes of an op.</summary>
    public static byte[] Raw(in Op op)
    {
        Op copy = op;
        return MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref copy, 1)).ToArray();
    }

    /// <summary>The 36 raw bytes of a reply.</summary>
    public static byte[] RawReply(in Reply reply)
    {
        Reply copy = reply;
        return MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref copy, 1)).ToArray();
    }

    /// <summary>A join op built with SetPayload.</summary>
    public static Op Join(uint playerId, byte slot, OpKind kind = OpKind.PlayerJoin, uint reqId = 0)
    {
        var op = new Op { Kind = kind, ReqId = reqId };
        op.SetPayload(new PlayerJoinPayload { PlayerId = playerId, Slot = slot });
        return op;
    }

    /// <summary>A leave op built with SetPayload.</summary>
    public static Op Leave(uint playerId, uint reqId = 0)
    {
        var op = new Op { Kind = OpKind.PlayerLeave, ReqId = reqId };
        op.SetPayload(new PlayerLeavePayload { PlayerId = playerId });
        return op;
    }

    /// <summary>A handler that does nothing; the codec tests never tick.</summary>
    private sealed class StubHandler : IOpHandler
    {
        public StubHandler(OpKind kind) => Kind = kind;

        public OpKind Kind { get; }

        public Reply Apply(in Op op, in TickContext ctx) => default;
    }
}
