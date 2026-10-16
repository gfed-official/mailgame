# 09 — UI and UX

Screens, HUD elements, interaction flows, and input mapping. The UI is built with Godot Control nodes driven by ClientWorld; every UI action becomes a request (chapter 06 §4) and every displayed value comes from replicated state or a local prediction tagged as pending.

## 1. Principles

- Read the address, not the tooltip. Addresses are legible on the item mesh, on the mailbox, on the street sign, and on the map, with consistent colour coding (district = swatch colour, street = stripe colour). Text is the fallback, not the primary channel.
- Never block the clock. No modal screen pauses the run in multiplayer. Inventory, map, and build menus are overlays; the shift timer and raid warnings remain visible.
- One panel plus one. A player sees at most their own inventory panel and one external container. Everything else is a HUD widget.
- Controller-complete. Every flow works with a gamepad; grid inventory uses cursor-snapping with rotate on a bumper.

## 2. Screens

### 2.1 Main menu

Play (Host Arcade, Join, Free Play [locked in v1 build]), Profile (rank, unlocks, stats, cosmetics), Settings, Quit. Version and protocol hash in the corner.

### 2.2 Lobby

| Region | Content |
|---|---|
| Left | Player list: avatar, name, kit (dropdown per player), ready state. Host badge. |
| Centre | Map archetype card (art, name, rank lock), seed field (random / enter / daily), Postage Stamps grid (toggle tiles with score multiplier and tier lock), computed score multiplier. |
| Right | Run rules summary (shifts, players, quota preview for current player count), invite button (Steam) / IP field, visibility toggle. |
| Bottom | Ready / Start (host). Start disabled until all ready or host overrides after 10 s. |

### 2.3 Loading

World generation progress bar with stage names (from chapter 02), a seed string, and a tip. Client verifies worldHash; mismatch shows an error and returns to the menu.

### 2.4 In-game HUD

```
+------------------------------------------------------------------+
| [Shift 3 / 5]  [Phase: DELIVERY 02:14]          [Quota 640 / 2214] |
| [Complaint envelope]                              [Wallet $18.20]  |
|                                                                    |
|  [Compass strip with district colours, pings, raid direction]      |
|                                                                    |
|       (reticle / interact prompt: "Deliver 3 Letters to            |
|                    13 Larch Lane  [E]")                            |
|                                                                    |
| [Event feed: "Jules delivered 12 letters", "Mega Giant north"]     |
| [HP bar] [Weight icon]              [Hotbar 1..8 with selected]    |
+------------------------------------------------------------------+
```

- Shift and phase: phase name, countdown mm:ss from server tick. Turns amber at 60 s, red at 15 s, pulses during Raid.
- Quota: progress bar shiftEarnings / quota; fills green when met, with a small "+surplus" label.
- Complaint envelope: an envelope icon that fills red with complaint value; shakes on increment; tooltip shows the causes this shift.
- Wallet: team wallet; pending purchases shown greyed until confirmed.
- Compass: 360° strip with district colour bands, teammate markers, map pings, raid spawn edge icon during warning, construct-under-attack icon.
- Interact prompt: context-sensitive: deliver (shows the selected stack's address and the mailbox's address side by side, with a green tick if they match and a red cross if not — the game never hides a mismatch), open, drive, operate, harvest, rebuild ruin.
- Event feed: last 5 events, 6 s each. Deliveries by teammates are compressed ("Jules delivered 12 letters").
- Hotbar: 8 slots; slot 1 "hands", 2–8 tools and mail. Number keys / d-pad cycle.
- Voice indicator: speaking teammates show a small icon by their name on the compass and over their head (chapter 13 §4.2).

There is no power widget.

### 2.5 Inventory panel (Tab / Y)

- Left: player's Hotbar (1x8), Inventory (2x8), Backpack (2x8) stacked as one visual grid with dividers.
- Right: the external container, if open. Small grids render every cell; containers above 160 cells render the manifest view: a scrollable list grouped by (kind, address) with counts and a fill bar; drop targets are group rows or a "put anywhere" bar.
- Item cells show the item icon, stack count, and for mail a mini address label with the district swatch and street stripe. Hover shows the full address and value.
- Controls: drag/drop, R rotate while dragging, shift-click quick-move to the other panel, ctrl-click split (mail stacks and materials), right-click context (Deliver-all when at a mailbox, Drop).
- Buttons: "Sort by address", "Sort by size" on each container; "Take all mail for [current district]" appears on external containers.
- Pending state: a moved item shows at 60% opacity until the server confirms; on rejection it snaps back with a sound.

### 2.6 Map (M)

- Top-down stylised map generated from the tile grid at load. Layers: districts (locked ones hatched with "Unlocks shift N"), streets with names on hover, houses with numbers at zoom ≥ 2, resources, constructs (belts as coloured lines with flow arrows), teammates, vehicles, NPC routes, enemies within any Alarm Post radius.
- Filter chips: Mail (highlights houses that currently have mail for them in the Intake/Depot/your inventory), Routes, Resources.
- Ping: click to place a world ping visible to teammates (compass icon + 3D marker, 10 s). Hold for ping wheel: "Deliver here", "Build here", "Danger", "Need materials".
- Route editor (when a Vehicle Depot console is open): click stops in order; district macro by clicking the district label; drag to reorder; estimated round-trip time shown from the routing graph.

### 2.7 Build mode (B)

