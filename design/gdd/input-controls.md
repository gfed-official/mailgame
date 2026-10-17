# Input & Controls

> **Status**: In Review — /design-review 2026-10-16: NEEDS REVISION → revision applied (17 blockers, creative-director rulings); awaiting re-review
> **Creative Director Review (CD-GDD-ALIGN)**: CONCERNS → REVISED 2026-10-16 (7 findings applied: commit scope G9, Grid Inventory rule-wording hand-back, Tool single grammar, catch-armed toggle exemption, one carrier per machine, Mastery relabelled, M-1a glyph scope)
> **Reconciled 2026-10-16 with sibling GDDs (hand-backs: 18 adopted, 5 adapted, 1 rejected)** — Player Character D-3, Manual Delivery OQ2, Throwing OQ2(d), HUD HN-1, Comms CQ-1; see review log
> **Specialists**: game-designer, ux-designer, gameplay-programmer, accessibility-specialist, godot-specialist, creative-director, systems-designer, qa-lead
> **Author**: user + agents
> **Last Updated**: 2026-10-16
> **Last Verified**: 2026-10-16
> **Implements Pillar**: Pillar 2 — Friendslop surface, factory depth (honest rules need honest triggers); Pillar 1 — The mail must flow (no verb lost)

## Summary

Input & Controls is the layer between the player's hands and every verb in the game. It owns the action map and default keyboard-and-mouse and gamepad bindings; the router states that decide which actions are live (World, Panel, Holding, Aiming and the later Build, Mounted, Dead, Map and menu states); the press grammar (tap, hold, aim-and-release, held, toggle); remapping with conflict checks that know which states can be live together; hold time and toggle accessibility; device glyphs; and the per-tick input frame. Its promise to the player is that **every mistake is yours**. The verb is fixed at press, a twitch never throws a whole stack, and no press is lost or doubled. A friend on a gamepad, or a player who can't hold a button, gets the same honest hands. That keeps the misdelivery gag funny rather than a bug.

> **Quick reference** — Layer: `Foundation` · Priority: `MVP (M-1a)` · Key deps: `None upstream (consumes declarations from Grid Inventory, Player Character & Movement, Mail & Destinations); Hard dependents: Player Character, Grid Inventory, Manual Delivery, Throwing & Catching, HUD`

## Overview

Input & Controls is the layer between the player's hands and every verb in the game. It owns the **action map** (every named action, its default keyboard-and-mouse and gamepad binding), the **input contexts** stack that decides which actions are live at a given moment (World, Panel, Holding and the later contexts this GDD adds), the **press grammar** (what a tap, a hold, a hold-and-release and a toggle are, when each fires, and how long a hold takes), **remapping** with conflict validation that knows which contexts can be live together, **device handling** (keyboard-and-mouse and any connected gamepad driving the single local player, last-used-device glyphs), and the **per-tick input frame** handed to the simulation (movement intent, look, and action edges that are never lost between 30 Hz ticks). Other systems *declare* verbs — Grid Inventory declares the panel verbs and the World / Panel / Holding contexts, Player Character declares movement, jump, sprint and the interaction framework, Manual Delivery declares the deliver hold, Throwing declares the aim-and-release throw — and this system binds them, routes them and guarantees two rules across all of them: **one meaning per binding per context**, and **no binding carries both a tap verb and a hold verb for the same target in the same context**.

The player never sees this system as a feature; they feel it as the absence of friction. A carrier who has played for ten seconds should be able to deliver, throw and open the panel without reading a prompt twice, a friend who joins with a gamepad should be able to do everything a mouse can (spec 09 §1 "controller-complete"), and a player who cannot hold a button should still be able to play (toggle alternatives, adjustable hold time, full remap). Without it every verb-declaring GDD would invent its own bindings and the collisions the Grid Inventory reviews found (A = pick and jump, X = quick-move and deliver, RMB = cancel and throw) would return.

*Design boundary:* this GDD specifies behaviour — which action is live, what fires when, what the player sees, what the simulation receives. How the router is built (a node in the scene tree vs an autoload, how `_UnhandledInput` and polled `Input.IsActionPressed` are gated, how `ui_*` actions are overridden) is implementation and **→ becomes an ADR** ("Input Router"); this GDD fixes the behaviour that ADR must deliver. Concrete numbers new to this GDD are marked **PROPOSED**.

## Player Fantasy

*Framing: indirect — players feel what the input layer enables, not the layer itself. Shaped with creative-director (two consultations, 2026-10-16); framing "Every Mistake Is Yours" chosen as the spine, with "Pick Up a Pad, You're In" as its Fellowship beat and "No Hand Left Out" as its guarantee.*

**Every mistake is yours.** You hold RMB and the arc swings red over the bakery's mailbox. You let go anyway, because Jules dared you. The parcel bonks off the porch, the baker glares, and the whole call is laughing — at you, because that was you, and you know it. Nothing on this island throws for you, delivers for you or drops for you. A tap is a tap and a hold is a hold; the ring at the mailbox fills only while you mean it; a twitch on the throw button never launches forty letters; a press that falls between two frames is never lost and never counted twice. The game's comedy is the misdelivery (art bible principle 2: *a bad outcome is never softened*), and a mistake is only funny when the player authored it — if the controller misread a button, the gag becomes a bug. The input layer's job is to make sure every disaster on the island has an author, and the author is always the one laughing.

**Pick up a pad, you're in.** Jules joins at Prep on a borrowed controller she has never used. The prompts are already in her glyphs. She takes the Elm Street stack, walks to 17 Elm, holds X, watches the ring fill, and the stack is gone — nobody had to explain anything, and nobody had to say "you can't do that on pad". By the second shift she is catching parcels you lob over the hedge. (Fellowship, MDA aesthetic #1; spec 09 §1 "controller-complete".)

**No hand left out.** You can't hold a button for half a second. You flip Deliver to toggle and set the hold time to what suits you, and you run the whole shift with the crew. Your bad throws are exactly as funny as everyone else's, because toggles are another way of giving the same honest input, never a softer one. *A friend on a gamepad, or a player who can't hold a button, gets the same honest hands.*

**Reference feelings.** *Celeste*'s input honesty (buffered presses, nothing eaten, every failure earned) — taken; its single-player precision bar — rejected, our verbs are coarse and social. *Overcooked*'s two-verb legibility for a newcomer — taken; its overloading of pick-up, place and chop onto one button, which produces misfires that feel like the game's fault — rejected, and the reason for this GDD's two binding rules. *Gang Beasts / Human: Fall Flat* slapstick — taken for outcomes only; their deliberately wobbly controls — rejected: the clumsiness lives in the parcel's flight, never in the hands. *The Last of Us Part II*'s hold-to-toggle everywhere — taken as the accessibility model.

**Pillars served.** Pillar 2 (*Friendslop surface, factory depth* — "keep the rules honest (a bad throw is never softened)"): an honest rule needs an honest trigger; a misfire is an unearned, invented failure. Pillar 1 (*The mail must flow* — "Every system exists to move addressed items from the Post Office to the right mailbox"): every verb that moves mail passes through this layer, and none of them may be lost. Pillar 3's ratified reading concerns automation versus presence, not input fluency, and Pillar 5 is about quota scaling, not device access, so neither is claimed (route mastery is a Challenge-aesthetic note in Game Feel).

**Fantasy test** (failing any one means the input layer broke the fantasy):
1. **Zero unauthored verbs.** Across the automated input-replay suite and logged playtests: no stack leaves a bag except by a completed deliver hold, a throw released after the minimum aim time while Aiming, or an explicit Drop; a tap never fires a hold verb (Hold mode — in Toggle mode a tap only starts the full, cancellable hold window, G7) and a hold never fires a tap verb; a cancelled aim never throws; a press and release inside one 30 Hz tick produce exactly one press edge. Misfire count = 0, and zero playtester reports of "it threw / delivered / dropped when I didn't mean to" (H-FX-1, H-FX-2).
2. **Ten-second guest.** 100 % of action-map actions are reachable on gamepad alone (no mouse-only flow); a 10-address hand route costs the same input count on gamepad as on KBM (≤ 11 inputs, matching Grid Inventory H-FX-1); and at least 80 % of first-time gamepad players complete a correct delivery within **10 s (PROPOSED)** of the matching prompt first appearing, with no help beyond device glyphs (H-FX-3, MANUAL).
3. **Hold-free shift.** With every toggle on, a scripted player whose presses never last longer than **300 ms (PROPOSED)** completes every M-1a verb — take, open panel, deliver, aim and throw, cancel aim, drop — and toggled aim still needs a separate deliberate press to release, so accessibility never makes a verb easier to misfire (H-FX-4).

## Detailed Design

*Specialists consulted (2026-10-16): game-designer, ux-designer, gameplay-programmer, accessibility-specialist, godot-specialist; revised after /design-review the same day. Where they disagreed the decision taken is marked **[D]** and logged in `production/session-logs/decision-log.md`. Numbers marked **PROPOSED** are new to this GDD; symbols in `code` are defined in Formulas. **HB** marks a change to Grid Inventory's provisional control table, handed back via Open Questions (this GDD does not edit grid-inventory.md).*

### Core Rules

