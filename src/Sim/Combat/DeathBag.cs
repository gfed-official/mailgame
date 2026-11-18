using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;

namespace PerformativeMail.Sim.Combat
{
    /// <summary>
    /// What a carrier who went down had on them (doc 01 §2.2, doc 03 §3.1): a world container at
    /// the spot, open to anyone, gone when it is empty or when its time is up.
    /// </summary>
    public sealed class DeathBag
    {
        public GridContainer Container = null!;
        /// <summary>Whose it was; None once they have left the game.</summary>
        public EntityId Owner;
        public float X, Z;
        public uint DespawnTick;

        public uint Id => Container.Id;
    }
}
