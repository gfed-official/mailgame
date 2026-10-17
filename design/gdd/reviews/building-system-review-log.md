# Building System — Review Log

## Review — 2026-10-16 — Verdict: NEEDS REVISION
Scope signal: M (one Feature system, 6 integer formulas, 7 upstream hard deps with World Generation unwritten; ADR candidates: occupancy/registry storage, shared TileLegality, run-encoded ConstructPlaced + epoch resync)
Specialists: game-designer, systems-designer, level-designer, qa-lead, creative-director (senior)
Blocking items: 14 (merged from 16 candidates; #9 and #13 narrowed, #16 demoted) | Recommended: 12 cheap (applied) + ~15 deferred
Summary: The core holds: legality order, atomic charge, prefix lines, the salvage bank and the trust model are sound, and the worked examples check out. The defects were contract and precision gaps. Placement's status under Input G9 and G8 was unclear, and Build shipped at M1 while Input schedules it at M2. B8 read HP and the record after unregistering them. F2 overlapped the actor checks, F6 did not define map-edge neighbours, and the outfeed lane, Path and Bridge tiles were unspecified. Solid AABBs had no height or removal step. Several ACs could not be set up in the 20 × 20 fixture or rested on an impossible HP premise. Fixing the pre-built constructs' refund faucet was the CD's top cheap item. Owner-ratified rulings (no terrain edits, remover-named feed, 15 % Delivery test, spawn pads out of v1) were not re-examined.
Prior verdict resolved: First review
Findings:
- [BLOCKING] B11 / Input G9: Place / PlaceLine commit status undefined (pad RT aim-release vs KBM tap; one placement per RTT)
- [BLOCKING] B11 / Input G8: click vs drag on one button with no rule (merged with the above)
- [BLOCKING] B7 / B11: drag beyond the 8 m reach — start latch and carrier movement while Dragging undefined
- [BLOCKING] B15 / Input R12: minimal Build at M1 vs Input's Build bindings at M2
- [BLOCKING] B8: Health.Unregister and record delete precede the F4/F3 reads; Health has no HP read API
- [BLOCKING] AC-B8-2 / Edge Cases: "stat change leaves hp ≠ maxHp" impossible under Health F2
- [BLOCKING] Acceptance Criteria: FX-B 20 × 20 cannot host AC-B7-2 / NET-2 / NET-3 / NET-5 or the PO-apron case of AC-B4-1
- [BLOCKING] F2 / B7: V counted actor checks (cap, locked), overlapping capLeft; no stopReason for k = N
- [BLOCKING] F6: out-of-map neighbours undefined
- [BLOCKING] B4: Intake outfeed lane vs refuse-all apron precedence undefined; no AC
- [BLOCKING] B4: Path and Bridge tiles unhandled by the `street` check
- [BLOCKING] B6 / B8: solid AABB vertical extent and register/remove steps missing
- [BLOCKING] AC-B11-4 / UI Accessibility: toggle text contradicts Input G7
- [BLOCKING] AC-EXP-2: unaffordable ghosts counted as X-marked invalid ghosts
- [BLOCKING] AC-B8-5 / Edge Cases: `container_closed` should be Grid's `container_gone`
- [RECOMMENDED] F4: pre-built (System) constructs refunded 100 % in Prep — faucet (applied: ratio 0)
- [RECOMMENDED] Network: event-log overflow rationale wrong; JoinState 150 constructs ≈ 2.0 KB (applied)
- [RECOMMENDED] B7: one-tile PlaceLine has no rot; vetoes vs earlier tiles in the scan (applied)
- [RECOMMENDED] F5: "PO anchor centre" means footprint centre (applied)
- [RECOMMENDED] AC-B12-1 vacuous Plank clause; AC-B11-5 ruin tag M3; AC-B4-3 2.01 m impossible on a 2 m grid (applied)
- [RECOMMENDED] OQ-BLD-11 remover follows rollCallNames; Grant skips zero refunds; disconnect = unknown_actor; OQ-BLD-14 ratified text (applied)
- [RECOMMENDED] Deferred: undo for a mis-dragged line; undo grace eaten by hold + RTT (watch); Build entry auto-stow when arms full; trapped carriers inside wall rings; Spawn > 64 drain cap / epoch; invariant fuzz AC; "most built" = net live; AC-B11-6 via JoinState round-trip; mega-AC splits; Spawn pad inside PO footprint per spec 02 (WG); depot apron rule (Vehicles); enemy routing off streets (Enemy AI)
Reviewed-Content-Hash: design/gdd/building-system.md 0ce707610f1b241da5a092fbafdba0b23af503c3
Reviewed-Content-Hash: design/registry/entities.yaml e5ae9df8fdc708b6e00c288315424752b278162d

Revision applied 2026-10-16: all 14 blockers and 12 cheap recommended items. 512 → 514 lines, about +7 % bytes, all of it from the blockers (FX-B-L fixture, B11 placement grammar, B8 snapshot, 2 new ACs). Status set to In Review (Provisional). Registry synced: buildLinePrefix, salvageRatio, poDepotEligible, buildTerrainFit, the legality order, and the oneCommitRule referenced_by. Note: a concurrent session applied the owner-ratification text (header line 9, OQ-BLD-11/12) between the review read and the revision. Re-review decides Approved.