**R1 — The action map.** Every verb the player can perform is a named **action** with a stable id (never renamed; renames need an alias table, R10). An action declares: its **grammar** (R3), its **live predicate** (a data predicate over the `RouterState` tuple, R2 — never prose), its **delivery** (`Frame`: through the per-tick input frame; `Immediate`: a reliable request on the event's frame, R11), its default KBM and gamepad bindings (R6), whether it is **reserved** (R10) or **required** (must stay bound on each device), and its owning GDD. Verb-declaring GDDs (Grid Inventory, Player Character & Movement, Manual Delivery, Throwing & Catching, later Building, Vehicles, Player Combat, HUD) own *what the verb does*; this GDD owns *when it is live, how it is triggered and what it is bound to*. No gameplay code reads a key, button or `Input.*` state directly: everything reads the router (R11). A static check enforces it (AC H-R1-2).

**R2 — Contexts.** The router's state is a tuple, not a single enum, so that "Holding survives closing the panel" and "Aiming is a sub-state of World" are exact. Every action's live predicate is evaluated over this tuple, and the remap validator uses the same predicates (R10).

`RouterState = (capture, overlay, body, holding, aiming, submenu)`

| Layer | Values (at most one per layer) | Enters | Exits |
|---|---|---|---|
| **capture** (takes all input) | none · **TextEntry** · **Pause** | TextEntry: Enter in World, Holding-closed or Dead (KBM). Pause: Esc at the end of the Back ladder (R5), P (KBM) or gamepad Menu, from anywhere; Pause over TextEntry suspends the draft and Resume restores it | TextEntry: Enter sends / Esc cancels. Pause: Esc / B / Menu / Resume |
| **overlay** | none · **Panel** (own or external, Grid Inventory) · **Map** (M2) · **Station** (Shop counter M1, Vehicle Depot / sorter Console M2) · **Draft** (M1) · **Results** (M1) · **History** (M1, HUD H21) | Panel: Tab / Y, or interacting with a container. Map: M / View. Station: tap-Interact on a counter or console. Draft / Results: phase events (Run Structure). History: L, or Pause › History | Back ladder (R5); Panel also Tab; History also L; Station also beyond 3.0 m ("Out of reach"); Draft by pick or timer |
| **body** | **OnFoot** · **Build** (M2) · **Mounted** (Player Character P14: vehicle seat — bike M1, truck M2 — or turret M3) · **Dead** (Player Character P14: knockdown → clock-in) | Build: B / LB from OnFoot. Mounted: tap-Interact on a seat or turret. Dead: knockdown | Build: B / LB, or Back ladder. Mounted: tap-Interact (Dismount); vehicle destroyed; knockdown. Dead: clock-in |
| **holding** (flag) | false · true | the cursor becomes non-empty (Grid Inventory R11) | the entry is placed, returned or dropped |
| **aiming** (flag) | false · true | Throw pressed while live (OnFoot, no overlay, not holding) | release (throws if `aimHeld ≥ throwMinAim`, else cancels), Back, any change that makes Throw non-live |
| **submenu** (Panel only) | none · **ActionMenu** · **SplitStepper** | Y / F / RMB-on-cell (R7); Split chosen | confirm, Back, or the Panel closing |

The **Payday** summary is a passive overlay that takes no input and auto-dismisses (no Skip in v1: a Skip on A would collide with Jump); the body stays live under it.

**Reachable states.** Not every tuple is reachable, and the conflict validator (R10) and AC H-R4-1 enumerate only reachable ones: `aiming ⇒ body OnFoot ∧ no overlay ∧ ¬holding ∧ no capture`; `submenu ≠ none ⇒ overlay Panel`; `holding ⇒ body OnFoot` (mounting and entering Build first auto-stow — rule 6 — and knockdown ends Holding); `overlay Panel ⇒ body OnFoot` (rule 5); `overlay History ⇒ body ∈ {OnFoot, Dead}`; `body Dead ⇒ overlay ∈ {none, Draft, Results, History} ∧ ¬holding ∧ ¬aiming` (Draft and Results are phase events: they open over any body and cancel an aim); `overlay ∈ {Map, Station, Draft, Results, History} ⇒ ¬aiming`. Each clause is stored as data; the validator and H-R2-8 read the same data.

Named shorthands used in the rest of this GDD (these are the three contexts Grid Inventory defines, made exact): **World** = no capture, no overlay, body OnFoot, not holding — it **includes** the aiming sub-state; a row that excludes aiming says "not aiming". **Panel** = overlay Panel, not holding. **Holding-open** = overlay Panel, holding. **Holding-closed** = no overlay, body OnFoot, holding.

**Layering rules.**
1. **capture** swallows everything below it. TextEntry suspends every game action including movement and push-to-talk (typing "w" never moves the carrier). Pause suspends every game action except push-to-talk (open-mic players keep talking, so PTT does too). In multiplayer Pause is a local overlay, never `SceneTree.Paused`: the Sim keeps ticking (spec 09 §2.12), the transform stream and keepalive keep sending, and the local player's movement intent is zeroed so they stand still. Entering any capture flushes unconsumed edges and mutes held buttons (G3).
2. **overlay** values are mutually exclusive: opening one closes the previous. **holding** survives every overlay change, and closing the panel never drops or returns the cursor entry by itself.
3. Under an overlay, the body keeps only its declared **pass-through** actions. Under Panel, Station and History (OnFoot): movement (walk speed — **sprint is not live**, [D] collision 2), hotbar number keys (KBM; the gamepad D-pad is the grid cursor), push-to-talk, Pause. **Look is not live under Panel** on either device, because the mouse and the right stick both drive the panel cursor [D]. Under Map and Draft, movement intent is zeroed as well (Map: the left stick pans the map — PROPOSED, OQ-IC-6).
4. A higher layer can only **disable** a lower verb, never give a lower binding a second meaning. This is how "one meaning per binding" stays true across layers.
5. Panel cannot open while body is Build, Mounted (any seat) or Dead ("Exit build first" / "Dismount first"; while Dead the panel's contents are in the spill bag). **Dead** keeps only the comms wheel (Emotes page greyed; pings anchor at the own pile — Comms H14), chat (KBM), push-to-talk, History, Status read, Pause and Back (Player Character P14 / P16 **[D-3]**: the camera auto-orbits the pile, so there is no Look, quick ping or spectate in v1 — post-v1); every other body verb is not live until clock-in. Players never see internal state names: Dead reads **Knocked down**, Holding-closed **Carrying**, World **On foot** (UI Requirements).
6. **Mounting (or entering Build) while holding** first attempts an automatic `return` ("Stowed"). If that fails with `no_room`, mounting is refused with "No room to put it away" — **HB** (Grid Inventory and Vehicles to confirm).

**R3 — Press grammar.** Every action has exactly one grammar.

| Grammar | Fires | Used by |
|---|---|---|
| **Tap** | on the **press** edge | jump, pick / place, rotate, quick-move, hotbar, panel open, Back, tap-Interact (open, mount, configure, pick up), quick ping, Drop, shoulder swap, history, status read |
| **Hold** (timed commit) | when the button has been continuously down for `holdEff` (Formulas F1); releasing early does nothing | deliver (`deliver_matching`), deconstruct (M2) |
| **Aim-release** | press enters a preview; release commits | Throw (with a minimum aim `throwMinAim`), comms wheel |
| **Held state** | live while the button is down | sprint (Hold mode), catch-armed, turret fire, throttle, push-to-talk |
| **Click-or-drag** (LMB in Panel only) | press lifts; release places if the pointer moved ≥ `dragMinPx` or the press lasted ≥ `dragMinSeconds` (F7); otherwise the entry stays on the cursor (tap-lift, tap-place) | grid pick / place |

Grammar rules:
- **G1 — Latched at press.** On the press edge the router resolves `(action, verb, targetId, routerState)` and locks it until that button is released. For Throw, the thrown hotbar entry is latched at aim start; hotbar select, Interact and Tool are not live while aiming. Nothing re-resolves mid-press: a hold never re-targets, and an aim never becomes a drop. If the focused target changes during a hold, the hold continues on the latched target while it stays valid (G2) and the prompt keeps naming it. Interact resolves against the focus snapshot **last presented on screen** (the prompt the player saw), read through Player Character's `IInteractionQuery` — never against a focus or catch state that changed after that frame. The snapshot fixes the **target**; a verb may judge its own **branch** at the press tick against the ordered edge list (Manual Delivery MD3: the deliver preview recomputed in the press tick decides MATCH / MISDELIVER_ONE, so a ✓ that turned ✗ in that tick never posts to the wrong house).
- **G2 — Cancel conditions.** A Hold or Aim-release in progress cancels with no effect (progress resets to 0, nothing is sent) when: the button is released early (Hold) · Back is pressed (Aim) · the latched target becomes invalid, is destroyed, or leaves `interactRange + cancelHysteresis` (Formulas F2) · the action stops being live because the router state changed · the window loses focus · the device that started it disconnects · the carrier is knocked down, starts swimming or mounts (list owned by Player Character & Movement) · the verb's own `ShouldCancel(latch)` returns a reason, checked every tick (Manual Delivery MD4: the latched payload changed — a MISDELIVER_ONE hold's selected entry, "You switched pieces"). Damage alone does not cancel a hold in v1 (PROPOSED; Player Character owns the final list). Every cancel is **shown** (Visual/Audio), never silent.
- **G3 — Muted until released.** A button that is down when the router state changes so that its latched action is no longer live becomes **muted**: it produces no further edges and its release is swallowed, even if the new state binds that button to something else. Examples: hold X at a mailbox, press Y to open the panel → X is muted, so its release cannot quick-move; A held for a jump when the panel opens cannot pick. An action that *becomes* live while its button is already down needs a fresh press (walking onto a mailbox with E already held does not start a deliver hold). Axes (movement) are state, not edges: they persist through Panel and Holding and are zeroed by capture, Map, Draft, a turret seat and Dead.
- **G4 — No lost or doubled presses.** Every press and release edge is captured from the event stream (never only by polling at tick time), arrival-stamped (Clocks) and queued **in order** for the next 30 Hz tick (R11). Mouse-wheel notches arrive as press and release in one event and count as steps, never as held state. A press and release inside one tick deliver both edges; two presses of a level verb inside one tick coalesce to one; stepped verbs (scroll hotbar, stepper ±) count steps (saturating, F6). OS key-repeat (echo) events never produce edges; the game's own auto-repeat (F5) is used wherever repetition is wanted.
- **G5 — Consumed press.** A press that fires an immediate verb is consumed: its follow-through cannot start a second verb. Example: RMB in Holding-open returns the entry; holding RMB longer does not then start a throw (Throw is not live there anyway, and the button is latched to Cancel until released).
- **G6 — Holds do not repeat.** A completed Hold fires once; keeping the button down does nothing until release. Delivering to the next door needs a new press.
- **G7 — Toggle mode** (per hold class, R10). *Hold* grammar in Toggle mode: one press starts the progress, which completes by itself after the same `holdEff` (the window to back out is kept, so toggles never make a delivery easier to misfire); a second press of the same action (consumed as a cancel, never re-resolved by R4.3, so it cannot become a catch), Back (ladder step 3b), or any G2 condition cancels. *Aim-release* in Toggle mode: the first press enters Aiming, a second press commits — a second press **before** `throwMinAim` is ignored and aiming continues (it neither throws nor cancels) — and Back cancels; there is no timeout. *Held state* in Toggle mode: press latches on, press again latches off; sprint also unlatches after the move stick is neutral for `sprintUnlatchSeconds` (F8) and on death, mounting, swimming or opening the panel; push-to-talk latches as "mic on" with a persistent indicator, and focus loss unlatches it. *Catch-armed* in Toggle mode: an open-air tap latches catch-armed for `catchArmSeconds` (3.0 s, the same 90-tick cap Throwing T7 puts on any arm), ending on a catch or any press — safe because catch-armed can never deliver or pick up (Degenerate strategies). Toggle mode changes only how the press is given, never what the verb does.
- **G8 — Tap targets fire on press; there is no tap-on-release.** Because no binding carries a tap verb and a hold verb for the same target in the same router state (R4), the router never has to wait for a release to tell them apart. The engine-level "tap must fire on release when the same button has a hold verb" pattern is therefore **not used**, and the remap validator keeps it unnecessary (R10).
- **G9 — Commits (CD-GDD-ALIGN finding 1).** A **commit** is any Hold or Aim-release action that moves items: deliver, deconstruct, Throw. A commit is **pending** from its press until the host's reply, or `pendingCommitTimeout` (500 ms PROPOSED) after sending; each request carries a client `reqId` and the latched `entryId`, and a rejection shows its reason in the prompt slot (a full ring followed by nothing is never allowed). While a commit is in progress or pending, taps that move items (Drop, quick-move) are not live — as G1 already rules for aiming — and a second commit started on another input is muted with "Busy" in the prompt slot (the one-commit rule, Edge Cases); if that button is still down when the commit ends, the prompt reads "Release to retry". Movement, Jump, quick ping, the comms wheel and push-to-talk are **never** commits, are never muted by one and never show "Busy": communication is never locked out (Fellowship).

**R4 — The two binding rules, made exact.**
1. **One meaning per binding per live state.** For any reachable `RouterState`, a physical input (including when used as a modifier) maps to at most one live action. Bindings shared between states that can never be live together are legal and intended (A = Jump in World, A = Pick / place in Panel).
2. **One grammar per binding per target type.** The single **Interact** action (E / X) is the one place a binding reaches several verbs in one state, and it does so only through the target. Each interactable type declares exactly **one** verb with **one** grammar in its definition (Player Character's `IInteractable`, which already specifies "one verb, tap or hold"): container, vehicle seat, turret, shop counter, console, construct (configure), WorldItem → **tap**; mailbox / destination → **deliver hold**; an inbound catchable item → **catch** (held state, armed). A target that needs two verbs is split into two interactables (a truck's driver door and its rear cargo door) or moves its second verb into the panel. **This resolves the X/E residue [D]:** X / E is tap-to-open at a container and hold-to-deliver at a mailbox *because those are different target types*, the prompt names the grammar before the thumb moves ("[E] Open" vs "Hold [E] Deliver"), and the verb is latched at press (G1). Options rejected: a separate Deliver binding (costs a scarce gamepad button and reintroduces spec 09's two-button "Deliver all" confusion); making every interaction a hold (slows the most frequent interaction, opening containers).
3. **Interact resolution at press** (first match wins; focus scoring among targets of the same rank is Player Character's, Interactions table):
   1. **Catch** — Throwing's prediction (T7 / F5, `catchPredictMs` 500) says a teammate's flight will enter the carrier's catch sphere, so the prompt has already flipped to "Catch" before the press. Over a focused target, catch wins only under Throwing's **D3 gate**: the prediction is true on the press tick, no hold is in progress, and the flight's predicted static receiver is not the focused interactable (a lob into the mailbox you face never steals your Deliver).
   2. **The focused target's single verb** (tap, or deliver hold).
   3. **No target** → catch-armed while held (a player can hold Interact in open air to wait for a pass); the host keeps any arm ≥ `catchTapMs` 0.5 s and ≤ 3.0 s (Throwing T7).
   A press latched to a deliver hold is never converted into a catch mid-hold; an item that arrives during the hold lands normally.
4. A tap on a hold target fires nothing; the prompt's "Hold" word pulses once (Visual/Audio). A press with no valid target never starts a hold, and walking onto a target with the button still down does not start one either (G3).

**R5 — The Back ladder (Esc / B): one press, one step.** Back is a single router-owned action with ordered handlers; the first applicable handler consumes the press and nothing else happens.
1. Cancel text entry (Esc only; B is not live in TextEntry).
2. Close the action menu or split stepper.
3. Cancel an aim (no throw). 3b. Cancel a running Toggle-mode hold (nothing sent; the press is consumed, so Pause needs a fresh Back).
4. Return the held entry (`return`). If it fails `no_room`, the press stops here with "No room to put it away — drop it or make space", plus "· [Esc] again to skip" on KBM or "· [Menu] Pause" on pad (Grid Inventory). **While that message is visible (F13), a second Back skips this step** and continues to step 5 / 6 with the entry still held **[D]**. KBM also has a direct, remappable Pause key (**P**), so Pause never depends on a timing window. The entry is never dropped by Back.
5. Close the panel / Map / Station / History; in Build, deselect the tool, then exit Build.
6. **Pause** — Esc only (P pauses directly). **B never pauses**; gamepad pause is Menu (reserved), which opens Pause directly from any state.

**R6 — Default bindings.** KBM keys are bound by **physical** key position (so WASD stays under the left hand on AZERTY) and displayed with the label the player's layout prints (R9). Gamepad names are Xbox-positional (A = bottom face button). Phase = the milestone where the binding first ships. Changes against Grid Inventory's provisional table are marked **HB**.

*On foot and carrying (World, Holding-closed, Aiming)*

| Action | Grammar | KBM | Gamepad | Live in | Phase | Note |
|---|---|---|---|---|---|---|
| Move | axis | WASD | LS | World, Holding-closed, Aiming, Panel, Station (walk) | M-1a | |
| Look | axis | mouse | RS | World, Holding-closed, Mounted (orbit) | M-1a | not live in Panel [D] or Dead (auto-orbit, PC D-3); digital `look_*` actions bindable to keys / buttons (one-stick play) |
| Sprint | held state; mode Hold / Toggle / **Auto** (KBM default Hold; pad default **Toggle**) | Shift | L3 | World, Holding-closed | M-1a | not live in Panel [D]. Auto (PC P14): sprinting whenever move magnitude ≥ 0.9 (PROPOSED); the Sprint input walks while held |
| Jump | tap | Space | A | World, Holding-closed | M-1a | |
| Interact (tap / deliver hold / catch, R4) | per target | E | X | World (not aiming), Holding-closed (deliver includes the cursor entry) | M-1a (catch M0) | not live in Build: Deconstruct is Build's only interaction (PC P11) |
| Throw (selected hotbar entry) | aim-release, `throwMinAim` | hold RMB, release | hold LT, release | World only | M-1a | spec 09 RMB; M-1a prototype's LMB superseded |
| Cancel aim | Back step 3 | Esc (Back) | B (Back) | Aiming | M-1a | new; not a separate binding |
| Tool / attack | tap | LMB | RT | World (not aiming) | M-1a (stub) → M3 | **HB**: not live in Holding-closed ("Put it away first"). A held Attack registers as a separate action when Player Combat is designed (M3) and must pass the conflict validator |
| Hotbar select | tap / stepped | 1–8 / wheel | D-pad ← → | World (not aiming), Holding-closed; 1–8 also Panel | M-1a | |
| Drop (World: selected hotbar entry; Holding-closed: the cursor entry) | tap | Q | D-pad ↓ | World (not aiming, not during a commit — G9), Holding-closed | M-1a | drops the **whole entry** as a WorldItem at your feet, feed line "Dropped [item] ×N" [D]; D-pad ↓ is ignored if ← or → went down in the last 50 ms (diagonal guard, PROPOSED); **HB** (pad Drop of the cursor entry with the panel closed) |
| Throw / Tool pressed while holding | — | RMB / LMB | LT / RT | Holding-closed | M-1a | shows "Put it away first [Esc]" / "[B]"; **HB**: RMB is *not* Cancel with the panel closed [D] |
| Open panel | tap | Tab | Y | World, Holding-closed | M-1a | |
| Shoulder swap (camera) | tap | X | RB | World, Holding-closed; Mounted on KBM only (pad RB is the horn there; remappable) | M-1a | Player Character P14 [D] |
| Quick ping | tap | MMB | R3 | World, Holding-closed, Build, Mounted (not Dead: no reticle, PC D-3) | M0 | spec 09's "hold for wheel" split out (tap/hold rule) |
| Comms wheel (ping types + emotes) | aim-release | hold C | hold D-pad ↑ | World (not aiming — it would fight look), Holding-closed, Build, Map, Mounted, Dead (Emotes page greyed, Comms H14) | M1 | replaces spec 09's Menu radial and PC's emote radial; never a commit (G9); list alternative in Pause › Comms; client cooldowns owned by Comms |
| Chat (type) | tap | Enter | — (no gamepad text chat in v1, OQ-IC-5) | World, Holding-closed, Dead | M0 | ≤ 200 characters, BBCode escaped (host enforces) |
| Push-to-talk (optional; open mic default) | held state | V | unbound (remappable) | every state except TextEntry (live under Pause) | M0 | |
| Pause | tap | Esc (Back step 6); **P** | Menu | everywhere | M-1a | reserved (Esc, Menu); P remappable |
| History overlay (HUD H21) | tap (toggle) | L | — (Pause › History; remappable) | World (not aiming), Holding-closed, Panel, Holding-open, Dead, History | M1 | overlay; no spare pad button (View is Map from M2) |
| Status read / Read prompt (HUD H26; Manual Delivery) | tap | I / unbound | — (Pause › Status; remappable) | every non-capture state | M4 | live-region only; Accessibility presets bind both |
| Back (R5 ladder) | tap | Esc | B | everywhere | M-1a | reserved (Esc). Cancel aim, Close panel, Cancel / return and Esc-Pause are **steps of this one action**, not separate bindings; the conflict matrix treats them as one |

*Panel, Holding-open, action menu*

| Action | Grammar | KBM | Gamepad | Live in | Phase | Note |
|---|---|---|---|---|---|---|
| Grid cursor | stepped (F5 repeat) | mouse / arrow keys | RS cell→cell; D-pad stack→stack (Panel); D-pad next-fit position (Holding-open) | Panel, Holding-open | M-1a | D-pad next-fit in Holding-open is PROPOSED |
| Pick up / place (swap on an occupied hotbar slot) | click-or-drag / tap | LMB / Enter | A | Panel, Holding-open | M-1a | |
| Rotate | tap | R | RB | Holding-open | M-1a | |
| Cancel / return | tap (Back step 4; RMB is a separate Cancel action live only here) | Esc / RMB | B | Holding-open | M-1a | RMB = Cancel only with the panel open [D] |
| Quick-move | tap (chord) | Shift+LMB / Shift+Enter | X | Panel | M-1a own; M0 external | |
| Action menu (R7) | tap | **F**; RMB on a cell (cursor empty); Shift+F10 | **Y** | Panel, Holding-open | M1 | **HB** (was R3; KBM was "—") |
| Close panel (keep the cursor entry) | tap | Tab | B (Back step 5); "Close panel, keep holding" menu row | Panel, Holding-open | M-1a | **HB**: Y no longer closes [D] |
| Switch focus own ↔ external | tap | arrow key across the panel edge | LB | Panel, Holding-open | M0 | |
| Split → count stepper | tap (chord) | Ctrl+LMB / Ctrl+Enter | action menu → Split | Panel | M1 | stepper: wheel / arrows / D-pad ±, Enter / A confirms, Esc / B cancels |
| Sort by address | tap | panel button; action menu | action menu | Panel | M1 | **HB** (was View: a stray press reorders a shared container) |
| Sort by size | tap | panel button; "Make space — Sort?" toast; action menu | action menu | Panel | M1 | |
| Take all [row / district] | tap | panel button; action menu | action menu | Panel (external) | M1 | |
| Drop (focused cell or held entry) | tap | Q | action menu | Panel, Holding-open | M1 (menu) | on a cell, Drop = atomic lift + drop (**HB**, Grid Inventory to confirm) |
| Page manifest by district | tap | PgUp / PgDn | LT / RT | Panel, Holding-open | M1 | |
| Jump to row start / end; header buttons | tap | Home / End; ↑ from the top row focuses the header (Sort, Take all) | D-pad ↑ from the top row | Panel | M1 | keyboard-only reachability |
| Hotbar select | tap | 1–8 | — | Panel | M-1a | |

*Later contexts (provisional until their GDDs exist)*

| Action | KBM | Gamepad | Live in | Phase |
|---|---|---|---|---|
| Vehicle enter / exit (tap-Interact) | E | X | World / Mounted | M1 |
| Throttle / brake-reverse / steer | W / S / A-D | RT / LT / LS | Mounted (vehicle) | M1 |
| Handbrake / horn | Space / H | A / RB | Mounted (vehicle) | M1 |
| Map | M | View | World, Holding-closed, Mounted (vehicle) | M2 |
| Build mode toggle | B | LB | World ↔ Build | M2 |
| Place / belt line (drag) | LMB | RT (aim-release = line) | Build | M2 |
| Rotate | R | RB | Build | M2 |
| Deconstruct (hold, 0.5 s content value) | hold RMB | hold LT | Build | M2 |
| Pipette | Q | D-pad ↓ | Build | M2 |
| Item / category select | 1–8 | D-pad ← → | Build | M2 |
| Belt tier | wheel | Y | Build | M2 |
| Turret mount / dismount (tap-Interact) | E | X | World / Mounted (turret) | M3 |
| Turret aim / fire (held) | mouse / LMB | RS / RT | Mounted (turret) | M3 |

**Collision rulings** (spec 09 §4 and Grid Inventory's provisional column, each decided **[D]**):
1. *Menu = chat / emotes and pause* → Menu is **Pause only** and reserved; emotes and ping types move to the comms wheel (hold C / hold D-pad ↑); typed chat is Enter on KBM; gamepad has no text chat in v1; KBM P is a direct Pause.
2. *Shift = sprint and quick-move modifier* → sprint is not live in Panel, so Shift there is only the modifier. The carrier walks while the panel is open (risk for raid escapes: OQ-IC-4, Player Character to confirm).
3. *RMB = Cancel in Holding vs hold-RMB Throw* → RMB is Cancel only in **Holding-open**. In Holding-closed, RMB and LT both show "Put it away first [Esc]/[B]", so both devices see the same honest message, and Grid Inventory's contradiction (a KBM player could never see that message) is removed — **HB**.
4. *Catch vs deliver at a mailbox* → resolved at press by R4.3; never mid-hold.
5. *R3 = ping and action menu while the right stick drives the cursor* → the action menu moves to **Y** in Panel; R3 is quick ping in body states only. Clicking a stick that is steering the cursor nudges the cursor, and stick clicks are a grip-strength burden.
6. *Y toggles the panel vs B closes it* → Y opens (World / Holding-closed) and is the action menu in Panel; B closes via the Back ladder; KBM Tab still toggles. Godot's `ui_*` actions are never relied on in game contexts: the router consumes game input and game Controls take no focus (R11), which also retires the "Tab collision". (An earlier claim that `ui_select` includes joypad Y was contradicted by a headless 4.7.1 probe that found no joypad events on any `ui_*` action; real-pad check in OQ-IC-9.)
7. *A = Jump vs pick / place* and 8. *LMB = tool vs pick / place* → disjoint states; G3 makes the transition safe. In Holding-closed there is no place target, so A is Jump.
9. *Throw released immediately* → a release before `throwMinAim` **cancels** with a "Hold to aim" hint; a twitch never throws a whole stack blind (whole-stack misdelivery, Grid Inventory OQ4).
10. *Enter = chat vs pick / place* → disjoint states (chat is never live with the panel open; pick / place needs the panel). NumpadEnter = Enter.
11. *Ping tap vs wheel hold on one button (spec 09)* → split into Quick ping (tap) and Comms wheel (separate binding).
12. *Spec 09 "Deliver all matching (F) / hold X 1 s"* → retired by Grid Inventory D-B (one deliver hold delivers every match); F is reused as the KBM action-menu key.

**R7 — The action menu.** A vertical list (not a radial: item counts vary, disabled rows need room for a reason, it must read at 150 % text, and a radial would fight the right stick) anchored beside the focused cell, flipping side near the screen edge. The first **enabled** row is preselected (never a "Take all" row, so Y, A cannot mass-move a row); D-pad / stick / arrows move, A / Enter confirms, B / Esc / the opening button closes. **Row order never changes:** rows that do not apply stay in place, greyed, with a reason ("Stack of 1 can't split"). Item rows sit above a divider; container rows below it.

| Focus | Item rows | Container rows |
|---|---|---|
| Own mail or material stack | Split…, Drop, Quick-move | Sort by address, Sort by size |
| External stack / manifest row | Take all for [row], Take all for [district], Split…, Quick-move | Sort by address, Sort by size |
| Held entry (Holding-open) | Put away, Drop, Rotate, **Close panel, keep holding** | — |
| Empty cell | — | Sort by address, Sort by size |

The same menu exists on KBM (F, Shift+F10, or RMB on a cell with an empty cursor) for parity, discoverability and one-handed play; every verb also keeps its direct key. Opening the menu latches the cell focused at press (G1); if the latched stack disappears, the menu closes with its reason.

**R8 — Panel navigation without a mouse.** Keyboard-only and gamepad-only panel play are requirements (Grid Inventory). The grid cursor moves cell→cell (arrows, RS) or stack→stack (D-pad) with the game's own auto-repeat (F5), never the OS's. The cursor skips the other cells of a multi-cell item (it lands on the item's origin) and does not wrap. At a panel edge the first push stops with a bump tick; a **new** push, or a push held for `edgeDwell` (F5), crosses to the other panel, landing on the nearest row and remembering the last cell per panel; LB also switches. ↑ from the top row focuses the panel's header buttons (Sort, Take all). With KBM, an arrow press hands the cursor to the keyboard; mouse motion ≥ `pointerReclaimPx` (F9) hands it back to the pointer. The movement keys (WASD) and the grid cursor (arrows) never share a key in default bindings.

**R9 — Devices and glyphs.** One local player: **one carrier per machine** — a second pad on the same PC drives the same carrier; local split-screen is out of v1 scope (CD-GDD-ALIGN finding 5). Every connected gamepad and the keyboard and mouse drive it at the same time (no "active device" lock on *input*). Held state is the OR across devices; movement takes the axis with the largest magnitude. The **glyph set** follows the **last-used device**, which changes only on a deliberate input above the noise thresholds of F9, never during an active hold, aim, action menu or stepper (deferred to release), and never more often than once per `glyphFlipCooldown`. Keyboard and mouse events are identified by the 4.7 constants `InputEvent.DEVICE_ID_KEYBOARD` / `DEVICE_ID_MOUSE` (VERIFIED, `docs/engine-reference/godot/breaking-changes.md`), never by `device == 0`. Synthetic action events (used by tests and the router itself) never change the last-used device (their device id defaults to 0, which would read as joypad 0 — godot-specialist local probe). Glyph sets: KBM (layout-correct key labels), Xbox, PlayStation, a neutral positional set; Steam Deck uses Xbox. A **Glyph style** setting (Auto / Keyboard & mouse / Xbox / PlayStation / Neutral; Auto falls back to Xbox for an unidentified pad) overrides detection, which is required because controller-family detection is UNVERIFIED in 4.7 and Steam Input may present every pad as an Xbox pad (UNVERIFIED). **Hot-plug** (`Input.joy_connection_changed`, UNVERIFIED in 4.7; `DEVICE_ID_EMULATION` −1 events — touch / trackpad emulation, probe-observed — count as mouse): a disconnecting pad synthesizes releases for everything it held (cancelling its holds and aim, G2); in solo the game auto-pauses if that pad was the last-used device (setting, default on); in multiplayer a banner "Controller disconnected" shows and nothing pauses. A reconnected pad produces no phantom press (its buttons are muted until released, G3).

**R10 — Remapping and input settings.**
- **What is remappable:** every action on each device — movement, look-on-keys (`look_*` digital actions), jump, sprint, mouse buttons and wheel, every gamepad button, trigger and stick click. KBM has a primary and a secondary binding per action; gamepad has a primary and a secondary. Chords are **modifier + one input** on KBM only; modifiers (Quick-move modifier, Split modifier) are themselves remappable actions, and every chord verb is also reachable without a chord (the action menu), so no verb ever needs two simultaneous presses. Gamepad bindings are never chords. Sticks can be swapped (left-handed) but individual stick axes are not rebound.
- **Reserved:** Esc (KBM) for Back / Pause and Menu (gamepad) for Pause cannot be removed (other keys may be added). Menu navigation (arrows, Enter, Esc / D-pad, A, B, mouse) is fixed in menus and in the remap screen, so a bad binding can never lock the player out.
- **Required:** Move, Interact, Throw, Open panel, Back and Pick / place must stay bound on each device; the screen will not close while one is unbound and names it.
- **Conflict = same physical input on two actions whose live predicates can both be true in one reachable `RouterState`** (R2). Additionally a tap-grammar action and a hold-grammar action may never share an input in overlapping states (the Interact resolver is the only exception, R4.2), and a chord conflicts with its plain key in the same state unless the router's **most-specific-chord-wins** rule resolves it (Shift+Enter beats Enter; the godot-specialist confirmed 4.7.1 matches modifiers non-exactly by default, so the router must resolve this itself). On conflict the remap is **blocked** with a text message naming the other action and context ("E is already Interact (On foot). Swap · Replace · Cancel"), mirrored to the live region; Replace leaves the other action "Unbound". Reuse across states that never coexist is allowed with an info line ("Also Pick up in Panel — they never overlap").
- **Capture:** press-to-bind is a `capture`-layer state of the remap screen; Esc or holding Menu 1 s cancels; 5 s without input cancels with no change; after Swap the whole matrix is re-validated.
- **Reset** per action, per context and per device, each with confirmation and one-step undo.
- **Hold settings:** one global **Hold time** slider (`holdSetting`, 0.2–1.0 s, step 0.05, default 0.4) that scales every timed hold proportionally (F1); a **Hold / Toggle** switch per hold class — Deliver & timed holds, Throw aim, Catch (Toggle latches for `catchArmSeconds`, G7), Sprint (Hold / Toggle / Auto, separately per device, pad default Toggle), Comms wheel, Push-to-talk (Open mic / Push / Toggle).
- **Other input settings:** mouse sensitivity, stick X / Y sensitivity, invert X and invert Y (separately for mouse and stick), rumble strength (0–100 %), minimum aim time (`throwMinAim`, F11), preview settle (`previewSettleMs` 0–250, default 0 — Throwing T3 owns its meaning; listed in the aim group), inner / outer stick deadzone and response curve (F3), trigger threshold (F4), auto-repeat delay / rate (F5), aim steadiness (F10, off by default), glyph style (R9), face-button layout (Xbox / Nintendo: swaps A↔B and X↔Y for game and menus; M4), swap sticks (M4), auto-pause on focus loss / pad unplug in solo (default on).
- **Storage (data shape, owned here; file owned by Profile & Settings Persistence):** `settings.input = { version, holdSetting, throwMinAim, previewSettleMs, holdMode{class→Hold|Toggle|Auto}, sensitivity{mouse, stickX, stickY}, invert{mouseX, mouseY, stickX, stickY}, rumble, deadzone{inner, outer, curve}, triggerThreshold, repeat{delay, rate}, aimSteadiness, glyphStyle, faceLayout, swapSticks, autoPauseSolo, bindings{actionId → {kbm[2], pad[2]}} }`. Only **overrides** of the defaults are stored, so an action added in a patch picks up its default with no migration. KBM bindings store physical keys **by name**, never raw enum ints (stable across engine versions; no Godot types in the core). Spec 08 §3.8's `MetaProfile.settings {sensitivity, invertY, fov}` must grow to hold this (OQ-IC-2); `fov` stays with the camera owner.
- **Validation at load:** unknown action ids are dropped and logged; malformed bindings fall back to that action's default; ranges are clamped; versioned migrations run vN → vN+1; conflicts are re-checked against the live-predicate matrix and, if a newly added action's default collides with a player's override, **the new action loses** (left "Needs binding", one notice) so a player's choice is never silently stolen; if a required action ends up unbound on a device, that device's bindings reset to defaults with a notice.
- **Server:** no request carries a hold duration and the server never checks one (`deliver_matching`'s `holdStartTick`, Manual Delivery MD5, is a start tick for closing grace, not a duration); hold length is a client setting under the friends-only trust model (spec 13 §4.3). Manual Delivery owns any proximity/consistency check on the deliver request.

**R11 — The router and the input frame (behaviour contract for the Input Router ADR).**
- **One gate.** All game actions — event-driven and polled — are read through the router. Direct `Input.IsActionPressed`-style reads in gameplay code are forbidden (CI lint), because polled reads ignore "handled" state (godot-specialist finding, behaviour UNVERIFIED in 4.7) and only a game-owned gate makes contexts reliable. The router tracks raw held state per physical input from every event, so a release is never lost when a UI node consumes it. Joypad motion arrives one axis per event, so the adapter caches per-device axis state for F3 / F4 (the lint's only exemption is the adapter). Chord modifiers read raw held state, never the mute-filtered state (G3).
- **`ui_*` actions.** The `ui_*` InputMap is **never mutated**. In game contexts the router consumes game events in `_Input` (set-as-handled), HUD and in-world Controls take no focus (`FocusMode` None), and entering a game context releases GUI focus, so a focused Control can never "click" on Space or Enter. The custom-drawn grid consumes its own navigation; exactly one navigation owner per context. Menus outside play keep stock `ui_*` navigation.
- **Input frame.** v1 movement is client-authoritative (spec 13 §3), so the per-tick input frame feeds the **owning client's** Sim step for its own carrier (Player Character), not a server prediction loop. Fields: `tick`, `moveIntent` (unit disc, after deadzone, quantized to int16), `lookYaw`, `lookPitch` (absolute, never deltas), `held` (per-action bits, OR across devices), `edges[]` (**ordered**: action, press / release, arrival sequence, and the `routerStateId` the edge was gated against at event time — so press-release-press, a Toggle second press and two commits in one tick keep their order, and a Jump pressed just before Tab is not re-gated against Panel), `steps[]` (signed, saturating, for stepped actions), `routerStateId` (informational). A tick's edges are processed **before** hold ticks accrue. Look is applied to the camera every rendered frame; the frame carries the value sampled at the tick; Sim-side look writes (Teleport yaw, mount, dismount) re-seed the adapter through `SetLook`, and the camera owner clamps pitch. Edges latch until a tick consumes them (G4); when one render frame runs several catch-up ticks, edges attach to the **first** tick only.
- **What bypasses the frame:** every server-validated verb (`Immediate` delivery, R1) is sent as a reliable request on the frame its event arrives — inventory ops (Grid Inventory), `deliver_matching` (on hold completion), throw release (`{reqId, entryId, originClient, dir (unit, non-zero)}` — no tick field, Throwing OQ2(d)(e); camera aim sampled on the release event's frame; the host clamps the origin to Player Character F5's history band and launches, and the thrower's local launch is cosmetic until the launch event), open container, build / deconstruct, buy / pick perk, ping, chat. Nothing relies on ordering between these requests and the transform stream; range correctness comes only from the host's position history (PC F5). Bypass requests are logged with their issuing tick, so the replay corpus (H-FX-1) includes them. Panel cursor navigation is local and immediate (Grid Inventory's "optimistic on the next rendered frame").
- **Hold timing** is counted in the **owning client's Sim ticks** by **Player Character P12, the single owner of the hold counter**, using `holdTicks` from F1 — one shared Sim function; Player Character's F4 copy is retired, OQ-IC-1 (the deliver hold is client-timed, spec 06 §5; there is no host-side hold timer). The ring interpolates between ticks for display. Tick counting is deterministic and testable headless; holds cancel on Pause (G2) anyway. **[D]** (an earlier draft used a real-time clock; aligned to Player Character to avoid two clocks).
- **Focus loss** (window unfocused, alt-tab): synthesize release-with-cancel for every held input, zero movement, mark every button muted until released (G3), swallow the click that recaptures the mouse (so it cannot attack or place); in solo, auto-pause (setting, default on). A toggled mic and a toggled sprint unlatch. Gamepads are ignored while the application is unfocused (engine setting `input_devices/joypads/ignore_joypad_on_unfocused_application` — present with default false in a headless 4.7.1 probe; UNVERIFIED until the probe artifact is committed, OQ-IC-9; set it true).
- **Mouse mode:** captured in body states with look; visible in Panel, Map, Station, Draft, History, Pause and TextEntry. Cursor restore position on the switch is an ADR detail (UNVERIFIED).
- **Event log.** The router writes every raw input (mouse motion coalesced to one entry per frame; reasons as enum ids), every gate decision (fired / not live / muted / swallowed), every router-state transition and every hold start / complete / cancel with its reason into the same ring-buffered UI event log Grid Inventory's H-UI-1 uses. Tests and bug reports read it; a debug overlay (Dev Tooling) shows the live router state and held actions.

**R12 — Phasing.** **M-1a:** the action map, the router (R2–R5, R11), the default KBM and gamepad bindings for M-1a verbs, the hold / toggle modes and Hold time as config values (a developer config file `user://input.json`, PROPOSED), glyph switching between **KBM and one pad set (Xbox)** — PlayStation and Neutral sets and the Glyph style override land at M1 (CD-GDD-ALIGN advisory 7). **M0:** catch, quick ping, chat, push-to-talk, external-panel focus switch. **M1:** the Controls settings screen (remap, conflict validation, hold time, toggles, auto-sprint, invert X / Y, rumble, minimum aim, sensitivity, deadzones, auto-repeat, glyph style, reset) persisted by Profile & Settings Persistence; the action menu; comms wheel; history overlay; vehicle bindings. **M2:** Map and Build bindings register into the matrix (no new infrastructure). **M3:** turret. **M4:** face-button layout, swap sticks, status read and read prompt, one-handed bindings fixture, screen-reader pass, gamepad polish (spec 13 §6 "gamepad pass"). This lands toggles and hold time **earlier** than spec 13 §6 (M4, "toggle interact") **[D]**: the deliver hold is the M-1 fun-gate verb, and retrofitting toggle and cancel rules later would change several GDDs at once (accessibility-specialist). No feature is added or cut — sequencing only.

### States and Transitions

**Router state transitions** (owned here; each transition runs G3 on every button that is down):

| From | Trigger | To | Side effects |
|---|---|---|---|
| World | Tab / Y | Panel | look and sprint stop; mouse visible; any hold / aim cancels (G2) |
| World | Throw press (live) | World + aiming | arc preview (Throwing) |
| World + aiming | release with `aimHeld ≥ throwMinAim` | World | throw request sent |
| World + aiming | release with `aimHeld < throwMinAim`, or Back | World | cancel, "Hold to aim" hint (early release only) |
| Panel | pick (cursor becomes non-empty) | Holding-open | |
| Holding-open | place / return / drop | Panel | |
| Holding-open | Tab, or "Close panel, keep holding" | Holding-closed | HUD chip shows the held entry (Grid Inventory) |
| Holding-closed | Back (return ok) / Drop | World | |
| Holding-closed | Back (`no_room`) | Holding-closed | message; press consumed |
| Holding-closed | Tab / Y | Holding-open | |
| Panel / Holding-open | Back (step 5) | World / Holding-closed | |
| Panel (external) | leash exceeded, container gone, mount, knockdown | World / Holding-closed | "Out of reach" etc. (Grid Inventory) |
| any non-capture | Esc at the end of the ladder / P / Menu | Pause | holds cancel; edges flushed; movement zeroed (MP, no `SceneTree.Paused`) or Sim stops (solo) |
| World / Holding-closed / Dead | Enter (KBM) | TextEntry | movement zeroed; all actions suspended |
| World / Holding-closed / Panel / Dead | L, or Pause › History | History (M1) | mouse visible; any hold / aim cancels (G2); holding survives |
| any | window focus lost | same state | releases synthesized, buttons muted, solo auto-pause |
| World | tap-Interact on seat / turret | Mounted | auto-stow first if holding (R2 rule 6) |
| World / Holding-closed / Mounted | knockdown (Player Character) | Dead | every hold / aim cancels; panel closes; Holding ends (the cursor entry goes to the spill bag, Grid Inventory) |
| Dead | clock-in | World | all buttons muted until released (G3) |
| World | B / LB | Build (M2) | |

**Per-press state machine** (one instance per physical input):

| State | Enters | Exits to |
|---|---|---|
| Idle | — | Pressed on a press edge |
| Pressed (latched `action, verb, target`) | press edge with a live action | Fired (tap / click), HoldProgress (hold), Aiming (aim-release), Active (held state), Muted (no live action, or another commit in progress or pending — the one-commit rule, Edge Cases: nothing fires, release swallowed) |
| HoldProgress | hold verb latched | Complete when `heldTime ≥ holdEff`; Cancelled on any G2 condition |
| Complete | hold reached | Muted until release (G6) |
| Cancelled | G2 | Muted until release (G3) |
| Muted | router change while down, focus loss, consumed press, one-commit rule | Idle on release (the release is swallowed) |

Toggle mode replaces "button still down" with "latched until second press" in HoldProgress, Aiming and Active; every other transition is the same.

### Interactions with Other Systems

| System | Direction | Data / interface | Owner of the interface |
|---|---|---|---|
| Grid Inventory & Containers | both | Grid Inventory declares the World / Panel / Holding contexts, panel verbs and the two binding rules; this GDD binds them, makes the contexts exact (R2) and routes ops as reliable requests. Hand-backs listed in Open Questions | contexts & verbs: Grid Inventory; bindings, router, menu: here |
| Player Character & Movement | both | consumes the input frame (`moveIntent`, look, sprint, jump edges); its interaction framework (`IInteractable`: one verb, tap or hold) supplies focus selection, the focused target and its verb grammar; this GDD supplies the press phases (pressed / released / cancelled with reason), `holdEff` (F1), the latch and cancel rules (G1–G3) and the Interact resolution order (R4.3). PC owns focus scoring, the `IInteractionQuery` focus snapshot, the hold counter (P12) and the "carrier can't act" cancel list (knockdown, swimming, mount) | focus & hold execution: PC; press grammar & F1 (shared hold-length function): here |
| Manual Delivery | Manual Delivery → here | declares the deliver hold on mailbox targets (`deliver_matching`, Grid Inventory D-B) with content duration `manualInsertHold` (spec 08 §2.4), its press-tick branch (G1), `ShouldCancel` reasons (G2) and the ✓ / ✗ mark on prompt line 1 | verb, branch and prompt content: Manual Delivery |
| Throwing & Catching | both | declares Throw (aim-release, World only) and Catch (armed); owns the arc, `throwMinAim`'s gameplay meaning (here as the input threshold), the catch window, and the non-colour preview marker | verbs & catch window: Throwing; grammar & thresholds: here |
| Mail & Destinations | here ← | per-destination `manualInsertHold` (content) is the base of F1 | Mail & Destinations / Content & Data |
| HUD & Notifications | here → | prompts (glyph + grammar + verb), hold ring, disabled-verb prompts, glyph-set changes, the event-log lines it displays, the "Controller disconnected" banner | rendering: HUD; content & rules: here |
| Inventory & Build UI | here → | action menu, stepper, header focus, panel cursor behaviour (R7–R8) | here (behaviour); Inventory & Build UI (screens) |
| Profile & Settings Persistence | here → | `settings.input` shape (R10); load-time validation is run by this system | file & I/O: Persistence; schema & validator: here |
| Menus & Run Flow Screens | here → | the Controls settings screen and remap capture (UI Requirements) | screens: Menus; rules: here |
| Networking & Session | here → | reliable requests are sent through its RPC layer; no hold durations on the wire; neutral input while paused in MP | Networking |
| Simulation Core | here → | the input frame is drained by `Inputs.Apply(tick)` on the owning client (spec 07 §4) | Simulation Core |
| Accessibility (#31, M4) | Accessibility → here | consumes remap, toggles, hold time, glyph style, live-region announcements; adds presets | here (hooks), Accessibility (presets & audit) |
| Dev Tooling | here → | router-state overlay, event-log viewer, input injection for the bot client (spec 06 §7) | Dev Tooling |
| Building, Vehicles, Player Combat, Map & Address UI, Proximity Voice | → here | register their actions into the map with live predicates; must pass the conflict validator | each verb's GDD |

## Formulas

*From systems-designer (2026-10-16), reconciled against Player Character & Movement F4 / P9 / P11 / P12. Every constant below is a data value in the input settings defaults or the balance file, never a code literal. Player settings are clamped on load (Edge Cases).*

**Clocks.** Two clocks, never mixed within one formula: **Sim ticks** (owning client, 30 Hz) for F1 holds, F2 range checks and F8 sprint unlatch — the things the Sim must reproduce; the client's **monotonic input clock** (integer ms) for F5 repeat, F7 click-or-drag, F9 glyph flips, F11 minimum aim, F12 ring display and F13 Back skip. Godot 4.7.1's `InputEvent` has no timestamp (headless probe), so the adapter **arrival-stamps** events in `_Input`: stamps are frame-granular, not sub-tick. A press and release batched into one frame by a hitch give `aimHeld ≈ 0` and cancel — the aim could not be measured, so nothing commits **[D]**; a hold whose button is still down keeps counting ticks. Tests use one fake clock: `ms = floor(tick × 1000 / 30) + subTickOffset`.

### F1 — Effective hold time

The `holdEff` formula is defined as:

`holdTicks = clamp(ceil(holdBase × (1 − insertPm / 1000) × (holdSetting / holdDefault) × 30), minHoldTicks, ceil(max(holdBase, holdCeil) × 30))` ; `holdEff = holdTicks / 30` — latched at press (G1); counted in the owning client's Sim ticks by Player Character P12 (R11)

Implementation note: **one shared Sim function** (Player Character's F4 copy is retired, OQ-IC-1), evaluated in **int64** with no intermediate truncation — `num = holdBaseMs × (1000 − insertPm) × holdSettingMs × 30`, `den = 1000 × holdDefaultMs × 1000`, `raw = (num + den − 1) / den`, then the clamp; `holdSetting` is stored as a count of 50 ms steps. Every constant comes from data, never a literal.

**Variables:**
| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| holdBase | float s | deliver 0.4, safe 0.2–0.8 (`manualInsertHold`, Mail & Destinations, per destination type); deconstruct 0.5 (spec 09 §2.7, M2) | data file | the content's hold length |
| insertPm | int ‰ | 0 or 500 (Quick Hands, every deliver hold — Manual Delivery MD9); StatSheet sum clamped ≤ 750 by Content & Data (≥ 1000 would zero the hold before the floor) | calculated (StatSheet) | faster-insert perk |
| holdSetting | float s | 0.2–1.0, step 0.05, default 0.4 | player setting (R10) | the global Hold time slider |
| holdDefault | float s | 0.4 | constant | the setting value that leaves content times unchanged |
| holdFloor | float s | `minHoldTicks / 30` (0.2 at 6) | derived, not a separate knob (art bible ≥ 0.2 s anticipation) | shortest possible hold |
| holdCeil | float s | 2.0 **PROPOSED** | constant | upper clamp; equals the largest content base in its safe range (`manualInsertHold` 0.8 s) at the max setting, so no v1 hold is clamped at the top |
| minHoldTicks | int | 6 | data file (Player Character F4) | tick floor |

**Output Range:** 0.2–2.0 s (6–60 ticks) for v1 holds. The setting scales every hold in proportion, so deliver : deconstruct stays 4 : 5. A future hold longer than 2.0 s can be shortened by the setting but never lengthened.
**Example:** deliver at the default setting = 0.4 s = 12 ticks; with Quick Hands = 0.2 s = 6 ticks. Quick Hands at setting 0.6 = 0.4 × 0.5 × 1.5 = **0.30 s** → 9 ticks. Deconstruct at setting 1.0 = 0.5 × 2.5 = **1.25 s** → 38 ticks (not clamped). Quick Hands at setting 0.2 = 0.1 → clamped to **0.2 s**, the same as without the perk (accepted, CD ruling ii — OQ-IC-7). A 0.8 s deliver base at setting 1.0 = **2.0 s** → 60 ticks (not clamped).
**Player Character F4** adopted `holdScalePm = 1000 × holdSetting / holdDefault`, range 500–2500 (PC OQ17); its own `holdTicks` copy is retired in favour of this function, which adds the `holdCeil` clamp (OQ-IC-1). The creative-director directive (Grid Inventory OQ22) fixes the player range at 0.2–1.0 s **[D]**.

### F2 — Hold cancel range

The `holdCancelRange` formula is defined as:

`holdCancelRange = interactRange + cancelHysteresis` — a hold or target-latched press cancels on the first Sim tick where the chest-to-anchor distance, in integer millimetres, is **strictly greater** than it (exactly 2900 mm continues).

**Variables:**
| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| interactRange | float m | 2.5 | data file (Player Character `rangeMm`, spec 11 §8) | prompt and start range |
| cancelHysteresis | float m | 0.4 | **alias** of Player Character `incumbentSlackMm` (one value, not a second knob) | slack before a hold cancels |
| hostSlack | float m | 0.5 | data file (Player Character F5 / `rangeSlackMm`) | host's range tolerance |

**Output Range:** 2.9 m (equal to Player Character's incumbent focus range, so focus loss and hold cancel are one boundary). Invariant (ContentValidator): `incumbentSlackMm ≤ rangeSlackMm − 100 mm`, so a hold that starts within 2.5 m and completes within 2.9 m sits inside the host's `rangeMm + rangeSlackMm` (3.0 m, PC F5) with 0.1 m for transform staleness; the host tests PC F5's history, where a receding carrier's older samples are closer. Grid Inventory's panel leash (open 2.5 m / close 3.0 m) is a separate value and unaffected.
**Example:** sprinting at 7.5 m/s past a mailbox 1.0 m to the side, a hold can start once within 2.5 m (x = −2.29 m) and cancels beyond 2.9 m (x = +2.72 m): 5.01 m of travel = 0.67 s, so a 0.4 s hold completes if pressed within the first 0.27 s inside start range. Drive-by delivery is legal and intended; at a hold setting ≥ 0.7 s a sprinting carrier must slow down.

### F3 — Stick deadzone (radial, scaled) and response curve

The `stickOut` formula is defined as:

`n = clamp((|s| − dzInner) / (dzOuter − dzInner), 0, 1)` ; `stickOut = |s| ≤ dzInner ? 0 : (s / |s|) × n^curve`

**Variables:**
| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| s | vec2 | each axis −1…1 | calculated (raw stick) | raw stick reading |
| dzInner | float | 0.05–0.40, default 0.15 **PROPOSED** | player setting | inner deadzone (drift rejection) |
| dzOuter | float | 0.70–1.00, default 0.95 | player setting | full-deflection point; validator raises it to ≥ dzInner + 0.10 |
| curve | float | 1.0–3.0, default 1.5 **PROPOSED** (look stick only; move stick fixed at 1.0) | player setting | response exponent |

**Output Range:** the unit disc; 0 inside the inner deadzone. Radial, so diagonals never snap to cardinals.
**Example:** s = (0.6, 0): n = (0.6 − 0.15) / 0.80 = 0.5625 → move intent 0.5625; look 0.5625^1.5 = 0.42.

### F4 — Trigger threshold with hysteresis

The `triggerPressed` formula is defined as:

`pressed_t = pressed_(t−1) ? (v ≥ max(trigOn − trigHyst, 0.05)) : (v ≥ trigOn)`

**Variables:**
| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| v | float | 0–1 | calculated (analog trigger) | trigger value |
| trigOn | float | 0.15–0.90, default 0.50 **PROPOSED** | player setting | press point |
| trigHyst | float | 0.15 **PROPOSED** | constant | release gap (release point floored at 0.05) |

**Output Range:** boolean. Digital triggers that report only 0 / 1 work at any setting.
**Example:** LT squeezed to 0.55 → pressed (aim starts); eased back to 0.40 → still pressed; at 0.34 → released, and the throw commits if `aimHeld ≥ throwMinAim` (F11).

### F5 — Auto-repeat and panel-edge dwell

The `repeatSteps` formula is defined as:

`steps(Tms) = Tms < repeatDelayMs ? 1 : 2 + (Tms − repeatDelayMs) / repeatIntervalMs` (integer ms and division — floats drift at exact boundaries) ; `edgeCross = newPush OR heldAtEdge ≥ edgeDwell`, with `edgeDwell = repeatDelay + edgeDwellExtra`

A stick acts as a digital direction when its dominant post-deadzone axis is ≥ `stickEngage` and stops below `stickRelease`; the other axis is ignored (no diagonal steps). The dwell clock starts at the first edge bump and is not reset by later repeat bumps. At most one step is emitted per rendered frame; surplus steps are dropped, never queued.

**Variables:**
| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| T | float s | ≥ 0 | calculated | time the direction has been held |
| repeatDelay | float s | 0.15–0.60, default 0.30 **PROPOSED** | player setting | delay before the first repeat |
| repeatInterval | float s | 0.04–0.20, default 0.08 **PROPOSED** | player setting | time between repeats |
| stickEngage / stickRelease | float | 0.5 / 0.3 **PROPOSED** | constant | digital-direction hysteresis |
| edgeDwellExtra | float s | 0.10–0.40, default 0.15 **PROPOSED** (`edgeDwell` 0.45 s at the default repeatDelay) | constant | held push beyond repeatDelay needed to cross to the other panel |

**Output Range:** steps ≥ 1; at most one step per rendered frame (≤ 25 / s at the 0.04 s minimum interval).
**Example:** sweeping 7 cells along an 8-wide row: the 7th step lands at 300 + 5 × 80 = 700 ms (699 ms gives 6); the bump plays; the cursor crosses to the external panel at 700 + 450 = 1150 ms, or immediately on a fresh push.

### F6 — Saturating step counts per tick

The `stepsTick` formula is defined as:

`stepsTick[a] = clamp(Σ notches in the tick, −stepCap[a], +stepCap[a])` — smooth / touchpad wheels accumulate fractional notches and emit one at |acc| ≥ 1; surplus beyond the cap is **discarded, not carried**, so scrolling stops when the hand stops.

**Variables:**
| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| notches | int | any | calculated | wheel notches / stepper presses in the tick |
| stepCap[hotbar] | int | `hotbarSize / 2` = 4, safe 2–4 | derived | half the hotbar, so a free-spinning wheel cannot wrap or reverse it in one tick |
| stepCap[stepper] | int | 10 **PROPOSED** | constant | split-count stepper |

**Output Range:** −cap…+cap (int8 on the frame).
**Example:** a hyper-scroll wheel emits 23 notches in one tick → hotbar +4 (slot 2 → slot 6).

### F7 — Click-or-drag

The `placeOnRelease` formula is defined as:

`placeOnRelease = maxDisp ≥ dragMinPx × uiScale OR (tRelease − tPress) ≥ dragMinSeconds`

**Variables:**
| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| maxDisp | float px | ≥ 0 | calculated | largest pointer distance from the press point during the press |
| dragMinPx | float px | 4–12, default 6 (at 1080p) | constant | drag distance |
| uiScale | float | content scale factor | calculated | resolution / UI scale |
| dragMinSeconds | float s | 0.15–0.40, default 0.25 **PROPOSED** | constant | slow-press threshold |

**Output Range:** boolean. 0.25 s rather than 0.2 s because motor-impaired taps run 200–300 ms; misreading the time clause is harmless (the entry is placed back on its own cell).
**Example:** a 180 ms press with 3 px travel → tap-lift (entry stays on the cursor); a 120 ms press with 40 px travel → places.

### F8 — Sprint unlatch (Toggle mode)

The `sprintUnlatch` formula is defined as:

`unlatch = |moveIntent| == 0 for ≥ ceil(sprintUnlatchSeconds × 30) consecutive ticks`

**Variables:**
| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| moveIntent | vec2 | unit disc | calculated (F3) | movement after deadzone |
| sprintUnlatchSeconds | float s | 0.10–1.0, default 0.25 | constant | neutral time that drops a latched sprint |

**Output Range:** 8 ticks (0.267 s) at the default.
**Example:** a stick reversal passes neutral in 1–3 ticks → sprint kept; stopping at a mailbox for 0.4 s → sprint unlatches.

### F9 — Glyph flip (last-used device)

The `glyphFlip` formula is defined as:

`flipToKbm = keyOrMouseButtonPress OR Σ|mouseRel| over glyphWindow ≥ mouseFlipPx`
`flipToPad = padButtonPress (triggers via F4) OR |stickOut| ≥ stickFlipMag continuously for ≥ stickFlipSeconds`
`apply = (no hold, aim, action menu or stepper active) AND (now − lastFlip ≥ glyphFlipCooldown)` — otherwise deferred to release. Pointer reclaim in the panel uses `pointerReclaimPx` (R8).

**Variables:**
| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| mouseFlipPx | float | 6–40, default 12 **PROPOSED** (raw counts — DPI-dependent, OQ-IC-8) | constant | mouse travel that counts as deliberate |
| glyphWindow | float s | 0.25 | constant | accumulation window |
| stickFlipMag / stickFlipSeconds | float | 0.5 / 0.10 (3 ticks) | constant | deliberate stick use |
| glyphFlipCooldown | float s | 0.34–1.0, default 0.4 **PROPOSED** | constant | minimum time between flips (the floor keeps flips under 3 / s, photosensitivity) |
| pointerReclaimPx | float | 12–60, default 24 **PROPOSED** | constant | mouse travel that hands the panel cursor back to the pointer (larger than mouseFlipPx because it moves the focused cell) |

**Output Range:** at most `1 / glyphFlipCooldown` flips per second (2.5 at the default, never 3).
**Example:** a pad bumped on the desk (stick 0.6 for 50 ms) → no flip; a mouse knocked 5 px → no flip; a deliberate 30 px mouse move → flip to KBM glyphs (unless a hold is in progress, then on release).

### F10 — Aim steadiness

The `lookRateAim` formula is defined as:

`lookRateAim = lookRate × aimCamMult × aimSteadiness`

**Variables:**
| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| lookRate | float | per device (sensitivity settings) | player setting | normal look rate |
| aimCamMult | float | 0.6 | data file (Player Character P9, camera owner) | existing aim sensitivity factor |
| aimSteadiness | float | 0.25–1.0, step 0.05, default 1.0 (off) | player setting | extra target-agnostic slowdown while aiming |

**Output Range:** combined multiplier 0.15–0.6 while aiming. **Invariant (art bible principle 2):** no term reads a target, distance, mailbox or outcome; no smoothing, snapping or magnetism; the throw vector is the camera aim sampled on the release event's frame (R11). A static check enforces that F10 makes no world query, and the preview colour / text / landing outcome are identical with it on or off.
**Example:** at aimSteadiness 0.5, 100 counts of mouse motion rotate the view as 30 counts would unaimed.

### F11 — Minimum aim

The `throwCommit` formula is defined as:

`throwCommit = aimHeld ≥ throwMinAim` ; `aimHeld = tRelease − tPress` (Toggle mode: `tSecondPress − tFirstPress`)

**Variables:**
| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| aimHeld | float s | ≥ 0 | calculated | time spent aiming |
| throwMinAim | float s | 0.15–0.40, default 0.25 **PROPOSED** | player setting (R10) [D] | shortest aim that throws |

**Output Range:** boolean. Does **not** scale with `holdSetting`: it is a twitch guard, not a commit hold. 0.25 s (over 0.15 / 0.18) is long enough for the arc to render and be read. A player setting, never scaled by stack size: an invisible rule could not be learned, and the player's own choice keeps every throw authored **[D]**.
**Example:** a 160 ms twitch → cancel, "Hold to aim"; a 300 ms aim → throws.

### F12 — Hold-ring visibility delay

The `ringDelay` formula is defined as:

`ringDelay = min(ringDelayMax, ringDelayFrac × holdEff)` — when the ring appears it shows true progress `heldTicks / holdTicks`, never a restarted fill.

**Variables:**
| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| ringDelayMax | float s | 0.05–0.15, default 0.10 | constant | cap on the delay |
| ringDelayFrac | float | 0.25 | constant | share of a short hold that may stay hidden |
| holdEff | float s | 0.2–2.0 | calculated (F1, `holdTicks / 30`) | the hold's length |

**Output Range:** 0.05–0.10 s at the default knobs (0.04–0.15 s across the knob ranges).
**Example:** holdEff 0.4 → ring appears at 0.10 s, already a quarter full; holdEff 0.2 (Quick Hands at the default setting) → 0.05 s. No hold is under the 6-tick floor, so Quick Hands needs no change here (Manual Delivery F2).

### F13 — Back skip window

The `backSkip` formula is defined as:

`backSkip = (previous Back stopped at ladder step 4 with no_room) AND messageVisible` — the message, and the skip, stay available for at least `backSkipSeconds` and until the next non-Back input or router-state change

**Variables:**
| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| backSkipSeconds | float s | 1.5–4.0, default 2.0 **PROPOSED** | player setting (accessibility) | minimum time the no_room message and its skip stay available |

**Output Range:** boolean; when true the press continues to ladder step 5 / 6 with the entry still held.
**Example:** holding a Large with a full bag, Esc → "No room to put it away — drop it or make space · [Esc] again to skip"; Esc again 1.9 s later → Pause opens, the Large stays on the cursor. On pad the message names [Menu] instead (in Holding-closed, B has no step after 4).

**Performance budget (PROPOSED, gameplay-programmer / systems-designer):** router + gatherer ≤ 0.1 ms per rendered frame typical, ≤ 0.3 ms during a 1000 Hz mouse burst, **0 steady-state managed allocations in the router core** (cached action names, preallocated event-log ring of 4096 entries ≤ 64 B each), and a measured bytes-per-event budget for the adapter, whose C# `InputEvent` wrappers may allocate (UNVERIFIED, OQ-IC-17); a movement input reaches the local Sim within 1 tick + 1 frame; reliable verbs are sent on the frame their event arrives.

## Edge Cases

*From systems-designer, plus the godot-specialist's engine gotchas. Each states the exact outcome.*

### Settings and data
- **If a stored setting is out of range, NaN or missing**: it is clamped to its range (NaN or missing → default) and logged. Ranges are clamped first (with the v1 ranges `dzOuter − dzInner ≥ 0.30` always holds); `trigOn` is floored at 0.15 and `repeatInterval` at 0.04 s, so no value can mean "always pressed" or divide by zero.
- **If a 0.8 s deliver base is used at `holdSetting` 1.0**: `holdEff` = 2.0 s (exactly `holdCeil`, not clamped). A future 3.0 s content hold stays 3.0 s at the maximum setting and becomes 1.5 s at 0.2.
- **If Quick Hands delivers at `holdSetting` ≤ 0.40**: the hold sits on the 6-tick floor (0.2 s); at 0.20 the perk gives nothing over no perk (accepted, CD ruling ii, Manual Delivery MD9).
- **If a perk or setting changes mid-hold**: no effect — `holdTicks` was latched at press (G1), and settings are reachable only through Pause, which cancels the hold.
- **If a loaded profile overrides two actions to the same input in co-live states**: required actions beat optional ones; otherwise canonical action-map order decides. The loser becomes "Needs binding" with one notice. Unknown keycodes are malformed and fall back to that action's default.
- **If a patch adds an action whose default collides with a player's override**: the new action is left "Needs binding" with one notice (R10); the player's binding is never silently taken.
- **If a required action is unbound on a device after load**: that device's bindings reset to defaults, with a notice naming the action.
- **If the player binds a hold-grammar action to a key already used by a tap-grammar action in a co-live state**: the remap is blocked with the reason ("E taps Open (On foot) — a hold can't share it"); the Interact resolver is the only exception (R4.2).

### Simultaneous inputs
- **If two different commits are started on different inputs** (E deliver hold, then LT throw): **one-commit rule [D]** — the first commit in progress or pending (G9) wins; the second press is muted and the prompt shows "Busy" for its duration. A bump never undoes a nearly complete delivery. Item-moving taps (Drop, quick-move) are not live during a commit; Jump, movement, quick ping, comms wheel and push-to-talk fire normally (G9).
- **If the same action is pressed on KBM and on the pad at once**: one press edge. The hold belongs to the input that started it; the other input is muted; an early release of the starting input cancels even though the other is still down. In Toggle mode, a press of that action from any device counts as the second press and cancels.
- **If a chord and its plain key are both bound in one state** (Shift+Enter quick-move, Enter pick / place): the most specific chord wins (R10); the plain action does not also fire. Holding Shift for another purpose never blocks E (Shift is not live as sprint in Panel; in World no chord shares E).
- **If Steam Input exposes a virtual pad as well as the physical pad**: held state stays correct (OR), but identical pad button edges from two pads within **20 ms (PROPOSED)** are collapsed into one so stepped verbs (D-pad hotbar) do not step twice; Glyph style overrides detection if Steam maps the pad to keyboard (UNVERIFIED behaviour — Steam spike, OQ-IC-9).

### Router-state changes
- **If the router state changes mid-hold, mid-aim or mid-drag**: G2 cancels with the reason shown and the button is muted (G3). A drag cut off by a leash auto-close leaves the entry on the cursor (Holding-closed) and the LMB release is swallowed — nothing is placed.
- **If focus is lost mid-drag**: release-with-cancel is synthesized; the entry stays on the cursor (not at the pointer position); the panel stays open; the click that recaptures the window is swallowed.
- **If the remap screen applies a change while an input is down**: Pause has already cancelled every hold; the new binding takes effect on the next press; any input down at the moment of applying is muted until released.
- **If Enter (chat) is pressed mid-hold**: TextEntry captures, the hold cancels, typing "e" produces no edge, and after Esc a physically held E stays muted until released.
- **If Pause opens in multiplayer while aiming**: the aim cancels with no throw; RMB / LT are muted; resuming never throws.
- **If the pad disconnects mid-aim** (Hold or Toggle mode): the synthesized release is a **cancel**, never a commit.
- **If the carrier is knocked down mid-hold or mid-aim**: cancel (Player Character's list), body → Dead, every button muted until released, so nothing fires on clock-in.
- **If the latched target is destroyed or becomes unavailable mid-hold**: cancel ("Target gone" / the target's reason), progress 0, button muted.
- **If the catch window opens mid-hold**: the deliver hold continues and the item lands normally (R4.3; Player Character P11 adopted this, OQ-IC-1). A press latched as catch-armed stays catch-armed even if a mailbox gains focus.
- **If Back hits `no_room` in Holding-closed**: the press stops with the `no_room` message; a second Back while it is visible (F13) skips the return and continues to close / pause with the entry still held (pad: B has no further step, so the message names [Menu]). Back never drops the entry.
- **If the player opens the action menu and the focused stack changes underneath** (another player takes it): the menu rows re-evaluate; a row that no longer applies greys with its reason; confirming a stale row returns Grid Inventory's reason code (e.g. `stale_entry`) with its normal feedback.

### Timing
- **If a hitch makes one render frame run several catch-up ticks and the batch contains a hold's release**: edges attach to the first catch-up tick (R11), so the release is processed before any further hold ticks accrue; the hold completes only if `heldTicks ≥ holdTicks` had already been reached — otherwise it is **cancelled**, never completed (safe failure).
- **If a press and release land inside one tick**: both edges reach the frame; a tap verb fires once; a hold verb starts and immediately cancels (shown as the "Hold" pulse).
- **If the mouse polls at 1000 Hz**: Godot accumulates motion (`use_accumulated_input` true in a headless 4.7.1 probe), so about one motion event per rendered frame arrives; with accumulation off (the tested worst case, H-PERF-2) ≈ 16 per frame are summed without allocation and the event log keeps one motion entry per frame; only F9's sum sees the travel.
- **If the player holds Throw for 10 s or more**: no timeout; release throws (if `aimHeld ≥ throwMinAim`, always true here).
- **If the comms wheel is released with no selection** (stick < 0.5 or pointer < **24 px (PROPOSED)** from centre): nothing is sent, the wheel closes, no feed line.

### Platform and layout
- **If the keyboard layout is AZERTY**: physical keycodes keep the positions; prompts show the layout's labels (ZQSD for movement, "A" for Drop); the number row shows the digits 1–8, not "&é…" (PROPOSED; layout-label API UNVERIFIED).
- **If NumpadEnter is pressed**: it is an alias of the default Enter binding and does not use the secondary slot; remapping treats it as a distinct key.
- **If a synthetic action event arrives** (tests, router internals): it never changes the last-used device (its device id defaults to 0 — local 4.7.1 probe) (R9).
- **If OS key-repeat (echo) events arrive for a held key**: they produce no edges (G4); the grid cursor uses F5's own repeat.

### Degenerate strategies
- **If a player macros 0.2 s holds**: identical to setting the slider to 0.2; the server checks no hold duration (R10), so a macro gains nothing beyond the slider.
- **If a Toggle-deliver player walks past a row of mailboxes**: each mailbox needs its own press (G1 latch, G6 no repeat), and a hold cancels beyond 2.9 m (F2). Drive-by delivery is legal; nothing chains by itself.
- **If a player holds catch-armed permanently**: walking onto a mailbox or WorldItem with Interact held starts nothing (G3), so a permanent catch-armed hold can never deliver or pick up by accident; holding is not better than pressing on time, only equal.
- **If a player binds Throw and Interact to the same key**: blocked — both are live in World (R10).

## Dependencies

*Hard = the system cannot function without it; Soft = enhanced by it. Input & Controls is a Foundation-layer system with **no upstream design dependency**: it consumes declarations (verbs, contexts, content hold lengths) but cannot be blocked by them. Bidirectional status: Grid Inventory (In Review) names this system in Dependencies and UI Requirements; Player Character & Movement, Manual Delivery, Throwing & Catching and HUD & Notifications (all In Review) name it as a Hard upstream; Comms (Draft) names it; every other dependent GDD is not yet written and must name Input & Controls when it is.*

**Sim / Presentation split (systems-index TD note).** The **router core** — `RouterState` resolution, live predicates, the per-press state machine (G1–G8), edge latching, hold / toggle logic, the remap validator and profile migration — is plain C# with no Godot references (fed by abstract raw-input records and a fake clock in tests), so every grammar rule is testable headless. The **Godot adapter** (Presentation) gathers events, identifies devices (`DEVICE_ID_*`), strips `ui_*`, sets mouse modes and hands records to the core. The **input frame** is a Sim struct drained by `Inputs.Apply(tick)` on the owning client (spec 07 §4). Prompts, rings and glyphs are rendered by HUD & Notifications.

**Upstream (this system consumes):**

| System | Hard/Soft | GDD | Interface this system consumes |
|---|---|---|---|
| Grid Inventory & Containers | Soft (declarations) | In Review | the World / Panel / Holding contexts, panel verbs and the two binding rules (UI Requirements); reason codes for menu rows |
| Player Character & Movement | Soft (declarations) | In Review | `IInteractionQuery` (last presented focus snapshot), `IInteractable` verb grammar per target, focus selection and focused target, the "carrier can't act" cancel list (knockdown, swim, mount), the Mounted and Dead contexts (P14), `minHoldTicks`, `rangeMm`, `incumbentSlackMm`, `aimCamMult` (P9) |
| Mail & Destinations / Content & Data Definitions | Soft | In Design / not yet written | `DestinationTypeDef.manualInsertHold` (spec 08 §2.4) — the base of F1 |
| Simulation Core | Hard (M0) | not yet written | 30 Hz tick and `Inputs.Apply(tick)`; M-1a uses a throwaway tick |
| Profile & Settings Persistence | Soft (M1) | not yet written | stores `settings.input` (R10); M-1a uses `user://input.json` |

**Downstream (depend on this system):**

| System | Hard/Soft | GDD | What they consume |
|---|---|---|---|
| Player Character & Movement | Hard | In Review | the input frame (move, look, sprint, jump), press phases with cancel reasons, `holdEff` / `holdTicks` (F1), latch and mute rules (G1–G3), Interact resolution order (R4.3). Its "Channel" grammar = this GDD's "Held state" |
| Grid Inventory & Containers | Hard | In Review | bindings for every panel verb, the action menu (R7), keyboard / gamepad navigation (R8), the Back ladder (R5); hand-backs in Open Questions |
| Manual Delivery | Hard | In Review | the deliver hold grammar, `holdEff`, Toggle mode, G1 / G2 hooks, prompt grammar with the ✓ / ✗ mark |
| Throwing & Catching | Hard | In Review | Aim-release grammar, `throwMinAim` (F11), aim steadiness (F10), Cancel aim, catch-armed resolution (R4.3) |
| HUD & Notifications | Hard | In Review | prompt content, hold ring timing (F12), disabled-verb prompts, glyph set changes (F9), banners |
| Inventory & Build UI | Hard | not yet written | R7 action menu and R8 navigation behaviour |
| Menus & Run Flow Screens | Hard (M1) | not yet written | Controls settings / remap screen rules (R10, UI Requirements) |
| Building System, Vehicles & NPC Drivers, Player Combat, Map & Address UI, Proximity Voice | Hard (their verbs) | not yet written | register actions with grammar + live predicate; must pass the conflict validator (R10) |
| Accessibility | Hard | not yet written (M4) | remap, toggles, hold time, glyph style, face layout, live-region announcements |
| Onboarding & Tips | Soft | not yet written | first-input moments and live glyphs for tip text |
| Dev Tooling | Soft | not yet written | router-state overlay, event log, raw-input injection for the bot client |
| Networking & Session | Soft | not yet written | neutral input while paused in MP; no hold durations on the wire |

Provisional assumptions (re-verify when those GDDs are written): Profile & Settings Persistence stores input settings per machine (OQ-IC-2).

## Tuning Knobs

*Derived from the Formulas variable tables (systems-designer). Player-facing settings are clamped to their safe range; constants live in the input defaults file, never in code.*

| Knob | Default | Safe range | Affects | Too low | Too high | Interacts with |
|---|---|---|---|---|---|---|
| `holdSetting` (player) | 0.4 s | 0.2–1.0 s | every timed hold (F1) | taps nearly complete holds; Quick Hands erased at 0.2 | fatigue; slow routes | `insertPm`, `holdFloor`, F12 |
| `holdCeil` | 2.0 s | ≥ 2.5 × the largest content base | upper clamp (F1) | content holds clamped at the max setting | long future holds get longer | `holdBase`, `manualInsertHold` |
| `cancelHysteresis` (= PC `incumbentSlackMm`, one value) | 0.4 m | 0.2 – (`rangeSlackMm` − 0.1) | walk-and-deliver (F2) | holds cancel from drift | completed holds rejected by the host | Player Character `incumbentSlackMm` |
| `dzInner` / `dzOuter` (player) | 0.15 / 0.95 | 0.05–0.40 / 0.70–1.00 | drift vs precision (F3) | stick drift walks the carrier | dead zone eats small aims | F9 `stickFlipMag` |
| `curve` (player, look) | 1.5 | 1.0–3.0 | look response (F3) | twitchy look | sluggish small turns | sensitivity |
| `trigOn` (player) | 0.5 | 0.15–0.90 | when LT / RT press (F4) | a resting finger starts aims | a long pull needed to aim | `throwMinAim` |
| `repeatDelay` / `repeatInterval` (player) | 0.30 / 0.08 s | 0.15–0.60 / 0.04–0.20 | grid and stepper speed (F5) | overshoot | sluggish navigation | `edgeDwellExtra` |
| `edgeDwellExtra` | 0.15 s | 0.10–0.40 (added to repeatDelay) | panel crossing (F5) | accidental crossings | crossing feels stuck | `repeatDelay` |
| `stepCap` hotbar / stepper | 4 / 10 | 2–4 (≤ hotbar / 2) / 5–20 | scroll control (F6) | slow scrolling | wheel wraps the hotbar | — |
| `dragMinPx` / `dragMinSeconds` | 6 px / 0.25 s | 4–12 / 0.15–0.40 | tap-lift vs drag (F7) | slow taps place back on their own cell | — | `uiScale` |
| `sprintUnlatchSeconds` | 0.25 s | 0.10–1.0 | Toggle sprint (F8) | reversals drop sprint | sprint survives stops | F3 |
| `mouseFlipPx` / `glyphFlipCooldown` | 12 / 0.4 s | 6–40 / 0.34–1.0 | glyph stability (F9) | glyph flicker | glyphs lag after switching device | mouse DPI |
| `pointerReclaimPx` | 24 px | 12–60 | KBM cursor hand-off (R8) | pointer steals the keyboard cursor | pointer feels ignored | `mouseFlipPx` |
| `aimSteadiness` (player) | 1.0 (off) | 0.25–1.0 | fine aim (F10) | too slow to track a moving catcher | — | `aimCamMult` |
| `throwMinAim` (player) | 0.25 s | 0.15–0.40 | twitch guard (F11) | blind whole-stack throws | quick lobs feel blocked | `trigOn` |
| `ringDelayMax` / `ringDelayFrac` | 0.10 s / 0.25 | 0.05–0.15 / 0.2–0.3 | ring noise on taps (F12) | ring flashes on taps | short holds lack feedback | `holdEff` |
| `backSkipSeconds` (player) | 2.0 s | 1.5–4.0 | how long the `no_room` skip stays offered (F13) | message gone before it is read | accidental skips | Back ladder |
| `catchArmSeconds` | 3.0 s | 1.5–5.0 | Toggle catch-armed latch (G7) | latch expires before the pass | a stale latch eats a later press | Throwing catch window |
| `pendingCommitTimeout` | 500 ms | 300–1000 | one-commit window after sending (G9) | a second commit races the first | follow-up throws feel blocked | host RTT |
| `padEdgeCollapseMs` (Steam double pad) | 20 ms | 10–50 | duplicate pad edges | double steps survive | real double-taps merged | Steam Input |

Knobs owned elsewhere and referenced here: `manualInsertHold` (Mail & Destinations / Content), deconstruct 0.5 s (Building), `minHoldTicks`, `rangeMm`, `incumbentSlackMm`, `aimCamMult` (Player Character), `insertPm` (Perks; clamp Content & Data), catch window, `catchTapMs`, `previewSettleMs` (Throwing & Catching), text scale (HUD H23).

## Visual/Audio Requirements

*Core-category system: this section is brief and gives behaviour only. It comes from the ux-designer and accessibility-specialist passes. **`art-director` was not consulted**: Visual/Audio is optional for Core, and the visual language is already locked in art bible §7. HUD & Notifications has adopted the ring, the slash and a focus bracket (HN-1e, answering OQ-IC-11). Motion follows art bible §7: squash, then pop ≤ 0.1 s, then hold. Under reduced motion, fills step in quarters and pops become a single outline flash.*

| Event | Visual | Audio / haptic |
|---|---|---|
| Hold starts | the glyph chip depresses at once (60 ms). The **ring** around the glyph chip in the interact prompt (under the reticle) becomes visible after `ringDelay` (F12), already showing true progress | none; light rumble tick on pad (scaled by the rumble setting, R10) |
| Hold progress | a thick clockwise ring with **tick marks at 25 / 50 / 75 %**. Chalk White on a Charcoal backing plate gives ≥ 3:1 contrast over any background, and progress is never shown by colour alone | rising tick (an added channel, not a replacement) |
| Hold completes | the ring snaps full and the chip pops. The ring promises "sent", not "succeeded": cha-ching or thud waits for the verdict (Manual Delivery) | one short rumble tick |
| Hold cancelled (release / Back) | the ring drains backward over 0.1 s | none |
| Hold cancelled (target lost / state change) | the ring dissolves (0.1 s fade) and the reason replaces the verb for 1 s | soft click-stop |
| Tap at a hold target | the word "Hold" in the prompt pulses once | none |
| Toggle-mode hold running | the same ring; the chip swaps to a stop mark labelled "[E] Cancel" | as above |
| Aim starts | the arc fades in over 0.08 s with "×N" and a "[Esc]/[B] Cancel" hint (arc and preview are owned by Throwing) | light tick |
| Early release (< `throwMinAim`) | arc vanishes; "Hold to aim" for 1 s | soft tick |
| Disabled verb | the glyph gets a diagonal slash and the reason replaces the verb ("Close panel to deliver", "Put it away first [Esc]"). Never 60 % opacity, which is reserved for pending ops | click-stop on press |
| One-commit "Busy" | "Busy" in the prompt slot while the commit is in progress or pending (G9), then "Release to retry" while the muted button stays down | none |
| Glyph set flip | prompts swap glyphs in place, with no animation | none |
| Controller disconnected | a banner, persistent until reconnect or dismissal | short two-note down |
| Focused target | HUD's focus bracket: Chalk corner carets on the focused interactable's bounds (HUD H11; Player Character supplies anchor and bounds) | — |

Every row also writes its text to the live-region Control (Grid Inventory OQ19; `Control.accessibility_live` typed `AccessibilityServer.AccessibilityLiveMode`, VERIFIED in breaking-changes.md). Announcements follow HUD H26: P0 items are assertive at once; everything else is polite, digested per category after `announceDigestMs` (1000 ms) and never dropped; items older than 10 s at flush collapse into "N earlier updates" (history keeps them).

📌 **Asset Spec**: Visual/Audio requirements are defined. After the art bible is approved, run `/asset-spec system:input-controls` to produce per-asset specs: glyph atlases for KBM, Xbox, PlayStation and Neutral; ring and tick art; the stop mark; the slash overlay; the banner; the three UI ticks.

## Game Feel

*Optional for a Core system. Included because input feel decides whether the fantasy test passes.*

- **Latency targets.** A tap verb is sent, or applied locally, on the frame its event arrives (R11). Movement reaches the local Sim within 1 tick + 1 frame (≤ 50 ms at 60 fps). Panel cursor moves render on the next frame. No tap ever waits for a release (G8).
- **The hold is a promise, not a wait.** At the default 0.4 s the ring is visible for 0.3 s. The hold exists so that a delivery is always meant. Toggle mode keeps the same window so that this promise survives.
- **Mastery (Challenge aesthetic).** By the third shift a carrier's thumb knows the route: take all, walk, hold, walk, hold, lob over the fence. The input layer serves this by never adding a step. The same route costs ≤ 11 inputs on either device (H-FX-3a). The action menu is a one-hop fallback, never the fast path on KBM.
- **No wobble in the hands.** All slapstick lives in outcomes: arcs, bonks, stumbles. Input has no randomness, no acceleration on mouse look, and no assist that knows targets (F10).
- **Navigation feel.** Panel stepping is 0.30 s / 0.08 s (F5). A bump at the edge, never an overshoot. Stick and D-pad feel identical because both use the game's own repeat.

## UI Requirements

*From ux-designer and accessibility-specialist. Screens are owned by Menus & Run Flow Screens (Controls settings) and HUD & Notifications (prompts). This section defines the rules those screens must follow.*

**Prompt grammar.**
- Tap prompts read "[glyph] Verb". Hold prompts read "**Hold** [glyph] Verb · object", and only hold glyphs carry the ring. In Toggle mode the word "Hold" is dropped.
- An optional leading **verdict mark** slot (✓ / ✗ / lock) precedes the glyph on line 1; its content belongs to the verb's GDD.
- Example (Manual Delivery owns the lines below): "✓ Hold [E] Deliver · 17 Elm St".
- At most 2 prompts show at once (1 at 200 % text, HUD).
- Prompts are always built from the **live** binding. Chords read "[Shift]+[Enter]". An unbound verb reads "Unbound — Settings › Controls".
- Prompt text is ≥ 18 px at 1080p at 100 % and follows HUD H23's text scale: reading text (prompts included) up to 200 %, layout-bound panels such as the action menu 150 % (spec 09 §5 amendment HN-14; 150 % stays the v1 floor).

**Glyphs.**
- The glyph set follows the last-used device (R9, F9).
- Sets: KBM with layout-correct labels, Xbox, PlayStation, Neutral. Steam Deck uses Xbox.
- Every glyph carries a text label ("E", "RMB", "X"), never a colour or button hue alone.

**Action menu (R7).** A vertical list anchored to the cell.
- It must render fully at 150 % text and 1280×720.
- It is reachable by pad (Y), keyboard (F, Shift+F10) and mouse (RMB on a cell with an empty cursor).
- A "⋯ Y" hint chip shows on the focused cell for pad players.
- On a pad player's first panel open, a footer legend shows: "{ActionMenu} Options · {QuickMove} Quick-move · {Rotate} Rotate".

**Controls settings screen (Menus & Run Flow Screens, M1).**
- **Layout:** tabs per device (KBM / Gamepad). Rows are grouped by context under player-facing names (On foot, Panel, Carrying, Aiming, Build, Mounted, Knocked down, Map, Menu); internal names (Dead, OnFoot, Holding-open / -closed) never reach players. Each row shows the action, its glyph, a "modified" dot, and primary plus secondary slots.
- **Binding capture:** press-to-bind (R10 Capture): Esc or holding Menu 1 s cancels; 5 s timeout.
- **Conflicts:** messages follow R10 ("E is already Interact (On foot). Swap · Replace · Cancel"). Conflicted rows show ⚠ with text, never colour alone.
- **Reset:** per action, per context and per device, each with confirm and one-step undo.
- **Hold settings:** the Hold time slider (0.2–1.0 s, step 0.05) with a "Try it" ring preview, and Hold/Toggle switches per class.
- **Other settings:** sensitivity, invert X / Y, rumble, deadzones and curve, trigger point, auto-repeat, minimum aim, aim steadiness, preview settle, glyph style, auto-pause in solo; Sprint offers Hold / Toggle / Auto. Face layout and swap sticks arrive at M4.
- **Access:** the screen is operable with keyboard only, gamepad only or mouse, and stays operable after any rebind (fixed menu navigation, R10). It has a search box (KBM text entry; pad players browse by group). Its layout respects text scale.
- **Exit check:** the screen will not close while a required action is unbound. It names the action instead.

**First-input moments (Onboarding & Tips; once each, never on a timer; `{Action}` tokens render the live binding on the last-used device, never a hard-coded glyph):**
- **First pad input:** glyphs swap, and HUD's tip slot reads "Controls: {Pause} › Settings".
- **First tap at a mailbox:** "Hold {Interact} to deliver". This dismisses on the first successful hold.
- **First early throw release:** "Hold to aim · {Back} cancels".
- **First holding with the panel closed:** "{Back} Put away · {Drop} Drop".
- **Three failed holds within 10 s:** a single hint, "Prefer press-to-start? Settings › Controls".
- **No device prompt at boot.**

> **📌 UX Flag — Input & Controls**: this system has UI requirements. In Pre-Production, run `/ux-design` for the Controls settings / remap screen, the interact prompt with its hold ring, and the action menu before writing epics. Stories should cite `design/ux/[screen].md`, not this GDD.

## Cross-References

| GDD | Section(s) referenced | What this GDD relies on or changes | Status |
|---|---|---|---|
| Grid Inventory & Containers (`grid-inventory.md`) | UI Requirements (contexts, control table, Esc order, keyboard-only nav), OQ19, OQ22, R11 cursor, reason codes | contexts, verbs and the two binding rules are owned there. Bindings, the router, the action menu and remap are owned here. Hand-backs in OQ-IC-10 | In Review; hand-backs pending |
| Player Character & Movement (`player-character-movement.md`) | P3 sprint, P9 camera (`aimCamMult` 0.6), P10 `IInteractable`, P11 focus and catch pre-emption, P12 holds, P14 Mounted/Dead contexts, F4 `holdTicks` / `holdScalePm` / `minHoldTicks` / `incumbentSlackMm` | focus scoring, the focus snapshot and the hold counter live there. Press grammar, F1 and toggles live here. OQ-IC-1 closed (PC OQ17: (a)–(i), (k) adopted; (j), (l) adapted) | In Review; D-3 Dead list adopted |
| Mail & Destinations (`mail-destinations.md`) | `DestinationTypeDef.manualInsertHold` (spec 08 §2.4) | base of F1 | In Design (`manualInsertHold` 0.4 s, safe 0.2–0.8) |
| Manual Delivery | deliver hold verb, `deliver_matching` (registry `deliverHoldRule`) | grammar and prompt rules apply to it; MD3 press-tick branch, `ShouldCancel`, ✓ / ✗ mark (OQ2 adopted) | In Review |
| Throwing & Catching | throw aim-release, catch window, arc preview | `throwMinAim`, Cancel aim, catch resolution order, no target-aware assist; T7 D3 gate on catch (OQ-IC-3 answered) | In Review |
| HUD & Notifications | prompt, ring, focus bracket, banners, tip slot, history (H21), live region (H26) | rendering of the Visual/Audio rows (HN-1 adopted) | In Review |
| Comms (`comms.md`) | H14 quick ping, comms wheel, chat | wheel liveness incl. Mounted and Dead (CQ-1) | Draft (frozen) |
| Menus & Run Flow Screens | Controls settings screen | UI Requirements rules | not yet written |
| Profile & Settings Persistence | `settings.input` schema (R10) | storage, per machine (OQ-IC-2) | not yet written |
| Accessibility | presets, audit | consumes R10 hooks | not yet written |
| Spec 09 §4 (input map), §5 | defaults and accessibility list | superseded where R6 differs: F / hold-X-1 s retired, Menu chat removed, ping split, R3 menu removed, Payday Skip removed, emote radial → comms wheel (OQ-IC-18) | spec |
| Spec 08 §2.4, §3.8 | `manualInsertHold`; `MetaProfile.settings` | F1 base; schema extension (OQ-IC-2) | spec |
| Spec 13 §3, §6 | client-authoritative movement; M4 "toggle interact" | input frame feeds the local Sim; toggles moved earlier (R12) | spec |

## Acceptance Criteria

*From qa-lead (2026-10-16). Tags: **[Logic]** = xUnit on the plain-C# router core (`tests/unit/input/<Rule>Tests.cs`), fed raw-input records and a fake clock, asserting frame bits and event-log lines; **[Integration]** = Godot headless through the adapter with injected `InputEvent*`, or across two systems; **[UI]** = Godot test on the UI event log plus a retained screenshot; **[MANUAL]** = tester on a build, evidence in `production/qa/evidence/`. Default fixture: `holdSetting` 0.4, every class in Hold mode (pad Sprint in Toggle), 30 Hz, press on tick 0. Parametrised ACs (lettered or listed rows) report each row as its own test case, so a failure names one row. **BLOCKED-ON-[GDD]** marks an AC written against a test double until that GDD exists (Throwing, Manual Delivery, HUD, Building, Networking). At `qa.level: full` every non-MANUAL criterion blocks Done, except millisecond wall-clock thresholds (H-PERF-1/2), which stay ADVISORY until the M1 reference machine exists. "Fires" = the verb's request or edge appears in the event log.*

### R1 — Action map
- **H-R1-1 [Logic] M-1a** **GIVEN** the loaded action map, **WHEN** validated, **THEN** every action has a unique id, exactly one grammar, a live predicate, KBM and pad defaults (or explicit "unbound"), reserved / required flags and an owner; a fixture missing any field fails to load and names the id.
- **H-R1-2 [Integration, CI] M-1a** **GIVEN** the gameplay source, **WHEN** the lint runs, **THEN** there are zero `Input.IsAction*` / `GetActionStrength` / `IsKeyPressed` / `IsJoyButtonPressed` reads outside the router adapter, and a planted violation fails CI.
- **H-R1-3 [Logic] M1** **GIVEN** an override stored under an aliased (renamed) id, **WHEN** the profile loads, **THEN** it applies to the new id.

### R2 — Contexts
- **H-R2-1 [Logic] M0** **GIVEN** TextEntry, **WHEN** W, V, E, Space are pressed, **THEN** `moveIntent` = 0 and no game edge fires.
- **H-R2-2 [Integration] M0** **GIVEN** an MP session with the stick at full, **WHEN** Pause opens, **THEN** ticks continue with `moveIntent` 0 and no game edges except push-to-talk, transforms keep sending and `SceneTree.Paused` stays false; in solo the Sim tick count stops.
- **H-R2-3 [Logic] M-1a** **GIVEN** Holding-open, **WHEN** Tab closes the panel and Y reopens it, **THEN** the cursor entry is unchanged and no `return` or `drop` is sent.
- **H-R2-4 [Logic] M-1a** **GIVEN** Panel, **WHEN** Shift+W, L3+LS, mouse, RS, 1–8 and the D-pad are used, **THEN** walk-speed `moveIntent` is present, Sprint and Look are not live, hotbar keys fire, and the D-pad moves the grid cursor.
- **H-R2-5 [Logic] M1–M2** **GIVEN** body Build or Mounted (any seat), **WHEN** Tab / Y is pressed, **THEN** no Panel opens ("Exit build first" / "Dismount first").
- **H-R2-5b [Logic] with knockdown** **GIVEN** body Dead, **WHEN** every R6 input is pressed, **THEN** exactly the comms wheel (Emotes page greyed), chat, PTT, History (M1), Status read (M4), Pause and Back fire; Look, quick ping, LMB / RMB / LB / RB, Tab / Y, Jump, Interact, Throw and Drop fire nothing; the remap screen labels the group "Knocked down".
- **H-R2-6 [Integration] M1** **GIVEN** Holding-closed at a bike seat, **WHEN** tap-Interact, **THEN** if `return` succeeds "Stowed" shows and the body becomes Mounted; on `no_room` mounting is refused with "No room to put it away". *(Pending hand-back OQ-IC-10.)*
- **H-R2-7 [Logic] M1** **GIVEN** the Payday overlay, **WHEN** A, Enter and the move stick are used, **THEN** no Skip action exists, A jumps, Enter opens TextEntry, movement stays live and the overlay auto-dismisses.
- **H-R2-8 [Logic] M-1a** **GIVEN** all 3 × 7 × 4 × 2 × 2 × 3 tuples, **WHEN** the reachable-state data predicate (R2) runs, **THEN** the reachable set equals the golden list `tests/unit/input/ReachableStates.golden`, and one negative fixture per clause is rejected (aiming with Panel, holding while Mounted, Panel while Mounted, History while Mounted, Dead with Map).

### R3 / Grammar G1–G8
- **H-R3-1 [Logic, every M-1a action] M-1a** **GIVEN** each action's grammar, **WHEN** presses whose release edge lands on tick 1, 6, 11, 12, 13 and 60 are applied, **THEN** fire ticks match R3: Tap on the press tick; Hold on tick 12 only when no release edge falls in ticks 1–12 (edges are processed before accrual, R11 — release on 11 or 12 cancels; on 13 or 60 the hold fired on tick 12); Aim-release on the release tick (if ≥ `throwMinAim`); Held state from press to release.
- **H-G1-1 [Logic] M-1a** **GIVEN** a deliver hold latched on mailbox A, **WHEN** focus moves to mailbox B at tick 6, **THEN** tick 12 delivers to A and the prompt names A throughout.
- **H-G1-2 [Logic] M-1a** **GIVEN** Aiming with hotbar slot 3 latched, **WHEN** 1–8 / D-pad / E / LMB are pressed, **THEN** none fires and the release throws slot 3's entry.
- **H-G2-1 [Logic, ×12] M-1a** **GIVEN** a deliver hold at tick 6 (an aim for the Back row), **WHEN** any of: early release, Back, target invalid, target destroyed, distance > 2900 mm, state change, focus loss, device disconnect, knockdown, swim, mount, a stub `ShouldCancel` reason, **THEN** progress = 0, nothing is sent, `hold_cancel{reason}` is logged and a cancel cue id is emitted. Damage at tick 6 does not cancel; the hold completes at tick 12.
- **H-G3-1 [Logic] M-1a** **GIVEN** X held at a mailbox, **WHEN** Y opens the panel and X is released, **THEN** no quick-move fires and the release is logged `swallowed`; A held from a jump when the panel opens picks nothing on release.
- **H-G3-2 [Logic] M-1a** **GIVEN** E held in open air, **WHEN** the carrier walks into mailbox range, **THEN** no hold starts until E is released and pressed again.
- **H-G3-3 [Logic] M-1a** **GIVEN** LS held, **WHEN** the state goes World → Panel → Holding-open → (Tab) Holding-closed → TextEntry, **THEN** `moveIntent` persists until TextEntry zeroes it (Map, Draft, turret seat and Dead zero it too, M2+).
- **H-G4-1 [Integration] M-1a** **GIVEN** E held with OS echo events at 30 / s for 1 s, **THEN** there is exactly one press edge; two Jump presses inside one tick give one edge.
- **H-G5-1 [Logic] M-1a** **GIVEN** Holding-open, **WHEN** RMB is held for 2 s, **THEN** exactly one `return` is sent, with no aim and no throw.
- **H-G6-1 [Logic] M-1a** **GIVEN** a deliver hold completing at tick 12, **WHEN** E stays down 60 more ticks while a second matching mailbox gains focus, **THEN** exactly one `deliver_matching` is sent.
- **H-G7-1 [Logic] M-1a** **GIVEN** Deliver in Toggle mode, **WHEN** a 1-tick press is made, **THEN** the hold completes at tick 12 with the button up; a second press at tick 8 cancels and nothing is sent.
- **H-G7-2 [Logic] M-1a** **GIVEN** Aim in Toggle mode, **WHEN** the first press is made, **THEN** Aiming starts with no timeout (still aiming at 30 s); a second press at 0.20 s is ignored (still aiming); a second press at 0.30 s throws; Back cancels.
- **H-G7-3 [Logic / UI] M-1a** **GIVEN** Sprint in Toggle mode, **THEN** one press latches, the next unlatches, and death, mount, swim and panel-open unlatch it; PTT Toggle shows a persistent "mic on" indicator that focus loss clears (M0, screenshot).
- **H-G7-4 [Logic] M0** **GIVEN** Catch and Deliver in Toggle mode, **WHEN** an open-air tap is made, **THEN** catch-armed latches and ends at 3.0 s, on a catch, or on any press; during a toggled deliver hold a second Interact press cancels it and never resolves as a catch; Back cancels a running toggled hold (ladder step 3b) with nothing sent, and that Back does not open Pause.
- **H-G8-1 [Logic, every Tap action in R6] M-1a** **GIVEN** a press at tick 0 and release at tick 30, **THEN** the verb fires in tick 0's frame and nothing fires at tick 30.

### R4 — Binding rules and Interact
- **H-R4-1 [Logic, table-driven] M-1a (rows added per phase to M3)** **GIVEN** every reachable `RouterState` × every physical input over the R6 tables, **WHEN** live actions per input are counted (including modifier use, after most-specific-chord resolution, Back counted as one action), **THEN** the count is ≤ 1 and no input carries a co-live Tap action and Hold action except Interact; each violation is reported as (state, input, actions).
- **H-R4-2 [Logic, parametrised] M-1a (catch row M0)** **GIVEN** (a) a teammate's lob predicted (T7) to reach the carrier, not the focused mailbox, (a′) the same lob predicted into the focused mailbox, (b) a container, (c) a mailbox, (d) no target, **WHEN** E is pressed, **THEN** (a) catch, (a′) a deliver hold (D3 gate), (b) Open on the press tick, (c) a deliver hold, (d) catch-armed while held; the prompts before the press read "Catch", "[E] Open", "Hold [E] Deliver".
- **H-R4-3 [Logic] M0** **GIVEN** a latched deliver hold, **WHEN** the catch window opens at tick 6, **THEN** `hold_complete` is logged at tick 12, no catch request and no `hold_cancel` are emitted, and the item lands normally (PC P11 adopted; PCM-P11-2 covers the Sim side).
- **H-R4-4 [Logic] M-1a** **GIVEN** a mailbox, **WHEN** a 1-tick press is made, **THEN** nothing is sent and one "Hold" pulse cue is emitted; a press with no target never starts a hold.
- **H-R4-5 [Logic] M-1a** **GIVEN** the v1 interactable definitions, **THEN** each declares exactly one verb with one grammar; a fixture with two fails validation.
- **H-R4-6 [Logic] M-1a** **GIVEN** the last presented prompt "Hold [E] Deliver" (mailbox A) and focus flipping to a container on the press tick, **WHEN** E is pressed, **THEN** a deliver hold latches on A (the presented snapshot, G1).

### R5 — Back ladder
- **H-R5-1 [Logic, parametrised] M-1a (menu row M1)** **GIVEN** (a) action menu open while holding, (b) aiming, (c) Holding-open with return ok, (d) Panel, (e) World, **WHEN** one Back is pressed, **THEN** (a) only the menu closes, (b) the aim cancels with no throw, (c) the entry returns and the panel stays open, (d) the panel closes, (e) Esc → Pause, B → nothing; Menu, and P on KBM, → Pause from every state.
- **H-R5-2 [Logic] M-1a** **GIVEN** every H-R5-1 and H-F13-1 fixture, **THEN** Back issues zero `drop` requests.

### R6 — Defaults and collision rulings
- **H-R6-1 [Logic, one row per R6 table line] M-1a–M3** **GIVEN** the default map, **THEN** each action's KBM binding, pad binding, grammar and live states equal its table row.
- **H-R6-2 [Logic / Integration, a–l = rulings 1–12] M-1a–M1** (a) Menu in World → Pause only; hold C → comms wheel. (b) Shift+LMB in Panel → quick-move at walk speed. (c) RMB / LT in Holding-closed → "Put it away first [Esc]" / "[B]" and no `return`; RMB in Holding-open → `return`. (d) = H-R4-3. (e) R3 in Panel → nothing; Y → action menu; R3 in World → quick ping. (f) [Integration] Y in Panel keeps the panel open, B closes it, Tab toggles it, Tab never moves Control focus, and no focused Control activates on a game input. (g) A → Jump in World and Holding-closed, Pick in Panel. (h) LMB → tool in World, pick in Panel, message in Holding-closed. (i) = H-F11-1. (j) Enter → TextEntry in World, pick / place in Panel; NumpadEnter identical. (k) MMB held 2 s → one quick ping, no wheel. (l) F in Panel → action menu; no "Deliver all" action exists in the map.

### R7 — Action menu [UI] M1
- **H-R7-1** **GIVEN** each focus type in the R7 table, **WHEN** the menu opens, **THEN** rows appear in table order, the first preselected, inapplicable rows greyed with a reason ("Stack of 1 can't split").
- **H-R7-2** **GIVEN** the menu open on stack S, **WHEN** another player takes S, **THEN** the rows grey; confirming a stale row shows `stale_entry` feedback.
- **H-R7-3** **GIVEN** F, Shift+F10, RMB on an empty-cursor cell, or Y, **THEN** the menu opens; B, Esc or the opening button closes it; at 150 % text scale every row is fully visible (screenshot).

### R8 — Navigation [Logic] M-1a (LB M0, header M1)
- **H-R8-1** **GIVEN** an 8-wide row containing a 2×2, **WHEN** the cursor steps right, **THEN** it lands on the item's origin only, does not wrap, bumps at the edge; a new push crosses to the nearest row; last cells are remembered per panel; LB switches panels.
- **H-R8-2** **GIVEN** the top row, **WHEN** ↑ is pressed, **THEN** the header (Sort, Take all) gains focus; an arrow press hands the cursor to the keyboard; 23 px of mouse travel leaves it there and 24 px reclaims it for the pointer.

### R9 — Devices and glyphs
- **H-R9-1 [Logic] M-1a** **GIVEN** pad Sprint in Hold mode and Shift and L3 both held, **WHEN** one is released, **THEN** sprint stays held; with LS at 0.4 and W at 1.0, `moveIntent` is W at magnitude 1.0.
- **H-R9-2 [Integration] M-1a** **GIVEN** key, mouse and joypad-0 events, **THEN** the device is identified via `DEVICE_ID_KEYBOARD` / `DEVICE_ID_MOUSE` and a keyboard event never reads as pad 0.
- **H-R9-3 [Logic] M1** **GIVEN** Glyph style set to Xbox / PlayStation / Neutral, **WHEN** F9 flip conditions occur, **THEN** the glyph set does not change.

### R10 — Remap and settings [Logic] M1
- **H-R10-1** **GIVEN** Throw being rebound on KBM, **WHEN** E is chosen, **THEN** it is blocked with "E is already Interact (On foot). Swap · Replace · Cancel" (same line to the live region); Swap exchanges; Replace leaves Interact "Unbound" and the screen will not close.
- **H-R10-2** **GIVEN** Pick / place on KBM, **WHEN** Space is added as secondary, **THEN** it is allowed with "Also Jump (On foot) — they never overlap"; adding Enter as a Jump secondary is blocked ("Enter is already Chat (On foot)").
- **H-R10-3** **GIVEN** Deconstruct (M2), **WHEN** E is chosen, **THEN** it is blocked with a tap / hold reason naming Build.
- **H-R10-4** **GIVEN** the remap screen, **WHEN** removing Esc from Back or Menu from Pause is attempted, **THEN** it is refused; adding secondaries succeeds. During capture, Esc or Menu held 1 s cancels, and 5 s without input cancels with no change.
- **H-R10-5** **GIVEN** any of the six required actions unbound on either device, **WHEN** Close is pressed, **THEN** the screen stays open and names the action.
- **H-R10-6** **GIVEN** a pad chord or a KBM chord of two non-modifier keys, **THEN** it is rejected; modifier + key is accepted.
- **H-R10-7** **GIVEN** a reset per action / context / device, **THEN** a confirmation is shown and one undo restores the prior bindings.
- **H-R10-8** **GIVEN** an untouched profile, **WHEN** saved, **THEN** `bindings` = {} and only changed settings are written; KBM bindings store physical keys by name.
- **H-R10-9 [Integration] M-1a** **GIVEN** the `deliver_matching` and throw request schemas, **THEN** neither has a hold-duration field and the throw request has no tick field.

### R11 — Router and frame
- **H-R11-1 [Logic] M-1a** **GIVEN** 90° of look over 3 ticks, **THEN** `lookYaw` holds absolute values and `steps[]` is int8; a Jump press then Tab inside one tick yields an ordered Jump edge gated against World (it jumps), then the panel opens.
- **H-R11-2 [Logic] M-1a** **GIVEN** a throw release event in frame F, **THEN** the throw request is enqueued in frame F, not at the next tick.
- **H-R11-3 [Integration] M-1a** **GIVEN** a release consumed by a UI node, **THEN** the router's raw held state still clears; `ignore_joypad_on_unfocused_application` is true; the mouse is captured in World and visible in Panel, Pause and TextEntry; with a Control focused, Space and Enter in World never activate it.
- **H-R11-4 [Logic] M-1a** **GIVEN** press, hold, state change, **THEN** the log holds raw input, gate decision, transition, hold start, cancel + reason in that order; the 4097th entry overwrites the oldest.

### R12 — Phasing
- **H-R12-1 [Integration] M-1a** **GIVEN** `user://input.json` with `holdSetting` 0.6 and Deliver = Toggle, **WHEN** the game boots, **THEN** both apply; a malformed file loads defaults and logs a warning.

### Formulas [Logic]

*Formula, edge-case, fantasy and performance criteria below are written compactly as "input → output": read each as **GIVEN** the stated input / fixture, **WHEN** the formula or scenario runs, **THEN** the stated output — every arrow is one independently checkable assertion.*

- **H-F1-1 M-1a / M2** `holdTicks`: default deliver 0.4 s → 12; Quick Hands at 0.4 → 6, at 0.6 → 9; deconstruct at 1.0 → 38; deliver base 0.8 at 1.0 → 60 (not clamped); Quick Hands at 0.2 → 6 (floor), at 0.45 → 7; `insertPm` 750 at 1.0 → 8; for every setting step 0.20–1.00 × `insertPm` 0 / 500 / 750 × base 100–2000 ms the int64 function equals exact rational ceil-then-clamp, and Player Character's hold calls the same function.
- **H-F2-1 M-1a** **GIVEN** a deliver hold, **WHEN** chest-to-anchor is 2900 mm, **THEN** the hold continues; at 2901 mm it cancels on that tick; a hold cannot start at 2501 mm; ContentValidator rejects `incumbentSlackMm` > `rangeSlackMm` − 100.
- **H-F3-1 M-1a** s = (0.6, 0) → move 0.5625, look ≈ 0.42; s = (0.14, 0) → 0; s = (0.5, 0.5) keeps its direction (no cardinal snap); a stored `dzInner` 0.9 loads clamped to 0.40 with `dzOuter` unchanged at 0.95.
- **H-F4-1 M-1a** LT rising to 0.55 → pressed; 0.40 → still pressed; 0.34 → released; with `trigOn` 0.15 the release point is 0.05.
- **H-F5-1 M-1a** a direction held: 699 ms → 6 steps, 700 ms → 7; bump; cross at 1150 ms (dwell from the first bump, not reset by repeat bumps) or on a fresh push at 710 ms; a 100 ms hitch emits at most one step that frame.
- **H-F6-1 M-1a / M1** 23 notches in one tick → hotbar +4 (slot 2 → 6); −23 → −4; stepper +10; the next tick with 0 notches adds 0; smooth-wheel fractions 0.4 × 3 emit 1.
- **H-F7-1 M-1a** 180 ms / 3 px → tap-lift; 120 ms / 40 px → place; 250 ms / 0 px → place, 249 ms → lift; at `uiScale` 2, 11 px → lift, 12 px → place.
- **H-F8-1 M-1a** Sprint Toggle: 7 neutral ticks keep the latch, the 8th unlatches; a 3-tick reversal keeps it; a 0.4 s stop drops it.
- **H-F9-1 M-1a** stick 0.6 for 50 ms → no flip, for 100 ms → flip; mouse 5 px → no flip, 30 px → flip to KBM (deferred to release during a hold); a second flip 0.3 s after the first applies at 0.4 s.
- **H-F10-1 M1** at `aimSteadiness` 0.5, 100 counts → the view moves as 30 would unaimed; a static check finds no world query in F10; the arc preview colour, text and landing outcome are identical with the setting on and off.
- **H-F11-1 M-1a** aim 249 ms → cancel, "Hold to aim"; 250 ms → throws; 160 ms → cancel; 300 ms → throws; at `holdSetting` 1.0, 250 ms still throws; with the `throwMinAim` setting at 0.15, 160 ms throws.
- **H-F12-1 M-1a** at default knobs: `holdEff` 0.4 → ring appears at 0.10 s showing 3 / 12; 0.2 → 0.05 s; 2.0 → 0.10 s; at the default setting a 2-tick tap never shows the ring.
- **H-F13-1 M-1a** Holding-closed with `no_room`: Esc → the message with "[Esc] again to skip"; Esc again 1.9 s later → Pause with the entry still held; after a WASD press clears the message (≥ 2.0 s), Esc shows it again; on pad, B shows the message naming [Menu] and a second B does nothing.

### Edge cases
- **H-EC-1 [Logic] M-1a** one-commit rule: E deliver hold at tick 4, then LT pressed → LT muted with "Busy", delivery completes at tick 12, LT's release throws nothing. During the same hold, Q / D-pad ↓ (Drop) at tick 6 → nothing fires, no `drop` sent; Space (Jump) at tick 6 → jump fires and the hold still completes at tick 12; quick ping, comms wheel and PTT at tick 6 fire with no "Busy". LT pressed at tick 14, before the host reply, is still muted (pending, G9); held on, it reads "Release to retry".
- **H-EC-2 [Logic] M-1a** E and pad X pressed together → one edge; the hold belongs to E; releasing E early cancels while X is still down; in Toggle mode X counts as the second press.
- **H-EC-3 [Logic] M-1a** press and release inside one tick → both edges in the frame; Jump fires once; a deliver hold starts and cancels with one "Hold" pulse.
- **H-EC-4 [Logic] M-1a** hitch of 3 catch-up ticks containing a release with `heldTicks` 10 / 12 → edges land on the first tick and the hold is **cancelled**; with 12 / 12 already reached before the batch, the hold completes once.
- **H-EC-5 [Integration] M-1a** focus loss mid-hold and mid-drag → releases synthesized, movement zeroed, every button muted; the drag entry stays on the cursor; the panel stays open; the recapture click is swallowed; solo auto-pauses.
- **H-EC-6 [Integration] M-1a** pad unplugged mid-aim (Hold and Toggle) → cancel, never a throw; solo auto-pauses if that pad was last-used; MP shows "Controller disconnected" and nothing pauses; reconnect with A held → no edge until A is released.
- **H-EC-7 [Logic] M0** Enter mid-hold cancels the hold; typing "e" produces no edge; after Esc a held E stays muted. Pause in MP while aiming cancels with no throw; resuming never throws.
- **H-EC-8 [Logic] M0** knockdown mid-aim → cancel; body Dead; buttons held through clock-in fire nothing.
- **H-EC-9 [Logic] M1** profile load: unknown ids dropped and logged; malformed → default; NaN → default; out-of-range clamped; two co-live overrides on one input → required wins, else canonical order, loser "Needs binding" with one notice; a new default colliding with an override loses; a required action left unbound resets that device to defaults with a notice; v1 → v2 migration runs.
- **H-EC-10 [Logic] M1** a binding applied while a key is down takes effect on the next press; the held key is muted.
- **H-EC-11 [Integration + MANUAL] M-1a** AZERTY: the physical W-position key moves forward and the prompt shows "Z"; number-row prompts read 1–8.
- **H-EC-12 [Integration] M-1a** a synthetic `InputEventAction` (device 0) while the last-used device is KBM leaves glyphs as KBM.
- **H-EC-13 [Logic] M-1a** duplicate pad edges on two pad ids 19 ms apart → one D-pad step; 21 ms apart → two.
- **H-EC-14 [Logic] M-1a / M1** Throw held 10 s throws on release; the comms wheel released 23 px from centre sends nothing and writes no feed line.
- **H-EC-15 [Logic] M1** binding Throw and Interact to the same key is blocked.

### Fantasy test
- **H-FX-1 [Logic] M-1a** over the input-replay corpus `tests/replay/input/` (200 seeded random traces plus 10 authored adversarial traces, logged bypass requests included), every stack leaving a bag in the log is caused by the same latched press as a completed deliver hold, a throw committed with `aimHeld` ≥ `throwMinAim`, or a Drop press; misfire count = 0.
- **H-FX-2 [MANUAL] M0** over ≥ 8 sessions of ≥ 30 min, each ending with "Did anything throw, deliver or drop when you didn't mean it? Y/N", every Y is checked against the event log; pass = 0 router-attributable misfires (evidence in `production/qa/evidence/`).
- **H-FX-3a [Logic] M-1a** every action has a pad binding or pad path except the exemption list (Chat — no pad text entry in v1, OQ-IC-5; Push-to-talk and Read prompt — unbound by default, remappable); Grid Inventory's H-FX-1 10-address route costs the same input count on pad as on KBM (≤ 11).
- **H-FX-3b [MANUAL] M0** ≥ 8 of ≥ 10 first-time gamepad players (Xbox-family pads at M0; every family from M1, R12) complete a correct delivery within 10 s of the prompt first appearing, with no help beyond glyphs (sample size PROPOSED, qa-lead).
- **H-FX-4 [Logic] M-1a (catch row M0)** a scripted run with all toggles on and every press ≤ 300 ms completes take, open panel, deliver, aim + throw, cancel aim and drop; a toggled aim with no second press for 30 s never throws; at M0 the same scripted player catches a teammate's lob using the Toggle catch-armed latch (G7).

### Performance
- **H-PERF-1 [Integration, ADVISORY] M1** replay a recorded 60 s mixed trace at 60 fps in a headless build on the reference machine, timing adapter + core with `Stopwatch` → p95 ≤ 0.1 ms per frame.
- **H-PERF-2 [Integration, ADVISORY] M1** same harness with accumulated input off and 16 mouse-motion events per frame for 600 frames → p99 ≤ 0.3 ms.
- **H-PERF-3 [Logic, BLOCKING] M-1a** after 300 warm-up frames, the `GC.GetAllocatedBytesForCurrentThread()` delta is 0 over 10,000 trace frames through the **core**; each event-log entry ≤ 64 B; the 4096-entry ring is allocated once. (Adapter allocations from C# `InputEvent` wrappers are UNVERIFIED — probe before extending the gate.)
- **H-PERF-4 [Integration] M0** a move press reaches the local Sim within 1 tick + 1 frame; reliable verbs are sent in the same frame (H-R11-2).

### Accessibility
- **H-A11Y-1 [Logic] M-1a** each hold class (Deliver / timed, Throw aim, Sprint per device, Comms wheel, PTT Open / Push / Toggle) has a toggle, and every verb in the class works under it.
- **H-A11Y-2 [Integration + MANUAL] M1** keyboard only (no mouse events): every Panel verb completes — pick, place, rotate, quick-move, split, both sorts, take all, drop, focus switch, header, close.
- **H-A11Y-3 [Integration + MANUAL] M1** the same flow with gamepad only.
- **H-A11Y-4 [Integration] M1** after Move, Pick / place and Interact are rebound to unusable keys, the remap screen is still fully operable with arrows / Enter / Esc and D-pad / A / B.
- **H-A11Y-5 [UI] M-1a** hold progress at 25 / 50 / 100 % is distinguishable in a greyscale screenshot (tick marks + fill shape, Visual/Audio).
- **H-A11Y-6 [UI] M1** remap conflicts, cancel reasons, "Needs binding" and "Controller disconnected" each send their exact text to the live region (tier and digest per HUD H26).

**Open flags from the qa-lead pass** (not yet resolvable): H-R2-6 is a pending hand-back (OQ-IC-10); the AZERTY label API, controller-family detection and Steam double-pad behaviour are UNVERIFIED, so the prompt halves of H-EC-11 / H-EC-13 stay MANUAL until the Steam / input spike (OQ-IC-9); H-FX-3b's sample size is a proposal; "every cancel is shown" can only be asserted as a cue id until HUD defines cue content.

## Open Questions

| # | Question | Owner | Target |
|---|---|---|---|
| OQ-IC-1 | **Closed 2026-10-16** (PC OQ17 reconciliation): (a)–(i) and (k) adopted by Player Character — F4 calls F1, `cancelHysteresis` = `incumbentSlackMm` (validator rule 9), comms wheel replaces the emote radial, Panel not openable while Mounted, `IInteractionQuery` presented snapshot, P12 owns the hold counter; (j) adapted (Menu = Pause, Shoulder swap X / RB, Sprint Auto; no Spectate); (l) adapted (`SetLook` on Teleport, mount, dismount). PC's D-3 Dead list is adopted here (R2 rule 5); Look-orbit and spectate in Dead are post-v1 | — | closed |
| OQ-IC-2 | `MetaProfile.settings` (spec 08 §3.8) must grow to hold `settings.input` (R10). Its single `sensitivity` is ambiguous between mouse and stick. Should input settings be per machine rather than roam with the profile? Recommended: per machine | Profile & Settings Persistence / technical-director | M1 |
| OQ-IC-3 | **Answered** by Throwing & Catching (OQ2(d), 2026-10-16), adopted: (a) `catchPredictMs` / `catchTapMs` 500, any arm capped at 90 ticks; (b) `throwMinAim` confirmed; (c) the arc is non-colour (pattern, silhouette, label); (d) catch stays on Interact; (e) request fields with no tick (R11); (f) Toggle latch honoured. T7's D3 gate is now R4.3 step 1 | — | closed |
| OQ-IC-4 | Sprint is not live with the panel open. Does this hurt raid escapes? Playtest at M3; the fallback is to allow sprint in Panel with Shift-as-modifier resolved by chord specificity | game-designer / playtest | M3 |
| OQ-IC-5 | Gamepad text chat: Steam on-screen keyboard, or canned comms-wheel messages only? Recommended: comms wheel only in v1 | ux-designer / Networking (Steam spike) | M0 |
| OQ-IC-6 | Map overlay zeroes movement because the left stick pans the map. Is that acceptable, or should KBM WASD still move the carrier? | ux-designer (Map & Address UI) | M2 |
| OQ-IC-7 | **Answered:** at `holdSetting` 0.2 Quick Hands (`insertPm` 500) is erased by the 6-tick floor — accepted (CD ruling ii, Manual Delivery MD9); the Perks card states the effective hold | — | closed |
| OQ-IC-8 | `mouseFlipPx` counts raw motion, so it depends on DPI. Should it be normalised by the sensitivity setting? | gameplay-programmer | M1 |
| OQ-IC-9 | **Engine spike (Input Router ADR, all UNVERIFIED).** To verify: `_Input` → GUI → `_UnhandledInput` order; polled-read behaviour under `set_input_as_handled`; GUI focus consuming `ui_accept` / `ui_cancel`; per-axis joypad motion vs polled stick reads; engine behaviour on focus-out; `joy_connection_changed`; `get_joy_info` keys and controller-family detection; the layout-label API (`keyboard_get_label_from_physical`); hot-plug index reuse; mouse-mode switch cursor restore; `parse_input_event` vs polled state (a local probe found it buffered by one frame); joypad events on `ui_*` with a real pad (headless shows none) and on `ui_accessibility_drag_and_drop`; C# name mapping (`DeviceIdKeyboard`, `UseAccumulatedInput`; no .NET SDK installed locally); Wayland mouse capture and the label API (headless: not supported); the `AccessibilityServer` screen-reader path (NVDA / Orca); Steam Input virtual-pad behaviour and glyphs; per-event allocation of C# `InputEvent` wrappers. **NOT SOURCEABLE** from `docs/engine-reference/godot/`; these must come from the 4.7 class reference or a .NET spike. Headless 4.7.1 probe (2026-10-16, non-Mono binary): `InputEvent` has no timestamp; keyboard / mouse / emulation device ids 16 / 32 / −1; `ignore_joypad_on_unfocused_application` default false; `use_accumulated_input` true; no joypad events on `ui_*`. Commit the probe scripts and output under `prototypes/` before citing them as VERIFIED | technical-director → ADR "Input Router" | before M-1a code |
| OQ-IC-10 | **Grid Inventory hand-backs** to its control table (this GDD does not edit grid-inventory.md): (1) action menu R3 → **Y** in Panel; KBM F / Shift+F10 / RMB-on-cell added; (2) Y no longer closes the panel; B closes via Back; Tab still toggles; (3) Sort by address View → action menu; (4) RMB = Cancel only in Holding-open; in Holding-closed RMB/LT show "Put it away first"; (5) Holding-closed: Tool not live; pad Drop of the cursor entry on D-pad ↓; jump/sprint live; (6) sprint and look not live in Panel; (7) mount/Build while holding → auto-`return`, refuse on `no_room`; (8) focused-cell Drop = atomic lift + drop; (9) Esc order gains steps (close menu/stepper, cancel aim) and the double-Back skip at `no_room` (F13); (10) "Close panel, keep holding" menu row; (11) D-pad in Holding-open jumps to the next fit position; (12) onboarding tip text ≥ 18 px (it says 16); (13) restate binding rule 2 as "no binding carries both a tap verb and a hold verb **for the same target** in the same context; Interact reaches several verbs only through target type (Input & Controls R4.2)" (CD-GDD-ALIGN finding 2); (14) Drop drops the **whole entry** as a WorldItem (R6 [D]) | Grid Inventory GDD (at its re-review) | Grid Inventory re-review |
| OQ-IC-11 | **Answered** by HUD & Notifications (HN-1e): ring, slash overlay and the focus bracket (H11) | — | closed |
| OQ-IC-12 | **Adapted from HUD HN-1e:** prompts follow HUD H23 (reading text to 200 %, layout-bound panels 150 %); 150 % stays the v1 floor until spec 09 §5's amendment (HUD HN-14) lands | ux-designer / producer (HN-14) | `/ux-design` HUD |
| OQ-IC-13 | Build-mode bindings (M2) are provisional. Y = belt tier; the panel cannot open in Build; the comms wheel shares D-pad ↑. PC P14 reads Build as "Interact reaching Deconstruct anchors"; this GDD binds Deconstruct to hold RMB / LT (spec 09). Confirm when Building is designed | Building System GDD | M2 |
| OQ-IC-14 | Should a completed deliver hold carry on to the next mailbox while the button stays down? Current rule: no (G6). Revisit only if playtests show hold fatigue on dense streets | game-designer | M1 playtest |
| OQ-IC-15 | Accessibility follow-ups (design-review 2026-10-16): swap sticks at M1 rather than M4; per-stick deadzones; a press debounce / ignore-repeat setting; one-handed cancel-aim on LMB / RT; a "Back pauses when nothing is left" option for adaptive pads; Hold time max 1.5 s; Toggle or auto-accelerate for throttle, handbrake and turret fire; an assertive live-region mode for errors | accessibility-specialist | M1 |
| OQ-IC-16 | A one-time quick setup before the first shift (Hold / Toggle, Hold time, glyph style)? | ux-designer (Onboarding & Tips) | M1 |
| OQ-IC-17 | Event-log ring sizing under 1000 Hz input; the adapter's bytes-per-event budget (C# `InputEvent` wrappers); a relative perf gate (≤ 2× the recorded CI baseline) replacing the nonexistent reference machine in H-PERF-1 / 2 | gameplay-programmer + technical-director | M0 |
| OQ-IC-18 | **Networking and spec hand-backs:** `pendingCommit` acks and `reqId` (G9); requests carry `transformSeq`; Pause without `SceneTree.Paused` plus a replicated "in menu" bit; client cooldowns and receiver caps for ping, emote and chat; a "Reconnecting" capture state on disconnect or host loss (pending commits cancelled, buttons muted on rejoin); bot input injected at action level, compiled out of release. Spec 06 §2 / §4.4: clients send intents and requests, not a per-tick input stream. Spec 09: Menu = Pause, comms wheel replaces the Menu radial, 150 % cap affirmed, Payday Skip removed | Networking & Session GDD; spec owners | M0 |
| OQ-IC-19 | **Rejected hand-back note:** Comms H14 lists Aiming among the wheel's live states; rejected — the wheel's stick / pointer would fight aim look, so R6 keeps "not aiming" (Comms to align when unfrozen). HUD's "pad View is Sort" is stale: Sort left View (OQ-IC-10 item 3); View is Map from M2 | Comms GDD (at unfreeze) | M0 |

**→ becomes an ADR ("Input Router")** (implementation, not design):
- node vs autoload, and process mode during pause;
- the single polling gate and its CI lint;
- the `ui_*` strip/replace strategy, and grid-focus ownership;
- edge latching and drain-at-tick, with catch-up ordering;
- the hold engine as a tick counter, with toggle state;
- the remap persistence schema, migrations and per-context validator;
- physical-keycode storage and label display;
- the device model (last-used state machine, noise filter, family detection and override, disconnect);
- mouse-mode transitions and focus loss;
- the test seam (`IInputSource` / raw-input records, fake clock) and engine smoke tests;
- the live-region announcer.
