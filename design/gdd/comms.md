# Comms — text chat, pings, emotes (split from HUD & Notifications)

> **Status**: Draft — split from HUD & Notifications 2026-10-16 by creative-director ruling (design-review, MAJOR REVISION NEEDED). **FROZEN behind the M-1a GUARD**: no /design-review and no further design until the two-friend greybox results are recorded in `prototypes/m1a-delivery-throwing-concept/REPORT.md`.
> **Author**: Evan Deters + agents (text moved close to verbatim from `design/gdd/hud-notifications.md` as reviewed, receipt `af60100…`)
> **Last Updated**: 2026-10-16
> **Implements Pillar**: Pillar 5 (Scales from 1 to 8), MDA Fellowship (#1) — the team's non-voice channel
> **Rule ids** H13–H19, H24 and AC ids are kept from the HUD GDD so existing references stay valid. "HUD-Hn" means a rule that stays in `hud-notifications.md`. The moved text has **not** been re-reviewed; the inherited findings below are open and must be resolved before this GDD's first /design-review.

## Overview

Comms is the team's non-voice voice: **text chat**, **pings** (one-press quick ping and the typed pings of the comms wheel, with the 3D world marker and its compass echo) and **emotes** (the wheel's emote page and the request that makes Player Character play the one-shot). These are the only messages the presentation layer originates: `ChatMessage`, `Ping`, `Emote`, relayed by the host without rate limits under the friends-only trust model (spec 13 §4.3). HUD & Notifications renders the surfaces Comms uses — the screen-projected marker layer (HUD-H12), the compass strip and its declutter (HUD-F6), name tags (HUD-H10), the feed and toast lanes (HUD-H4) — and owns none of the comms model. Milestones: quick ping, chat and name-tag states **M0**; wheel, typed pings, Say page and emotes **M1**; map pings **M2** (Map & Address UI).

## Player Fantasy

Inherited from HUD's "Within Shouting Distance" (HUD Player Fantasy) — fantasy test 1 "Heard without voice" is Comms' (H-FT-1, needs a no-ping baseline). Comms carries the *player voice* register: chat verbatim, canned ping labels and Say lines present-tense and earnest, emotes the carriers' own gestures.

## Inherited review findings (open — resolve before first review)

*From the HUD design-review of 2026-10-16. Source-tagged; none is fixed in the moved text below.*

| # | Finding | Proposed fix |
|---|---|---|
| CF-1 | [network-programmer] `commsSeq` global vs per-peer is undefined; exclude-sender `Emote` leaves a false gap on the sender's own stream | per-recipient `commsSeq`, incremented only for messages queued to that peer; joiner's counter starts at the snapshot; valve logs the dropped range |
| CF-2 | [network-programmer] the typing bit cannot live in Player Character's transform (all 8 `animState` bits used; `pktSeq` u8 wrap; host-authored Dead bodies; 4 Hz keepalive breaks the 600 ms timeout) | `TypingState{on}` on ch3 at open / close, bounded by idle-close and `PingPurge`. The speaking-bit design is removed (Proximity Voice owns it — HUD OQ) |
| CF-3 | [network-programmer] ping refresh / replace / ack are not deterministic across peers (local anchors, local receipt time) | the host resolves cap and refresh on `pos`, emits the surviving `pingId` (reuse on refresh or an explicit replaced-id) and `expiresTick`; receivers apply, never re-decide |
| CF-4 | [network-programmer, qa-lead] JoinState "≤ 4.4 KB" is false — 20 lines × 800 B ≈ 16 KB | replay by byte budget (newest lines until 4 KB) |
| CF-5 | [network-programmer] Say is 10 B not 12; no text / Say discriminator; no msg-type or version byte; `sayId` and Pass-to-me `targetId == senderSlot` missing from the host shape table | add a type + version byte; fix sizes; extend the shape table |
| CF-6 | [network-programmer] history ordering `(hostTick, arrival index)` — the arrival index is local, ch1 / ch3 unordered | state cross-channel interleave as best-effort |
| CF-7 | [network-programmer] ENet channel count is fixed at connect; spec 06 lists 0–2 | spike item + spec 06 amendment via Networking & Session |
| CF-8 | [network-programmer] joiner gating (may a Joining peer send ch3? when does relay start?), `clientSeq` reconcile on reconnect, tick sync before replay aging | specify in Network |
| CF-9 | [ux-designer, game-designer, CD ruling b] ping **ack is cut from v1** (40 px override = hidden mode error, tens of metres at range; per-acker toasts flood the 2-slot lane) | remove `PingAck`, ack toast and H-PING-4; "explicit ack action" is an open question after the M0 Fellowship playtest |
| CF-10 | [game-designer] quick ping: MMB beside the scroll-wheel hotbar → accidental pings, no cancel, cap evicts deliberate pings; mailbox auto-types to "Address" not "Deliver here"; a teammate in front captures "Over here" | re-ping within 2 m toggles off; mailbox → "Deliver here" + address chip; revert metric = ping-then-retype within 5 s |
| CF-11 | [game-designer, accessibility] chat freezes the carrier up to 20 s; Enter mid-hold cancels a deliver; a typing carrier cannot catch | ignore Enter during a hold; idle-close ≈ 8 s (setting incl. Off); promote HN-21 walk-while-typing |
| CF-12 | [ux-designer, accessibility] Say page lacks Yes / No / "Where are you?"; wheel reopening on the last-used page breaks muscle memory; list alternative "from the wheel's settings" is circular; D-pad ↑ held occupies the left thumb on pad | fixed default page; add Yes / No; own action for the list view |
| CF-13 | [game-designer] solo wheel on Emotes is dead chrome; emote poses are suppressed while holding a stack (nearly always) | hide the wheel at roster 1 or make solo pings personal waypoints; decide pose-with-stack |
| CF-14 | [accessibility] chat opacity floor 50 % fails contrast over L*95 walls | floor 70 %, delete the outline escape |
| CF-15 | [CD ruling] per-category mutes are cut from HUD; **per-player mutes stay here** (chat; pings / emotes) | keep H18 mute; drop category-mute references |
| CF-16 | [qa-lead] H-WHEEL-1 Mounted / Say-while-Dead rely on unprocessed Input & Controls hand-back (I&C "Dead: ping ring only") | mark blocked on HUD OQ hand-back to I&C |

## Detailed Design

### Core Rules

**H13 — Quick ping [M0] [D].** One tap (MMB / R3, Input & Controls) places a ping at the point under the reticle.
- **Raycast range:** `pingRangeM` = 150 m. No hit sends nothing and plays a soft click-stop.
- **Auto-typing:** the ping types itself from the **type of the first thing the ray hits** (a mapping, not a priority search):
  1. teammate → **"Over here"**, anchored on that teammate;
  2. mail, pile or WorldItem → **Item**, showing the item's address chip;
  3. mailbox or house → **Address**, showing the swatch-with-glyph and "17 Elm St";
  4. enemy → **Danger** [M3];
  5. construct → **Look here**, with the construct icon;
  6. ground → **Look here**.
