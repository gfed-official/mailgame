# UX Spec: Inventory Panel (M-1a — hotbar, own panel, one external grid)

> **Status**: Draft complete — awaiting `/ux-review` (autonomous run; decisions in `production/session-logs/decision-log.md`)
> **Cited by**: grid-inventory stories 023 (view, hotbar) and 024 (interactions, M-1a feedback); resolves Grid Inventory OQ14 (D7 panel dimensions) and decides OQ8 (pending art-director sign-off)
> **Sources**: `design/gdd/grid-inventory.md` (UI Requirements, D7, R7–R11), `design/gdd/input-controls.md` (R2–R8, F5, F7), `design/gdd/hud-notifications.md` (H4–H6, H10–H12, H20–H28), `design/art/art-bible.md` §4/§7, ADR-0012, ADR-0016, ADR-0019
> **Cross-reference check (2026-10-16)**: GDD M-1a UI requirements 13/13 control rows + all M-1a feedback rows covered; new patterns for a future `design/ux/interaction-patterns.md` (none exists): Panel frame, Single-draw grid, Focus readout, Origin marker; navigation matches Input & Controls R2/R5; accessibility tier assumed (OQ-IP-1); every data-dependent element has an empty state
> **Author**: user + ux-designer (autonomous run, `/ux-design inventory-panel`)
> **Last Updated**: 2026-10-16
> **Journey Phase(s)**: unknown — no `design/player-journey.md`
> **Platform Target**: PC (Windows / Linux); keyboard + mouse primary, gamepad full; no touch
> **Template**: UX Spec

---

## Purpose & Player Need

**The player opens this screen to get mail into their hands in the order they will deliver it — then gets out of it as fast as possible.** It is the carrier's bag on the screen: the hotbar (what you deliver or throw next), the own panel (hotbar row, inventory, backpack) and, at most, one external container beside it. At M-1a that external container is the Post Office **Intake**, shown as a plain grid.

The player arrives wanting to:
1. **Take mail** out of the Intake (Shift-click / X quick-move, or pick and place).
2. **Stage the next stop** — put a stack on the hotbar so delivery stance and Throw use it.
3. **Make room** — move, rotate and drop entries until a new piece fits.
4. **Read the bag** — what is in it, how full it is, and which slot is selected.

If this screen were slow or ambiguous, the "Case Your Route" fantasy (Grid Inventory, Player Fantasy) fails. A piece that silently does not move, a press that seems to do nothing, or a bag that cannot be read at a glance turns the most-touched surface after walking into a chore. The GDD's fantasy test 2 adds a hard constraint: a 10-address route needs **no** panel work beyond one `open` and one take. The panel exists to make the optional work (staging, packing, throwing prep) quick; it must never become a mandatory stop.

**Scope of this spec.** These are M-1a deliverables, cited by stories grid-inventory/023 and 024:
- the HUD hotbar strip (panel closed);
- the own panel;
- one external container rendered as a grid (the Intake, interim until the M1 manifest);
- pick / place / drag / rotate / return / drop / quick-move;
- the M-1a feedback rows;
- the leash fade and auto-close;
- the measured panel dimensions that make Grid Inventory D7 testable (OQ14).

Contention cues (M0), the manifest view, Sort, Split, Take all and the action menu (M1) are **forward-reserved in the layout** so that adding them later does not change `panelInnerWidthPx` / `panelInnerHeightPx`. They are not specified as behaviour here.

---

## Player Context on Arrival

- **When first seen.** The first minute of the M-1a greybox. The player walks to the Intake console, taps Interact and sees the panel for the first time with ten loose letters in a 20×16 grid. Tab can open the own panel earlier, but the solo loop first needs it at the Intake.
- **What they were just doing.** Walking (movement stays live while the panel is open — walk speed, no sprint, no look). Usually they arrive from the street, either to restock or because a pickup bounced with "Make space".
- **Emotional state.** *Brisk and task-focused.* This is real-time co-op: the shift clock runs, nothing pauses (multiplayer) and raids may come later. Design assumption: the player wants out within about **5 seconds** and reads the panel peripherally while walking. That is why:
  - nothing in the panel animates in a way that gates input (Game Feel: "animation never gates input");
  - focus lands where the player most likely wants it;
  - every press gives visible feedback on the next rendered frame.
- **Voluntary vs sent.** Always voluntary (Tab / Y, or tap-Interact on a container). The game never opens the panel by itself. It can **close** the external panel by itself (leash > 3.0 m, container gone) and says so ("Out of reach").
- **Prior knowledge.** None assumed. There is no tutorial. The readout strip's context legend teaches the three verbs live in the current context (see Component Inventory), and HUD tips arrive at M4.

**Alternative considered (thorough).** A paused "inventory screen" in the RE4-attaché style was rejected: the GDD rejects paused arranging ("this is real-time co-op and the panel must work in seconds"), and pausing is impossible in multiplayer (Input & Controls R2 rule 1). The panel is therefore a non-modal overlay over a live world.

---

## Navigation Position

This screen lives at: **Run (in-shift gameplay) → World → Panel overlay** (Input & Controls `overlay = Panel`). It is context-dependent, not a top-level destination:
- reachable only while `body = OnFoot` with no capture (Pause, TextEntry) active;
- not reachable while Mounted, in Build or Dead (Input & Controls R2 rule 5) — the refusal messages are owned there and land at M1/M2.

The **hotbar strip** is the panel's always-on remnant in World and Holding-closed. It is a HUD widget (HUD H10 owns its frame), specified here because its content and selection rules are the own panel's.

Alternate entry paths into the same overlay:
- **Tab / Y** → own panel only;
- **tap-Interact on a container console** → own + external.

Opening a second external closes the first (R10). Pause (Esc at the end of the Back ladder, P, Menu) layers **over** the panel. In multiplayer the panel stays live underneath; in solo the Sim pauses.

---

## Entry & Exit Points

| Entry Source | Trigger | Player carries this context |
|---|---|---|
| World (on foot) | **Tab** (KBM) / **Y** (pad) | Own panel only. Focus lands on the **selected hotbar slot**. Cursor empty. |
| Holding-closed (carrying a cursor entry with the panel closed) | Tab / Y | Own panel opens in **Holding-open**. The held entry follows the focus, which lands on the entry's **origin cells** if they are in the own panel, else on the selected hotbar slot. |
| World, facing a container console ≤ 2.5 m | **tap Interact** (E / X) — `open` op (range-gated, Player Character F5) | Own + external panel. Focus lands on the **external panel's first occupied entry** in row-major order (or cell (0,0) if empty), because the player came to take something. The external panel is "subscribed" from the open reply. |
| Panel already open (own only) | tap Interact is **not live** in Panel (Grid Inventory UI Req) | Opening an external requires closing the panel first. **Recommended M-1a flow:** walk to the console with the panel closed. |
| Panel open on external A, Interact on external B | not live (as above) | — |

| Exit Destination | Trigger | Notes |
|---|---|---|
| World | **Tab** (KBM toggles); **B / Esc** = Back ladder step 5 when no submenu, aim, toggle-hold or held entry is pending | Closes own + external together. The cursor entry is **kept** (Holding survives a manual close — R11). The HUD chip appears beside the hotbar. |
| Holding-open → Panel | Back step 4 (Esc / B / **RMB with the panel open**) = `return` | The entry goes back to its origin cells, else AutoPlace into the panel, else stays held with "No room to put it away — drop it or make space · [Esc] again to skip" (KBM) / "· [Menu] Pause" (pad). A second Back while that message shows skips to closing the panel, entry still held (F13). |
| World (external closed by the game) | Leash: claimed position > 3.0 m from the console anchor; container destroyed / expired; death | External slides out, toast **"Out of reach"**. A cursor entry is **put away** into the panel on a leash close (R10). The own panel stays open (the leash closes only the external). **Decision:** the own panel is not auto-closed, because the player may be mid-arrangement. |
| Pause overlay | Esc at Back step 6 (only when nothing else is pending), **P**, pad **Menu** | Panel remains underneath. Returning from Pause restores the same focus. |
| World (the player is knocked down — M3) | knockdown | Panel and cursor close; contents spill (R13). Out of M-1a scope; listed for completeness. |

