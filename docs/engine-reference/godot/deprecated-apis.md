# Godot — Deprecated / Renamed APIs (4.3 → 4.7)

Last verified: 2026-10-15. Only renames and removals stated on the official upgrade pages (see breaking-changes.md for sources). The pages list removals and renames; a separate deprecation list was not fetched — NOT SOURCEABLE for "deprecated but still present" APIs.

| Don't use | Use instead | Since | C# note |
|-----------|-------------|-------|---------|
| `JSONRPC.set_scope` | `JSONRPC.set_method` | 4.5 | breaks C# |
| `Node.get_rpc_config` | `Node.get_node_rpc_config` | 4.5 | GDScript only break |
| `RenderingDevice.Features.Address` | `RenderingDevice.Features.BufferDeviceAddress` | 4.5 | |
| `RenderingServer.instance_reset_physics_interpolation`, `instance_set_interpolated` | (removed — no replacement stated) | 4.5 | |
| `RichTextLabel.add_image(... size_in_percent ...)` | `width_in_percent` / `height_in_percent` (4.5), then `width_unit` / `height_unit` (4.7) | 4.5 / 4.7 | |
| `RichTextLabel.ImageUpdateMask.UPDATE_WIDTH_IN_PERCENT` | `UPDATE_WIDTH_UNIT` | 4.7 | source break |
| `Resource.duplicate(true)` for deep copy of sub-resources | `Resource.duplicate_deep(DEEP_DUPLICATE_ALL)` | 4.5 | behaviour change |
| `AudioEffectSpectrumAnalyzer.tap_back_pos` | (removed) | 4.7 | |
| `StreamPeerTCP.get_status/poll/disconnect_from_host` (declared on TCP) | declared on `StreamPeerSocket` | 4.6 | binary break for `get_status` |
| `TCPServer.is_connection_available/is_listening/stop` (declared on TCP) | declared on `SocketServer` | 4.6 | |
| `FileAccess.get_as_text(skip_cr)` | `get_as_text()` (param removed) | 4.6 | |
| Comparing `InputEvent.device == 0` for mouse/keyboard | `InputEvent.DEVICE_ID_MOUSE` / `DEVICE_ID_KEYBOARD` | 4.7 | |
