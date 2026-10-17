# Story 010: Deliver prompt content as data — `WritePrompt` `PromptArgs`, localisation keys and arguments (no concatenation), screen-reader settle gate

> **Epic**: Manual Delivery
> **Status**: Blocked
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day once unblocked)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **BLOCKED: ADR-0018 (HUD composition, prompt data and live-region policy) is not written.** `TR-delivery-019` and `TR-delivery-020` are governed by ADR-0018 (registry coverage: gap). The prompt key set, the argument layout HUD consumes, the reason-strip lifetime hook and the announcement events (needed for M-MD4-1e) are undecided. ADR-0014 fixes only the transport (`WritePrompt(in InteractionContext, ref PromptArgs)`, 32 B, copied into `InteractionSnapshot` by `production/epics/player-character/story-018-interaction-snapshot-iinteractionquery.md`). Unblock: `/architecture-decision` ADR-0018 → accept → `/story-readiness`. No HUD epic exists yet (EPIC risk 2).

## Context

**GDD**: `design/gdd/manual-delivery.md` (UI Requirements branch table; MD10 never builds address text; M-UI-1; M-MD4-1e)
**Requirement**: `TR-delivery-019` (prompt content as data from the Sim; HUD renders; localisation keys, no concatenation), `TR-delivery-020` (live-region announcement policy; `accessibility_live` API unsourced — schema at M-1a, announcer M4)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0018 (planned, **not written**): HUD composition & prompt data. Secondary: ADR-0014 (§7 `PromptArgs` 32 B in `InteractionSnapshot`)
**ADR Decision Summary**: — (pending ADR-0018). Known from ADR-0014: the owner writes up to 32 B of prompt arguments per tick for the focused anchor; HUD renders from the snapshot.
**ADR Version**: ADR-0018 unversioned (not written); ADR-0014 2026-10-16

**Engine**: Godot 4.7.1 (.NET) | **Risk**: HIGH for the live-region surface (NOT SOURCEABLE `accessibility_live` in 4.7.1; ADR-0019 cites `Control.AccessibilityLive` with `AccessibilityServer.AccessibilityLiveMode`) — LOW for the Sim-side data
**Engine Notes**: The Sim half has no engine surface. The announcer is HUD's (M4).

**Control Manifest Rules (Feature + Global)**:
- Required: prompt text is data (`StringId` keys + arguments); the Sim never formats strings (Global; MD10)
- Forbidden: concatenating address fragments; a literal "¢" in keys

---

## Acceptance Criteria

*From GDD `design/gdd/manual-delivery.md`, scoped to the Sim-side data (rendering is HUD's):*

- [ ] **AC-1 (M-UI-1, data half)** `WritePrompt` emits a key per branch row of the UI Requirements table plus typed arguments (address as a world `StringId`, counts, cents as integers, flags). A test locale that places `{address}` first renders it without truncation in a scripted HUD stub. A lint finds no code path concatenating address fragments. No key contains a literal "¢". Each `a11y.deliver.*` variant is its own key.
- [ ] **AC-2 (M-MD4-1e)** In screen-reader mode no ✗ hold starts before the ✗ announcement event has fired, even after T+6 (the gate reads an announcement-fired signal whose source ADR-0018 defines).

---

## Implementation Notes

- Re-derive everything from ADR-0018 once it exists. Keep `PromptArgs` ≤ 32 B (ADR-0014).

---

## Out of Scope

- HUD prompt widget, ring, strip lifetime and screenshots (M-UI-2, M-UI-3, M-MD2-3, M-MD2-4): HUD epic (not created).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/delivery/DeliverPromptDataTests.cs` (+ the concatenation lint) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: ADR-0018 (not written); Story 003, Story 004; `production/epics/player-character/story-018-interaction-snapshot-iinteractionquery.md`
- Unlocks: the HUD prompt story (HUD epic, not created)
