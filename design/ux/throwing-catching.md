# UX Spec: Throwing & Catching — aim preview, arc label, prompt slot and catch feedback

> **Status**: In Review — lean `/ux-review` 2026-10-16: NEEDS REVISION (5 blocking) → revised the same day; Accessibility and Pattern Library dimensions NOT ASSESSED (no `design/accessibility-requirements.md`, no `design/ux/interaction-patterns.md`); confirmatory `/ux-review` re-run recommended
> **Cited by**: throwing-catching story 019 (arc label, reason strip, live region — M-1a); stories 022–024 presentation follow-ups (M0 catch rows)
> **Sources**: `design/gdd/throwing-catching.md` (UI Requirements, Visual/Audio "Label" + Accessibility rows, F7, TC-U-1…3), `design/ux/throwing-catching-seed.md` (input only), `design/gdd/hud-notifications.md` (H4, H9–H12, H23, H26, F1), `design/gdd/input-controls.md` (R2, R4.3, R6, R9, G2, G7, G9), `design/art/art-bible.md` §3/§7, `design/ux/inventory-panel.md`, ADR-0019
> **Cross-reference check (2026-10-16)**: GDD UI Requirements + UX Flag 6/6 items covered (arc label, prompt-slot catalogue, hotbar pulse + caught history line, incoming cue incl. off-screen, live region Brief/Full, hands-up pose trigger); new patterns for a future `design/ux/interaction-patterns.md` (none exists): Marker-anchored label (OQ-TC-9); navigation matches Input & Controls R2/R4.3 and `inventory-panel.md` (Throw not live with the panel open; "Put it away first" owned by Input); accessibility tier assumed (OQ-TC-6); every data-dependent element has an empty / unavailable state (Data Requirements)
> **Author**: user + ux-designer (autonomous run, `/ux-design throwing-catching`)
> **Last Updated**: 2026-10-16
> **Journey Phase(s)**: unknown — no `design/player-journey.md`
> **Platform Target**: PC (Windows / Linux); keyboard + mouse primary, gamepad full; no touch
> **Template**: UX Spec

---

## Purpose & Player Need

**The player is deciding, in about half a second, whether to commit a whole stack of mail to the air.** This flow is the screen-space layer around that decision and its aftermath:
- the **arc label** beside the end marker;
- the **prompt-slot reason lines** for throws that cannot start or did not happen;
- the **live-region line**;
- on the receiving side (M0), the **catch prompt states**, the incoming-throw cue, the caught/fumbled feedback and the hotbar pulse.

The ribbon itself (line patterns, markers, hat pins) is specified in the GDD's Visual/Audio section and built by story 018. This spec **places and words** everything around it.

The player arrives wanting to know three things before they let go:
1. **Will it hit?** Green / red / white is carried by line pattern, marker shape and the label's glyph chip, never by colour alone.
2. **Which house is that?** The address, so a red arc names the wrong house rather than just saying "no".
3. **What does it cost?** "×20 · −120¢". The whole stack goes (CD D-D), so the stake must be legible. This is the GDD's mitigation for OQ8 / Grid OQ21 — **money is shown [D]**.

After the press, they need to know **why** nothing happened when nothing happens ("Nothing to throw", "Hold to aim", "Couldn't throw"), because Input & Controls G9 forbids a press followed by nothing.

Without this layer the arc is a guess (Pillar 2: the arc tells the truth about the town). A misdelivered 20-stack would read as the game's fault rather than the thrower's, and the comedy depends on it being the thrower's.

**Scope by milestone.**
- **M-1a — story throwing-catching/019:** the arc label (all classes, placement, flip, 100–200 % text, settle behaviour), the M-1a reason-strip lines, and the live-region line.
- **M0 (catch slice, stories 022–024 and their presentation follow-ups):** the catch prompt states, incoming cue, caught / fumble / race-lost lines, hotbar pulse, teammate hat-pin label and the hands-up pose trigger. Specified here so the shared surfaces (prompt slot, live region) are designed once.

---

## Player Context on Arrival

- **When.** Within the first minutes of the M-1a greybox: the first time the player selects mail on the hotbar and holds RMB / LT. The prototype hypothesis is that players *choose* to throw (spec 00 M-1a fun gate), so this is the game's signature moment, not a side verb.
- **What they were doing.** Walking or sprinting along a street (movement never stops while aiming — T1, P3), usually just after a delivery hold or while approaching the next house. The camera eases to the aim profile (shoulder +0.75 m, boom 2.4 m) over 0.1 s.
- **Emotional state.** *Playful but time-pressured* — a skill shot under a shift clock. The label must be readable at a **glance** (≤ 250 ms reading budget while the camera moves), so its shape is fixed (always two lines) and its position is predictable (up-right of the marker unless that is blocked).
- **Voluntary.** Always: aiming starts only on the player's press. Catch prompts (M0) are game-initiated — a teammate's flight makes the prompt flip before the press — and must never steal a deliver hold (Input R4.3 D3 gate).
- **Prior knowledge.** None. The label teaches the colour grammar by pairing it with words on every throw.

**Alternatives considered (thorough).**
1. A floating label above the *carrier* (fixed position) was rejected: it pulls the eye away from the landing point, which is the read at 20 m (art bible §7).
2. Showing only the verdict glyph without money was rejected by the GDD [D]: the stake must be legible.
3. A third line naming a teammate on the arc was dropped by CD D4: the hat pin carries the name.

---

## Navigation Position

