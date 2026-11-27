using System;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Doc 05 §2's pushes_constructs trait, the Tank: it walks at the first of its targetPriority
    /// within aggroRange straight through what is built — a belt, a splitter, a merger or a pipe in
    /// its way it crushes; a one-tile construct with nothing in it it shoves a tile on, once a
    /// second, where there is room for it; anything else in its way, and its target, it strikes.
    /// It goes for no carrier (design choice 136).
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>A Tank shoves what stands in its way this often.</summary>
        private static readonly uint PushTicks = SimConstants.SecondsToTicks(1.0);

        /// <summary>One tick of a raider that pushes constructs, not winding up or fleeing.</summary>
        private void StepPusher(Enemy e, float dt)
        {
            CrushUnder(e);
            if (!ChooseTarget(e, dt, out var target, out var built)) return;
            TargetPoint(e, target, built!, out float gx, out float gz, out float dist);
            if (dist <= e.Def.AttackRange + e.Radius)
            {
                Face(e, gx, gz);
                if (Tick < e.NextAttackTick) return;
                e.State = EnemyState.Windup;
                e.StateEndTick = Tick + _combat.WindupTicks;
                e.NextAttackTick = Tick + SimConstants.SecondsToTicks(e.Def.AttackRate);
                e.StrikeTx = built?.Tx ?? (target == null ? PostOfficeStrike : -1);
                e.StrikeTz = built?.Tz ?? 0;
                return;
            }
            if (target == null && built == null) PostOfficeGoal(e.X, e.Z, out gx, out gz);
            if (PushWhatBlocks(e, gx, gz)) return;
            Walk(e, gx, gz, e.Speed * dt);
        }

        /// <summary>Doc 05 §2 "destroys belts in path": a belt, a splitter, a merger or a pipe under it is crushed (they stop no walker).</summary>
        private void CrushUnder(Enemy e)
        {
            if (Constructs.All.Count == 0) return;
            const float tile = SimConstants.TileSize;
            var under = Constructs.At((int)MathF.Floor(e.X / tile), (int)MathF.Floor(e.Z / tile));
            if (under != null && Crushed(under.Def)) DamageConstruct(under, under.Hp + 1f);
        }

        private static bool Crushed(BuildingDef def) => def.Carries || def.PipeRole != PipeRole.None;

        /// <summary>What is built in its way, crushed, shoved or struck. True if it stops for it this tick.</summary>
        private bool PushWhatBlocks(Enemy e, float goalX, float goalZ)
        {
            if (Constructs.All.Count == 0) return false;
            WalkPoint(e, goalX, goalZ, out float wx, out float wz);
            float dx = wx - e.X, dz = wz - e.Z, len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < 1e-3f) return false;
            dx /= len; dz /= len;
            var built = Constructs.Ahead(e.X, e.Z, dx, dz, e.Radius + StrikeReach);
            if (built == null) return false;
            Face(e, built.CentreX, built.CentreZ);
            var def = built.Def;
            // Doc 05 §2 "destroys belts in path".
            if (Crushed(def))
            {
                DamageConstruct(built, built.Hp + 1f);
                return true;
            }
            if (Tick < e.PushReadyTick) return true;
            if (def.TileCount <= 1 && built.ContainerId == 0 && !def.HasBay)
            {
                // A tile on along the way it walks, the nearer of the two axes.
                int sx = MathF.Abs(dx) >= MathF.Abs(dz) ? Math.Sign(dx) : 0, sz = sx == 0 ? Math.Sign(dz) : 0;
                int tx = built.Tx + sx, tz = built.Tz + sz;
                if (BuildRules.CheckTile(Map, Constructs, tx, tz, def.OnStreet) == OpResult.Ok && !TileOccupied(tx, tz))
                {
                    Shove(e, built, tx, tz);
                    e.PushReadyTick = Tick + PushTicks;
                    return true;
                }
            }
            // No room to shove it, or too big: it is struck down.
            if (Tick < e.NextAttackTick) return true;
            e.State = EnemyState.Windup;
            e.StateEndTick = Tick + _combat.WindupTicks;
            e.NextAttackTick = Tick + SimConstants.SecondsToTicks(e.Def.AttackRate);
            e.StrikeTx = built.Tx;
            e.StrikeTz = built.Tz;
            return true;
        }

        /// <summary>A construct moved to another tile as it was, its health kept; the clients see it taken away and put up there.</summary>
        private void Shove(Enemy e, Construct built, int tx, int tz)
        {
            int hp = built.Hp;
            Events.Add(new ConstructPushedEvent { FromX = (ushort)built.Tx, FromY = (ushort)built.Tz, X = (ushort)tx, Y = (ushort)tz, Enemy = e.Def.Index });
            RemoveConstruct(built, EntityId.None, destroyed: false);
            var moved = Erect(built.Def, tx, tz, built.Rotation, EntityId.None);
            if (hp < moved.Hp)
            {
                Constructs.SetHp(moved, hp);
                Events.Add(new ConstructDamagedEvent { X = (ushort)tx, Y = (ushort)tz, Hp = (ushort)hp });
            }
        }
    }
}
