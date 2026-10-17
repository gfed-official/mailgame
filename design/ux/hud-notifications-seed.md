# HUD & Notifications — parked seed material

> **UNREVIEWED SEED — not authoritative.** Moved verbatim out of `design/gdd/hud-notifications.md` (as reviewed 2026-10-16, receipt `af60100…`) by the creative-director's scope ruling (P1). Nothing here binds implementation. Each section names the artefact that must absorb it; that author re-decides everything. Where the HUD GDD and this file disagree, the GDD wins. Rule ids (H…, F…) refer to the HUD GDD **as reviewed**; several were renumbered or rewritten in the revision.
>
> **Review corrections that apply to this material** (design-review 2026-10-16):
> - [accessibility] Chalk on Go Green ≈ 2.1:1 and Chalk on Stamp Red ≈ 4.4:1 fail SC 1.4.3 → Charcoal numerals on Go Green (≈ 6.4:1); red fills carry ≥ 18.66 px Bold Chalk or the fill darkens. The #5A5E6B outline on Charcoal ≈ 2.4:1 fails SC 1.4.11 for interactive pills → lighten to ≈ #787D8C or use Chalk. Chat opacity floor 70 % (no outline escape).
> - [CD] "Until dismissed" toast duration is **cut** (1× / 1.5× / 2× / 3×); the **Hidden** HUD mode is **cut** (Essentials only); per-category mutes are **cut** (per-player mutes live in `design/gdd/comms.md`); the 1.25 × quota track is **cut**; Compact layout moves to **M4**.
> - [ux-designer] the prompt zone and the toast lane overlapped at y ≈ 70 % — the layout below is superseded by HUD H11's protected-zone rule.
> - [accessibility] HUD sizes are in 1080-reference units scaled by window height (HUD H23), not physical pixels.

## 1. → `/ux-design` HUD spec (`design/ux/hud.md`)

### Layout and screens (was UI Requirements)

*From ux-designer and accessibility-specialist (2026-10-16). Visual language is locked by art bible §7. Bindings belong to Input & Controls. Content belongs to the emitting GDDs. This section fixes **layout zones and screen rules**; the screens themselves are specified by `/ux-design`.*

**Safe area and scaling.**
- **Margins:** 48 px at the sides and 32 px top and bottom. On ultrawide, the HUD anchors to a centred 16:9 frame.
- **Floors are physical pixels (H23):** below 900 px of window height the HUD switches to Compact rather than scaling under the floors. This reading of the 14 px floor is a decision of this GDD, to be confirmed by the art-director in `/ux-design`.
- **Minimum supported window:** 1280×720. A hard minimum window size is proposed in Open Questions.

**Layout at 1920×1080, 100 % text** (PROPOSED).

| Zone | Anchor | Content |
|---|---|---|
| Top-left | y 32 | "Shift 3 / 5 · DELIVERY 02:14" (Heading 28), complaint envelope inline to its right |
| Top-centre | y 24, 640 × 40 | compass strip (±120°) |
| Top-right | right-aligned column, 360 wide | quota bar; below it the wallet (left) and backlog chip (right); the **pill lane** hangs under the wallet |
| Banner zone | top-centre, y 80–150 | one banner; the **knocked-down block** sits directly beneath it as its own widget (H20) |
| Tip slot | centre, y 14–20 % | one onboarding card, max 520 wide (M4) |
| **Central band** | y 14–67 % | reserved for the inventory panel, external container, map and Payday overlays — **no persistent widget sits here** |
| Prompt zone | centred, 96 px under the reticle, max 720 wide | ≤ 2 prompts; protected (H11) |
| Toast lane | centre column, y 70–82 %, 420 wide | ≤ 2 toasts, outside the prompt rect; the outcome appears next to where the eye already is after a hold |
| Bottom-left | above HP / weight | **chat** box, 420 wide, 6 lines; HP and weight icons in the corner; own mic-state icon |
| Bottom-right | above the hotbar | **event feed**, right-aligned, 5 lines; hotbar (8 slots) in the corner with the **held-entry chip** to its left |