This is a **flow inside World context**, not a screen. It lives at Run → World (OnFoot) → **Aiming** sub-state (Input & Controls R2 `aiming` flag) for the thrower, and Run → World / Holding-closed → **catch-armed** (held state of Interact) for the catcher (M0). It is never reachable with an overlay open (Panel, Map, Station, History), while Mounted or Dead (no message — Throw is not live), while swimming (refused with "Can't throw while swimming"), or while Holding (Input's "Put it away first" prompt row).

---

## Entry & Exit Points

| Entry Source | Trigger | Player carries this context |
|---|---|---|
| World, a throwable entry selected | press-and-hold **RMB** / **LT** (aim-release; Toggle mode: first press) | Latched entry id and units (T3 entry latch). The arc appears within ≤ 2 rendered frames; the label shows on the first frame (first appearance is immediate). |
| World, hands slot / empty slot / swimming | the same press | No aim starts. Reason line in the prompt slot ("Select mail to throw" / "Nothing to throw" / "Can't throw while swimming"). |
| Holding-closed | the same press | No aim. "Put it away first {Back}" (Input & Controls ruling 3). |
| World / Holding-closed, a teammate's flight predicted to reach you (M0) | none — the prediction flips the prompt | Prompt "Catch ×20 from Jules", carets on the parcel, edge chevron if off-screen. |

| Exit Destination | Trigger | Notes |
|---|---|---|
| World (thrown) | release after `aimHeld ≥ throwMinAim` (default 0.25 s) | Arc, marker and label fade out over 0.08 s on the release frame. The trail (every peer) carries the verdict. **Irreversible**: the whole entry is in flight. |
| World (cancelled) | release before `throwMinAim` | Reason "Hold to aim". Nothing sent. |
| World (cancelled) | **Esc / B** (Back step 3); any G2 condition (entry changed, swimming, knocked down, panel opened, window focus lost) | Reason "Aim cancelled". Nothing sent. |
| World (rejected after release) | host reply `stale_entry`, `bad_direction`, `throw_cooldown`, other 64–71 | The launch ghost fades over 150 ms where it is; the entry stays in its slot; reason "Couldn't throw". |
| World (catch resolved, M0) | host CAUGHT / FUMBLE / race lost | The hotbar pulse, or a toast — see Interaction Map. |

---

## Layout Specification

**Units.** 1080-reference px (ADR-0019 §7, `canvas_items` / `keep_height`). `Wref` = 1080 × aspect (1920 at 16:9). `t` = the **reading-text scale** (HUD H23: 100–200 %); the arc label is reading text, so it scales to 200 %. The marker and ribbon do not scale with text (art bible §7: markers are constant screen size; Force patterns ×1.5).

### ASCII Wireframe

Aiming at 1920×1080, t = 1.0. The end marker is ≈ 20 m away, up-right of the reticle.

```
                                                     ┌───────────────────────────────┐
                                                     │[✗] 9 Jade Close — wrong house │   ← line 1: glyph chip + place (Chalk)
                                                     │    ×20 · −120¢                │   ← line 2: ×N (Gold) · money (Chalk)
                                                     └───────────────────────────────┘
                                                   ╳   ← end marker (heavy X, ≥ 24 px)   (label: 16 px up-right of marker)
                                       ╌ ╌ ╌ ╌ ╌ ╌
                           ╌ ╌ ╌ ╌ ╌ ╌              (red dashed ribbon, 6 px + 2 px Charcoal outline — story 018)
                ╌ ╌ ╌ ╌ ╌
         ✋ (carrier, wind-up pose)          ·  ← crosshair dot (camera forward only, 4 px)

                          ┌──────── prompt slot (screen-fixed, top 96 px under the reticle, ≤ 720 wide) ───────┐
                          │ 🔒 Nothing to throw                       (reason strip: dimmed + lock, HUD F1 life) │
                          └───────────────────────────────────────────────────────────────────────────────────┘
                                         (toast lane: prompt bottom + 16)                    ┌hotbar strip┐
                                                                                             └────────────┘
```

Label anatomy (t = 1.0):

```
 ┌──8──┬──────────────────────────────────────────────8──┐
 │     │ 17 Elm St                       (18 px Medium,  │  6 px top pad
 │ ┌──┐│                                  Bold address)  │  line height 22
 │ │✓ ││ ×20 · +240¢                     (18 px; ×20     │  line height 22
 │ └──┘│                                  in Gold)       │  6 px bottom pad
 └─────┴─────────────────────────────────────────────────┘
  chip 28×28, centred vertically; gap 8 to the text column. Plate height 56 at t = 1 (112 at t = 2)
```

### Layout Zones

| Zone | Rect / anchor | Rule |
|---|---|---|
| **Aim ribbon + end marker** | world-projected (story 018) | Exact every frame. Not specified here beyond the publishing of the marker's screen rect. |
| **Arc label (pill)** | anchored to the end marker's screen rect `M` (centre `m`, half-size `r` ≥ 12) | Placement algorithm below; published each aiming frame together with `M` as one **protected rect** to the HUD (H11). |
| **Safe inset** | 24 px from every viewport edge (the GDD's 24 px, inside HUD's 48 / 32 safe area for persistent widgets — the label is transient and follows the marker) | The label never leaves it. |
| **Prompt slot / reason strip** | HUD H11: screen-fixed, centred, top edge 96 px under the reticle (y 636 at 1080), max 720 wide | Reason lines show here (dimmed + lock). The label must never intersect this rect, including the reserved height of one reason line (40 × t px) even when no reason is showing. **Keep-out rect** (with or without a prompt showing, and for the story 019 stub strip): x centred, w 720, top y 636, h (40 + 40) × t (one prompt line + one reason line). |
| **Crosshair dot** | screen centre | 4 px Chalk dot + 1 px Charcoal outline; camera forward only. |
| **Catch prompt** (M0) | the same prompt slot | "Catch ×20 from Jules" / "Catching…" — Input & Controls' prompt grammar. |
| **Incoming cue** (M0) | screen-projected on the inbound parcel (HUD H12 MarkerLayer); edge chevron at the 48 / 32 safe edge | Carets ≥ 24 px; chevron 32 px. |
| **Hotbar strip** | HUD H10 (bottom-right; see `design/ux/inventory-panel.md`) | Catch pulse target. |

**Label size.**
- Padding: 8 px horizontal, 6 px vertical, × t.
- Glyph chip: 28 × t px square, r = 6.
- Text column: two logical lines, each 18 × t px on a 22 × t px line height.
- Plate height: `(12 + 44) × t` = 56 / 84 / 112 at 100 / 150 / 200 %. It grows by 22 × t per wrapped line.
- Width: shrink-wraps to content, min 120 × t, **max `0.35 × Wref`** (672 px at 1920; 504 at 1440). Line 1 wraps at word boundaries and never truncates or ellipsises an address. Line 2 wraps only at the `·` separators.
- **Plate:** Charcoal #23252B; 2 px #787D8C edge (≥ 3 : 1 on Charcoal — the seed's #5A5E6B fails SC 1.4.11); radius 8 (not a full pill radius, which the art bible reserves for buttons — §3/§7).

**Placement algorithm (normative).** Each aiming frame, with the marker rect `M` and the label size `L`:
1. Candidates, in order:
   - **UR** — label bottom-left at `(m.x + r + 16, m.y − r − 16)`;
   - **UL** — label bottom-right at `(m.x − r − 16, m.y − r − 16)`;
   - **DR** — label top-left at `(m.x + r + 16, m.y + r + 16)`;
   - **DL** — label top-right at `(m.x − r − 16, m.y + r + 16)`.
2. A candidate is **valid** iff it:
   - lies fully inside the 24 px inset;
   - does not intersect `M` (the label never covers its marker);
   - does not intersect the prompt rect plus one reserved reason line.
3. **Sticky choice:** keep the previous frame's candidate while it stays valid; otherwise take the first valid one in order. This stops the label jumping sides frame to frame as the stick moves.
4. **No valid candidate:**
   - if the conflict is the prompt rect, place the label **above the prompt rect** — bottom edge 8 px above the prompt's top, x centred on `m.x`, clamped to the inset (the GDD's "flips above the marker rather than meet the prompt rect");
   - else clamp the UR candidate into the inset.

   In both cases it still must not cover `M`. **Last resort** (the marker sits on or under the keep-out rect): place the label above the keep-out rect, shifted horizontally to the side of `M` with more room, clamped to the inset. **Priority when not every rule can hold:** never cover `M` > stay inside the inset > avoid the keep-out rect.
