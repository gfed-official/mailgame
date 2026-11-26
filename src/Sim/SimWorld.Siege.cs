using System;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Doc 05 §2's siege trait, the Giant: it goes for what the carriers built, the first class of
    /// its targetPriority within aggroRange, and strikes it from its long reach; a carrier is its
    /// target only after hurting it from within traitParams.provokeRange, and for provokeSeconds
    /// (design choice 131).
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>A carrier's blow on a siege raider: from close by it turns on them, from afar it does not notice.</summary>
        private void Provoke(Enemy e, Player attacker)
        {
            float reach = e.Def.ProvokeRange + e.Radius;
            if (DistanceSq(e, attacker) > reach * reach) return;
            e.ProvokedBy = attacker.Id;
            e.ProvokedUntilTick = Tick + SimConstants.SecondsToTicks(e.Def.ProvokeSeconds);
            // It turns on them at once rather than at its next look round.
            e.RepathTick = Tick;
        }

        /// <summary>Whether a siege raider is after <paramref name="p"/>: they hurt it from close by lately and may still be fought.</summary>
        private bool Provoked(Enemy e, Player p) =>
            e.ProvokedBy == p.Id && Tick < e.ProvokedUntilTick && !p.Dead && !p.Away && !Protected(p);

        /// <summary>
        /// One tick of a melee raider of a targetPriority (the siege Giant, the Hog Rider) that is
        /// not winding up or fleeing: at the first of its priority, struck from its attackRange.
        /// </summary>
        private void StepPriorityMelee(Enemy e, float dt)
        {
            if (!ChooseTarget(e, dt, out var target, out var built)) return;
            TargetPoint(e, target, built!, out float gx, out float gz, out float dist);
            if (dist <= e.Def.AttackRange + e.Radius)
            {
                Face(e, gx, gz);
                if (Tick < e.NextAttackTick) return;
                e.State = EnemyState.Windup;
                e.StateEndTick = Tick + _combat.WindupTicks;
                e.NextAttackTick = Tick + SimConstants.SecondsToTicks(e.Def.AttackRate);
                e.StrikeTx = built?.Tx ?? -1;
                e.StrikeTz = built?.Tz ?? 0;
                return;
            }
            if (StrikeWhatBlocks(e, gx, gz)) return;
            Walk(e, gx, gz, e.Speed * dt);
        }
    }
}