**One-way exits:** none. Every exit can be re-entered with Tab / Interact. Note that a leash auto-close puts the held entry away; this is not undone on reopening.

---

## Layout Specification

**Units.** Every size is in **1080-reference px**: ADR-0019 §7 sets stretch `canvas_items` / aspect `keep_height`, so the reference height is always 1080. Reference width `Wref` = 1080 × window aspect: 1920 at 16:9, 1728 at 16:10, 1440 at 4:3. On ultrawide the HUD frame is a centred 16:9 box (HUD UI Requirements), so `Wref` caps at 1920 and x is measured from that box's left edge.

**`s` (panel scale)** = `min(textScale, 1.50)`, where `textScale` is the game-owned text-scale setting (100–150 % for the HUD; layout-bound panels cap at 150 % — Input & Controls UI Requirements, HN-14). `s` multiplies cell pitch and every font in the panel. Reading text outside the panel (toasts, prompt) may go to 200 % under HUD H23. The window's own content scale (height / 1080) is applied by the stretch mode and is **never** multiplied in again.

**Cell pitch `p`** = `round(56 × s)` → **56 / 70 / 84** at 100 / 125 / 150 %. Grid Inventory D7's `cellPx × textScale` is exactly `p`. A W×H grid occupies **W·p × H·p**. Each cell face is `p − 2`, with the 2 px Charcoal gutter split 1 px per side.

### ASCII Wireframe

Panel open, own + external (Intake, M-1a interim grid), 1920×1080 reference, s = 1.0. The coordinates are normative (see Layout Zones).

```
x: 48                     520 544                                                           1872
y:┌──────────────────────────────────────────────────────────────────────────────────────────────┐
  │ Shift 1 / 1 · DELIVERY 02:14             [compass]                       quota · wallet ¢   │ top bar (HUD, never gives way)
  │                                    (banner zone y 80–150, transient)          pills         │
151 ┌─ Your bag ─────── 9 / 32 ┐  ┌─ Intake ──────────[M1: Sort·Take all][M0: chips]── 212 / 320 ┐
  │ │ 1   2   3   4   5   6  … │  │ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░ fill bar │ header 56
207 │ ┌──┬──┬──┬──┬──┬──┬──┬──┐ │  │ ┌──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┐▲│
  │ │ │✋│▣ │▣ │  │  │  │  │  │ │  │ │✉ │✉ │✉ │✉ │✉ │✉ │✉ │✉ │✉ │✉ │  │  │  │  │  │  │  │  │  │  │█│
  │ │ └──┴══┴──┴──┴──┴──┴──┴──┘ │  │ ├──┼──┼──┼──┼──┼──┼──┼──┼──┼──┼──┼──┼──┼──┼──┼──┼──┼──┼──┼──┤█│ grid viewport
  │ │  hotbar (slots; 1 = hands)│  │ │  │  │  │  │  │  │  │  │  │  │  │  │  │  │  │  │  │  │  │  │ │ inner 1304 × 576
  │ │ ┌──┬──┬──┬──┬──┬──┬──┬──┐ │  │ ⋮   (20 × 16 at p = 56 → 1120 × 896: scrolls vertically   ⋮ │ │ (Intake shows rows
  │ │ │╔═══╗│✉ │✉ │  │  │  │  │ │  │      at M-1a — interim until the M1 manifest)                │ │  1–10 of 16)
  │ │ │║ ▢ ║├──┼──┼──┼──┼──┼──┤ │  │                                                               │▼│
  │ │ │╚═══╝│  │  │  │  │  │  │ │  │                                                               │ │
  │ │ └──┴──┴──┴──┴──┴──┴──┴──┘ │  │                                                               │ │
  │ │  inventory 8×2            │  │                                                               │ │
  │ │ ┌──┬──┬──┬──┬──┬──┬──┬──┐ │  │                                                               │ │
  │ │ │  │  │  │  │  │  │  │  │ │  │                                                               │ │
  │ │ └──┴──┴──┴──┴──┴──┴──┴──┘ │  │                                                               │ │
  │ │  backpack 8×2 (if worn)   │  │                                                               │ │
791 │ (own frame shrink-wraps)  │  └───────────────────────────────────────────────────────────────┘ │
799 │ Letter ×12 · 17 Elm St · District 3 ■   [A] Pick up · [X] Quick-move · [B] Close     Rows 1–10/16 │ readout strip 40
839 └──────────────────────────────────────────────────────────────────────────────────────────────────┘
847                       ┌──────── 🔒 Close panel to deliver ────────┐                                 prompt (HUD, footer)
                          └───────────────────────────────────────────┘
          (chat, bottom-left)        ┌──── toast lane: "No room there" ────┐          (feed, bottom-right; ≤ 3 lines)
                                     └──────────────────────────────────────┘    [hotbar strip hidden while panel open]
```

Panel closed (World / Holding-closed), bottom-right corner:

```
                                                         ┌held┐  ┌─1──2──3──4──5──6──7──8─┐  y 976
                                                         │✉12 │  │✋ ▣  ✉  ·  ·  ·  ·  ·  │  (hotbar strip, 464 × 72 at s = 1)
                                                         └────┘  └──▔▔───────────────────┘  y 1048 (bottom safe edge)
                                                     (chip only while Holding-closed)  x 1408 … 1872
```

### Layout Zones

**Zone geometry (normative).** The panel overlay occupies the HUD central band (HUD H11 / UI Requirements, y 14–67 %) and extends it downward to the panel's footer. That extension is recorded as hand-back **HB-1** under Open Questions. The reason: the band alone (151–724 = 573 px) cannot hold a header plus the 560 px that an 8×10 truck needs at 100 %, and the GDD requires the truck to stay a grid at 100 % (D7 note, `manifestCellThreshold` safe range).

| Zone | Rect (s = 1.0, `Wref` = 1920) | Rule |
|---|---|---|
| **Own frame** | x 48, y 151; width `8p + 24` (**472** / 584 / 696); height shrink-wraps to content (max 688) | Left-anchored at the safe edge. Contains: header 56 · gap 8 · own grid viewport · gap 8 · readout 40. |
| **External frame** | x `48 + ownW + 24` (**544** / 656 / 768); y 151; width shrink-wraps to content, min 360, max `Wref − 48 − x` (**1328** / 1216 / 1104); height shrink-wraps (max 688) | Right of the own frame. Same vertical structure. |
| **Header** (each frame) | top 56 px of the frame | Title · reserved controls (M1) · reserved chips (M0) · fill readout; fill bar along its bottom (6 px). |
| **Grid viewport** (each frame) | from y 215; inner padding 12 left/right | **Inner width** = frame width − 24; **inner height** = min(content height, 576). Scrollbars live in the padding (right: 6 px thumb in the 12 px pad; bottom: 6 px thumb in the 8 px gap). |
| **Readout strip** | 40 px under the viewport (bottom of each frame); at max height y 799–839 | Focused-cell description (left) and context legend (right). The own and external strips are separate. Each shows only while its panel holds the focus; otherwise it shows the panel's fill text. |
| **Panel footer** (HUD prompt) | prompt top edge at **y 847**, centred, max 720 wide | Fixed while any panel is open, whatever the frame heights, so the prompt never jumps when the external opens. HUD draws "Close panel to deliver" here (H11). |
| **Toast lane** | prompt bottom + 16, centred, 420 wide | HUD H11 anchor. M-1a: one toast. |
| **Hotbar strip** (panel closed) | right-aligned at `Wref − 48`, bottom at 1048; plate `8p + 16` × `p + 16` (**464 × 72**) | HUD widget (H10). **Hidden while the panel is open**: the own panel's hotbar row is the live hotbar then. |
| **Held-entry chip** (Holding-closed) | 12 px left of the hotbar strip, `p + 16` square | HUD widget (H10); content specified under Component Inventory. |

**OQ14 resolved — the D7 panel dimensions.** D7's `panelInnerWidthPx` / `panelInnerHeightPx` are the **external grid viewport's maximum inner size**, measured as the viewport `Control.Size` (in 1080-reference px under `canvas_items` / `keep_height`) when laid out at its maximum frame size, re-measured on every window resize and `textScale` change. D7 is evaluated for the **external** container only. Own containers (hotbar, inventory, backpack) are always grid; if the own content exceeds the 576 px viewport, it scrolls.

