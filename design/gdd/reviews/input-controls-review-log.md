# Review Log: input-controls.md

## Review — 2026-10-16 — Verdict: NEEDS REVISION
Scope signal: L
Specialists: game-designer, ux-designer, accessibility-specialist, qa-lead, godot-specialist, gameplay-programmer, network-programmer, systems-designer, ui-programmer, creative-director (senior)
Blocking items: 17 | Recommended: 10
Summary: The design holds up: the router tuple, one grammar per action, latch-at-press, the Back ladder and the commit rule are all sound. The failures are local and can be fixed by replacing text. They are stale cross-references (Player Character adopted OQ-IC-1), Player Character verbs with no binding (Shoulder swap, Spectate, Look and ping while knocked down, auto-sprint, invert X), reachability holes, formula invariants that break inside their own safe ranges, engine claims contradicted by a headless 4.7.1 probe, and AC defects. qa-lead's MAJOR verdict was overruled to NEEDS REVISION: its findings are AC defects.
Prior verdict resolved: First review
Findings:
- [BLOCKING] Edge Cases / OQ-IC-1 / H-R4-3: Player Character adopted OQ-IC-1 (honest latch, holdScalePm 500–2500); remove "conflicts with P11", unblock H-R4-3
- [BLOCKING] R6 / R10: Player Character verbs unbound: Shoulder swap, Spectate next/prev, auto-sprint, per-axis invert, rumble scale
- [BLOCKING] R2 rule 5 / H-R2-5: Dead allow-list contradicts PC P16 (Look, ping, spectate); comms wheel Mounted/Dead/Aiming liveness; no list alternative
- [BLOCKING] R2 reachability: Panel while Mounted + holding ⇒ OnFoot undefined; Draft/Results while Dead; Pause over TextEntry; Payday Skip on A collides with Jump; "World" defined inconsistently
- [BLOCKING] R6 Interact in Build contradicts PC (Deconstruct only)
- [BLOCKING] R4.3 / G1: press can resolve against a focus/prompt the player never saw; hold counter has two owners
- [BLOCKING] F1: duplicate of PC F4 diverges for manualInsertHold up to 0.8 s (holdCeil clamp, ms truncation, int32 overflow); holdFloor not derived from minHoldTicks
- [BLOCKING] F2: invariant fails inside safe ranges; worked example wrong (press must start ≤ 2.5 m)
- [BLOCKING] F5 / F6: edgeDwell > repeatDelay unsatisfiable; "≤ 1 step per tick" false; float floor bug; stepCap > hotbar/2 reverses
- [BLOCKING] R6 ruling 6 / R11 / Clocks: "ui_select includes joypad Y" contradicted by probe; ui_* stripping leaks Space/Enter; InputEvent has no timestamp (arrival stamping only); per-axis joypad events need adapter cache; VERIFIED tags without repo artifacts
- [BLOCKING] R11: bitmask edges lose order; tick-sampled routerStateId loses event-gated taps
- [BLOCKING] G9 / R11 network: one-commit ends before host ack; throw request payload unspecified; cross-stream ordering assumed; Pause in MP specifics
- [BLOCKING] R5 / F13: no step for cancelling a toggled hold; double-Back skip dead end on pad and too short; no direct KBM Pause
- [BLOCKING] G7 / R10: Toggle second press semantics; "a tap never fires a hold verb" false in Toggle; catch-armed exemption unfair to players who cannot hold
- [BLOCKING] Acceptance Criteria: H-R10-2, H-F3-1, H-F5-1, H-G3-3, H-R2-7, H-R2-8, H-R9-1, H-R3-1, H-F12-1, H-FX-1/2/3a/4 wrong or untestable; G9 never-Busy untested; bundled ACs
- [BLOCKING] UI Requirements: hard-coded glyphs in tips; "Dead" must read "Knocked down"; Busy has no render slot; Jules joins mid-shift (join-in-progress is Lobby/Prep only)
- [BLOCKING] R10 remap capture: no pad cancel; key storage as enum ints; search box needs text entry on pad
- [RECOMMENDED] R6/R10: digital look_* actions; glyphFlipCooldown ≥ 0.34 s; D-pad ↓ diagonal guard; action menu preselects first enabled row; modifiers read raw held state; flush edges on Pause; latched mic unlatches on focus loss; joy_connection_changed + DEVICE_ID_EMULATION; chat cap/escape and comms cooldowns; live region never drops outcomes
Reviewed-Content-Hash: design/gdd/input-controls.md fdda54d29b9f7189133cee1917331a08c57e5a28
Reviewed-Content-Hash: design/registry/entities.yaml 482158feebf06ba5bf6b08f6d2a366343fb5bd01

