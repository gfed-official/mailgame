using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Client.UI;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using GridContainer = PerformativeMail.Sim.Inventory.GridContainer;

namespace PerformativeMail.App
{
    /// <summary>
    /// The shop in the smoke's run (doc 09 §2.8): in the first Prep the wallet allows it the host
    /// stands at the counter, opens it through the prompt, sees the server's shelf on the cards,
    /// buys what heals with the card's key, sells one back, uses one, sells the rest and what the
    /// land gave before the visit — and the team's money is right after every step, on both ends.
    /// </summary>
    public partial class SmokeDriver
    {
        private const int ShopStepFrames = 150;
        private const float ShopStandOff = 1.0f;

        private int _shopStep, _shopFrames, _shopVisits, _shopWallet, _shopSlot, _shopGathered;
        private ShopItemDef? _shopCard;

        private void NextShopStep()
        {
            _shopStep++;
            _shopFrames = 0;
        }

        private static int Units(GridContainer container, ItemDef item)
        {
            int n = 0;
            foreach (var e in container.Entries)
                if (e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == item.Index) n += e.Stack.Count;
            return n;
        }

        /// <summary>The stacks in a container that are not mail.</summary>
        private static int Goods(GridContainer container)
        {
            int n = 0;
            foreach (var e in container.Entries)
                if (e.Stack.Kind == StackKind.Item) n++;
            return n;
        }

        /// <summary>A frame of Prep. Returns true while the visit to the counter goes on: the go key waits for it.</summary>
        private bool StepShop()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var po = world.Map.PostOffice;
            var player = server.GetPlayer(world.LocalId)!;

            if (_shopStep == 0)
            {
                if (_shopVisits > 0 || !world.ShopServes || world.Local.Dead) return false;
                // What heals is what the visit is for; the cheapest card of it.
                ShopItemDef? card = null;
                foreach (var def in world.ShopShelf)
                    if (def.Item != null && def.Item.Heal > 0 && def.Count >= 3 && (card == null || world.ShopPrice(def) < world.ShopPrice(card))) card = def;
                if (card == null || world.Global.Wallet != server.Economy.Wallet || world.ShopState(card) != OpResult.Ok) return false;
                _shopCard = card;
                // The pace of a player again, and the view on the counter.
                _session.FixedTicksPerFrame = TicksPerFrame;
                player.Move.X = po.ShopX + ShopStandOff;
                player.Move.Z = po.ShopZ;
                client.Input.YawRadians = Mathf.Pi * 0.5f;
                client.Input.PitchRadians = -0.3f;
                NextShopStep();
                return true;
            }

            var item = _shopCard!.Item!;
            var panel = _screen.Inventory.Shop;
            int price = world.ShopPrice(_shopCard), count = _shopCard.Count;
            // The counter is opened and closed as a player does it: through the prompt, with a press of interact.
            _session.Interaction.Update(1.0 / 60.0, (_shopStep == 2 || _shopStep == 9) && _shopFrames == 0, false);
            hud.Refresh();
            var prompt = _session.Interaction.Prompt;
            bool settled = client.PendingRequestCount == 0 && world.Global.Wallet == server.Economy.Wallet;
            int held = Units(world.Hotbar, item);
            if (++_shopFrames > ShopStepFrames)
            {
                Fail($"the visit to the shop stopped at step {_shopStep}: prompt {prompt.Kind}, cards {(world.ShopOpen ? "up" : "down")}, wallet {world.Global.Wallet} (server {server.Economy.Wallet}), "
                    + $"{held} {item.Name} in the hotbar, {client.PendingRequestCount} requests pending");
                return false;
            }

