# ADR-0014: Interaction Framework, Hold Timing and Carrier Life States

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
`/architecture-decision` (autonomous mode, `--review lean`) on the owner's standing instruction to plan autonomously; engine check by `godot-specialist` (Step 5.5). TD-ADR skipped (lean mode). Acceptance requires the project owner's explicit confirmation (batch request by the main session). Inputs: `design/gdd/player-character-movement.md` P10–P17, P19, F4, F5, F8, Network, ACs PCM-P10/P11/P12/F4; `design/gdd/input-controls.md` R4, G1–G3, G6, G7, R11, F1, F2, H-R3-1, H-F1-1, H-F2-1; `design/gdd/manual-delivery.md` MD1–MD6, F2; `design/gdd/throwing-catching.md` T7 (D3 gate); `docs/architecture/architecture.md` §Sim.Players, §Interaction, Appendix A; ADR-0003, ADR-0007, ADR-0009 (Accepted); ADR-0013 (Proposed).

**Accepted 2026-10-16** by `technical-director` on the project owner's explicit confirmation (owner answer "Accept all 8 (Recommended)" to the main session's batch request for ADR-0012, 0013, 0014, 0015, 0016, 0017, 0019 and 0026; acceptance mode `.claude/skills/architecture-decision/references/0-accept.md`, dependency order 0013 → 0014 → 0012 → 0026 → 0017 → 0015 → 0016 → 0019). Amended before acceptance: §3/§7, the diagram, Alternative 4 and the hold guideline now place focus, press and hold in ADR-0016's `Previews` sub-steps 4a/4d/4e.

