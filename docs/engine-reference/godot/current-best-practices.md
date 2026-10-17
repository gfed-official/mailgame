# Godot — Current Best Practices (post-4.3)

Last verified: 2026-10-15. Limited to what the four upgrade pages state or directly imply; no general advice from training data.

- **Run Project > Tools > Upgrade Project Files** after moving a project to 4.6+ to convert all scenes at once (4.6 stops writing `load_steps` and saves unique node IDs; re-saving piecemeal causes large VCS diffs). Source: upgrading_to_godot_4.6.
- **Compare input device IDs against constants** (`InputEvent.DEVICE_ID_MOUSE`, `DEVICE_ID_KEYBOARD`) rather than `0` — relevant to a KBM + gamepad game. Source: upgrading_to_godot_4.7.
- **Use `duplicate_deep(DEEP_DUPLICATE_ALL)`** where a full sub-resource deep copy is required. Source: upgrading_to_godot_4.5.
- **Jolt is the default 3D physics engine for new projects** (since 4.6, `physics/3d/physics_engine`); the Sim assembly in this project uses its own solver (spec 07), so Jolt only affects client-side presentation physics. Source: upgrading_to_godot_4.6.
- **Wrap OpenXR editor types in `#if TOOLS`** if ever referenced (moved to `GodotSharpEditor`). Source: upgrading_to_godot_4.5. (Not expected to apply.)
- NOT SOURCEABLE — C# best practices for 4.7 (source generators, `[Signal]` delegate patterns, `GodotSharp` assembly layout): not covered by the upgrade pages. Consult the C# docs before relying on any pattern recalled from training.