            switch (_shopStep)
            {
                case 1:
                {
                    float dx = world.Local.X - (po.ShopX + ShopStandOff), dz = world.Local.Z - po.ShopZ;
                    if (dx * dx + dz * dz > 0.1f || prompt.Kind != InteractKind.OpenShop || _shopFrames < 4) break;
                    string want = HudText.Prompt(world, prompt, out _);
                    Check(!prompt.ShopClosed && hud.PromptText == want && want.StartsWith("Open Shop", StringComparison.Ordinal), $"at the shop's counter in Prep the prompt says \"{hud.PromptText}\"");
                    var sign = _screen.World.GetNodeOrNull<Label3D>("Shop");
                    Check(sign != null && sign.Text == "SHOP", "the shop's counter has no sign");
                    Check(_screen.Mouse.Wanted, "before the shop's cards are up the mouse is not the view's");
                    Shot("shop-counter");
                    NextShopStep();
                    break;
                }

                case 2:
                {
                    // The press was this frame's; the screen shows the cards from the next one on.
                    if (_shopFrames < 4) break;
                    var shelf = world.ShopShelf;
                    string want = HudText.ShopText(world);
                    Check(want.IndexOfAny(new[] { '$', '¢' }) < 0 && want.Contains(" " + HudText.GoldUnit, StringComparison.Ordinal), "the shop's prices are not in gold");
                    Check(world.ShopOpen && _screen.Inventory.Visible && panel.Visible, "the press of interact at the counter did not put the cards up");
                    Check(!_screen.Mouse.Wanted, "with the shop's cards up the mouse is not free for them");
                    Check(want.Length > 0 && panel.Text == want, $"the shop shows \"{panel.Text}\", expected \"{want}\"");
                    string cards = string.Join(", ", world.ShopSpecials.Select(d => d.Id)), truth = string.Join(", ", server.ShopSpecials.Select(d => d.Id));
                    Check(cards == truth && panel.SpecialCount == world.ShopSpecials.Count, $"the client's specials are {cards} ({panel.SpecialCount} cards framed), the server's {truth}");
                    int greyed = shelf.Count(d => world.ShopState(d) != OpResult.Ok);
                    Check(panel.CardCount == shelf.Count && panel.GreyedCount == greyed, $"{panel.CardCount} cards for {shelf.Count} entries, {panel.GreyedCount} greyed where {greyed} cannot be bought");
                    var view = GetViewport().GetVisibleRect();
                    for (int i = 0; i < panel.CardCount; i++)
                        Check(view.Encloses(panel.CardAt(i).GetGlobalRect()), $"shop card {i + 1} at {panel.CardAt(i).GetGlobalRect()} is not inside the view");
                    Check(!hud.Overlay.Visible, "a summary panel is shown under the shop's cards");
                    foreach (char c in FontCheck.MissingGlyphs(_screen.Inventory))
                        if (_missingGlyphs.IndexOf(c) < 0) _missingGlyphs += c;
                    Shot("shop");

                    // A card that cannot be bought asks the server nothing; the key of ours buys it.
                    int locked = -1, place = -1;
                    for (int i = 0; i < shelf.Count; i++)
                    {
                        if (shelf[i] == _shopCard) place = i;
                        else if (world.ShopState(shelf[i]) != OpResult.Ok) locked = i;
                    }
                    if (locked >= 0)
                    {
                        panel.Click(locked);
                        Check(panel.LastRequestId == 0, $"a click on the greyed card {locked + 1} sent a request");
                    }
                    _shopWallet = server.Economy.Wallet;
                    Check(client.PressShopKey(place) && world.IsBuying(_shopCard), $"key {HudText.CardKey(place)} did not buy {_shopCard.Name} ({world.ShopState(_shopCard)})");
                    NextShopStep();
                    break;
                }

                case 3:
                {
                    if (!settled || world.IsBuying(_shopCard) || held + Units(world.Inventory, item) < count) break;
                    Check(server.Economy.Wallet == _shopWallet - price, $"{_shopCard.Name} at {price}: the wallet went from {_shopWallet} to {server.Economy.Wallet}");
                    Check(held == count, $"{held} of the {count} {item.Name} bought are in the hotbar");
                    string line = HudText.ShopBought(world, world.LocalId, _shopCard, price);
                    Check(_session.Feed.Entries.Any(e => e.Text == line), $"the feed does not say \"{line}\"");
                    var entry = world.Hotbar.Entries.FirstOrDefault(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == item.Index);
                    if (entry == null) { Fail("what was bought is not in the hotbar"); return false; }
                    _shopSlot = entry.X;
                    // One of them back over the counter.
                    Check(client.Sell(world.HotbarId, entry.EntryId, 1) != 0, "the open shop took no request to sell");
                    NextShopStep();
                    break;
                }

                case 4:
                {
                    if (!settled || held != count - 1) break;
                    Check(server.Economy.Wallet == _shopWallet - price + item.SellPrice, $"one {item.Name} sold at {item.SellPrice}: the wallet is {server.Economy.Wallet}, was {_shopWallet} before the visit");
                    string line = HudText.ShopSold(world, world.LocalId, item.Index, 1, item.SellPrice);
                    Check(_session.Feed.Entries.Any(e => e.Text == line), $"the feed does not say \"{line}\"");
                    Check(panel.Text == HudText.ShopText(world), "the shop's cards did not follow the wallet");
                    // Hurt, with the stack in the hand: the next one is used.
                    player.Hp = player.MaxHp - item.Heal - 10f;
                    client.Input.HeldSlot = (byte)_shopSlot;
                    NextShopStep();
                    break;
                }

                case 5:
                {
                    if (_shopFrames < 4) break;
                    var entry = world.Hotbar.EntryAt(_shopSlot, 0);
                    string want = entry == null ? "" : HudText.Held(world, entry.Stack);
                    Check(want.Contains(HudText.UseKey, StringComparison.Ordinal) && hud.HeldText == want, $"with {item.Name} in the hand the HUD says \"{hud.HeldText}\", expected \"{want}\"");
                    Check(client.UseHeld() != 0, $"the use key did nothing with {item.Name} in the hand");
                    NextShopStep();
                    break;
                }

                case 6:
                {
                    if (!settled || held != count - 2) break;
                    Check(player.Hp >= player.MaxHp - 10.01f && player.Hp < player.MaxHp, $"a {item.Name} used at {player.MaxHp - item.Heal - 10f:0} HP left {player.Hp:0.0} of {player.MaxHp:0}");
                    var entry = world.Hotbar.EntryAt(_shopSlot, 0);
                    // The rest goes back: the run goes on with empty pockets.
                    Check(entry != null && client.Sell(world.HotbarId, entry.EntryId) != 0, "the open shop took no request to sell the rest");
                    NextShopStep();
                    break;
                }

                case 7:
                {
                    if (!settled || held != 0) break;
                    int want = _shopWallet - price + item.SellPrice * (count - 1);
                    Check(server.Economy.Wallet == want, $"after the visit the wallet is {server.Economy.Wallet}, expected {want}");
                    Check(panel.Text == HudText.ShopText(world), "the shop's cards did not follow the wallet");
                    player.Hp = player.MaxHp;
                    client.Input.HeldSlot = 0;
                    // What the land gave goes over the counter too: the run goes on with nothing but mail in the pockets.
                    _shopGathered = 0;
                    foreach (uint id in new[] { world.HotbarId, world.InventoryId })
                        foreach (var e in world.Prediction.View(id)!.Entries.ToArray())
                        {
                            if (e.Stack.Kind != StackKind.Item) continue;
                            var def = world.Content.Items[e.Stack.DefIndex];
                            int value = ShopRules.SaleValue(def, e.Stack.Count);
                            Check(value > 0 && client.Sell(id, e.EntryId) != 0, $"the open shop took no request to sell {def.Name} x{e.Stack.Count}");
                            _shopGathered += value;
                        }
                    NextShopStep();
                    break;
                }

                case 8:
                {
                    if (!settled || Goods(world.Hotbar) + Goods(world.Inventory) != 0) break;
                    int want = _shopWallet - price + item.SellPrice * (count - 1) + _shopGathered;
                    Check(server.Economy.Wallet == want, $"with {_shopGathered} for what was gathered the wallet is {server.Economy.Wallet}, expected {want}");
                    Check(_shopGathered > 0 == _harvestVisits > 0, $"the counter paid {_shopGathered} for what was gathered on {_harvestVisits} rounds of the nodes");
                    Check(panel.Text == HudText.ShopText(world), "the shop's cards did not follow the wallet");
                    NextShopStep();
                    break;
                }

                case 9:
                    // The press was this frame's.
                    if (_shopFrames < 4) break;
                    Check(!world.ShopOpen && !_screen.Inventory.Visible, "the press of interact did not take the cards down");
                    // Issue (c) of the 25fbca5 playtest: no click is needed to look around again.
                    Check(_screen.Mouse.Wanted && !_screen.Mouse.Released, "with the shop's cards down the mouse is not the view's again");
                    CheckMouseNote();
                    GD.Print($"[smoke] shop: {world.ShopShelf.Count} cards ({world.ShopSpecials.Count} special), {_shopCard.Name} for {HudText.Gold(price)}, "
                        + $"{count - 1} sold back, 1 used, what was gathered sold for {HudText.Gold(_shopGathered)}; wallet {HudText.Gold(_shopWallet)} -> {HudText.Gold(server.Economy.Wallet)}");
                    _shopVisits++;
                    _shopStep = 0;
                    _session.FixedTicksPerFrame = RunOutTicksPerFrame;
                    return false;
            }
            return true;
        }

