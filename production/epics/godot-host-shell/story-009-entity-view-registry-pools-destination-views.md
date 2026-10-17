# Story 009: `EntityViewRegistry`, `EntityViewHost`, `NodePool<T>` with the ADR-0019 caps, and one view per lot under `World/Destinations`

> **Epic**: Godot Host Shell & Greybox Scene
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Integration
> **Estimate**: M (~1 day; ~450 lines prod + tests/smoke)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/mail-destinations.md` (L618/L637/L639 presentation caps: envelope props 48 island-wide, residents 8, heap props 12, flashes ≤ 3/s, mail audio voices 16 — the caps this story encodes; the views themselves are mail-destinations 016), `design/gdd/throwing-catching.md` (L967–969: ×N tag pool 8 hidden beyond 35 m; flight caps — throwing-catching 020), `design/gdd/mail-destinations.md` R3 (the 10-address street, one destination per lot)
**Requirement**: `TR-mail-022` (presentation caps: residents, envelope pool, flashes, audio voices — the cap constants and the pre-instantiation/exhaustion mechanism only; the pools' scenes and behaviour are the feature epics'), `TR-content-027` (a per-lot view is instantiated from the resolved `ViewCatalog` or its placeholder)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. `EntityViewRegistry`, `NodePool<T>` and the `Destinations` host have **no separate TR-ID in the registry**; they cite ADR-0019 §5 and ADR-0004.)*

**ADR Governing Implementation**: ADR-0019: Godot Presentation Scene Architecture (§5 entity views and pools: `EntityViewRegistry` is a `Dictionary<EntityId, Node3D>` preallocated to the cap, added on the spawn event, removed on the despawn event, every lookup guarded with `GodotObject.IsInstanceValid`; pools are pre-instantiated at boot with fixed caps, exhaustion recycles the oldest **cosmetic** instance or skips the visual, never allocates; §2 `Destinations`: one `MailboxView` per lot, static, not pooled; Key Interfaces `EntityViewRegistry`, `NodePool<T>`) — Accepted 2026-10-16 (primary); ADR-0004 (id structs never cross as `[Signal]`/`[Export]`/Variant) — Accepted 2026-10-16 (secondary)
**ADR Decision Summary**: Presentation owns a C# `Dictionary<EntityId, Node3D>` sized to a cap; pools are `NodePool<T>(PackedScene, cap, parent)` instantiated at boot step 8, with the §5 caps table (envelope props 48, residents 8, heap props 12, ×N `Label3D` tags 8, mail audio voices 16, 4 MultiMesh bundle tiers). Steady-state frames instantiate no nodes. Iteration order of the registry never affects gameplay. Destinations are static: one view per `LotRecord`, instantiated once at world-build time.
**ADR Version**: ADR-0019 2026-10-16; ADR-0004 2026-10-16

**Engine**: Godot 4.7.1 .NET | **Risk**: HIGH
**Engine Notes**: `GodotObject.IsInstanceValid` (not `!= null`) is the freed-node test — a `QueueFree`d node can still be referenced from a C# `Dictionary`. Toggle pooled nodes with `Visible`/`ProcessMode` rather than re-parenting to keep per-rent cost allocation-free (re-parenting is not guaranteed allocation-free — UNVERIFIED, measure). `Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)` is the node-count probe for the soak (name per GodotSharp 4.7.1 — verify). Node transforms are floats in metres: Sim mm → Godot m by `mm / 1000f` at the view boundary only. BAM yaw → `Rotation.Y = bam × 2π / 65536` with **no sign flip** (ADR-0013 §3).

**Control Manifest Rules (Presentation)**:
- Required: Godot views map `EntityId` → node through a C# `Dictionary` owned by Presentation; pass `.Value` to Godot and range-check when converting back; every registry lookup `IsInstanceValid`-guarded (ADR-0004, ADR-0019 §5)
- Required: pools pre-instantiated at boot within §5's caps; steady-state frames instantiate no nodes and allocate 0 B (ADR-0019); exhaustion recycles the oldest cosmetic instance or skips the visual, never allocates
- Forbidden: id structs as `[Signal]` parameters, `[Export]` fields, `SetMeta` values, `Godot.Collections` elements or `Callable` binds (ADR-0004); iteration order of the registry ordering gameplay; a view reading `SimWorld` directly
- Guardrail: 0 B managed per registry lookup and per pool rent/return over 600 frames (measured, logged; blocking only for the panel/ribbon stories)

---

## Acceptance Criteria

*From ADR-0019 §5 and Validation Criteria ("pool caps: a soak with 200 flights and 100 outcomes per second never exceeds the caps or instantiates nodes after boot"), scoped to this story. Delta: no registry, pool or per-lot view exists; `World/Entities`, `World/Destinations`, `World/Props` and `World/Flights` are empty nodes (story 002).*

- [ ] **`EntityViewRegistry` and `EntityViewHost`.** `EntityViewRegistry` (`TryGet(EntityId, out Node3D)`, `Add`, `Remove`, `Count`, counters) wraps a `Dictionary<EntityId, Node3D>` constructed with its capacity (`PresentationCaps.EntityViews`, a named constant — **ASSUMPTION 512** bounding the 8 players, world items up to grid-inventory 013's cap and the 10 destinations; revisit when that cap is final; log the chosen value); a duplicate `Add` and an `Add` beyond capacity are refused and counted (the dictionary never grows); `TryGet` on a node freed behind the registry's back removes the entry, increments `StaleLookups` and returns false. `EntityViewHost` (a plain C# class used by presenters, parenting under `World/Entities`) offers `Spawn(EntityId id, PackedScene scene)` (instantiate, parent, inject `PresenterDeps` if the node is `IInjectable<PresenterDeps>`, register), `SpawnForDef(EntityId id, ResolvedViews views, DefIndex def)` (uses story 008's by-`DefIndex` scene, i.e. the placeholder at M-1a) and `Despawn(EntityId id)` (remove from the registry, free the node). Smoke scenario `entity-registry` proves each row, including that 600 lookups allocate 0 B after warm-up (measured and logged).
- [ ] **`NodePool<T>`, the caps table and boot-time instantiation.** `NodePool<T>(PackedScene scene, int cap, Node parent) where T : Node` instantiates all `cap` instances at construction (hidden, `ProcessMode = Disabled`), `Rent()` returns an instance or `null` when exhausted, `Return(T)` hides it again (returning a node the pool does not own is refused), and `RentOrRecycleOldest()` (for cosmetic pools) recycles the oldest rented instance instead of failing; counters `RentFails`, `Recycles`, `PeakInUse` are readable. `PresentationCaps` holds the ADR-0019 §5 numbers as named constants (`EnvelopeProps = 48`, `Residents = 8`, `HeapProps = 12`, `FlashesPerSecond = 3`, `BundleTags = 8`, `BundleTiers = 4`, `MailAudioVoices = 16` with the per-cue sub-caps cha-ching 4 / wrong-door 3 / `sfx_feed_tick` 6 / Lost Parcel chirp 3 and one reserved voice for the local player's own outcome). Feature epics register `PoolSpec(name, scene, cap, parent)` entries through an `IBootContributor` at boot step 8; the step instantiates every spec before the first frame, refuses a cap above its `PresentationCaps` constant, and logs `POOL <name> cap=<n> instantiated=<n>`. Smoke scenario `node-pool-soak` (a test pool of 48): 200 rents and 100 returns per simulated second for 600 frames never exceed the cap, `ObjectNodeCount` is constant across the run (no node instantiated after boot) and rent/return allocate 0 B per frame (measured, logged).
- [ ] **One view per lot under `World/Destinations`.** At world-build time (story 006's `IWorldBuildStep`), `DestinationsHost` creates exactly one static view per `IWorldQuery.Lots[i]` — 10 on the M-1a street — from the `destination_types` catalog's resolved scene for the `house` type (the placeholder at M-1a; at M-1a every lot is `house`, and mail-destinations 002's per-lot registry type is used when present), named `Lot_<Number>`, at `LotRecord.Anchor / 1000f` (metres), yawed so the view faces `LotRecord.Face` using a pure, unit-tested `FaceYaw.ToBam(FaceDir)` (Client.Core, derived from the `FaceDir` definition in world-stub 004; BAM 0 faces −Z, no sign flip), registered in `EntityViewRegistry` under `LotRecord.Destination`. The view is injected through `IDestinationView : IInjectable<PresenterDeps> { void Bind(in LotBinding b); }` when it implements it (`LotBinding`: destination `EntityId`, ordinal, anchor in metres, face, display number) — **the attach point mail-destinations 016's `MailboxView` implements**; a placeholder with no script is fine. Smoke scenario `destinations-lots` asserts 10 children, unique names and ids, positions within the map bounds, each registry entry valid, and that none is pooled.

---

## Implementation Notes

*Derived from ADR-0019 §2/§5, ADR-0004:*

- Files in `src/Game/Views/`: `EntityViewRegistry.cs`, `EntityViewHost.cs`, `NodePool.cs`, `PoolSpec.cs`, `PresentationCaps.cs`, `Destinations/DestinationsHost.cs`, `Destinations/IDestinationView.cs`, `Destinations/LotBinding.cs`; `src/PerformativeMail.Client.Core/Presentation/FaceYaw.cs` (+ `tests/unit/presentation/FaceYawTests.cs`); `src/Game/Boot/PoolsBootStep.cs` (runs inside boot step 8 after story 008's resolution).
- Who calls `Spawn`/`Despawn`: the feature epics' presenters on their spawn/despawn domain events, via cursors on `PresentationEventStream` (story 006) — player-character 022 for the local `PlayerView`; the **unowned `WorldItemView` seam** (throwing-catching 020 notes it; grid-inventory/shell presentation work) would call `SpawnForDef` with the item/mail-kind catalogs. This story provides the mechanism and builds neither view.
- Dictionary key: `EntityId` as a value struct with proper `IEquatable<EntityId>` (content of simulation-core 002); do not box it. Registry iteration never orders gameplay; if enumeration is needed it is Presentation-only.
- Pool exhaustion is a cosmetic degradation, never an error: `Rent()` returning `null` means "skip the visual"; callers must handle it. Flashes (≤ 3/s) are a governor, not a node pool — the constant exists for the HUD core; do not build a limiter here.
- The `Flights` MultiMeshes (4 tiers, `InstanceCount` = flight cap 160) and ×N tag `Label3D` pool are built by throwing-catching 020 using `NodePool`/`PoolSpec` where applicable; the audio voice pools are the audio presenter's (mail-destinations 016 / throwing-catching 021) — this story only fixes the numbers.
- Placeholder views are enough: the Visual/Feel evidence for this story is the greybox street with ten placeholder markers at the mailbox anchors (story 010's screenshot).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- `MailboxView` flap/outcome beats (mail-destinations 016), `PlayerView`/rig (player-character 022/024), `WorldItemView` (unowned seam), `FlightView` and MultiMesh bundles (throwing-catching 020), residents/envelope/heap prop scenes and behaviour (mail-destinations 016), audio voice pools and mixing.
- Story 008: catalog resolution. Story 006: `Initialize(deps)` and `IWorldBuildStep` hooks. Story 010: terrain, solids, the Post Office.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Logic half: `tests/unit/presentation/FaceYawTests.cs` — must exist and pass
- Integration half: smoke scenarios `entity-registry`, `node-pool-soak`, `destinations-lots` (`SMOKE … PASS`, measured allocation figures logged) in `production/qa/evidence/host-shell-views-evidence.md` — `/story-done` checks the test, scenarios and evidence file exist

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 006 (`IWorldBuildStep`, `PresenterDeps`), 008 (`ResolvedViews`), 005 (`IWorldQuery` available), 004 (boot plan); `production/epics/world-stub/story-004-addressid-lots-destination-ids.md` (`LotRecord`, `FaceDir`), `production/epics/world-stub/story-006-m1a-street-and-fixtures.md` (the 10-lot street)
- Unlocks: Story 010, Story 011; `production/epics/player-character/story-022-playerview-scene-render-pose.md` (`EntityViewRegistry`, `World/Entities`), `production/epics/mail-destinations/story-016-mailbox-greybox-view-outcome-beats.md` (`IDestinationView`, `PoolSpec`, `PresentationCaps`), `production/epics/throwing-catching/story-020-flight-view-multimesh-trail-shadow.md` (`PoolSpec`, `World/Flights`)
