using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Inventory;

/// <summary>Grid Inventory's <c>ItemsExpire</c> phase (phase 1). Empty until the world-item stories (story 012) fill it.</summary>
internal sealed class ItemsExpirePhase : ISimPhase
{
    public void Run(in TickContext ctx)
    {
    }
}

/// <summary>Grid Inventory's <c>IntakeDrain</c> phase (phase 2). Empty until the intake story fills it.</summary>
internal sealed class IntakeDrainPhase : ISimPhase
{
    public void Run(in TickContext ctx)
    {
    }
}

/// <summary>Grid Inventory's conservation check (ADR-0009). A no-op stub; story 017 gives it a body.</summary>
internal sealed class InventoryConservationCheck : ITickEndCheck
{
    public void Check(uint tick)
    {
    }
}