        private int _paydayShopStep, _paydayShopFrames, _paydayShopVisits, _paydayWallet, _paydayUnits, _paydaySold;
        private ShopItemDef? _paydayCard;

        private void NextPaydayShopStep()
        {
            _paydayShopStep++;
            _paydayShopFrames = 0;
        }

        /// <summary>
        /// A frame of a Payday whose summary has been checked (playtest 25fbca5: the counter was a
        /// walk away inside a 20 s window). The host stands at the far end of the street: the hint
        /// names the shop key, the key puts the cards up there, a card is bought and sold back, and
        /// the summary is back when the cards are down.
        /// </summary>
        private void StepPaydayShop()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var po = world.Map.PostOffice;
            var player = server.GetPlayer(world.LocalId)!;
            var panel = _screen.Inventory.Shop;

            if (_paydayShopStep == 0)
            {
                if (_paydayShopVisits > 0 || !world.ShopServes || world.Local.Dead || world.PhaseSecondsLeft < 10
                    || !hud.HintText.Contains(HudText.ShopKey, StringComparison.Ordinal)) return;
                // The mailbox farthest from the counter, at the pace of a player.
                float far = 0f;
                foreach (var dest in world.Map.Destinations)
                {
                    float ddx = dest.MailboxX - po.ShopX, ddz = dest.MailboxZ - po.ShopZ, d = ddx * ddx + ddz * ddz;
                    if (d <= far) continue;
                    far = d;
                    player.Move.X = dest.MailboxX + 1.0f;
                    player.Move.Z = dest.MailboxZ;
                }
                _session.FixedTicksPerFrame = 1;
                NextPaydayShopStep();
                return;
            }

