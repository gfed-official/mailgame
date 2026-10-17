# Prototypes Index

Complete history of what was tried and what was learned. One row per concept prototype.

## Concept prototypes

| Concept | Date | Path | Verdict | Hypothesis | Report |
|---------|------|------|---------|------------|--------|
| M-1a — Delivery + Throwing greybox | 2026-10-15 | Engine (Godot 4.7.1, GDScript) | PROCEED, accepted with CD-PLAYTEST CONCERNS (hypothesis NOT FALSIFIED — preference signal only; 1 internal tester, no external playtest; two-friend greybox + M0 Fellowship check required) | Players choose to throw mail and enjoy it despite real misdelivery risk | [REPORT.md](m1a-delivery-throwing-concept/REPORT.md) |

## Technical spikes

| Spike | Date | Path | Verdict | Question | Report |
|-------|------|------|---------|----------|--------|
| Godot 4.7.1 .NET ↔ net8.0 Sim (OQ1 tick loop, OQ4 build) | 2026-10-16 | Engine (Godot 4.7.1 .NET, C#) | PASS (editor/headless); export path UNVERIFIED (no templates) | Does a Godot.NET.Sdk/4.7.1 game build against and load the Godot-free net8.0 Sim; which runtime; is `_Process` + Stopwatch usable for a 30 Hz accumulator | [REPORT.md](spike-godot-dotnet/REPORT.md) |
| ADR-0019 scene architecture (OQ1 GameLoop placement, S1 content packing, S4 ViewCatalog uid) | 2026-10-16 | Engine (Godot 4.7.1 .NET, C#) | PASS for OQ1/S4/S1-packing; S1 C# read-back + S2 UNVERIFIED (no export templates) | Autoload vs root order; do `*.jsonc` pack and read; do `PackedScene[]` catalogs keep uid through renames; does export need a .sln | [REPORT.md](spike-adr0019/REPORT.md) |
| ADR-0019 engine-specialist check (stretch/min-window settings, .gdignore vs include_filter, C# draw overloads) | 2026-10-16 | Engine (Godot 4.7.1, GDScript) | PASS (setting names VERIFIED; no min-window project setting; .gdignore beats include_filter) | Are ADR-0019 setting names and export filters right | [REPORT.md](spike-adr0019-check/REPORT.md) |
| OQ-IC-9 input spike (ADR-0012 §7 adapter: order/handled, devices/axes, focus/mouse, alloc) | 2026-10-16 | Engine (Godot 4.7.1 .NET, C#) | GO-WITH-DELTAS (synthetic/headless + short windowed; pad, alt-tab, AZERTY, Steam MANUAL) | May the adapter rely on arrival-stamped `_Input`, releases before GUI, `SetInputAsHandled`, type-first `DEVICE_ID_*` classification; what changes in §7 | [REPORT.md](spike-oq-ic-9-input/REPORT.md) |

## Pivot chains

None.