## Summary
Every "press E on it" verb — deliver, open, pick up, catch, later mount and build — needs one registry, one integer focus rule, one press-latch-and-hold pipeline and one host validation chain, or each owner would re-invent focus, hold timing and range checks and they would disagree with the prompt the player saw. This ADR fixes the Sim-side `IInteractable` port and anchor registry with a dense 2 m spatial grid, integer focus scoring with a 4-tick dwell, the client-side press resolution (catch → focused verb → catch-armed, with Throwing's D3 gate), the hold counter in owner Sim ticks using the single shared `Hold.HoldTicks` (`holdEff`) function, the strict 2900 mm cancel range, the host's interaction validation chain for every range-gated op, and the host-owned life-state machine with absolute-tick timers and the phase-11 knockdown chain.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 .NET, C# |
| **Domain** | Core (plain-C# Sim; no engine surface) |
| **Knowledge Risk** | LOW. All of it lives in `PerformativeMail.Sim/Players/` (ADR-0001). The focus cone's `cosMinPm` is computed by the Presentation from the camera FOV and passed in as an integer (ADR-0012 `InputFrame`); prompt rendering is HUD's (ADR-0018). |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `current-best-practices.md` (nothing applicable) |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | None engine-side. PCM-P10/P11/P12, H-F1-1 and H-F2-1 run under xUnit. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0003 (phase 3 host role, phase 11 `CombatReact` reader, `ClientStepId.CarrierStep`, `I…Api` rules, no statics); ADR-0004 (`EntityId`, `PlayerSlot`, `StringId` reason keys); ADR-0006 (integer maths, `Fix.CeilDiv`/`ISqrt`, dump, 0-alloc); ADR-0007 (`Op`, reply reasons ≥ 64, Players OpKind block `0x0040–0x007F`); ADR-0008 (event payloads ≤ 48 B, readers, classes); ADR-0009 (`IInventoryApi.SpillToBag`/`Grant`/close, range-gated ops call `IPlayersApi`); ADR-0010 (`players` settings record, StatSheet `DeliverInsertPm`, `RespawnMs`); **ADR-0013** (chest point from `CarrierState`, `Sim/Geometry` occlusion primitives, `IWorldQuery`) |
| **Enables** | ADR-0012 (the router reads `IInteractionQuery` and the presented snapshot); ADR-0015 (implements the `ICatchPredictor` port; catch arming flows from press resolution); ADR-0016 (ClientLayer placement of focus/press/hold and the presented snapshot); ADR-0017 (Deliver anchors registered per destination); ADR-0020 (the F5 history ring behind `CheckInteraction`); ADR-0024 (hold settings persistence) |
| **Blocks** | Player Character epic: registry, focus, hold counter, life-state and knockdown stories (TR-movement-011..014); Manual Delivery: anchor, availability and hook stories (TR-delivery-001, 002, 007, 015); Input & Controls: `holdEff` and cancel-range stories (TR-input-011, 012) |
| **Ordering Note** | Accept after ADR-0013 and before ADR-0012 and ADR-0015. The F5 position-history ring is ADR-0020's (M0); the Sim-side ring may be pulled forward for M-1a in the shape architecture.md gives (9 + 3 sample-and-hold per slot). The knockdown chain needs Health & Damage (M-1b stub). |

## Context

### Problem Statement
Player Character P10–P12 defines an interaction framework that four GDDs plug into, and Input & Controls R4/R11 and Manual Delivery MD1–MD5 constrain it from both sides: the target is the one the player saw (G1), the hold is timed in the owning client's Sim ticks with one shared `holdEff` (F1, OQ-IC-1), the host has no hold timer, and a ✓ hold can never misdeliver (MD5). The host must validate every range-gated request in one fixed order before the owner's own checks (P13, ADR-0009 §validation). Life states and knockdowns (P15–P19) are host-owned timers that must survive pause and phase changes (absolute ticks). None of this has an architectural home yet; Appendix A maps TR-movement-011..014, TR-input-011..012 and TR-delivery-001, 002, 007, 015 here.

### Constraints
- ADR-0003: `ClientLayer` steps run `MirrorDrain → InputLatch → CarrierStep → Previews`; `ClientLayer` never reads `SimWorld`; calls go down the dependency map, and callees never call back into callers.
- Owners (Delivery, Grid, Throwing, later Vehicles/Building) depend on Players, so Players can only reach them through a port they implement.
- Integer-only, 0 B per tick, no `Dictionary` iteration (ADR-0006).
- Owner `CarrierStep` + focus p99 ≤ 0.10 ms with 2 000 registered anchors (PCM-PERF-1).

### Requirements
- TR-movement-011 (registry + 2 m spatial hash; one verb per anchor; integer focus; 4-tick dwell), TR-movement-012 (hold counter in owner ticks; one request at the held tick; no host hold timer), TR-movement-013 (host-owned life states with absolute-tick timers), TR-movement-014 (knockdown chain in phase 11 in fixed order, emits `CarrierKnockedDown`).
- TR-input-011 (`holdEff` one shared int64 Sim function, 6-tick floor, `holdCeil` clamp), TR-input-012 (cancel range 2900 mm, hysteresis = `incumbentSlackMm`).
- TR-delivery-001 (one Deliver anchor per destination, priority 2, never deregistered), TR-delivery-002 (ordered 6-reason availability; `deliver.pending` derived from the pending queue), TR-delivery-007 (`ShouldCancel` / `GetHoldTicks` hooks), TR-delivery-015 (`holdEff` with `insertPm` 500; `DeliverInsertPm` clamp 0…750).

## Decision

### 1. The `IInteractable` port and the anchor registry
- **`IInteractable`** is a port **defined by Players** and **implemented by each verb's owner** (Delivery, Grid, Throwing's catch has no anchor, later Vehicles, Building, Economy, …). Players calls only its **pure query hooks**: they read the owner's state (host state on the host, the owner's ClientLayer mirror on the client) and **must not mutate, raise events, draw RNG or allocate**. That keeps the dependency inversion consistent with ADR-0003 §5: a pure provider is not a callback.
- **Anchor identity:** `AnchorRef = (EntityId Entity, byte Slot)`. **Registration** `IPlayersApi.RegisterAnchor(in AnchorDef def, IInteractable impl) → RegisterResult` (phases 0–12 or at world build) and `UnregisterAnchor(AnchorRef)`. `AnchorDef = { AnchorRef, Vec3Mm Point, Verb, InteractMode Mode (Tap | Hold | Channel), int HoldBaseMs, int RangeMm (default 2500), byte Priority (0–3), AnchorFlags (AllowWhileSwimming, BuildModeOnly, SeatOf…) }`.
- **One verb per anchor per context** is enforced at registration in **every build**: a second `(AnchorRef, context)` registration returns `RegisterResult.DuplicateVerb` and writes an always-on `SimCheckRing` record (PCM-P10-1). Capacity `maxAnchors` (settings; 4096) — overflow returns `Full`, counted.
- **Spatial index:** a **dense uniform grid of 2 m cells** over `IWorldQuery.Bounds` (`int[] cellHead` + `int[] next` intrusive lists, allocated at world build; a 1 km map is 250 000 cells ≈ 1 MiB). Anchors are inserted at registration; moving anchors (vehicle seats, M1) call `MoveAnchor`. Queries visit cells within `CeilDiv(maxRegisteredRange + incumbentSlackMm, 2000)` of the chest point. Iteration order never affects results (the winner rule has a total tie-break), so no sort is needed.
- **Mailboxes (MD1):** Delivery registers one `Deliver` anchor per destination with `manualInsertHold > 0` at run start (`Hold`, priority 2, `RangeMm` 2500, slot 0) and never unregisters it.

