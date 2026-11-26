using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Doc 05 §2's airborne trait, the Balloon: it flies straight at the first class of its
    /// targetPriority within aggroRange — over houses, walls and whatever else stands in the way —
    /// and, hovering over it, drops a bomb every attackRate: its damage to everything built and every
    /// carrier within traitParams.bombRadius of where it falls. Only the mail cannon and turrets
    /// reach it; the hammer and spikes do not, and it stops nobody (design choice 135).
    /// </summary>
    public sealed partial class SimWorld
    {
        private readonly List<Construct> _bombScratch = new List<Construct>();

        /// <summary>One tick of an airborne raider that is not winding up or fleeing.</summary>
        private void StepAirborne(Enemy e, float dt)
        {
            if (!ChooseTarget(e, dt, out var target, out var built)) return;
            float gx = target != null ? target.Move.X : built!.CentreX, gz = target != null ? target.Move.Z : built!.CentreZ;
            float dx = gx - e.X, dz = gz - e.Z;
            if (dx * dx + dz * dz <= e.Def.AttackRange * e.Def.AttackRange)
            {
                if (Tick < e.NextAttackTick) return;
                e.State = EnemyState.Windup;
                e.StateEndTick = Tick + _combat.WindupTicks;
                e.NextAttackTick = Tick + Core.SimConstants.SecondsToTicks(e.Def.AttackRate);
                return;
            }
            Walk(e, gx, gz, e.Speed * dt);
        }

        /// <summary>The windup is over: the bomb falls where it hovers, on everything built and every carrier within its reach.</summary>
        private void DropBomb(Enemy e)
        {
            float r = e.Def.BombRadius;
            _bombScratch.Clear();
            foreach (var c in Constructs.All)
                if (Constructs.Distance(c, e.X, e.Z, out _, out _) - c.Def.Radius <= r) _bombScratch.Add(c);
            foreach (var c in _bombScratch) DamageConstruct(c, e.Damage);
            foreach (var p in _players)
            {
                float dx = p.Move.X - e.X, dz = p.Move.Z - e.Z, reach = r + _move.Radius;
                if (!p.Dead && dx * dx + dz * dz <= reach * reach) DamagePlayer(p, e.Damage, e.Def.Index);
            }
            Events.Add(new EnemyShotEvent { Kind = EnemyShotKind.Bomb, FromX = e.X, FromZ = e.Z, ToX = e.X, ToZ = e.Z, AtConstruct = _bombScratch.Count > 0 });
            _bombScratch.Clear();
        }
    }
}
