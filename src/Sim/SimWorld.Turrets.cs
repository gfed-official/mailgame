using System.Collections.Generic;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Turrets (doc 05 §4 "12 dmg / 0.5 s, 18 m range; always fires at full rate; targets nearest
    /// enemy, can hit Balloons"; doc 10 turret behind bp_turret; design choice 123): in the tick
    /// after the raiders move, a turret whose gun is ready fires at the nearest raider within its
    /// range that is not fleeing, and hits. A turret never fires at a carrier ("Turrets and spike
    /// strips do not damage players"); what it fells is nobody's kill, its loot lies for anybody.
    ///
    /// "Player-operated: 2× fire rate and manual aim" (design choice 125): a carrier at a turret
    /// takes its controls; while they hold them the gun points where they face and fires while
    /// they hold attack, <see cref="Content.BuildingDef.OperatedRate"/> times as often, at the
    /// first raider along that line within its range — it can miss. Their shots are their kills.
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>How far to the side of the line of an operated turret's shot a raider's body may be and still be hit, m.</summary>
        public const float TurretAimSlack = 0.3f;

        /// <summary>The tick each turret's gun is ready again; fractional, so an operated gun fires exactly twice as often (7.5 ticks).</summary>
        private readonly Dictionary<Construct, double> _turretReady = new Dictionary<Construct, double>();
        private readonly Dictionary<Construct, Player> _turretOperators = new Dictionary<Construct, Player>();

        /// <summary>Shots the turrets have fired since the world began.</summary>
        public int TurretShots { get; private set; }

        /// <summary>Who holds the controls of a turret; null: nobody (it fires by itself).</summary>
        public Player? TurretOperator(Construct turret) => _turretOperators.TryGetValue(turret, out var p) ? p : null;

        private void StepTurrets()
        {
            if (_turretOperators.Count > 0) CheckOperators();
            if (_enemies.Count == 0 || Constructs.All.Count == 0) return;
            foreach (var c in Constructs.All)
            {
                if (!c.Def.IsTurret || _turretOperators.ContainsKey(c)) continue;
                if (_turretReady.TryGetValue(c, out double ready) && Tick < ready) continue;
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

        private OpResult OperateTurretOp(Player player, in SimRequest r)
        {
            if (player.Dead) return OpResult.Dead;
            if (r.Count == 0)
            {
                if (player.Turret == null) return OpResult.BadRequest;
                LetGoOfTurret(player);
                return OpResult.Ok;
            }
            if (!Map.InBounds(r.X, r.Y)) return OpResult.BadRequest;
            var turret = Constructs.At(r.X, r.Y);
            if (turret == null || !turret.Def.IsTurret) return OpResult.BadRequest;
            if (player.Move.Riding) return OpResult.AlreadyRiding;
            if (!player.WasWithin(turret.CentreX, turret.CentreZ, _serverInteractRange, _rewindTicks)) return OpResult.OutOfRange;
            if (player.Turret == turret) return OpResult.Ok;
            if (_turretOperators.ContainsKey(turret)) return OpResult.Manned;
            if (player.Turret != null) LetGoOfTurret(player);
            player.Turret = turret;
            _turretOperators[turret] = player;
            Events.Add(new TurretOperatedEvent { X = (byte)turret.Tx, Y = (byte)turret.Tz, Player = player.Id });
            return OpResult.Ok;
        }

        /// <summary>Attack with a turret's controls in the hands: the gun fires along the carrier's facing, when it is ready.</summary>
        private void FireOperatedTurret(Player p)
        {
            var c = p.Turret!;
            bool had = _turretReady.TryGetValue(c, out double ready);
            if (had && Tick < ready) return;
            float rate = c.Def.OperatedRate > 0f ? c.Def.OperatedRate : 1f;
            // Held through, the next shot is timed from when this one was due, not from the whole tick it fell on.
            double from = had && Tick - ready < 1.0 ? ready : Tick;
            _turretReady[c] = from + System.Math.Max(1.0, c.Def.ShotSeconds * SimConstants.TickRate / rate);
            float yaw = PlayerInput.YawToRadians(p.Move.Yaw);
            float fx = -System.MathF.Sin(yaw), fz = -System.MathF.Cos(yaw);
            Enemy? target = null;
            float best = float.MaxValue;
            foreach (var e in _enemies)
            {
                if (e.Hp <= 0f) continue;
                float dx = e.X - c.CentreX, dz = e.Z - c.CentreZ;
                float along = dx * fx + dz * fz;
                if (along <= 0f || along > c.Def.ShotRange + e.Radius) continue;
                float side = dx * fz - dz * fx, reach = e.Radius + TurretAimSlack;
                if (side * side > reach * reach || along >= best) continue;
                best = along;
                target = e;
            }
            TurretShots++;
            float tx = target != null ? target.X : c.CentreX + fx * c.Def.ShotRange;
            float tz = target != null ? target.Z : c.CentreZ + fz * c.Def.ShotRange;
            Events.Add(new TurretFiredEvent { X = (byte)c.Tx, Y = (byte)c.Tz, TargetX = tx, TargetZ = tz });
            if (target != null) DamageEnemy(target, c.Def.ShotDamage, p);
        }

        /// <summary>
        /// The controls are let go of by whoever is down, away, gone, in a saddle or has walked
        /// off, and those of a turret taken down.
        /// </summary>
        private void CheckOperators()
        {
            List<Player>? off = null;
            foreach (var (c, p) in _turretOperators)
            {
                bool gone = Constructs.At(c.Tx, c.Tz) != c || !_players.Contains(p) || p.Dead || p.Away || p.Move.Riding;
                float dx = p.Move.X - c.CentreX, dz = p.Move.Z - c.CentreZ;
                if (gone || dx * dx + dz * dz > _serverInteractRange * _serverInteractRange) (off ??= new List<Player>()).Add(p);
            }
            if (off == null) return;
            foreach (var p in off) LetGoOfTurret(p);
        }

        private void LetGoOfTurret(Player p)
        {
            var c = p.Turret;
            if (c == null) return;
            p.Turret = null;
            _turretOperators.Remove(c);
            Events.Add(new TurretOperatedEvent { X = (byte)c.Tx, Y = (byte)c.Tz, Player = EntityId.None });
        }

        /// <summary>A run ends or begins: no gun is half way through its reload, and nobody holds one.</summary>
        private void ClearTurrets()
        {
            _turretReady.Clear();
            foreach (var p in _turretOperators.Values) p.Turret = null;
            _turretOperators.Clear();
        }
    }
}