### Revision applied — 2026-10-16 (autonomous, [A] Revise now)
All 17 blocking items and the 10 cheap recommended items were applied per the creative-director's rulings. The changes were targeted replacements: +10 lines and about +12 % bytes, mostly in-place rewrites of R2, R5, R6, R10, R11, F1, F2, F5, F13 and the ACs. Deferred items became OQ-IC-15 to OQ-IC-18. OQ-IC-1, OQ-IC-3, OQ-IC-9 and OQ-IC-10 were extended with hand-backs. Registry updated: holdEff, interactResolutionOrder, holdSettingRange, holdCancelRange, throwMinAim, backLadder and oneCommitRule; catchArmSeconds is new. Status: In Review. The next step is a full re-review in a fresh session.

### Hand-back reconciliation — 2026-10-16
Sources: Player Character OQ17 / D-3 (its OQ-IC-1 reconciliation), Manual Delivery OQ2 (a)–(f), Throwing & Catching OQ2(d) + T7 D3 gate + `previewSettleMs`, HUD & Notifications HN-1 (b)–(e), Comms CQ-1 / H14, Simulation Core (throw request tick), ledger `planning-run-2026-10-16.md`. Grid Inventory and Mail & Destinations address nothing new to this GDD (OQ-IC-10 still waits on Grid Inventory).
- **Adopted (18):** Dead keeps only the comms wheel, chat, PTT, Pause, Back (+ History / Status read). There is no Look, quick ping or Spectate in v1 (PC D-3, R2 rule 5, R6, H-R2-5b). PC OQ17's open items are answered by R4.2, G7, R6 and R11. Mounted and Dead contexts are confirmed. Quick Hands `insertPm` 500 is applied on every deliver hold, with a ≤ 750 clamp owned by Content & Data (F1, examples, edge case, knob, H-F1-1, OQ-IC-7 closed). The ✓ / ✗ mark slot is added to prompt line 1. A G2 `ShouldCancel` reason was added (payload changed). The Toggle Back cancel is consumed, so it does not open Pause (R5 3b, H-G7-4). G1: the snapshot fixes the target, and the verb judges its branch at the press tick (MD3). F12 is unchanged (6-tick floor). OQ-IC-3 is closed. The throw request carries no tick. The D3 gate is in R4.3, with arm bounds 0.5–3.0 s (H-R4-2 a′). The live region uses H26's digest (no drop rule). The first-pad tip is in the tip slot. OQ-IC-11 is closed (focus bracket). The comms wheel is live while Mounted. In Dead the Emotes page is greyed. Wheel cooldowns are owned by Comms.
- **Adapted (5):** History overlay on KBM L, as a new overlay value (M1); pad reaches it via Pause › History, since View is Map, not Sort. Status read on KBM I, pad via Pause (M4). Read prompt is registered unbound (M4) and is in the H-FX-3a exemption list. `previewSettleMs` is listed in the Controls aim group and in `settings.input`, while Throwing owns its meaning. 200 % prompt text follows HUD H23, while panels stay at 150 % and 150 % remains the v1 floor (OQ-IC-12).
- **Rejected (1):** Comms H14 "wheel live while Aiming". The wheel would fight aim look (OQ-IC-19 note).
- **Noted, not resolved:** PC P14 reads Build as Interact → Deconstruct, while this GDD uses hold RMB / LT (OQ-IC-13, Building). Manual Delivery's earlier 4-tick floor request was withdrawn by its own review (CD ruling ii); no action.
- Registry: `interactResolutionOrder` (D3 gate, arm bounds, press-tick branch), `backLadder` (3b consumed, History, MD reference). YAML is valid.
- Size: 913 → 919 lines, 137,727 → 140,673 bytes (+2.1 %). Status stays In Review; a re-review is next.
