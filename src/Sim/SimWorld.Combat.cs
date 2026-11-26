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
        /// <summary>A raider this near to something built that is in its way strikes it.</summary>
        private const float StrikeReach = 0.3f;
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

        private CombatTuning _combat = null!;
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
        /// <summary>What of the budget is left to the pulses' rolls once the leads are bought (design choice 141).</summary>
        private double _raidRollBudget;
        /// <summary>The raid's leads, bought when it starts, by the pulse they come with (design choice 141).</summary>
        private readonly List<List<EnemyDef>> _raidLeads = new List<List<EnemyDef>>();
        /// <summary>The raid's ways in (doc 05 §3.2): the first half of its pulses by the one, then both; −1: none.</summary>
        private int _raidEdgeA = -1, _raidEdgeB = -1;
        private RngStream _spawnRng = null!;
        /// <summary>How many of each enemy the raid under way brings, against their caps.</summary>
        private readonly Dictionary<EnemyDef, int> _raidSpawnedOf = new Dictionary<EnemyDef, int>();

        public IReadOnlyList<Enemy> Enemies => _enemies;
        public IReadOnlyList<LootBundle> Loot => _loot;
        public IReadOnlyList<Shot> Shots => _shots;
        public RaidState Raid { get; private set; }
        /// <summary>The tick of the raid's first spawn this shift, 0 if the shift has no raid.</summary>
        public uint RaidStartTick => _raidStartTick;
        /// <summary>The tick the raid of this shift is announced by (Alarm Posts move it while it is to come); 0 without one.</summary>
        public uint RaidWarnTick => _raidWarnTick;
        /// <summary>The tick the last <see cref="RaidChangedEvent"/> carried, for who joins after it.</summary>
        public uint RaidTick { get; private set; }
        /// <summary>The whole budget of the raid under way (doc 05 §3.2), set when it starts.</summary>
        public double RaidBudget => _raidBudget;
        /// <summary>Pulses of the raid under way that have come in.</summary>
        public int RaidPulsesSpawned => _raidPulse;
        /// <summary>The way in of the raid's first pulses (an index into the map's spawn edges); −1 without a raid.</summary>
        public int RaidEdgeA => _raidEdgeA;
        /// <summary>The second way in of its later pulses; −1 if they come by the first too.</summary>
        public int RaidEdgeB => _raidEdgeB;
        public int EnemiesSpawnedTotal { get; private set; }
        public int EnemiesKilledTotal { get; private set; }

        // --- tick -----------------------------------------------------------------------------

        private void StepCombat()
        {
            StepPostOffice();
            StepHealth();
            StepRaid();
            StepShots();
            StepEnemies();
            StepTurrets();
            StepLoot();
            StepBags();
            RecordEnemyPositions();
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
                // Walked off the mailbox, they are in the raid like anybody else.
                if (p.ProtectedAtPark && !AtPark(p)) p.ProtectedAtPark = false;
                if (p.Hp < p.MaxHp && Tick - p.LastDamageTick >= _combat.RegenDelayTicks)
                    p.Hp = MathF.Min(p.MaxHp, p.Hp + _combat.RegenPerTick);
                StepHealing(p);
            }
        }

        // --- carriers ---------------------------------------------------------------------------

        private void SeatPosition(byte slot, out float x, out float z) => Map.PostOffice.Seat(slot, out x, out z);

        /// <summary>
        /// Hurts a carrier; at 0 HP they are down until the respawn and what they carried lies in
        /// a bag where they fell (doc 05 §1.1).
        /// </summary>
        public void DamagePlayer(Player p, float damage, byte enemyKind = 0)
        {
            if (p.Dead || p.Away || damage <= 0f || Protected(p) || GodMode) return;
            p.LastDamageTick = Tick;
            p.Hp -= damage;
            if (p.Hp > 0f) return;
            p.Hp = 0f;
            StopHealing(p);
            p.Move.Dead = true;
            // Gone down on the open water, they and their bag wash up on the nearest shore (design choice 113).
            if (p.Move.Riding && Map.IsWater((int)MathF.Floor(p.Move.X / SimConstants.TileSize), (int)MathF.Floor(p.Move.Z / SimConstants.TileSize))
                && PlayerMovement.Ashore(Map, Constructs, p.Move.X, p.Move.Z, out float ax, out float az))
            {
                p.Move.X = ax;
                p.Move.Z = az;
            }
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
        public bool Protected(Player p) => !p.Dead && (Tick < p.ProtectedUntilTick || (p.ProtectedInYard && InYard(p)) || (p.ProtectedAtPark && AtPark(p)));

        /// <summary>Still where a playtest parked them, before their mailbox.</summary>
        private static bool AtPark(Player p)
        {
            float dx = p.Move.X - p.ParkX, dz = p.Move.Z - p.ParkZ;
            return dx * dx + dz * dz <= PlaytestSpot.GuardRadius * PlaytestSpot.GuardRadius;
        }

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
            p.ProtectedAtPark = false;
        }

        private void HandleAttackInput(Player p, in PlayerInput input)
        {
            if (p.Dead) return;
            // At a turret's controls the attack is the gun's, and neither the hammer nor the cannon is in the hands.
            if (p.Turret != null)
            {
                if (input.Has(InputButtons.Attack)) FireOperatedTurret(p);
                return;
            }
            // The Slingshot in the hand looses a Stone instead of a swing (doc 05 §1.2); the cannon stays on the other button.
            if (HeldSlingshot(p) is { } sling)
            {
                p.RepairTarget = null;
                if (input.Has(InputButtons.Attack) && Tick >= p.SlingReadyTick) FireSlingshot(p, sling);
                if (input.Has(InputButtons.AltAttack) && Tick >= p.CannonReadyTick) FireCannon(p);
                return;
            }
            // The Repair Hammer in the hand mends what is hurt before it; with nothing hurt there, it swings (doc 05 §4).
            bool mending = input.Has(InputButtons.Attack) && StepRepair(p);
            if (!mending) p.RepairTarget = null;
            if (input.Has(InputButtons.Attack) && !mending && Tick >= p.HammerReadyTick) SwingHammer(p);
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
            // Doc 05 §2 Tank "Pickaxe deals 2×": the swing of one with a Pickaxe in the hand.
            bool pickaxe = HeldTool(p) is byte held && held > 0 && Content.Items[held - 1].Id == "pickaxe";
            float knockback = StatFor(p, Stat.MeleeKnockback, _combat.HammerKnockback);
            int hits = 0;
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var e = _enemies[i];
                // Doc 05 §2 Balloon: "Only ranged weapons and turrets can hit it".
                if (e.Def.Airborne) continue;
                // Doc 06 §4.3: where the swinger's client drew it, and its body 0.3 m the larger.
                Rewound(e, out float ex, out float ez);
                float dx = ex - p.Move.X, dz = ez - p.Move.Z, body = e.Radius + MeleeHitSlack;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist > _combat.HammerRange + body) continue;
                if (dist > body)
                {
                    float angle = MathF.Acos(Math.Clamp((dx * fx + dz * fz) / dist, -1f, 1f));
                    if (angle > _combat.HammerHalfArc + MathF.Asin(body / dist)) continue;
                }
                // Shoved away from the swinger from where it is now.
                float nx = e.X - p.Move.X, nz = e.Z - p.Move.Z, now = MathF.Sqrt(nx * nx + nz * nz);
                if (now > e.Radius)
                {
                    e.PushX += nx / now * knockback;
                    e.PushZ += nz / now * knockback;
                }
                hits++;
                DamageEnemy(e, pickaxe ? damage * e.Def.PickaxeMult : damage, p);
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
                BaseSpeed = def.Speed * (mega ? waves.MegaSpeedMult : 1f),
                Radius = waves.BodyRadius(def, mega),
            };
            e.Hp = e.MaxHp;
            e.Edge = NearestEdge(x, z);
            // Each enemy thinks on its own tick, so a pulse does not path all at once.
            e.RepathTick = Tick + 1 + e.Id.Counter % RepathTicks;
            if (!def.Airborne) PlayerMovement.ResolveCollisions(ref e.X, ref e.Z, 0f, Map, e.Radius, Constructs, carrier: false, jumper: def.JumpsWalls);
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
                if (e.ProvokedBy == player) e.ProvokedBy = EntityId.None;
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
                // Whoever hurt it has its attention for a while, from however far the blow came;
                // a siege raider's only from close by.
                if (!e.Def.Siege) e.ProvokedUntilTick = Tick + _combat.ProvokedTicks;
                else Provoke(e, attacker);
            }
            if (e.Hp > 0f) return;
            _enemies.Remove(e);
            EnemiesKilledTotal++;
            var killer = GetPlayer(e.LastHitBy);
            if (killer != null) killer.Kills++;
            Events.Add(new EnemyDiedEvent { Enemy = e.Id, Kind = e.Def.Index, Mega = e.Mega, X = e.X, Z = e.Z, Killer = e.LastHitBy });
            DropLoot(e);
            DropCarried(e);
            if (e.Mega) DropLostParcel(e);
        }

        private void StepEnemies()
        {
            if (_enemies.Count == 0) return;
            const float dt = SimConstants.TickDt;
            bool fleeOver = _fleeEndTick != 0 && Tick >= _fleeEndTick;
            RankAttackers();
            StepMegaAuras();
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                // A blow that fells the Post Office ends the run and clears the field mid-step.
                if (i >= _enemies.Count) { i = _enemies.Count; continue; }
                var e = _enemies[i];
                StepPush(e, dt);
                switch (e.State)
                {
                    case EnemyState.Flee:
                    {
                        var edge = EdgeOf(e);
                        float gx = edge.X, gz = edge.Z;
                        float dx = gx - e.X, dz = gz - e.Z;
                        if (fleeOver || dx * dx + dz * dz < 1.5f * 1.5f) { LoseCarried(e); _enemies.RemoveAt(i); continue; }
                        Walk(e, gx, gz, e.Speed * _combat.FleeSpeedMult * dt);
                        break;
                    }
                    case EnemyState.Windup:
                    {
                        if (Tick < e.StateEndTick) break;
                        e.State = EnemyState.Chase;
                        if (e.Def.Ranged) { Loose(e); break; }
                        if (e.Def.Airborne) { DropBomb(e); break; }
                        if (e.StrikeTx == PostOfficeStrike)
                        {
                            e.StrikeTx = -1;
                            if (PostOfficeDistance(e.X, e.Z, out _, out _) <= e.Radius + MathF.Max(StrikeReach, e.Def.AttackRange) + WindupSlack) DamagePostOffice(e.Damage, e);
                            break;
                        }
                        if (e.StrikeTx >= 0)
                        {
                            var built = Constructs.At(e.StrikeTx, e.StrikeTz);
                            e.StrikeTx = -1;
                            // A raider at a construct of its targetPriority strikes from its attackRange.
                            float strikeReach = MathF.Max(StrikeReach, e.Def.AttackRange);
                            if (built != null && Constructs.Distance(built, e.X, e.Z, out _, out _) <= built.Def.Radius + e.Radius + strikeReach + WindupSlack)
                            {
                                if (e.Def.StealsItems) Steal(e, built);
                                DamageConstruct(built, e.Damage);
                            }
                            break;
                        }
                        var target = GetPlayer(e.Target);
                        if (target == null || target.Dead) break;
                        float dx = target.Move.X - e.X, dz = target.Move.Z - e.Z;
                        float reach = e.Def.AttackRange + e.Radius + WindupSlack;
                        if (dx * dx + dz * dz > reach * reach) break;
                        // A wall between them takes the blow that was meant for the carrier behind it (one who jumps walls reaches over).
                        var cover = e.Def.JumpsWalls ? null : Constructs.Between(e.X, e.Z, target.Move.X, target.Move.Z);
                        if (cover != null) DamageConstruct(cover, e.Damage);
                        else DamagePlayer(target, e.Damage, e.Def.Index);
                        break;
                    }
                    default:
                        if (e.Def.Airborne) StepAirborne(e, dt);
                        else if (e.Def.PushesConstructs) StepPusher(e, dt);
                        else if (e.Def.Ranged) StepRanged(e, dt);
                        else if (e.Def.Suicide) StepSuicide(e, dt);
                        else if (e.Def.Siege || e.Def.StealsItems) StepPriorityMelee(e, dt);
                        else StepChase(e, dt);
                        break;
                }
            }

            // Spikes hurt whoever of them stands on their tile (doc 05 §4), carriers never.
            if (Constructs.All.Count > 0)
            {
                for (int i = _enemies.Count - 1; i >= 0; i--)
                {
                    var e = _enemies[i];
                    var under = Constructs.At((int)MathF.Floor(e.X / SimConstants.TileSize), (int)MathF.Floor(e.Z / SimConstants.TileSize));
                    if (under != null && under.Def.Behaviour == BuildingBehaviour.Spikes && e.State != EnemyState.Flee && !e.Def.Airborne)
                        DamageEnemy(e, under.Def.DamagePerSecond * dt, null);
                }
            }

            // Bodies do not stack: overlapping enemies are pushed apart, then kept out of walls.
            for (int i = 0; i < _enemies.Count; i++)
            {
                var a = _enemies[i];
                for (int j = i + 1; j < _enemies.Count; j++)
                {
                    var b = _enemies[j];
                    // Those in the air keep apart from each other, not from those under them.
                    if (a.Def.Airborne != b.Def.Airborne) continue;
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
            foreach (var e in _enemies)
                if (!e.Def.Airborne) PlayerMovement.ResolveCollisions(ref e.X, ref e.Z, 0f, Map, e.Radius, Constructs, carrier: false, jumper: e.Def.JumpsWalls);
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
                // One who shoots from afar takes nobody's place at the carrier.
                var target = e.State == EnemyState.Flee || e.Def.Ranged || e.Def.Suicide ? null : GetPlayer(e.Target);
                if (target == null) continue;
                float own = DistanceSq(e, target);
                int nearer = 0;
                foreach (var other in _enemies)
                {
                    if (other == e || other.Target != e.Target || other.State == EnemyState.Flee || other.Def.Ranged || other.Def.Suicide) continue;
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
                FindPath(e, gx, gz);
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
            if (StrikeWhatBlocks(e, target.Move.X, target.Move.Z)) return;
            Walk(e, target.Move.X, target.Move.Z, e.Speed * dt);
        }

        /// <summary>
        /// The way to a point: none if it can be walked straight; else round the houses, the props
        /// and what the carriers built; and where they have built every way shut, the way there
        /// would be without it (<see cref="StrikeWhatBlocks"/> makes it one).
        /// </summary>
        private void FindPath(Enemy e, float gx, float gz)
        {
            // One in the air flies straight.
            if (e.Def.Airborne) return;
            // One that shoves what is built out of its way goes round the houses only.
            if (e.Def.PushesConstructs)
            {
                if (!LineClear(e.X, e.Z, gx, gz, ignoreBuilt: true)) TilePathfinder.Find(Map, e.X, e.Z, gx, gz, e.Path, _navScratch);
                return;
            }
            bool jumper = e.Def.JumpsWalls;
            if (LineClear(e.X, e.Z, gx, gz, jumper)) return;
            if (TilePathfinder.Find(Map, e.X, e.Z, gx, gz, e.Path, _navScratch, Constructs, jumper: jumper) || Constructs.All.Count == 0) return;
            TilePathfinder.Find(Map, e.X, e.Z, gx, gz, e.Path, _navScratch);
        }

        /// <summary>
        /// A raider on its way to a carrier that is up against something they built breaks it
        /// (doc 05 §4): it stands and strikes, a blow every attackRate. True if that is what it
        /// does this tick. Its tile paths keep off what is built, so it only comes up against a
        /// wall where there is no way round, or where its target stands right behind one.
        /// </summary>
        private bool StrikeWhatBlocks(Enemy e, float goalX, float goalZ)
        {
            if (Constructs.All.Count == 0) return false;
            WalkPoint(e, goalX, goalZ, out float tx, out float tz);
            float dx = tx - e.X, dz = tz - e.Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist < 1e-3f) return false;
            var built = Constructs.Ahead(e.X, e.Z, dx / dist, dz / dist, e.Radius + StrikeReach, e.Def.JumpsWalls);
            if (built == null) return false;
            Constructs.Distance(built, e.X, e.Z, out float nearX, out float nearZ);
            if (MathF.Abs(nearX - e.X) + MathF.Abs(nearZ - e.Z) > 1e-3f) e.Yaw = MathF.Atan2(-(nearX - e.X), -(nearZ - e.Z));
            if (Tick < e.NextAttackTick) return true;
            e.State = EnemyState.Windup;
            e.StateEndTick = Tick + _combat.WindupTicks;
            e.NextAttackTick = Tick + SimConstants.SecondsToTicks(e.Def.AttackRate);
            e.StrikeTx = built.Tx;
            e.StrikeTz = built.Tz;
            return true;
        }

        /// <summary>
        /// The raiders' line (nit 17) of the raid's first way in: on its path to the Post Office,
        /// raidStandoffMetres from the pad. Farther than any aggroRange, so nobody is fought on
        /// the pad who did not pick the fight; the street between the Post Office and its
        /// mailboxes is theirs.
        /// </summary>
        public void RaidLine(out float x, out float z) => RaidLine(RaidEdge(), out x, out z, out _, out _);

        /// <summary>The line of a way in, and the way along its path towards the Post Office there.</summary>
        private void RaidLine(SpawnEdge edge, out float x, out float z, out float dirX, out float dirZ) =>
            // On a path too short for the standoff the line is the gate's mouth.
            edge.LinePoint(Map.PostOffice.SpawnPadX, Map.PostOffice.SpawnPadZ, _combat.RaidStandoff, HoldDeep, out x, out z, out dirX, out dirZ);

        /// <summary>Where on its way in's line this raider stands: lanes across the street, ranks back towards the mouth.</summary>
        public void HoldSpot(Enemy e, out float x, out float z)
        {
            var edge = EdgeOf(e);
            RaidLine(edge, out x, out z, out float dirX, out float dirZ);
            uint n = e.Id.Counter;
            float back = (n / HoldLanes % HoldRanks) * (HoldDeep / HoldRanks);
            float across = ((n % HoldLanes) / (HoldLanes - 1f) * 2f - 1f) * edge.HalfWidth;
            x += -dirX * back + dirZ * across;
            z += -dirZ * back - dirX * across;
        }

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
            WalkPoint(e, goalX, goalZ, out float tx, out float tz);
            float dx = tx - e.X, dz = tz - e.Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist < 1e-3f) return;
            if (step > dist) step = dist;
            e.X += dx / dist * step;
            e.Z += dz / dist * step;
            e.Yaw = MathF.Atan2(-dx, -dz);
        }

        /// <summary>Where it walks at next: the waypoint of its path it has not reached yet, or the goal itself.</summary>
        private static void WalkPoint(Enemy e, float goalX, float goalZ, out float x, out float z)
        {
            x = goalX; z = goalZ;
            while (e.Waypoint < e.Path.Count - 1)
            {
                float wx = e.Path[e.Waypoint].X - e.X, wz = e.Path[e.Waypoint].Z - e.Z;
                if (wx * wx + wz * wz > 0.6f * 0.6f) { x = e.Path[e.Waypoint].X; z = e.Path[e.Waypoint].Z; break; }
                e.Waypoint++;
            }
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
        private bool LineClear(float x0, float z0, float x1, float z1, bool jumper = false, bool ignoreBuilt = false)
        {
            const float tile = SimConstants.TileSize;
            float dx = x1 - x0, dz = z1 - z0;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            int steps = (int)MathF.Ceiling(dist / 0.5f);
            for (int i = 1; i < steps; i++)
            {
                float t = i / (float)steps;
                int tx = (int)MathF.Floor((x0 + dx * t) / tile), tz = (int)MathF.Floor((z0 + dz * t) / tile);
                if (Map.IsNavBlocked(tx, tz) || (!ignoreBuilt && Constructs.Blocks(tx, tz, jumper: jumper))) return false;
            }
            return true;
        }

        // --- the raid ---------------------------------------------------------------------------

        /// <summary>Where the raid's first pulses come in: the mouth of its first way in (the first of the map's without a raid).</summary>
        public void RaidGate(out float x, out float z)
        {
            var edge = RaidEdge();
            x = edge.X;
            z = edge.Z;
        }

        private SpawnEdge RaidEdge() => Map.SpawnEdges[_raidEdgeA >= 0 && _raidEdgeA < Map.SpawnEdges.Count ? _raidEdgeA : 0];

        /// <summary>The way in a raider came by, and leaves by.</summary>
        public SpawnEdge EdgeOf(Enemy e) => Map.SpawnEdges[e.Edge >= 0 && e.Edge < Map.SpawnEdges.Count ? e.Edge : 0];

        /// <summary>The way in nearest a point: whoever is put down there came by it.</summary>
        private int NearestEdge(float x, float z)
        {
            int best = 0;
            float bestSq = float.MaxValue;
            foreach (var edge in Map.SpawnEdges)
            {
                float dx = edge.X - x, dz = edge.Z - z, sq = dx * dx + dz * dz;
                if (sq < bestSq) { bestSq = sq; best = edge.Index; }
            }
            return best;
        }

        /// <summary>
        /// Doc 05 §3.2: the raid's ways in, chosen from those facing the districts open now — one
        /// for the first half of the pulses, a second one (if there is another) for the rest.
        /// From the seed's "spawns" stream, so a raid's way in moves nothing else of the run.
        /// </summary>
        private void ChooseRaidEdges()
        {
            _raidEdgeA = _raidEdgeB = -1;
            var open = new List<int>();
            foreach (var edge in Map.SpawnEdges)
                if (edge.OpenAt(Run.Shift)) open.Add(edge.Index);
            if (open.Count == 0) open.Add(0);
            if (open.Count == 1) { _raidEdgeA = open[0]; return; }
            int a = _spawnRng.NextInt(open.Count);
            _raidEdgeA = open[a];
            open.RemoveAt(a);
            _raidEdgeB = open[_spawnRng.NextInt(open.Count)];
        }

        /// <summary>
        /// Seconds of warning before the raid (doc 11 §10): the waves', with Early Warning, plus
        /// every Alarm Post in range of the raiders' way in, up to the cap.
        /// </summary>
        public double RaidWarningSeconds()
        {
            var waves = Content.Waves;
            return RaidRules.WarningSeconds(waves, TeamStat(Stat.RaidWarningSeconds, (float)waves.WarningSeconds), RaidRules.AlarmBonusSeconds(Map, Constructs, Run.Shift));
        }

        /// <summary>
        /// When the raid is announced, from the warning as it stands. An Alarm Post built or lost
        /// before then moves it; once announced, it stays.
        /// </summary>
        private void SetRaidWarnTick()
        {
            if (_raidStartTick == 0 || _raidAtStart || Raid != RaidState.None || Tick + 1 >= _raidStartTick) return;
            uint warning = SimConstants.SecondsToTicks(RaidWarningSeconds());
            _raidWarnTick = _raidStartTick - Math.Min(warning, _raidStartTick - Tick - 1);
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
            _raidEdgeA = _raidEdgeB = -1;
            var waves = Content.Waves;
            if (!Settings.Raids || Content.Enemies.Count == 0 || (Map.Debug is { Raids: false } && !RaidsAtShiftStart)) return;
            if (RaidsAtShiftStart)
            {
                // A shift without a raid of its own borrows the first one there is.
                while (_raidShift < ShiftCount && waves.BudgetFor(_raidShift) <= 0) _raidShift++;
                if (waves.BudgetFor(_raidShift) <= 0) return;
                ChooseRaidEdges();
                _raidAtStart = true;
                _raidWarnTick = Tick + 1;
                _raidStartTick = Tick + Math.Max(2, SimConstants.SecondsToTicks(PlaytestMask.RaidNowSeconds));
                return;
            }
            if (waves.BudgetFor(Run.Shift) <= 0) return;
            ChooseRaidEdges();
            uint length = Run.PhaseEndTick - Tick;
            uint window = Math.Min(SimConstants.SecondsToTicks(waves.RaidWindowSeconds), length - 1);
            _raidStartTick = Run.PhaseEndTick - window;
            SetRaidWarnTick();
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
                PlanRaid(waves);
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

        /// <summary>The leads of the raid under way, by the pulse they come with (design choice 141).</summary>
        public IReadOnlyList<IReadOnlyList<EnemyDef>> RaidLeads => _raidLeads;

        /// <summary>
        /// Design choice 141: a pulse's share of doc 05 §3.2's budget (budget / 6) never bought a
        /// Tank (150), and seldom a Giant (60), for a team of four or fewer. The shift's leads
        /// (waves.json "lead") are bought from the whole raid's budget when it starts instead,
        /// dearest first, each while what is left is at least leadBudgetRatio times its cost, and
        /// walk in one a pulse from the first (the Tank walks 2 m/s and the raid lasts 90 s). What
        /// they cost is taken from every pulse's share alike; a shift without leads rolls as it did.
        /// </summary>
        private void PlanRaid(WaveDef waves)
        {
            int pulses = waves.Pulses;
            while (_raidLeads.Count < pulses) _raidLeads.Add(new List<EnemyDef>());
            while (_raidLeads.Count > pulses) _raidLeads.RemoveAt(_raidLeads.Count - 1);
            foreach (var list in _raidLeads) list.Clear();
            _raidSpawnedOf.Clear();
            // The pulses' shares together: budget / 6 each, the finale's half again.
            double shares = (pulses - 1 + waves.FinalePulseMult) / pulses;
            double whole = _raidBudget * shares;
            int nth = 0;
            if (waves.LeadBudgetRatio > 0)
                foreach (var l in waves.LeadsFor(_raidShift))
                    for (int n = 0; n < l.Value && whole >= l.Key.Cost * waves.LeadBudgetRatio; n++)
                    {
                        _raidLeads[Math.Min(nth++, pulses - 1)].Add(l.Key);
                        _raidSpawnedOf[l.Key] = n + 1;
                        whole -= l.Key.Cost;
                    }
            _raidRollBudget = whole / shares;
        }

        /// <summary>
        /// One pulse (doc 05 §3.2): its leads (design choice 141), then weighted picks until its
        /// share of the budget is spent; what is too little for any enemy carries over to the next pulse.
        /// </summary>
        private void SpawnPulse(WaveDef waves, int pulse)
        {
            bool finale = pulse == waves.Pulses - 1;
            double budget = _raidRollBudget / waves.Pulses * (finale ? waves.FinalePulseMult : 1.0) + _raidCarry;
            var weights = waves.WeightsFor(_raidShift);
            int megaPermille = (int)Math.Round((waves.MegaChanceBase + waves.MegaChancePerShift * Run.Shift) * 1000);
            // Doc 05 §3.2: the last pulse of the last shift always brings a Mega.
            bool forceMega = finale && Run.Shift >= ShiftCount;
            int nth = 0;
            var leads = pulse < _raidLeads.Count ? _raidLeads[pulse] : null;
            int lead = 0;
            while (_enemies.Count < waves.MaxAlive)
            {
                EnemyDef def;
                bool led = leads != null && lead < leads.Count;
                if (led) def = leads![lead];
                else
                {
                    _affordable.Clear();
                    int total = 0;
                    foreach (var w in weights)
                    {
                        if (w.Key.Cost > budget) continue;
                        // Doc 05 §3.2 "Tank … (max 1)": no more of it than the raid's cap.
                        if (_raidSpawnedOf.TryGetValue(w.Key, out int sent) && sent >= waves.MaxFor(_raidShift, w.Key)) continue;
                        _affordable.Add(w);
                        total += w.Value;
                    }
                    if (total == 0) break;
                    int roll = _waveRng.NextInt(total);
                    def = _affordable[_affordable.Count - 1].Key;
                    foreach (var w in _affordable)
                    {
                        if (roll < w.Value) { def = w.Key; break; }
                        roll -= w.Value;
                    }
                }
                bool mega = forceMega || _waveRng.Chance(megaPermille);
                forceMega = false;
                var edge = Map.SpawnEdges[Math.Max(0, RaidRules.PulseEdge(pulse, waves.Pulses, nth++, _raidEdgeA, _raidEdgeB))];
                // Spread over the mouth, so a pulse walks in as a crowd rather than a stack.
                float inward = _waveRng.NextInt(300) / 100f, across = (_waveRng.NextInt(2001) - 1000) / 1000f * edge.HalfWidth;
                float x = edge.X + edge.InX * inward + edge.InZ * across;
                float z = edge.Z + edge.InZ * inward - edge.InX * across;
                var spawned = SpawnEnemy(def, x, z, mega);
                if (spawned == null) break;
                spawned.Edge = edge.Index;
                if (mega) Events.Add(new MegaSightedEvent { Enemy = spawned.Id, Kind = def.Index, Edge = (sbyte)edge.Index });
                if (led) { lead++; continue; }
                _raidSpawnedOf[def] = _raidSpawnedOf.TryGetValue(def, out int before) ? before + 1 : 1;
                budget -= def.Cost;
            }
            // A lead that found the field full comes with the next pulse.
            if (leads != null && lead < leads.Count && pulse + 1 < _raidLeads.Count)
                _raidLeads[pulse + 1].InsertRange(0, leads.GetRange(lead, leads.Count - lead));
            _raidCarry = budget;
        }

        private void SetRaid(RaidState state, uint tick)
        {
            if (Raid == state) return;
            Raid = state;
            RaidTick = tick;
            if (state == RaidState.None) _raidEdgeA = _raidEdgeB = -1;
            Events.Add(new RaidChangedEvent { Raid = state, Tick = tick, EdgeA = (sbyte)_raidEdgeA, EdgeB = (sbyte)_raidEdgeB });
        }

        /// <summary>Doc 05 §3.4: when the shift clock ends every enemy runs for the gate and is gone 5 s later.</summary>
        private void EndRaid()
        {
            _raidWarnTick = _raidStartTick = 0;
            _raidAtStart = false;
            _shots.Clear();
            if (_enemies.Count == 0 && Raid == RaidState.None) return;
            _fleeEndTick = Tick + _combat.FleeTicks;
            foreach (var e in _enemies)
            {
                e.State = EnemyState.Flee;
                e.Path.Clear();
                e.Waypoint = 0;
                e.StrikeTx = -1;
                var edge = EdgeOf(e);
                FindPath(e, edge.X, edge.Z);
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
            foreach (var e in _enemies) LoseCarried(e);
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
            _spawnRng = new RngStream(Settings.Seed, "spawns");
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
            // Doc 05 §2.1 "plus 2 Iron Ingots", on top of its kind's drops.
            if (e.Mega) AddMegaItems(bundle);
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
