<!-- PROTOTYPE - NOT FOR PRODUCTION -->
<!-- Question: Do players choose to throw mail (with real misdelivery risk) and enjoy it? -->
<!-- Date: 2026-10-15 -->

# M-1a — Delivery + Throwing greybox

Throwaway concept prototype. Never import from or into production code.

## Hypothesis

If the player hand-delivers mail by matching addresses and can throw it along an arc, they will choose to throw and enjoy it — evidenced by a meaningful share of deliveries thrown, play continuing after a misdelivery, and the owner plus two friends each asking to play again. (spec 13 §6 M-1a; riskiest assumption: an honest misdelivery penalty on a throw is funny, not frustrating.)

## How to run

Godot 4.7.1 standard build (GDScript; no .NET needed). From the repo root:

```
godot --path prototypes/m1a-delivery-throwing-concept
```

Controls: WASD move · mouse look · 1–8 / wheel / Q select held mail · E near the Post Office takes mail · hold E at a mailbox hand-delivers · hold LMB to aim (arc: green = right mailbox, red = wrong, white = ground), release to throw · E near a dropped item picks it up · Esc frees the mouse · F2 writes stats.

Stats go to `user://playtest_stats.txt` (`~/.local/share/godot/app_userdata/PM M-1a Prototype (throwaway)/`) at 10:00, on F2, or on close.

Headless smoke test: `godot --headless --path prototypes/m1a-delivery-throwing-concept -s res://test_headless.gd` (exit 0 = PASS).

## Status

Concluded 2026-10-15.

## Findings

See [REPORT.md](REPORT.md). Summary: hypothesis NOT FALSIFIED — preference signal only (one internal tester reports throwing clearly more fun than hand delivery; no friends played, no stats captured); recommendation PROCEED, accepted with CD-PLAYTEST CONCERNS. The tester locates the fun in multiplayer chaos rather than the solo loop; a two-friend run of this greybox (thresholds in REPORT.md) and the M0 Fellowship criterion are now gate requirements.
