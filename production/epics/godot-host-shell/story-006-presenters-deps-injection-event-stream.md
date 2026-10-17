# Story 006: Presentation plumbing — `PresenterDeps`, `Initialize(deps)` subtree injection, the World/Hud/Audio presenters' registration seams and the `PresentationEventStream` feed (boot step 7)

> **Epic**: Godot Host Shell & Greybox Scene
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Integration
> **Estimate**: M (~1 day; ~450 lines prod + smoke scenarios)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/simulation-core.md` (R17 Presentation is called explicitly with `Alpha`); consumer GDDs only for what the seams carry (`design/gdd/hud-notifications.md` H30 "an `opId` is never rendered twice"; `design/gdd/throwing-catching.md` / `manual-delivery.md` outcome-beat rules)
**Requirement**: `TR-movement-007` (presentation is called after `RunFrame` with `AlphaPermille`; views animate from data `Present` handed them), `TR-content-027` (the `ViewCatalog` handles are carried in `PresenterDeps`; resolution is story 008)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. `PresenterDeps`, subtree injection, the presenter registration seams and the event-stream feed have **no TR-ID in the registry**; they cite ADR-0019 §1/§2/Implementation Guidelines and ADR-0016 §5.)*

**ADR Governing Implementation**: ADR-0019: Godot Presentation Scene Architecture (§1 views are inert until `GameLoop` injects dependencies; §2 `PresentationRoot.Present(alpha)` calls `World`, `Hud`, `Audio` presenters in that order; Implementation Guidelines "child views `_Ready` before `GameLoop._Ready` … must be inert until `Initialize(deps)`") — Accepted 2026-10-16 (primary); ADR-0016 (§5 `PresentationEventStream`: `ClientAdapter` publishes each decoded batch to Presentation in the frame it arrives; consumers hold cursors; `(sourceTick, ordinal)` order; never delivered twice to a cursor; `[OutcomeBeat]` types fed only from cursors) — Accepted 2026-10-16 (secondary); ADR-0004 (views map `EntityId` → node, ids never in `[Signal]`/`[Export]`)
**ADR Decision Summary**: Children `_Ready` before the root (spike OQ1-1), so dependencies flow down by explicit injection from `GameLoop` through the presenters, never by lookup. Presenters are invoked by `PresentationRoot.Present` in the order World → Hud → Audio, once per frame after `RunFrame` and the render-clock sample. Outcome beats are fed only by cursors on `PresentationEventStream`, which is published in the frame a batch is decoded (before the client tick that applies it), in `(sourceTick, ordinal)` order.
**ADR Version**: ADR-0019 2026-10-16; ADR-0016 2026-10-16; ADR-0004 2026-10-16

**Engine**: Godot 4.7.1 .NET | **Risk**: HIGH
**Engine Notes**: Godot readies children before parents and calls `_Ready` once per node; injection must therefore be an explicit method call, and must tolerate scene children created later (pools, per-lot views). `Node` child enumeration allocates in some bindings (`GetChildren()` builds an array): walk the subtree **once at `Initialize`**, never per frame. `GodotObject.IsInstanceValid` guards every stored node reference (ADR-0004 risk). Id structs (`EntityId`) never cross as `[Signal]`/`[Export]`/Variant; pass `.Value` and range-check on the way back.

**Control Manifest Rules (Presentation)**:
- Required: Presentation is called explicitly by `GameLoop` after `RunFrame` via `IPresentationRoot.Present(AlphaPermille)`, presenters in order World → Hud → Audio; views are inert until `Initialize(deps)`; outcome beats play only from `PresentationEventStream` cursors; presentation time comes from `IRenderClock` (ADR-0002, ADR-0016, ADR-0019)
- Required: Godot views map `EntityId` → node through a C# `Dictionary` owned by Presentation (story 009); pass `.Value` to Godot (ADR-0004)
- Forbidden: any view reading `SimWorld`/`ClientLayer` directly; `GetNode`/singleton/static lookups of dependencies inside `_Ready`; feeding a Presentation value into the Sim; `delta` or `Timer` for gameplay-facing timing (ADR-0016)
- Guardrail: presenter `Present` allocates 0 B in steady state (arrays + `for`, no LINQ/closures)

---

## Acceptance Criteria

*From ADR-0019 §1/§2 and ADR-0016 §5, scoped to this story. Delta: story 002 left `WorldPresenter`/`HudPresenter`/`AudioPresenter` as inert stubs; story 003 owns `PresentationRoot`; `PresentationEventStream` has an owner only on paper (manual-delivery 006 adds it "if absent"; nothing feeds it from `ClientAdapter` — network-adapter-loopback 006 has no publish hook).*

- [ ] **`PresenterDeps` and subtree injection.** `PresenterDeps` (immutable, built once at boot) carries: the read-only `IWorldQuery`, the `IRenderClock`, the `PresentationEventStream`, the view-catalog handles (story 008; empty until then), the `EntityViewRegistry` (story 009; null-object until then) and a typed `PublishedStates` bag that boot contributors fill with the ClientLayer-published read-only interfaces the feature epics need (`ICarrierView`, `IInteractionQuery`, `IThrowPreviewRead`, `IDeliverPreviewRead`, `IFlightRead`, …). `PublishedStates.Get<T>()` throws a descriptive exception naming `T` when absent (fail loud at `Initialize`, never a null view). `IInjectable<TDeps> { bool IsInitialized { get; } void Initialize(TDeps deps); }` is the one injection contract. `WorldPresenter.Initialize(deps)`, `HudPresenter.Initialize(deps)` and `AudioPresenter.Initialize(deps)` each walk their own subtree once, call `Initialize(deps)` on every descendant implementing `IInjectable<PresenterDeps>` and register those implementing `IPresentedView { void Present(int alphaPermille); }` in tree order (so a scene child such as `CameraRig` is injected and presented with no `_Ready` lookup). A second `Initialize` is refused. A smoke scenario proves: descendants are not initialised before boot step 7, are initialised exactly once after it, and a view added later via `Register` is initialised and presented from the next frame.
- [ ] **Present order and seams.** `PresentationRoot.Present(alpha)` (story 003) calls World, then Hud, then Audio, and each presenter calls its registered views in registration order, passing `alphaPermille` through unchanged (recording test, including a view that registers during a `Present` — it is first presented next frame, no collection-modified exception). Seams other epics use: `WorldPresenter.Register(IPresentedView)`; `HudPresenter.AddLayer(CanvasLayer layer, int order)` (a stable-ordered layer list under `Main/Hud`; the aim ribbon's dedicated `AimLayer` below the HUD stack — throwing-catching 018 — uses it; the stack's final order is ADR-0018's, not written) and `HudPresenter.Register`; `AudioPresenter.RegisterSink(IAudioCueSink)` with `IAudioCueSink { void Cue(in AudioCue cue); }` and `AudioCue(StringName Id, Vector3 Position, bool Positional)`; the default sink logs `AUDIO cue=<id>` (rate-limited to one line per id per second) so cues are observable before assets exist — no placeholder audio files are added.
- [ ] **`PresentationEventStream` is fed in the frame a batch arrives.** The stream exists in Client.Core (`PerformativeMail.Client.Core.Presentation`: `OpenCursor(EventKindSet)`, `EventCursor.TryRead(out PresentedEvent)`, `[OutcomeBeat]` — exactly as ADR-0016 Key Interfaces shows; added here **if absent**, recorded in Last Updated). `ClientAdapter` publishes every decoded batch to it in the frame it is decoded — before the client tick that applies it through `MirrorDrain`. With a loopback fixture op that raises one event (the test-profile style of network-adapter-loopback 007): a cursor opened at boot reads that event in the **same frame** the batch is decoded, exactly once, in `(sourceTick, ordinal)` order; a second cursor reads it independently; neither re-reads it. If `ClientAdapter` has no decoded-batch observer seam, add the smallest one (an interface the shell implements) and record it; the architecture test "types tagged `[OutcomeBeat]` receive data only from cursors" (manual-delivery 006 AC-4) is not weakened.
- [ ] **Boot step 7 hands the world to the presenters.** The step logs `BOOT 7 presenters`, builds `PresenterDeps` from `BootContext` (the immutable `IWorldQuery` is taken from the composition result — `SimHost` exposes it read-only; add the accessor if absent, noting it is an immutable table interface and so does not breach "Presentation never reads `SimWorld`"), calls the three presenters' `Initialize(deps)` once, in order, and then invokes the world-build hooks story 010 (`TerrainView`) and story 009 (per-lot views) register on `WorldPresenter` (`IWorldBuildStep.Build(IWorldQuery)`; none registered logs `no world-build steps` explicitly). Any exception goes to story 004's error screen.

---

## Implementation Notes

*Derived from ADR-0019 §1–§2, Implementation Guidelines; ADR-0016 §5:*

- Files in `src/Game/Presentation/`: `PresenterDeps.cs`, `PublishedStates.cs`, `IInjectable.cs`, `IPresentedView.cs`, `IWorldBuildStep.cs`, `WorldPresenter.cs`, `HudPresenter.cs`, `AudioPresenter.cs`, `IAudioCueSink.cs`, `LogAudioCueSink.cs`; step in `src/Game/Boot/PresentersBootStep.cs`. Client.Core: `src/PerformativeMail.Client.Core/Presentation/PresentationEventStream.cs`, `EventCursor.cs`, `PresentedEvent.cs`, `OutcomeBeatAttribute.cs` (if absent), and the `ClientAdapter` publish seam.
- `PublishedStates` is a typed bag handed **explicitly** through `Initialize`; it is not reachable globally (no static, no autoload), which keeps ADR-0019's "no global lookup path" — the "locator" ban in ADR-0003 is about Sim systems and is not weakened. Contributors fill it in boot step 6/7 (`IPortContributor`/`IBootContributor`, stories 004–005); each feature epic registers its own published interface there.
- Do not make presenters call into the Sim: they read only `PresenterDeps` contents and the published interfaces. `WorldPresenter.Present` is not where views animate; it hands `alphaPermille` to views that may animate in their own `_Process` from data `Present` gave them.
- Preallocate the registered-view arrays (cap 64 per presenter, throw on overflow at registration — a boot-time programming error); `Present` iterates with `for`.
- The Hud presenter owns only registration here. The CanvasLayer stack order (MarkerLayer → persistent → notifications → panels → chat/wheel → banners → pause → debug, seed order in ADR-0019 §2) and every HUD widget are ADR-0018 / the (not yet created) HUD epic; `AddLayer` takes the order as a parameter and does not invent one.
- `AudioCue` ids are proposals owned by the audio-director (throwing-catching 021, mail-destinations 016): this story does not define a cue catalogue.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 008: `ViewCatalog` resolution (the handle slot exists in `PresenterDeps`). Story 009: `EntityViewRegistry`, pools, per-lot views. Story 010: `TerrainView`. Story 003: `PresentationRoot` itself.
- Any actual view (PlayerView, CameraRig, MailboxView, FlightView, panel, ribbon), HUD widgets, `ADR-0018` layer order, audio mixing/voice pools beyond the registration seam (mail-destinations 016 / throwing-catching 021).
- Outcome-beat controllers (manual-delivery 006/011, throwing-catching 021) and their `[OutcomeBeat]` enforcement test — they consume this story's stream.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/presentation/PresentationEventStreamFeedTests.cs` (Godot-free, loopback fixture op, same-frame/once/ordering rows) — must exist and pass; plus smoke scenarios `presenter-injection` and `present-order` (`SMOKE … PASS`) retained in `production/qa/evidence/host-shell-presenters-evidence.md`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 003, 004, 005; `production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md` (`ClientAdapter` decode path), `production/epics/network-adapter-loopback/story-007-host-loopback-session-frame-timing.md` (the fixture-op style), `production/epics/world-stub/story-007-world-dump-composition-replay-id.md` (`IWorldQuery` composed)
- Unlocks: Stories 008, 009, 010, 011; `production/epics/player-character/story-022-playerview-scene-render-pose.md`, `production/epics/player-character/story-023-camera-rig-node-focus-cone.md`, `production/epics/player-character/story-026-interaction-prompt-anchor-projection.md` (`Hud` presenter slot), `production/epics/manual-delivery/story-006-reply-verdict-hand-beat-keys.md`, `production/epics/throwing-catching/story-018-aim-ribbon-view.md`, `story-020-flight-view-multimesh-trail-shadow.md`, `story-021-launch-ghost-release-snap-landing-beats.md`, `production/epics/mail-destinations/story-016-mailbox-greybox-view-outcome-beats.md`, `production/epics/grid-inventory/story-023-panel-grid-view-hotbar.md`