**[D] Deviation from spec 09 §2.4:** the wireframe puts the event feed bottom-left. That collides with chat and with the left (own) inventory panel, so the feed is **right** and chat **left**.

**Compact (below 900 px height, e.g. 1280×720) and 150 % text.**
- Quota bar 280 px; compass 420 px.
- Feed 3 lines; chat 4 lines (3 at 150 %).
- "Shift n/N" dropped; backlog chip merged into the quota row.
- Pill lane 2.
- Prompt: Manual Delivery's branch table already puts each address on its own line (≤ 3 lines + 1 chip / strip row), so the old "two-address body stacks at ≥ 125 %" rule is retired (reconciliation 2026-10-16). The prompt grows **downward** from its top edge under the reticle (Manual Delivery's "grows upward from its bottom anchor" is adapted: the toast lane is anchored to the prompt's bottom edge, H11); width ≤ 720 ref px satisfies its ≤ 40 % cap; verify its EN budgets and overflow order at 1280×720. Throwing's arc label flips above the marker rather than meet the prompt rect; off-screen label placement is open (Throwing OQ13).
- Numerals wrap, never truncate.

**200 % (HUD reading text, H23):**
- 1 prompt, 1 toast, 3 feed lines;
- chat ≤ 40 % of screen width;
- the top bar wraps to two rows.

**Coexistence.**
- **Persistent HUD:** never enters the central band. Transient layers (pills, toasts, banners, markers) draw above panels.
- **Map open:** the map draws under the top bar, chat, feed, toasts and banners. The compass, prompt, hotbar and HP hide.
- **Payday overlay:** owns the central band; the compass and hotbar dim to 40 %.
- **Top bar:** never gives way to anything.


- **History overlay** (H21): a non-pausing list, category filter chips, timestamps, unread badge. It is reached from the toast "+N" chip and from an Input & Controls action — the binding is to be assigned there, because pad View is already Sort (hand-back).
- **Status read action** (H26): one action announces phase, time left, quota, wallet and complaint (Input & Controls binding, hand-back).

**Settings this system needs** (screens owned by Menus & Run Flow; persistence by Profile & Settings):

| Setting | Values |
|---|---|
| Toast duration | 1× / 1.5× / 2× / Until dismissed |
| Chat fade | Never / 5 / 10 / 20 s / Always show |
| Chat background opacity | 50–100 % |
| Chat read-out | off by default |
| Text scale | 100–150 %; HUD reading text to 200 % |
| HUD mode | Full / Essentials / Hidden |
| HUD opacity | 40–100 %, tier C |
| Per-category mutes | H22 |
| Reduced Motion, Reduced Flash | separate toggles |
| Caption size and background | 3 sizes; background opacity |
| Hide name tags | toggle |

**Accessibility requirements imposed.**
- **Every comms action** works keyboard-only, gamepad-only and with the mouse.
- **Colour is never the only channel:**
  - pings — shape + glyph;
  - quota — numeral + ✓;
  - complaint — numeral + ticks;
  - pills — sign + ✓ / ✗;
  - timer — icon + outline;
  - compass bands — numeral.
- **Live region:** every notification mirrors as text (H26).
- **Flash:** governed (H28).
- **Captions:** for essential cues (H27).
- **Not blind-playable:** v1 is announcements-tier, and is not blind-playable — the store text must say so (Accessibility, M4).

**Onboarding tip surface (M4).**
- One tip at a time, non-modal, lowest priority. It hides during any banner, an active hold or the knocked-down state, then resumes.
- It lives until its condition is met or it is dismissed (spec 09 §6) and never enters the toast queue.
- Glyph tokens such as `{action:interact}` resolve from live bindings.
- Input & Controls' first-pad-input line ("Controls: [Menu] › Settings") uses this slot rather than a toast (hand-back to Input & Controls).