```
panelInnerHeightPx = 576                                   (all aspects, all s — keep_height fixes the reference height)
panelInnerWidthPx  = Wref − 144 − ownFrameW(s)             ownFrameW(s) = 8·round(56·s) + 24
                   = Wref − 168 − 8·round(56·s)
```

| `Wref` (aspect) | s = 1.00 | s = 1.25 | s = 1.50 |
|---|---|---|---|
| 1920 (16:9, incl. 1280×720 and ultrawide) | **1304 × 576** | **1192 × 576** | **1080 × 576** |
| 1728 (16:10) | 1112 × 576 | 1000 × 576 | 888 × 576 |
| 1440 (4:3) | 824 × 576 | 712 × 576 | 600 × 576 |

The derivation of the width: `Wref` − left safe 48 − own frame − gap 24 − right safe 48 − external padding 24.

**D7 outcomes at 1920×1080** (`manifestCellThreshold` 160). These are the H-R18-2b screens and their neighbours:

| Container (W×H) | 100 % | 125 % | 150 % |
|---|---|---|---|
| chest 8×4 (448×224 → 672×336) | grid | grid | grid |
| chest 8×6 (Bulk Depot) | grid | grid | grid (504 ≤ 576) |
| bike 8×2 / 8×3, sorter buffer 4×2 | grid | grid | grid |
| mail truck 8×10 (560 tall at 100 %) | **grid** (560 ≤ 576) | manifest (700 > 576) | manifest (840) |
| depot 16×10 (896×560) | **grid** | manifest (height 700) | **manifest** (width 1344 > 1080) |
| depot 16×15 (240 cells), PO Depot, Intake (320) | **manifest** (cells) | manifest | manifest |

The 125 % column flips the truck and the 16-wide depot by the **height** clause. Grid Inventory R18's example ("the 16-wide depot flips at ≥ 150 %") is therefore ≥ 125 % with real dimensions. H-R18-2a's stubbed 1.25 → grid case is unaffected (it uses stub values); hand-back **HB-2**.

**M-1a interim — Intake as a grid.** D7 is **not evaluated at M-1a**. The manifest view is M1, and story grid-inventory/011 makes the Intake the M-1a external container. At M-1a every external container renders as a grid. The Intake (20×16 = 1120 × 896 at s = 1) fits the width and **scrolls vertically**: about 10 rows are visible, and the viewport scrolls in whole rows. At s ≥ 1.25 it also scrolls horizontally (1400 > 1192). Scrolling is a draw-time offset inside the single custom-drawn grid Control (no per-cell nodes, ADR-0019 §6). From M1, D7 decides and the Intake is a manifest.

**Arrangement alternatives considered.** Recommended option chosen, decision logged:
- **(A) Side-by-side, own left, external right, top-anchored at the band.** Chosen. It matches the GDD layout ("own panel left … external right"), keeps chat (bottom-left) and feed (bottom-right) clear, and gives the external the widest possible viewport, which keeps the most containers in grid view.
- **(B) Own panel docked bottom-centre above a stretched hotbar, external top-centre.** Rejected: it competes with the prompt and toast lane (centre column) and halves the external's height.
- **(C) Single tabbed panel (own / external tabs).** Rejected: quick-move between two visible grids is the core verb, and tabs hide one side.

### Component Inventory

| # | Zone | Component | Type | Content | Interactive | Pattern |
|---|---|---|---|---|---|---|
| C1 | Own / external frame | **Panel frame** | container plate | Charcoal #23252B plate, r = 12, 2 px outline **#787D8C** (≥ 3 : 1 on Charcoal; the art bible's #5A5E6B fails SC 1.4.11 per the HUD seed correction) | no | new: *Panel frame* |
| C2 | Header | **Title** | label | Own: "Your bag". External: the container's display name ("Intake"). Body 20 Bold × s, Chalk White | no | — |
| C3 | Header | **Fill readout + bar** | stat display | "9 / 32" (own: occupied cells of inventory + backpack; the hotbar is excluded) / "212 / 320" (external: occupied cells / cells), Bold 18 × s, tabular. 6 px bar: Chalk fill on #2E3138 track. At 100 %: **hatched** bar + "FULL" (never colour alone). The scroll position ("Rows 1–10 / 16") is not shown here; it goes in the readout strip | no | GDD Layout |
| C4 | Header (reserved) | **Header controls slot** | button row | **M1:** "Sort: street", "Sort: size", "Take all" pills. **M0:** who's-inside chips (hat + name, ≤ 12 graphemes). Overflow: chips collapse to "+N", buttons collapse to "⋯" (opens the action menu). **Empty at M-1a** — the header height stays 56 so D7's dimensions never change | M1 | reserved |
| C5 | Grid viewport | **Grid control** (one per panel) | custom-drawn `Control` (`_Draw`, `FocusMode.None`) | Every entry at `(x,y,rotated)`; empty cells; hotbar row (own); section dividers (own); focus ring; held ghost; feedback overlays; scroll offset | yes | new: *Single-draw grid* (ADR-0019 §6) |
| C6 | Own grid | **Hotbar row** | slots row (8×1) | 8 slots at pitch `p`; slot 1 = hands (see Cell anatomy); key numerals 1–8 in a 16 px band above the row (Micro 14, decoration — the readout carries the slot name) | yes (2–8) | — |
| C7 | Own grid | **Section dividers** | spacer | 12 px gap between hotbar / inventory / backpack, with a 2 px #787D8C rule centred in the gap and inset 12 px. The backpack section exists only while a backpack is equipped (R1); otherwise the frame shrinks | no | — |
| C8 | Readout strip | **Focus readout** | label, single line | The focused cell in words (see Information Hierarchy), Body 18 Medium × s, Chalk. Truncates with "…" at the legend's left edge. Rebuilt only on focus or mirror-version change (cached per entry) | no | new: *Focus readout* |
| C9 | Readout strip | **Context legend** | glyph hints | ≤ 3 hints for the current context, from live bindings (Input & Controls prompt grammar): Panel `[Pick] Pick up · [QuickMove] Quick-move · [Back] Close`; Holding-open `[Place] Place · [Rotate] Rotate · [Back] Put back`. Drops hints right-to-left when space runs out; glyph + text label always | no | Input & Controls footer legend (adapted: always on at M-1a, since there are no tips until M4) |
| C10 | Panel | **Scrollbars** | indicator (+ mouse drag) | 6 px Chalk thumb on a #2E3138 track; shown only when content exceeds the viewport | mouse only | — |
| C11 | Panel | **Live region** | invisible `Control`, `AccessibilityLive = Polite` (`AccessibilityServer.AccessibilityLiveMode`) | Every feedback row's text (same string as the toast) | no | GDD UI Req / ADR-0019 §6 |
| C12 | HUD (closed) | **Hotbar strip** | slots row | Same slot rendering as C6, plate Charcoal r = 12; key numerals above each slot | no (select by keys / D-pad / wheel) | HUD H10 |
| C13 | HUD (closed) | **Held-entry chip** | stat display | Swatch-with-glyph tab **with the district numeral**, street stripe, Bold house number, Gold count on a Charcoal chip, skull if Cursed (HUD H10). Non-mail: item icon + count | no | HUD H10 |
| C14 | HUD | **Toast** | notification | Feedback-row text (see Interaction Map); HUD pipeline (H6: 1 slot at M-1a) | no | HUD H4/H6 |