- Radial or bar categories: Transport (belt, pipe, splitter, merger, ramps), Sorting (sorter, inserter), Storage (chest, depot), Vehicles (depot, pier, port), Defense (walls, gate, spikes, turret, alarm), Extractors (oil pump). Locked recipes show the missing blueprint or materials.
- Ghost preview with validity colour and reason text ("On street", "Too steep", "Needs deep water", "Missing 2 Iron Ingot").
- Drag lines for belts, pipes, walls; R rotates; scroll switches belt tier; Q picks the hovered construct's type ("pipette").
- Deconstruct mode: hold on a construct for 0.5 s; refund preview shown.
- Configure: interacting with a sorter opens the filter panel (per output: add filter chips from a searchable list of unlocked districts, streets, number ranges, kinds; live "matches N items currently in Intake" counter). Interacting with a depot console opens the route editor on the map.

### 2.8 Shop (at PO counter during Prep and Payday)

Grid of cards: name, icon, price, description, "once per run" or "hire" tag, unlock shift lock. Rotating specials highlighted. Buy is a request; the card greys with a spinner until confirmed. Team-shared: everyone sees the same stock, and a card bought by one player updates for all.

### 2.9 Payday summary

Full-screen but non-blocking overlay for 20 s: quota bar filling with a count-up; breakdown by mail kind; misdeliveries with the offending addresses; constructs lost; MVP callouts; upcoming district preview and mail forecast for the next shift.

### 2.10 Perk draft

Three cards centred; each shows name, category badge, rarity frame, description, scope (Team / Personal), and any prerequisite or exclusion note. Team perks show teammate avatars who already picked it and grey out. Reroll button with remaining count. Timer bar (30 s). Picks are visible to teammates live.

### 2.11 Results

Victory or Run Over banner; score with stamp multiplier breakdown; per-player XP with rank bar animation and unlock reveals; run stats; seed string with a copy button; "Play again (same seed)", "New seed", "Back to lobby".

### 2.12 Pause menu (Esc)

Settings, Leave run (with confirmation; explains inventory is dropped after 120 s), Invite. Does not pause in multiplayer; pauses the server in solo. Host additionally sees a Kick option per player (chapter 13 §4.3).

## 3. Interaction flows

### 3.1 Manual delivery

1. Player selects a hotbar slot containing mail (or opens inventory and picks a stack) → enters delivery stance; the held item shows its address label.
2. Approach a mailbox (≤ 2.5 m): prompt shows both addresses with match indicator.
3. Hold interact 0.4 s: progress ring; on completion the server validates; on success "cha-ching", wallet increments, feed entry; on mismatch "thud", penalty toast, complaint envelope shakes.
4. "Deliver all matching" (F) delivers every stack in the player's panel addressed to this mailbox, one per 0.4 s.

### 3.2 Throwing

Hold the throw button to show an arc preview from the held stack; release to throw. The arc is coloured green if it ends inside a matching mailbox volume, red if inside a non-matching one, white otherwise. See chapter 13 §4.1.

### 3.3 Loading a truck by hand

Open the truck's rear (container) → the right panel shows a 10x8 grid → shift-click stacks or press "Take all mail for District 2" on the PO Depot first, then "Put all" into the truck → "Sort by address" on the truck so the driver can pull stacks in street order.

### 3.4 Building a sorted belt line

Build mode → belt from PO Intake outfeed face → Address Sorter → per-output belts → mailboxes. Filter panel per output. HUD shows the sorter's live "unmatched items → overflow" count so an incomplete filter set is visible before mail piles up.

### 3.5 Raid

Warning banner + horn + compass marker (15 s). Constructs under attack pulse on the compass and map. Repair hammer shows a target health bar. Turret operation: interact to mount, mouse aim, 2× fire rate; a mounted player has a "Dismount" prompt and is still vulnerable.

## 4. Input map (default)

| Action | Keyboard/Mouse | Gamepad |
|---|---|---|
| Move / look | WASD / mouse | Left stick / right stick |
| Sprint | Shift | L3 |
| Jump | Space | A |
| Interact / deliver | E (hold) | X (hold) |
| Deliver all matching | F | Hold X longer (1 s) |
| Attack / use tool | LMB | RT |
| Throw held stack | RMB (hold to aim, release to throw) | LT (hold), release |
| Inventory | Tab | Y |
| Map | M | View |
| Build mode | B | LB |
| Rotate (build / inventory) | R | RB |
| Hotbar select | 1–8 / scroll | D-pad L/R |
| Ping | Middle mouse (hold for wheel) | RS click |
| Push-to-talk (optional; default open mic) | V | — |
| Chat | Enter | Menu (radial emotes) |
| Pause menu | Esc | Menu |

## 5. Accessibility

- Colour-blind safe palette for district and street colours (8 distinguishable hues plus patterns: stripes, dots, chevrons) with a setting to force patterns on labels.
- Text scale 100–150%; address labels scale in the inventory and HUD.
- Hold-to-interact can be switched to toggle.
- Subtitles for barks and captions for key sounds (raid horn).
- Reduced screen shake and flash options.

## 6. Onboarding

- First run (per profile): shift 1 shows contextual tips: "Pick up mail from the Intake", "Match the address", "Build a belt from the Intake's outfeed", "Raids start next shift". Tips are dismissable and never appear again once the action is performed.
- Lobby "How to play" card summarising the shift loop.
- Practice seed: a fixed seed with a 2-shift run and no raid, selectable from the main menu, does not award XP.