**Streaming (Discord ≈ 720p).** No gameplay-critical information in Micro 14 px text. Streamer mode (hide join codes, IP, seed) belongs to Menus & Run Flow; the HUD provides "Hide chat" and "Hide name tags".

> **📌 UX Flag — HUD & Notifications**: This system has UI requirements. In Phase 4 (Pre-Production), run `/ux-design` to create UX specs **before** writing epics, for:
> - the in-game HUD layout (zones, Compact and 200 % variants, modes);
> - the notification stack (toasts, feed, pills, banners, history overlay);
> - the interaction prompt and hold ring (shared with Input & Controls, Player Character, Manual Delivery);
> - the comms wheel and its list alternative;
> - the chat box;
> - ping markers and the compass;
> - the knocked-down block (shared with Player Character).
>
> Run `/ux-design accessibility` for the announcement tier (Grid Inventory OQ19). Stories that reference UI should cite `design/ux/[screen].md`, not this GDD.

### Compact layout (was H23; now M4)

- **Compact layout:** below `compactHeightPx` = 900 physical px (for example, 1280×720) the HUD **does not scale down** under the floors. Instead it switches to the Compact layout:
  - quota bar 280 px;
  - feed 3 lines; chat 4 lines;
  - compass 420 px;
  - "Shift n/N" dropped;
  - backlog chip merged into the quota row;
  - 2 pills.

## 2. → Art bible §7 / §8 amendments and `/asset-spec` (art-director)

*REQUIRED for the UI category. Specialist: art-director (2026-10-16), consistent with art bible §1, §2, §4, §5 and §7, which **lock** the visual language. Cue names are proposals for audio-director. All motion follows the bible: squash 0.2 s → pop ≤ 0.1 s → hold ≥ 0.15 s. Every row also writes its text to the live region (H26). Render constraints come from technical-artist.*

**Global rules.**
- **Text colour:** Chalk White on Charcoal. Stamp Red and Rival Magenta are **fills** behind Chalk text, never text (bible §7).
- **Gold:** **Gold fills never carry Chalk text** (≈ 1.7:1). Gold appears only as text, a glyph or an outline on Charcoal — PROPOSED §7 amendment.
- **Containers:** r = 12 px with a 3 px #5A5E6B outline, which survives the raid dim. Interactive elements are pills. The eight-point burst is reserved for the raid and Mega alerts.
- **Plates:** feed, chat and name-tag plates are Charcoal at ≥ 85 % opacity, so walls at L* 80–95 cannot leak through. Use 9-slice plates, not text outlines, for persistent text (technical-artist); outlines only on transient world text without a plate.
- **Identity:** in every comms row, the order is hat icon → Bold name → 4 px tint strap. Tint is never text and never a type code.
- **Outlines** are filled shapes or StyleBox borders ≥ 2 px, never `draw_line`. 4.7 CanvasItem lines lost antialias feathering (sourced, `breaking-changes.md`), and the apparent weight must stay 2–3 px at 1080p. This also answers the art side of Grid Inventory OQ18.
- **Never on HUD widgets:** perforation; district colours (except compass bands and address labels); Rival Magenta on anything a player authors.

**1. Widget styling** (supports H10).

