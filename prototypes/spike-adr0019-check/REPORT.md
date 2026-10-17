# Spike check — ADR-0019 engine-specialist verification (2026-10-16)

Throwaway. Run by the godot-specialist during `/architecture-decision` step 5.5 for ADR-0019.

- `~/.local/bin/godot-mono --headless --path prototypes/spike-adr0019-check -s chk.gd`
  - `display/window/stretch/aspect` hint `ignore,keep,keep_width,keep_height,expand`; `display/window/stretch/mode` hint `disabled,canvas_items,viewport` — VERIFIED
  - no `display/window/size/min_*` project setting exists (min window = `Window.MinSize` at runtime) — VERIFIED
  - `AccessibilityServer.AccessibilityLiveMode` = `LIVE_OFF/LIVE_POLITE/LIVE_ASSERTIVE`; `Window.min_size`, `content_scale_aspect`, `content_scale_factor` exist — VERIFIED
- `--export-pack Linux /tmp/chk-adr0019.pck` with `include_filter="inc/*.txt, sub/*.txt"`: `inc/b.txt` packed; `sub/a.txt` (folder has `.gdignore`) NOT packed → `.gdignore` beats `include_filter` — VERIFIED. `.gdignore` is not a runtime sandbox (still readable from the project dir).
- GodotSharp.xml (4.7.1 Release API): `DrawPrimitive`/`DrawPolygon`/`DrawColoredPolygon` have `ReadOnlySpan<Vector2>` overloads — VERIFIED.
- Not checked: full export, Windows, nested wildcard behaviour.
