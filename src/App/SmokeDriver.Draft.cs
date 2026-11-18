using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.App
{
    /// <summary>
    /// The Draft in the smoke's run: the cards on the screen are the server's, a number key takes
    /// one, and from the next Prep on the perk is held on both ends and named on the HUD.
    /// </summary>
    public partial class SmokeDriver
    {
        private int _draftShift, _draftFrames, _drafts, _draftPicks;
        private string? _draftPerk;

        /// <summary>A frame of the run while the cards lie open.</summary>
        private void StepDraft()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var panel = hud.Draft;
            // The phase comes with the snapshot, the cards as an event of the same tick.
            if (world.DraftOffer.Count == 0) return;

            int shift = world.Global.Shift;
            if (_draftShift != shift)
            {
                _draftShift = shift;
                _draftFrames = 0;
                _drafts++;
                string cards = string.Join(", ", world.DraftOffer.Select(p => p.Id)), truth = string.Join(", ", server.DraftOffer.Select(p => p.Id));
                Check(server.Run.Phase == RunPhase.Draft && cards == truth, $"the client's cards after shift {shift} are {cards}, the server ({server.Run.Phase}) dealt {truth}");
                string want = HudText.DraftText(world);
                Check(panel.Visible && want.Length > 0 && panel.Text == want, $"the Draft after shift {shift} shows \"{panel.Text}\", expected \"{want}\"");
                Check(panel.CardCount == world.DraftOffer.Count && panel.GreyedCount == 0, $"{panel.CardCount} cards are shown, {panel.GreyedCount} of them greyed, before anybody chose");
                Check(!hud.Overlay.Visible, "the Payday summary is still shown under the cards");
                Check(hud.PhaseText.Contains("DRAFT", StringComparison.Ordinal), $"in the Draft the HUD phase line is \"{hud.PhaseText}\"");
                Check(hud.HintText.StartsWith($"Take a perk: {HudText.CardKey(0)}", StringComparison.Ordinal), $"in the Draft the HUD hint is \"{hud.HintText}\"");
                Check(_session.Feed.Entries.Any(e => e.Text.StartsWith("The Draft: ", StringComparison.Ordinal)), "the feed does not announce the Draft");
                Shot($"draft{shift}");
                return;
            }
            if (_draftPerk != null || ++_draftFrames < 2) return;

            // Laid out by now: the cards stand in the view, clear of the lines at the top.
            var box = panel.GetGlobalRect();
            Check(GetViewport().GetVisibleRect().Encloses(box) && box.Position.Y >= hud.TopStack[3].GetGlobalRect().End.Y, $"the Draft panel stands at {box}");
            for (int i = 0; i < panel.CardCount; i++)
                Check(box.Encloses(panel.CardAt(i).GetGlobalRect()), $"card {i + 1} at {panel.CardAt(i).GetGlobalRect()} is not inside the panel at {box}");
            Check(panel.ClockShown > 0f && panel.ClockShown < 1f, $"the bar of the Draft's seconds stands at {panel.ClockShown:0.00}");

            // A different card each time, by the key a player would press.
            int card = (shift - 1) % world.DraftOffer.Count;
            if (world.CardState(card) != OpResult.Ok) card = world.FirstOpenCard;
            _draftPerk = world.DraftOffer[card].Id;
            Check(client.PressCardKey(card), $"key {HudText.CardKey(card)} did not take a card ({world.CardState(card)})");
        }

        /// <summary>The first Prep after a card was taken: the perk is the player's, here and on the server.</summary>
        private void CheckDraftPick()
        {
            if (_draftPerk == null) return;
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var perk = world.Content.Perk(_draftPerk);
            _draftPerk = null;
            _draftPicks++;

            var player = server.GetPlayer(world.LocalId)!;
            bool held = (perk.Team ? server.TeamPerks : player.Perks).Any(p => p.Id == perk.Id);
            bool seen = (perk.Team ? world.TeamPerks : world.LocalPerks).Contains(perk);
            Check(held && seen, $"{perk.Name} was taken: the server {(held ? "has" : "does not have")} it, the client {(seen ? "has" : "does not have")} it");
            Check(!hud.Draft.Visible && world.DraftOffer.Count == 0, "the cards still lie open in Prep");
            string tracker = HudText.PerkTracker(world);
            Check(tracker.Length > 0 && hud.PerksText == tracker, $"the perks row says \"{hud.PerksText}\", expected \"{tracker}\"");
            string line = HudText.PerkPicked(world, world.LocalId, perk, auto: false);
            Check(_session.Feed.Entries.Any(e => e.Text == line), $"the feed does not say \"{line}\"");
        }

        /// <summary>After the run: every Draft was seen and chosen in, and nothing of it is left in the Lobby.</summary>
        private void VerifyDrafts()
        {
            var world = _session.Client.World!;
            int drafts = _phases.Count(p => p == RunPhase.Draft);
            Check(drafts >= 1 && _drafts == drafts && _draftPicks == drafts, $"the run had {drafts} Drafts; the smoke saw the cards of {_drafts} and took {_draftPicks} perks");
            Check(world.TeamPerks.Count == 0 && world.LocalPerks.Count == 0 && _screen.Hud.PerksText.Length == 0, "perks of the finished run are still held in the Lobby");
        }
    }
}