| Element | Visual | Never moves / moves |
|---|---|---|
| Timer | Charcoal panel, Heading 28 tabular digits; Amber / Red states change fill, icon and outline thickness; raid = Stamp Red fill, Chalk digits, ≤ 1 Hz pulse 100 → 80 % | digits never move |
| Quota bar | Gold fill on a Charcoal track (1.25 × quota), Chalk notch, numeric "640 / 2214" always shown; ✓ icon on reaching quota | outline never moves; one bump per step |
| Complaint envelope | Chalk envelope silhouette filling Stamp Red from the bottom; Chalk numeral on a Charcoal chip; threshold ticks | one shake per op |
| Wallet | Charcoal panel, Gold coin glyph, Gold numerals | one bump per update |
| Pills | fill + sign + glyph + number in Chalk (Mail & Destinations): gain Go Green ✓ "+N¢"; loss Stamp Red ✗ "−N¢"; **LATE** = Charcoal fill, 3 px Gold outline, Gold clock, Chalk number; **DEAD LETTER** = #5A5E6B fill with a strike band | rise with a squash |
| Compass strip | district colours on bands only, each with its numeral; echo icons are Chalk-on-Charcoal pin silhouettes, never district-coloured | bands never move |
| Toast | Charcoal panel, 40 px icon, Body text; hat + Bold name + left-edge tint strap | slides in |
| Feed line | 18 px row, hat + name + strap, no icon bounce | fades in |
| Banner | full-width band. Raid: Stamp Red fill, Display 48 Chalk, burst. Phase: Charcoal, Gold-framed only at Payday. Controller disconnected / host lost: plain Charcoal, never a burst | one ≤ 0.3 s scale-in |
| Chat box | Charcoal plate, 18 px Medium; line = hat + Bold name + Chalk text + left strap; input is a pill with a heavy outline | history never animates; new lines snap in |
| Ping marker | **screen-projected** teardrop pin, ≥ 32 px constant size, through geometry, offset above its target so it never covers an address glyph; type glyph inside; distance in Label below; sender hat + name + strap under the pin | one arrival ripple, then static |
| Compass echo | 20 px version of the same pin and glyph | static |
| Name tag | Charcoal pill, Chalk Label text: hat icon, name, strap; suffixes "· On break 1:43", "Typing…" | never bounces |
| Speaking icon | static speaker glyph in a pill beside the name tag | shown / hidden, never animated |
| Held-entry chip | bible §7 grid-cell language: swatch-with-glyph tab with district numeral, patterned stripe, Bold house number Charcoal on Chalk, Gold count, skull if Cursed | pops on change; address text never moves |
| HP bar | Go Green fill, Stamp Red at ≤ 25 %, Chalk plaster ticks at 50 / 25 %, numeral | outline static |
| Weight icon | Chalk sack silhouette filled by height (F7) | no colour, no motion |
| Backlog chip | Kraft sack icon with a count; tier 3 numeral on a Stamp Red chip | one pop per tier crossing |
| Emote bubble | Chalk glyph bubble above the hat (the 20 m read; faces vanish there) | pop, held 2 s |



**2. Feedback per HUD event.**

| Event | Visual | Motion | Reduced motion / Reduced flash |
|---|---|---|---|
| Gain / loss pill | as above | rises into place with a squash | 0.1 s opacity cut |
| Wallet change | numerals bump | one bump | outline flash once (governor) / thickness change |
| Toast | panel + identity | slides in, holds F1 | opacity cut |
| Feed line | row | fades in (≥ 0.25 s, not a flash) | cut |
| Complaint added | envelope shakes, fill steps | one shake | single outline flash + count / outline thick for 1 s |
| Quota step | Gold fill grows | one bump | step fill |
| Raid banner | burst + red band | ≤ 0.3 s scale-in, once, no strobe | opacity cut; static, no scale |
| Raid timer | red fill pulse | ≤ 1 Hz (shared pulse clock, F2) | held static red + thicker outline |
| Phase banner | Charcoal band | one scale-in | cut |
| Ping placed / received | pin + ripple; compass echo; 3 s edge arrow off-screen | one ripple, pin static 10 s | no ripple (reduced flash: static from the start) |
| Chat line | snap in | none | none |
| Emote | pose (Player Character) + glyph bubble | pop, held 2 s | static glyph, no pose |
| Speaking | icon on / off | none | none |
| Knocked down | mild desaturation only (Player Character) | none | none |
| Lost Parcel marker | Gold "×3" on Charcoal | none | none |
| Hold ring (OQ-IC-11) | 6 px filled-arc ring, clockwise from 12, Chalk on a Charcoal plate with a #5A5E6B outline; **ticks at 25 / 50 / 75 % are 3 px gaps** through the fill (visible in greyscale) | drains backward 0.1 s on cancel | quarter steps |
| Disabled verb (OQ-IC-11) | slash overlay: a 4 px Chalk bar top-left → bottom-right with 2 px Charcoal edges, over a #2E3138 chip; the glyph stays at full opacity; lock icon + reason text | none | — |
| Focus bracket (OQ-IC-11) | screen-projected corner carets ≥ 24 px, Chalk with a 2 px Charcoal gap, on the focused target's bounds; a single 16 px caret at 2.5–6 m | none | — |

