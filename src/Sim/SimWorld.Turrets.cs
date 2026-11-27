using System.Collections.Generic;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Perks;
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
                DamageEnemy(target, c.Def.ShotDamage * TeamStat(Stat.TurretDamage, 1f), null);
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

        /// <summary>
        /// Attack with a turret's controls in the hands: the gun fires, when it is ready, at what is under
        /// the carrier's crosshair — the first raider along their line of sight, from the gunner's seat
        /// over the gun (<see cref="TurretSight.Seat"/>, where their eyes are at its controls), within
        /// the gun's reach of its middle (design choices 134 and 138).
        /// </summary>
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
            TurretSight.Seat(c.CentreX, c.CentreZ, fx, fz, out float px, out float pz);
            Enemy? target = null;
            float best = float.MaxValue;
            foreach (var e in _enemies)
            {
                if (e.Hp <= 0f) continue;
                // Doc 06 §4.3: where the gunner's client drew it.
                Rewound(e, out float ex, out float ez);
                float cx = ex - c.CentreX, cz = ez - c.CentreZ, reachOfGun = c.Def.ShotRange + e.Radius;
                if (cx * cx + cz * cz > reachOfGun * reachOfGun) continue;
                float dx = ex - px, dz = ez - pz;
                float along = dx * fx + dz * fz;
                if (along <= -e.Radius) continue;
                float side = dx * fz - dz * fx, reach = e.Radius + TurretAimSlack;
                if (side * side > reach * reach || along >= best) continue;
                best = along;
                target = e;
            }
            TurretShots++;
            float tx, tz;
            if (target != null) Rewound(target, out tx, out tz);
            else TurretSight.Exit(px, pz, fx, fz, c.CentreX, c.CentreZ, c.Def.ShotRange, out tx, out tz);
            Events.Add(new TurretFiredEvent { X = (byte)c.Tx, Y = (byte)c.Tz, TargetX = tx, TargetZ = tz });
            if (target != null) DamageEnemy(target, c.Def.ShotDamage * TeamStat(Stat.TurretDamage, 1f), p);
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

    /// <summary>Where an operated turret's carrier looks from, and where their line of sight leaves the gun's reach (design choices 134 and 138).</summary>
    public static class TurretSight
    {
        /// <summary>
        /// How far behind the gun's bearing, against its facing, the gunner's eyes are, m, and how high
        /// over the ground (the playtest f32ea2e: from where the carrier stood, the gun's head filled the
        /// middle of the view; from over it, the head and the barrels are at the bottom of it).
        /// </summary>
        public const float SeatBack = 0.3f, SeatHeight = 2.4f;

        /// <summary>The ground point of the gunner's eyes at a turret whose middle is (<paramref name="cx"/>, <paramref name="cz"/>), facing the unit (<paramref name="fx"/>, <paramref name="fz"/>).</summary>
        public static void Seat(float cx, float cz, float fx, float fz, out float x, out float z)
        {
            x = cx - fx * SeatBack;
            z = cz - fz * SeatBack;
        }

        /// <summary>
        /// The point along the ray from (<paramref name="px"/>, <paramref name="pz"/>) in the unit
        /// direction (<paramref name="fx"/>, <paramref name="fz"/>) that is <paramref name="range"/>
        /// from the turret's middle (<paramref name="cx"/>, <paramref name="cz"/>); from beyond the
        /// reach looking away, the ray's point nearest the middle.
        /// </summary>
        public static void Exit(float px, float pz, float fx, float fz, float cx, float cz, float range, out float x, out float z)
        {
            float ox = px - cx, oz = pz - cz;
            float b = ox * fx + oz * fz, c = ox * ox + oz * oz - range * range;
            float disc = b * b - c;
            float t = disc >= 0f ? -b + System.MathF.Sqrt(disc) : -b;
            if (t < 0f) t = 0f;
            x = px + fx * t;
            z = pz + fz * t;
        }
    }
}
