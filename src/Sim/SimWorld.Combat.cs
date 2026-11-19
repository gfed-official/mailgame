using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// The fight (doc 05): carriers' health, the hammer and the mail cannon, the raiders, the night
    /// raid's schedule and what the fallen leave behind. All of it server-side; clients see enemies
    /// in snapshots and everything else as events.
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>Balance numbers of the fight, in ticks and metres.</summary>
        private sealed class CombatTuning
        {
            public readonly float PlayerHp, RegenPerTick;
            public readonly uint RegenDelayTicks;
            public readonly float RespawnSeconds;
            public readonly float HammerDamage, HammerRange, HammerHalfArc, HammerKnockback;
            public readonly uint HammerCooldownTicks;
            public readonly float CannonDamage, CannonBlastRadius, CannonFuseRadius, CannonRange, CannonStep;
            public readonly uint CannonCooldownTicks;
            public readonly uint WindupTicks, FleeTicks, LootDespawnTicks, BagDespawnTicks;
            public readonly float FleeSpeedMult, LootPickupRange;
            public readonly uint ProtectionTicks, ProvokedTicks;
            public readonly float RaidStandoff, LeashMult, WaitDistance, YardRadius;
            public readonly int MaxAttackers;

            public CombatTuning(Balance b)
            {
                PlayerHp = b.GetFloat("playerHp");
                RegenPerTick = b.GetFloat("hpRegenPerSecond") * SimConstants.TickDt;
                RegenDelayTicks = SimConstants.SecondsToTicks(b.GetDouble("hpRegenDelaySeconds"));
                RespawnSeconds = b.GetFloat("respawnSeconds");
                HammerDamage = b.GetFloat("hammerDamage");
                HammerRange = b.GetFloat("hammerRange");
                HammerHalfArc = b.GetFloat("hammerArcDegrees") * MathF.PI / 360f;
                HammerKnockback = b.GetFloat("hammerKnockback");
                HammerCooldownTicks = SimConstants.SecondsToTicks(b.GetDouble("hammerCooldownSeconds"));
                CannonDamage = b.GetFloat("cannonDamage");
                CannonBlastRadius = b.GetFloat("cannonBlastRadius");
                CannonFuseRadius = b.GetFloat("cannonFuseRadius");
                CannonRange = b.GetFloat("cannonRange");
                CannonStep = b.GetFloat("cannonSpeed") * SimConstants.TickDt;
                CannonCooldownTicks = SimConstants.SecondsToTicks(b.GetDouble("cannonCooldownSeconds"));
                WindupTicks = SimConstants.SecondsToTicks(b.GetDouble("enemyWindupSeconds"));
                FleeTicks = SimConstants.SecondsToTicks(b.GetDouble("enemyFleeSeconds"));
                FleeSpeedMult = b.GetFloat("enemyFleeSpeedMult");
                LootDespawnTicks = SimConstants.SecondsToTicks(b.GetDouble("lootDespawnSeconds"));
                LootPickupRange = b.GetFloat("lootPickupRange");
                BagDespawnTicks = SimConstants.SecondsToTicks(b.GetDouble("deathBagDespawnSeconds"));
                ProtectionTicks = SimConstants.SecondsToTicks(b.GetDouble("respawnProtectionSeconds"));
                ProvokedTicks = SimConstants.SecondsToTicks(b.GetDouble("enemyProvokedSeconds"));
                YardRadius = b.GetFloat("respawnYardMetres");
                RaidStandoff = b.GetFloat("raidStandoffMetres");
                LeashMult = b.GetFloat("enemyLeashMult");
                MaxAttackers = b.GetInt("maxAttackersPerCarrier");
                WaitDistance = b.GetFloat("attackerWaitMetres");
            }
        }

        /// <summary>Enemies look for a new target and a new path this often, each on its own tick.</summary>
        private const uint RepathTicks = 15;
        /// <summary>A blow still lands on a target that backed off less than this during the windup.</summary>
        private const float WindupSlack = 0.6f;
        /// <summary>Knockback travels at this speed.</summary>
        private const float PushSpeed = 12f;
        /// <summary>The raiders' line: lanes across the street, ranks over this many metres towards the gate.</summary>
        private const int HoldLanes = 4, HoldRanks = 6;
        private const float HoldDeep = 6f;
        /// <summary>A raider this near its place on the line stands.</summary>
        private const float HoldSlack = 0.4f;
        /// <summary>A cannon package that comes this close to an enemy's body has hit it.</summary>
        private const float ShotRadius = 0.35f;
        private const uint LootSettleTicks = 15;
        private const uint LootRetryTicks = 30;

        private readonly CombatTuning _combat;
        private readonly List<Enemy> _enemies = new List<Enemy>();
        private readonly List<LootBundle> _loot = new List<LootBundle>();
        private readonly List<Shot> _shots = new List<Shot>();
        private readonly TilePathfinder.Scratch _navScratch = new TilePathfinder.Scratch();
        private readonly List<KeyValuePair<EnemyDef, int>> _affordable = new List<KeyValuePair<EnemyDef, int>>();
        private RngStream _waveRng = null!;
        private RngStream _lootRng = null!;

        private uint _raidWarnTick, _raidStartTick, _fleeEndTick;
        /// <summary>The raid under way or to come was asked for at the shift's start (<see cref="PlaytestFlags.RaidNow"/>).</summary>
        private bool _raidAtStart;
        /// <summary>The shift whose budget and enemies the raid has: the running one, unless that has no raid of its own.</summary>
        private int _raidShift;
        private int _raidPulse;
        private double _raidBudget, _raidCarry;

        public IReadOnlyList<Enemy> Enemies => _enemies;
        public IReadOnlyList<LootBundle> Loot => _loot;
        public IReadOnlyList<Shot> Shots => _shots;
        public RaidState Raid { get; private set; }
        /// <summary>The tick of the raid's first spawn this shift, 0 if the shift has no raid.</summary>
        public uint RaidStartTick => _raidStartTick;
        /// <summary>The tick the last <see cref="RaidChangedEvent"/> carried, for who joins after it.</summary>
        public uint RaidTick { get; private set; }
        /// <summary>The whole budget of the raid under way (doc 05 §3.2), set when it starts.</summary>
        public double RaidBudget => _raidBudget;
        /// <summary>Pulses of the raid under way that have come in.</summary>
        public int RaidPulsesSpawned => _raidPulse;
        public int EnemiesSpawnedTotal { get; private set; }
        public int EnemiesKilledTotal { get; private set; }

        // --- tick -----------------------------------------------------------------------------

        private void StepCombat()
        {
            StepHealth();
            StepRaid();
            StepShots();
            StepEnemies();
            StepLoot();
            StepBags();
        }

        private void StepHealth()
        {
            foreach (var p in _players)
            {
                if (p.Dead)
                {
                    if (Tick >= p.RespawnTick) Respawn(p);
                    continue;
                }
                // Out of the yard once its time is up, the protection does not come back by walking in
                // again; and with no raider about there is nothing to be protected from.
                if (p.ProtectedInYard && Tick >= p.ProtectedUntilTick && (_enemies.Count == 0 || !InYard(p))) p.ProtectedInYard = false;
                if (p.Hp < p.MaxHp && Tick - p.LastDamageTick >= _combat.RegenDelayTicks)
                    p.Hp = MathF.Min(p.MaxHp, p.Hp + _combat.RegenPerTick);
                StepHealing(p);
            }
        }

        // --- carriers ---------------------------------------------------------------------------

        private void SeatPosition(byte slot, out float x, out float z)
        {
            // Seats fan out around the pad so players do not spawn inside each other.
            x = Map.PostOffice.SpawnPadX + (slot % 2) * 1.0f - 0.5f;
            z = Map.PostOffice.SpawnPadZ + (slot / 2) * 1.0f - 1.5f;
        }

        /// <summary>
        /// Hurts a carrier; at 0 HP they are down until the respawn and what they carried lies in
        /// a bag where they fell (doc 05 §1.1).
        /// </summary>
        public void DamagePlayer(Player p, float damage, byte enemyKind = 0)
        {
            if (p.Dead || p.Away || damage <= 0f || Protected(p)) return;
            p.LastDamageTick = Tick;
            p.Hp -= damage;
            if (p.Hp > 0f) return;
            p.Hp = 0f;
            StopHealing(p);
            p.Move.Dead = true;
            p.RespawnTick = Tick + Math.Max(1, SimConstants.SecondsToTicks(StatFor(p, Stat.RespawnSeconds, _combat.RespawnSeconds)));
            CloseExternal(p, notify: true);
            Events.Add(new PlayerDiedEvent { Player = p.Id, RespawnTick = p.RespawnTick, Killer = enemyKind });
            DropBag(p, keepHotbar: StatFor(p, Stat.KeepHotbarOnDeath, 0f) > 0f);
        }

        /// <summary>
        /// Back on their feet at the Post Office. In a fight they stand up protected (nit 17):
        /// nothing hurts them and no raider goes for them for a few seconds, and after those for
        /// as long as they stay in the yard — until they hurt a raider themselves.
        /// </summary>
        private void Respawn(Player p, bool protect = true)
        {
            SeatPosition(p.Slot, out float x, out float z);
            p.Move = new PlayerMoveState { X = x, Z = z, Y = Map.HeightAt(x, z), Grounded = true, Yaw = p.Move.Yaw };
            p.Hp = p.MaxHp;
            p.LastDamageTick = 0;
            p.ProtectedUntilTick = protect ? Tick + _combat.ProtectionTicks : 0;
            p.ProtectedInYard = protect;
            Events.Add(new PlayerRespawnedEvent { Player = p.Id, ProtectedUntilTick = p.ProtectedUntilTick });
            // What they carried lies in the bag where they fell.
            GivePlaytestPackages(p);
        }

        /// <summary>True while the carrier's protection after a respawn holds.</summary>
        public bool Protected(Player p) => !p.Dead && (Tick < p.ProtectedUntilTick || (p.ProtectedInYard && InYard(p)));

        /// <summary>The Post Office's yard: respawnYardMetres around the pad, the Intake within it.</summary>
        public bool InYard(Player p)
        {
            float dx = p.Move.X - Map.PostOffice.SpawnPadX, dz = p.Move.Z - Map.PostOffice.SpawnPadZ;
            return dx * dx + dz * dz <= _combat.YardRadius * _combat.YardRadius;
        }

        private static void EndProtection(Player p)
        {
            p.ProtectedUntilTick = 0;
            p.ProtectedInYard = false;
        }

        private void HandleAttackInput(Player p, in PlayerInput input)
        {
            if (p.Dead) return;
            if (input.Has(InputButtons.Attack) && Tick >= p.HammerReadyTick) SwingHammer(p);
            if (input.Has(InputButtons.AltAttack) && Tick >= p.CannonReadyTick) FireCannon(p);
        }

        /// <summary>
        /// The hammer (the Mail Bat of doc 05 §1.2): everything in a 60° arc within reach is hit
        /// and shoved back. The arc is widened by each enemy's own girth.
        /// </summary>
        private void SwingHammer(Player p)
        {
            p.HammerReadyTick = Tick + _combat.HammerCooldownTicks;
            float yaw = PlayerInput.YawToRadians(p.Move.Yaw);
            float fx = -MathF.Sin(yaw), fz = -MathF.Cos(yaw);
            float damage = StatFor(p, Stat.MeleeDamage, _combat.HammerDamage);
            float knockback = StatFor(p, Stat.MeleeKnockback, _combat.HammerKnockback);
            int hits = 0;
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var e = _enemies[i];
                float dx = e.X - p.Move.X, dz = e.Z - p.Move.Z;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist > _combat.HammerRange + e.Radius) continue;
                if (dist > e.Radius)
                {
                    float angle = MathF.Acos(Math.Clamp((dx * fx + dz * fz) / dist, -1f, 1f));
                    if (angle > _combat.HammerHalfArc + MathF.Asin(e.Radius / dist)) continue;
                    e.PushX += dx / dist * knockback;
                    e.PushZ += dz / dist * knockback;
                }
                hits++;
                DamageEnemy(e, damage, p);
            }
            Events.Add(new AttackEvent
            {
                Player = p.Id, Weapon = Weapon.Hammer, X = p.Move.X, Z = p.Move.Z, Yaw = p.Move.Yaw, Hits = (byte)Math.Min(hits, 255),
            });
        }

        /// <summary>
        /// The mail cannon (doc 05 §1.2): fires a Small Package out of the carrier's own mail. The
        /// package is gone for good, with no misdelivery penalty. Without one, nothing happens.
        /// </summary>
        private void FireCannon(Player p)
        {
            if (!TakeAmmo(p, p.Hotbar) && !TakeAmmo(p, p.Inventory)) return;
            p.CannonReadyTick = Tick + _combat.CannonCooldownTicks;
            float yaw = PlayerInput.YawToRadians(p.Move.Yaw);
            float fx = -MathF.Sin(yaw), fz = -MathF.Cos(yaw);
            _shots.Add(new Shot { Owner = p.Id, X = p.Move.X + fx * 0.5f, Z = p.Move.Z + fz * 0.5f, DirX = fx, DirZ = fz });
            Events.Add(new AttackEvent { Player = p.Id, Weapon = Weapon.Cannon, X = p.Move.X, Z = p.Move.Z, Yaw = p.Move.Yaw });
        }

        private bool TakeAmmo(Player p, GridContainer container)
        {
            GridEntry? found = null;
            foreach (var e in container.Entries)
            {
                if (e.Stack.Kind != StackKind.Mail || !Content.MailKinds[e.Stack.DefIndex].CannonAmmo) continue;
                // The lowest entry id: the package carried longest goes first, whatever the dictionary's order.
                if (found == null || e.EntryId < found.EntryId) found = e;
            }
            if (found == null) return false;
            var mail = found.Stack.Mail;
            mail.RemoveAt(mail.Count - 1);
            if (mail.Count == 0) container.Remove(found.EntryId);
            else container.MarkDirty(found.EntryId);
            CommitContainer(container.Id);
            return true;
        }

        /// <summary>True if the carrier has a package the cannon can fire.</summary>
        public bool HasAmmo(Player p) => HasAmmo(p.Hotbar) || HasAmmo(p.Inventory);

        private bool HasAmmo(GridContainer container) => CountAmmo(container) > 0;

        private int CountAmmo(GridContainer container)
        {
            int n = 0;
            foreach (var e in container.Entries)
                if (e.Stack.Kind == StackKind.Mail && Content.MailKinds[e.Stack.DefIndex].CannonAmmo) n += e.Stack.Count;
            return n;
        }

        // --- the cannon's packages --------------------------------------------------------------

        private void StepShots()
        {
            const float tile = SimConstants.TileSize;
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                var s = _shots[i];
                float left = _combat.CannonStep;
                bool done = false;
                while (left > 0f && !done)
                {
                    float step = MathF.Min(left, 0.4f);
                    left -= step;
                    float nx = s.X + s.DirX * step, nz = s.Z + s.DirZ * step;
                    if (Map.IsSolid((int)MathF.Floor(nx / tile), (int)MathF.Floor(nz / tile))) { done = true; break; }
                    s.X = nx; s.Z = nz;
                    s.Travelled += step;
                    if (s.Travelled >= _combat.CannonRange) done = true;
                    // A package goes off on a raider, and also beside one: at the point where it is
                    // nearest to a raider it would pass within the fuse's reach, so the blast has them.
                    foreach (var e in _enemies)
                    {
                        float dx = e.X - s.X, dz = e.Z - s.Z, distSq = dx * dx + dz * dz;
                        float touch = e.Radius + ShotRadius, fuse = e.Radius + _combat.CannonFuseRadius;
                        if (distSq <= touch * touch || (distSq <= fuse * fuse && dx * s.DirX + dz * s.DirZ <= 0f)) { done = true; break; }
                    }
                }
                if (!done) continue;
                _shots.RemoveAt(i);
                Blast(s.X, s.Z, GetPlayer(s.Owner));
            }
        }

        private void Blast(float x, float z, Player? owner)
        {
            int hits = 0;
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var e = _enemies[i];
                float dx = e.X - x, dz = e.Z - z;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist > _combat.CannonBlastRadius + e.Radius) continue;
                if (dist > 0.05f)
                {
                    e.PushX += dx / dist;
                    e.PushZ += dz / dist;
                }
                hits++;
                DamageEnemy(e, _combat.CannonDamage, owner);
            }
            Events.Add(new BlastEvent { X = x, Z = z, Hits = (byte)Math.Min(hits, 255) });
        }

        // --- enemies ----------------------------------------------------------------------------

        /// <summary>
        /// Puts an enemy into the world (the raid's pulses; tests and the smoke directly). Null at
        /// the cap on living enemies.
        /// </summary>
        public Enemy? SpawnEnemy(EnemyDef def, float x, float z, bool mega = false)
        {
            var waves = Content.Waves;
            if (_enemies.Count >= (waves.MaxAlive > 0 ? waves.MaxAlive : 255)) return null;
            var e = new Enemy
            {
                Id = _entityIds.Allocate(EntityClass.Enemy),
                Def = def,
                Mega = mega,
                X = x, Z = z,
                Yaw = MathF.PI / 2f,
                MaxHp = def.Hp * (mega ? waves.MegaHpMult : 1f),
                Damage = def.Damage * (mega ? waves.MegaDamageMult : 1f),
                Speed = def.Speed * (mega ? waves.MegaSpeedMult : 1f),
                // A Mega is drawn larger; its body in the sim stays within what the props are indexed for.
                Radius = MathF.Min(def.Radius * (mega ? waves.MegaScale : 1f), WorldMap.WalkerReach),
            };
            e.Hp = e.MaxHp;
            // Each enemy thinks on its own tick, so a pulse does not path all at once.
            e.RepathTick = Tick + 1 + e.Id.Counter % RepathTicks;
            PlayerMovement.ResolveCollisions(ref e.X, ref e.Z, 0f, Map, e.Radius);
            _enemies.Add(e);
            EnemiesSpawnedTotal++;
            return e;
        }

        /// <summary>A player has left: their shots in the air, the blows they dealt and their bags are nobody's now.</summary>
        private void ForgetInCombat(EntityId player)
        {
            foreach (var e in _enemies)
            {
                if (e.Target == player) e.Target = EntityId.None;
                if (e.LastHitBy == player) e.LastHitBy = EntityId.None;
            }
            foreach (var s in _shots)
                if (s.Owner == player) s.Owner = EntityId.None;
            DisownBags(player);
        }

        public Enemy? GetEnemy(EntityId id)
        {
            foreach (var e in _enemies) if (e.Id == id) return e;
            return null;
        }

        /// <summary>Hurts an enemy; at 0 HP it falls and leaves its loot.</summary>
        public void DamageEnemy(Enemy e, float damage, Player? attacker)
        {
            if (e.Hp <= 0f) return;
            e.Hp -= damage;
            if (attacker != null)
            {
                e.LastHitBy = attacker.Id;
                // Who hurts a raider is in the fight again; a swing at the air is not a blow.
                EndProtection(attacker);
                // Whoever hurt it has its attention for a while, from however far the blow came.
                e.ProvokedUntilTick = Tick + _combat.ProvokedTicks;
            }
            if (e.Hp > 0f) return;
            _enemies.Remove(e);
            EnemiesKilledTotal++;
            var killer = GetPlayer(e.LastHitBy);
            if (killer != null) killer.Kills++;
            Events.Add(new EnemyDiedEvent { Enemy = e.Id, Kind = e.Def.Index, Mega = e.Mega, X = e.X, Z = e.Z, Killer = e.LastHitBy });
            DropLoot(e);
        }

        private void StepEnemies()
        {
            if (_enemies.Count == 0) return;
            const float dt = SimConstants.TickDt;
            bool fleeOver = _fleeEndTick != 0 && Tick >= _fleeEndTick;
            RankAttackers();
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var e = _enemies[i];
                StepPush(e, dt);
                switch (e.State)
                {
                    case EnemyState.Flee:
                    {
                        RaidGate(out float gx, out float gz);
                        float dx = gx - e.X, dz = gz - e.Z;
                        if (fleeOver || dx * dx + dz * dz < 1.5f * 1.5f) { _enemies.RemoveAt(i); continue; }
                        Walk(e, gx, gz, e.Speed * _combat.FleeSpeedMult * dt);
                        break;
                    }
                    case EnemyState.Windup:
                    {
                        if (Tick < e.StateEndTick) break;
                        e.State = EnemyState.Chase;
                        var target = GetPlayer(e.Target);
                        if (target == null || target.Dead) break;
                        float dx = target.Move.X - e.X, dz = target.Move.Z - e.Z;
                        float reach = e.Def.AttackRange + e.Radius + WindupSlack;
                        if (dx * dx + dz * dz <= reach * reach) DamagePlayer(target, e.Damage, e.Def.Index);
                        break;
                    }
                    default:
                        StepChase(e, dt);
                        break;
                }
            }

            // Bodies do not stack: overlapping enemies are pushed apart, then kept out of walls.
            for (int i = 0; i < _enemies.Count; i++)
            {
                var a = _enemies[i];
                for (int j = i + 1; j < _enemies.Count; j++)
                {
                    var b = _enemies[j];
                    float dx = b.X - a.X, dz = b.Z - a.Z, min = a.Radius + b.Radius;
                    float distSq = dx * dx + dz * dz;
                    if (distSq >= min * min) continue;
                    float dist = MathF.Sqrt(distSq);
                    if (dist < 1e-4f)
                    {
                        // Exactly on top of each other: apart along a direction the pair's ids pick.
                        float angle = (a.Id.Counter * 7 + b.Id.Counter * 13) % 16 * (MathF.PI / 8f);
                        dx = MathF.Cos(angle); dz = MathF.Sin(angle); dist = 1f;
                    }
                    float push = (min - dist) * 0.5f / dist;
                    a.X -= dx * push; a.Z -= dz * push;
                    b.X += dx * push; b.Z += dz * push;
                }
            }
            foreach (var e in _enemies) PlayerMovement.ResolveCollisions(ref e.X, ref e.Z, 0f, Map, e.Radius);
        }

        /// <summary>
        /// Among the raiders after one carrier only the nearest few close in; the rest wait their
        /// turn a few steps back, so a carrier is fought rather than buried (nit 17).
        /// </summary>
        private void RankAttackers()
        {
            foreach (var e in _enemies)
            {
                e.Waiting = false;
                var target = e.State == EnemyState.Flee ? null : GetPlayer(e.Target);
                if (target == null) continue;
                float own = DistanceSq(e, target);
                int nearer = 0;
                foreach (var other in _enemies)
                {
                    if (other == e || other.Target != e.Target || other.State == EnemyState.Flee) continue;
                    float d = DistanceSq(other, target);
                    if (d < own || (d == own && other.Id.Value < e.Id.Value)) nearer++;
                }
                e.Waiting = nearer >= _combat.MaxAttackers;
            }
        }

        private static float DistanceSq(Enemy e, Player p)
        {
            float dx = p.Move.X - e.X, dz = p.Move.Z - e.Z;
            return dx * dx + dz * dz;
        }

        /// <summary>
        /// Whether an enemy may be after a carrier at most <paramref name="range"/> from a point:
        /// not one who is down or stands protected after a respawn; one who hurt it lately, from
        /// any distance.
        /// </summary>
        private bool ValidTarget(Enemy e, Player p, float range, float fromX, float fromZ)
        {
            if (p.Dead || p.Away || Protected(p)) return false;
            if (e.LastHitBy == p.Id && Tick < e.ProvokedUntilTick) return true;
            float dx = p.Move.X - fromX, dz = p.Move.Z - fromZ;
            return dx * dx + dz * dz <= range * range;
        }

        /// <summary>Doc 05 §2: the nearest valid target within aggroRange.</summary>
        private Player? Acquire(Enemy e)
        {
            Player? best = null;
            float bestSq = float.MaxValue;
            foreach (var p in _players)
            {
                if (!ValidTarget(e, p, e.Def.AggroRange, e.X, e.Z)) continue;
                float sq = DistanceSq(e, p);
                if (sq >= bestSq) continue;
                best = p;
                bestSq = sq;
            }
            return best;
        }

        private void StepChase(Enemy e, float dt)
        {
            // Whoever it is after is let go once they have got well away from where it last had them
            // within aggroRange (its own way round a house does not count as their getting away).
            var target = GetPlayer(e.Target);
            if (target != null)
            {
                if (DistanceSq(e, target) <= e.Def.AggroRange * e.Def.AggroRange) { e.SeenX = e.X; e.SeenZ = e.Z; }
                if (!ValidTarget(e, target, e.Def.AggroRange * _combat.LeashMult, e.SeenX, e.SeenZ)) target = null;
            }
            if (target == null || Tick >= e.RepathTick)
            {
                var nearest = Acquire(e);
                if (nearest != null && nearest != target)
                {
                    target = nearest;
                    e.SeenX = e.X; e.SeenZ = e.Z;
                }
                e.Target = target?.Id ?? EntityId.None;
                e.RepathTick = Tick + RepathTicks;
                e.Path.Clear();
                e.Waypoint = 0;
                float gx, gz;
                if (target != null) { gx = target.Move.X; gz = target.Move.Z; }
                else HoldSpot(e, out gx, out gz);
                if (!LineClear(e.X, e.Z, gx, gz)) TilePathfinder.Find(Map, e.X, e.Z, gx, gz, e.Path, _navScratch);
            }
            if (target == null) { Hold(e, dt); return; }

            float dx = target.Move.X - e.X, dz = target.Move.Z - e.Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            float reach = e.Def.AttackRange + e.Radius;
            if (dist <= reach && Tick >= e.NextAttackTick)
            {
                e.State = EnemyState.Windup;
                e.StateEndTick = Tick + _combat.WindupTicks;
                e.NextAttackTick = Tick + SimConstants.SecondsToTicks(e.Def.AttackRate);
                if (dist > 1e-3f) e.Yaw = MathF.Atan2(-dx, -dz);
                return;
            }
            // Close enough to strike, or waiting for room at the carrier: hold the ground rather than walk in.
            if (dist <= reach * 0.7f || (e.Waiting && dist <= _combat.WaitDistance + e.Radius))
            {
                if (dist > 1e-3f) e.Yaw = MathF.Atan2(-dx, -dz);
                return;
            }
            Walk(e, target.Move.X, target.Move.Z, e.Speed * dt);
        }

        /// <summary>
        /// The raiders' line (nit 17): on the street, raidStandoffMetres from the Post Office's
        /// pad towards the gate. Farther than any aggroRange, so nobody is fought on the pad who
        /// did not pick the fight; the street between the Post Office and its mailboxes is theirs.
        /// </summary>
        public void RaidLine(out float x, out float z)
        {
            RaidGate(out float gx, out z);
            float px = Map.PostOffice.SpawnPadX, dz = z - Map.PostOffice.SpawnPadZ;
            float along = MathF.Sqrt(MathF.Max(0f, _combat.RaidStandoff * _combat.RaidStandoff - dz * dz));
            // On a street too short for the standoff the line is the gate's mouth.
            x = gx >= px ? MathF.Min(px + along, gx - HoldDeep) : MathF.Max(px - along, gx + HoldDeep);
        }

        /// <summary>Where on the line this raider stands: lanes across the street, ranks towards the gate.</summary>
        public void HoldSpot(Enemy e, out float x, out float z)
        {
            RaidLine(out x, out z);
            RaidGate(out float gx, out _);
            uint n = e.Id.Counter;
            x += (gx >= x ? 1f : -1f) * (n / HoldLanes % HoldRanks) * (HoldDeep / HoldRanks);
            z += ((n % HoldLanes) / (HoldLanes - 1f) * 2f - 1f) * StreetHalfWidth();
        }

        /// <summary>Half the first street's width, less a raider's shoulder.</summary>
        private float StreetHalfWidth() =>
            Map.Streets.Count > 0 ? Map.Streets[0].Rect.H * SimConstants.TileSize * 0.5f - 0.6f : 1.4f;

        /// <summary>With nobody to go for: to its place on the line, and there it watches the Post Office.</summary>
        private void Hold(Enemy e, float dt)
        {
            HoldSpot(e, out float hx, out float hz);
            float dx = hx - e.X, dz = hz - e.Z;
            if (dx * dx + dz * dz > HoldSlack * HoldSlack)
            {
                Walk(e, hx, hz, e.Speed * dt);
                return;
            }
            e.Yaw = MathF.Atan2(-(Map.PostOffice.SpawnPadX - e.X), -(Map.PostOffice.SpawnPadZ - e.Z));
        }

        /// <summary>One tick of walking at a point: along the tile path while it has one, straight otherwise.</summary>
        private void Walk(Enemy e, float goalX, float goalZ, float step)
        {
            float tx = goalX, tz = goalZ;
            while (e.Waypoint < e.Path.Count - 1)
            {
                float wx = e.Path[e.Waypoint].X - e.X, wz = e.Path[e.Waypoint].Z - e.Z;
                if (wx * wx + wz * wz > 0.6f * 0.6f) { tx = e.Path[e.Waypoint].X; tz = e.Path[e.Waypoint].Z; break; }
                e.Waypoint++;
            }
            float dx = tx - e.X, dz = tz - e.Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist < 1e-3f) return;
            if (step > dist) step = dist;
            e.X += dx / dist * step;
            e.Z += dz / dist * step;
            e.Yaw = MathF.Atan2(-dx, -dz);
        }

        private static void StepPush(Enemy e, float dt)
        {
            float left = MathF.Sqrt(e.PushX * e.PushX + e.PushZ * e.PushZ);
            if (left < 1e-3f) { e.PushX = e.PushZ = 0f; return; }
            float step = MathF.Min(left, PushSpeed * dt);
            e.X += e.PushX / left * step;
            e.Z += e.PushZ / left * step;
            float keep = (left - step) / left;
            e.PushX *= keep; e.PushZ *= keep;
        }

        private Player? NearestLivingPlayer(float x, float z, float range)
        {
            Player? best = null;
            float bestSq = range == float.MaxValue ? float.MaxValue : range * range;
            foreach (var p in _players)
            {
                if (p.Dead || p.Away) continue;
                float dx = p.Move.X - x, dz = p.Move.Z - z, sq = dx * dx + dz * dz;
                if (sq > bestSq || (best != null && sq == bestSq)) continue;
                best = p;
                bestSq = sq;
            }
            return best;
        }

        /// <summary>True if a walker can go straight from one point to the other without leaving the open tiles.</summary>
        private bool LineClear(float x0, float z0, float x1, float z1)
        {
            const float tile = SimConstants.TileSize;
            float dx = x1 - x0, dz = z1 - z0;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            int steps = (int)MathF.Ceiling(dist / 0.5f);
            for (int i = 1; i < steps; i++)
            {
                float t = i / (float)steps;
                if (Map.IsNavBlocked((int)MathF.Floor((x0 + dx * t) / tile), (int)MathF.Floor((z0 + dz * t) / tile))) return false;
            }
            return true;
        }

        // --- the raid ---------------------------------------------------------------------------

        /// <summary>Where the raiders come in and leave: the east end of the first street ("from the east").</summary>
        public void RaidGate(out float x, out float z)
        {
            x = Map.WidthM - 1.5f;
            z = Map.Streets.Count > 0 ? Map.Streets[0].Rect.CenterZ : Map.DepthM * 0.5f;
        }

        /// <summary>
        /// Called when Delivery starts: a shift with a wave budget gets a raid over its last
        /// raidWindowSeconds, announced warningSeconds before (doc 05 §3.1).
        /// </summary>
        private void ScheduleRaid()
        {
            _raidWarnTick = _raidStartTick = 0;
            _raidAtStart = false;
            _raidShift = Run.Shift;
            var waves = Content.Waves;
            if (!Settings.Raids || Content.Enemies.Count == 0) return;
            if (RaidsAtShiftStart)
            {
                // A shift without a raid of its own borrows the first one there is.
                while (_raidShift < ShiftCount && waves.BudgetFor(_raidShift) <= 0) _raidShift++;
                if (waves.BudgetFor(_raidShift) <= 0) return;
                _raidAtStart = true;
                _raidWarnTick = Tick + 1;
                _raidStartTick = Tick + Math.Max(2, SimConstants.SecondsToTicks(PlaytestMask.RaidNowSeconds));
                return;
            }
            if (waves.BudgetFor(Run.Shift) <= 0) return;
            uint length = Run.PhaseEndTick - Tick;
            uint window = Math.Min(SimConstants.SecondsToTicks(waves.RaidWindowSeconds), length - 1);
            uint warning = SimConstants.SecondsToTicks(TeamStat(Stat.RaidWarningSeconds, (float)waves.WarningSeconds));
            _raidStartTick = Run.PhaseEndTick - window;
            _raidWarnTick = _raidStartTick - Math.Min(warning, _raidStartTick - Tick - 1);
        }

        /// <summary>waveBudget(shift, n) of doc 05 §3.2 (no stamps yet).</summary>
        public static double WaveBudget(WaveDef waves, int shift, int players, int complaint)
        {
            if (players < 1) players = 1;
            double budget = waves.BudgetFor(shift) * (1 + waves.WaveScalePerExtraPlayer * (players - 1)) * (1 + complaint / 100.0);
            return players <= waves.SoloMaxPlayers ? budget * waves.SoloMult : budget;
        }

        private void StepRaid()
        {
            if (_raidStartTick == 0 || Run.Phase != RunPhase.Delivery) return;
            var waves = Content.Waves;
            if (Raid == RaidState.None && Tick >= _raidWarnTick) SetRaid(RaidState.Warning, _raidStartTick);
            if (Raid == RaidState.Warning && Tick >= _raidStartTick)
            {
                // The head count of the shift's start (doc 06 §6: not recalculated mid-shift).
                _raidBudget = WaveBudget(waves, _raidShift, Run.PlayerCount, Economy.Complaint);
                _raidCarry = 0;
                _raidPulse = 0;
                SetRaid(RaidState.Active, Run.PhaseEndTick);
            }
            if (Raid != RaidState.Active) return;
            uint interval = Math.Max(1, SimConstants.SecondsToTicks(waves.PulseIntervalSeconds));
            while (_raidPulse < waves.Pulses && Tick >= _raidStartTick + (uint)_raidPulse * interval)
            {
                SpawnPulse(waves, _raidPulse);
                _raidPulse++;
            }
        }

        /// <summary>
        /// One pulse (doc 05 §3.2): weighted picks until its share of the budget is spent; what is
        /// too little for any enemy carries over to the next pulse.
        /// </summary>
        private void SpawnPulse(WaveDef waves, int pulse)
        {
            bool finale = pulse == waves.Pulses - 1;
            double budget = _raidBudget / waves.Pulses * (finale ? waves.FinalePulseMult : 1.0) + _raidCarry;
            var weights = waves.WeightsFor(_raidShift);
            int megaPermille = (int)Math.Round((waves.MegaChanceBase + waves.MegaChancePerShift * Run.Shift) * 1000);
            // Doc 05 §3.2: the last pulse of the last shift always brings a Mega.
            bool forceMega = finale && Run.Shift >= ShiftCount;
            RaidGate(out float gx, out float gz);
            float halfStreet = StreetHalfWidth();
            while (_enemies.Count < waves.MaxAlive)
            {
                _affordable.Clear();
                int total = 0;
                foreach (var w in weights)
                {
                    if (w.Key.Cost > budget) continue;
                    _affordable.Add(w);
                    total += w.Value;
                }
                if (total == 0) break;
                int roll = _waveRng.NextInt(total);
                var def = _affordable[_affordable.Count - 1].Key;
                foreach (var w in _affordable)
                {
                    if (roll < w.Value) { def = w.Key; break; }
                    roll -= w.Value;
                }
                bool mega = forceMega || _waveRng.Chance(megaPermille);
                forceMega = false;
                // Spread over the mouth of the gate, so a pulse walks in as a crowd rather than a stack.
                float x = gx - _waveRng.NextInt(300) / 100f;
                float z = gz + (_waveRng.NextInt(2001) - 1000) / 1000f * halfStreet;
                if (SpawnEnemy(def, x, z, mega) == null) break;
                budget -= def.Cost;
            }
            _raidCarry = budget;
        }

        private void SetRaid(RaidState state, uint tick)
        {
            if (Raid == state) return;
            Raid = state;
            RaidTick = tick;
            Events.Add(new RaidChangedEvent { Raid = state, Tick = tick });
        }

        /// <summary>Doc 05 §3.4: when the shift clock ends every enemy runs for the gate and is gone 5 s later.</summary>
        private void EndRaid()
        {
            _raidWarnTick = _raidStartTick = 0;
            _raidAtStart = false;
            _shots.Clear();
            if (_enemies.Count == 0 && Raid == RaidState.None) return;
            _fleeEndTick = Tick + _combat.FleeTicks;
            RaidGate(out float gx, out float gz);
            foreach (var e in _enemies)
            {
                e.State = EnemyState.Flee;
                e.Path.Clear();
                e.Waypoint = 0;
                if (!LineClear(e.X, e.Z, gx, gz)) TilePathfinder.Find(Map, e.X, e.Z, gx, gz, e.Path, _navScratch);
            }
            if (Raid != RaidState.None) SetRaid(RaidState.Fleeing, _fleeEndTick);
        }

        /// <summary>
        /// Between shifts nothing of the fight is left (doc 05 §3.4: "Enemies never persist into
        /// Prep"): enemies, packages in flight and loot go, bags are returned, and whoever was down
        /// stands up.
        /// </summary>
        private void ClearCombat()
        {
            _enemies.Clear();
            _shots.Clear();
            foreach (var bundle in _loot) Events.Add(new LootGoneEvent { LootId = bundle.Id });
            _loot.Clear();
            ReturnAllBags();
            _raidWarnTick = _raidStartTick = _fleeEndTick = 0;
            _raidAtStart = false;
            SetRaid(RaidState.None, 0);
            foreach (var p in _players)
            {
                if (p.Dead) Respawn(p, protect: false);
                EndProtection(p);
            }
        }

        private void ResetCombatRng()
        {
            _waveRng = new RngStream(Settings.Seed, "waves");
            _lootRng = new RngStream(Settings.Seed, "loot");
        }

        // --- loot -------------------------------------------------------------------------------

        private void DropLoot(Enemy e)
        {
            int mult = e.Mega ? Math.Max(1, Content.Waves.MegaLootMult) : 1;
            var bundle = new LootBundle
            {
                X = e.X, Z = e.Z, EnemyKind = e.Def.Index, Mega = e.Mega,
                Coins = Math.Min(ushort.MaxValue, (e.Def.CoinsMax > 0 ? _lootRng.NextRange(e.Def.CoinsMin, e.Def.CoinsMax) : 0) * mult),
                PickupTick = Tick + LootSettleTicks,
                DespawnTick = Tick + _combat.LootDespawnTicks,
            };
            foreach (var drop in e.Def.Drops)
            {
                if (!_lootRng.Chance(drop.ChancePermille)) continue;
                bundle.Items.Add(new LootItem { Item = drop.Item.Index, Count = (byte)Math.Min(255, drop.Count * mult) });
            }
            if (bundle.Coins == 0 && bundle.Items.Count == 0) return;
            bundle.Id = _entityIds.Allocate(EntityClass.WorldItem).Value;
            _loot.Add(bundle);
            var ev = new LootDroppedEvent
            {
                LootId = bundle.Id, X = bundle.X, Z = bundle.Z, EnemyKind = bundle.EnemyKind, Mega = bundle.Mega, Coins = (ushort)bundle.Coins,
            };
            ev.Items.AddRange(bundle.Items);
            Events.Add(ev);
        }

        /// <summary>
        /// Loot goes to whoever walks up: coins into the team wallet, items into that carrier's
        /// inventory. What does not fit stays on the ground until it does, or until the despawn.
        /// </summary>
        private void StepLoot()
        {
            for (int i = _loot.Count - 1; i >= 0; i--)
            {
                var bundle = _loot[i];
                if (Tick >= bundle.DespawnTick)
                {
                    _loot.RemoveAt(i);
                    Events.Add(new LootGoneEvent { LootId = bundle.Id });
                    continue;
                }
                if (Tick < bundle.PickupTick) continue;
                var p = NearestLivingPlayer(bundle.X, bundle.Z, _combat.LootPickupRange);
                if (p == null) continue;

                var taken = new LootTakenEvent { LootId = bundle.Id, Player = p.Id, Coins = (ushort)bundle.Coins };
                Economy.AddLoot(bundle.Coins);
                p.LootCoins += bundle.Coins;
                bundle.Coins = 0;
                for (int k = bundle.Items.Count - 1; k >= 0; k--)
                {
                    var item = bundle.Items[k];
                    var stack = ItemStack.OfItem(Content.Items[item.Item], item.Count);
                    // Placed whole, the stack itself is in the grid now; otherwise what merged is gone from it.
                    int left = InventoryOps.AutoPlace(Content, p.Inventory, stack, _entryIds) ? 0 : stack.Count;
                    int got = item.Count - left;
                    if (got <= 0) continue;
                    taken.Items.Add(new LootItem { Item = item.Item, Count = (byte)got });
                    if (left == 0) bundle.Items.RemoveAt(k);
                    else bundle.Items[k] = new LootItem { Item = item.Item, Count = (byte)left };
                }
                if (taken.Items.Count > 0) CommitContainer(p.Inventory.Id);
                taken.Gone = bundle.Items.Count == 0;
                if (taken.Gone) _loot.RemoveAt(i);
                else bundle.PickupTick = Tick + LootRetryTicks;
                if (taken.Coins > 0 || taken.Items.Count > 0) Events.Add(taken);
                else if (taken.Gone) Events.Add(new LootGoneEvent { LootId = bundle.Id });
            }
        }
    }
}