**What never moves:** timer digits, compass bands, quota outline, address text, chat history, name tags, the speaking icon, HP and weight outlines, the hotbar frame, ping markers once placed. There is no idle HUD motion during Delivery (bible §2).

**3. Principles applied most directly.**
- **Principle 2** (*honest outcomes are physical*): a HUD number is never the only report (H3, cue-pairing table).
- **Principle 3** (*chunky silhouettes, flat state colour*): hat first, tint second, in every comms row and marker.
- **Principle 1** (*address before ornament*): pings and bubbles never cover an address glyph; Address pings carry the swatch-with-glyph chip.

**Render constraints (technical-artist).**
- **Layer order, bottom → top:** 3D viewport with Environment → optional full-screen effect layer → **MarkerLayer** → persistent HUD → notifications → panels → chat input and wheel → banners and the RETURN TO SENDER stamp → pause → debug.
- **Exempt from post-processing:** every layer above the 3D viewport is exempt from the raid dim, haze and Run Over desaturation. Prefer Environment adjustments for desaturation (no backbuffer copy). **VERIFY-IN-ENGINE** (Godot 4.7.1): that CanvasLayers composite after tonemap, glow and adjustments; that the HUD stays native resolution under `scaling_3d`; and that `LinearToSRGB` (no longer clamped in 4.7, sourced) is absent from HUD shaders.
- **Fonts:** MSDF for Display and 200 % prompts. Hinted raster at integer sizes for 14–20 px text, with caches pre-warmed for 100 / 125 / 150 / 200 % at load. Tabular numerals via OpenType `tnum` (VERIFY-IN-ENGINE); fallback to fixed-width digit cells.
- **Atlas:** the UI atlas is 2048² with **no mipmaps** (≈ 16.8 MB). The art bible §8 "~24 MB expected" VRAM figure understates HUD memory (≈ 19–21 MB alone) — flagged to the art-director.
- **Stretch mode:** `project.godot` sets no stretch mode or aspect (the 4.7 `canvas_items` / `expand` default applies to new projects only). It must be set explicitly → ADR.

**Audio intent** (mood only; layering, ducking and triggers belong to audio-director; rate limits in H27).

| Cue | Intent |
|---|---|
| `sfx_ui_ping_chirp` | bright, short, pitched per type; a descending two-note for Danger |
| `sfx_ui_ping_ack` | soft double tick |
| `sfx_ui_chat_blip` | soft paper tick |
| `sfx_ui_toast_tick` | dry rubber-stamp tick |
| `sfx_ui_banner_sting` | brass postal-horn flourish, phase only |
| `sfx_raid_horn` | (Audio / Enemy AI) with the caption "[Raid horn, north]" |
| `sfx_ui_slowdown` | click-stop for a refused send |

**PROPOSED art-bible amendments** (for the art-bible owner; this GDD does not edit the bible):
1. §7: Gold fills never carry Chalk text; Kraft and Postal Blue allowed as ping fills.
2. §7: the ping pin silhouette and the type → glyph / fill table above.
3. §7: hold-ring, slash-overlay and focus-bracket specs (closes OQ-IC-11).
4. §7: emote glyph bubble.
5. §7: an **18 px reading-text floor** for HUD reading text ("Body S"), with Micro 14 px reserved for redundant decoration (accessibility).
6. §8: HUD VRAM ≈ 19–21 MB.