5. **Marker off-screen** (beyond the viewport, or behind the camera on a steep lob): the marker is not drawn (story 018). The label clamps to the nearest point of the 24 px inset along the direction from screen centre to the marker's projected point (behind the camera: use the projected point mirrored through screen centre, i.e. the direction of the marker's camera-space x / y), then the step-4 keep-out rule applies, and its glyph chip gains a **direction arrow** (Chalk triangle, 10 × t px) on the side facing the marker. The verdict is never hidden while aiming.

### Component Inventory

| # | Component | Type | Content | Interactive | Pattern |
|---|---|---|---|---|---|
| T1 | **Arc label plate** | custom-drawn `Control` (one, pooled; `FocusMode.None`) | Charcoal plate, glyph chip, two text lines | no | new: *Marker-anchored label* |
| T2 | **Glyph chip** | part of T1 | Per class (Information Hierarchy table): Accept = Go Green fill + Charcoal ✓; Misdeliver = Stamp Red fill + **Bold Chalk ✗ ≥ 20 × t px** (Chalk on red ≈ 4.4 : 1: the glyph is a graphical object, so SC 1.4.11's 3 : 1 applies at every resolution, including 720p where 20 ref px renders at 13.3 px); neutral classes = Chalk White fill + Charcoal glyph (—, ≈, >, clock, plain disc dot, wall). **Updates every frame with the marker** (exact). | no | — |
| T3 | **Label line 1** | text | Place text (no glyph — the glyph is in T2); addresses in Bold | no | — |
| T4 | **Label line 2** | text | "×N" in **Payday Gold** (stack-count voice) + " · " + money / state in Chalk. **Never red or magenta** — the sign carries the meaning. | no | — |
| T5 | **Prompt-slot reason line** | HUD reason strip (H11), fed one reason key by Throwing | Dimmed per HUD H11's reason-strip style (owned by HUD — not Input's 60 % pending-op opacity) with a **lock icon** + reason | no | HUD H11 reason strip |
| T6 | **Live-region line** | invisible `Control`, `AccessibilityLive = Polite` (4.7 typed `AccessibilityServer.AccessibilityLiveMode`) | Aim lines (Brief / Full) and reason lines | no | HUD H26 |
| T7 | **Catch prompt** (M0) | HUD prompt (`PromptData` from Throwing) | "[E] Catch ×20 from Jules" → "Catching…" + mitt icon; Toggle: 3 px draining bar under the prompt | — (press is Interact) | Input & Controls prompt grammar |
| T8 | **Inbound carets** (M0) | screen-projected marker (MarkerLayer, H12) | Four static Chalk corner carets (≥ 24 px bracket) around the inbound parcel, Charcoal 1 px outline. **Never green** (it does not pre-announce a catch). Catcher only. | no | HUD H12 focus-bracket family |
| T9 | **Edge chevron** (M0) | screen-edge marker | Chalk chevron 32 px with Charcoal outline at the 48 / 32 safe edge, pointing toward the off-screen or behind-camera parcel; static | no | Comms ping edge arrow |
| T10 | **Hotbar catch pulse** (M0) | effect on the hotbar slot | Slot outline thickness 2 → 5 → 2 px over 0.3 s + Gold count bounce (scale 1.2, 0.1 s) | no | — |
| T11 | **Teammate hat pin label** (M0) | part of story 018's pin | Name under the hat pin, Bold 18 × t on a Charcoal plate, ≤ 12 graphemes (HUD H9); no ✓ / ✗ | no | HUD H9 identity order |

### Information Hierarchy

1. **Verdict class:** the marker silhouette and line pattern (story 018) and the glyph chip. Exact every frame.
2. **Place:** line 1 (address for Accept / Misdeliver; the reason for neutral classes).
3. **Stake:** line 2 (×N first, then money).
4. **Refusals:** the prompt-slot reason (only when a press did nothing).
5. **Catch cues (M0):** the prompt ("Catch ×N from Jules"), then the carets / chevron.

**Label catalogue (normative EN strings; keys with arguments, never concatenated).** These reconcile the seed with the GDD's UI Requirements and its review corrections (the GDD wins; the seed is input only). The line-1 keys match story 005's key set and add the classes 005 leaves unnamed (Locked, WontFit, Blocked) — **flagged for story 005** (OQ-TC-4).

