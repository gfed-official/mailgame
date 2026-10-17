# Godot — Breaking Changes (4.3 → 4.7)

Last verified: 2026-10-15. Each section cites the official upgrade page. Entries are filtered to what matters for a C#, 3D, networked project; see the source pages for the full tables.

## 4.6 → 4.7
Source: https://docs.godotengine.org/en/stable/tutorials/migrating/upgrading_to_godot_4.7.html

| Area | Change | Impact |
|------|--------|--------|
| C# | `Animation.length` is now `double` (was `float`) | Binary + source break |
| C# | `Control.accessibility_live` type moved to `AccessibilityServer.AccessibilityLiveMode` | Binary + source break |
| C# | `RichTextLabel.ImageUpdateMask.UPDATE_WIDTH_IN_PERCENT` → `UPDATE_WIDTH_UNIT` | Source break |
| C# | `RenderingServer.particles_request_process_time`: `time` → `process_time`, new `process_time_residual` | Source break |
| UI | `RichTextLabel.add_image`/`update_image`: `width`/`height` now `float`; `*_in_percent` → `width_unit`/`height_unit` (`RichTextLabel.ImageUnit`) | Call sites change |
| Input | Mouse/keyboard device IDs now `InputEvent.DEVICE_ID_MOUSE` / `DEVICE_ID_KEYBOARD` (were `0`, which some joypads use) | Compare `InputEvent.device` against the constants — affects gamepad/KBM switching |
| Physics (Jolt) | `WorldBoundaryShape3D.plane.d` sign interpreted opposite | Flip sign to keep 4.6 behaviour |
| Physics (Jolt) | `SoftBody3D` mass defaults to 1 kg for the whole body; `linear_stiffness` applied differently; `Area3D` now reports `SoftBody3D` overlaps | Retune / use layers |
| Audio | `AudioStreamPlayer.area_mask` default `0` (was `1`) | Set mask to layer 1 if using `audio_bus_override` on areas |
| Rendering | `LinearToSRGB` visual shader no longer clamps on Mobile/Forward+; `CanvasItem` lines no longer antialias-feathered (look thinner) | Visual |
| Defaults | `rendering/reflections/sky_reflections/roughness_layers` 7→8; `LookAtModifier3D.relative` true→false; new projects default stretch mode `canvas_items`, aspect `expand` | Existing projects unaffected for stretch/aspect |
| GDScript | Typed-return method overrides inherit the type and need an explicit return | n/a for C# |

## 4.5 → 4.6
Source: https://docs.godotengine.org/en/stable/tutorials/migrating/upgrading_to_godot_4.6.html

| Area | Change | Impact |
|------|--------|--------|
| C# | `AnimationPlayer.assigned_animation` / `autoplay` / `current_animation` now `StringName`; `get_queue` returns `StringName[]`; `current_animation_changed` name param `StringName` | Binary + source break |
| C# | `StreamPeerTCP.get_status` moved to `StreamPeerSocket` | Binary break (networking) |
| Networking | `StreamPeerTCP.disconnect_from_host/poll` → `StreamPeerSocket`; `TCPServer.is_connection_available/is_listening/stop` → `SocketServer` | Source compatible |
| Physics | New projects default to Jolt Physics (`physics/3d/physics_engine`) | No physics API breaks listed |
| Rendering | Glow default blend mode now Screen (brighter); Mobile glow rewritten; volumetric fog blending changed (brighter); `glow_intensity` 0.8→0.3; new Windows projects default to D3D12 (`rendering/rendering_device/driver.windows`) | Visual retune |
| Scenes | `.tscn` no longer writes `load_steps`; unique node IDs saved | Large VCS diffs on re-save; use Project > Tools > Upgrade Project Files |
| Navigation | `AStar2D/3D.get_point_path`, `AStarGrid2D.get_id_path/get_point_path` return empty path when `from_id` disabled/solid | Behaviour |
| 3D | `MeshInstance3D.skeleton` default `NodePath("")` (was `".."`) | Setting `animation/compatibility/default_parent_skeleton_in_mesh_instance_3d` restores old |
| Core | `FileAccess.get_as_text` lost `skip_cr`; `FileAccess.create_temp` `mode_flags` is `FileAccess.ModeFlags` | |

## 4.4 → 4.5
Source: https://docs.godotengine.org/en/stable/tutorials/migrating/upgrading_to_godot_4.5.html

| Area | Change | Impact |
|------|--------|--------|
| C# | Android export requires .NET 9; other platforms still .NET 8 minimum | Not relevant to PC target |
| C# | `StringExtensions.PathJoin` no separator when base empty / appended path starts with separator; `GetExtension` returns empty string when none | Behaviour |
| C# | `Quaternion(Vector3, Vector3)` now shortest-arc | Behaviour |
| C# | `RenderingDevice.Features.Address` → `BufferDeviceAddress` | Binary + source break |
| Networking | `Node.get_rpc_config` → `Node.get_node_rpc_config` | GDScript break; C# compatible |
| Networking | `JSONRPC.set_scope` → `set_method` | Breaks GDScript and C# |
| Rendering | `RenderingServer.instance_reset_physics_interpolation` and `instance_set_interpolated` removed | |
| Physics | `TileMapLayer` physics chunking on by default | n/a unless tilemaps used |
| Physics (Jolt) | Setting `physics/jolt_physics_3d/simulation/areas_detect_static_bodies` removed; `Area3D` always reports static-body overlaps | Use layers/masks |
| Core | `Resource.duplicate(true)` now duplicates only internal resources; use `duplicate_deep(DEEP_DUPLICATE_ALL)` for old behaviour | Affects any def/record cloning |
| Navigation | Region updates async by default (`navigation/world/region_use_async_iterations`) | |
| Import | New .gltf/.glb/.blend/.fbx imports use fixed node naming; "Naming Version" in Import dock for old files | |

## 4.3 → 4.4
Source: https://docs.godotengine.org/en/stable/tutorials/migrating/upgrading_to_godot_4.4.html

| Area | Change | Impact |
|------|--------|--------|
| C# | `FileAccess.store_*` methods return `bool` (was `void`) | Binary break |
| C# | `Shader.get_default_texture_parameter` returns `Texture` (was `Texture2D`) | Binary + source break |
| C# | `VisualShaderNodeCubemap.cube_map`, `VisualShaderNodeTexture2DArray.texture_array` now `TextureLayered` | Binary + source break |
| UI | `GraphEdit.frame_rect_changed` `new_rect` is `Rect2` (was `Vector2`) | |
| Physics | CSG uses Manifold; non-manifold meshes unsupported in CSG | |
| Core | `Curve` enforces value range | |
| Export | `@export_file` stores `uid://` references | GDScript annotation |
| Android | Sensor events no longer enabled by default | n/a |
