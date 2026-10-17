# Story 012: Interaction anchor registry — `IInteractable`, `RegisterAnchor`, the verb catalogue and the dense 2 m grid

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~500 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Interaction-framework story (2 of 9).** Manual Delivery (Deliver anchors per destination), Grid Inventory (Open / Pickup anchors), Throwing and Input & Controls register through or read this registry. API: `IPlayersApi.RegisterAnchor` / `UnregisterAnchor` / `MoveAnchor`, `IInteractable`.

## Context

**GDD**: `design/gdd/player-character-movement.md` (P10 the interaction framework, the v1 verb catalogue; F4 variables; Edge Cases "two players request the same seat")
**Requirement**: `TR-movement-011` (IInteractable registry with 2 m spatial hash; one verb per anchor)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P10-1, PCM-P10-2.)*

**ADR Governing Implementation**: ADR-0014: Interaction framework & hold timing (§1 the `IInteractable` port and the anchor registry) — Accepted 2026-10-16
**ADR Decision Summary**: `IInteractable` is a port **defined by Players and implemented by each verb's owner**; Players calls only its pure query hooks (no mutation, events, RNG or allocation). Anchor identity is `AnchorRef = (EntityId, byte Slot)`. `IPlayersApi.RegisterAnchor(in AnchorDef, IInteractable) → RegisterResult` and `UnregisterAnchor` / `MoveAnchor` run in phases 0–12 or at world build. **One verb per anchor per context is enforced at registration in every build** (`RegisterResult.DuplicateVerb` plus an always-on `SimCheckRing` record); capacity `maxAnchors` (settings, 4096) overflow returns `Full`, counted. The spatial index is a dense uniform grid of 2 m cells over `IWorldQuery.Bounds` (`int[] cellHead` + `int[] next` intrusive lists, allocated at world build; ≈ 1 MiB for a 1 km map).
**ADR Version**: ADR-0014 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: The grid (≤ 1 MiB `int[]`) lands on the large object heap: allocate once at world build, `Array.Clear` on rebuild, never reallocate per run (ADR-0014 engine note 1). Use explicit sequential layout and `byte` flags — not `bool` — in registered structs (note 2).

**Control Manifest Rules (Feature layer)**:
- Required: every interactable verb registers through `IPlayersApi.RegisterAnchor` (2 m dense grid; one verb per anchor per context; `DuplicateVerb` otherwise). `IInteractable` hooks are pure: no mutation, events, RNG or allocation (ADR-0014 §1)
- Required: size every collection from `SimSettings`; 0 B in the tick path; no `Dictionary` iteration (ADR-0006 §8)
- Required: `IPlayersApi` only as a declared `SystemGraph` dependency (ADR-0003 §5)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (PCM-P10-1, PCM-P10-2), scoped to this story:*

