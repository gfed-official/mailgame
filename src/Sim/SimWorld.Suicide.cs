using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Doc 05 §2's suicide trait, the Wall Breaker: it runs at the nearest wall, gate or belt of its
    /// targetPriority (a carrier last) and goes off beside it, or against whatever stands in its
    /// way — traitParams.structureDamage to the walls, gates and belts in the blast (less a wall's
    /// wallBreakerResist), its damage to everything else built there and to the carriers in it
    /// (design choice 129).
    /// </summary>
    public sealed partial class SimWorld
    {
        private readonly List<Construct> _blastScratch = new List<Construct>();

        /// <summary>One tick of a suicide raider that is not fleeing. It may be gone after it.</summary>
        private void StepSuicide(Enemy e, float dt)
        {
            if (!ChooseTarget(e, dt, out var target, out var built)) return;
            TargetPoint(e, target, built!, out float gx, out float gz, out float dist);
            Face(e, gx, gz);
            float reach = target != null ? e.Def.AttackRange + e.Radius : e.Radius + StrikeReach;
            if (dist <= reach) { GoOff(e); return; }
            // Whatever the carriers built that stands in its way, it goes off against.
            if (Constructs.All.Count > 0)
            {
                WalkPoint(e, gx, gz, out float wx, out float wz);
                float dx = wx - e.X, dz = wz - e.Z, len = MathF.Sqrt(dx * dx + dz * dz);
                if (len > 1e-3f && Constructs.Ahead(e.X, e.Z, dx / len, dz / len, e.Radius + StrikeReach) != null) { GoOff(e); return; }
            }
            Walk(e, gx, gz, e.Speed * dt);
        }

        /// <summary>Doc 05 §2 "explodes (r = 1.5 m)": the blast, and the raider is gone (nobody's kill, no loot).</summary>
        private void GoOff(Enemy e)
        {
            float r = e.Def.BlastRadius, mult = e.Damage / MathF.Max(1e-3f, e.Def.Damage);
            _blastScratch.Clear();
            foreach (var c in Constructs.All)
                if (Constructs.Distance(c, e.X, e.Z, out _, out _) - c.Def.Radius <= r) _blastScratch.Add(c);
            foreach (var c in _blastScratch)
            {
                var b = c.Def.Behaviour;
                bool structure = b == BuildingBehaviour.Wall || b == BuildingBehaviour.Gate || b == BuildingBehaviour.Belt;
                DamageConstruct(c, structure ? e.Def.StructureDamage * mult * (1f - c.Def.WallBreakerResist) : e.Damage);
            }
            foreach (var p in _players)
            {
                float dx = p.Move.X - e.X, dz = p.Move.Z - e.Z, reach = r + _move.Radius;
                if (dx * dx + dz * dz <= reach * reach) DamagePlayer(p, e.Damage, e.Def.Index);
            }
            Events.Add(new EnemyShotEvent { Kind = EnemyShotKind.Blast, FromX = e.X, FromZ = e.Z, ToX = e.X, ToZ = e.Z, AtConstruct = _blastScratch.Count > 0 });
            _blastScratch.Clear();
            _enemies.Remove(e);
        }
    }
}