**Cell anatomy (C5/C6/C12).** Applies to every grid and slot. Art bible §7, with values fixed here:
- **Well and empty cell.** The well is #2E3138 with 2 px Charcoal gutters. An **empty cell** is a #2E3138 face (r = 6) with a 2 px #787D8C outline (≥ 3 : 1 against the well; it marks a drop target). Empty cells carry no text.
- **Mail entry (any footprint).** One rounded rect (r = 6) spanning the whole footprint — no internal gutters — in **Parcel Kraft #C89A5E**, with a 3 px Charcoal outline. On top of it:
  - **top-left:** the **swatch-with-glyph tab**, 18 × s px: the district colour with its shape glyph (circle / triangle / square / cross / diamond) in Charcoal;
  - **top-right:** a **house-number chip**, Bold 16 × s, Charcoal on Chalk White, tabular;
  - **centre:** the **mail-kind silhouette** in Charcoal (Letter = flat rect, Parcel = cube, Large = tall box), ≤ 50 % of the face;
  - **bottom edge:** the **street stripe**, 6 × s px, patterned (solid / stripes / dots / chevrons) in the stripe hue;
  - **bottom-right:** the **count**, Label 16 × s Payday Gold on a Charcoal chip, shown when `count ≥ 2`. Gold on Kraft would be ≈ 1.6 : 1, hence the chip.
  - **Cursed:** a skull stamp (20 × s) overlaps the silhouette's upper-left, plus a 2 px #B5E61D inner border (M1+ content; specified now so the cell layout holds).
