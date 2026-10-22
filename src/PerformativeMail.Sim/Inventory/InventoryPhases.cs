using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.World;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// Grid Inventory's <c>ItemsExpire</c> phase (phase 1, after <c>InputsApply</c>, so a pickup on the expiry tick wins; ADR-0003; story
/// 013): WorldItem expiry, water returns and the cap re-check (<see cref="WorldItems.Expire"/>).
/// </summary>
internal sealed class ItemsExpirePhase : ISimPhase
{
    private readonly WorldItems _worldItems;

    public ItemsExpirePhase(WorldItems worldItems)
    {
        _worldItems = worldItems;
    }

    public void Run(in TickContext ctx) => _worldItems.Expire(ctx.Tick);
}

/// <summary>
/// Grid Inventory's <c>IntakeDrain</c> phase (phase 2, after <c>ItemsExpire</c>; ADR-0003 section 1; story 021): the backlog drain
/// (<see cref="InventorySystem.DrainBacklog"/>).
/// </summary>
internal sealed class IntakeDrainPhase : ISimPhase
{
    private readonly InventorySystem _system;

    public IntakeDrainPhase(InventorySystem system)
    {
        _system = system;
    }

    public void Run(in TickContext ctx) => _system.DrainBacklog();
}

/// <summary>Grid Inventory's conservation check (ADR-0009). A no-op stub; story 017 gives it a body.</summary>
internal sealed class InventoryConservationCheck : ITickEndCheck
{
    public void Check(uint tick)
    {
    }
}
