using System.Collections.Generic;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Turrets (doc 05 §4 "12 dmg / 0.5 s, 18 m range; always fires at full rate; targets nearest
    /// enemy, can hit Balloons"; doc 10 turret behind bp_turret; design choice 123): in the tick
    /// after the raiders move, a turret whose gun is ready fires at the nearest raider within its
    /// range that is not fleeing, and hits. A turret never fires at a carrier ("Turrets and spike
    /// strips do not damage players"); what it fells is nobody's kill, its loot lies for anybody.
    /// </summary>
    public sealed partial class SimWorld
    {
        private readonly Dictionary<Construct, uint> _turretReady = new Dictionary<Construct, uint>();

        /// <summary>Shots the turrets have fired since the world began.</summary>
        public int TurretShots { get; private set; }

        private void StepTurrets()
        {
            if (_enemies.Count == 0 || Constructs.All.Count == 0) return;
            foreach (var c in Constructs.All)
            {
                if (!c.Def.IsTurret) continue;
                if (_turretReady.TryGetValue(c, out uint ready) && Tick < ready) continue;
                Enemy? target = null;
                float best = c.Def.ShotRange * c.Def.ShotRange;
                foreach (var e in _enemies)
                {
                    if (e.State == EnemyState.Flee || e.Hp <= 0f) continue;
                    float dx = e.X - c.CentreX, dz = e.Z - c.CentreZ, d = dx * dx + dz * dz;
                    if (d <= best) { best = d; target = e; }
                }
                if (target == null) continue;
                _turretReady[c] = Tick + System.Math.Max(1u, SimConstants.SecondsToTicks(c.Def.ShotSeconds));
                TurretShots++;
                Events.Add(new TurretFiredEvent { X = (byte)c.Tx, Y = (byte)c.Tz, TargetX = target.X, TargetZ = target.Z });
                DamageEnemy(target, c.Def.ShotDamage, null);
            }
        }

        /// <summary>A run ends or begins: no gun is half way through its reload.</summary>
        private void ClearTurrets() => _turretReady.Clear();
    }
}
