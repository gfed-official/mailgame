using PerformativeMail.Client.Core.Input;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Game.Smoke;

/// <summary>A settable ms clock for the input smokes.</summary>
internal sealed class ManualClock : IInputClock
{
    public long NowMs { get; set; }
}

/// <summary>An interaction query with nothing focused.</summary>
internal sealed class IdleInteraction : IInteractionQuery
{
    private readonly InteractionSnapshot _snapshot;

    public ref readonly InteractionSnapshot Current => ref _snapshot;
}

/// <summary>A throw preview that never refuses an aim.</summary>
internal sealed class NeverRefusingAim : IThrowAimView
{
    public byte NotThrowable => 0;

    public bool EntryStale => false;
}

/// <summary>A client tick source stuck at 0.</summary>
internal sealed class ZeroTicks : IClientTickSource
{
    public uint ClientTick => 0;
}
