using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.App
{
    /// <summary>
    /// The Draft in the smoke's run: the cards on the screen are the server's, a number key takes
    /// one (a click does in the raid playtest's run), and from the next Prep on the perk is held on
    /// both ends and named on the HUD. While the cards wait for the host the mouse is the pointer's, and the view's again when
    /// one is taken; no world label is drawn on them.
    /// </summary>
    public partial class SmokeDriver
    {
        private int _draftShift, _draftFrames, _drafts, _draftPicks, _draftLabelsUnder;
        private string? _draftPerk;
        private SizedLabel3D? _draftLabel;
        private float _draftYaw, _draftPitch;
        private int _rerollStep, _rerollFrames;
        private string _rerollShared = "";

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
            // Laid out by now: the view goes to where a label of the world stands under the cards.
            if (_draftFrames == 2) AimAtALabel();
            if (_draftFrames < 4) return;
            CheckLabelsUnderTheCards();
            // The cards wait for a click: the mouse is the pointer's (issue (c) of the 25fbca5 playtest).
            Check(world.ChoosingCard && !_screen.Mouse.Wanted && _screen.Mouse.PanelOpen, $"with the Draft's cards open to the host the mouse is {(_screen.Mouse.Wanted ? "the view's" : "free")}");

            // The cards stand in the view, clear of the lines at the top.
            var box = panel.GetGlobalRect();
            Check(GetViewport().GetVisibleRect().Encloses(box) && box.Position.Y >= hud.TopStack[3].GetGlobalRect().End.Y, $"the Draft panel stands at {box}");
            for (int i = 0; i < panel.CardCount; i++)
                Check(box.Encloses(panel.CardAt(i).GetGlobalRect()), $"card {i + 1} at {panel.CardAt(i).GetGlobalRect()} is not inside the panel at {box}");
            Check(panel.ClockShown > 0f && panel.ClockShown < 1f, $"the bar of the Draft's seconds stands at {panel.ClockShown:0.00}");

            // The first Draft: new cards with a click on the reroll button (doc 01 §2.5, doc 09 §2.10).
            if (_drafts == 1 && _rerollStep < 2)
            {
                if (_rerollStep == 0)
                {
                    Check(panel.RerollOpen && world.RerollsLeft == 1, $"the reroll button is {(panel.RerollShown ? "shown" : "not shown")}, {world.RerollsLeft} rerolls left");
                    _rerollShared = string.Join(", ", world.DraftOffer.Select(p => p.Id));
                    panel.ClickReroll();
                    Check(panel.LastRequestId != 0, "a click on the reroll button sent nothing");
                    _rerollStep = 1;
                    _rerollFrames = 0;
                    return;
                }
                string cards = string.Join(", ", world.DraftOffer.Select(p => p.Id));
                if (client.PendingRequestCount > 0 || cards == _rerollShared)
                {
                    if (++_rerollFrames > 60) Fail($"the reroll brought no new cards: still {cards}");
                    return;
                }
                var me = server.GetPlayer(world.LocalId)!;
                string truth = string.Join(", ", server.OfferFor(me).Select(p => p.Id));
                Check(cards == truth && world.DraftOffer.All(p => !_rerollShared.Contains(p.Id, StringComparison.Ordinal)), $"after the reroll the client shows {cards}, the server dealt {truth}, the shared cards were {_rerollShared}");
                Check(world.RerollsLeft == 0 && server.RerollsLeft(me) == 0 && !panel.RerollOpen, $"after the reroll {world.RerollsLeft} are left (server {server.RerollsLeft(me)})");
                hud.Refresh();
                Check(panel.Text == HudText.DraftText(world) && panel.Text.Contains("No rerolls left this run", StringComparison.Ordinal), $"after the reroll the Draft shows \"{panel.Text}\"");
                Check(_session.Feed.Entries.Any(e => e.Text == HudText.Rerolled(0)), "the feed does not say new cards were drawn");
                Shot("draft-reroll");
                GD.Print($"[smoke] draft: rerolled with the button, {_rerollShared} -> {cards}");
                _rerollStep = 2;
                return;
            }

            // A different card each time, by the key a player would press.
            int card = (shift - 1) % world.DraftOffer.Count;
            if (world.CardState(card) != OpResult.Ok) card = world.FirstOpenCard;
            _draftPerk = world.DraftOffer[card].Id;
            Check(client.PressCardKey(card), $"key {HudText.CardKey(card)} did not take a card ({world.CardState(card)})");
        }

        /// <summary>The view goes to the nearest label of the world that is drawn in full, and as far under it as puts it in the middle of the cards.</summary>
        private void AimAtALabel()
        {
            var client = _session.Client;
            var eye = _screen.Camera.GlobalPosition;
            _draftYaw = client.Input.YawRadians;
            _draftPitch = client.Input.PitchRadians;
            _draftLabel = null;
            float best = float.MaxValue;
            foreach (var label in SizedLabel3D.Live)
            {
                if (label.Slides || !label.IsVisibleInTree()) continue;
                float distance = eye.DistanceTo(label.GlobalPosition);
                if (distance < 2f || distance >= best || (label.FadeEnd > 0f && distance > label.FadeStart)) continue;
                best = distance;
                _draftLabel = label;
            }
            if (_draftLabel == null) return;
            var at = _draftLabel.GlobalPosition;
            ViewRay.Aim(client.World!.Local, at.X, at.Y, at.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
            float half = GetViewport().GetVisibleRect().Size.Y * 0.5f;
            float up = (half - _screen.Hud.Draft.GetGlobalRect().GetCenter().Y) / half;
            client.Input.PitchRadians -= MathF.Atan(up * MathF.Tan(Mathf.DegToRad(_screen.Camera.Fov) * 0.5f));
        }

        /// <summary>Issue (d) of the 25fbca5 playtest: what stands under the Draft's cards is not drawn on them.</summary>
        private void CheckLabelsUnderTheCards()
        {
            var client = _session.Client;
            var camera = _screen.Camera;
            var rect = _screen.Hud.Draft.GetGlobalRect();
            var cards = new ScreenBox(rect.Position.X, rect.Position.Y, rect.End.X, rect.End.Y);
            Check(_screen.Hud.KeepClear().Any(clear => clear.X0 == cards.X0 && clear.Y0 == cards.Y0 && clear.X1 == cards.X1 && clear.Y1 == cards.Y1), "the Draft's cards are not kept clear of world labels");
            _screen.Labels.Step(0f);
            float height = GetViewport().GetVisibleRect().Size.Y;
            foreach (var label in SizedLabel3D.Live)
            {
                if (!label.IsVisibleInTree() || camera.IsPositionBehind(label.GlobalPosition)) continue;
                var at = camera.UnprojectPosition(label.GlobalPosition);
                float tall = label.ScreenFraction(camera) * height;
                if (!ScreenBox.Around(at.X, at.Y + label.SlidY, tall * label.Aspect, tall).Overlaps(cards)) continue;
                if (label == _draftLabel) _draftLabelsUnder++;
                Check(label.Target <= 0f, $"\"{label.Text}\" at {at} is drawn on the Draft's cards ({rect})");
            }
            if (_draftLabel != null)
            {
                client.Input.YawRadians = _draftYaw;
                client.Input.PitchRadians = _draftPitch;
                _draftLabel = null;
            }
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
            // With the card taken the mouse turns the view again, without a click to take it back.
            Check(!world.ChoosingCard && _screen.Mouse.Wanted && !_screen.Mouse.Released, $"after the Draft the mouse is not the view's (panel {(_screen.Mouse.PanelOpen ? "open" : "closed")})");
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
            Check(_draftLabelsUnder >= 1, "no label of the world ever stood under the Draft's cards: the check checks nothing");
            GD.Print($"[smoke] draft: {drafts} Drafts, a card taken by its key in each, the mouse free while they lay open and the view's after; {_draftLabelsUnder} times a label stood under the cards, never drawn");
            Check(world.TeamPerks.Count == 0 && world.LocalPerks.Count == 0 && _screen.Hud.PerksText.Length == 0, "perks of the finished run are still held in the Lobby");
        }
    }
}