| Class (story 004 end class) | Chip glyph | Line 1 key → EN | Line 2 key → EN |
|---|---|---|---|
| Accept | ✓ (green chip) | `throw.label.ok.address` → "{house} {street}" e.g. "17 Elm St" | `throw.label.money.accept` → "×{n} · +{cents}¢" e.g. "×20 · +240¢" |
| Accept, all dead letters | ✓ | same | `throw.label.money.accept_dead` → "×{n} · +0¢ · dead letters" |
| Misdeliver | ✗ (red chip) | `throw.label.wrong.address` → "{house} {street} — wrong house" | `throw.label.money.miss` → "×{n} · −{cents}¢" |
| Misdeliver, capped by the floor | ✗ | same | `throw.label.money.miss_floor` → "×{n} · −{cents}¢ to floor · {b} returned" |
| Misdeliver, wallet at the floor | ✗ | same | `throw.label.money.miss_refused` → "×{n} · refused at floor · all returned" |
| Reject — Closed | — (bar) | `throw.label.closed.opens-at-delivery` → "Closed — opens at Delivery" | `throw.label.count` → "×{n}" |
| Reject — Locked district | — | `throw.label.locked` → "Not on today's route" | "×{n}" |
| Reject — Won't fit (non-mail / kind) | — | `throw.label.wontfit` → "Won't fit here" | "×{n}" |
| Ground | plain disc | `throw.label.ground` → "Ground" | "×{n}" |
| Water | ≈ | `throw.label.water.floats` → "Water — floats {s} s" (s = `waterReturnSeconds`, 10) | "×{n}" |
| Belt (M-1b) | > | `throw.label.belt.joins` → "Joins belt" | "×{n}" |
| Timeout | clock | `throw.label.timeout.drops` → "Drops ({s} s)" (s = 3) | "×{n}" |
| Blocked (wall, roof, construct, map edge) | wall | `throw.label.blocked` → "Blocked — drops here" | "×{n}" |
| Text gate (`previewSettleMs` = 0; first 100 ms after a class / address change) | the **new** class glyph (switches at t0 with the marker) | `throw.label.pending` → "…" | "×{n}" |
| Pending marker (`previewSettleMs` > 0) | none (neutral ring marker; chip empty) | `throw.label.pending` → "…" | "×{n}" |

- Line 2 always leads with ×N, **including N = 1** ("×1"), so the plate is always two lines.
- Money uses the locale formatter (`{cents}¢` in EN; the cents sign is a formatter token, not literal text).
- The Misdeliver floor figures follow F7 exactly (story 005 TC-F7-1).

---

## States & Variants

