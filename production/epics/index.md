# Epics Index

Last Updated: 2026-10-23, m1a-replan PR (counts from the story headers, `/workspace/scripts/count-story-status.sh`, on main 26cb877 (PR #44 merged) plus this PR): **109 Complete, 1 In Progress, 89 Ready, 5 Blocked, 8 Deferred — 212 stories** (3 new HUD stories, Blocked on ADR-0018 Proposed; 2 new Deferred stories split from manual-delivery 010 and throwing-catching 005/019). Before this PR, on main 26cb877: 109 / 1 / 6 / 91, 207 stories. Previous line: 2026-10-23 (counts from the story headers, `/workspace/scripts/count-story-status.sh`: main c201e87 (PR #13–#43 all merged, PR #42 last): 109 Complete, 1 In Progress, 6 Blocked, 91 Ready — 207 stories; player-character 016 Complete (PR #42, merged as main c201e87). Before that: 2026-10-23, main a0d8fee (PR #13–#43 merged except #42): 108 / 1 / 6 / 92; plus PR #42 (then unmerged): 109 / 1 / 6 / 91. Before that: 2026-10-22, main 242a62f (PR #13–#38 merged): 107 / 1 / 6 / 93; plus PR #40 (player-character 014): 108 Complete, 1 In Progress (content-data 018), 6 Blocked, 92 Ready — 207 stories; player-character 014 Complete (PR #40, merged); godot-host-shell 003/004 (PR #17), grid-inventory 004/005, throwing-catching 001, manual-delivery 001 (PR #14), player-character 013 (PR #13) and grid-inventory 006 (PR #16) Complete; grid-inventory 009 Complete (PR #20); grid-inventory 012 Complete (PR #21); grid-inventory 013 Complete (PR #26); grid-inventory 015 Complete (PR #27); grid-inventory 016 Complete (PR #29); grid-inventory 011 Complete (PR #30); grid-inventory 007 Complete (PR #31); grid-inventory 008 Complete (PR #32); grid-inventory 010 Complete (PR #33, merged); grid-inventory 014 Complete (PR #34, merged); grid-inventory 021 Complete (PR #35, merged as main c889f59; was stacked on PR #34 until it merged, main c2d4a64 merged in at 5e9fe8d); grid-inventory 017 Complete (PR #36, merged as main eda1efd))

Last Updated: 2026-10-23 (counts from the story headers, `/workspace/scripts/count-story-status.sh`: main 26cb877 (PR #13–#45 all merged, PR #44 last): 109 / 1 / 6 / 91; plus docs PR `docs-unblock-gi022-ic029` (PR #46, main 26cb877 merged in, no rebase): 109 Complete, 1 In Progress, 4 Blocked, 93 Ready — 207 stories; grid-inventory 022 (re-scoped to the host panel view) and input-controls 029 (Depends on 026–028) Blocked → Ready. Before that: 2026-10-23, main a441650 (PR #13–#43 and #45 merged): 109 / 1 / 6 / 91; main c201e87 (PR #13–#43 all merged, PR #42 last): 109 / 1 / 6 / 91; player-character 016 Complete (PR #42, merged as main c201e87). Before that: 2026-10-23, main a0d8fee (PR #13–#43 merged except #42): 108 / 1 / 6 / 92; plus PR #42 (then unmerged): 109 / 1 / 6 / 91. Before that: 2026-10-22, main 242a62f (PR #13–#38 merged): 107 / 1 / 6 / 93; plus PR #40 (player-character 014): 108 Complete, 1 In Progress (content-data 018), 6 Blocked, 92 Ready — 207 stories; player-character 014 Complete (PR #40, merged); godot-host-shell 003/004 (PR #17), grid-inventory 004/005, throwing-catching 001, manual-delivery 001 (PR #14), player-character 013 (PR #13) and grid-inventory 006 (PR #16) Complete; grid-inventory 009 Complete (PR #20); grid-inventory 012 Complete (PR #21); grid-inventory 013 Complete (PR #26); grid-inventory 015 Complete (PR #27); grid-inventory 016 Complete (PR #29); grid-inventory 011 Complete (PR #30); grid-inventory 007 Complete (PR #31); grid-inventory 008 Complete (PR #32); grid-inventory 010 Complete (PR #33, merged); grid-inventory 014 Complete (PR #34, merged); grid-inventory 021 Complete (PR #35, merged as main c889f59; was stacked on PR #34 until it merged, main c2d4a64 merged in at 5e9fe8d); grid-inventory 017 Complete (PR #36, merged as main eda1efd))
Engine: Godot 4.7.1 pinned (the box has Godot 4.7.2 mono at `~/.local/bin/godot` — see HANDOFF.md), C# (.NET 8 Sim / net10.0 tests)
Scope: M-1a slice (solo greybox: delivery + throwing)

| Epic | Layer | System | GDD | Stories | Status |
|------|-------|--------|-----|---------|--------|
| [simulation-core](simulation-core/EPIC.md) | Foundation | Simulation Core | design/gdd/simulation-core.md | 30 stories (28 Complete, 2 Ready) | Ready |
| [content-data](content-data/EPIC.md) | Foundation | Content & Data Definitions (incl. StatSheet) | design/gdd/content-data-definitions.md | 19 stories (17 Complete, 1 In Progress, 1 Ready) | Ready |
| [input-controls](input-controls/EPIC.md) | Foundation | Input & Controls | design/gdd/input-controls.md | 29 stories (6 Complete, 21 Ready, 1 Blocked, 1 Deferred) | Ready |
| [godot-host-shell](godot-host-shell/EPIC.md) | Foundation | Godot host shell (Presentation half of Simulation Core + Content & Data) | design/gdd/simulation-core.md, design/gdd/content-data-definitions.md | 11 stories (4 Complete, 7 Ready) | Ready |
| [grid-inventory](grid-inventory/EPIC.md) | Core | Grid Inventory & Containers (M-1a: hotbar + own panel) | design/gdd/grid-inventory.md | 24 stories (18 Complete, 6 Ready) | Ready |
| [world-stub](world-stub/EPIC.md) | Core | World Generation — M-1a hand-authored street stub (`IWorldQuery`) | none (consumer GDDs + architecture.md Sim.World) | 7 stories (7 Complete) | Complete (Provisional — no GDD; ADR-0026 Accepted) |
| [network-adapter-loopback](network-adapter-loopback/EPIC.md) | Core | Network adapter core — loopback (Networking & Session seam) | design/gdd/simulation-core.md §Network (no Networking GDD) | 9 stories (7 Complete, 2 Ready) | Ready |
| [mail-destinations](mail-destinations/EPIC.md) | Feature | Mail & Destinations (M-1a slice) | design/gdd/mail-destinations.md | 16 stories (6 Complete, 10 Ready) | Ready |
| [player-character](player-character/EPIC.md) | Feature | Player Character & Movement (M-1a slice) | design/gdd/player-character-movement.md | 27 stories (14 Complete, 12 Ready, 1 Deferred) | Ready |
| [manual-delivery](manual-delivery/EPIC.md) | Feature | Manual Delivery | design/gdd/manual-delivery.md | 12 stories (1 Complete, 9 Ready, 2 Deferred) | Ready |
| [throwing-catching](throwing-catching/EPIC.md) | Feature | Throwing & Catching (M-1a slice + Sim-side catch pull-forward) | design/gdd/throwing-catching.md | 25 stories (1 Complete, 20 Ready, 4 Deferred) | Ready |
| [hud-notifications](hud-notifications/EPIC.md) | Presentation | HUD & Notifications (M-1a minimal: wallet, time-only timer, deliver prompt) | design/gdd/hud-notifications.md | 3 stories (3 Ready: ADR-0018 Accepted by Evan 2026-10-23 8:10 AM PT) | Ready |

## M-1a dependency / implementation order

> **Story-level order:** see [implementation-order.md](implementation-order.md), regenerated 2026-10-23 (m1a-replan) **playable-first**: Track A (the visible greybox path, 73 stories in 16 waves) before Track B (16 stories in 4 waves), 0 dependency cycles. Authority: Evan, 2026-10-23 7:13 AM PT, "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". The epic-level list below is the original coarse plan (history).

1. `simulation-core` (+ `network-adapter-loopback` alongside — `FrameRunner` needs the loopback transport)
2. `content-data`
3. `grid-inventory` (Sim half)
4. `world-stub` → `mail-destinations`
5. `player-character` (Sim half: solver + interaction framework)
6. `manual-delivery`
7. `throwing-catching`
8. `input-controls` router core (Client.Core) — the minimal HUD is now the `hud-notifications` epic (2026-10-23; ADR-0018 Accepted 8:10 AM PT)
9. `godot-host-shell`, then each epic's presentation stories (panel view, PlayerView/camera, mailbox views, aim ribbon) and the Godot input adapter

## ADRs to write before the matching `/create-stories` (untraced M-1a requirements)

| ADR (planned) | Unblocks |
|---|---|
| ADR-0012 Input router core & Godot input adapter | input-controls |
| ADR-0013 Carrier kinematic solver | player-character, world-stub |
| ADR-0014 Interaction framework & hold timing | player-character, manual-delivery, input-controls |
| ADR-0015 Ballistic, sweep & flight resolution | throwing-catching, world-stub |
| ADR-0016 ClientLayer & presentation timing | manual-delivery, throwing-catching, mail-destinations |
| ADR-0017 Mail table, registry & IWalletGate | mail-destinations |
| ADR-0019 Godot presentation scene architecture | godot-host-shell, all presentation stories |
| ADR-0026 (stub section) `IWorldQuery` | world-stub |
| ADR-0018 HUD composition, minimal M-1a slice — **Accepted by Evan 2026-10-23 8:10 AM PT** (no longer blocking) | hud-notifications 001–003 (now Ready) |
| ADR-0024 session stats file (M-1a slice) — **Accepted 2026-10-23 (Evan)** | manual-delivery 009, throwing-catching 016 (both Ready) |

## Deferred — out of the M-1a slice (Evan, 2026-10-23)

Authority: Evan, Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024"; 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example". Each story keeps its text and carries "Deferred (Evan 2026-10-23 7:13 AM PT)" with both quotes. The decision log (2026-10-23, m1a-replan) lists the hardening stories that were **kept** because an Accepted ADR requires them, with draft errata for Evan.

| Story | Deferred to |
|---|---|
| [throwing-catching/022](throwing-catching/story-022-catcharm-op-arm-window-and-sender.md) | M0 |
| [throwing-catching/023](throwing-catching/story-023-catcher-receiver-contact-trycatch-terminals.md) | M0 |
| [throwing-catching/024](throwing-catching/story-024-catch-end-to-end-two-carriers-history-ring.md) | M0 |
| [throwing-catching/025](throwing-catching/story-025-deferred-throw-accessibility.md) (new; from 005/019) | M1+ (accessibility) |
| [manual-delivery/008](manual-delivery/story-008-race-suite-bell-fuzz-soak.md) | M0 |
| [manual-delivery/012](manual-delivery/story-012-deferred-prompt-accessibility-localisation.md) (new; from 010) | M1+ (accessibility, localisation) |
| [input-controls/023](input-controls/story-023-fantasy-scripts-toggle-run-route-parity-a11y.md) | M1 (accessibility) |
| [player-character/027](player-character/story-027-movement-rulings-extra-jump-slide-td-091.md) | M1 |
