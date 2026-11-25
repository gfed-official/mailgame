using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Client.UI;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.App
{
    /// <summary>
    /// The fight of the concept video, played through the client's own input in the Lobby (so the
    /// loot does not disturb the run's books): a raider felled with the hammer, its loot on the
    /// cards and in the wallet; then a giant shot with the mail cannon, its blow on the health bar.
    /// The enemy on screen, the weapons in the hands and the HUD must all follow.
    /// </summary>
    public partial class SmokeDriver
    {
        private const int CombatStepFrames = 400;
        private const float RaiderDistance = 2.2f, GiantDistance = 10f;

        private int _fightStep, _fightFrames;
        private Enemy _raider = null!, _giant = null!;
        private int _fightKills, _fightSwings, _fightShots, _fightBlasts, _fightWallet, _raiderCoins;
        /// <summary>When the raider fell (ms): the hammer comes up in real time, the fight runs at fixed ticks a frame, so it gets half a second.</summary>
        private ulong _raiderFellMsec;
        private const ulong HammerWaitMsec = 500;
        private bool _sawWeaponsLine, _sawHammerDrawn, _sawCannonOut, _sawLootOnGround, _sawEnemyView;

        private void StartCombat()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var player = server.GetPlayer(world.LocalId);
            if (player == null) { Fail("the server has no player for the host"); return; }
            if (!ClearLine(server, player.Move.X, player.Move.Z, RaiderDistance, out float x, out float z)) { Fail("no open ground beside the player for a raider"); return; }
            var raider = server.SpawnEnemy(_session.Content.Enemy("barbarian"), x, z);
            if (raider == null) { Fail("the server would not spawn a raider"); return; }
            _raider = raider;
            _fightKills = world.LocalKills;
            _fightSwings = client.LocalHammerSwings;
            _fightShots = client.LocalCannonShots;
            _fightBlasts = _screen.Effects.Blasts;
            _fightWallet = server.Economy.Wallet;
            // The cards of the run's own raid may still be up: the run went by in seconds.
            _screen.Hud.Loot.Cards.Clear();
            _fightStep = 0;
            _fightFrames = 0;
            Enter(Stage.Combat);
        }

        /// <summary>
        /// A point that far from the player with nothing solid on the straight line to it (the
        /// cannon's package stops at the first wall). Looks west first: the smoke's chest stands east.
        /// </summary>
        private static bool ClearLine(SimWorld server, float px, float pz, float distance, out float x, out float z)
        {
            var map = server.Map;
            const float tile = SimConstants.TileSize;
            for (int i = 0; i < 16; i++)
            {
                float angle = MathF.PI + i * MathF.Tau / 16f;
                float dx = MathF.Cos(angle), dz = MathF.Sin(angle);
                bool clear = true;
                for (float d = 0.5f; d <= distance + 1f && clear; d += 0.5f)
                    clear = !map.IsNavBlocked((int)MathF.Floor((px + dx * d) / tile), (int)MathF.Floor((pz + dz * d) / tile));
                if (!clear) continue;
                x = px + dx * distance;
                z = pz + dz * distance;
                return true;
            }
            x = z = 0f;
            return false;
        }

        private static ClientEnemy? Seen(ClientWorld world, Enemy enemy) =>
            world.Enemies.TryGetValue((ushort)enemy.Id.Value, out var seen) ? seen : null;

        /// <summary>Turns the view to a point on the ground and holds (or lets go of) a button.</summary>
        private void AimAt(float x, float z, InputButtons buttons)
        {
            var client = _session.Client;
            var local = client.World!.Local;
            client.Input.YawRadians = MathF.Atan2(-(x - local.X), -(z - local.Z));
            client.Input.PitchRadians = 0f;
            client.Input.Buttons = buttons;
        }

        /// <summary>Walks to the loot on the ground, if there is any out of reach.</summary>
        private void WalkToLoot(ClientWorld world)
        {
            var client = _session.Client;
            client.Input.MoveX = 0f;
            client.Input.MoveZ = 0f;
            foreach (var loot in world.Loot.Values)
            {
                var to = new Vector2(loot.X - world.Local.X, loot.Z - world.Local.Z);
                if (to.Length() < 1.2f) return;
                to = to.Normalized();
                client.Input.MoveX = to.X;
                client.Input.MoveZ = to.Y;
                return;
            }
        }

        private void NextFightStep()
        {
            _fightStep++;
            _fightFrames = 0;
        }

        private void StepCombat()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var player = server.GetPlayer(world.LocalId)!;
            var cards = hud.Loot.Cards;
            hud.Refresh();
            TrackCamera();
            if (hud.WeaponsText.Contains("hammer", StringComparison.Ordinal)) _sawWeaponsLine = true;
            if (_screen.Camera.WeaponDrawn && !_screen.Camera.CannonOut) _sawHammerDrawn = true;
            if (_screen.Camera.CannonOut) _sawCannonOut = true;
            if (_screen.Effects.LootViewCount > 0) _sawLootOnGround = true;
            if (_screen.Enemies.ViewCount > 0) _sawEnemyView = true;
            foreach (char c in FontCheck.MissingGlyphs(hud))
                if (_missingGlyphs.IndexOf(c) < 0) _missingGlyphs += c;

            if (++_fightFrames > CombatStepFrames)
            {
                string ground = string.Join("; ", server.Loot.Select(l => $"{l.Coins} coins and {l.Items.Count} kinds of item at {l.X:0.0},{l.Z:0.0}"));
                Fail($"combat step {_fightStep} did not finish: {world.Enemies.Count} enemies seen ({server.Enemies.Count} on the server), {world.Loot.Count} loot seen, "
                    + $"on the server [{ground}] with the player at {player.Move.X:0.0},{player.Move.Z:0.0}, "
                    + $"kills {world.LocalKills - _fightKills}, swings {client.LocalHammerSwings - _fightSwings}, shots {client.LocalCannonShots - _fightShots}, ammo {client.CannonAmmo}, blasts {_screen.Effects.Blasts}, "
                    + $"hp {world.LocalHp:0}, cards \"{cards.Title}\" x{cards.Cards.Count}, wallet {world.Global.Wallet} (server {server.Economy.Wallet})");
                return;
            }

            switch (_fightStep)
            {
                case 0:
                {
                    // The raider is on the client and on the screen; the hammer comes out with the first swing.
                    var seen = Seen(world, _raider);
                    if (seen == null || _screen.Enemies.ViewCount < 1) break;
                    Check(seen.Def.Id == "barbarian" && !seen.Mega, $"the client sees the raider as a {seen.Def.Id}");
                    Check(hud.WeaponsText == HudText.Weapons(0), $"with a raider about the weapons line is \"{hud.WeaponsText}\"");
                    AimAt(seen.X, seen.Z, InputButtons.Attack);
                    NextFightStep();
                    break;
                }

                case 1:
                {
                    var seen = Seen(world, _raider);
                    if (seen != null) AimAt(seen.X, seen.Z, InputButtons.Attack);
                    if (_sawHammerDrawn) Shot("hammer");
                    if (world.LocalKills == _fightKills) break;
                    client.Input.Buttons = InputButtons.None;
                    // Headless frames can be quicker than the hammer's tenth of a second to come up.
                    if (_raiderFellMsec == 0) _raiderFellMsec = Time.GetTicksMsec();
                    if (!_sawHammerDrawn && Time.GetTicksMsec() - _raiderFellMsec < HammerWaitMsec) break;
                    Check(client.LocalHammerSwings - _fightSwings >= 3, $"the raider fell after {client.LocalHammerSwings - _fightSwings} swings of the hammer");
                    Check(_session.Feed.Entries.Any(e => e.Text == "You felled a Raider"), "the feed does not say the raider was felled");
                    Check(_sawHammerDrawn, "the hammer never showed in the player's hands");
                    Check(_sawWeaponsLine, "the HUD never named the weapons");
                    _raiderCoins = server.Loot.Count > 0 ? server.Loot[0].Coins : server.Economy.Wallet - _fightWallet;
                    Check(_raiderCoins > 0, "the fallen raider left no coins");
                    NextFightStep();
                    break;
                }

                case 2:
                    // Walking over the loot takes it: the cards over the hotbar, the coins in the wallet.
                    WalkToLoot(world);
                    if (!cards.Visible || world.Global.Wallet != server.Economy.Wallet || server.Loot.Count > 0) break;
                    client.Input.MoveX = 0f;
                    client.Input.MoveZ = 0f;
                    Check(_sawLootOnGround, "the raider's loot was never drawn on the ground");
                    Check(hud.Loot.Visible, "the loot cards are not on screen");
                    Check(cards.Title == "Raider", $"the loot cards are titled \"{cards.Title}\"");
                    Check(cards.Cards[0].Coins && cards.Cards[0].Value == _raiderCoins, $"the coins card shows {cards.Cards[0].Value}, the raider left {_raiderCoins}");
                    Check(server.Economy.Wallet == _fightWallet + _raiderCoins, $"the wallet went from {_fightWallet} to {server.Economy.Wallet} for {_raiderCoins} coins");
                    Check(hud.WalletText == HudText.Wallet(world.Global), $"with the loot taken the HUD shows \"{hud.WalletText}\", expected \"{HudText.Wallet(world.Global)}\"");
                    Check(GetViewport().GetVisibleRect().Encloses(hud.Loot.GetGlobalRect()), $"the loot cards at {hud.Loot.GetGlobalRect()} are not inside the screen");
                    Shot("lootcards");
                    NextFightStep();
                    break;

                case 3:
                {
                    // A giant down the street, a Small Package for the cannon, and a carrier in full health.
                    if (_fightFrames < 3) break;
                    if (!ClearLine(server, player.Move.X, player.Move.Z, GiantDistance, out float x, out float z)) { Fail("no clear line from the player for the mail cannon"); return; }
                    var giant = server.SpawnEnemy(_session.Content.Enemy("giant"), x, z);
                    if (giant == null) { Fail("the server would not spawn a giant"); return; }
                    _giant = giant;
                    player.Hp = player.MaxHp;
                    var package = new MailItem { Id = 900200, Kind = _session.Content.MailKind("small").Index, Address = world.Map.Destinations[0].Address, Value = 30, SpawnShift = 1, DeadlineShift = 1 };
                    Check(server.Give(player.Hotbar, ItemStack.OfMail(package)), "a Small Package did not fit the hotbar");
                    NextFightStep();
                    break;
                }

                case 4:
                {
                    var seen = Seen(world, _giant);
                    if (seen == null || client.CannonAmmo == 0) break;
                    Check(hud.WeaponsText == HudText.Weapons(1), $"with a package to fire the weapons line is \"{hud.WeaponsText}\"");
                    AimAt(seen.X, seen.Z, InputButtons.AltAttack);
                    NextFightStep();
                    break;
                }

                case 5:
                {
                    // One package: fired, gone from the hotbar, and it goes off on the giant.
                    var seen = Seen(world, _giant);
                    if (seen != null) AimAt(seen.X, seen.Z, client.LocalCannonShots == _fightShots ? InputButtons.AltAttack : InputButtons.None);
                    if (_sawCannonOut) Shot("cannon");
                    if (_screen.Effects.Blasts > _fightBlasts) Shot("blast");
                    if (client.LocalCannonShots == _fightShots || _screen.Effects.Blasts == _fightBlasts || client.CannonAmmo != 0) break;
                    Check(client.LocalCannonShots - _fightShots == 1, $"one package made {client.LocalCannonShots - _fightShots} cannon shots");
                    Check(_sawCannonOut, "the mail cannon never showed in the player's hands");
                    Check(_giant.Hp < _giant.MaxHp, "the cannon's package did not hurt the giant");
                    Check(hud.WeaponsText == HudText.Weapons(0), $"with the package fired the weapons line is \"{hud.WeaponsText}\"");
                    NextFightStep();
                    break;
                }

                case 6:
                {
                    // The giant walks up and lands a blow: the health bar and its number drop together.
                    var seen = Seen(world, _giant);
                    if (seen != null) AimAt(seen.X, seen.Z, InputButtons.None);
                    if (world.LocalHp >= world.LocalMaxHp) break;
                    Check(hud.HealthText == HudText.Health(world.LocalHp), $"hurt to {world.LocalHp:0.0} the HUD shows \"{hud.HealthText}\"");
                    Check(hud.HealthShown < 1f && Mathf.Abs(hud.HealthShown - world.LocalHp / world.LocalMaxHp) < 0.01f, $"hurt to {world.LocalHp:0.0} of {world.LocalMaxHp:0} the health bar is at {hud.HealthShown:0.00}");
                    Check(!world.Local.Dead, "one blow of a giant felled a carrier in full health");
                    Shot("giant");
                    server.DamageEnemy(_giant, 9999f, player);
                    NextFightStep();
                    break;
                }

                case 7:
                    // Its stone and ore come with the coins, under its own name.
                    WalkToLoot(world);
                    if (cards.Title != "Horned Giant" || server.Loot.Count > 0 || world.Loot.Count > 0 || world.Global.Wallet != server.Economy.Wallet) break;
                    client.Input.MoveX = 0f;
                    client.Input.MoveZ = 0f;
                    Check(world.LocalKills - _fightKills == 2, $"two enemies felled, the client counts {world.LocalKills - _fightKills}");
                    Check(_session.Feed.Entries.Any(e => e.Text == "You felled a Horned Giant"), "the feed does not say the giant was felled");
                    Check(cards.Cards.Any(c => !c.Coins && c.Name == "Stone" && c.Count == 3), "the giant's three stones are not on the loot cards");
                    // The raider's cards may still be up: then the coins of both are on the one card.
                    int giantCoins = server.Economy.Wallet - _fightWallet - _raiderCoins;
                    Check(giantCoins > 0 && cards.Cards[0].Coins && (cards.Cards[0].Value == giantCoins || cards.Cards[0].Value == giantCoins + _raiderCoins),
                        $"the coins card shows {cards.Cards[0].Value}, the giant left {giantCoins}");
                    Check(hud.WalletText == HudText.Wallet(world.Global), $"with the giant's loot taken the HUD shows \"{hud.WalletText}\", expected \"{HudText.Wallet(world.Global)}\"");
                    Shot("giantloot");
                    NextFightStep();
                    break;

                case 8:
                    // Nothing is left behind: no enemy, no body on screen, no loot.
                    if (world.Enemies.Count > 0 || _screen.Enemies.ViewCount > 0 || _screen.Effects.LootViewCount > 0) break;
                    Check(_sawEnemyView, "no enemy was ever drawn");
                    Check(server.Enemies.Count == 0 && server.Loot.Count == 0, $"the fight left {server.Enemies.Count} enemies and {server.Loot.Count} loot bundles on the server");
                    Check(player.Hotbar.ComputeHash() == world.Hotbar.ComputeHash() && player.Inventory.ComputeHash() == world.Inventory.ComputeHash(), "after the fight the player's containers differ from the server's");
                    Check(client.ContainerDesyncs == 0 && client.BadPackets == 0, $"after the fight: {client.ContainerDesyncs} desyncs, {client.BadPackets} bad packets");
                    Check(_maxEyeOffset <= 0.001f && _maxAimOffset <= 0.001f, $"in the fight the camera strayed {_maxEyeOffset:0.000} m from the eyes and {_maxAimOffset:0.000} from the aim");
                    Check(_missingGlyphs.Length == 0, $"the UI font has no glyph for: {_missingGlyphs}");
                    GD.Print($"[smoke] combat: a raider felled with {client.LocalHammerSwings - _fightSwings} swings, a giant shot with the mail cannon and felled; "
                        + $"loot +{HudText.Gold(server.Economy.Wallet - _fightWallet)}; health {hud.HealthText}");
                    client.Input.Buttons = InputButtons.None;
                    StartBag();
                    break;
            }
        }
    }
}
