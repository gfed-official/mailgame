using System;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Doc 05 §2's enemy traits beyond the melee raider: its target priority (a carrier, else the
    /// nearest construct of the next class it names within aggroRange) and the ranged trait of the
    /// Archer — it shoots from attackRange, keeps keepDistance from a carrier and backs off from one
    /// coming nearer (design choice 128).
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>Ranged: this long backing off from a carrier who keeps coming, then it stands and shoots once.</summary>
        private static readonly uint BackOffTicks = SimConstants.SecondsToTicks(2.0);

        /// <summary>
        /// The first class of its targetPriority with one in its aggroRange: a carrier (the nearest
        /// valid one) or a construct (the nearest standing one of that class).
        /// </summary>
        private void AcquireByPriority(Enemy e, out Player? player, out Construct? built)
        {
            player = null;
            built = null;
            foreach (var target in e.Def.TargetPriority)
            {
                if (target == EnemyTarget.Player)
                {
                    player = Acquire(e);
                    if (player != null) return;
                    continue;
                }
                float best = e.Def.AggroRange;
                foreach (var c in Constructs.All)
                {
                    if (!EnemyDef.IsOf(target, c.Def)) continue;
                    float d = Constructs.Distance(c, e.X, e.Z, out _, out _) - c.Def.Radius;
                    if (d > best) continue;
                    best = d;
                    built = c;
                }
                if (built != null) return;
            }
        }

        /// <summary>The construct a raider goes for, if it still stands.</summary>
        private Construct? GoalOf(Enemy e)
        {
            if (e.GoalTx < 0) return null;
            var built = Constructs.At(e.GoalTx, e.GoalTz);
            if (built == null || built.Tx != e.GoalTx || built.Tz != e.GoalTz) { e.GoalTx = -1; return null; }
            return built;
        }

        /// <summary>One tick of a ranged raider that is not winding up or fleeing.</summary>
        private void StepRanged(Enemy e, float dt)
        {
            var target = GetPlayer(e.Target);
            if (target != null)
            {
                if (DistanceSq(e, target) <= e.Def.AggroRange * e.Def.AggroRange) { e.SeenX = e.X; e.SeenZ = e.Z; }
                if (!ValidTarget(e, target, e.Def.AggroRange * _combat.LeashMult, e.SeenX, e.SeenZ)) target = null;
            }
            var built = target == null ? GoalOf(e) : null;
            if ((target == null && built == null) || Tick >= e.RepathTick)
            {
                AcquireByPriority(e, out var player, out var goal);
                if (player != null && player != target) { e.SeenX = e.X; e.SeenZ = e.Z; }
                target = player;
                built = player == null ? goal : null;
                e.Target = target?.Id ?? EntityId.None;
                e.GoalTx = built?.Tx ?? -1;
                e.GoalTz = built?.Tz ?? 0;
                e.RepathTick = Tick + RepathTicks;
                e.Path.Clear();
                e.Waypoint = 0;
                float px, pz;
                if (target != null) { px = target.Move.X; pz = target.Move.Z; }
                else if (built != null) { px = built.CentreX; pz = built.CentreZ; }
                else HoldSpot(e, out px, out pz);
                FindPath(e, px, pz);
            }
            if (target == null && built == null) { e.BackingSince = 0; Hold(e, dt); return; }

            float gx, gz, dist;
            if (target != null)
            {
                gx = target.Move.X; gz = target.Move.Z;
                float dx = gx - e.X, dz = gz - e.Z;
                dist = MathF.Sqrt(dx * dx + dz * dz);
            }
            else
            {
                dist = Constructs.Distance(built!, e.X, e.Z, out gx, out gz) - built!.Def.Radius;
            }

            // A carrier nearer than keepDistance: it backs off, and only shoots when that has not shaken them off.
            if (target != null && dist < e.Def.KeepDistance)
            {
                if (e.BackingSince == 0) e.BackingSince = Tick;
                if (Tick - e.BackingSince < BackOffTicks)
                {
                    BackOff(e, gx, gz, e.Speed * dt);
                    return;
                }
            }
            else e.BackingSince = 0;

            Face(e, gx, gz);
            float reach = e.Def.AttackRange + e.Radius;
            if (dist <= reach)
            {
                if (Tick < e.NextAttackTick) return;
                e.State = EnemyState.Windup;
                e.StateEndTick = Tick + _combat.WindupTicks;
                e.NextAttackTick = Tick + SimConstants.SecondsToTicks(e.Def.AttackRate);
                e.StrikeTx = built?.Tx ?? -1;
                e.StrikeTz = built?.Tz ?? 0;
                // Cornered or not, after this shot it backs off again.
                e.BackingSince = 0;
                return;
            }
            if (StrikeWhatBlocks(e, gx, gz)) return;
            Walk(e, gx, gz, e.Speed * dt);
        }

        private static void Face(Enemy e, float x, float z)
        {
            float dx = x - e.X, dz = z - e.Z;
            if (dx * dx + dz * dz > 1e-6f) e.Yaw = MathF.Atan2(-dx, -dz);
        }

        /// <summary>A step straight away from a point, or as near straight as the ground allows; it keeps facing the point.</summary>
        private void BackOff(Enemy e, float fromX, float fromZ, float step)
        {
            float dx = e.X - fromX, dz = e.Z - fromZ;
            float len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < 1e-3f) { dx = MathF.Sin(e.Yaw); dz = MathF.Cos(e.Yaw); len = 1f; }
            dx /= len; dz /= len;
            const float tile = SimConstants.TileSize;
            foreach (float turn in BackOffTurns)
            {
                float c = MathF.Cos(turn), s = MathF.Sin(turn);
                float ax = dx * c - dz * s, az = dx * s + dz * c;
                float ahead = e.Radius + step + 0.2f;
                int tx = (int)MathF.Floor((e.X + ax * ahead) / tile), tz = (int)MathF.Floor((e.Z + az * ahead) / tile);
                if (!Map.InBounds(tx, tz) || Map.IsNavBlocked(tx, tz) || Constructs.Blocks(tx, tz)) continue;
                e.X += ax * step;
                e.Z += az * step;
                break;
            }
            Face(e, fromX, fromZ);
        }

        private static readonly float[] BackOffTurns = { 0f, 0.6f, -0.6f, 1.2f, -1.2f, MathF.PI / 2f, -MathF.PI / 2f };

        /// <summary>
        /// The ranged raider's windup is over: the shot goes at the construct it was for, or at its
        /// carrier if they are still within its reach — a wall or gate between them takes it.
        /// </summary>
        private void Loose(Enemy e)
        {
            float reach = e.Def.AttackRange + e.Radius + WindupSlack;
            if (e.StrikeTx >= 0)
            {
                var built = Constructs.At(e.StrikeTx, e.StrikeTz);
                e.StrikeTx = -1;
                if (built == null) return;
                float d = Constructs.Distance(built, e.X, e.Z, out float nx, out float nz);
                if (d > reach + built.Def.Radius) return;
                Events.Add(new EnemyShotEvent { Kind = EnemyShotKind.Arrow, FromX = e.X, FromZ = e.Z, ToX = nx, ToZ = nz, AtConstruct = true });
                DamageConstruct(built, e.Damage);
                return;
            }
            var target = GetPlayer(e.Target);
            if (target == null || target.Dead) return;
            float dx = target.Move.X - e.X, dz = target.Move.Z - e.Z;
            if (dx * dx + dz * dz > reach * reach) return;
            var cover = Constructs.Between(e.X, e.Z, target.Move.X, target.Move.Z);
            if (cover != null)
            {
                Constructs.Distance(cover, e.X, e.Z, out float cx, out float cz);
                Events.Add(new EnemyShotEvent { Kind = EnemyShotKind.Arrow, FromX = e.X, FromZ = e.Z, ToX = cx, ToZ = cz, AtConstruct = true });
                DamageConstruct(cover, e.Damage);
                return;
            }
            Events.Add(new EnemyShotEvent { Kind = EnemyShotKind.Arrow, FromX = e.X, FromZ = e.Z, ToX = target.Move.X, ToZ = target.Move.Z });
            DamagePlayer(target, e.Damage, e.Def.Index);
        }
    }
}
