---
name: simcore-review-rulings
description: Simulation Core first /design-review (2026-10-16) CD rulings — MAJOR REVISION, reconnect per spec 06, op drain cap, reqId handler-local, host-mirror timing, 9 ADRs, size/AC caps
metadata:
  type: project
---

Simulation Core GDD first review 2026-10-16: verdict MAJOR REVISION NEEDED (core contracts sound; scope bloat + spec 06 contradiction).

- Reconnect: same-account rejoin into a Frozen slot is allowed mid-Shift (spec 06 §6 ratified wins); only NEW accounts get `join_closed`.
- Op drain capped by `maxOpsDrainedPerTick`; leftovers stay queued; event log must not overflow at cap.
- Coalescing scoped to one handler/API call, never a phase.
- reqId dedupe is handler-local where a GDD contracts it (Grid Inventory rule 8); Sim Core has no generic dedupe; Networking HB10 primary defence. Sim Core treats reqId as opaque echoed value (GI wire is u16).
- Host mirror: drained at end of the frame that ran tick T — Presentation sees it same frame; host ClientLayer processes it at exactly the next client tick.
- 9 ADRs (composition/DI separate; driver+thread stay together; replay/dump separate from numerics).
- Caps: ≤1,000 lines target (hard 1,050), ≤55 ACs, ≤30 tagged M-1a. Moved material → docs/architecture/simulation-core-adr-inputs.md as UNREVIEWED SEED.

**Why:** foundation GDD must state behaviour/contracts only; seven GDDs depend on it.
**How to apply:** re-review must check these; treat seed doc as unreviewed input to TD ADRs. Related: [[project-autonomous-mode]]
