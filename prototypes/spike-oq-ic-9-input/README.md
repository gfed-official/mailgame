# Spike OQ-IC-9 — Godot 4.7.1 .NET input surface

**Hypothesis:** the ADR-0012 §7 Godot input adapter design (arrival-stamped `_Input` gathering, type-first device classification, `SetInputAsHandled` + unfocused GUI, focus-loss handling) works on Godot 4.7.1 .NET as written.

**Run:** `./run_all_headless.sh` (builds, then runs every headless probe into `logs/`). One probe: `./run_probe.sh <id>` (`baseline`, `order`, `devices`, `alloc`, `exitleak`, `focus`, `window`). Windowed: `./run_probe.sh window --windowed --display-driver wayland|x11`. Requires `~/.local/bin/godot-mono` (4.7.1 mono) and `mise exec dotnet@10.0.402`.

**Status:** concluded 2026-10-16 — verdict **GO-WITH-DELTAS** (hardware rows NOT RUN; collected by input-controls story 029).

**Findings:** see [REPORT.md](REPORT.md). Headlines: input callbacks run in reverse tree order and `ProcessPriority` does not reorder them (adapter must be the last root child); the stock `ui_*` map binds pad D-pad/stick/Y; the WM focus notification reaches the Window only; Wayland rejects mouse capture without pointer focus; C# `_Input` allocates ≈0.5 KB per engine-created event and engine-created key/mouse events were observed to stay alive after GC (open defect — follow-up spike before story 026 is Done).

Throwaway code — never referenced from `src/`.
