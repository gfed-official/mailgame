# Throwing & Catching — parked seed material

> **UNREVIEWED SEED — not authoritative.** Moved verbatim out of `design/gdd/throwing-catching.md` (as reviewed 2026-10-16, receipt `646891c…`) by the creative-director's scope ruling (CD-SENIOR D6). Nothing here binds implementation. Each section names the artefact that must absorb it; that author re-decides everything. Where the Throwing GDD and this file disagree, the GDD wins.
>
> **Review corrections that apply to this material** (design-review 2026-10-16):
> - [CD D2] Colour, line pattern, marker shape and glyph are **exact every frame**. Only label text, the live-region line and the preview sound wait 100 ms. The accessibility setting `previewSettleMs` (0–250, default 0) shows a neutral pending marker (Charcoal-outlined ring, no glyph, no colour) during a change, never the previous verdict. The "Debounce" bullet below is superseded.
> - [CD D4] The third label line ("Heads up, Jules") is **dropped**: the hat pin carries the teammate's name, and the label is always two lines.
> - [ux-designer, CD] Floor strings: `×20 · −60¢ to floor · 10 returned` and `×20 · refused at floor · all returned`. Reject line 1 is word-first with no leading "—" (`Closed — opens at Delivery`). Water and Belt get their own marker silhouettes (wave-bottomed disc; chevron-notched disc); Ground, Reject, Timeout and Blocked keep disc + glyph (inner glyph ≥ 12 px at 1080p).
> - [ux-designer, CD] Prompt-slot reason lines last **2 s** (Manual Delivery `reasonShowTicks`, HUD), not 1.5 s; muted cues follow HUD H26. The "Throw pressed during the client cooldown" row is superseded: the client checks the cooldown at release (GDD T1).
> - [ux-designer] Screen-reader throwing is out of scope for v1; the live region serves low-vision and cognitive access. A directional edge chevron warns of an off-screen inbound flight (GDD T7).

## 1. → `/ux-design` arc label, prompt slot, catch feedback and live region (`design/ux/throwing-catching.md`)

*From ux-designer. Colour is always redundant (WCAG SC 1.4.1). Label text is Chalk White on Charcoal, never red or magenta (art bible §7). "×N" is Gold, the stack-count voice. Strings are localisation keys with arguments, never concatenated.*

**1. Arc label (thrower only; screen-space). This closes Input & Controls OQ-IC-3(c) together with Visual/Audio's patterns and markers.**

