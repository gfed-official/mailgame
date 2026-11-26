using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>The fight of doc 05 as the concept video shows it: stamina, hammer, mail cannon, raiders, the night raid, loot.</summary>
    public class CombatTests
    {
        private const int Tps = 30;
        /// <summary>Looking along +X, down the street to the east.</summary>
        private const float East = -MathF.PI / 2f;

        private uint _nextRequest = 1;
        private uint _nextMailId = 700000;

        private static ContentDb Db => TestContent.Db;

        private static SimWorld NewWorld(uint seed = 7, bool raids = true) =>
            new SimWorld(Db, new SimSettings { Seed = seed, Raids = raids, Draft = false });

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static void RunUntil(SimWorld world, Func<bool> done, int maxSeconds)
        {
            for (int i = 0; i < maxSeconds * Tps && !done(); i++) world.Step();
            Assert.True(done(), $"still in {world.Run.Phase} of shift {world.Run.Shift} after {maxSeconds} s");
        }

        private void StartDelivery(SimWorld world, Player host)
        {
            if (world.Run.Phase == RunPhase.Lobby) Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            foreach (var p in world.Players) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
        }

        /// <summary>Plays shift 1 on quota and stops as the Delivery of shift 2 begins.</summary>
        private void ReachShiftTwo(SimWorld world, Player p)
        {
            StartDelivery(world, p);
            var home = world.Map.Destinations[0];
            var item = TestContent.Mail("letter", home.Address, _nextMailId++, 1);
            item.Value = (ushort)world.Run.Quota;
            Assert.True(world.Give(p.Hotbar, ItemStack.OfMail(item)));
            SimDriver.Teleport(p, home.MailboxX, home.MailboxZ);
            SimDriver.StepTicks(world, 15);
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.Deliver(0, p.Hotbar.Id, p.Hotbar.Entries.Single().EntryId, home.Id)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 241);
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 21);
            StartDelivery(world, p);
            Assert.Equal(2, world.Run.Shift);
        }

        /// <summary>One tick with one input: standing still, facing <paramref name="yaw"/>, with these buttons down.</summary>
        private static void Press(SimWorld world, Player p, InputButtons buttons, float yaw = East)
        {
            world.QueueInput(p.Id, new PlayerInput { Seq = p.LastQueuedSeq + 1, Yaw = PlayerInput.QuantizeYaw(yaw), Buttons = buttons });
            world.Step();
        }

        private static Player PlayerOnTheStreet(SimWorld world, float x = 50f, float z = 28f)
        {
            var p = world.AddPlayer("carrier")!;
            SimDriver.Teleport(p, x, z);
            return p;
        }

        private static int Carried(Player p, string itemId)
        {
            ushort index = Db.Item(itemId).Index;
            return p.Inventory.Entries.Where(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == index).Sum(e => e.Stack.Count);
        }

        // --- content ----------------------------------------------------------------------------

        [Fact]
        public void ContentHasTheThreeRaidersOfTheVideo_AndAWaveTable()
        {
            Assert.Equal(new[] { "barbarian", "swarmling", "giant", "archer", "wall_breaker", "hog_rider" }, Db.Enemies.Select(e => e.Id));
            for (int i = 0; i < Db.Enemies.Count; i++) Assert.Equal(i, Db.Enemies[i].Index);
            var giant = Db.Enemy("giant");
            Assert.Equal(400f, giant.Hp);
            Assert.Equal(new[] { "stone", "iron_ore" }, giant.Drops.Select(d => d.Item.Id));

            var waves = Db.Waves;
            Assert.Equal(0, waves.BudgetFor(1));
            Assert.Equal(120, waves.BudgetFor(2));
            Assert.Equal(225, waves.BudgetFor(5));
            Assert.Empty(waves.WeightsFor(1));
            Assert.DoesNotContain(waves.WeightsFor(2), w => w.Key.Id == "giant");
            Assert.Contains(waves.WeightsFor(3), w => w.Key.Id == "giant");
            // Nothing is on a shift's table before its unlock shift.
            for (int shift = 1; shift <= 5; shift++)
                Assert.All(waves.WeightsFor(shift), w => Assert.True(w.Key.UnlockShift <= shift, $"{w.Key.Id} in shift {shift}"));
        }

        [Fact]
        public void WaveBudgetScalesWithPlayersAndComplaint_AndIsEasedForOneOrTwo()
        {
            var waves = Db.Waves;
            Assert.Equal(120 * 0.8, SimWorld.WaveBudget(waves, 2, 1, 0), 6);
            Assert.Equal(120 * 1.35 * 0.8, SimWorld.WaveBudget(waves, 2, 2, 0), 6);
            Assert.Equal(120 * (1 + 0.35 * 3), SimWorld.WaveBudget(waves, 2, 4, 0), 6);
            Assert.Equal(225 * (1 + 0.35 * 3) * 1.5, SimWorld.WaveBudget(waves, 5, 4, 50), 6);
            Assert.Equal(0, SimWorld.WaveBudget(waves, 1, 4, 100), 6);
        }

        // --- stamina ----------------------------------------------------------------------------

        [Fact]
        public void SprintingSpendsTheStaminaInEightSeconds_ThenTheCarrierWalksUntilAFifthIsBack()
        {
            var map = Db.Map("test_street");
            var c = MoveConstants.From(Db.Balance);
            var s = new PlayerMoveState { X = 5f, Z = 28f, Grounded = true };
            var sprint = new PlayerInput { MoveX = PlayerInput.QuantizeAxis(1f), Buttons = InputButtons.Sprint };

            for (int i = 0; i < 8 * Tps - 2; i++) PlayerMovement.Step(ref s, sprint, map, 1f, c);
            Assert.False(s.Winded);
            Assert.InRange(s.Stamina, 0.001f, 0.02f);
            Assert.InRange(s.X, 5f + 7.5f * 7.9f, 5f + 7.5f * 8f);

            for (int i = 0; i < 4; i++) PlayerMovement.Step(ref s, sprint, map, 1f, c);
            Assert.True(s.Winded);

            // Winded: the sprint key does nothing, and the stamina comes back at a fifth per second.
            float x = s.X;
            for (int i = 0; i < 20; i++) PlayerMovement.Step(ref s, sprint, map, 1f, c);
            Assert.True(s.Winded);
            Assert.Equal(5f / Tps * 20, s.X - x, 2);

            for (int i = 0; i < 12; i++) PlayerMovement.Step(ref s, sprint, map, 1f, c);
            Assert.False(s.Winded);
            x = s.X;
            PlayerMovement.Step(ref s, sprint, map, 1f, c);
            Assert.Equal(7.5f / Tps, s.X - x, 3);
        }

        [Fact]
        public void StaminaComesBackInFiveSecondsOfNotSprinting()
        {
            var map = Db.Map("test_street");
            var c = MoveConstants.From(Db.Balance);
            var s = new PlayerMoveState { X = 30f, Z = 28f, Grounded = true, Fatigue = 1f, Winded = true };
            var walk = new PlayerInput { MoveX = PlayerInput.QuantizeAxis(-1f) };
            for (int i = 0; i < 5 * Tps - 2; i++) PlayerMovement.Step(ref s, walk, map, 1f, c);
            Assert.InRange(s.Fatigue, 0.001f, 0.03f);
            for (int i = 0; i < 4; i++) PlayerMovement.Step(ref s, default, map, 1f, c);
            Assert.Equal(0f, s.Fatigue);

            // Holding sprint without moving costs nothing.
            var held = new PlayerInput { Buttons = InputButtons.Sprint };
            for (int i = 0; i < 60; i++) PlayerMovement.Step(ref s, held, map, 1f, c);
            Assert.Equal(0f, s.Fatigue);
        }

        // --- the hammer -------------------------------------------------------------------------

        [Fact]
        public void HammerHitsWhatIsInFront_ShovesItBack_AndNeedsItsCooldown()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world);
            var raider = Db.Enemy("barbarian");
            var front = world.SpawnEnemy(raider, 52f, 28f)!;
            var behind = world.SpawnEnemy(raider, 48f, 28f)!;
            var far = world.SpawnEnemy(raider, 54f, 28f)!;

            Press(world, p, InputButtons.Attack);
            var swing = world.Events.OfType<AttackEvent>().Single();
            Assert.Equal((p.Id, Weapon.Hammer, (byte)1), (swing.Player, swing.Weapon, swing.Hits));
            Assert.Equal(35f, front.Hp);
            Assert.Equal(60f, behind.Hp);
            Assert.Equal(60f, far.Hp);
            Assert.True(front.X > 52.1f, $"expected a shove east, x = {front.X}");
            Assert.Equal(front.LastHitBy, p.Id);

            // 0.7 s = 21 ticks between swings, however long the button is held.
            for (int i = 0; i < 20; i++) Press(world, p, InputButtons.Attack);
            Assert.Single(world.Events.OfType<AttackEvent>());
            Press(world, p, InputButtons.Attack);
            Assert.Equal(2, world.Events.OfType<AttackEvent>().Count());
        }

        [Fact]
        public void WhoeverTakesALeaversId_InheritsNothingOfTheirFight()
        {
            // Player ids are used again (nit 11): a blow, a shot and a chase must not pass to the newcomer.
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            var guest = PlayerOnTheStreet(world);
            var raider = world.SpawnEnemy(Db.Enemy("barbarian"), 52f, 28f)!;
            var far = world.SpawnEnemy(Db.Enemy("barbarian"), 68f, 28f)!;
            Press(world, guest, InputButtons.Attack);
            Assert.Equal(guest.Id, raider.LastHitBy);
            SimDriver.StepTicks(world, 5);
            Assert.Equal(guest.Id, raider.Target);
            var home = world.Map.Destinations[0];
            Assert.True(world.Give(guest.Hotbar, ItemStack.OfMail(TestContent.Mail("small", home.Address, _nextMailId++))));
            Press(world, guest, InputButtons.AltAttack);
            Assert.Equal(guest.Id, world.Shots.Single().Owner);

            world.RemovePlayer(guest.Id);
            Assert.True(raider.LastHitBy.IsNone && raider.Target.IsNone && far.Target.IsNone);
            Assert.True(world.Shots.Single().Owner.IsNone);
            var next = world.AddPlayer("next")!;
            Assert.Equal(guest.Id, next.Id);

            // The package still lands, the raider it fells is nobody's kill, and nobody is paid for it.
            raider.Hp = 1f;
            world.Events.Clear();
            for (int i = 0; i < 12 && world.Shots.Count > 0; i++) world.Step();
            Assert.Empty(world.Shots);
            var died = world.Events.OfType<EnemyDiedEvent>().Single();
            Assert.Equal(raider.Id, died.Enemy);
            Assert.True(died.Killer.IsNone);
            Assert.Equal(0, next.Kills + host.Kills);
        }

        [Fact]
        public void ASwingAtNothingStillSwings()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world);
            Press(world, p, InputButtons.Attack);
            Assert.Equal(0, world.Events.OfType<AttackEvent>().Single().Hits);
        }

        [Fact]
        public void KillingARaiderCountsForTheCarrier_AndTheSnapshotsStopListingIt()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world);
            var swarmling = world.SpawnEnemy(Db.Enemy("swarmling"), 51.5f, 28f)!;
            Press(world, p, InputButtons.Attack);
            Assert.Empty(world.Enemies);
            Assert.Null(world.GetEnemy(swarmling.Id));
            var died = world.Events.OfType<EnemyDiedEvent>().Single();
            Assert.Equal((swarmling.Id, swarmling.Def.Index, p.Id), (died.Enemy, died.Kind, died.Killer));
            Assert.Equal(1, p.Kills);
            Assert.Equal(1, world.EnemiesKilledTotal);
        }

        // --- the mail cannon --------------------------------------------------------------------

        [Fact]
        public void CannonNeedsASmallPackage_AndSpendsItWithoutAComplaint()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world);
            var raider = Db.Enemy("barbarian");
            var a = world.SpawnEnemy(raider, 58f, 28f)!;
            var b = world.SpawnEnemy(raider, 59f, 29f)!;
            var c = world.SpawnEnemy(raider, 68f, 28f)!;

            // Letters are not ammunition.
            var home = world.Map.Destinations[0];
            Assert.True(world.Give(p.Inventory, ItemStack.OfMail(TestContent.Mail("letter", home.Address, _nextMailId++))));
            Assert.False(world.HasAmmo(p));
            Press(world, p, InputButtons.AltAttack);
            Assert.Empty(world.Events.OfType<AttackEvent>());
            Assert.Empty(world.Shots);

            Assert.True(world.Give(p.Hotbar, ItemStack.OfMail(TestContent.Mail("small", home.Address, _nextMailId++))));
            Assert.True(world.Give(p.Inventory, ItemStack.OfMail(TestContent.Mail("small", home.Address, _nextMailId++))));
            Assert.True(world.HasAmmo(p));
            Press(world, p, InputButtons.AltAttack);
            var shot = world.Events.OfType<AttackEvent>().Single();
            Assert.Equal((p.Id, Weapon.Cannon), (shot.Player, shot.Weapon));
            Assert.Equal(0, p.Hotbar.EntryCount);
            Assert.Single(world.Shots);

            // 40 m/s: the package is among them within a third of a second and takes both of the near pair.
            for (int i = 0; i < 12 && world.Shots.Count > 0; i++) Press(world, p, InputButtons.None);
            Assert.Empty(world.Shots);
            var blast = world.Events.OfType<BlastEvent>().Single();
            Assert.Equal(2, blast.Hits);
            Assert.Equal(20f, a.Hp);
            Assert.Equal(20f, b.Hp);
            Assert.Equal(60f, c.Hp);
            Assert.Equal(0, world.Economy.TotalMisdeliveries);
            Assert.Equal(0, world.Economy.Complaint);

            // 2 s between shots; the second package comes out of the inventory.
            int waited = 0;
            while (world.Events.OfType<AttackEvent>().Count() < 2 && waited < 100) { Press(world, p, InputButtons.AltAttack); waited++; }
            Assert.InRange(waited, 2 * Tps - 12, 2 * Tps);
            Assert.False(world.HasAmmo(p));
            Assert.Equal(1, p.Inventory.EntryCount); // the letter
        }

        [Fact]
        public void ACannonPackageStopsAtAWall()
        {
            var world = NewWorld();
            // South of the first house (x 20..26, z 16..20), firing north into its wall.
            var p = PlayerOnTheStreet(world, 23f, 24f);
            var home = world.Map.Destinations[0];
            Assert.True(world.Give(p.Hotbar, ItemStack.OfMail(TestContent.Mail("small", home.Address, _nextMailId++))));
            Press(world, p, InputButtons.AltAttack, yaw: 0f);
            for (int i = 0; i < 10; i++) Press(world, p, InputButtons.None, yaw: 0f);
            var blast = world.Events.OfType<BlastEvent>().Single();
            Assert.InRange(blast.Z, 20f, 20.5f);
            Assert.Equal(23f, blast.X, 2);
            Assert.Equal(0, blast.Hits);
        }

        /// <summary>Fires one package at a point and steps until it has gone off.</summary>
        private BlastEvent FireAt(SimWorld world, Player p, float x, float z)
        {
            float yaw = MathF.Atan2(-(x - p.Move.X), -(z - p.Move.Z));
            int before = world.Events.OfType<BlastEvent>().Count();
            Press(world, p, InputButtons.AltAttack, yaw);
            for (int i = 0; i < Tps && world.Events.OfType<BlastEvent>().Count() == before; i++) Press(world, p, InputButtons.None, yaw);
            Assert.Equal(before + 1, world.Events.OfType<BlastEvent>().Count());
            return world.Events.OfType<BlastEvent>().Last();
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(1.2f)]
        [InlineData(-1.2f)]
        public void APackageGoesOffAsItPassesARaider_AimedBesideItStillHits(float beside)
        {
            // The playtest's cannon never hit a raider: a package had to touch one to go off.
            var world = NewWorld();
            var p = PlayerOnTheStreet(world);
            var home = world.Map.Destinations[0];
            Assert.True(world.Give(p.Hotbar, ItemStack.OfMail(TestContent.Mail("small", home.Address, _nextMailId++))));
            var raider = world.SpawnEnemy(Db.Enemy("barbarian"), 62f, 28f)!;

            var blast = FireAt(world, p, 62f, 28f + beside);
            Assert.Equal(1, blast.Hits);
            Assert.Equal(20f, raider.Hp);
            Assert.InRange(blast.X, 58f, 62f);
        }

        [Theory]
        [InlineData(3.5f)]
        [InlineData(-3.5f)]
        public void APackageAimedWellWideFliesOn(float beside)
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world);
            var home = world.Map.Destinations[0];
            Assert.True(world.Give(p.Hotbar, ItemStack.OfMail(TestContent.Mail("small", home.Address, _nextMailId++))));
            var raider = world.SpawnEnemy(Db.Enemy("barbarian"), 62f, 28f)!;

            var blast = FireAt(world, p, 62f, 28f + beside);
            Assert.Equal(0, blast.Hits);
            Assert.Equal(60f, raider.Hp);
            Assert.True(blast.X > 66f, $"went off at x {blast.X:0.0}");
        }

        [Fact]
        public void FromThePad_FourPackagesAtTheRaidOnItsLine_AllHit()
        {
            // The playtest's cannon check: a carrier on the pad, the raid 19 m off, a hand that is a few degrees out.
            var world = NewWorld(seed: 21);
            var p = world.AddPlayer("carrier")!;
            ReachShiftTwo(world, p);
            var po = world.Map.PostOffice;
            SimDriver.Teleport(p, po.SpawnPadX, po.SpawnPadZ);
            RunUntil(world, () => world.RaidPulsesSpawned == 3, 271);
            SimDriver.StepTicks(world, 14 * Tps);
            Assert.True(world.Enemies.Count >= 4, $"{world.Enemies.Count} raiders");
            var home = world.Map.Destinations[0];
            for (int i = 0; i < 4; i++)
            {
                var package = ItemStack.OfMail(TestContent.Mail("small", home.Address, _nextMailId++, 2));
                Assert.True(world.Give(p.Hotbar, package) || world.Give(p.Inventory, package));
            }
            p.MaxHp = p.Hp = 100000f;
            world.Events.Clear();

            float[] degreesOff = { 3f, -3f, 2f, -2f };
            int hurt = 0;
            foreach (float off in degreesOff)
            {
                var target = world.Enemies.OrderBy(e => Distance(e, p)).First();
                float dx = target.X - p.Move.X, dz = target.Z - p.Move.Z, dist = MathF.Sqrt(dx * dx + dz * dz);
                float yaw = MathF.Atan2(-dx, -dz) + off * MathF.PI / 180f;
                var blast = FireAt(world, p, p.Move.X - MathF.Sin(yaw) * dist, p.Move.Z - MathF.Cos(yaw) * dist);
                Assert.True(blast.Hits >= 1, $"the package aimed {off}° beside a raider {dist:0.0} m off hit nobody");
                hurt += blast.Hits;
                // Until the cannon is ready again.
                for (int i = 0; i < 2 * Tps; i++) Press(world, p, InputButtons.None, yaw);
            }
            Assert.True(hurt >= 4);
            Assert.False(world.HasAmmo(p));
        }

        // --- raiders ----------------------------------------------------------------------------

        [Fact]
        public void ARaiderWalksUp_WindsUp_AndHits()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world);
            var e = world.SpawnEnemy(Db.Enemy("barbarian"), 60f, 28f)!;

            // 8.1 m at 4.5 m/s, then a 0.4 s windup.
            int ticks = 0;
            bool sawWindup = false;
            while (p.Hp == p.MaxHp && ticks < 5 * Tps)
            {
                world.Step();
                ticks++;
                sawWindup |= e.State == EnemyState.Windup;
            }
            Assert.True(sawWindup);
            Assert.Equal(90f, p.Hp);
            Assert.InRange(ticks, (int)(2.1f * Tps), (int)(2.4f * Tps));
            Assert.InRange(MathF.Abs(e.X - p.Move.X), 1.2f, 1.95f);

            // One blow a second after that.
            SimDriver.StepTicks(world, 3 * Tps);
            Assert.Equal(60f, p.Hp);
        }

        [Fact]
        public void SteppingBackOutOfReachDuringTheWindupDodgesTheBlow()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world);
            var e = world.SpawnEnemy(Db.Enemy("barbarian"), 53f, 28f)!;
            int guard = 0;
            while (e.State != EnemyState.Windup && guard++ < 3 * Tps) world.Step();
            Assert.Equal(EnemyState.Windup, e.State);
            SimDriver.Teleport(p, p.Move.X - 4f, p.Move.Z);
            SimDriver.StepTicks(world, 13);
            Assert.Equal(EnemyState.Chase, e.State);
            Assert.Equal(p.MaxHp, p.Hp);
        }

        [Fact]
        public void RaidersFindTheirWayAroundAHouse()
        {
            var world = NewWorld();
            // North of the first house; the raider starts on the street south of it.
            var p = PlayerOnTheStreet(world, 23f, 13f);
            var e = world.SpawnEnemy(Db.Enemy("barbarian"), 23f, 27f)!;
            int ticks = 0;
            while (p.Hp == p.MaxHp && ticks++ < 15 * Tps) world.Step();
            Assert.True(p.Hp < p.MaxHp, $"the raider never arrived: it stands at {e.X:0.0}, {e.Z:0.0}");
        }

        [Fact]
        public void RaidersDoNotStandInsideEachOther()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world);
            var raider = Db.Enemy("barbarian");
            var pack = new List<Enemy>();
            for (int i = 0; i < 6; i++) pack.Add(world.SpawnEnemy(raider, 60f, 28f)!);
            SimDriver.StepTicks(world, 4 * Tps);
            for (int i = 0; i < pack.Count; i++)
                for (int j = i + 1; j < pack.Count; j++)
                {
                    float dx = pack[i].X - pack[j].X, dz = pack[i].Z - pack[j].Z;
                    Assert.True(MathF.Sqrt(dx * dx + dz * dz) > 0.5f, $"raiders {i} and {j} overlap");
                }
            Assert.True(p.Hp < p.MaxHp);
        }

        [Fact]
        public void TheCapOnLivingEnemiesHolds()
        {
            var world = NewWorld();
            var raider = Db.Enemy("swarmling");
            for (int i = 0; i < Db.Waves.MaxAlive; i++) Assert.NotNull(world.SpawnEnemy(raider, 40f + i, 28f));
            Assert.Null(world.SpawnEnemy(raider, 30f, 28f));
            Assert.Equal(Db.Waves.MaxAlive, world.EnemiesSpawnedTotal);
        }

        [Fact]
        public void AMegaIsTougherHitsHarderAndWalksSlower()
        {
            var world = NewWorld();
            var def = Db.Enemy("barbarian");
            var mega = world.SpawnEnemy(def, 60f, 28f, mega: true)!;
            Assert.Equal(def.Hp * 2.5f, mega.MaxHp);
            Assert.Equal(def.Damage * 1.5f, mega.Damage);
            Assert.Equal(def.Speed * 0.9f, mega.Speed, 4);
            Assert.True(mega.Mega);
        }

        // --- health -----------------------------------------------------------------------------

        [Fact]
        public void HealthComesBackAfterFiveQuietSeconds()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world);
            Assert.Equal(100f, p.Hp);
            world.DamagePlayer(p, 30f);
            SimDriver.StepTicks(world, 5 * Tps - 1);
            Assert.Equal(70f, p.Hp);
            SimDriver.StepTicks(world, 1 + 10 * Tps);
            Assert.InRange(p.Hp, 89.9f, 90.2f);

            // A new hit stops it again.
            world.DamagePlayer(p, 10f);
            float hp = p.Hp;
            SimDriver.StepTicks(world, 4 * Tps);
            Assert.Equal(hp, p.Hp);
            SimDriver.StepTicks(world, 30 * Tps);
            Assert.Equal(100f, p.Hp);
        }

        [Fact]
        public void AtZeroHealthTheCarrierIsDown_AndStandsUpAtThePostOfficeTenSecondsLater()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world);
            var other = world.AddPlayer("other")!;
            world.DamagePlayer(p, 250f, Db.Enemy("giant").Index);
            Assert.True(p.Dead);
            Assert.Equal(0f, p.Hp);
            Assert.Equal(0, p.HpByte);
            var died = world.Events.OfType<PlayerDiedEvent>().Single();
            Assert.Equal((p.Id, world.Tick + 10 * Tps, Db.Enemy("giant").Index), (died.Player, died.RespawnTick, died.Killer));

            // Down: no walking, no swinging, no hands in containers; a second blow changes nothing.
            float x = p.Move.X;
            for (int i = 0; i < 10; i++)
            {
                world.QueueInput(p.Id, new PlayerInput
                {
                    Seq = p.LastQueuedSeq + 1, MoveX = PlayerInput.QuantizeAxis(1f), Buttons = InputButtons.Attack | InputButtons.Jump,
                });
                world.Step();
            }
            Assert.Equal(x, p.Move.X);
            Assert.True(p.Move.Grounded);
            Assert.Empty(world.Events.OfType<AttackEvent>());
            Assert.Equal(OpResult.Dead, Request(world, p, SimRequest.Open(0, world.Intake.Id)));
            world.DamagePlayer(p, 50f);
            Assert.Single(world.Events.OfType<PlayerDiedEvent>());

            // Raiders leave the fallen alone and go for whoever still stands.
            SimDriver.Teleport(other, x + 10f, 28f);
            var e = world.SpawnEnemy(Db.Enemy("barbarian"), x + 2f, 28f)!;
            SimDriver.StepTicks(world, 2);
            Assert.Equal(other.Id, e.Target);
            world.DamageEnemy(e, 1000f, null);

            RunUntil(world, () => !p.Dead, 11);
            Assert.Equal(died.RespawnTick, world.Tick);
            Assert.Equal(p.MaxHp, p.Hp);
            var po = world.Map.PostOffice;
            Assert.InRange(MathF.Abs(p.Move.X - po.SpawnPadX) + MathF.Abs(p.Move.Z - po.SpawnPadZ), 0f, 4f);
            Assert.Equal(p.Id, world.Events.OfType<PlayerRespawnedEvent>().Single().Player);
        }

        [Fact]
        public void DyingClosesTheContainerTheCarrierHadOpen()
        {
            var world = NewWorld();
            var p = world.AddPlayer("carrier")!;
            StartDelivery(world, p);
            var po = world.Map.PostOffice;
            SimDriver.Teleport(p, po.IntakeX, po.IntakeZ + 1.5f);
            SimDriver.StepTicks(world, 2);
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.Open(0, world.Intake.Id)));
            Assert.Equal(world.Intake.Id, p.OpenContainer);
            world.DamagePlayer(p, 1000f);
            Assert.Equal(0u, p.OpenContainer);
        }

        // --- who the raiders go for (nit 17) ------------------------------------------------------

        private static float Distance(Enemy e, float x, float z) => MathF.Sqrt((e.X - x) * (e.X - x) + (e.Z - z) * (e.Z - z));

        private static float Distance(Enemy e, Player p) => Distance(e, p.Move.X, p.Move.Z);

        [Fact]
        public void WithNobodyInAggroRange_RaidersTakeTheirPlacesOnTheLine_AndLeaveThePadAlone()
        {
            var world = NewWorld();
            var p = world.AddPlayer("carrier")!;
            var po = world.Map.PostOffice;
            world.RaidGate(out float gx, out float gz);
            world.RaidLine(out float lx, out float lz);
            Assert.Equal(gz, lz);
            Assert.Equal(19f, MathF.Sqrt((lx - po.SpawnPadX) * (lx - po.SpawnPadX) + (lz - po.SpawnPadZ) * (lz - po.SpawnPadZ)), 3);

            var pack = new List<Enemy>();
            for (int i = 0; i < 12; i++) pack.Add(world.SpawnEnemy(Db.Enemy(i % 3 == 0 ? "swarmling" : "barbarian"), gx - i * 0.3f, gz)!);
            SimDriver.StepTicks(world, 25 * Tps);

            foreach (var e in pack)
            {
                Assert.True(e.Target.IsNone);
                Assert.InRange(e.X, lx - 0.6f, lx + 6.6f);
                Assert.InRange(e.Z, lz - 2f, lz + 2f);
                Assert.True(Distance(e, po.SpawnPadX, po.SpawnPadZ) > e.Def.AggroRange, $"{e.Def.Id} holds {Distance(e, po.SpawnPadX, po.SpawnPadZ):0.0} m from the pad");
                Assert.True(Distance(e, po.IntakeX, po.IntakeZ) > e.Def.AggroRange);
                // Watching the Post Office: looking west, not back at the gate.
                Assert.True(MathF.Sin(e.Yaw) > 0.5f, $"yaw {e.Yaw}");
            }
            Assert.Equal(p.MaxHp, p.Hp);

            // They stand: no shuffling on the spot.
            var before = pack.Select(e => (e.X, e.Z)).ToList();
            SimDriver.StepTicks(world, 2 * Tps);
            for (int i = 0; i < pack.Count; i++)
                Assert.InRange(Distance(pack[i], before[i].X, before[i].Z), 0f, 0.05f);
        }

        [Fact]
        public void ARaiderGoesForACarrierWithinAggroRange_AndLetsGoOfOneWhoGotAway()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world, 60f, 28f);
            var near = world.SpawnEnemy(Db.Enemy("barbarian"), 74.5f, 28f)!;
            var far = world.SpawnEnemy(Db.Enemy("barbarian"), 77f, 29f)!;
            SimDriver.StepTicks(world, 2);
            Assert.Equal(p.Id, near.Target);
            Assert.True(far.Target.IsNone);

            // On its way to the line the other one comes by, and has them too.
            SimDriver.StepTicks(world, Tps);
            Assert.Equal(p.Id, far.Target);

            // Twenty metres up the street is not away (the leash is half again the aggro range) …
            SimDriver.Teleport(p, near.X - 20f, 28f);
            world.Step();
            Assert.Equal(p.Id, near.Target);
            // … twenty-five are.
            SimDriver.Teleport(p, near.X - 25f, 28f);
            SimDriver.StepTicks(world, 2);
            Assert.True(near.Target.IsNone && far.Target.IsNone);
        }

        [Fact]
        public void ABlowFromAfarBringsTheRaiderOver()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world, 60f, 28f);
            p.MaxHp = p.Hp = 1000f;
            var e = world.SpawnEnemy(Db.Enemy("barbarian"), 88f, 28f)!;
            SimDriver.StepTicks(world, 3);
            Assert.True(e.Target.IsNone);
            world.DamageEnemy(e, 40f, p);
            SimDriver.StepTicks(world, 2);
            Assert.Equal(p.Id, e.Target);
            // Six seconds of that bring it within aggro range, and then it is a fight like any other.
            int ticks = 0;
            while (p.Hp == p.MaxHp && ticks++ < 12 * Tps) world.Step();
            Assert.True(p.Hp < p.MaxHp, $"the raider never arrived: it stands at {e.X:0.0}, {e.Z:0.0}");
        }

        [Fact]
        public void BackOnTheirFeet_ACarrierIsProtectedForFiveSeconds()
        {
            var world = NewWorld();
            var p = world.AddPlayer("carrier")!;
            world.DamagePlayer(p, 1000f);
            RunUntil(world, () => !p.Dead, 11);
            var back = world.Events.OfType<PlayerRespawnedEvent>().Single();
            Assert.Equal(world.Tick + 5 * Tps, back.ProtectedUntilTick);
            Assert.True(world.Protected(p));

            // With no raider about, five seconds is all there is to it.
            world.DamagePlayer(p, 30f);
            Assert.Equal(p.MaxHp, p.Hp);
            SimDriver.StepTicks(world, 5 * Tps);
            Assert.False(world.Protected(p));
            world.DamagePlayer(p, 30f);
            Assert.Equal(p.MaxHp - 30f, p.Hp);
        }

        [Fact]
        public void InTheYard_TheProtectionHoldsForAsLongAsTheyStay()
        {
            // The playtest's loop: down again the moment they stood up, raiders all around the pad.
            var world = NewWorld();
            var p = world.AddPlayer("carrier")!;
            world.DamagePlayer(p, 1000f);
            RunUntil(world, () => !p.Dead, 11);
            var po = world.Map.PostOffice;

            // A raider right beside them: its blows do nothing, and it does not even try.
            var e = world.SpawnEnemy(Db.Enemy("barbarian"), p.Move.X + 1.2f, p.Move.Z)!;
            world.DamagePlayer(p, 30f, e.Def.Index);
            Assert.Equal(p.MaxHp, p.Hp);
            SimDriver.StepTicks(world, 4 * Tps);
            Assert.Equal(p.MaxHp, p.Hp);
            Assert.True(e.Target.IsNone);
            Assert.NotEqual(EnemyState.Windup, e.State);
            Assert.True(Distance(e, p) > 8f, $"the raider is still {Distance(e, p):0.0} m from the pad");

            // The five seconds are long up; on the pad and at the Intake nothing has changed.
            SimDriver.StepTicks(world, 10 * Tps);
            Assert.True(world.InYard(p));
            Assert.True(world.Protected(p));
            SimDriver.Teleport(p, po.IntakeX, po.IntakeZ + 1.5f);
            SimDriver.StepTicks(world, Tps);
            Assert.True(world.InYard(p) && world.Protected(p));
            var near = world.SpawnEnemy(Db.Enemy("barbarian"), p.Move.X + 3f, p.Move.Z + 3f)!;
            SimDriver.StepTicks(world, 3 * Tps);
            Assert.True(near.Target.IsNone);
            Assert.Equal(p.MaxHp, p.Hp);

            // Ten metres from the pad they are out of the yard, and coming back does not bring it back.
            SimDriver.Teleport(p, po.SpawnPadX + 10f, po.SpawnPadZ);
            world.Step();
            Assert.False(world.InYard(p));
            Assert.False(world.Protected(p));
            SimDriver.Teleport(p, po.SpawnPadX, po.SpawnPadZ);
            SimDriver.StepTicks(world, 2);
            Assert.False(world.Protected(p));
            world.DamagePlayer(p, 30f);
            Assert.Equal(p.MaxHp - 30f, p.Hp);
        }

        [Fact]
        public void OutOfTheYardWithinTheFiveSeconds_TheProtectionLastsThemOut()
        {
            var world = NewWorld();
            var p = world.AddPlayer("carrier")!;
            world.DamagePlayer(p, 1000f);
            RunUntil(world, () => !p.Dead, 11);
            var e = world.SpawnEnemy(Db.Enemy("barbarian"), 60f, 28f)!;
            SimDriver.Teleport(p, 52f, 28f);
            SimDriver.StepTicks(world, 4 * Tps);
            Assert.True(world.Protected(p));
            Assert.True(e.Target.IsNone);
            Assert.Equal(p.MaxHp, p.Hp);
            SimDriver.StepTicks(world, Tps + 1);
            Assert.False(world.Protected(p));
            SimDriver.StepTicks(world, Tps);
            Assert.Equal(p.Id, e.Target);
        }

        [Theory]
        [InlineData(InputButtons.Attack)]
        [InlineData(InputButtons.AltAttack)]
        public void ABlowThatLandsEndsTheProtection_OneAtTheAirDoesNot(InputButtons button)
        {
            var world = NewWorld();
            var p = world.AddPlayer("carrier")!;
            var home = world.Map.Destinations[0];
            world.DamagePlayer(p, 1000f);
            RunUntil(world, () => !p.Dead, 11);
            for (int i = 0; i < 2; i++) Assert.True(world.Give(p.Hotbar, ItemStack.OfMail(TestContent.Mail("small", home.Address, _nextMailId++))));
            Assert.True(world.Protected(p));
            // Moving, jumping, hands in the Intake: none of it is a blow.
            world.QueueInput(p.Id, new PlayerInput { Seq = p.LastQueuedSeq + 1, MoveX = PlayerInput.QuantizeAxis(1f), Buttons = InputButtons.Jump | InputButtons.Sprint });
            world.Step();
            Assert.True(world.Protected(p));

            // Nor is a swing at the air or a package into the wall: who hammers the button as they stand up loses nothing.
            Press(world, p, button, yaw: MathF.PI / 2f);
            Assert.Single(world.Events.OfType<AttackEvent>());
            SimDriver.StepTicks(world, Tps);
            Assert.True(world.Protected(p));
            world.DamagePlayer(p, 30f);
            Assert.Equal(p.MaxHp, p.Hp);

            // A raider is hurt: they are in the fight again, in the yard or not.
            var e = world.SpawnEnemy(Db.Enemy("barbarian"), p.Move.X + 2f, p.Move.Z)!;
            SimDriver.StepTicks(world, 2 * Tps);
            Assert.True(e.Target.IsNone);
            SimDriver.Teleport(p, e.X - 2f, e.Z);
            Press(world, p, button);
            for (int i = 0; i < 10 && e.Hp == e.MaxHp; i++) world.Step();
            Assert.True(e.Hp < e.MaxHp);
            Assert.False(world.Protected(p));
            SimDriver.StepTicks(world, 2);
            Assert.Equal(p.Id, e.Target);
            world.DamagePlayer(p, 30f);
            Assert.Equal(p.MaxHp - 30f, p.Hp);
        }

        [Fact]
        public void AFightOnThePad_WhoStandsUpInItIsLeftAlone_AndTakesTheirBagBack()
        {
            // The a5d987e playtest could not take a bag back by hand: down within a second of every revive.
            var world = NewWorld();
            var strong = world.AddPlayer("strong")!;
            var p = world.AddPlayer("carrier")!;
            StartDelivery(world, strong);
            var po = world.Map.PostOffice;
            var home = world.Map.Destinations[0];
            strong.MaxHp = strong.Hp = 100000f;
            Assert.True(world.Give(p.Hotbar, ItemStack.OfMail(TestContent.Mail("letter", home.Address, _nextMailId++))));
            Assert.True(world.Give(p.Inventory, ItemStack.OfItem(Db.Item("stone"), 3)));
            // The raid has followed the two of them home: eight raiders on the pad.
            var pack = new List<Enemy>();
            for (int i = 0; i < 8; i++) pack.Add(world.SpawnEnemy(Db.Enemy("barbarian"), po.SpawnPadX + 2f + i % 4, po.SpawnPadZ + 1f + i / 4)!);
            RunUntil(world, () => p.Dead, 30);
            var bag = Assert.Single(world.Bags);
            Assert.True(world.InYard(p));

            // Ten seconds later they stand up in the middle of it, and nobody turns to them.
            RunUntil(world, () => !p.Dead, 11);
            Assert.True(pack.Count(e => Distance(e, p) < 6f) >= 4, "the fight has left the pad");
            for (int i = 0; i < 8 * Tps; i++)
            {
                world.Step();
                Assert.Equal(p.MaxHp, p.Hp);
                Assert.DoesNotContain(pack, e => e.Target == p.Id);
            }
            Assert.True(world.Protected(p));

            // The bag lies where they fell, in the yard: they open it and take it all back.
            Assert.True(SimDriver.WalkTo(world, p, bag.X, bag.Z, 1.2f));
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.Open(0, bag.Id)));
            foreach (uint entry in bag.Container.Entries.Select(e => e.EntryId).ToList())
                Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.QuickMove(0, bag.Id, entry)));
            Assert.Empty(world.Bags);
            Assert.Equal(1, p.Hotbar.Entries.Count(e => e.Stack.Kind == StackKind.Mail));
            ushort stone = Db.Item("stone").Index;
            Assert.Equal(3, p.Hotbar.Entries.Concat(p.Inventory.Entries).Where(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == stone).Sum(e => e.Stack.Count));
            Assert.Equal(p.MaxHp, p.Hp);
            Assert.True(strong.Hp < strong.MaxHp);
            Assert.True(pack.Count(e => e.Target == strong.Id) >= 4);
        }

        [Fact]
        public void AShiftBoundaryPutsTheFallenOnTheirFeetWithoutProtection()
        {
            var world = NewWorld();
            var p = world.AddPlayer("carrier")!;
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.StartRun(0)));
            world.DamagePlayer(p, 1000f);
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.False(p.Dead);
            Assert.False(world.Protected(p));
            Assert.Equal(0u, world.Events.OfType<PlayerRespawnedEvent>().Single().ProtectedUntilTick);
        }

        [Fact]
        public void OnlyFourRaidersCloseInOnACarrier_TheOthersWaitTheirTurn()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world);
            p.MaxHp = p.Hp = 100000f;
            var raider = Db.Enemy("barbarian");
            var pack = new List<Enemy>();
            for (int i = 0; i < 10; i++) pack.Add(world.SpawnEnemy(raider, 56f + i * 0.7f, 27f + i % 3)!);
            SimDriver.StepTicks(world, 5 * Tps);

            float reach = raider.AttackRange + raider.Radius;
            Assert.All(pack, e => Assert.Equal(p.Id, e.Target));
            Assert.Equal(4, pack.Count(e => Distance(e, p) <= reach));
            Assert.Equal(6, pack.Count(e => e.Waiting));
            Assert.All(pack.Where(e => e.Waiting), e => Assert.InRange(Distance(e, p), 3f, 5.5f));
            Assert.True(p.Hp < p.MaxHp);

            // One of the four falls: the nearest of those waiting steps in.
            var fallen = pack.First(e => !e.Waiting);
            world.DamageEnemy(fallen, 1000f, null);
            pack.Remove(fallen);
            SimDriver.StepTicks(world, 2 * Tps);
            Assert.Equal(4, pack.Count(e => Distance(e, p) <= reach));
            Assert.Equal(5, pack.Count(e => e.Waiting));
        }

        [Fact]
        public void ARaidDoesNotCampThePad()
        {
            // The playtest's raid: two carriers, one of them picks the fight and goes down.
            var world = NewWorld(seed: 21);
            var p = world.AddPlayer("carrier")!;
            var other = world.AddPlayer("other")!;
            ReachShiftTwo(world, p);
            var po = world.Map.PostOffice;
            SimDriver.Teleport(p, po.SpawnPadX - 0.5f, po.SpawnPadZ - 1.5f);
            // Half the raid is in, and has had the time to walk up the street.
            RunUntil(world, () => world.RaidPulsesSpawned == 3, 271);
            SimDriver.StepTicks(world, 14 * Tps);
            Assert.True(world.Enemies.Count >= 4, $"{world.Enemies.Count} raiders");

            // Hands in their pockets on the pad, both are left alone.
            Assert.Equal((p.MaxHp, other.MaxHp), (p.Hp, other.Hp));
            Assert.All(world.Enemies, e => Assert.True(e.Target.IsNone));

            // One walks into the line and is back on the pad ten seconds later …
            world.RaidLine(out float lx, out float lz);
            SimDriver.Teleport(other, lx - 3f, lz);
            RunUntil(world, () => other.Dead, 20);
            Assert.Equal(p.MaxHp, p.Hp);
            RunUntil(world, () => !other.Dead, 11);
            Assert.True(world.Protected(other));

            // … where nobody waits for them: the Intake is theirs, and so is the time to load the cannon.
            SimDriver.StepTicks(world, 6 * Tps);
            Assert.True(world.Protected(other));
            Assert.All(world.Enemies, e =>
            {
                Assert.True(e.Target.IsNone);
                Assert.True(Distance(e, other) > e.Def.AggroRange, $"a {e.Def.Id} is {Distance(e, other):0.0} m from the pad");
            });
            SimDriver.Teleport(other, po.IntakeX, po.IntakeZ + 1.5f);
            SimDriver.StepTicks(world, 5 * Tps);
            Assert.Equal(OpResult.Ok, Request(world, other, SimRequest.Open(0, world.Intake.Id)));
            Assert.Equal((p.MaxHp, other.MaxHp), (p.Hp, other.Hp));
        }

        // --- loot -------------------------------------------------------------------------------

        [Fact]
        public void AFallenGiantLeavesStoneOreAndCoins_ForWhoeverWalksUp()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world);
            var giant = world.SpawnEnemy(Db.Enemy("giant"), 62f, 28f)!;
            world.DamageEnemy(giant, 399f, p);
            Assert.Empty(world.Loot);
            world.DamageEnemy(giant, 1f, null);
            Assert.Equal(1, p.Kills); // the last carrier to hit it

            var bundle = world.Loot.Single();
            var dropped = world.Events.OfType<LootDroppedEvent>().Single();
            Assert.Equal((bundle.Id, giant.Def.Index, false), (dropped.LootId, dropped.EnemyKind, dropped.Mega));
            Assert.InRange(bundle.Coins, 30, 50);
            Assert.Equal(bundle.Coins, dropped.Coins);
            Assert.Equal(new[] { (Db.Item("stone").Index, (byte)3), (Db.Item("iron_ore").Index, (byte)1) },
                dropped.Items.Select(i => (i.Item, i.Count)));

            // Twelve metres off nothing happens; within reach it is taken once it has settled.
            int coins = bundle.Coins;
            SimDriver.StepTicks(world, 2 * Tps);
            Assert.Single(world.Loot);
            SimDriver.Teleport(p, bundle.X - 2f, bundle.Z);
            SimDriver.StepTicks(world, 2);
            Assert.Empty(world.Loot);
            var taken = world.Events.OfType<LootTakenEvent>().Single();
            Assert.Equal((bundle.Id, p.Id, coins, true), (taken.LootId, taken.Player, (int)taken.Coins, taken.Gone));
            Assert.Equal(coins, world.Economy.Wallet);
            Assert.Equal(coins, world.Economy.TotalLoot);
            Assert.Equal(coins, p.LootCoins);
            // Loot is not mail: it is no part of the shift's earnings or the quota.
            Assert.Equal(0, world.Economy.ShiftEarnings);
            Assert.Equal(3, Carried(p, "stone"));
            Assert.Equal(1, Carried(p, "iron_ore"));
            Assert.Empty(world.Events.OfType<LootGoneEvent>());
        }

        [Fact]
        public void LootIsNotTakenTheInstantItFalls()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world);
            var e = world.SpawnEnemy(Db.Enemy("swarmling"), 51f, 28f)!;
            world.DamageEnemy(e, 100f, p);
            SimDriver.StepTicks(world, 14);
            Assert.Single(world.Loot);
            SimDriver.StepTicks(world, 2);
            Assert.Empty(world.Loot);
            Assert.InRange(world.Economy.Wallet, 1, 3);
        }

        [Fact]
        public void UntakenLootIsGoneAfterTwoMinutes()
        {
            var world = NewWorld();
            PlayerOnTheStreet(world, 20f, 28f);
            var e = world.SpawnEnemy(Db.Enemy("giant"), 70f, 28f)!;
            // Nobody to chase would keep it there; kill it where it stands.
            world.DamageEnemy(e, 1000f, null);
            uint id = world.Loot.Single().Id;
            SimDriver.StepTicks(world, 120 * Tps - 1);
            Assert.Single(world.Loot);
            world.Step();
            Assert.Empty(world.Loot);
            Assert.Equal(id, world.Events.OfType<LootGoneEvent>().Single().LootId);
            Assert.Equal(0, world.Economy.Wallet);
        }

        [Fact]
        public void AMegaLeavesThreeTimesTheLoot()
        {
            var world = NewWorld();
            PlayerOnTheStreet(world, 20f, 28f);
            var e = world.SpawnEnemy(Db.Enemy("giant"), 70f, 28f, mega: true)!;
            world.DamageEnemy(e, 5000f, null);
            var bundle = world.Loot.Single();
            Assert.True(bundle.Mega);
            Assert.InRange(bundle.Coins, 90, 150);
            Assert.Equal(new[] { (byte)9, (byte)3 }, bundle.Items.Select(i => i.Count));
        }

        [Fact]
        public void LootThatDoesNotFitStaysOnTheGround()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world);
            // Fill the inventory with parcels to distinct addresses, so nothing stacks.
            var large = Db.MailKind("large");
            int n = 0;
            while (world.Give(p.Inventory, ItemStack.OfMail(TestContent.Mail("large", world.Map.Destinations[n % world.Map.Destinations.Count].Address, _nextMailId++))))
                Assert.True(++n < 200);
            var letter = Db.MailKind("letter");
            while (world.Give(p.Inventory, ItemStack.OfMail(TestContent.Mail("letter", world.Map.Destinations[n % world.Map.Destinations.Count].Address, _nextMailId++))))
                Assert.True(++n < 2000);
            Assert.NotNull(large);
            Assert.NotNull(letter);

            var e = world.SpawnEnemy(Db.Enemy("giant"), 51.5f, 28f)!;
            world.DamageEnemy(e, 1000f, p);
            SimDriver.StepTicks(world, 20);
            var bundle = world.Loot.Single();
            Assert.Equal(0, bundle.Coins);
            Assert.NotEmpty(bundle.Items);
            var taken = world.Events.OfType<LootTakenEvent>().Single();
            Assert.False(taken.Gone);
            Assert.True(world.Economy.Wallet >= 30);
        }

        // --- the raid ---------------------------------------------------------------------------

        [Fact]
        public void TheFirstShiftHasNoRaid()
        {
            var world = NewWorld();
            var p = world.AddPlayer("carrier")!;
            StartDelivery(world, p);
            Assert.Equal(0u, world.RaidStartTick);
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 241);
            Assert.Equal(RaidState.None, world.Raid);
            Assert.Empty(world.Events.OfType<RaidChangedEvent>());
            Assert.Equal(0, world.EnemiesSpawnedTotal);
        }

        [Fact]
        public void TheSecondShiftEndsInARaid_WarnedOf_InSixPulses_OnBudget()
        {
            var world = NewWorld(seed: 21);
            var p = world.AddPlayer("carrier")!;
            ReachShiftTwo(world, p);
            uint end = world.Run.PhaseEndTick;
            uint start = world.RaidStartTick;
            Assert.Equal(end - 90 * Tps, start);
            world.Events.Clear();

            RunUntil(world, () => world.Raid == RaidState.Warning, 271);
            Assert.Equal(start - 15 * Tps, world.Tick);
            var warned = world.Events.OfType<RaidChangedEvent>().Single();
            Assert.Equal((RaidState.Warning, start), (warned.Raid, warned.Tick));
            Assert.Equal(RaidState.Warning, world.GetGlobalState().Raid);
            Assert.Empty(world.Enemies);

            // Every enemy that walks in is counted with its cost and the tick it came on.
            var seen = new HashSet<uint>();
            var spawnTicks = new SortedSet<uint>();
            int cost = 0;
            while (world.Run.Phase == RunPhase.Delivery)
            {
                world.Step();
                foreach (var e in world.Enemies)
                {
                    if (!seen.Add(e.Id.Value)) continue;
                    spawnTicks.Add(world.Tick);
                    cost += e.Def.Cost;
                    Assert.True(e.Def.UnlockShift <= 2);
                    world.RaidGate(out float gx, out float gz);
                    Assert.InRange(e.X, gx - 4f, gx + 1f);
                    Assert.InRange(e.Z, gz - 2f, gz + 2f);
                }
                if (world.Tick == start) Assert.Equal(RaidState.Active, world.Raid);
            }

            double budget = SimWorld.WaveBudget(Db.Waves, 2, 1, 0);
            Assert.Equal(96, budget, 6);
            Assert.Equal(budget, world.RaidBudget, 6);
            // Five pulses of a sixth and a finale of one and a half; less than the cheapest enemy is left over.
            double whole = budget / 6 * 6.5;
            Assert.InRange(cost, whole - 4, whole);
            Assert.Equal(seen.Count, world.EnemiesSpawnedTotal);
            Assert.Equal(6, spawnTicks.Count);
            Assert.Equal(Enumerable.Range(0, 6).Select(i => start + (uint)(i * 15 * Tps)), spawnTicks);

            // The clock ran out: they run, and five seconds later the street is empty.
            Assert.Equal(RunPhase.Payday, world.Run.Phase);
            Assert.Equal(RaidState.Fleeing, world.Raid);
            Assert.All(world.Enemies, e => Assert.Equal(EnemyState.Flee, e.State));
            var fleeing = world.Events.OfType<RaidChangedEvent>().Last();
            Assert.Equal((RaidState.Fleeing, world.Tick + 5 * Tps), (fleeing.Raid, fleeing.Tick));
            SimDriver.StepTicks(world, 5 * Tps);
            Assert.Empty(world.Enemies);

            // Nothing was delivered this shift, so the run ends here; either way the raid is over with the Payday.
            RunUntil(world, () => world.Run.Phase == RunPhase.Results, 21);
            Assert.Equal(RaidState.None, world.Raid);
            Assert.Equal(RaidState.None, world.Events.OfType<RaidChangedEvent>().Last().Raid);
        }

        [Fact]
        public void FleeingRaidersDoNotHit()
        {
            var world = NewWorld();
            var p = world.AddPlayer("carrier")!;
            StartDelivery(world, p);
            RunUntil(world, () => world.Run.PhaseEndTick - world.Tick <= 2, 241);
            // Between the carrier and the gate, already swinging as the shift ends.
            SimDriver.Teleport(p, 60f, 28f);
            var e = world.SpawnEnemy(Db.Enemy("barbarian"), 61.5f, 28f)!;
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 1);
            Assert.Equal(EnemyState.Flee, e.State);
            float hp = p.Hp;
            float x = e.X;
            SimDriver.StepTicks(world, Tps);
            Assert.Equal(hp, p.Hp);
            Assert.InRange(e.X - x, 6f, 7f); // 4.5 m/s × 1.5
            SimDriver.StepTicks(world, 4 * Tps);
            Assert.Empty(world.Enemies);
        }

        [Fact]
        public void RaidsCanBeSwitchedOff()
        {
            var world = NewWorld(raids: false);
            var p = world.AddPlayer("carrier")!;
            ReachShiftTwo(world, p);
            Assert.Equal(0u, world.RaidStartTick);
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 271);
            Assert.Empty(world.Events.OfType<RaidChangedEvent>());
            Assert.Equal(0, world.EnemiesSpawnedTotal);
        }

        [Fact]
        public void NothingOfTheFightOutlivesItsPhase()
        {
            var world = NewWorld();
            var p = world.AddPlayer("carrier")!;
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.StartRun(0)));
            SimDriver.Teleport(p, 30f, 28f);
            var e = world.SpawnEnemy(Db.Enemy("giant"), 70f, 28f)!;
            world.SpawnEnemy(Db.Enemy("barbarian"), 72f, 28f);
            world.DamageEnemy(e, 1000f, null);
            uint loot = world.Loot.Single().Id;
            world.DamagePlayer(p, 1000f);
            Assert.True(p.Dead);

            // The fallen can still say they are ready.
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.Empty(world.Enemies);
            Assert.Empty(world.Loot);
            Assert.Equal(loot, world.Events.OfType<LootGoneEvent>().Single().LootId);
            Assert.False(p.Dead);
            Assert.Equal(p.MaxHp, p.Hp);
        }

        [Fact]
        public void ARaidIsTheSameForTheSameSeed()
        {
            string Play(uint seed)
            {
                var world = NewWorld(seed);
                var p = world.AddPlayer("carrier")!;
                ReachShiftTwo(world, p);
                SimDriver.Teleport(p, 60f, 28f);
                var log = new System.Text.StringBuilder();
                while (world.Run.Phase == RunPhase.Delivery)
                {
                    Press(world, p, InputButtons.Attack);
                    if (world.Tick % 150 != 0) continue;
                    log.Append(world.Tick).Append(':').Append(p.Hp.ToString("R")).Append(' ').Append(p.Kills).Append(' ').Append(world.Economy.Wallet);
                    foreach (var e in world.Enemies)
                        log.Append('|').Append(e.Def.Id).Append(e.Mega ? "M" : "").Append(' ').Append(e.X.ToString("R")).Append(' ').Append(e.Z.ToString("R")).Append(' ').Append(e.Hp);
                    log.Append('\n');
                }
                Assert.True(world.EnemiesSpawnedTotal > 0);
                return log.ToString();
            }

            string a = Play(5);
            Assert.Equal(a, Play(5));
            Assert.NotEqual(a, Play(6));
        }
    }
}