- [ ] **PCM-P10-1.** `AnchorDef.RangeMm` defaults to 2500 when unset; registering a **second verb on one anchor in one context** returns `RegisterResult.DuplicateVerb` in Debug **and Release** (and writes an always-on `SimCheckRing` record) and leaves the first registration intact; `IsAvailable` returns `ok` or a reason `StringId`; the catalogue (`Verb` enum) has **no revive verb**, and an out-of-range numeric `Verb` is rejected at registration.
- [ ] **CV-PLR-13 (ContentValidator, picked up here per `production/epics/content-data/story-011-cv-plr-rules.md`).** The rule is implemented in `Rules/Player.cs` (alias `PCM-P10-1`): it errors when a content definition names a verb outside the closed `Verb` catalogue (e.g. `revive`) or declares two verbs for one interactable (H-R4-5); a fixture with a valid single verb passes; `BuiltInContent.CreateM1a()` passes (no M-1a def carries a verb yet, so the rule is exercised by fixtures).
- [ ] **PCM-P10-2 (parametrised).** the catalogue's default modes: Deliver = Hold 400 ms; Open, Pick up, Mount, Operate, Configure, Shop, Announce, Dismount = Tap; Catch and Push = Channel; Rebuild and Deconstruct = Hold; default priorities: Deliver 2; Open / Pick up / Mount 1; every other verb 0; `AllowWhileSwimming` defaults true only for the registration of Open-on-pile and Pick-up-on-Spill-heap flags (set explicitly by their owners — the default is false).
- [ ] **Registration contract.** `RegisterAnchor` stores `{AnchorRef, Vec3Mm Point, Verb, InteractMode, HoldBaseMs, RangeMm, Priority 0–3, AnchorFlags (AllowWhileSwimming, BuildModeOnly, SeatOf…)}`; `UnregisterAnchor` frees the slot (re-registering the same `AnchorRef` afterwards succeeds); `MoveAnchor` re-buckets the anchor; `Priority` > 3 is rejected `InvalidArgument`; a null `IInteractable` is rejected.
- [ ] **Capacity.** The 4097th registration with `maxAnchors = 4096` returns `Full`, increments a counter, and the registry is unchanged; a registry sized from a `SimSettings` value of 8 behaves the same at 9.
- [ ] **Spatial index.** Anchors are inserted into the cell containing their `Point`; a query for cells within `CeilDiv(maxRegisteredRange + incumbentSlackMm, 2000)` of a chest point returns exactly the anchors whose cell is inside that square (checked against a brute-force scan over 2 000 random anchors, seeded PCG); the result is independent of insertion order for any consumer that applies a total tie-break (iteration is by ascending cell then insertion index, no sort); points outside `Bounds` are clamped into the edge cells.
- [ ] **Hook purity (ADR-0014 Risks).** An architecture test asserts every `IInteractable` member takes only `in`/value parameters, and that registered test doubles that try to mutate through a captured reference are caught by a debug wrapper (`PureHookGuard` — a test-only decorator that snapshots a hash of the owner's test state around each call and fails if it changed).
- [ ] **Dump.** The registry is dumped by Players' contributor in registration index order (ref, point, verb, mode, range, priority, flags — no object references); 2 000 registered anchors allocate 0 B per tick afterwards.
- [ ] `IInteractable.GetHoldTicks(latch)` has a default implementation that calls `Hold.HoldTicks(def.HoldBaseMs, insertPm = 0, holdSettingMs, settings)` (Story 011), so a verb that does not override it gets the shared length.

---

## Implementation Notes

*Derived from ADR-0014 §1 and Key Interfaces:*

- Files in `src/PerformativeMail.Sim/Players/`: `AnchorRef.cs`, `AnchorDef.cs`, `Verb.cs` (closed `byte` enum following content-data story 002's closed-enum pattern — `production/epics/content-data/story-002-closed-content-enums.md` explicitly leaves `Verb` for "the first consumer, through the interaction framework"), `InteractMode.cs`, `IInteractable.cs` (members per ADR-0014: `IsAvailable(in InteractionContext, out StringId)`, `GetHoldTicks(in InteractionLatch, in InteractionContext)`, `ShouldCancel(in InteractionLatch, in InteractionContext, out StringId)`, `WritePrompt(in InteractionContext, ref PromptArgs)`, `TryBuildRequest(in InteractionLatch, in InteractionContext, ref Op)`, `OnPress(in InteractionContext, ref InteractionLatch)`), `AnchorRegistry.cs` (+ `AnchorGrid`), `RegisterResult.cs`. Extend `IPlayersApi` with `RegisterAnchor`, `UnregisterAnchor`, `MoveAnchor`.
- `InteractionContext` / `InteractionLatch` / `PromptArgs` are *declared* here with the fields ADR-0014 §3 and §7 list (`InteractionLatch = { AnchorRef, Verb, Mode, PressTick, HoldTicks, ClaimedPosition, OwnerPayload 16 B }`; `PromptArgs` 32 B opaque), but are *populated* by Stories 016–018.
- "Context" in "one verb per anchor per context" is the input context of the registration (World default; Build-mode-only anchors carry `BuildModeOnly`); keep the key `(AnchorRef, ContextMask)`.
- Grid dimensions: `cellsX = CeilDiv(Bounds.MaxX − Bounds.MinX, 2000)`, same for Z; `cellHead[cell]` / `next[anchorIdx]` intrusive lists; build in the world-build hook (`Players` constructor receives `IWorldQuery`). A 1 km map is 250 000 cells ≈ 1 MiB; the M-1a street (≤ 256 m × 128 m) is ≈ 8 000 cells.
- Registration is legal in phases 0–12 and at world build; calling it from a client step is a programming error (`TickGuard`). Owners register Deliver anchors at run start (Manual Delivery) and Open/Pickup anchors when containers/items appear (Grid Inventory).
- Unavailable-reason keys (`reason.dry_land`, `reason.close_panel`, …) are `StringId`s from content (ADR-0004 §6); the framework-level reasons come from Story 013.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 013: contexts, the framework availability gate (Panel / swimming / Dead), the verb × context table.
- Stories 014–015: focus scoring and occlusion over this grid. Stories 016–018: press, hold, snapshot. Story 019: `CheckInteraction`.
- The owners' `IInteractable` implementations (Manual Delivery, Grid Inventory, Throwing) — their epics.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/AnchorRegistryTests.cs` (`[Trait("Id", "PCM-P10-1")]`, `PCM-P10-2`) and `tests/unit/content/PlayerVerbRuleTests.cs` (CV-PLR-13) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (Players system, `IPlayersApi`), Story 011 (`Hold.HoldTicks`), `production/epics/content-data/story-003-diagnostics-and-rule-registry.md` and `story-011-cv-plr-rules.md` (rule registry, `Rules/Player.cs`), `production/epics/simulation-core/story-007-simcounters-simcheckring.md` (`SimCheckRing`), `production/epics/world-stub/story-002-tile-flags-bounds-clamp-snap.md` (`Bounds`), `production/epics/content-data/story-002-closed-content-enums.md` (enum pattern)
- Unlocks: Stories 013–019; manual-delivery epic (Deliver anchor registration, `IInteractable` impl); grid-inventory Open/Pickup anchors; throwing-catching `ICatchPredictor` wiring
