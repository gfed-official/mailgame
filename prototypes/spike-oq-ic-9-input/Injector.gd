extends Node
# Throwaway GDScript injector for the allocation probe (AC-6).
# Purpose: create InputEvents NATIVELY (no C# wrapper exists for them), buffer them with
# Input.parse_input_event and flush, so the C# _Input receiver has to build a fresh managed
# wrapper per event -- the same situation as an OS-generated event.
# kind: "key" | "motion" | "joymotion" | "button"; n events; flush_every events per flush.
func run(kind: String, n: int, flush_every: int) -> void:
	var pending := 0
	for i in n:
		var ev: InputEvent
		match kind:
			"key":
				var k := InputEventKey.new()
				k.keycode = KEY_F5
				k.physical_keycode = KEY_F5
				k.pressed = (i % 2) == 0
				ev = k
			"motion":
				var m := InputEventMouseMotion.new()
				m.position = Vector2(100, 100)
				m.relative = Vector2(1, 0)
				m.screen_relative = Vector2(1, 0)
				ev = m
			"joymotion":
				var j := InputEventJoypadMotion.new()
				j.device = 0
				j.axis = JOY_AXIS_LEFT_X
				j.axis_value = float(i % 100) / 100.0
				ev = j
			"touch":
				var t := InputEventScreenTouch.new()
				t.index = 0
				t.position = Vector2(2000, 2000)
				t.pressed = (i % 2) == 0
				ev = t
			"button":
				var b := InputEventMouseButton.new()
				b.button_index = MOUSE_BUTTON_RIGHT
				b.position = Vector2(2000, 2000)
				b.pressed = (i % 2) == 0
				ev = b
		Input.parse_input_event(ev)
		pending += 1
		if pending >= flush_every:
			Input.flush_buffered_events()
			pending = 0
	Input.flush_buffered_events()