📌 **Asset Spec** — Visual/Audio requirements are defined. After the art bible is approved, run `/asset-spec system:hud-notifications` to produce per-asset specs:
- HUD widget sheet: timer, quota, envelope, wallet, pills, compass, hotbar frame, chip, HP, weight, backlog;
- ping pins and type glyphs;
- emote glyph bubbles;
- hold ring, slash overlay, focus bracket;
- banner bands and burst;
- chat and feed plates;
- the 7 UI cues.

## 3. → Accessibility GDD (#31)

- **Announcement tier (was H26):**
- **Tier:** the v1 tier is **announcements only** (Grid Inventory OQ19). Full screen-reader navigation is out of scope, and the store text must say the game is not blind-playable.
- Settings catalogue: see section 1, "Settings this system needs".
- **H-A11Y-1 [Integration] M4** **GIVEN** every reading-text node at 1080p / 100 %, **THEN** prompt, toast, feed, chat and caption text are ≥ 18 px; timer ≥ 28; wallet and quota ≥ 20; 14 px appears only on nodes tagged decoration. At 200 % the layout is 1 prompt, 1 toast, 3 feed, chat ≤ 40 % width, and numerals never truncate.
- **H-A11Y-2 [Logic] M4** **GIVEN** Reduced Flash, **THEN** the transient budget is 0 and the raid timer is a static fill with a thicker outline, with no burst. Reduced Motion cuts travel to 0.1 s. Stamp Red appears only as a held fill or a ≤ 1 Hz pulse.
- **H-A11Y-4 [MANUAL] M4** **GIVEN** NVDA / Orca with the build, **THEN** announcements are spoken in tier order, and keyboard-only and pad-only players can ping, use the wheel or list, send Say lines, open history and mute.

## 4. → Audio (audio-director) — UI sound rate limits (was H27)

**H27 — UI sound rate limits** (proposals for audio-director; the HUD emits cue ids, and Audio enforces).
- **Toast tick:** ≤ 1 per 300 ms globally; a merged toast plays one tick; feed lines about others are silent.
- **Ping chirp:** your own ping plays at once; others' play ≤ 1 per 500 ms per sender and ≤ 1 per 250 ms overall, ducked under the raid horn.
- **Chat blip:** ≤ 1 per 1 s globally and 2 s per sender.
- **Banner sting:** once per banner.
- **Your own rejection** always plays.
- **Captions** (H4) are required for the raid horn, knockdown thud (≈ 30 m), clock-in stamp, on-break cue and controller-disconnected two-note. Format "[Raid horn, north]" with a compass arrow, ≥ 3 s, ≤ 2 lines.

## 5. → ADR "HUD Composition & Notification Queue"

**H25 — The UI event log.** Every pipeline decision is appended to a ring buffer shared with Grid Inventory's H-UI-1 and the Input router. Record kinds: `Enqueue`, `Merge`, `Drop`, `Show`, `Expire`, `Fade`, `Gone`, `Evict`, `Dismiss`, `Announce`, `Revert`, `GlyphSwap`, plus chat and ping `Received`, `Sanitised`, `CooldownRejected`. Each record carries `cueId`, `mergeKey`, `actorId`, `args`, `queueDepth`, `dropReason` and the localisation key. Tests assert keys and args; the rendered string is for humans. The ring holds 1024 records, allocated once.

**H29 — Frame hygiene.**
- **Dirty flags:** every widget is dirty-flagged. Text is set only when its value changes (the timer once per second, the wallet on change).
- **No per-frame allocation:** no per-frame string or LINQ allocation; formatted numbers are cached.
- **Pooling:** toast, feed and chat rows are pooled (8 / 8 / 100).
- **Lines:** no `draw_line` strokes. 4.7 CanvasItem lines lost antialias feathering (sourced), so rings, ticks and outlines are filled shapes or StyleBox borders ≥ 2 px.
- **Budgets:** in Formulas F8.

**HUD frame budget (was F8)** — all numbers PROPOSED, unmeasured.
`hudCpuMs = Σ components ≤ 0.8 (steady) / 1.0 (peak)`