| State / Variant | Trigger | What changes |
|---|---|---|
| **Idle (empty)** | not aiming | Nothing from this flow on screen (hotbar strip shows the selected slot). |
| **Loading** | — | None: the label is built from the local client preview, with no async fetch. The only wait is the 100 ms text gate (Pending row). |
| **Aiming — first frame** | aim starts | Ribbon, marker, chip and **full label text appear at once** (first appearance is immediate, T3), with a 0.08 s alpha fade-in (0.1 s cut under Reduced Motion). |
| **Aiming — steady** | class and address unchanged | Label tracks the marker every frame (sticky candidate); text unchanged. |
| **Class / address change — text gate** | `ThrowLabel` key or args change (story 005 `DisplayedLabel` ≠ current) | Marker, line pattern and **chip glyph switch the same frame**. Both text lines show **"…" / "×{n}"** with the plate held at its previous width for the 100 ms until `DisplayedLabel` catches up, then the new text. **The previous class's text is never shown after a change [D]** (decision log; CD to confirm, OQ-TC-1). |
| **Pending marker** (`previewSettleMs` 1–250) | class change with the setting on | Neutral ring marker (story 018) + empty chip + "…" lines for the setting's duration (the text stays "…" until the settle ends, even though the 100 ms gate has passed), then the new class (never the previous verdict). |
| **Label flipped / clamped** | the UR candidate is invalid | UL / DR / DL / above-prompt placement (algorithm). Sticky until invalid. |
| **Marker off-screen** | the projected marker is outside the viewport | Label clamped to the 24 px inset with a direction arrow on the chip. |
| **Text scale 150 % / 200 %** | setting | Plate 84 / 112 px tall; width capped at 35 % `Wref`; addresses wrap (plate grows); placement re-evaluated with the bigger rect. |
| **Narrow window** (1280×720 = same reference layout; 4:3 → `Wref` 1440 → max width 504) | window | Wrapping occurs earlier; the never-truncate rule holds. |
| **Released (thrown)** | release ≥ `throwMinAim` | Arc, marker and label fade out 0.08 s (cut under Reduced Motion). The trail carries the verdict (story 018/020). Live region (Full only): "Threw 20 to 17 Elm St". |
| **Refused at press** | hands slot / empty slot / swimming | No aim. Reason line in the prompt slot (table below). Deny tick. (Holding: Input's "Put it away first" prompt row; nothing from Throwing.) |
| **Cancelled** | early release / Back / G2 | Arc fades 0.08 s; reason line. |
| **Host-rejected** | reply 64–71 or `bad_request` (`LastThrowReply`) | Launch ghost fades 150 ms in place; entry still in its slot; reason "Couldn't throw". |
| **Busy** (one-commit rule, Input G9) | Throw pressed while a deliver hold is pending | Input & Controls' "Busy" / "Release to retry" in the prompt slot (owned there; listed so Throwing never emits a second line). |
| **Catchable** (M0) | F5 prediction true for ≥ 3 ticks | Prompt flips to "Catch ×{n} from {name}"; carets on the parcel; edge chevron if off-screen. Off after 5 ticks false. |
| **Armed** (M0) | Interact press resolves to Catch / no target | Prompt "Catching…" + mitt icon for the whole arm window (a ≤ 200 ms tap never blinks it off); hands-up pose; Toggle mode adds the draining bar. |
| **Caught** (M0) | host CAUGHT | Hotbar slot pulse + count bounce; history "Caught ×20 — slot 3" / "Caught ×20 — bag" (M1); **no toast** (HUD H4). |
| **Fumble** (M0) | host FUMBLE | Catcher toast "Bag full — fumbled" (neutral styling, no red or magenta); hotbar flashes once. |
| **Race lost** (M0) | another catcher won | Loser toast "**{name}** caught it"; empty-clap pose. |
| **Thrower sees a catch** (M0) | catch of own flight | P3 toast "**{name}** caught ×20" / "**{name}** caught 15 · 5 fumbled". |
| **Swimmer presses** (M0) | Interact while swimming with an inbound flight | Prompt reason "Dry land required" (dimmed + lock). |
| **Reduced Motion** | setting | All fades → 0.1 s cuts; pulse → a single thickness step; carets cut in. |
| **Force patterns** | setting | Marker × 1.5; chip 42 × t px with glyph 30 × t px (the chip grows so the glyph fits; plate height = max(text column, chip) + padding); label text unaffected — it follows `t` (deviation from the GDD's "label sizes" row → OQ-TC-3 f). |

---

## Interaction Map

Mapping for **keyboard + mouse and gamepad (Xbox-positional)**; bindings from Input & Controls R6 (remappable; all prompt text uses live bindings).

| Action | Context | KBM | Gamepad | Immediate feedback | Outcome |
|---|---|---|---|---|---|
| Start aim | World (not Holding, throwable selected) | press-hold **RMB** | press-hold **LT** | Arc + marker + label within ≤ 2 rendered frames; aim camera eases 0.1 s; pad light tick | `aiming = true`; latch entry |
| Aim | Aiming | mouse look | RS (× `aimCamMult` 0.6) | Arc / marker / chip exact every frame; label text after the 100 ms gate | client preview only |
| Commit throw | Aiming, `aimHeld ≥ throwMinAim` | release RMB (Toggle: second press) | release LT (Toggle: second press) | Arc fades; launch ghost leaves the hand ≤ 1 frame; release haptic | `ThrowRelease` (Grid `throw` op) via the immediate-action sink (story 007) |
| Early release | Aiming, `aimHeld < throwMinAim` | release RMB | release LT | Arc fades; reason "Hold to aim" | nothing sent |
| Cancel aim | Aiming | **Esc** (Back step 3) | **B** | Arc fades; reason "Aim cancelled"; `sfx_throw_cancel` | nothing sent |
| Press with nothing throwable | World | RMB | LT | Reason line, deny tick | nothing sent |
| Press while Holding | Holding-closed | RMB | LT | "Put it away first {Back}" | nothing sent |
| Catch (M0) | World / Holding-closed, catchable or open air | press / hold **E** (Toggle: press, press) | **X** | Prompt → "Catching…"; hands-up pose | `CatchArm{down}` / `{up}` |

**Reason lines (M-1a, prompt-slot reason strip).** Dimmed with a lock icon, plus a soft deny tick (`sfx_ui_deny_tick`, proposal). Lifetime = **HUD F1** (`lifeMs = m × clamp(1500 + 400 × words, 2500, 8000) / 1000`, `m` permille 1000–3000 from the duration setting; `words` counts tokens of the rendered string that contain a letter or digit — an em dash, icons, binding glyphs and "×N" count 0), ended early by a focus change. A repeated press **restarts** the lifetime and never stacks. Each line is also sent to the polite live region. The UI event log gets `Show` with the cue id.

| Case | Cue id | Key → EN | F1 lifetime at 1× |
|---|---|---|---|
| Hands slot selected | `throw.reason.hands_slot` | "Select mail to throw" | 3100 ms (4 words) |
| Selected slot empty | `throw.reason.empty_slot` | "Nothing to throw" | 2700 ms (3 words) |
| Holding (cursor entry) | `throw.reason.holding` | "Put it away first {Back}" → "Put it away first [Esc]" / "[B]" (Input ruling 3 — same string Input shows; one emitter: Input owns it, Throwing never doubles it) | not a Throwing line: Input / HUD render it as a **prompt row** (disabled-verb, no F1 lifetime; HUD H-INT-2 "Prompt rows"). Story 019 emits nothing for it. |
| Swimming | `throw.reason.swimming` | "Can't throw while swimming" | 3100 ms (4 words) |
| Release before `throwMinAim` | `throw.reason.too_early` | "Hold to aim" | 2700 ms (3 words; the onboarding tip "Hold to aim · {Back} cancels" is Input & Controls' first-input moment, M4) |
| G2 cancel (Back, entry changed / stale, swimming, knocked down, panel opened, focus loss) | `throw.reason.cancelled` | "Aim cancelled" | 2500 ms (2 words → 2300, clamped to the 2500 floor) |
| Host rejection (`stale_entry`, `bad_direction`, `throw_cooldown`, other 64–71, `bad_request`) | `throw.reason.host_rejected` | "Couldn't throw" | 2500 ms (2 words, clamped) |
| Mounted / Panel / Dead / Map / Station / History | — | no message (Throw is not live or has another meaning there) | — |

**Catch lines (M0).**

| Moment | Surface | Key → EN | F1 lifetime at 1× |
|---|---|---|---|
| Catchable | prompt | `catch.prompt.catch` → "{Interact} Catch ×{n} from {name}" | while catchable (F5 hysteresis) |
| Armed | prompt | `catch.prompt.armed` → "Catching…" (+ mitt icon) | the arm window |
| Caught | hotbar pulse + history (M1) + live region | `catch.result.caught_slot` → "Caught ×{n} — slot {k}" / `catch.result.caught_bag` → "Caught ×{n} — bag" | no toast (HUD H4) |
| Fumble | catcher toast (P2) | `catch.result.fumbled` → "Bag full — fumbled" | 2700 ms (3 words; matches HUD H-INT-2) |
| Race lost | loser toast (P2, contention key) | `catch.result.race_lost` → "**{name}** caught it" | 2700 ms (name = 1 token) |
| Thrower informed | thrower toast (P3) | `catch.result.thrower` → "**{name}** caught ×{n}" / `catch.result.thrower_partial` → "**{name}** caught {a} · {b} fumbled" | 2500 ms (2 words, clamped) / 3500 ms (5 words, e.g. "Jules caught 15 · 5 fumbled") |
| Swimmer | prompt reason | `catch.reason.swimming` → "Dry land required" | 2700 ms (3 words) |

Third-party teammates get no line for a catch (HUD H4: it concerns one player).

---

## Data Requirements

| Data | Source | R / W | Notes |
|---|---|---|---|
| End class, polyline, marker screen rect, `EntryStale`, `NotThrowable` | Throwing `ThrowPreviewState` (story 004) via `IThrowPreviewRead` | Read | Marker rect from story 018's projection. |
| `ThrowLabel` (keys + int args) and `PreviewDisplay { PendingMarker, DisplayedLabel, Cue, LiveRegionDue }` | Throwing story 005 (`ThrowLabelModel`, `PreviewSettle`) | Read | **Godot never computes money or class** (story 019 forbidden rule). The view compares `DisplayedLabel` with the current `ThrowLabel` to drive the text gate. |
| `LastThrowReply { Seq, Reason }` | story 007 `ThrowReplyPart` | Read | Drives "Couldn't throw". |
| Prompt rect (incl. reserved reason line) | HUD layout (H11) | Read | Placement constraint. |
| Protected rect (marker ∪ label) | → HUD each aiming frame | **Write** (publish) | HUD H11 moves toasts to the alternate anchor and fades markers. |
| Reason key | → HUD reason strip (`PromptData`'s one reason key; Player Character OQ28 precedence) | Write | Until ADR-0018 exists, a stub strip implementing F1 lifetime. |
| `t` (reading text scale), Reduced Motion, Force patterns, `previewSettleMs`, `previewSoundEnabled`, **Throw announcements (Off / Brief / Full)** | Profile & Settings | Read | The announcements setting is new (OQ-TC-2). |
| Live bindings / glyph set | Input & Controls | Read | `{Back}`, `{Interact}` tokens. |
| Catch prediction (`Catchable`, `flightId`, units, thrower id, parcel screen point) | Throwing F5 client prediction (M0) | Read | Carets and chevron; name from the roster. |
| Catch results (`CAUGHT {slot / bag}`, `FUMBLE`, race lost) | Throwing domain events (M0) | Read | Presentation only after the host event (Game Feel). |

The UI owns none of this state; every row's owner is the named system.

**Update frequency and unavailable data.**

| Data | Updates | When unavailable |
|---|---|---|
| Preview state, marker rect | every rendered aiming frame (client preview, story 004 / 018) | no preview yet (frame 0 before the first step) → nothing drawn that frame; `NotThrowable` / `EntryStale` → no aim, the matching reason line |
| `ThrowLabel` / `PreviewDisplay` | every aiming frame; `DisplayedLabel` after 100 ms stability | never occurs during aiming: first appearance is immediate (story 005 TC-T3-1 d); after a change, the Text-gate or Pending-marker row of the catalogue |
| `LastThrowReply` | on reply arrival (event, keyed by `Seq`) | none pending → no line; a reply for an older `Seq` is ignored |
| Prompt rect | on HUD layout change (resolution, text scale) | HUD not present (pre-ADR-0018) → the stub strip's own fixed rect (OQ-TC-7) |
| Settings (`t`, Reduced Motion, Force patterns, `previewSettleMs`, announcements) | on change; applied on the next aim start (text scale re-lays the live label at once) | defaults: t = 1.0, off, off, 0, Brief |
| Live bindings | on rebind / device change | missing glyph → the text binding name in brackets (Input & Controls fallback) |
| Catch prediction (M0) | per client tick | none → no prompt flip, no carets |
| Thrower / catcher name (M0) | roster snapshot | player left → the roster's last-known name; never empty |

---

## Events Fired

| Player action | Request / event | Payload | UI event log |
|---|---|---|---|
| Commit throw | `ThrowRelease` (`throw {entryId, units, origin, dir}`, `seq` = reqId) | story 007 | — (rejection → `throw.reason.host_rejected` `Show`) |
| Refused / cancelled press | none | — | `Show` (reason cue), `Announce` |
| Aim label settles | none (live region) | — | `Announce` (aim line; superseded, never queued — HUD H26 state line) |
| Catch press (M0) | `CatchArm{down}` / `{up}` | story 022 | — |
| Catch result (M0) | none (consumes host events) | — | `Show` / `Announce` |

**Analytics:** none fired by the UI. Throwing's T14 telemetry (class at release vs outcome, `redAgeAtReleaseMs`) is Sim-side (story 016).

**Persistent state:** none written by the UI. A throw changes run state and wallet via the host only.

---

## Transitions & Animations

| Element | Default | Reduced Motion |
|---|---|---|
| Arc + label appear | 0.08 s alpha fade-in; geometry correct from frame 1 | 0.1 s opacity cut |
| Label follows marker | every frame, no easing (exact) | same |
| Label side change (sticky → new candidate) | 0.08 s cross-fade at the new position (no slide — sliding text is unreadable) | cut |
| Text gate | "…" for ≤ 100 ms, then the text cuts in (no fade, to avoid a smeared read) | same |
| Release / cancel | arc, marker, label fade out 0.08 s | 0.1 s cut |
| Reason line | cuts in; fades out 0.15 s at F1 expiry | cut |
| Catch prompt flip (M0) | cut (hysteresis already damps it) | cut |
| Carets / chevron (M0) | cut in (static; never pulse) | cut |
| Hotbar catch pulse (M0) | thickness 2 → 5 → 2 px over 0.3 s; count bounce 1.2× for 0.1 s | one thickness step for 0.3 s, no bounce |
| Toggle catch drain bar (M0) | width shrinks linearly over the 3.0 s arm | same (a static mapping, not decorative motion) |
| Hands-up pose (M0; Player Character animation, triggered here) | raise 0.1 s on the arm press, held for the whole armed window, drop 0.15 s at window end | same (a character pose, not screen motion) |

No element here flashes in the WCAG sense (all are small-area). Nothing in this flow moves the camera beyond Player Character's aim profile, and the optional screen shake is Throwing's (off under Reduced Motion).

---

## Input Method Completeness Checklist

Touch is not supported (v1 platform).

**Keyboard + mouse**
- [x] Aim / commit / cancel: RMB hold, release, Esc. Toggle mode supported (Input G7).
- [x] Every refusal has visible text (reason strip) — a press is never followed by nothing (Input G9).
- [x] Esc behaviour: Back step 3 cancels the aim, without pausing on the same press.
- [ ] One-handed / keyboard-only aiming (digital `look_*` keys) is Input & Controls' (R6 Look row); not verified for aim precision here — OQ-TC-5.

**Gamepad**
- [x] LT hold / release, B cancel, RS aim with `aimCamMult`; haptics per Game Feel (setting).
- [x] Catch on X (M0); the prompt names the verb before the press.
- [x] Controller disconnect mid-aim: Input R9 synthesizes the release-with-cancel → "Aim cancelled" (never a throw).

**Visible focus:** not applicable (no focusable controls; `FocusMode.None`). **Hit targets:** not applicable (no pointer targets).

---

## Accessibility

**Tier:** announcements (HUD H26; no `design/accessibility-requirements.md` yet — OQ-TC-6). Screen-reader *throwing* is out of scope for v1 (GDD UI Requirements); the live region serves low-vision and cognitive access.

- **Colour never alone (SC 1.4.1).** The verdict is carried five ways:
  - line pattern (solid / dashed / dotted);
  - marker silhouette;
  - chip glyph (✓ / ✗ / — / ≈ / > / clock);
  - label words ("wrong house");
  - the preview sound.

  Money carries its **sign**, not a colour.
- **Contrast (TC-U-3 b, normative):**
  - label text Chalk on Charcoal ≈ 13 : 1;
  - ×N Gold on Charcoal ≈ 9.6 : 1;
  - ✓ Charcoal on Go Green ≈ 6.4 : 1;
  - ✗ Chalk Bold ≥ 20 px on Stamp Red ≈ 4.4 : 1 (graphical object, SC 1.4.11 ≥ 3 : 1);
  - neutral glyphs Charcoal on Chalk ≈ 13 : 1;
  - plate edge #787D8C ≥ 3 : 1;
  - marker outline 2 px Charcoal ≥ 3 : 1 against the terrain backdrops (verified per story 018 / TC-U-3).
- **Text:** 18 × t px (reading-text floor), up to 200 %. Wraps, never truncates an address.
- **Timing (SC 2.2.1):** reason lines follow F1 × the duration setting (1×–3×) and are mirrored to history (M1) and the live region. The 100 ms text gate and `previewSettleMs` (0–250) are the only waits; `previewSettleMs` exists for players who misread flickering verdicts.
- **Motion:** Reduced Motion converts every fade / pulse to cuts or static steps (table above). Carets and chevrons never pulse.
- **Live region — verbosity setting "Throw announcements" (Off / Brief / Full), default Brief [D]** (answers GDD OQ13's Brief / Full). Polite, ≤ 1 aim line per second, built from label keys, emitted when `LiveRegionDue` (100 ms stable); a superseded aim line is replaced, never queued (HUD H26 state line). Reason and catch lines are outcome cues — never dropped. **Off silences aim and commit lines only**; reason and catch lines are still announced (HUD H26: outcome cues are not verbosity-gated).

| Moment | Off | Brief | Full |
|---|---|---|---|
| Aim (Accept) | — | "Hit: 17 Elm St" | "Aiming: 17 Elm St, 20 letters, plus 240 cents" |
| Aim (Misdeliver) | — | "Miss: 9 Jade Close" | "Aiming: wrong house, 9 Jade Close, minus 120 cents" |
| Aim (neutral) | — | "Ground" / "Closed" / "Water" / "Belt" / "Drops" / "Blocked" | "Aiming: ground, 20 letters" (etc.) |
| Commit | — | — | "Threw 20 to 17 Elm St" |
| Results (from domain events) | Mail's delivery lines (HUD) | Mail's delivery lines (HUD) | + "Landed on the ground" / "Landed in water, floats 10 seconds" / "Joined a belt" |
| Reason lines | the reason text | the reason text | the reason text |
| Catch (M0) | "Caught 20, slot 3"; "Fumbled"; "Jules caught it" | "Incoming from Jules"; "Caught 20, slot 3"; "Fumbled"; "Jules caught it" | "Incoming from Jules, 20 letters"; "Caught 20 letters, slot 3"; "Bag full. Fumbled 20 letters at your feet"; "Jules caught it" |

Spoken strings come from `spokenKey`s, e.g. "cents" rather than "¢" and "minus" rather than "−".

---

## Localization Considerations

| Element | EN longest | Budget | Risk |
|---|---|---|---|
| Line 1, Misdeliver | "1204 Wolverhampton Crescent — wrong house" (≈ 42 chars) | max width 672 px at t = 1 (≈ 60 chars), 504 at 4:3 | **HIGH** at 200 % / 4:3 / +40 % pseudo-loc: wraps to 2–3 lines; the plate grows; the placement algorithm uses the grown rect. Address never truncated. |
| Line 2, floor | "×20 · −60¢ to floor · 10 returned" | wraps at `·` | **HIGH** (German "zurückgegeben" etc.). Never split mid-clause. |
| Reject line 1 | "Closed — opens at Delivery" | wraps | MEDIUM — "Delivery" is the phase name (glossary term). |
| Reason lines | "Can't throw while swimming" | prompt slot 720 px | LOW–MEDIUM; HUD wraps. |
| Catch prompt | "[E] Catch ×20 from Jules" | prompt slot | MEDIUM; names ≤ 12 graphemes (HUD H9). |

- Numbers and money via the locale formatter.
- House numbers are data.
- Street names come from content (possibly long — the wrap rule covers them).
- Never concatenate: each line is one key with arguments.
- **Pseudo-loc +40 % at 1280×720 and 200 % is an acceptance gate (TC-U-1).**

---

## Acceptance Criteria

*Evidence: `production/qa/evidence/throwing/arc-label/` and `arc-label-evidence.md` (ux-designer sign-off).*

**Performance**
- [ ] **TC-UX-1** The label's first appearance is on the same rendered frame as the arc (≤ 2 rendered frames after the aim press). 600 frames of aiming with the class changing every 10 frames allocate **0 managed bytes** in the label path; label build + draw stays within the ribbon's 0.3 ms budget.

**Core purpose**
- [ ] **TC-UX-2 (TC-U-1, parametrised over every row of the label catalogue)** The plate shows exactly two logical lines built from the listed keys with arguments: Chalk text, Gold "×N", and no red or magenta text. The chip glyph matches the marker class on every frame.
- [ ] **TC-UX-3** FX-L20 values render as specified: "×20 · +240¢"; "×20 · −120¢"; "×20 · −60¢ to floor · 10 returned" (wallet −440); "×20 · refused at floor · all returned" (wallet −500).

**Placement / layout**
- [ ] **TC-UX-4** Sweeping the marker over a 9×5 grid of screen positions at 1920×1080 and 1280×720, t = 1.0 / 1.5 / 2.0, with pseudo-loc +40 %: the label never intersects the marker rect, never intersects the prompt rect plus one reserved reason line, always stays inside the 24 px inset, never exceeds 35 % of `Wref` in width, and never truncates an address. Default placement is up-right (16 px offset). The side does not change while the current side stays valid. Where every rule cannot hold at once, the observed violations follow the priority never-cover-`M` > inset > keep-out rect (keep-out = w 720, top y 636, h 80 × t).
- [ ] **TC-UX-5** With the marker projected off-screen, the label stays visible at the inset edge with a direction arrow pointing toward the marker.

**Timing**
- [ ] **TC-UX-6** On a class change at t0: the marker, pattern and chip change at t0; the text lines show "…" / "×N" until t0 + 100 ms and the new text at t0 + 100 ms (not at t0 + 99); the previous class's text is never visible after t0. During the 100 ms gate the chip already shows the new class glyph. With `previewSettleMs` = 250, the neutral pending marker shows from t0 to t0 + 250 with an empty chip, and the text stays "…" / "×N" until t0 + 250.

**Error states (TC-U-2 c, M-1a)**
- [ ] **TC-UX-7** Each reason row ("Select mail to throw" 3100, "Nothing to throw" 2700, "Can't throw while swimming" 3100, "Hold to aim" 2700, "Aim cancelled" 2500, "Couldn't throw" 2500 ms at 1×) shows dimmed with a lock icon in the prompt slot's reason strip for HUD F1's lifetime ± 1 frame. Holding: the Input / HUD "Put it away first [Esc]/[B]" prompt row is present exactly once and story 019 emits no reason line. A repeated press restarts the lifetime with a single line visible. Each line reaches the live region once, and nothing is sent to the host for any refusal.

**Accessibility**
- [ ] **TC-UX-8 (TC-U-3 b)** Measured label text contrast ≥ 4.5 : 1, ×N ≥ 4.5 : 1, ✗-on-red ≥ 3 : 1 at ≥ 20 px Bold, and plate edge and marker outline ≥ 3 : 1. Recorded in the evidence file.
- [ ] **TC-UX-9** With "Throw announcements" = Brief, aiming across three houses in 2 s produces ≤ 2 polite live-region lines (≤ 1 per second, superseded lines dropped). With Off, there are no aim or commit lines (reason and catch lines still arrive). With Full, the lines match the table above.
- [ ] **TC-UX-10** With Reduced Motion on, no label or reason element translates or fades longer than 0.1 s.

**M0 (catch slice; not gating story 019)**
- [ ] **TC-UX-11 (TC-U-2)** An inbound flight shows "Catch ×20 from Jules" before the press and "Catching…" for the whole arm window; a fumble shows "Bag full — fumbled" with no red or magenta; a clean catch pulses the hotbar slot and shows no toast; carets show only on the predicted catcher's screen.

---

## Open Questions

| # | Question | Owner | Resolve by |
|---|---|---|---|
| OQ-TC-1 | **Text gate shows "…" for ≤ 100 ms after a class change** instead of keeping the previous text (this spec's [D]). It honours CD D2's "never the previous verdict" for text as well as markers. Confirm it reads better than 100 ms of stale text in the first playtest; if the "…" blink annoys, the alternative is to keep the old text but dim it to 50 %. | creative-director / ux-designer | M-1a playtest |
| OQ-TC-2 | New setting **"Throw announcements" (Off / Brief / Full, default Brief)** — needs a row in the Profile & Settings schema and the Menus settings screen; answers GDD OQ13 (verbosity). | Profile & Settings / Menus & Run Flow | M1 settings screen |
| OQ-TC-3 | **Hand-backs to the Throwing GDD** (not edited here): (a) the seed's "flips below at screen edges" is superseded by this spec's 4-candidate sticky algorithm with the above-prompt fallback; (b) OQ13's off-screen placement is answered (clamp + arrow); (c) OQ13's intercept-cue visibility is answered as predicted-catcher-only (default yes); (d) the reason-line lifetime is HUD F1 everywhere (the GDD's text already says so; the seed's 1.5 s / 2 s values are void); (e) the label plate radius is 8 (not a full pill) to keep "pill = button" (art bible); (f) Force patterns scales the chip and glyph (42 / 30 × t) but not label text, which follows the text-scale setting — the GDD's Accessibility row says "label sizes". | Throwing & Catching GDD | next GDD revision |
| OQ-TC-4 | **Story 005 key set** names line-1 keys for ok / wrong / closed / ground / water / belt / timeout only. This spec adds `throw.label.locked`, `throw.label.wontfit` and `throw.label.blocked`, plus `throw.label.pending` ("…") and the line-2 keys `money.accept_dead` and `count`. Story 005's table-driven "every end class maps to a line-1 key" test must include them — **recorded in story 005 (addendum, 2026-10-16)**. | Throwing epic (story 005) | before story 019 implementation |
| OQ-TC-5 | Keyboard-only aim precision (digital look keys) is untested; may need a slower look rate while aiming. | Input & Controls / accessibility-specialist | M4 accessibility pass |
| OQ-TC-6 | No `design/accessibility-requirements.md` or `design/player-journey.md`; tier assumed = announcements (HUD H26). | ux-designer / producer | before `/gate-check production` |
| OQ-TC-7 | **HUD dependency:** the reason strip, the F1 lifetime and the protected-rect consumer are HUD-owned (ADR-0018 not written; no HUD epic). Story 019 implements a stub strip honouring F1 behind the same port; swap it for the HUD's when the HUD epic lands. | HUD / technical-director | ADR-0018 |
| OQ-TC-8 | Art-director: (i) chip colours (green chip with Charcoal ✓, red chip with Chalk ✗, Chalk chip for neutrals); (ii) the off-screen direction arrow on the chip; (iii) the plate edge #787D8C. Plus GDD OQ9's amendments (dotted white arc, disc variants, hat pins as screen-space UI, Gold ×N tag). | art-director | `/art-bible` revision |
| OQ-TC-9 | **Pattern library:** `design/ux/interaction-patterns.md` does not exist. This spec introduces *Marker-anchored label* (4-candidate sticky placement, prompt-rect avoidance, off-screen clamp + arrow); it reuses HUD H11 reason strip, H12 focus-bracket carets and the Comms edge arrow by reference. Add it when the library is authored (`/ux-design patterns`). | ux-designer | `/ux-design patterns` |