**Placement:**
- A Charcoal pill (#5A5E6B edge) 16 px up and right of the end marker. It never covers the marker, flips below it at screen edges, and stays inside a 24 px safe inset.
- Always two lines, so its height is stable: line 1 is glyph + place, line 2 is "×N · money".
- It uses the same client `Evaluate` and value hint as Manual Delivery's prompt.

| Class | Line 1 | Line 2 |
|---|---|---|
| Accept | `✓ 17 Elm St` | `×20 · +240¢` |
| Accept, all dead letters | `✓ 17 Elm St` | `×20 · +0¢ · dead letters` |
| Misdeliver | `✗ 9 Jade Close — wrong house` | `×20 · −120¢` |
| Misdeliver, capped by the floor | same | `×20 · −60¢ to floor · 10 bounce` |
| Misdeliver, wallet already at the floor | same | `×20 · refused at floor — bounces` |
| Reject, closed | `— Closed — opens at Delivery` | `×20` |
| Reject, locked district | `— Not on today's route` | `×20` |
| Reject, non-mail | `— Won't fit here` | `×1` |
| Ground | `Ground` | `×20` |
| Water | `≈ Water — floats 10 s` | `×20` |
| Belt | `> Joins belt` | `×20` |
| Timeout | `Drops (3 s)` | `×20` |
| Teammate pin on the arc | adds a third line: `Heads up, Jules` (no promise of a catch, and never "in the way": CD ruling) | |

**Behaviour:**
- **Money is shown [D].** It is the stake the player commits, which answers OQ21's failure mode without softening anything.
- **Debounce:** colour, marker and label change together after the new class or address has held 100 ms (3 ticks). The first appearance is immediate.
- **Text scale:** follows the setting up to 200 % (16 px Label at 100 %). Text wraps and is never truncated, addresses are never ellipsised, and the panel's maximum width is 35 % of the screen. The marker stays ≥ 24 px.
- **Greyscale pass** (with protan, deutan and tritan simulations):
  1. The markers are distinguishable by silhouette at 20 m on 1080p over a white wall, grass, road and every district backdrop.
  2. Label contrast ≥ 4.5:1, and markers ≥ 3:1 via their outline.
  3. Solid, dashed and dotted are distinguishable.
  4. A tester reading only the label can state hit, miss or ground.
  5. No label overlaps the marker at 100–200 % text scale.

**2. Disallowed-throw and cancel feedback.**
- These go in the prompt slot, the same slot as "Busy" and Player Character P11's unavailable prompts. The prompt is dimmed, with a lock icon plus the reason, never colour-only.
- They last 1.5 s. A repeated press restarts the timer rather than stacking. Each plays a soft deny tick and is mirrored to the live region.

| Case | Feedback |
|---|---|
| Swimming | "Can't throw while swimming" |
| Holding | "Put it away first [Esc]/[B]" (Input ruling 3) |
| Slot empty | "Nothing to throw" |
| Hands slot selected | "Select mail to throw" |
| One-commit rule | "Busy", then "Release to retry" if the button is still down (Input G9) |
| Early release | "Hold to aim · [Esc]/[B] cancels" for 1 s (Input) |
| G2 cancel | "Aim cancelled" for 1 s |
| Host rejection (`stale_entry`, `bad_direction`, cooldown) | "Couldn't throw" for 1 s; the ghost fades over 150 ms and the slot stays filled |
| Mounted, Panel open, knocked down | no message: Throw has another meaning or none there |
| Throw pressed during the client cooldown (≤ 5 ticks after a launch) | no aim, no message (the cooldown is shorter than any visible beat) |

**3. Catch UX.**

| Moment | What the player sees |
|---|---|
| Catchable | The Interact prompt flips to `[E] Catch ×20 from Jules` / `[X] …` (Input R4.3; hysteresis on after 3 ticks ≈ 100 ms, off after 5 ticks ≈ 167 ms). Static Chalk carets mark the inbound parcel (Visual/Audio), with a screen-edge chevron if it is off-screen. Nothing is shown for your own throws |
| Armed | The prompt reads `Catching…` with a mitt icon for the whole armed window (a ≤ 200 ms tap never blinks off); the carrier raises both hands. In Toggle mode (Input G7) a press latches the arm for `catchArmSeconds` (3.0 s) or until a second press, and a thin draining bar under the prompt shows the latch (never announced) |
| Caught | Feed: `Caught ×20 — slot 3`. The hotbar slot gets a 0.3 s thickness ring pulse and the count bounces. Selection moves only if it was the hands slot or mail (T7, OQ24) |
| Fumble | `Bag full — fumbled` (catcher only), neutral styling |
| Race lost | `Jules caught it` to the loser; the thrower gets `Jules caught ×20` |
| Swimmer presses | Dimmed, locked `Dry land required` |

**4. Announcements (live region).** Polite priority, ≤ 1 per second, and a superseded line is dropped. The tier is undecided (Grid Inventory OQ19).

| Moment | Line |
|---|---|
| Aim (after the label has held 400 ms) | "Aiming: 17 Elm St, 20 letters, plus 240 cents" / "Aiming: wrong house, 9 Jade Close, minus 120 cents" / "Aiming: ground" |
| Commit | "Threw 20 to 17 Elm St" |
| Results (from domain events) | "Delivered 20 to 17 Elm St, plus 240 cents"; "Wrong house: 9 Jade Close, minus 120 cents"; "Closed. Opens at Delivery"; "Landed on the ground"; "Landed in water, floats 10 seconds" |
| Catching | "Incoming from Jules"; "Catching"; "Caught 20 letters, slot 3"; "Bag full. Fumbled 20 letters at your feet"; "Jules caught it" |
| Feedback | every disallowed string from §2 |

## 2. → art bible §5 / `/asset-spec system:throwing-catching` (outcome beat timings)

### Outcome beats owned here

Grammar: squash 0.2 s → pop ≤ 0.1 s → hold ≥ 0.15 s. Squash stays within 0.8–1.2 and limbs stretch ≤ 1.3× (art bible §5). One-shots keep these timings and are never time-scaled (Player Character).

| Beat | Visual / animation | Timing |
|---|---|---|
| Aim hold (wind-up) | Upper-body layer while aiming: lean back 20°, arm cocked, strain face, hat flattens; legs keep walking | for the hold (≥ 0.25 s) |
| Release | Arm snaps to full extension, rear foot off the ground, Chalk speed-puff at the hand. The parcel leaves the hand socket on the snap frame and is never delayed for the animation | snap ≤ 0.1 s; follow-through 0.15 s |
| Aim cancel | Arm drops, face returns to neutral | 0.1 s |
| Catch-armed pose | Both arms up, palms out; plays on the press for the whole armed window, then drops | raise 0.1 s, drop 0.15 s |
| Incoming catchable (catcher's screen only) | Four static Chalk White corner carets on the inbound parcel; a screen-edge chevron if off-screen; never green (it does not pre-announce) | from the F5 prompt |
| Catch | Parcel squashes into the hands (0.8), body dips, grin, Chalk dust ring; the parcel shrinks into the satchel and the ×N tag flies to the hotbar. The **squash plays only on the host's confirmation** | squash 0.2 s, hold 0.15 s |
| Relay hop | Chalk-on-Charcoal **FORWARDED** stamp thumps onto the parcel at the catch | 0.8 s |
| Race lost | The loser's hands clap on empty air, squash 0.9, small shrug, no red, no face change | 0.25 s |
| Fumble | Parcel bounces off the chest and drops at their feet, squash 0.85, hands flap; neutral face, no red | 0.35 s |
| Bonk | Parcel bounces off the hat (cosmetic); the hat lifts 15 cm (never the knockdown fly-off), shock face 0.3 s, spring settle; the entry drops at their feet | lift ≤ 0.1 s, settle 0.25 s; per-victim 1.0 s cosmetic cooldown |
| Goon hit | Paper-puff burst (Chalk confetti, ≤ 8 pieces, 3 sizes for 5/15/40), goon flinch squash 0.9 with a 50 ms hit-stop **on the goon's rig only**; the item drops at its feet | 0.3 s |
| Goon floored by a throw | Small **SIGNED FOR** stamp (Chalk on Charcoal, never red or magenta), once per goon, ≤ 3 on screen | 0.8 s |
| Ground | Squash 0.8 → 1.0 with a dust ring scaled by tier; the item rests glyph-up | snap ≤ 0.1 s, hold 0.15 s |
| Water | Splash ring plus the bible's foam ring; bobs 3 cm at 1 Hz for 10 s, then sinks with three bubbles | 10 s |
| Belt join | Squash 0.9, then it adopts the belt's motion (blend ≤ 100 ms into the lane position); no extra VFX | 0.15 s |
| Reject bounce at a closed or locked box | Mail draws the box's 0.8 squash; this system adds a cosmetic hop (≤ 0.6 m) back along the reflection, ending at the F8 drop point | 0.4 s |
| Timeout | Spin stops; the item drops straight down and lands as Ground | at 3 s |