| Component | CPU ms (PROPOSED, unmeasured) |
|---|---|
| Marker projection (41 × ~6 µs) | 0.25 |
| Compass declutter (F6) | 0.05 |
| Pipeline (F1, F2, F8) | 0.10 |
| Chat / feed / toast layout (dirty only) | 0.15 |
| Text shaping on change (amortised) | 0.10 |
| Persistent widgets (dirty-flagged) | 0.05 |
| Slack | 0.10 |
| **Total** | **0.80** |

**Output Range:**
- GPU ≤ 0.4 ms at 1080p.
- ≤ 60 canvas draws: persistent ≤ 20, markers ≤ 20 (atlased), rows ≤ 12, prompts and banners ≤ 8. That is ≈ 3 % of the art bible's ~2,000-draw cap.
- HUD panels cover ≤ 15 % of the screen, with no persistent full-screen translucent layer.
- Memory ≈ 19–21 MB: the 2048² UI sheet without mipmaps ≈ 16.8 MB, plus font caches 2–4 MB.
- ≈ 0 ms when nothing changes.
- The reference hardware is unspecified (Open Questions).

**Example:** 3 toast cues with one key in 900 ms → one item "×3". Feed events at t = 0, 1.5, 3.4 and 9.0 s → one line committing at 5.4 s (3.4 + 2 < 8), then a new line at 9.0 s.

### Performance (F8) — fixture Max-8P: roster 8, 41 markers, 5 feed, 2 toasts, 3 pills, 6 chat, 1 banner, full compass, realistic traffic plus one burst per 10 s
- **H-PERF-1 [Integration, ADVISORY] M1** After 300 warm-up and 600 measured frames, HUD update + layout has median ≤ 0.8 ms and p99 ≤ 1.0 ms. **Missing:** a reference machine (Open Questions); GPU ≤ 0.4 ms cannot be measured headless.
- **H-PERF-2 [Integration, BLOCKING] M1** Max-8P uses ≤ 60 canvas draws (persistent ≤ 20, markers ≤ 20, rows ≤ 12, prompts / banners ≤ 8). Panels cover ≤ 15 % of the screen, with no full-screen translucent layer. The UI atlas is 2048² RGBA8 with no mipmaps. The 4.7.1 draw-counter API needs a spike first.
- **H-PERF-3 [Logic, ADVISORY at M-1a → BLOCKING from M0] M-1a** After warm-up, 10,000 idle frames of the core allocate 0 bytes and write 0 text properties. The timer writes once per second. Rows are pooled (8 / 8 / 100) and never grow under flood. CI finds no `draw_line` in the HUD view. ≤ 512 B is allocated per cue event.

**→ becomes an ADR** ("HUD Composition & Notification Queue"; implementation, not design):
- the `Ui.Core` assembly boundary and the `IUiClock` / `IRoster` / `IHudState` seams;
- the CanvasLayer stack and the MarkerLayer (screen-projected, custom-drawn, atlas regions); stretch mode, aspect, safe area and Compact switching;
- pooling, dirty flags and string caches;
- font pipeline (MSDF vs raster, cache pre-warm, `tnum`, fallback fonts, IME);
- the live-region announcer Control;
- the comms RPC adapter (channel 3, packed `byte[]`, relay, valve);
- the governor's shared pulse clock.

**Engine verification items** (VERIFY-IN-ENGINE / NOT SOURCEABLE in `docs/engine-reference/godot/` — **there is no `modules/ui.md` or `modules/networking.md`**, so the UI and networking feasibility checks were **not** run against the pinned engine):
- HUD over post-process (raid dim, haze, Run Over desaturation);
- `Camera3D.UnprojectPosition` / `IsPositionBehind`;
- `_Input` vs GUI order and TextEntry focus;
- RichTextLabel `AddText` not parsing tags;
- `tnum` via FontVariation; colour-emoji fonts; IME on Linux / Wayland and Windows;
- `accessibility_live` behaviour beyond the type move;
- the draw-call counter API;
- all multiplayer RPC behaviour (Network spike list).