### 2. Focus (client-only, F4, P11)
Computed once per client tick for the local carrier at the end of Players' `CarrierStep` binder (after `OwnedCarrier.Step`):
- `chest = feet + (0, chestMm, 0)` (1000); `d = ISqrt(Δx²+Δy²+Δz²)`, `dh = ISqrt(Δx²+Δz²)`; `cosPm = dh < nearAxisMm ? 1000 : MulDiv(dot(fwdQ10, Δxz), 1000, 1024·dh)` with `fwdQ10` from `InputFrame.LookYaw` (Q15 → Q10).
- Eligible iff `d ≤ RangeMm (+ incumbentSlackMm if incumbent)` ∧ (`cosPm ≥ InputFrame.FocusCosMinPm` ∨ `d ≤ nearMm`) ∧ context filter (Panel → display only; Dead → none; Mounted → own seat's Dismount only; Build → `BuildModeOnly` anchors only) ∧ not occluded. *(Erratum 2026-10-20: "own seat" = the `SeatOf` `Dismount` anchor whose `Ref == CarrierStatus.OccupiedSeat`; `None` → no candidates — see GDD Impact.)*
- `score = (wDist·d·1000/RangeMm + wAng·(1000 − cosPm))/1000 − 100·Priority − (incumbent ? stickyPm : 0)`; lowest wins; ties → lowest `EntityId`, then lowest `Slot`. A challenger takes focus on its **4th** consecutive leading tick (`focusDwellTicks`), immediately when there is no incumbent.
- **Occlusion** is tested lazily in score order until one candidate passes: the chest→anchor segment against `HeightfieldMarch` and static boxes other than the anchor entity's own (`SegmentIntervals.Slab`, ADR-0013 §6). Client-side only; the host has no line-of-sight check.
- **Focus is latched while interacting** (a hold or channel in progress, or toggle-armed): no challenger takes it and occlusion does not drop it; only §4's cancel list ends it.
- `IsAvailable` is evaluated for the focused anchor every tick; an unavailable target still shows (dimmed, reason key), never hidden.

### 3. Press resolution (Input R4.3, G1; Throwing T7 D3)
In `PreviewSubStep.EdgeResolve` (ADR-0016 §2) — after focus (4a) and the owners' previews (4b–4c) of the same tick — the tick's ordered `Interact` edges (ADR-0012 `InputFrame.Edges`; edges are processed **before** hold accrual, G4) are applied in order:
- **Press.** Resolution, first match wins:
  1. **Catch** iff `ICatchPredictor.Evaluate(…)` (port defined here, implemented by Throwing's client side, ADR-0015) predicts a non-own flight entering this carrier's catch sphere **on this tick**, **and** no hold is in progress, **and** the flight's predicted static receiver is not the presented target anchor (D3 gate; no off-hysteresis for preemption). → `Channel` start: the framework asks Throwing to send `CatchArm{down}`.
  2. **The presented target's single verb.** The target is the anchor named by the edge's presented-snapshot fields (G1: the prompt the player saw), if it is still registered, in `RangeMm` (start range, not the incumbent range — a press at 2501 mm never starts a hold, H-F2-1) and `IsAvailable` on this tick's state. For Hold verbs the owner's branch is judged now, on this tick (MD3/MD4 press-tick recompute through the pure hooks). `Tap` → build and send the request on this tick. `Hold` → latch. `Channel` → start.
  3. **No valid target** → catch-armed while held (`Channel` start with no anchor; Throwing sends `CatchArm{down}`).
  *(Erratum 2026-10-20, owner ruling: a swimmer's press follows this order unchanged; the host refuses the arm as ineligible — see GDD Impact.)*
  A press with a target that is unavailable fires nothing (prompt pulse, R4.4).
- **Release / Cancel edges** end a `Channel` (`CatchArm{up}`) and cancel an incomplete `Hold`. In Toggle mode the router has already translated presses into logical press/release (ADR-0012), so this layer never sees modes.
- **The latch** `InteractionLatch = { AnchorRef, Verb, Mode, PressTick, HoldTicks, ClaimedPosition (feet at press), OwnerPayload (16 B opaque for the owner: e.g. MD's branch kind + `selectedEntryId` + slot) }`. A latched deliver hold is never converted into a catch.

### 4. The hold counter (P12, the single owner) and `holdEff`
- **Counting convention (decided here).** `heldTicks = tick − PressTick`, evaluated after the tick's edges. The hold **completes on tick `PressTick + HoldTicks`** — so a 12-tick hold pressed on tick 0 sends on tick 12, and a release edge on tick 12 (processed first) cancels. This makes the elapsed hold exactly `holdEff = HoldTicks/30` s and matches Input & Controls H-R3-1. Player Character PCM-P12-1's "press tick = held tick 1" (completion one tick earlier) is superseded; see GDD Impact.
- **On completion** exactly one request is built (`IInteractable.TryBuildRequest(in latch, ref Op)`) and sent through the ClientLayer request port; the hold enters `Complete` and does not repeat (G6). No request carries a hold duration; the host has **no hold timer**.
- **Cancels, checked every held tick up to and including the completion tick** (after edges, before completion): release / cancel edge; chest→anchor distance² **strictly greater** than `(RangeMm + incumbentSlackMm)²` (2900 mm continues, 2901 cancels — Input F2's `holdCancelRange`, whose `cancelHysteresis` *is* `incumbentSlackMm`, one value); anchor unregistered or `IsAvailable` false; `ShouldCancel(in latch)` returning a reason (MD4); context change into Panel, Mounted or Dead; swim depth; knockdown. Walking and non-lethal damage never cancel. Each cancel records its reason in the snapshot (shown, never silent).
- **`Hold.HoldTicks` — the one shared `holdEff` function** (`Sim/Players/Hold.cs`, pure, T2):
  ```
  insertPm   = clamp(insertPm, 0, maxInsertPm (750))           (StatSheet already clamps DeliverInsertPm; defensive, counted)
  settingMs  = clamp(holdSettingMs, 200, 1000)                  (multiples of 50)
  num  = (long)holdBaseMs × (1000 − insertPm) × settingMs × 30
  den  = 1000L × holdDefaultMs (400) × 1000
  raw  = Fix.CeilDiv(num, den)
  ceil = Fix.CeilDiv((long)max(holdBaseMs, holdCeilMs (2000)) × 30, 1000)
  return clamp(raw, minHoldTicks (6), ceil)
  ```
  `GetHoldTicks(latch)` defaults to it; the result is latched at press (G1). Player Character F4's copy is retired; Manual Delivery F2 is a restatement, not a copy.

### 5. Host validation chain (P13) for every range-gated request
- `IPlayersApi.CheckInteraction(ActorId actor, AnchorRef anchor, in ClaimedPosition claim) → InteractionCheck` returns the **first** failure in this fixed order: `Dead` → `Frozen` → `NoTarget` (anchor unknown) → `Unavailable(reasonKey)` (`IsAvailable` on host state) → `PositionStale` / `OutOfRange` (F5: the claim's chest within `RangeMm + rangeSlackMm` of the anchor **and** the claim within `claimToleranceMm` of a history sample or the dead-reckoned sample; stale if nothing received for `positionStaleTicks`).
- Op handlers that need it (ADR-0009's `Open`, `Pickup`, `DeliverMatching`; later Mount, Shop, …) call it first and map the result to their own `(OpKind, Reason)` codes. It is read-only and legal in any phase 0–12.
- **The host range is the anchor's one `RangeMm` plus `rangeSlackMm`.** There is no second per-verb host range. The F5 ring and dead-reckoning are ADR-0020's; the predicate and its order are fixed here.
- `ClaimedPosition` = feet `int x, y, z` mm (12 B, Player Character P13 layout), carried in every range-gated payload (ADR-0009 payload tables).

### 6. Life states (host-owned, absolute ticks) and the knockdown chain
- **State per player** (host `SimWorld`, Players' dump block): `Life ∈ {Joining, Alive, Dead, DeadAway, Frozen, Despawned}`; deadlines as **absolute ticks** (`ClockInTick`, `FrozenUntilTick`, `ProtectedUntilTick`); `TeleportSeq` (u8, wrap-safe). Phase changes, Prep/Payday and solo pause never move a deadline (pause stops ticks; ADR-0002).
- **Phase 3 (`PlayersStep`, host role)** processes, in slot order: transport-driven transitions queued by `System` ops (disconnect → Frozen/DeadAway; reconnect), then timers: `ClockInTick` reached → Spawn-pad slot (F8 rotating ring) → `Alive` + `ProtectedUntilTick` → `IPlayersApi.Teleport{seq+1, Respawn}` → standard-issue `Grant` (P18); `FrozenUntilTick` reached → `SpillToBag` → `Despawned` + `PlayerDespawned`. Then the host-role solver step for authored bodies (ADR-0013 §1).
- **Knockdown chain (phase 11, `CombatReact`, Players first among reactors):** a pull reader over `PlayerDied` (ADR-0008). For each entry, in log order, for a carrier that is `Alive`: (1) mark any interaction cancelled host-side (client holds end through the life-state event; Throwing clears catch arms by reading life state at the start of its next step — no upward call); (2) close the external container (`IInventoryApi`); (3) eject from a seat (Vehicles port, M1); (4) `IInventoryApi.SpillToBag(slot, bagPosition)` (swim-depth rule P15); (5) `Life = Dead`, `ClockInTick = tick + respawnTicks`, collider off, Postal Sprint cleared; (6) raise `CarrierKnockedDown` (bumps `TeleportSeq`). A second `PlayerDied` for a non-Alive carrier is ignored (counted).
- **Events (Players block `0x0040–0x007F`, class `Replicated`, ≤ 48 B):** `CarrierKnockedDown {playerId u32, pos 3×i32, teleportSeq u8, causeKind u8, hasPile u8, bagId u32, bagPos 3×i32, bagExpiresTick u32}` = 39 B; `LifeStateChanged {playerId, life, remainingTicks u32, protected u8}`; `PlayerFrozen`; `PlayerDespawned`.

### 7. Where it runs, and what it publishes
- **Client (every client, owner role):** Players' `IClientStep` at `ClientStepId.CarrierStep` runs only `OwnedCarrier.Step` (ADR-0013). Interaction runs inside ClientLayer's `Previews` composite (ADR-0016 §2): Players binds focus (§2) at `PreviewSubStep.Focus` (4a); the owners' previews and trackers (Manual Delivery's `DeliverPreviewTracker`, Throwing's prompt hysteresis) run at 4b–4c; press resolution (§3) runs from `PreviewSubStep.EdgeResolve` (4d) on this tick's 4a–4c state; hold accrual and cancels (§4) run at `PreviewSubStep.HoldAdvance` (4e), which then publishes the **interaction snapshot**. This is Manual Delivery's required order (events → focus → preview → press judged on the press-tick branch, TR-delivery-005). The hooks stay pure. Example: a MISDELIVER_ONE settle check computes `displayedTicks = 0` whenever the press-tick branch key differs from the tracker's last key. *(Amended 2026-10-16 before acceptance: the draft ran focus → press → hold at the tail of `CarrierStep`, which contradicted ADR-0016 §2. ADR-0016 governs the sub-step placement; §2–§4 semantics are unchanged.)*
- **`IInteractionQuery`** (read-only, Sim-defined, consumed by the router (ADR-0012) and HUD): `InteractionSnapshot { uint Tick; ushort Seq; AnchorRef Focused; Verb; InteractMode; Availability (ok | reason StringId); PromptArgs (owner-supplied, 32 B); InteractState (Idle | Holding | Channel | ToggleArmed | Complete); short HeldTicks; short HoldTicks; ushort ProgressPm; AnchorRef Latched; CancelReason LastCancel; CatchPrompt (from ICatchPredictor) }`. `Presented` (the snapshot the Presentation last rendered) is tracked by ADR-0016; press edges carry its `Seq`.
- **Host:** `CheckInteraction` (§5), registry, life states and knockdown (§6). The host does not run focus or holds for remote players.

### Architecture Diagram
```
 ClientLayer (owner)                                        SimWorld (host)
  InputLatch ─ InputFrame{edges, FocusCosMinPm} (ADR-0012)   phase 0  op handlers ── IPlayersApi.CheckInteraction
  CarrierStep; Previews 4a/4d/4e:                                       (dead→frozen→no_target→unavailable→range/stale)
    OwnedCarrier.Step (ADR-0013)                              phase 3  life-state timers (absolute ticks), Teleport,
    Focus: 2 m dense grid → F4 score → dwell 4 → occlusion            host-role solver for Dead/Frozen
    Press: Catch(ICatchPredictor, D3) → presented verb → armed phase 11 PlayerDied reader → knockdown chain → CarrierKnockedDown
    Hold:  heldTicks = tick − PressTick; cancels; complete at
           PressTick + Hold.HoldTicks(...) → TryBuildRequest → request port → loopback/ClientAdapter → op ring
    publish InteractionSnapshot ──► IInteractionQuery ──► router (ADR-0012), HUD (ADR-0018)
  Previews: owner previews / trackers (ADR-0016)
 IInteractable (port; owners implement pure hooks): IsAvailable, GetHoldTicks, ShouldCancel, PromptArgs, TryBuildRequest
```
*Placement (ADR-0016 §2): `CarrierStep` runs `OwnedCarrier.Step`; Focus is `Previews` 4a, Press is 4d `EdgeResolve`, Hold is 4e `HoldAdvance`.*

### Key Interfaces
```csharp
namespace PerformativeMail.Sim.Players;
public readonly struct AnchorRef { public readonly EntityId Entity; public readonly byte Slot; }
public enum InteractMode : byte { Tap, Hold, Channel }
public interface IInteractable                                   // implemented by verb owners; every member pure
{
    bool IsAvailable(in InteractionContext c, out StringId reason);
    int  GetHoldTicks(in InteractionLatch latch, in InteractionContext c);     // default: Hold.HoldTicks(...)
    bool ShouldCancel(in InteractionLatch latch, in InteractionContext c, out StringId reason);
    void WritePrompt(in InteractionContext c, ref PromptArgs args);
    bool TryBuildRequest(in InteractionLatch latch, in InteractionContext c, ref Op op);   // client only
    void OnPress(in InteractionContext c, ref InteractionLatch latch);         // fills OwnerPayload (press-tick branch); pure w.r.t. owner state
}
public interface IPlayersApi
{
    RegisterResult RegisterAnchor(in AnchorDef def, IInteractable impl);
    void UnregisterAnchor(AnchorRef a); void MoveAnchor(AnchorRef a, in Vec3Mm p);
    InteractionCheck CheckInteraction(ActorId actor, AnchorRef anchor, in ClaimedPosition claim);   // read-only
    void Teleport(PlayerSlot p, in Vec3Mm pos, ushort yaw, TeleportReason r);
    LifeState GetLifeState(PlayerSlot p);
}
public interface ICatchPredictor { CatchPrediction Evaluate(in CatchQuery q); }   // implemented by Throwing (ADR-0015); pure
public interface IInteractionQuery { ref readonly InteractionSnapshot Current { get; } }
public static class Hold { public static int HoldTicks(int holdBaseMs, int insertPm, int holdSettingMs, in HoldSettings s); }
public readonly struct ClaimedPosition { public readonly int X, Y, Z; }   // 12 B, P13 layout
```

### Implementation Guidelines
- Every interactable verb **must** register through `IPlayersApi.RegisterAnchor`; a second verb on one anchor in one context **must** return `DuplicateVerb` in every build.
- `IInteractable` hooks **must** be pure: no mutation, events, RNG or allocation. Owner effects **must** happen only through requests (ops) or the owner's own phases.
- Focus **must** use the §2 integer formula, the total tie-break and the 4-tick dwell; the cone threshold **must** come from `InputFrame.FocusCosMinPm`, never from a Godot call.
- Press resolution **must** follow §3's order with the D3 gate, against the presented target carried by the edge.
- Holds **must** be counted only by Players' `PreviewSubStep.HoldAdvance` binding (ADR-0016 §2) in owner Sim ticks with `heldTicks = tick − PressTick`; edges **must** be processed before accrual; the host **must never** time a hold or receive a duration.
- `holdEff` **must** exist once, as `Hold.HoldTicks`, evaluated in `long` with one `CeilDiv`; every caller (Player Character, Manual Delivery, HUD ring, tests) **must** call it.
- The cancel range **must** be `RangeMm + incumbentSlackMm`, compared strictly (`>`) on squared integer distances.
- Range-gated op handlers **must** call `CheckInteraction` first and map its result; the host range **must** be the anchor's `RangeMm + rangeSlackMm`.
- Life-state deadlines **must** be absolute ticks; nothing may decrement a timer per tick.
- The knockdown chain **must** run in phase 11 in §6's order as a pull reader; Players **must not** call Throwing or other dependants upward.

## Alternatives Considered

### Alternative 1: Host-timed holds (client sends press and release; host counts)
- **Description**: The host starts a timer on a press request and completes the verb when the timer elapses.
- **Pros**: The host is authoritative over hold length.
- **Cons**: Adds RTT to every hold (a press-to-complete delay the client cannot hide); needs host-side cancel tracking for range/focus that only the client sees; contradicts spec 06 §4.3, Input R10 ("the server never checks a duration") and P12; doubles messages.
- **Rejection Reason**: Worse feel and more protocol for a friends-only trust model that does not need it.

### Alternative 2: A `Dictionary<(int,int), List<Anchor>>` spatial hash
- **Description**: Hash cells on demand.
- **Pros**: Memory proportional to used cells.
- **Cons**: Allocates on insert, iteration order is undefined (banned in state code by ADR-0006), and lookups cost more than an array index.
- **Rejection Reason**: The dense 2 m grid is ≤ 1 MiB even on a 1 km map, is allocation-free and is deterministic.

### Alternative 3: Re-target holds to the current focus (no latch)
- **Description**: The hold follows whatever is focused when it completes.
- **Pros**: Simpler state.
- **Cons**: Violates G1 and MD5 ("a hold that started on a ✓ can never misdeliver"); a bump or a passing teammate changes what is delivered.
- **Rejection Reason**: Breaks the honest-latch promise of three GDDs.

### Alternative 4: Add a `ClientStepId.Interaction` between `CarrierStep` and `Previews`
- **Description**: Amend ADR-0003's client step list so focus/press/hold get their own step after the previews.
- **Pros**: Makes Manual Delivery's per-tick order visible in the step table.
- **Cons**: Amends an accepted ADR. The order is already achievable with pure on-demand hooks. A separate step would still need the previews to run between focus and press, which needs a fifth step.
- **Rejection Reason**: Unnecessary. ADR-0016 §2 meets the order with `PreviewSubStep`s inside the existing `Previews` step (§7 as amended).

## Consequences

### Positive
- One focus rule, one hold function, one validation order: the prompt, the ring and the host agree by construction.
- Owners add verbs by registering anchors and implementing pure hooks; no owner times holds or checks range.
- Life-state timers survive pause and phase changes, and knockdowns are ordered within the tick without subscribers.

### Negative
- The pure-hook rule forces owners such as Manual Delivery to compute the press-tick branch on demand (≤ 26-entry scans) rather than reading a cached tracker. This is negligible, but it is a discipline.
- The dense grid's memory scales with map area, not anchor count.
- The hold-counting convention differs from PCM-P12-1's wording (one tick later), which needs a GDD amendment.

## Risks
- **Pure hooks accidentally mutate owner state** (for example a tracker updated inside `IsAvailable`). *Mitigation:* an architecture test asserts that `IInteractable` implementations take only `in`/read-only parameters and that their owner state is mutated only in the owner's bound steps; code review checklist item.
- **Occlusion cost spikes with many candidates.** *Mitigation:* lazy, score-ordered testing stops at the first pass; PCM-PERF-1 counts occlusion tests ≤ candidates in range.
- **`ICatchPredictor` cost at press time.** *Mitigation:* it runs only on press ticks and for the per-tick prompt (Throwing's own budget, TC-P-1).
- **F5 ring not ready at M-1a.** *Mitigation:* M-1a solo runs through loopback; the Sim-side ring is pulled forward in the architecture.md shape; ADR-0020 may change replication, not the predicate.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| player-character-movement.md | P10, P11 / TR-movement-011: registry + 2 m spatial hash; one verb per anchor; integer focus; 4-tick dwell | §1 registry and dense grid; §2 focus |
| player-character-movement.md | P12 / TR-movement-012: hold counter in owner ticks; one request at the held tick; no host timer | §4 |
| player-character-movement.md | P15–P19, F8 / TR-movement-013: host-owned life states with absolute-tick timers | §6 |
| player-character-movement.md | P15 / TR-movement-014: knockdown chain in phase 11, fixed order, `CarrierKnockedDown` | §6 |
| player-character-movement.md | P13, F5: host validation order and range | §5 |
| input-controls.md | F1 / TR-input-011: `holdEff` one shared int64 Sim function, floor 6, ceiling `holdCeil` | §4 `Hold.HoldTicks` |
| input-controls.md | F2 / TR-input-012: cancel strictly beyond 2900 mm; hysteresis = `incumbentSlackMm` | §4 cancels |
| input-controls.md | R4.3, G1: Interact resolution order; latched presented target | §3 |
| manual-delivery.md | MD1 / TR-delivery-001: one Deliver anchor per destination, priority 2, never deregistered | §1 |
| manual-delivery.md | MD2 / TR-delivery-002: ordered availability reasons; `deliver.pending` derived | §1 `IsAvailable` port (Delivery implements rows 1–6) |
| manual-delivery.md | MD4 / TR-delivery-007: `ShouldCancel` / `GetHoldTicks` hooks checked every held tick | §3, §4 |
| manual-delivery.md | MD9, F2 / TR-delivery-015: `holdEff` with `insertPm` 500; clamp 0…750 | §4 `Hold.HoldTicks` |
| throwing-catching.md | T7 D3 gate on catch preemption | §3 via `ICatchPredictor` |

## Performance Implications
- **CPU**: Focus visits ≤ 25 cells and scores ≤ tens of candidates per tick, with ≥ 1 occlusion test; inside the 0.10 ms owner budget shared with the solver. `CheckInteraction` is O(history 12) per request on the host.
- **Memory**: anchor table 4096 × ≈ 48 B ≈ 192 KiB; dense grid ≈ 4 B per 4 m² cell (≤ 1 MiB at 1 km²); per-player life state ≈ 32 B.
- **Load Time**: Grid allocation at world build only.
- **Network**: `CarrierKnockedDown` 39 B, life-state events ≤ 12 B; no focus or hold replication (the `interacting` bit is in the transform, ADR-0020).

## Migration Plan
No code exists. Stories create `Sim/Players/{AnchorRegistry, Focus, Interaction, Hold, LifeStates, KnockdownReactor}` and the `IPlayersApi` methods. `Hold.HoldTicks` ships first, with the shared H-F1-1/PCM-F4-2 vector table.

## Validation Criteria
- PCM-P10-1/2, P11-1, P11-2a–d, P12-1 (with the §4 convention), P12-2, F4-1, F4-2 pass.
- H-F1-1 and H-F2-1 pass against `Hold.HoldTicks` and the strict cancel compare; H-R3-1 Hold row passes (release on tick 12 cancels).
- H-R4-2/4/5/6 and H-G1-1, H-G2-1 pass through the router (ADR-0012) and this framework together.
- Registering two verbs on one anchor fails in Release.
- 0 B allocated per tick with 2 000 anchors registered (PCM-PERF-1).

## GDD Impact (no GDD edited by this ADR)
- **player-character-movement.md P12 / PCM-P12-1 vs input-controls.md H-R3-1 (one-tick contradiction).** PC counts the press tick as held tick 1, so completion is on `PressTick + HoldTicks − 1`. Input's H-R3-1 has a hold pressed on tick 0 fire on tick 12, with a release on tick 12 cancelling. This ADR adopts Input's convention (`heldTicks = tick − PressTick`, completion on `PressTick + HoldTicks`) because it makes the hold last exactly `holdEff`. PCM-P12-1 should read "a 400 ms Hold pressed on tick 0 sends one request on tick 12, none on tick 11".
- **player-character-movement.md F5 variable `r` ("3000 (deliver, spec 06 §4.3)").** Superseded by the anchor's single `RangeMm` (Deliver 2500, MD1) + `rangeSlackMm` 500 = 3000 on the host, which matches Input F2's invariant. Remove the per-verb 3000 row.
- **input-controls.md R11 / Dependencies ("the input frame is drained by `Inputs.Apply(tick)` on the owning client")** — the frame is consumed by ClientLayer `InputLatch`/`CarrierStep` (ADR-0003 §6), not phase 0 (also recorded in ADR-0012).
- **manual-delivery.md MD3 per-tick order:** satisfied by pure press-tick hooks plus the `Previews` tracker (§7); ADR-0016 records the tracker detail.
- **Erratum 2026-10-20 (owner decision — accepted by the project owner, Evan, 2026-10-20; drafted by the planning lead) — the Mounted focus filter's "own seat" (player-character story 013 OQ-013-1).** §2's context filter reads "Mounted → own seat's Dismount only" and §1 flags seat anchors with `AnchorFlags.SeatOf`, but nothing in this ADR, the GDD or the code named the seat a carrier occupies. **Resolution.** The ClientLayer-published carrier status — `CarrierStatus`, the struct ADR-0012's router reads (§2; through `ICarrierStatusSource`, Key Interfaces) (declared by player-character story 013, published each client tick by story 020; this ADR does not otherwise define it) — carries **`AnchorRef OccupiedSeat`**: the `AnchorRef` (§1 anchor identity) of the `Dismount` anchor, registered with `SeatOf`, of the seat the carrier occupies, and `AnchorRef.None` whenever the body is not Mounted (the status builder normalises it). It is a builder input supplied by Vehicles' client-side mount state (M1); at M-1a it is always `None`. **§2 Mounted filter, as amended:** a candidate is eligible iff `Verb == Dismount`, `(Flags & SeatOf) != 0` and `Ref == carrierStatus.OccupiedSeat`; `OccupiedSeat == None` or any mismatch yields **no candidates** (never a fallback to another seat; a `None` ref can never be registered, so the compare is safe by construction). **§1 is otherwise unchanged:** the registry has only the World and Build context masks and one verb per `(AnchorRef, context)`, so a seat's Mount and Dismount anchors need distinct `AnchorRef`s or a Mounted registration context — Vehicles (M1) decides, and registers the Dismount anchor under the ref it reports as `OccupiedSeat`. The amended filter constrains only the Mounted context: while the registry has only World/Build masks, a `SeatOf` Dismount anchor registered in World is otherwise a candidate for an on-foot carrier, so Vehicles must keep Dismount out of on-foot focus (its `IsAvailable`, or a Mounted registration context). **GDD:** player-character-movement.md P10 Dismount row ("your own seat") and the Network table's `vehicleId`, seat row now mean this `AnchorRef` (wire packing stays ADR-0020's); Open Questions row 14 (Vehicles contract) gains "register one `SeatOf` Dismount anchor per seat under the `OccupiedSeat` ref" — owner applies at re-review. **Stories:** player-character 013 (`CarrierStatus` AC, PCM-P11-2d, OQ-013-1 closed, TD-106 resolved), 016 (Mounted press follows the own-seat gate) and 020 (publishes `None` at M-1a).
- **Erratum 2026-10-20 (owner ruling — Evan, 2026-10-20 22:05 PT; recorded by the planning lead) — a swimmer's Catch press.** §3 did not say how a swimming carrier's press resolves. **Ruling: keep the existing design.** The press order is **unchanged** for a swimmer: the client applies rules 1–3 as for any carrier and, on rule 1 or 3, sends `CatchArm{down}` — there is **no client-side suppression and no press-order exception**. The host refuses the arm as ineligible (ADR-0015 catch-arm eligibility "not swimming"; throwing-catching story 022 "Eligibility on arming": no arm, counted `armIneligible`, reply `ok`); Throwing T14 records the press cause `ineligible` (swim); and the swimmer sees "Dry land required" (throwing-catching.md "Who may catch" table and result lines, routed by HUD). **Stories:** player-character 016 (PC016-SWIM-CATCH matches story 022).

## Engine Validation (Step 5.5, godot-specialist, 2026-10-16)
**Verdict: no BLOCKING findings; no engine surface.** Minor notes, adopted:
1. The dense spatial grid (≤ 1 MiB `int[]`) lands on the large object heap. It is allocated once at world build and cleared with `Array.Clear` on rebuild, never reallocated per run.
2. Owner payloads inside `InteractionLatch` and request structs use explicit sequential layout with `byte` flags, never `bool`.
3. The net8.0 minimum for 4.6/4.7 desktop builds is UNVERIFIED (`VERSION.md`); nothing here depends on it.

## Related
- ADR-0013 (solver, chest point, `Sim/Geometry`); ADR-0012 (router, `InputFrame`, presented snapshot on edges, one-commit rule); ADR-0015 (`ICatchPredictor`, catch arming); ADR-0016 (ClientLayer timing, previews, presented snapshot); ADR-0017 (Deliver anchors per destination); ADR-0009 (range-gated ops call `CheckInteraction`); ADR-0020 (F5 ring, `Teleport` protocol); ADR-0018 (prompt and ring rendering).
- `design/gdd/player-character-movement.md`, `design/gdd/input-controls.md`, `design/gdd/manual-delivery.md`, `design/gdd/throwing-catching.md`.