- Addresses and names resolve on each receiver from `targetId`, never from strings.
- An entity ping **follows its target** and ends early if the target despawns. A ground ping is fixed.

**H14 — The comms wheel [M1] [D].** Hold C / D-pad ↑ (aim-release, Input & Controls; never a commit, G9).
- **On open:** the world target is raycast **when the wheel opens**, look freezes, and movement continues.
- **Selection:** the sector under the pointer / stick on release; the dead zone sends nothing.
- **Pages:** 3 pages × 8 fixed slots, switched with Q/E, the mouse wheel, or LB/RB. The wheel opens on the last-used page.

| Page | Slots |
|---|---|
| **Pings** | Deliver here · Build here · Danger · Need materials (spec 09 §2.6) · Look here · Regroup here · **Pass to me** (anchored on and following the sender; M1) · 1 reserved (M3: Defend) |
| **Say** | 8 canned, localised lines: "Thanks" · "On my way" · "Wait for me" · "Need a hand" · "Got it" · "Coming through" · "Behind you" · "Good throw" (narrative-director to confirm wording in the carriers' earnest voice) — **the gamepad's chat send path** (OQ-IC-5) |
| **Emotes** | H16 |

- **Typed pings override auto-typing.** The anchor still comes from the opening raycast.
- **A list alternative** of the wheel (a vertical menu with the same entries) is required for accessibility (Player Character UI Requirements).
- **Live in:** World, Aiming, Holding-closed, Build, Map, Dead (Input & Controls) — **and Mounted** (Player Character P14 allows the emote radial there; hand-back to Input & Controls, Open Questions).
- **While knocked down:** there is no Look and no quick ping (Player Character D-3: the camera auto-orbits the pile; no spectate in v1). The wheel stays live: the Emotes page is greyed with "Knocked down"; Say works; **Pings-page entries anchor at the player's own pile** (or the knockdown spot if nothing dropped) — the "get my pile" callout.

**H15 — Ping lifetime and caps.**
- **Lifetime:** `pingLifetimeMs` = 10000 (spec 09 §2.6).
- **Caps:** at most `pingsPerPlayerMax` = 2 live per sender (16 team-wide). A third ping replaces that sender's oldest.
- **Refresh:** a ping of the same type within `pingRefreshRadiusMm` = 2000 (2 m) of the sender's own live ping refreshes it instead of adding one.
- **Acknowledge [M1]:** a quick ping with the reticle within `pingAckRadiusPx` = 40 px of a teammate's live marker's screen projection (nearest wins; tie → lower `pingId`) acknowledges it ✓ instead of placing a ping — this overrides auto-typing; your own marker is refreshed, not acked. The marker shows the first acknowledger's hat (+N for more), and the sender gets a toast "**Sam** saw your ping" per acknowledger.
- **Visible to every peer:** the world marker, the compass echo, a 3 s off-screen edge arrow after placement (or until first on-screen), and a dim log line in chat ("Sam · Danger · 14 Larch Ln").
- **Labels:** the 8 pings nearest the **local player** show sender and distance labels; the rest are icon only (F6). The same-type refresh is evaluated **before** the cap.
- **Sender courtesy cooldown** (client-side, H17): `pingSendCooldownMs` = 500.

**H16 — Emotes [M1].**
- **v1 set of 6:** Tip the hat · Salute · Point · Thumbs-up · Shrug · Wave, plus 2 reserved slots (creative-director proposal; narrative-director and animation to confirm).
- All six are upper-body one-shots played by Player Character's animation layer (P20). If a full-body emote is added later, movement input cancels it (Player Character P14).
- **Glyph bubble:** every emote also shows a Chalk glyph bubble above the hat for 2 s. That is the read at 20 m, where faces vanish. The bubble **always** shows, so if Player Character suppresses the pose (e.g. while holding a stack or mounted), the signal is never lost.
- **Local lockout:** `emoteCooldownMs` = 2000. One bubble per player at a time.
- An emote is a P3 log line in chat, mutable per player. **Ping and emote log lines (combined) are capped at 3 per sender per 10 s**; further pings still place their markers and everything still reaches history, but no chat line is written. A receiver ignores a second emote from one sender whose `hostTick` is < 45 ticks (1500 ms) after the previous one's (jitter tolerance under the 2000 ms local lockout; host ticks, not arrival time, so head-of-line delay cannot skew it). Whether the pose is suppressed (holding a stack, mounted) is Player Character's decision; HUD always shows the bubble.

**H17 — Trust model: no host rate limits; client courtesy limits [D].**
- **Host:** v1 deletes server-side chat, ping and RPC rate limits (spec 13 §3, §4.3; spec 06 §4's "3/s, 1/s" is superseded as a server rule). The host relay checks **shape only** (Network).
- **Sender (UX limits, not security):** the sending client applies courtesy limits so a held key cannot spam:
  - pings — `pingSendCooldownMs`;
  - chat — a token bucket of 3 that refills 1 per 0.75 s (F5);
  - emotes — `emoteCooldownMs`.
- **Receiver:** display throttling (H8 merging, the H26 digest, H27 sound limits) keeps a misbehaving modded client from flooding anyone's screen.
- **Real defences:** the per-player local mute (H18) and the host's kick (spec 13 §4.3).

**H18 — Text chat [M0].**
- **Channel:** one team channel, island-wide. Text reaches where voice cannot. Enter opens it (KBM only; gamepad reads chat and sends with the Say page, OQ-IC-5).
- **Entering text:** Input & Controls' TextEntry router state captures the keyboard. The carrier stands still, and a "Typing…" state shows on their name tag, so a stopped teammate reads as talking, not lost.
- **Keys:** Enter sends; empty Enter or Esc closes (the first Back-ladder step); Up recalls the kept draft if one exists, else the last sent line.
- **Length:** `chatMaxChars` = 200 characters (spec 06 §4), counted in grapheme clusters, with a hard cap of 800 UTF-8 bytes on the wire.
- **Display:**
  - 6 lines visible bottom-left, at 18 px on a Charcoal plate at `chatBackgroundOpacity` (default 85 %, floor 50 %; below 70 % a 2 px Charcoal text outline is forced, keeping 4.5:1).
  - Lines fade after `chatFadeSeconds` (setting: Never / 5 / **10** / 20 / Always show). New lines always show.
  - When open: 10 lines with scrollback over the run's 100-line history (cleared per run).
- **Line format:** hat icon + Bold name + Chalk text. A line containing your display name gets a highlight bar (thickness, not colour).
- **System lines in chat:** only comms echoes (pings, Say lines, emotes, "Sam left") and the gap line "N messages not received" (a `commsSeq` gap, Network), rendered dim. Gameplay events go to the feed, never to chat.
- **Idle typing:** the field closes after `typingIdleCloseSeconds` = 20 without a keystroke, keeping the draft for Up-recall, so nobody is left frozen by a forgotten field.
- **Solo (roster 1):** no chat panel, no "Typing…", no Pings or Say pages; Enter and quick ping do nothing and the wheel opens on Emotes. The chrome appears when a second player joins.
- **Mute:** a per-player local mute, split into "chat" and "pings / emotes", in the Pause player list and on a click on a chat name. It is never broadcast. There is no profanity filter in a friends-only build.

**H19 — Proximity voice indicators [M0 basic].**
- **Speaking teammate:** a static speaker glyph beside their name tag, plus a "talking" state on their compass icon (spec 09 §2.4, spec 13 §4.2) — icon and state, never colour alone.
- **Own mic state:** shown locally (open / push-to-talk held / muted) as a small hotbar-side icon. At roster 1 the own mic-state icon is hidden (H18 solo rule); it appears when a second player joins.
- **Not here:** PA broadcasts and push-to-talk polish are Alpha, owned by Proximity Voice.

#### Ingress sanitisation (moved from HUD H24)

**H24 — Ingress sanitisation.** Every receiver sanitises chat text and display names before display:
- normalise to NFC;
- strip C0 / C1 controls (including U+0085, line and paragraph separators U+2028 / U+2029), lone surrogates, and tag characters U+E0000–E007F;
- strip bidi marks U+200E, U+200F, U+061C;
- strip invisible fillers U+00AD, U+3164, U+2800;
- strip bidi overrides and isolates (U+202A–202E, U+2066–2069);
- strip zero-width characters (U+200B, U+2060, U+FEFF) — **ZWNJ U+200C and ZWJ U+200D are kept**, because emoji sequences and Persian and Indic text need them;
- cap the length (H18), counted after sanitising (F5).

Player text is **never** parsed as markup: it is inserted as plain text segments, never concatenated into BBCode (ui-programmer). The sanitiser's actions are written to the UI event log.

**Name rules for the sanitiser.** Whitespace-only names count as empty ("Carrier N"). The duplicate check compares the NFC, **invariant**-case-folded, **truncated** form, so names that truncate identically still get a suffix. `IRoster` retains a departed slot's name and hat snapshot until that slot's `PingPurge` is processed (Network), so a line ordered just before the purge never renders as "Someone".

### States and Transitions

**Chat input.**

| State | Event | Next |
|---|---|---|
| Closed | Enter (KBM, chat live in the current router state) | Typing — TextEntry captures, movement zeroed, "Typing…" tag on |
| Typing | Enter with text, bucket has a token | Sent → Closed (line appears locally at once, tagged pending until the relay echo; on host loss it stays with a ✗) |
| Typing | Enter with text, bucket empty | stays Typing; the field outline thickens once and "Slow down" shows for 1 s |
| Typing | Esc, or Enter on an empty field | Closed (nothing sent) |
| Typing | knocked down | stays Typing (chat is live while Dead) |
| Typing | phase change | stays Typing (TextEntry has capture priority) |
| Typing | Pause (Esc is consumed by TextEntry first, so this is pause from another path, e.g. focus loss) | the field hides and the draft is suspended; Resume restores it (Input & Controls); "Typing…" stays on the tag |
| Typing | `typingIdleCloseSeconds` without a keystroke | Closed, draft kept for Up-recall |

**Comms wheel.**

| State | Event | Next |
|---|---|---|
| Closed | press-and-hold C / D-pad ↑ in a live state | Open — target raycast once, look frozen, movement live |
| Open | page switch | Open (other page) |
| Open | release on a live sector | Selected → message sent (subject to the cooldown) → Closed |
| Open | release in the dead zone, or Back | Cancelled → Closed (nothing sent, no log line) |
| Open | knockdown, mount or panel open | Cancelled → Closed (from Dead the wheel can be reopened) |
| Open | Toggle mode (Input & Controls) | stays Open until the second press, which selects |

**Ping lifecycle.**

| State | Event | Next |
|---|---|---|
| Requested | quick tap or wheel selection, cooldown clear | Live (local marker shows at once, pending) |
| Live | 10 s elapse | Expiring → Gone (0.3 s fade) |
| Live | sender's third ping | Replaced → Gone |
| Live | same-type ping by the same sender within 2 m | Live (timer reset) |
| Live | entity target despawns or is consumed | Gone (early) |
| Live | sender disconnects or is kicked | Gone at once on every peer |
| Live | a teammate acknowledges | Live + ✓ marker |

### Interactions

HUD & Notifications (marker layer, compass, name tags, feed / toast lanes — renders); Input & Controls (quick ping, wheel, TextEntry, Say); Player Character (emote playback P20, focus, knockdown, on-break); Networking & Session (transport, roster, JoinState, kick); Proximity Voice (speaking state); Map & Address UI (reuses the ping model, M2); Audio (chirp / blip rate classes).


## Formulas

### F5 — Sender courtesy limits (client-side, H17)

The courtesy limits formula is defined as:

`credit′ = min(2250, credit + dt)`; `chatOK = credit′ ≥ 750` (on send: `credit′ −= 750`)
`pingOK = now − lastPing ≥ pingSendCooldownMs`; `emoteOK = now − lastEmote ≥ emoteCooldownMs`
`textOK = graphemes(sanitise(s)) ≤ chatMaxChars ∧ utf8Bytes(s) ≤ chatMaxBytes`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| credit | int ms | 0–2250 | calculated | chat bucket (capacity 3 lines × 750 ms); starts full |
| pingSendCooldownMs | int | 500 | data file | shared by quick ping and wheel pings |
| emoteCooldownMs | int | 2000 | data file | per sender |
| chatMaxChars | int | 200 | constant (spec 06 §4) | grapheme clusters per UAX #29 |
| chatMaxBytes | int | 800 | constant | wire cap |

A Say-page line spends one chat token. A refused request does not restart a cooldown. Grapheme tests use fixed vectors, because .NET's Unicode version can drift.

**Output Range:** a burst of 3 lines, then a sustained 1 line per 750 ms (80 per minute); refilling from empty takes 2250 ms. The typing field stops accepting input at the first grapheme boundary that would break either cap. A receiver re-checks and **truncates** at the boundary, never rejects (H24).

**Example:** credit starts at 2250. Sends at t = 0, 100 and 200 ms leave 1500, 850 and **200** (the credit refills between sends). A fourth send at 300 ms finds 300 and shows "Slow down"; at t = 750 ms the credit reaches 750 and one more line goes (749 is refused).

### F6c — Ping replacement (comms half of HUD F6)

`onPing(s, type, p): if ∃ live(s, type) with |p − p′|² ≤ (2000 mm)² → expiresAt = now + 10000; else if count(s) ≥ 2 → drop argmin(expiresAt) over s; then add`

`pingsPerPlayerMax` 2 (enforced per sender id on receivers), `pingRefreshRadiusMm` 2000, `pingLifetimeMs` 10000 (spec 09 §2.6), `pingLabelMax` 8 (nearest the local player; tie lower pingId; re-ranked ≤ 4 Hz, 1 m hysteresis). Refresh runs before the cap check; "oldest" = lowest `expiresAt`; an entity ping measures from its target's current anchor (see CF-3). Compass mapping and declutter: HUD F6.


## Edge Cases

- **If chat opens mid-deliver hold** (Enter in World, Holding-closed or Dead): TextEntry captures, the hold cancels with its reason, and E stays muted until released (Input & Controls). Outcomes already committed (pills, toasts) still render while typing.
- **If the comms wheel opens mid-hold**: the hold continues — the wheel is never a commit (Input G9). Look freezes, movement continues, and the hold keeps its own range-cancel rule.
- **If Enter or MMB is pressed in Panel or Aiming**: Enter stays pick / place, and quick ping is not live; nothing fires, no toast.
- **If the wheel opens in Map before M2**: the Pings page greys with "Close the map"; Say and Emotes work.
- **If an emote arrives from a sender the receiver's state shows as knocked down** (a wheel race): the receiver drops it (no bubble, no line, `Drop: sender_dead`). The sender's cooldown stays spent.
- **If the game is paused while the chat field is open** (multiplayer): the field hides and the draft is suspended; Resume restores it (Input & Controls). "Typing…" stays on the tag, because the body is still standing in the world.
- **If Payday is open and a ping is sent**: it places and replicates normally. Compass echoes dim with the compass (40 %); world markers stay at full strength.
- **If the HUD is Hidden and the player presses Enter or C**: the chat plate or wheel shows while open (chat for 3 s after). Incoming lines and pings stay hidden, sending still works, and teammates are never told.
- **If the controller disconnects with the wheel open**: the synthesised release is a cancel (Input & Controls). Nothing sends, the wheel and its list view close, and the disconnect banner shows. Reconnecting does not reopen the wheel.
### Comms

- **If a ping targets a mailbox in a locked district**: it places normally (pings carry no gameplay decision). The marker adds a lock glyph from destination state, and a typed "Deliver here" is not refused.
- **If the sender's own capsule, own vehicle or a Build ghost is under the reticle**: it is excluded from the raycast, which hits whatever is behind or sends nothing at 150 m. "Over here" never self-targets. The raycast origin is the active camera; while knocked down there is no raycast (wheel pings anchor at the own pile, H14).
- **If a ping is placed while swimming, mounted or in Build**: all are live states for quick ping (Input & Controls), with the exclusions above.
- **If the receiver despawned, or never had, the pinged WorldItem**: the ping places at `pos` and binds lazily for 1000 ms, else degrades to "Look here" (Network). A ping that *has* bound ends early when its target later despawns (H15).
- **If a sender's third ping also qualifies for a same-type refresh**: refresh runs first (F6), so the count stays at 2.
- **If the wheel is released on a reserved slot**: reserved slots render empty and act as the dead zone (cancel, nothing sent). A reserved `emoteId` reaching a receiver is ignored and logged.
- **If a pending echo times out (5000 ms) and the relay copy then arrives late**: the ✗ flips to delivered by `clientSeq` with no duplicate row. A ping removed at the timeout is re-added with its `hostTick`-based remaining lifetime, or discarded if 10 s have passed.
- **If a player tries to mute themselves**: the control is disabled on their own row and their own chat name opens no mute menu. Own pings are never filtered.
- **If a newly muted sender's lines are already in scrollback**: they are purged from the buffer (unmute does not restore them), and later lines are discarded on receipt. A flooder therefore cannot evict real lines from the 100-line buffer.

### Network, join, disconnect, host lost

- **If a kicked player has queued lines**: lines ordered before `PingPurge{slot}` on channel 3 display. Anything after it, from a slot not in the roster, is dropped with a throttled log. Scrollback keeps what was said, with the name snapshotted at receipt (H9).
- **If a player rejoins in the same slot within 120 s**: their old pings stay purged. JoinState replays the last 20 chat lines and up to 16 pings, and the client skips any `commsSeq` already seen. Mutes persist (keyed by account id), and an "X joined" feed line shows.
- **If JoinState replays chat lines and pings**: no blip, no mention highlight alert, no live-region read-out, no unread count. A ping aged ≥ 10 s by `hostTick` is discarded (`expired_on_arrival`). A joiner's scrollback is therefore shorter (20 lines) than a veteran's (100) — documented, accepted.
- **If the host is lost**: pings clear, chat scrollback stays until the menu, and pending echoes turn ✗. Quick ping becomes a click-stop, Enter shows "Not connected", and the wheel cannot open. The timer freezes at its last smoothed value, with no more threshold announcements. The Host lost banner pre-empts everything (H7).
- **If the 32 KB valve drops a slow peer's oldest comms**: the receiver detects the `commsSeq` gap and inserts one dim line, "N messages not received" (H18).
- **If a disconnected player's body is frozen on break** (120 s, Player Character): their pings are purged at once; their name tag shows "· On break 1:43" (Player Character); their chat lines stay.
- **If the player is solo (roster 1)**: no chat panel, no "Typing…", no own mic-state icon, no Pings or Say pages. Enter and quick ping do nothing, and the wheel opens on Emotes. The chrome appears when a second player joins and hides again at roster 1, with scrollback kept until the menu (anti-goal 4: no dead comms chrome).
### Degenerate strategies

- **If a modded client spams pings**: the receiver's per-sender cap of 2, the refresh rule and the H27 sound limits bound the markers. Ping and emote log lines are capped at 3 per sender per 10 s (H16), and extra pings still place and reach history.
- **If a modded client floods chat**: identical lines merge "×N" (10 s). Distinct lines scroll, and the defences are the mute purge and the host's kick. Receivers re-truncate to 200 graphemes and 800 B. The host's 32 KB valve bounds memory.
- **If emotes are spammed over the throw arc**: receivers ignore a second emote from one sender within 1500 ms, and bubbles are one per player. Draw order puts the throw-arc landing marker and the prompt above bubbles, name tags and pings (H11).
- **If pings are used to cheat**: there is nothing to gain. A ping has no gameplay effect, its address resolves locally from `targetId`, and positions and addresses are already team-shared.
- **If pings mislead** ("Deliver here" at the wrong house): accepted in a friends-only build. Every marker shows the sender's hat, the cap is 2 per sender, local mute hides a sender's markers and lines, and the host can kick. The address chip cannot lie about which house it targets.
- **If a player forgets the chat field open**: their body stands still and damageable, the team sees "Typing…" (a stopped teammate reads as talking, not lost), and the field closes after `typingIdleCloseSeconds` = 20 (H18), so nobody is left frozen.
- **If a player farms "saw your ping" acks**: harmless. Acks have no reward and are not telemetry for any score. The fantasy-test telemetry (test 1) counts a teammate moving within 20 m or replying, not acks alone.

## Dependencies

Upstream: Input & Controls (Hard), Player Character & Movement (Hard), HUD & Notifications (Hard — marker layer, compass, lanes), Networking & Session (Hard, M0), Proximity Voice (Soft), World Generation (address tables), Grid Inventory (despawn tombstone for lazy binding). Downstream: Map & Address UI (M2, ping model), Menus & Run Flow (Pause player list mutes), Audio (cue rate classes). Bidirectional notes are pending (frozen).


## Tuning Knobs

| Knob | Default | Safe range | Affects | Too low | Too high | Interacts with |
|---|---|---|---|---|---|---|
| `pingRangeM` | 150 | 80–250 | quick-ping raycast | can't ping across a district | pings across the island at the horizon pixel | `pingLabelMax` |
| `pingLifetimeMs` | 10000 (spec 09 §2.6) | 6000–15000 | ping marker life | marker gone before a teammate turns | clutter | `pingsPerPlayerMax` |
| `pingsPerPlayerMax` | 2 | 1–3 | per-sender live pings (16 team-wide) | can't mark "here and there" | 24 markers at 8 players | F6 marker budget (41) |
| `pingRefreshRadiusMm` | 2000 | 1000–4000 | re-ping refreshes instead of adding | double markers on one spot | refresh hijacks a nearby distinct ping | — |
| `pingLabelMax` | 8 | 4–16 | labelled pings | unlabelled nearby pings | label clutter | `minSpacingPx` |
| `pingAckRadiusPx` | 40 | 24–64 | ack detection | acks hard to land | quick pings near a marker always ack | text scale |
| `pingSendCooldownMs` | 500 | 250–1500 | client courtesy (H17) | spam from a held key | rapid callouts blocked | sound limit H27 |
| `pingBindGraceMs` | 1000 | 500–2000 | lazy entity binding (Network) | item pings degrade too often | a wrong anchor lingers | channel latency |
| `chatBucketLines` / `chatRefillMs` | 3 / 750 | 2–5 / 500–1500 | F5 chat courtesy | normal bursts blocked ("Slow down") | flood from a held paste | — |
| `chatMaxChars` | 200 (spec 06 §4) | **fixed** (spec) | line length | — | — | `chatMaxBytes` 800 |
| `chatVisibleLines` | 6 (Compact 4) | 3–8 | chat box | conversation lost | covers bottom-left HP | `chatFadeSeconds` |
| `chatFadeSeconds` (setting) | 10 | Never / 5 / 10 / 20 / Always | chat persistence | lines missed | screen clutter | — |
| `chatBackgroundOpacity` (setting) | 85 % | 50–100 % | legibility | below 70 % a 2 px text outline is forced (4.5:1) | — | — |
| `chatHistoryLines` | 100 | 50–200 | scrollback per run | context lost | memory (trivial) | JoinState replays 20 |
| `typingIdleCloseSeconds` | 20 | 10–60 | anti-idle in chat | drafts closed mid-thought | chat as a hiding place | — |
| `emoteCooldownMs` | 2000 | 1000–4000 | local lockout | bubble spam | expressive play blocked | receiver tolerance 1500 |
| `emoteBubbleMs` | 2000 | 1500–3000 | emote glyph bubble | missed at 20 m | covers name tags | — |

## Visual/Audio Requirements

**Ping types** (moved from HUD Visual/Audio; widget styling and the global colour rules stay in HUD and the seed file).

**Ping types — shape and glyph carry the meaning; fill supports it.**

| Type | Glyph | Fill |
|---|---|---|
| Look here (quick) | dot-and-bar | Charcoal |
| Over here (teammate) | hat outline | Charcoal |
| Item | parcel silhouette + address chip | Parcel Kraft |
| Address | swatch-with-glyph + "17 Elm St" chip | Charcoal |
| Deliver here | envelope with down arrow | Parcel Kraft |
| Build here | brick / ghost | Postal Blue |
| Need materials | crate | Charcoal |
| Regroup here | three converging chevrons | Charcoal |
| Pass to me | open hands | Charcoal |
| Danger | exclamation | Stamp Red |

- **No green** (green means "matching mailbox" on the arc). **No Gold** (Gold is money). **Magenta never** appears on player-authored comms: a Danger ping on a goon is still red, because the player is warning, not being attacked.
- Danger is distinct from the system raid-edge icon by shape. The pins avoid the five district glyphs (circle, triangle, square, cross, diamond).

## Game Feel

**Comms feel.**
- **A ping is a sentence, not a dot.** The type, the address chip and the sender's hat arrive together on one press. Quick ping costs one button and no menu, so it is the fast path; the wheel is the expressive path.
- **The wheel never stops the body.** Movement continues while it is open, it is never a commit (Input G9), and a running deliver hold survives it.
- **Chat costs presence.** On a keyboard the movement keys are the text keys, so typing stops the carrier, and the team sees "Typing…" so a stopped teammate reads as talking, not lost. That is why the Say page and pings exist.

## UI Requirements

**Comms screens** (moved from HUD UI Requirements; to be specified by `/ux-design`).
- **Comms wheel** (H14):
  - a radial of 8 sectors, r = 12 container, Chalk-on-Charcoal filled icons;
  - focus is a thickness ring (4 px Chalk + caret), never colour;
  - a page indicator with glyphs for page switching;
  - the **list alternative** is a vertical menu with the same entries and order, reachable from the wheel's settings (Hold / Toggle per Input & Controls);
  - a dead zone of < 24 px or stick < 0.5 sends nothing (Input & Controls).
- **Chat** (H18):
  - the input is a pill under the history;
  - PgUp / PgDn or the wheel scrolls when open;
  - clicking a name opens a per-player menu ("Mute chat", "Mute pings / emotes");
  - the Pause player list carries the same mutes;
  - gamepad players get the chat history read-only and the Say page.

## Network

*Specialist: network-programmer (2026-10-16). Vocabulary from spec 06 §4: **E** event, **R** request / validate / apply, **S** snapshot. Godot 4.7.1 high-level multiplayer behaviour is **NOT SOURCEABLE** in `docs/engine-reference/godot/`, so this section is a contract on the transport adapter, as in the Grid Inventory and Player Character Network sections. Every number is **PROPOSED**. The spec 13 trust model applies: the host validates shape only and applies no rate limits (H17).*

**What this system replicates.** Only its own three message families. Everything else the HUD shows is read from other systems' replicated state or events (`RunState`, wallet, `DeliveryOutcome`, `backlogUnits`, inventory replies, the roster), whose Network sections own them.

**Channel.**
- All comms ride a **dedicated reliable-ordered channel 3 ("comms")**, separate from channel 1 (events) and channel 2 (bulk / JoinState). A 12 KB inventory burst or a 64 KB JoinState chunk therefore never stalls chat, and a chat flood never stalls inventory.
- **No ordering is assumed** between channel 3 and any other channel.
- Each message is one packed `byte[]` argument (per-field Variant overhead is unverified).
- **Sender identity:** clients never send a sender field. The host reads the RPC sender peer, maps it to a lobby slot through the account mapping, and writes `senderSlot` into the relay header. Host→client relay RPCs are authority-only, so no client can address another client directly.

**Message shapes** (payload bytes; `RelayHdr` = `senderSlot u8, commsSeq u16, hostTick u32` = 7 B).

| Message | Client → host | Host relay | Sent to | Notes |
|---|---|---|---|---|
| `ChatMessage` | `clientSeq u16` + length-prefixed UTF-8 (≤ 800 B) | RelayHdr + `clientSeq` + text (11 B + text) | all **including the sender** (the echo confirms delivery) | Say-page lines are `ChatMessage` with a `sayId u8` instead of text (12 B); every receiver renders the line in its own locale |
| `Ping` | `clientSeq u16, type u8 (bit 7 = typed override), targetKind u8, targetId u32, pos (x, z int32 cm; y int16 cm)` = 18 B | RelayHdr + `pingId u16` + body = 27 B | all, including the sender | `type` includes Pass to me (`targetKind` Teammate with `targetId` = the sender's own slot, so it follows the sender); `targetKind` ∈ Ground, Teammate (`targetId` = slot), MailboxHouse, ItemOrPile, Construct, Enemy; `pos` is always sent as the fallback anchor; the sender resolves auto-typing (H13), so quick and typed pings share one shape |
| `PingAck` | `pingId u16` | 9 B | all | acker = `senderSlot`; an unknown or expired `pingId` is dropped by the host, never turned into a new ping |
| `PingPurge` | host-originated | 8 B (`slot u8`) | all | on disconnect, freeze or kick; sent on channel 3 *after* that sender's last relayed ping, so ordering guarantees no ghost ping |
| `Emote` | `emoteId u8` (0–5 used, 6–7 reserved) | 8 B | all **except** the sender (who plays locally) | the **same** message as Player Character's emote one-shot — one message, not two (hand-back) |

**No `PingCancel`.** Every receiver expires pings locally at 10 s. Target despawn is known locally. The per-sender cap, replace-oldest and refresh rules (H15, F6) are deterministic over an ordered stream, and disconnects use `PingPurge`. A cancel message is added only if a later milestone adds "dismiss my ping".

**Host relay validation (shape only, never rate).**

| Check | Rule |
|---|---|
| Chat | valid UTF-8, 1–800 bytes; no grapheme count on the host (receivers cap at 200 graphemes, H24) |
| Ping | `type` and `targetKind` in enum range; `pos` inside world bounds + 10 m; `targetId` **not** checked for existence (lazy binding, below) |
| Ack | known `pingId` |
| Emote | `emoteId` 0–7 |
| Invalid | **drop**; never disconnect or kick; one log line per (sender, kind, reason) per 10 s with a count (the log is rate-limited, not the messages); the sender's pending echo becomes "not delivered" after `chatEchoTimeoutMs` = 5000 |

- **Memory guard (not a rate limit):** a per-peer comms high-water valve of `commsHighWaterBytes` = 32 KB. When a slow peer's unsent comms exceed it, the host drops that peer's oldest queued comms. A modded flood can therefore not grow host memory without bound. The real defences remain the local mute and the host's kick (spec 13 §4.3).

**Local echo.**
- **Chat:** the sender shows a dim pending line tagged with `clientSeq`. The relayed copy, matched on `(senderSlot == me, clientSeq)`, replaces it in place. `clientSeq` is for matching, not dedupe, because reliable-ordered delivery does not duplicate.
- **Pings:** a ghost marker is replaced by the relayed ping, which supplies `pingId`.
- **Host:** the host's own client uses the same loopback path.

**Ordering.**
- Channel 3 gives one total order from the host, so a chat line and its comms echoes are never reordered.
- Chat and feed are separate surfaces and need no cross-ordering on screen. Only the history log (H21) interleaves feed lines with **comms echoes** (pings, Say lines, emotes, acks — free chat text stays in chat scrollback), ordered by `(hostTick, arrival index)`; the host stamps both, so no clock sync is needed.
- **A ping on an unknown entity** (for example a WorldItem spawned in the same tick on channel 1) places its marker at `pos` and binds `targetId` lazily for `pingBindGraceMs` = 1000:
  - if the id appears in time, the marker follows it;
  - if Grid Inventory's despawn tombstone says the entity is gone, the ping ends;
  - if the grace expires, the ping degrades to a plain "Look here" at `pos` — never a wrong address chip.
- Mailboxes and houses come from the World Generation tables every peer already has, so they have no race.

**Bandwidth** (assumes 48 B framing per message, uncoalesced — a spike item).

| Case | Down per client | Up per sender | Host up |
|---|---|---|---|
| Realistic busy 8-player session (each: a chat line per 5 s, a ping per 10 s, an emote per 30 s) | ≈ 265 B/s ≈ 2.1 kbps ≈ **9 B/tick** | ≈ 0.25 kbps | — |
| Burst: all 8 send 3 chat lines (60 chars) + 1 ping + 1 emote in one second | ≈ 3.9 KB ≈ 31 kbps for 1 s | ≈ 3.6 kbps | ≈ 220 kbps for 1 s |
| Same burst with pasted 200-char lines | ≈ 58 kbps for 1 s | ≈ 7 kbps | ≈ 410 kbps for 1 s |

- **Sustained budget:** **≤ 10 B/tick** per peer (≈ 2.4 kbps, 6 % of spec 06 §4.2's 40 kbps). All eight players pasting 200-character lines at the courtesy limits (≈ 33 kbps) is not a design case for honest humans, so no pacing is proposed.
- **Up budget:** Player Character's transforms already exceed spec 06 §4.2's 8 kbps up budget by ~12 %. Comms adds ≈ 0.25 kbps realistic and 3.6–7 kbps in a transient burst. Networking & Session should re-baseline the up budget to ≥ 12 kbps (hand-off).

**Typing bit.** The "Typing…" name-tag state (H18) is 1 bit in the owner's unreliable transform beside the speaking bit, with the same 600 ms receiver timeout (Player Character hand-back).

**Speaking flag** (owned by Proximity Voice; the HUD only reads `IVoiceState.IsSpeaking(slot)` plus a change event).
- **Replication:** it must be replicated, because the compass shows teammates beyond the 30 m falloff whose voice stream a receiver may not get.
- **PROPOSED encoding:** 1 bit in the owner's unreliable transform packet (taken from Player Character's `pktSeq`, needs its agreement). Receivers time out after 600 ms so a lost packet cannot leave a stuck icon. Debounce: on immediately, off after a 300 ms hang.
- **Fallback:** a `SpeakingChanged` event on channel 3 (≈ 6 kbps for 8 chatty players — more than all other comms combined).

**JoinState / RejoinState contribution** (channel 2, ≤ **4.4 KB**).
- **Contents:**
  - active pings: ≤ 16 × 22 B (slot, `pingId`, type, kind, `targetId`, pos, `remainingMs u16`, ack mask) ≤ 352 B;
  - the last 20 chat and Say lines (no ping or emote echoes), ≤ 4 KB;
  - `commsSeq` at the snapshot.
- **Catch-up:** the joiner buffers comms with `commsSeq` above the snapshot until the last chunk is applied, and discards comms at or below it.
- **Departed players:** history lines from a departed player carry a display-name snapshot (≤ 48 B) rather than a slot, in case Prep reuses slots (Networking & Session to decide).

**Disconnect, kick, host lost.**
- **Disconnect, freeze or kick:** the host sends `PingPurge{slot}`, and that sender's pings vanish on every peer **immediately**, not after 120 s. Their chat lines stay.
- **Host lost:** the P0 "Host lost" banner shows, all pings clear, pending echoes become "not delivered", and chat scrollback stays visible until the player returns to the menu. There is no migration (spec 06 §6).

**M0 spike items** (all **NOT SOURCEABLE**; extends Grid Inventory OQ12):
1. C# `[Rpc]` Mode, TransferMode and TransferChannel in 4.7.1, and whether a separate channel gives independent head-of-line behaviour.
2. `GetRemoteSenderId()` on loopback, over the Steam `MultiplayerPeer`, and after reconnect.
3. Whether client→client RPCs are blocked, and what happens when the receiver node is not yet ready.
4. Variant overhead for a `byte[]` argument, against the 48 B framing assumption.
5. Fragmentation of an ~807 B reliable message on ENet and on Steam.
6. Exclude-sender broadcast for `Emote`.
7. Peer-removed signal ordering against the last relayed comms.
8. Steam Voice from C#: whether it is distributed beyond 30 m, and whether a speaking / VAD state is readable.
9. Spare-bit packing in a `MultiplayerSynchronizer` property.
10. Confirmation that no ordering exists across channels.

## Acceptance Criteria

*Moved from HUD; tags and fixture as in HUD Acceptance Criteria. Not re-reviewed — H-PING-4 is void under CF-9.*

### Comms (H13–H19)
- **H-PING-1 [Logic, parametrised] M0 (Danger M3)** **GIVEN** a quick ping on each hit type, **THEN** teammate → "Over here" (Teammate, `targetId` = slot); mail / pile / WorldItem → Item + address chip; mailbox / house → Address "17 Elm St"; enemy → Danger; construct → "Look here" + icon; ground → "Look here". A hit at 149.9 m sends; at 150.1 m nothing is sent and a click-stop cue plays. Own capsule, own vehicle and Build ghosts are excluded from the ray. In solo nothing happens.
- **H-PING-2 [Logic] M0** **GIVEN** a ping at 0, **THEN** an attempt at 499 logs `CooldownRejected` and sends nothing; 500 sends; the refusal does not move the window.
- **H-PING-3 [Logic] M0 (F6)** **GIVEN** an own Look ping at P (t 0), a second 10 m away (1000) and a third a further 10 m away (2000), **THEN**:
  - the oldest drops and 2 are live; a same-type ping within 2000 mm refreshes to +10000 with an unchanged count, while 2001 mm is new; a different type within 2 m is new;
  - a third ping qualifying for refresh drops nothing;
  - a modded sender's 5 pings leave 2 per sender id (≤ 16 for 8 senders);
  - a ping is live at 9999 and gone by 10300;
  - a purge removes it at once; an entity ping ends on target despawn; a ground ping stays fixed.
- **H-PING-4 [Logic] M1** **GIVEN** a reticle 40 px from a teammate's live marker, **THEN**:
  - it acks; nearest wins, ties go to the lower `pingId`; at 41 px a new ping is placed;
  - over your own marker it refreshes;
  - the sender gets one toast per distinct acker, and a repeat ack by the same player is idempotent (no second toast);
  - the marker shows the first acker's hat + N;
  - an ack on the host-expiry tick is dropped and the acker's ✓ reverts.
- **H-PING-5 [Logic] M0** **GIVEN** a ping, **THEN**:
  - every peer gets the marker, compass echo, 3 s edge arrow and dim line "Sam · Danger · 14 Larch Ln";
  - a 4th ping or emote line from one sender within 10 s places its marker and reaches history but writes no chat line;
  - the 8 pings nearest the local player are labelled;
  - a ping on a locked-district mailbox shows a lock glyph and "Deliver here" is not refused.
- **H-WHEEL-1 [Logic] M1** **GIVEN** hold C / D-pad ↑ in each router state, **THEN**:
  - the wheel is live in World, Aiming, Holding-closed, Build, Map, Dead and Mounted, and not in Panel, Holding-open, TextEntry or Pause;
  - on open it raycasts once, look freezes and movement continues;
  - after a 90° turn the typed ping still anchors to the opening ray;
  - the dead zone, a reserved slot or Back sends nothing and logs no line;
  - a running deliver hold continues.
- **H-WHEEL-2 [Logic] M1** **THEN**:
  - pages are Pings 7 + 1 reserved (incl. Pass to me, anchored on and following the sender), Say 8, Emotes 6 + 2; Q/E, the wheel and LB/RB switch pages; it reopens on the last page;
  - in Toggle mode the second press selects;
  - knockdown, mount or panel open cancels; a release stamped at the knockdown tick sends, one tick after it cancels;
  - knocked down: Emotes greyed "Knocked down"; Map before M2: Pings greyed "Close the map";
  - solo: opens on Emotes only; controller disconnect cancels and does not reopen.
- **H-WHEEL-3 [Integration] M1** **GIVEN** the list alternative, **THEN** every entry sends the same message as the wheel, keyboard-only and pad-only. A Say entry sends `sayId`, spends 1 chat token, and renders in each receiver's locale.
- **H-CHAT-1 [Logic] M0** **GIVEN** the chat state table, **THEN**:
  - Enter → Typing (movement zeroed, "Typing…" bit on);
  - send with a token → pending line at once, then close; empty bucket → stays Typing with "Slow down" for 1 s; Esc or empty Enter sends nothing;
  - knockdown or phase change → stays Typing;
  - pause hides the field and suspends the draft, Resume restores it;
  - idle closes at 20000 ms (19999 stays open), keeps the draft, and Up recalls the draft first.
- **H-CHAT-2 [Logic] M0** **THEN**:
  - 6 lines show and fade at 10 s by default; "Always show" never fades; when open, 10 lines; the 101st line evicts the oldest of 100;
  - your name gets a highlight bar;
  - system lines are comms echoes and the gap line only;
  - opacity 69 % forces the 2 px outline, 70 % does not, 49 % clamps to 50;
  - solo: no panel, no own mic-state icon, and Enter does nothing; the chrome appears at roster 2 and hides at roster 1, keeping scrollback.
- **H-CHAT-3 [Logic] M0** **GIVEN** a chat mute on account A, **THEN** A's existing lines are purged (not restored by unmute) and later lines are discarded on receipt and logged. A pings / emotes mute hides A's markers, bubbles and lines. Your own row offers no mute.
- **H-EMOTE-1 [Logic] M1** **THEN**:
  - ids 0–5 play; 6 and 7 are ignored and logged;
  - the local lockout refuses at 1999 ms and allows at 2000;
  - the sender plays locally and is excluded from the relay;
  - the bubble lasts 2000 ms, one per player, and always shows (pose suppression is Player Character's decision).
- **H-EMOTE-2 [Logic] M1** **GIVEN** two emotes from one sender, **THEN** the second is ignored at a `hostTick` difference of 44 ticks and accepted at 45. An emote from a sender known knocked down drops (`Drop: sender_dead`). Emote lines are P3 and mutable.
- **H-VOICE-1 [Logic] M0 — BLOCKED on Proximity Voice** **GIVEN** `IsSpeaking` true at 0, **THEN** a static speaker glyph and a compass "talking" state appear. False at 100 clears it at 400; no update after 0 clears it at 600. Nothing animates. Own mic shows open / PTT held / muted.

- **H-SAN-1 [Logic, parametrised] M0** **THEN**:
  - stripped: U+202E, U+2066–2069, U+200B, U+2060, U+FEFF, U+200E/F, U+061C, U+2028/9, U+00AD, U+3164, U+2800, tags U+E0000–E007F, lone surrogates, C0/C1 including U+0085 and newline;
  - kept: ZWNJ in "می‌خواهم" and ZWJ in a family emoji;
  - NFC applied;
  - 200 graphemes pass, 201 truncate at the boundary;
  - 44 family emoji (792 B) pass, the 45th is refused (810 B);
  - an empty-after-sanitise line is discarded with a `Sanitised` record and no blank row.
- **H-SAN-2 [Integration] M0** **GIVEN** "[b]x[/b] [url=a]y[/url]" as chat or as a name, **THEN** the rendered text equals the literal with no style runs. A receiver truncates (never rejects) more than 200 graphemes or 800 B.

### Network
- **H-NET-1 [Logic] M0** Payload sizes: `Ping` 18 B up / 27 B relayed; `PingAck` 9 B; `PingPurge` 8 B; `Emote` 8 B; `ChatMessage` relay 11 B + text; Say line 12 B. JoinState ping record 22 B (16 pings ≤ 352 B); the whole HUD JoinState block ≤ 4.4 KB.
- **H-NET-2 [Integration] M0** Host relay:
  - chat of 0 B drops; 1 and 800 B relay; 801 B or invalid UTF-8 drops;
  - out-of-range enums drop; `pos` at bounds + 10 m relays, + 10.01 m drops; an unknown `targetId` relays; an unknown ack `pingId` drops; `emoteId` 7 relays, 8 drops;
  - an invalid message never disconnects or kicks; one log line per (sender, kind, reason) per 10 s;
  - 100 valid pings in 1 s are all relayed (no rate limit);
  - a forged sender is ignored (`senderSlot` comes from the RPC peer).
- **H-NET-3 [Logic] M0** **GIVEN** Ping(s3), Chat(s3), PingPurge(3), Chat(s3), **THEN** the ping is gone, the first chat shows with the retained name, and the second is dropped with a throttled log.
- **H-NET-4 [Logic] M0** A pending echo is replaced in place on matching `(senderSlot == me, clientSeq)`. ✗ shows at 5000 ms (still pending at 4999). A late echo flips to delivered with no duplicate row. A ping ghost is replaced by the relayed `pingId`.
- **H-NET-5 [Logic] M0** JoinState replays 20 chat lines and ≤ 16 pings. Comms with `commsSeq` ≤ the snapshot are discarded and those above are buffered. Replays give no blip, alert, read-out or unread count. A ping aged ≥ 10000 ms logs `expired_on_arrival`. A valve gap of 5 → one line "5 messages not received". Seq 65535 → 0 is newer; 65534 → 1 is a gap of 2.
- **H-NET-6 [Logic] M0** A ping on an unknown `targetId` binds if the id appears at 999 ms, ends on a tombstone, and degrades at 1001 ms to "Look here" with no chip. On host lost: pings clear; echoes ✗; quick ping click-stops; Enter shows "Not connected"; the wheel cannot open; the timer freezes with no threshold announcements; the Host lost banner shows.
- **H-NET-7 [Integration] M0** The realistic 8-player fixture is ≤ 10 B/tick per peer over 60 s (≈ 8.8 expected); the burst fixture is ≈ 3.9 KB down. Payload bytes are BLOCKING; framed bytes are ADVISORY until M0 spike item 4.

- **H-FT-1 [MANUAL, playtest] M0** **GIVEN** ≥ 3 four-player sessions, each with one voice-off player, **THEN**:
  - pooled over ≥ 10 of that player's pings, ≥ 50 % are answered within 30 s (a different player moves within 20 m — excluding anyone already there — or replies by ping or chat);
  - each session has ≥ 1 throw-to-teammate within 10 s of a ping or chat line;
  - no voice-off player reports feeling "left out".

## Open Questions

| # | Question / request | Owner | Resolve by |
|---|---|---|---|
| CQ-1 (was HN-1 a, c) | **Input & Controls:** (a) add **Mounted** to the comms wheel's live states (Player Character P14 already allows the emote radial there); (b) replace "live-region announcements rate-limited one per 0.5 s per category" with H26's per-category digest that never drops; (c) bind a **history overlay** action and a **status-read** action (pad View is taken by Sort); (d) the first-pad-input line uses the HUD tip slot, not a toast; (e) the OQ-IC-11 answer (ring, slash, focus bracket — Visual/Audio) and OQ-IC-12 for HUD (200 % reading text) are adopted here. | Input & Controls | before its /design-review |
| CQ-2 (was HN-2 a, b) | **Player Character:** (a) a typing bit and a speaking bit in the owner's unreliable transform (Network; 600 ms receiver timeout); (b) one `Emote` message shared with its 2 B one-shot; (c) whether a pose is suppressed while holding a stack or mounted (HUD always shows the bubble); (d) the focus bracket replaces any world-side outline (anchor and bounds only). | Player Character | before its /design-review |
| HN-8 | Speaking state readable on every peer, including beyond 30 m; own mic mode; Steam Voice VAD from C# (NOT SOURCEABLE). | Proximity Voice | M0 spike |
| HN-9 | Comms channel 3; RPC sender identity; JoinState chunk for the HUD block (≤ 4.4 KB); slot reuse in Prep; **re-baseline the per-client up budget to ≥ 12 kbps** (transforms already exceed 8 kbps by ~12 %); the 10 M0 spike items in Network. | Networking & Session, technical-director | M0 spike |
| HN-12 | The ping model reused for map pings (spec 09 §2.6; the map's own click / hold flow). | Map & Address UI | M2 |
| HN-16 | **Emote set and Say-page wording** (Tip the hat, Salute, Point, Thumbs-up, Shrug, Wave; 8 Say lines in the carriers' earnest voice). Accessibility asks for ≥ 12 canned phrases for gamepad chat parity — expand to a second Say page if M0 playtests show pad players can't say what they need. | narrative-director, creative-director | M1 |
| HN-20 | **Quick-ping auto-typing** is a spec delta (spec 09 has only the four wheel types). Playtest signal to revert: players use typed wheel pings for > 80 % of pings, or report wrong auto-types (e.g. "Item" when they meant the house). | game-designer | M0 playtest |
| HN-21 | **Chat stops the carrier.** If playtests show players avoid chat because of it, consider "walk while typing" with WASD routed to movement (an Input & Controls change). | game-designer / Input & Controls | M0 playtest |
| CQ-3 | Explicit ping-ack action (CF-9) — revisit after the M0 Fellowship playtest. | game-designer | M0 playtest |
| CQ-4 | Inherited findings CF-1…CF-16 — resolve all before the first /design-review of this GDD. | network-programmer, ux-designer, game-designer | after the M-1a GUARD lifts |