            _session.Interaction.Update(1.0 / 60.0, false, false);
            hud.Refresh();
            bool settled = client.PendingRequestCount == 0 && world.Global.Wallet == server.Economy.Wallet;
            int held = _paydayCard?.Item == null ? 0 : Units(world.Hotbar, _paydayCard.Item) + Units(world.Inventory, _paydayCard.Item);
            if (++_paydayShopFrames > ShopStepFrames)
            {
                Fail($"shopping at Payday stopped at step {_paydayShopStep}: cards {(world.ShopOpen ? "up" : "down")}, wallet {world.Global.Wallet} (server {server.Economy.Wallet}), "
                    + $"{held} of {_paydayCard?.Name ?? "nothing"} carried, {client.PendingRequestCount} requests pending");
                _paydayShopStep = 0;
                _paydayShopVisits++;
                return;
            }

            switch (_paydayShopStep)
            {
                case 1:
                {
                    float sx = world.Local.X - player.Move.X, sz = world.Local.Z - player.Move.Z;
                    if (sx * sx + sz * sz > 0.1f || _paydayShopFrames < 4) break;
                    float dx = world.Local.X - po.ShopX, dz = world.Local.Z - po.ShopZ, reach = world.Content.Balance.GetFloat("serverShopRange");
                    Check(dx * dx + dz * dz > (reach + 5f) * (reach + 5f), $"the host is {MathF.Sqrt(dx * dx + dz * dz):0.0} m from the counter: not far enough to tell");
                    Check(hud.HintText.EndsWith($"{HudText.ShopKey} shop", StringComparison.Ordinal), $"at Payday the HUD hint is \"{hud.HintText}\"");
                    Check(_session.Interaction.Prompt.Kind != InteractKind.OpenShop && !world.ShopOpen, "the counter is prompted for from across the map");
                    Check(client.ToggleShop() && world.ShopOpen, "the shop key did not put the cards up at Payday");
                    NextPaydayShopStep();
                    break;
                }

                case 2:
                {
                    if (_paydayShopFrames < 4) break;
                    string want = HudText.ShopText(world);
                    Check(_screen.Inventory.Visible && panel.Visible && panel.Text == want, $"at Payday the shop shows \"{panel.Text}\", expected \"{want}\"");
                    Check(!hud.Overlay.Visible, "the Payday summary is shown under the shop's cards");
                    Check(!_screen.Mouse.Wanted, "with the shop's cards up at Payday the mouse is not free for them");
                    Check(_session.Interaction.Prompt.Kind == InteractKind.CloseShop, $"with the cards up at Payday the prompt is {_session.Interaction.Prompt.Kind}");
                    Shot("payday-shop");
                    var shelf = world.ShopShelf;
                    int place = -1;
                    for (int i = 0; i < shelf.Count; i++)
                        if (shelf[i].Item != null && world.ShopState(shelf[i]) == OpResult.Ok && (place < 0 || world.ShopPrice(shelf[i]) < world.ShopPrice(shelf[place]))) place = i;
                    if (place < 0) { Fail($"with {HudText.Gold(world.Global.Wallet)} at Payday no card can be bought"); return; }
                    _paydayCard = shelf[place];
                    _paydayWallet = server.Economy.Wallet;
                    _paydayUnits = Units(world.Hotbar, _paydayCard.Item!) + Units(world.Inventory, _paydayCard.Item!);
                    Check(client.PressShopKey(place) && world.IsBuying(_paydayCard), $"key {HudText.CardKey(place)} did not buy {_paydayCard.Name} at Payday ({world.ShopState(_paydayCard)})");
                    NextPaydayShopStep();
                    break;
                }

                case 3:
                {
                    var card = _paydayCard!;
                    if (!settled || world.IsBuying(card) || held < _paydayUnits + card.Count) break;
                    int price = world.ShopPrice(card);
                    Check(server.Economy.Wallet == _paydayWallet - price, $"{card.Name} at {price} at Payday: the wallet went from {_paydayWallet} to {server.Economy.Wallet}");
                    // Back over the counter with it, from out here too: the run goes on as it would have.
                    _paydaySold = 0;
                    foreach (uint id in new[] { world.HotbarId, world.InventoryId })
                        foreach (var e in world.Prediction.View(id)!.Entries.ToArray())
                        {
                            if (e.Stack.Kind != StackKind.Item || e.Stack.DefIndex != card.Item!.Index) continue;
                            _paydaySold += ShopRules.SaleValue(card.Item, e.Stack.Count);
                            Check(client.Sell(id, e.EntryId) != 0, $"the open shop took no request to sell {card.Item.Name} at Payday");
                        }
                    NextPaydayShopStep();
                    break;
                }

                case 4:
                {
                    if (!settled || held != 0) break;
                    int want = _paydayWallet - world.ShopPrice(_paydayCard!) + _paydaySold;
                    Check(server.Economy.Wallet == want, $"after shopping at Payday the wallet is {server.Economy.Wallet}, expected {want}");
                    Check(client.ToggleShop() && !world.ShopOpen, "the shop key did not take the cards down");
                    NextPaydayShopStep();
                    break;
                }

                case 5:
                    if (_paydayShopFrames < 4) break;
                    Check(hud.Overlay.Visible && !_screen.Inventory.Visible, "the Payday summary is not back when the shop's cards are down");
                    Check(_screen.Mouse.Wanted, "with the shop's cards down at Payday the mouse is not the view's again");
                    GD.Print($"[smoke] shop at Payday: {MathF.Sqrt((world.Local.X - po.ShopX) * (world.Local.X - po.ShopX) + (world.Local.Z - po.ShopZ) * (world.Local.Z - po.ShopZ)):0} m from the counter, "
                        + $"{_paydayCard!.Name} for {HudText.Gold(world.ShopPrice(_paydayCard))} and sold back for {HudText.Gold(_paydaySold)}; wallet {HudText.Gold(_paydayWallet)} -> {HudText.Gold(server.Economy.Wallet)}");
                    _paydayShopVisits++;
                    _paydayShopStep = 0;
                    _session.FixedTicksPerFrame = RunOutTicksPerFrame;
                    break;
            }
        }

        /// <summary>Esc with nothing open lets the mouse go; where nothing is paused for it the HUD says so, and a click takes the view back.</summary>
        private void CheckMouseNote()
        {
            var hud = _screen.Hud;
            var mouse = _screen.Mouse;
            Check(hud.MouseText.Length == 0, $"with the mouse the view's the HUD says \"{hud.MouseText}\"");
            mouse.Toggle();
            hud.Refresh();
            Check(!mouse.Wanted && hud.MouseText == HudText.MouseReleased, $"with the mouse let go the HUD says \"{hud.MouseText}\"");
            Check(mouse.Click() && mouse.Wanted, "a click did not take the mouse back");
            hud.Refresh();
            Check(hud.MouseText.Length == 0, "the HUD still says the mouse is let go");
        }

        /// <summary>After the run: the counter was visited, and the Lobby's is bare.</summary>
        private void VerifyShop()
        {
            var world = _session.Client.World!;
            Check(_shopVisits == 1, $"the smoke went to the shop {_shopVisits} times");
            Check(_paydayShopVisits == 1 && _paydayShopStep == 0, $"the smoke shopped at {_paydayShopVisits} Paydays (step {_paydayShopStep})");
            Check(world.ShopSpecials.Count == 0 && world.ShopOwned.Count == 0 && !world.ShopOpen, "the shop of the finished run is still stocked in the Lobby");
        }
    }
}
