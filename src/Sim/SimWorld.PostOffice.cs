using System;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// The Post Office's health (doc 05 §5 "PO 3000 … The PO cannot become a ruin: at 0 HP the run
    /// ends. The PO regenerates 5 HP/s during Prep"; doc 01 §7 "Post Office HP reaches 0 — Run Over
    /// (immediate)"; design choice 137). Raiders with "po" in their targetPriority go for it and
    /// strike its walls from their reach.
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>Windup: the blow is for the Post Office.</summary>
        private const int PostOfficeStrike = -2;
        /// <summary>How far outside its walls a raider is sent to stand at the Post Office.</summary>
        private const float PostOfficeStandOff = 0.6f;

        public float PostOfficeHp { get; private set; }
        public float PostOfficeMaxHp => Content.Balance.GetFloat("postOfficeHp");
        public bool PostOfficeStands => PostOfficeHp > 0f;

        private void ResetPostOffice() => PostOfficeHp = PostOfficeMaxHp;

        /// <summary>A tick: in Prep it mends itself, balance.postOfficeRegenPerSecond.</summary>
        private void StepPostOffice()
        {
            if (Run.Phase != RunPhase.Prep || PostOfficeHp >= PostOfficeMaxHp || PostOfficeHp <= 0f) return;
            PostOfficeHp = MathF.Min(PostOfficeMaxHp, PostOfficeHp + Content.Balance.GetFloat("postOfficeRegenPerSecond") * SimConstants.TickDt);
        }

        /// <summary>A blow on the Post Office; at 0 the run is over at once (a quota waived or not).</summary>
        public void DamagePostOffice(float damage, Enemy? by = null)
        {
            if (damage <= 0f || PostOfficeHp <= 0f || !IsRunning(Run.Phase)) return;
            // The debug world's showcase is not to be lost (its god mode).
            if (GodMode) return;
            PostOfficeHp = MathF.Max(0f, PostOfficeHp - MathF.Max(1f, MathF.Round(damage)));
            Events.Add(new PostOfficeDamagedEvent { Hp = (ushort)MathF.Ceiling(PostOfficeHp), Enemy = by?.Def.Index ?? 255 });
            if (PostOfficeHp <= 0f) BeginResults(RunOutcome.PostOfficeLost);
        }

        private static bool IsRunning(RunPhase phase) => phase == RunPhase.Prep || phase == RunPhase.Delivery || phase == RunPhase.Payday || phase == RunPhase.Draft;

        /// <summary>How far a point is from the Post Office's walls, and the nearest point of them.</summary>
        public float PostOfficeDistance(float x, float z, out float nearX, out float nearZ) => RepairRules.PostOfficeDistance(Map, x, z, out nearX, out nearZ);

        /// <summary>Where a raider at (x, z) stands to strike the Post Office: just outside the nearest point of its walls.</summary>
        private void PostOfficeGoal(float x, float z, out float gx, out float gz)
        {
            float d = PostOfficeDistance(x, z, out gx, out gz);
            if (d < 1e-3f) return;
            gx += (x - gx) / d * PostOfficeStandOff;
            gz += (z - gz) / d * PostOfficeStandOff;
        }
    }
}