- **Item entry (tool, material).** A #2E3138 face lightened to **#3A3F4A**, with a 3 px #787D8C outline. The item icon (from `ViewCatalog` by def id) is centred in Chalk White; the count is as above.
- **Rotated entries.** The footprint is rotated (`h×w`). The silhouette rotates 90°; **text never rotates** (art bible §7: address text never moves).
- **Hotbar slot 1 (hands).** A hand glyph in Chalk at 60 % size, with a 45° hatch (2 px #787D8C lines, 8 px apart) behind it. There is never a count or tab. It reads as "not a storage slot" without colour.
- **Selected hotbar slot.** The slot face is raised 4 px, with a **3 px Chalk White bar under the slot** (a "tab" shape) and its numeral in Bold. This is thickness and shape only — deliberately distinct from the focus ring, so "selected" and "focused" never look alike when both apply to one slot.
- **Focus ring (OQ8 — decided: draw on top).** A 4 px Chalk White ring **outside** the cell's 3 px outline, plus a 10 × s px corner caret at the top-left, plus a **1 px Charcoal keyline outside the ring** so the ring keeps ≥ 3 : 1 against light neighbours (Butter, Chalk chips). It is drawn **after** all cells, overlapping the 2 px gutter and 2–3 px of the neighbour's outline. Gutters stay 2 px, because widening them would shrink every face or break `p`, which D7 depends on. The ring scales with the cell. Hover (pointer) = ring alone; a held entry = ring around the **ghost footprint**.
- **Held ghost (Holding-open).** The held entry is drawn at the focus anchor with its footprint, at **scale 1.08 plus a 4 px Charcoal drop shadow offset (+3, +4)** — never opacity (60 % opacity is reserved for pending, GDD [D]). Validity preview, display-only and computed locally from the mirror:
  - fits → a 2 px Chalk outline;
  - would be `out_of_bounds` / overlap → a **dashed Stamp Red outline (6 on / 4 off, 3 px) with a Chalk X on a Stamp Red 40 % fill** (build-ghost grammar).

  The press is still sent, and the server's reply decides (Network rule 1: the UI never un-sends on a local guess).
- **Origin marker (Holding).** The held entry's origin cells show four 2 px L-shaped corner ticks in #787D8C, so the player can see where Back will put it. This is a new grammar; art-director to confirm (OQ-IP-3).
- **Outlines are filled rects / StyleBox shapes ≥ 2 px, never `DrawLine`** (Godot 4.7 removed line antialias feathering — ADR-0019, HUD seed §5). Dashes are filled rect segments; the X is two filled quads (`DrawPrimitive`).

### Information Hierarchy

Ranked by what the player needs first. This drives contrast, size and position.
1. **Where my focus is and what I'm holding.** Focus ring and held ghost (thickest strokes on screen; the ghost is the only lifted object).
2. **What each entry is for: address and district.** The district glyph tab and house number in each mail cell. The colour gradient is a bonus — shape and number carry it (Pillar: "Case Your Route", read the route off the panel).
3. **How many.** The Gold count chip.
4. **Whether it fits / why it didn't.** The validity ghost while holding; the toast and live-region text after a rejection.
5. **Which slot is selected.** The selected-slot bar (it drives delivery stance and Throw).
6. **How full.** The header fill readout and bar.
7. **What I can press.** The context legend (discoverable, low emphasis).
8. **The words for the focused cell.** The readout strip ("Letter ×12 · 17 Elm St · District 3 (Square) · Bag row 1"). Secondary for sighted players, and the full non-visual equivalent of the cell.

Readout grammar (left to right, `·`-separated, omitting empty parts):
- **mail:** `[Kind] ×[count] · [house] [street] · District [n] ([glyph name]) · [section] [position]`;
- **item:** `[Item name] ×[count] · [section] [position]`;
- **empty:** `Empty · [section] row [r], column [c]`;
- **slot 1:** `Hands — slot 1 can't hold items`;
- **Holding:** prefix `Holding [Kind] ×[n] → `.

Position wording: own sections read "Hotbar slot 3", "Bag row 2, column 5", "Backpack row 1, column 1"; external reads "Intake row 7, column 12". Rows and columns are 1-based for players.

---

## States & Variants

| State / Variant | Trigger | What changes |
|---|---|---|
| **Closed** (World) | default | Hotbar strip visible bottom-right (selected-slot bar); no panel. |
| **Closed, Holding** (Holding-closed) | cursor non-empty, panel closed | Hotbar strip plus the **held-entry chip** on its left. Tool / Throw presses show "Put it away first" in the prompt slot (HUD). |
| **Own panel open** (Panel) | Tab / Y | Own frame slides in from the left. Focus on the selected hotbar slot. Hotbar strip hidden. HUD prompt moves to the footer ("Close panel to deliver" when facing a mailbox). Movement continues at walk speed. |
| **Own + external open** | tap-Interact on a container | Both frames. Focus on the external's first occupied entry (else cell (0,0)). |
| **Holding-open** | a `pick` confirmed | Held ghost at the focus anchor (scale + shadow), origin corner ticks, validity preview, legend switches to Place / Rotate / Put back. |
| **Empty container** | no entries | External: every cell empty; readout "Empty · Intake row 1, column 1"; header "0 / 320". Own: hotbar shows hands + empty slots, legend unchanged. No special empty illustration: an empty grid **is** the message. |
| **Full container** | occupied = cells | Fill bar hatched + "FULL". Placement into it shows the red X ghost. A rejected `no_room` flashes the bar once. |
| **No backpack** | none equipped — **always the case at M-1a** (backpack is M-1b per the epic) | Backpack section and its divider absent; own frame is 2 sections tall. |
| **Own content overflow** | own content > 576 (e.g. Big Pockets 8×4 + backpack at s = 1.5: 16 + 84 + 12 + 336 + 12 + 168 = 628) | Own viewport scrolls vertically. **The hotbar row and its numeral band are pinned** (never scroll); the inventory and backpack scroll beneath them. Not reachable at M-1a (no perks). |
| **M-1a Intake grid (interim)** | the Intake is open at M-1a | 20×16 grid in a scrolling viewport (≈ 10 rows visible at s = 1; horizontal scroll too at s ≥ 1.25). Readout strip shows "Rows 1–10 / 16" at its right edge, before the legend. |
| **Walking away** (leash warning) | claimed chest position ≥ 2.5 m from the console anchor | External frame **outline and 12 px padding band** fade from 100 % to 30 % opacity linearly from 2.5 m to 3.0 m (contents stay opaque, so text contrast is unchanged). The header title is replaced by "Walking away" plus an outward arrow glyph (text + shape cue). Reverts when back under 2.5 m. |
| **Leash auto-close** | > 3.0 m (server) / container gone / death | External slides out, toast "Out of reach" (soft click). A held entry is put away into the panel (R10). Focus moves to the own panel's nearest row. |
| **Return refused** | Back step 4 → `no_room` | Toast "No room to put it away — drop it or make space · [Esc] again to skip" (KBM) / "· [Menu] Pause" (pad); held chip / ghost pulses once; the entry stays held. A second Back within F13's window closes the panel, still holding. |
| **Swimming** | body swimming | Drop is refused with "Can't drop while swimming" (hand-with-wave icon on the ghost or cell). Everything else works. |
| **Loading** | none at M-1a (loopback). From M0 a large external snapshot is paced (≤ 0.5 s) | M0+: external header shows "Loading…" with a static glyph, grid hidden, input on that panel ignored. No spinner animation (reduced motion default). |
| **Panel can't open** | Mounted / Build / Dead | Not reachable at M-1a. From M1 / M2, Input & Controls' refusal text ("Dismount first", "Exit build first") goes in the prompt slot. |
| **Text scale 125 % / 150 %** | setting | `p` = 70 / 84 and every panel font × s; D7 re-evaluated (M1+); the legend drops hints; the readout truncates. |
| **Narrow aspect** (16:10, 4:3) | window | Reference width 1728 / 1440: the external max width shrinks (OQ14 table). Own frame unchanged. Intake scrolls on both axes when needed. |
| **Pause (multiplayer)** | Pause overlay | Panel visible under Pause, dimmed by the Pause overlay; the world runs; leash still applies. **Solo:** the Sim and UI clocks freeze. |
| **Platform variant** | — | None in v1 (PC only). Steam Deck uses the pad map at 1280×800 → `Wref` 1728. |

---

## Interaction Map

Mapping interactions for: **keyboard + mouse and gamepad (Full support, Xbox-positional glyphs); no touch.** Bindings are Input & Controls R6's defaults. Players may remap them, and all prompt and legend text comes from live bindings. Grammar follows Input & Controls R3 (tap on the press edge; click-or-drag for LMB in Panel, F7).

| Action | Context | KBM | Gamepad | Immediate feedback (next rendered frame) | Outcome |
|---|---|---|---|---|---|
| Open own panel | World, Holding-closed | **Tab** | **Y** | Own frame slides in (0.15 s); focus ring on the selected slot | overlay = Panel; no op |
| Open external | World (facing console ≤ 2.5 m) | tap **E** | tap **X** | Prompt "[E] Open" fires; frames slide in when the open reply arrives (≤ 2 ticks on loopback) | `open {container}` (range-gated; P13 order) |
| Close panel (keep holding) | Panel, Holding-open | **Tab**; **Esc** (Back step 5) | **B** (Back step 5) | Frames slide out (0.12 s); held chip appears if holding | overlay = none; external `close` |
| Move grid cursor | Panel, Holding-open | pointer hover; **arrow keys** (F5 auto-repeat) | **RS** cell→cell; **D-pad** stack→stack (Panel) / next-fit anchor (Holding-open, PROPOSED in Input & Controls) | Ring moves. At a panel edge: a bump tick (`sfx.ui.bump`) and the ring nudges 3 px. A new push, or a hold ≥ `edgeDwell` (0.45 s), crosses to the other panel's nearest row (remembering the last cell per panel). The viewport auto-scrolls to keep the focused footprint fully visible plus one row of margin. | none (client) |
| Pointer reclaim | Panel | mouse travel ≥ 24 px (`pointerReclaimPx`) | — | Focus follows the pointer again | none |
| Pick up | Panel, focused entry, cursor empty | **LMB** press (click-or-drag) / **Enter** | **A** | Pressed cell squashes at once (0.1 s, local acknowledgement); lift (scale + shadow) and sound when confirmed | `pick {from, entryId}` → Holding-open |
| Pick on an empty cell / slot 1 | Panel | LMB / Enter | A | Nothing (no op, no sound) — an empty cell is not an error | — |
| Place / merge / swap | Holding-open | **LMB** (tap, or drag release) / **Enter** | **A** | Ghost drops; squash-pop-hold on the target; `sfx.inv.place` (thump), merge → `sfx.inv.merge` ("tuck") + Gold count bumps once | `move {from: cursor, entryId, to, x, y, rotated}`; a mergeable target is an implicit merge; an occupied non-mergeable **hotbar** slot swaps (the displaced entry becomes the held one) |
| Drag | Panel | LMB press on an entry, move ≥ 6 px or hold ≥ 0.25 s (F7) | — | Entry lifts under the pointer; the release places | `pick` at press, `move` at release |
| Drag released outside both panels | Holding-open (drag) | LMB release outside | — | Ghost glides back to origin | `return {}` — **never a drop** (GDD) |
| Click outside the panels | Panel | LMB | — | Nothing | — |
| Rotate | Holding-open | **R** | **RB** | Ghost footprint rotates 90° (0.1 s); square → ghost shakes once + "Can't rotate" toast (client-only) | none (next op carries `rotated`) |
| Put back (Cancel) | Holding-open | **Esc** (Back step 4) / **RMB** | **B** | Ghost glides to origin (0.15 s) | `return {}` |
| Put back | Holding-closed | **Esc** | **B** | Chip disappears on confirm | `return {}` |
| Quick-move | Panel, focused entry | **Shift+LMB** / **Shift+Enter** | **X** | Entry slides toward the target panel (0.15 s) on confirm; partial for stackables leaves the remainder in place with its new count (no toast — the remaining stack is the visible result) | `quick_move {from, entryId}` (R9 targets: own ↔ hotbar; Intake → `[inventory, backpack, hotbar]` for mail; panel → external when open — the server decides, e.g. `not_allowed`) |
| Drop (focused cell) | Panel | **Q** | — at M-1a (pad: action menu, M1) | Cell cuts out on confirm; the WorldItem lands in the world | `drop {entryId}` (atomic lift + drop) |
| Drop (held entry) | Holding-open | **Q** | — at M-1a (pad: close with B, then D-pad ↓) | Ghost cuts out | `drop {entryId of cursor}` |
| Drop (held entry) | Holding-closed | **Q** | **D-pad ↓** | Chip cuts out | `drop` |
| Select hotbar slot | Panel (KBM only); World / Holding-closed | **1–8**; wheel (World) | D-pad ← → (World / Holding-closed only) | Selected-slot bar moves the same frame (strip and own panel row) | none (selection rides the input frame, Input & Controls R11) |
| Scroll the external viewport | Panel | mouse wheel over the viewport; drag the scrollbar thumb | (RS / D-pad auto-scroll via focus) | Viewport moves one row per notch (`stepCap` 4 per tick) | none |
| Jump / Interact / Throw / Tool | Panel | Space / E / RMB / LMB-outside | A / X / LT / RT | Not live. At a mailbox the HUD prompt reads "🔒 Close panel to deliver" | — |
| Throw / Tool while holding | Holding-closed | RMB / LMB | LT / RT | Prompt slot "Put it away first [Esc]" / "[B]" (click-stop) | — |

**Feedback rows (M-1a).** These are the M-1a subset of Grid Inventory's feedback table. Text is **exact** (EN); H-UI-1 asserts the revert, cue id and text in the UI event log and the live region. Cue ids are the presenter's row ids. Sound ids are proposals for audio-director (`sfx.*`).

| Reason / state | Revert (visual) | Sound | Toast text (= live-region text) | Cue id |
|---|---|---|---|---|
| `blocked` | dashed Stamp Red ghost with X at the target for 0.3 s; held entry stays held | short buzz `sfx.inv.buzz` | "No room there" | `inv.fb.blocked` |
| `stack_full` | as `blocked` | buzz | "Stack is full" | `inv.fb.stack_full` |
| `no_room` | fill bar of the target flashes once + ghost with X | buzz | "Make space" (M-1a never offers "Make space — Sort?": Sort is M1) | `inv.fb.no_room` |
| `out_of_bounds` | ghost shakes once (±4 px, 0.15 s), X | click-stop `sfx.inv.clickstop` | "Doesn't fit there" | `inv.fb.out_of_bounds` |
| invalid rotation (client) | ghost shakes once | click-stop | "Can't rotate" | `inv.fb.cant_rotate` |
| `not_allowed` | ghost with X and the container's category icon (e.g. envelope for mail-only) | buzz | "Mail only" (container admits only mail) / "Won't go in here" (other) | `inv.fb.not_allowed` |
| `reserved_slot` | slot 1's outline flashes once (thickness 2 → 5 px → 2 px) | click-stop | "Slot 1 is your hands" | `inv.fb.reserved_slot` |
| `cursor_busy` | held ghost / chip pulses once (scale 1.08 → 1.14 → 1.08) | click-stop | "Hands full — place it first" | `inv.fb.cursor_busy` |
| `cursor_empty` | focused cell outline flashes once | click-stop | "Nothing held" | `inv.fb.cursor_empty` |
| `swimming` | hand-with-wave icon over the ghost / cell for 0.6 s | click-stop | "Can't drop while swimming" | `inv.fb.swimming` |
| `not_open` / `out_of_range` | external frame slides out | soft click `sfx.inv.softclick` | "Out of reach" | `inv.fb.out_of_reach` |
| `position_stale` (open) | prompt resets | soft click | "Lost track of you — try again" | `inv.fb.position_stale` |
| `busy` | ghost rolls back to its pre-press state | soft click | "Busy — try again" | `inv.fb.busy` |
| `return` → `no_room` | ghost / chip pulses once | buzz | "No room to put it away — drop it or make space · [Esc] again to skip" (KBM) / "… · [Menu] Pause" (pad) | `inv.fb.return_no_room` |
| `stale_entry` / `container_gone` (M0 rows; reachable at M-1a only via `system` expiry) | snap-back + outline flash | thunk `sfx.inv.thunk` | "**[Name]** got it first" / "The inserter got it first"; `system` → "It's gone" | `inv.fb.lost_grab` |
| `bad_request` | none | none | none — **logged only** (`Drop: bad_request` in the UI event log) | `inv.fb.bad_request` |

Every rejection leaves the held entry exactly where it was before the press (the cursor never empties on a rejection), and the panel never closes on a rejection except for `not_open` / `out_of_range`.

---

## Data Requirements

| Data | Source system | Read / Write | Notes |
|---|---|---|---|
| Own containers (hotbar, inventory, backpack): entries `(entryId, x, y, rotated, w, h, kind/item, addressId, flags, count)` + `version` | Grid Inventory via `ContainerMirror` (confirmed state; ADR-0009 / ADR-0016 `IPanelView`) | Read | M-1a renders **confirmed** state only (no pending queue until M0, ADR-0016 ordering note). Redraw on mirror version change only (ADR-0019 §6). |
| External container mirror (Intake): entries + dims + `version` | Grid Inventory, subscribed on `open` | Read | Dims replicate (perks change them, R1). |
| Cursor entry | Grid Inventory (owner-only) | Read | Drives the ghost and the HUD chip. |
| Selected hotbar index | Input frame / Player Character | Read | Drives the selected-slot bar; Throw and deliver use the same value. |
| Op requests (`open`, `close`, `pick`, `move`, `return`, `quick_move`, `drop`) | → Grid Inventory through `ClientAdapter` (loopback = remote; ADR-0011) | **Write (request only)** | The UI never mutates a mirror; replies retire requests. No "is host" shortcut. |
| Op replies `{ok, reason, byActorId, cause, requested/got}` | Grid Inventory replies | Read | Feed the plain-C# feedback presenter → UI event log → toast + live region. |
| Mail address facts: district id, glyph, colour, street stripe (hue + pattern), house number, street name, kind | Mail & Destinations address table + Content & Data `MailKindDef` | Read | Static per run; cached per entry on mirror change. |
| Item facts: icon, display name, category | Content & Data `ItemDef` via `ViewCatalog` (ADR-0019) | Read | Icons by def id. |
| Container display name, category icon, `allowedCategories` | Content & Data `ContainerDef` | Read | Category icon used by the `not_allowed` revert. |
| Leash distance (claimed position → console anchor) | Player Character F5 (client-side claimed position) | Read | Drives the walking-away fade; the close itself is server-side. |
| Router state (Panel / Holding-open / Holding-closed) and live glyphs | Input & Controls router (ADR-0012) | Read | Legend and prompt text; panel input arrives via the router, never `Input.*`. |
| `textScale`, Reduced Motion | Profile & Settings (game-owned) | Read | `s` and animation paths. |
| Roster names, hats | Networking & Session roster | Read | M0 (contention text). M-1a solo: unused. |
| Toast pipeline | HUD (ADR-0018, not written) | Write (cue) | M-1a: 1-slot lane (H6). Until ADR-0018 exists, the panel emits cues to a stub lane that obeys H6 M-1a. |

**Architectural flags.** None. The UI owns only presentation state: focus position, scroll offset, held-ghost rotation (client-only per R11 — it travels in the next op's `rotated`), and pointer/keyboard mode. It owns no game state.

---

## Events Fired

| Player action | Event / request fired | Payload | UI event log record(s) |
|---|---|---|---|
| Open own panel / close | router overlay change; external `close` if open | — | none |
| Open external | `open` op | `{containerId, claimedPos (P13)}` | `Show` / feedback row on rejection |
| Pick / place / merge / swap | `pick`, `move` | `{from, entryId}`; `{from, entryId, to, x, y, rotated, count?}` (`baseVersion` from M0) | `Revert` + row on rejection |
| Return | `return` | `{}` | row on `no_room` |
| Quick-move | `quick_move` | `{from, entryId}` | row on rejection |
| Drop | `drop` | `{entryId}` | row on `swimming` / other |
| Rotate | none (client) | — | `inv.fb.cant_rotate` row when square |
| Hotbar select | none (input frame) | slot index | none |
| Focus move / scroll | none | — | none |
| Any feedback row | toast cue to HUD + live-region text | `cueId`, localisation key, args (`name`), `spokenKey` | `Enqueue` / `Show` / `Announce` (HUD H25 record kinds) |

**Analytics.** None at M-1a (a deliberate choice: the greybox measures the fun gate by observation). M1 telemetry hooks named by the GDD (space-sorts on the Intake, `contestedGrabWinsByHost`) are fired by the Sim, not the panel.

**Persistent state.** No action here writes save data or economy directly. `drop` creates a world entity and every op changes run state on the host; all of this is server-validated and outside UI ownership.

---

## Transitions & Animations

All in-panel motion follows art bible §7 (squash 0.2 s → pop ≤ 0.1 s → hold ≥ 0.15 s). **Animation never gates input:** a new press is accepted during any animation, and a cell's animation is interrupted by that cell's next state change (Game Feel). One animator, driven by the HUD flash governor's tween driver (H28), runs every panel tween and its reduced-motion path.

| Transition | Default | Reduced Motion |
|---|---|---|
| Own panel enter / exit | slide 48 px from the left + fade, 0.15 s in / 0.12 s out, ease-out | 0.1 s opacity cut |
| External enter / exit | slide 48 px from the right + fade, same timings | 0.1 s opacity cut |
| Leash auto-close | slide out right 0.2 s | 0.1 s cut |
| Press acknowledgement | pressed cell scales to 0.94 and back in 0.1 s (local, before the reply) | outline thickens 2 → 4 px for 0.1 s |
| Pick (lift) | scale 1.0 → 1.08 + shadow fades in, 0.1 s | instant lift (shadow only) |
| Place | squash 0.9 (0.2 s) → pop 1.04 (≤ 0.1 s) → hold 1.0 (≥ 0.15 s) | 0.1 s cut |
| Merge | stacks slide together 0.15 s; Gold count bumps once (scale 1.2, 0.1 s) | cut + count changes |
| Quick-move | entry slides toward the target panel edge 0.15 s, then appears placed | cut |
| Return / snap-back | ghost glides to origin 0.15 s, outline flashes once | cut + one outline flash |
| Rotate | 90° turn of the ghost footprint, 0.1 s | instant |
| Shake (out of bounds, can't rotate) | ±4 px horizontal, 2 cycles, 0.15 s | one outline flash (art bible §7: shakes → flash) |
| Walking-away fade | continuous with distance | same (it is a static mapping, not motion) |
| Selected-slot change | bar slides between slots 0.08 s | instant |
| Scroll | 0.08 s ease to the new row | instant |

**Flash safety.** Panel flashes (outline, fill bar) are small-area (< 10 % of the screen), so they are not governed flashes under HUD H28. They never use saturated-red full fills: Stamp Red appears only as the 40 % ghost fill.

**Motion sickness.** No camera or full-screen motion originates here.

---

## Input Method Completeness Checklist

Touch is not supported in v1 (`project.yaml` platform), so there is no touch block.

**Keyboard (keyboard-only play is a GDD requirement)**
- [x] Every M-1a verb is reachable without a mouse: open (Tab / E), focus (arrows), pick / place (Enter), rotate (R), return (Esc), quick-move (Shift+Enter), drop (Q), hotbar select (1–8), close (Tab / Esc).
- [x] Focus order: arrows move cell→cell in the grid, skip a multi-cell item's non-origin cells, never wrap, bump at the edge, and cross panels on a new push or a held dwell (Input & Controls R8, F5).
- [x] Visible focus: 4 px ring + caret + keyline, on top of neighbours.
- [x] Focus trap: none is needed. The panel is non-modal; the router owns all input, and game Controls use `FocusMode.None` (ADR-0012).
- [x] Esc behaviour: Back ladder, one step per press (submenu → aim → held entry → close → pause).
- [ ] Header controls (↑ from the top row): M1 — no header controls exist at M-1a.

**Gamepad**
- [x] Open (Y), focus (RS cell, D-pad stack), pick / place (A), rotate (RB), return / close (B), quick-move (X) are all mapped.
- [ ] **Drop from the panel on pad** has no M-1a binding (the action menu is M1). Workaround: B closes the panel while holding, then D-pad ↓ drops. Recorded as OQ-IP-5.
- [x] Panel switch: RS / D-pad push across the edge (LB is M0).
- [x] Back behaviour: B follows the ladder and never pauses (Menu pauses).
- [x] Controller disconnect: handled by Input & Controls R9 (synthesized releases, banner / auto-pause). A drag cannot be in progress on pad, so nothing is stranded.

**Mouse**
- [x] Hit targets: cells are `p − 2` ≥ 54 px — well over the 24 px minimum (WCAG 2.5.8).
- [x] Hover state = ring alone; drag lift = scale + shadow; tap-lift / tap-place works for players who cannot hold a button (F7's 0.25 s threshold).
- [x] Right-click = Cancel only with the panel open (Input & Controls R6, collision 3).
- [x] Drag released outside = return, never drop.

---

## Accessibility

**Tier.** No `design/accessibility-requirements.md` exists yet (OQ-IP-1). This spec targets the tier the HUD GDD names for v1 — **announcements** (HUD H26 / seed §3: v1 is not blind-playable, and the store text must say so) — plus WCAG 2.2 AA-equivalent contrast, text and motion rules for the visual surface.

- **Keyboard-only and gamepad-only:** full (checklist above), except pad Drop-from-panel at M-1a (OQ-IP-5).
- **Contrast** (HUD H23, normative):
  - Chalk on Charcoal ≈ 13 : 1;
  - house number Charcoal on Chalk ≈ 13 : 1;
  - kind silhouette Charcoal on Kraft ≈ 6 : 1;
  - Gold count on a Charcoal chip ≈ 9.6 : 1;
  - empty-cell and item outlines #787D8C on #2E3138 ≈ 3.2 : 1 (≥ 3 : 1 non-text, SC 1.4.11);
  - panel outline #787D8C on Charcoal ≥ 3 : 1;
  - focus ring Chalk with a Charcoal keyline ≥ 3 : 1 against any neighbour (SC 2.4.13-style thickness ≥ 2 px).
- **Colour never the only channel:**
  - district = glyph shape + colour (+ the numeral on the HUD chip and the readout's words);
  - street = stripe **pattern** + hue;
  - validity = X + dashed outline + colour;
  - FULL = hatch + word;
  - selected slot = bar shape;
  - slot 1 = hand glyph + hatch;
  - Cursed = skull + border.
- **Text:** fonts × s up to 150 % (the layout-bound panel cap, HN-14). Readout and header text ≥ 18 × s (reading-text floor); in-cell labels 16 × s (art bible label tier; never the only report — the readout repeats them in words). **Gap:** cell labels cannot reach 200 %. The readout carries the same facts but is also capped at 150 %. The toast and live region carry outcomes at up to 200 %. See OQ-IP-2.
- **Screen reader / announcements:** every feedback row is announced through the panel's **polite live-region Control** with the toast's text (spoken key). Focus moves are **not** announced at M-1a (per-cell navigation would need a separate accessibility tree — Grid Inventory OQ19). The readout strip is the visual equivalent; announcing it on focus settle is an M4 candidate (OQ-IP-2).
- **Motion:** Reduced Motion collapses all travel to 0.1 s opacity cuts and shakes to one outline flash (Transitions table). No auto-playing loops. The pending spinner (M0) is a static glyph.
- **Timing:** nothing on this screen is timed except the leash (distance, not time) and F13's skip window. Toast duration follows HUD's duration setting (1× – 3×).
- **Motor:** tap-to-lift / tap-to-place (no drag required); auto-repeat delay and interval are player settings (F5); no chorded verb is required (quick-move also works as pick + place).
- **Cognitive:** one meaning per binding per context; the context legend always shows the three live verbs; rejections say *why* in plain words.

---

## Localization Considerations

| Element | EN example | Budget at s = 1.0 | 40 % expansion risk |
|---|---|---|---|
| Panel title | "Your bag" / "Intake" | ≤ 200 px (≈ 18 chars at Body 20 Bold) | LOW — truncates with "…"; the full name is in the readout when the header is focused (M1) |
| Fill readout | "212 / 320", "FULL" | numerals via the locale formatter; "FULL" ≤ 80 px | LOW |
| Readout strip | "Letter ×12 · 17 Elm St · District 3 (Square) · Bag row 1, column 3" | own: ≤ 448 px minus the legend; external: ≤ 1304 px | **HIGH** in the own panel (narrow): the legend gives way first (drops hints right-to-left), then the readout truncates **from the position clause backwards** (address is kept longest). Never wraps (fixed 40 px strip). |
| Context legend | "[A] Pick up · [X] Quick-move · [B] Close" | shares the strip | HIGH → drops hints; each hint is glyph + ≤ 14-char verb |
| Toasts | "Hands full — place it first" | toast lane 420 px, **wraps to 2 lines** (HUD); 200 % → 1 toast | MEDIUM — HUD owns wrapping; strings stay ≤ 40 chars EN |
| Return-refused toast | "No room to put it away — drop it or make space · [Esc] again to skip" | 2–3 lines | **HIGH** — the longest string; HUD may need 3 lines; German ≈ 95 chars. Flag to localization: keep the clause order (reason · remedy · skip hint) so a truncation would lose the hint last. |
| "Walking away" | header title replacement | ≤ 200 px | LOW |
| Slot numerals 1–8 | key labels from live bindings (layout-correct, R9) | 16 px band | LOW |
| House numbers | "17", "1204" | chip ≤ 40 × s px | Numerals are data, not translated; 4+ digits shrink to fit within the chip at no less than 14 px, then elide the leading digits. |

All strings are localisation keys (`ui.inv.*`); names are inserted as args, never concatenated. The `·` separator and the ×N count pattern are locale-formatted. RTL is out of v1 scope.

---

## Acceptance Criteria

*Verifiable without reading another document. Evidence goes to `production/qa/evidence/grid-inventory/`.*

**Performance**
- [ ] **IP-AC-1** With a 120 Hz-capable machine at 1920×1080, pressing Tab shows the own frame starting on the **next rendered frame** (≤ 16 ms after the input is read), and the frame is fully in place at 0.15 s. A pick press shows the press squash on the next rendered frame; on loopback the lifted entry appears within 2 Sim ticks (≤ 67 ms).
- [ ] **IP-AC-2** 600 consecutive frames with both panels open and the mirror changing every tick allocate **0 B per frame** in steady state (`GC.GetAllocatedBytesForCurrentThread` probe). The grid control redraws only on a mirror version, focus or scroll change.

**Layout and OQ14**
- [ ] **IP-AC-3** At 1920×1080, s = 1.0, with the Intake open: the own frame's left edge is at x 48 and its top at y 151 (± 1 px); the external frame's left edge is at x 544; the external grid viewport's measured maximum inner size is **1304 × 576** (± 1 px). At s = 1.25 it is **1192 × 576**, and at s = 1.5 it is **1080 × 576**. At a 1440×1080 (4:3) window and s = 1.0 it is **824 × 576**. Screenshot evidence for each.
- [ ] **IP-AC-4** No panel frame pixel lies below y 839, and the HUD prompt top edge is at y 847 (± 1 px) whenever a panel is open, at every s.
- [ ] **IP-AC-5** At 1280×720 the layout equals the 1920×1080 layout scaled by 2/3 (same reference coordinates); every panel text renders at ≥ the floors above in reference px.

**Core purpose**
- [ ] **IP-AC-6** With letters (one stack ×12 to 17 Elm St, district 3), an Axe and a Medium in the panel, every entry renders at its `(x, y, rotated)` with: the district glyph tab top-left, the house number chip top-right, the kind silhouette, the street stripe (with pattern), and a Gold count on a Charcoal chip when count ≥ 2. Slot 1 shows the hand glyph and hatch; the selected slot shows the 3 px bar. Screenshot.
- [ ] **IP-AC-7** With the panel closed, the hotbar strip sits bottom-right (right edge `Wref − 48`, bottom 1048) and shows the selected slot. Pressing 1–8 moves the selected bar **the same frame** in the strip; with the panel open, the own panel's hotbar row updates the same frame and the strip is hidden.
- [ ] **IP-AC-8** Tap-to-lift / tap-to-place and drag both move an entry. R rotates the held ghost. Esc and RMB (panel open) return it to its origin. Shift-click moves an inventory entry to the hotbar and an Intake entry to the panel. Q drops the focused entry (Panel) and the held entry (Holding). A drag released outside both panels returns the entry (verified by the `return` op in the op log, and no WorldItem created).

**Navigation**
- [ ] **IP-AC-9** Keyboard only: from a closed panel, a tester can open the Intake (E), move focus with arrows to any Intake entry, quick-move it (Shift+Enter), cross to the own panel by arrow push at the edge, place an entry onto the hotbar (Enter, Enter), and close (Tab) — without touching the mouse. Gamepad only: the same with X, RS / D-pad, X, A, A, B.
- [ ] **IP-AC-10** Closing the panel while holding keeps the entry held: the held-entry chip appears beside the hotbar strip, Esc / B returns it, and Q / D-pad ↓ drops it.

**Error and empty states**
- [ ] **IP-AC-11** For each M-1a reason (`blocked`, `stack_full`, `no_room`, `out_of_bounds`, `not_allowed`, `reserved_slot`, `cursor_busy`, `cursor_empty`, `swimming`, `not_open` / `out_of_range`, `busy`), driven by a scripted Sim stub, the UI event log records the row's revert, cue id and **exact** text from the Interaction Map feedback table, and the live-region Control receives the same text. `bad_request` produces one log line and no toast. (H-UI-1, M-1a rows.)
- [ ] **IP-AC-12** An empty Intake shows "0 / 320" and empty cells only; a full one shows the hatched bar and "FULL"; Back with a full panel while holding shows the return-refused toast, and a second Esc within the skip window closes the panel with the entry still held.
- [ ] **IP-AC-13** Walking from the console: between 2.5 m and 3.0 m the external frame edge fades and the header reads "Walking away"; past 3.0 m the external panel slides out with "Out of reach", and an entry held from the Intake is now in the own panel (or still held if the panel is full).

**Accessibility**
- [ ] **IP-AC-14** Every interactive cell shows a visible focus ring ≥ 4 px when focused by keyboard or pad; no state is conveyed by colour alone (checked with a greyscale screenshot of: selected slot, slot 1, invalid ghost, FULL, district tabs).
- [ ] **IP-AC-15** With Reduced Motion on, no element translates. All transitions are ≤ 0.1 s opacity cuts, and shakes are replaced by one outline flash.

**M1 gate (phase-gated, not M-1a):** Grid Inventory H-R18-2b runs against IP-AC-3's dimensions once the manifest exists. Expected at 1920×1080: depot 16×10 at 100 % → grid; PO Depot → manifest; depot 16×15 → manifest; depot 16×10 at 150 % → manifest.

---

## Open Questions

| # | Question | Owner | Resolve by |
|---|---|---|---|
| OQ-IP-1 | No `design/accessibility-requirements.md` and no `design/player-journey.md` exist. This spec assumes the **announcements** tier (HUD H26) and WCAG 2.2 AA-equivalent contrast and text. Run `/ux-design accessibility`; author the journey map from `.claude/docs/templates/player-journey.md`. | ux-designer / producer | before `/gate-check production` |
| OQ-IP-2 | **200 % text vs a layout-bound grid.** Cell labels and the readout cap at 150 % (Input & Controls / HN-14). Is per-cell content at 200 % required (WCAG 1.4.4 reading)? Options: (a) announce the readout on focus settle (300 ms) through the live region at M4; (b) a 200 % magnifier tooltip on the focused cell. Recommended: (a). | accessibility-specialist | `/ux-design accessibility` |
| OQ-IP-3 | Art-director confirmations: (i) focus ring drawn **on top** with a 1 px Charcoal keyline, gutters kept at 2 px (**OQ8 — decided here, pending sign-off**); (ii) origin corner-tick marker while holding (new grammar); (iii) selected-slot bar vs focus ring; (iv) cell outline / empty-cell #787D8C instead of #5A5E6B; (v) item-cell face #3A3F4A (a new UI grey, not in the art bible palette); (vi) art bible §7's "selected/dragging = ring + 60 % lift" read as **scale + shadow** per the GDD [D] (60 % opacity is pending-only); (vii) OQ18's outline grammar: dashed = yours pending (M0), hat outline = another's lift (M0), red X dashed ghost = invalid — confirmed distinct here; 2 px minimum strokes as filled shapes under 4.7. | art-director | before story 023 visual sign-off |
| OQ-IP-4 | OQ25 remainder: (a) the per-cell **non-colour district cue** is the glyph tab (shape), and the district *numeral* appears on the HUD chip and the readout only — confirm no numeral is needed in cells at 54 px; (b) who's-inside chips (M0): ≤ 8 chips of 24 px hat + 12-grapheme name in the header controls slot, overflow "+N"; (c) manifest focus stability on regroup and "Make space — Sort?" persistence are M1 (HUD HN-22) — not decided here. | ux-designer | (a) art-director review; (b) M0 spec revision; (c) M1 manifest spec |
| OQ-IP-5 | Gamepad has **no Drop from the open panel** at M-1a (the action menu is M1). Accept the workaround (B closes keeping the entry, then D-pad ↓), or bind a pad Drop in Panel before M1? Recommended: accept for M-1a. | Input & Controls | M1 |
| OQ-IP-6 | Leash fade reads the client's claimed position; the server closes on its own newest sample. A brief mismatch can show "Walking away" at 2.6 m while the server closes at 3.0 m by a later sample. Acceptable (the close is always announced)? | Player Character / network-programmer | M0 |
| OQ-IP-7 | Focus on opening an external: the first occupied entry (chosen) vs the last-remembered cell for that container across opens. Revisit after the first playtest. | ux-designer | M-1a playtest |
| **HB-1** | **Hand-back to HUD & Notifications (H11 / UI Requirements):** while a panel is open, the panel occupies y 151–839 (the band 14–67 % **plus** an extension to 77.7 %), and the prompt footer is fixed at y 847 with the toast lane under it. The band alone cannot fit a header and an 8×10 truck at 100 % (560 px). The extension collides with no persistent widget (chat starts ≈ y 868, bottom-left; the hotbar strip is hidden while the panel is open). **Also:** while a panel is open the event feed must not draw over a panel frame — suggest a cap of 3 lines and drop to fit. Not edited in the GDD. | HUD & Notifications GDD / `/ux-design hud` | next HUD revision |
| **HB-2** | **Hand-back to Grid Inventory (R18 example):** "the 16-wide depot flips at ≥ 150 % text scale [D]" becomes **≥ 125 %** with the real dimensions (the height clause: 10 × 70 = 700 > 576), and the truck likewise flips at 125 %. H-R18-2a (stubs) and H-R18-2b's four screens (100 % / 150 %) are unaffected. If grid-at-125 % for the depot or truck matters, `panelInnerHeightPx` would need ≥ 700, which does not fit 1080 beside the HUD's bottom zones. Not edited in the GDD. | Grid Inventory GDD | next GDD revision |
| **HB-3** | **Hand-back to Grid Inventory UI Requirements / Layout:** the HUD hotbar strip **hides while the panel is open** (the own panel's hotbar row replaces it), and the M-1a external is drawn as a scrolling grid (D7 not evaluated until M1). | Grid Inventory GDD / HUD | next revision |
