using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Shared setup for the Player Character story 001 tests: a composed world over the FX-Pad fixture (the Post Office pad centre is
/// <see cref="WorldTablesFixture.PadCentreMm"/>-relative, so slot positions come from <see cref="PadSlots.Position"/>, never
/// re-derived), join/leave ops driven through the real Sim Core handlers, and access to the composed <see cref="PlayersSystem"/>.
/// </summary>
internal sealed class PlayersRig
{
    private uint _req;

    private PlayersRig(SimHost host, WorldTables world)
    {
        Host = host;
        World = world;
        Players = host.Systems.OfType<PlayersSystem>().Single();
    }

    public SimHost Host { get; }

    public WorldTables World { get; }

    public PlayersSystem Players { get; }

    public SimWorld Sim => Host.World;

    /// <summary>Builds a world (FX-Pad by default) with Players composed through <see cref="SimComposition.Build"/>.</summary>
    public static PlayersRig Create(WorldTables? world = null, ulong seed = 7)
    {
        WorldTables w = world ?? WorldTablesFixture.Pad();
        var args = new SimBuildArgs(SimProfile.M1aGreybox, BuiltInContent.CreateM1a(), seed) { World = w };
        return new PlayersRig(SimComposition.Build(in args), w);
    }

    /// <summary>Enqueues a <c>PlayerJoin</c> for <paramref name="slot"/> and ticks once (the join is applied on the tick that runs).</summary>
    public void JoinAndTick(byte slot)
    {
        var op = new Op { Kind = OpKind.PlayerJoin, ReqId = ++_req };
        op.SetPayload(new PlayerJoinPayload { Slot = slot });
        Assert.True(Sim.EnqueueOp(ActorId.System, in op));
        Sim.Tick();
    }

    /// <summary>Enqueues a <c>PlayerLeave</c> for the player currently in <paramref name="slot"/> and ticks once.</summary>
    public void LeaveAndTick(byte slot)
    {
        var op = new Op { Kind = OpKind.PlayerLeave, ReqId = ++_req };
        op.SetPayload(new PlayerLeavePayload { PlayerId = Host.Services.Slots.Entry(PlayerSlot.FromValue(slot)).PlayerId.Value });
        Assert.True(Sim.EnqueueOp(ActorId.System, in op));
        Sim.Tick();
    }

    /// <summary>Advances the world by <paramref name="ticks"/> empty ticks.</summary>
    public void TickEmpty(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            Sim.Tick();
        }
    }

    public ref PlayerRecord Record(byte slot) => ref Players.RecordRef(PlayerSlot.FromValue(slot));
}

/// <summary>
/// Test-only decorator (story 012 "PureHookGuard"; lifted here from <c>AnchorRegistryTests</c> by story 016): hashes the owner's
/// test state before and after each hook and throws when it changed, so a double that mutates through a captured reference is caught.
/// </summary>
internal sealed class PureHookGuard : IInteractable
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
