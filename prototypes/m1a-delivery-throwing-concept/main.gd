# PROTOTYPE - NOT FOR PRODUCTION
# Question: If the player hand-delivers mail by matching addresses and can throw it
#           along an arc, will they choose to throw and enjoy it, even with real
#           misdelivery risk? (spec 13 §6, M-1a)
# Date: 2026-10-15
extends Node3D

# ---- tunables (hardcoded on purpose) ----
const GRAVITY := 9.8
const MAX_FLIGHT := 3.0
const LOFT_DEG := 6.0
const WALK_SPEED := 5.0
const MOUSE_SENS := 0.0025
const DELIVER_RANGE := 2.5
const DELIVER_HOLD := 0.4
const PICKUP_RANGE := 2.0
const PO_RANGE := 5.0
const MAILBOX_HALF := Vector3(0.6, 0.7, 0.6)
const HOTBAR_SIZE := 8
const SPAWN_EVERY := 5.0
const MAX_PENDING := 14
const MISDELIVERY_RATIO := 0.5
const SESSION_SECONDS := 600.0

# kind: value (¢), throw speed (light 14 / medium 10 / heavy 6, spec 13 §4.1), pick weight (spec 03 §1.3 shift 1)
const KINDS := {
	"Letter":  {"value": 8,   "speed": 14.0, "weight": 60, "color": Color(0.95, 0.95, 0.9), "size": Vector3(0.25, 0.04, 0.18)},
	"Small":   {"value": 30,  "speed": 14.0, "weight": 30, "color": Color(0.80, 0.65, 0.40), "size": Vector3(0.30, 0.15, 0.20)},
	"Medium":  {"value": 70,  "speed": 10.0, "weight": 10, "color": Color(0.60, 0.42, 0.25), "size": Vector3(0.40, 0.30, 0.30)},
}

# ---- state ----
var player: CharacterBody3D
var pivot: Node3D
var cam: Camera3D
var pitch := 0.0
var money := 0
var hotbar: Array = []          # items: {kind, addr, value}
var selected := 0
var mailboxes: Array = []       # {addr, pos}
var house_boxes: Array = []     # AABB
var pending: Array = []         # mail waiting at the PO
var world_items: Array = []     # {item, node}
var thrown: Array = []          # {item, node, pos, vel, t}
var po_pos := Vector3(0, 0, -30)
var aiming := false
var e_prev := false
var hold_t := 0.0
var hold_locked := false
var spawn_t := 0.0
var elapsed := 0.0
var session_ended := false
var focus := {"type": "none"}

# stats (the hypothesis signal)
var hand_right := 0
var hand_wrong := 0
var throw_right := 0
var throw_wrong := 0
var throw_miss := 0
var throws_started := 0

# nodes
var arc_mesh: ImmediateMesh
var arc_inst: MeshInstance3D
var arc_mat: StandardMaterial3D
var land_marker: MeshInstance3D
var head_label: Label3D
var po_label: Label3D
var money_l: Label
var hotbar_l: Label
var prompt_l: Label
var toast_l: Label
var stats_l: Label
var flash: ColorRect
var toast_tween: Tween
var flash_tween: Tween

func _mat(c: Color) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	return m

func _box(parent: Node, pos: Vector3, size: Vector3, c: Color, solid := false) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = size
	mi.mesh = bm
	mi.material_override = _mat(c)
	mi.position = pos
	parent.add_child(mi)
	if solid:
		var sb := StaticBody3D.new()
		var cs := CollisionShape3D.new()
		var bs := BoxShape3D.new()
		bs.size = size
		cs.shape = bs
		sb.add_child(cs)
		mi.add_child(sb)
	return mi

func _label3d(parent: Node, text: String, pos: Vector3, px := 0.01, size := 48) -> Label3D:
	var l := Label3D.new()
	l.text = text
	l.position = pos
	l.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	l.pixel_size = px
	l.font_size = size
	l.outline_size = 12
	l.no_depth_test = true
	parent.add_child(l)
	return l

func _ready() -> void:
	randomize()
	_build_world()
	_build_player()
	_build_hud()
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED

func _build_world() -> void:
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.55, 0.75, 0.95)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.75, 0.75, 0.78)
	var we := WorldEnvironment.new()
	we.environment = env
	add_child(we)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-55, 30, 0)
	add_child(sun)
	# ground + road
	_box(self, Vector3(0, -0.5, 0), Vector3(80, 1, 90), Color(0.35, 0.6, 0.3), true)
	_box(self, Vector3(0, 0.01, 0), Vector3(8, 0.02, 70), Color(0.25, 0.25, 0.28))
	# houses: odd numbers west, even east, 5 each side
	var zs := [-16.0, -8.0, 0.0, 8.0, 16.0]
	for i in range(5):
		_make_house(-9.0, zs[i], 1 + i * 2, Color(0.85, 0.55, 0.5))
		_make_house(9.0, zs[i], 2 + i * 2, Color(0.5, 0.6, 0.85))
	# post office
	_box(self, Vector3(0, 2.5, -34), Vector3(14, 5, 6), Color(0.75, 0.2, 0.2), true)
	house_boxes.append(AABB(Vector3(-7, 0, -37), Vector3(14, 5, 6)))
	_label3d(self, "POST OFFICE", Vector3(0, 6.0, -34), 0.012, 64)
	po_label = _label3d(self, "", Vector3(0, 1.6, -29.5), 0.008, 48)

func _make_house(x: float, z: float, num: int, c: Color) -> void:
	var size := Vector3(6, 4, 6)
	_box(self, Vector3(x, 2.0, z), size, c, true)
	house_boxes.append(AABB(Vector3(x, 2.0, z) - size * 0.5, size))
	var addr := "%d Elm St" % num
	_label3d(self, addr, Vector3(x, 4.8, z), 0.01, 56)
	var mx := -4.6 if x < 0 else 4.6
	var mpos := Vector3(mx, 0.7, z)
	_box(self, mpos, Vector3(0.5, 0.9, 0.7), Color(0.15, 0.2, 0.7))
	_label3d(self, str(num), mpos + Vector3(0, 0.9, 0), 0.006, 48)
	mailboxes.append({"addr": addr, "pos": mpos})

func _build_player() -> void:
	player = CharacterBody3D.new()
	var cs := CollisionShape3D.new()
	var cap := CapsuleShape3D.new()
	cap.radius = 0.4
	cap.height = 1.8
	cs.shape = cap
	cs.position = Vector3(0, 0.9, 0)
	player.add_child(cs)
	var mi := MeshInstance3D.new()
	var cm := CapsuleMesh.new()
	cm.radius = 0.4
	cm.height = 1.8
	mi.mesh = cm
	mi.material_override = _mat(Color(0.95, 0.8, 0.2))
	mi.position = Vector3(0, 0.9, 0)
	player.add_child(mi)
	player.position = Vector3(0, 0.05, -26)
	player.rotate_y(PI)  # face +z, down the street
	add_child(player)
	pivot = Node3D.new()
	pivot.position = Vector3(0, 1.6, 0)
	player.add_child(pivot)
	cam = Camera3D.new()
	cam.position = Vector3(0.6, 0.4, 3.8)
	pivot.add_child(cam)
	cam.current = true
	head_label = _label3d(player, "", Vector3(0, 2.4, 0), 0.006, 48)
	# arc preview (top-level so it lives in world space)
	arc_mesh = ImmediateMesh.new()
	arc_inst = MeshInstance3D.new()
	arc_inst.mesh = arc_mesh
	arc_mat = StandardMaterial3D.new()
	arc_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	arc_inst.material_override = arc_mat
	arc_inst.top_level = true
	add_child(arc_inst)
	land_marker = MeshInstance3D.new()
	var sm := SphereMesh.new()
	sm.radius = 0.25
	sm.height = 0.5
	land_marker.mesh = sm
	var lm := StandardMaterial3D.new()
	lm.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	land_marker.material_override = lm
	land_marker.top_level = true
	land_marker.visible = false
	add_child(land_marker)

func _hud_label(size := 24) -> Label:
	var l := Label.new()
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_constant_override("outline_size", 8)
	l.add_theme_color_override("font_outline_color", Color.BLACK)
	return l

func _build_hud() -> void:
	var hud := CanvasLayer.new()
	add_child(hud)
	flash = ColorRect.new()
	flash.color = Color(1, 1, 1, 0)
	flash.mouse_filter = Control.MOUSE_FILTER_IGNORE
	flash.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	hud.add_child(flash)
	money_l = _hud_label(32)
	money_l.position = Vector2(16, 8)
	hud.add_child(money_l)
	hotbar_l = _hud_label(22)
	hotbar_l.position = Vector2(16, 60)
	hud.add_child(hotbar_l)
	stats_l = _hud_label(18)
	stats_l.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	stats_l.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	stats_l.offset_right = -16
	stats_l.offset_top = 8
	hud.add_child(stats_l)
	toast_l = _hud_label(54)
	toast_l.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	toast_l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	toast_l.offset_top = 130
	toast_l.modulate.a = 0.0
	hud.add_child(toast_l)
	prompt_l = _hud_label(26)
	prompt_l.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	prompt_l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	prompt_l.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
	prompt_l.offset_bottom = -90
	hud.add_child(prompt_l)
	var cross := _hud_label(28)
	cross.text = "+"
	cross.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	cross.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	cross.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	hud.add_child(cross)
	var help := _hud_label(16)
	help.text = "WASD move | mouse look | 1-8 / wheel / Q select | E near PO: take mail | E (hold) at mailbox: hand-deliver | hold LMB: aim, release: THROW | E near item: pick up | Esc: free mouse | F2: save stats"
	help.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	help.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
	help.offset_left = 12
	help.offset_bottom = -8
	hud.add_child(help)

# ---------------- input ----------------
func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		player.rotate_y(-event.relative.x * MOUSE_SENS)
		pitch = clamp(pitch - event.relative.y * MOUSE_SENS, -1.0, 0.7)
		pivot.rotation.x = pitch
	elif event is InputEventMouseButton:
		if Input.mouse_mode != Input.MOUSE_MODE_CAPTURED and event.pressed:
			Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
			return
		if event.button_index == MOUSE_BUTTON_LEFT:
			if event.pressed:
				aiming = hotbar.size() > 0
			elif aiming:
				aiming = false
				_throw()
		elif event.pressed and event.button_index == MOUSE_BUTTON_WHEEL_UP:
			_cycle(-1)
		elif event.pressed and event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			_cycle(1)
	elif event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_ESCAPE:
			Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		elif event.keycode == KEY_Q:
			_cycle(1)
		elif event.keycode == KEY_F2:
			_write_stats()
		elif event.keycode >= KEY_1 and event.keycode <= KEY_8:
			selected = min(event.keycode - KEY_1, max(hotbar.size() - 1, 0))

func _cycle(d: int) -> void:
	if hotbar.size() > 0:
		selected = posmod(selected + d, hotbar.size())

# ---------------- simulation ----------------
func _physics_process(delta: float) -> void:
	elapsed += delta
	_move(delta)
	_spawn(delta)
	_update_focus()
	_handle_interact(delta)
	_step_thrown(delta)
	_update_arc()
	_update_hud()
	if not session_ended and elapsed >= SESSION_SECONDS:
		session_ended = true
		_sting("10:00 - STOP. Ask: want to play again?\nWhat was confusing?", Color(1, 0.9, 0.3))
		_write_stats()

func _move(delta: float) -> void:
	var dir := Vector3.ZERO
	var b := player.global_transform.basis
	if Input.is_physical_key_pressed(KEY_W): dir -= b.z
	if Input.is_physical_key_pressed(KEY_S): dir += b.z
	if Input.is_physical_key_pressed(KEY_A): dir -= b.x
	if Input.is_physical_key_pressed(KEY_D): dir += b.x
	dir.y = 0
	dir = dir.normalized()
	player.velocity.x = dir.x * WALK_SPEED
	player.velocity.z = dir.z * WALK_SPEED
	if not player.is_on_floor():
		player.velocity.y -= GRAVITY * delta
	else:
		player.velocity.y = 0
	player.move_and_slide()

func _spawn(delta: float) -> void:
	spawn_t += delta
	if spawn_t >= SPAWN_EVERY:
		spawn_t = 0.0
		if pending.size() < MAX_PENDING:
			pending.append(_make_item())
	po_label.text = "%d mail waiting" % pending.size()

func _make_item() -> Dictionary:
	var total := 0
	for k in KINDS: total += KINDS[k].weight
	var r := randi() % total
	var kind := "Letter"
	for k in KINDS:
		r -= KINDS[k].weight
		if r < 0:
			kind = k
			break
	var m = mailboxes.pick_random()
	return {"kind": kind, "addr": m.addr, "value": KINDS[kind].value}

func _hdist(a: Vector3, b: Vector3) -> float:
	return Vector2(a.x - b.x, a.z - b.z).length()

func _update_focus() -> void:
	focus = {"type": "none"}
	var pp := player.global_position
	if hotbar.size() > 0:
		var best := DELIVER_RANGE
		for m in mailboxes:
			var d := _hdist(pp, m.pos)
			if d <= best:
				best = d
				focus = {"type": "mailbox", "m": m}
		if focus.type == "mailbox":
			return
	var bestg := PICKUP_RANGE
	for wi in world_items:
		var d := _hdist(pp, wi.node.global_position)
		if d <= bestg:
			bestg = d
			focus = {"type": "ground", "wi": wi}
	if focus.type == "ground":
		return
	if pending.size() > 0 and _hdist(pp, po_pos) <= PO_RANGE:
		focus = {"type": "po"}

func _handle_interact(delta: float) -> void:
	var e_down := Input.is_physical_key_pressed(KEY_E)
	var just := e_down and not e_prev
	e_prev = e_down
	if not e_down:
		hold_t = 0.0
		hold_locked = false
	match focus.type:
		"mailbox":
			if e_down and not hold_locked:
				hold_t += delta
				if hold_t >= DELIVER_HOLD:
					_hand_deliver(focus.m)
					hold_locked = true
					hold_t = 0.0
			else:
				hold_t = 0.0 if not e_down else hold_t
		"ground":
			if just and hotbar.size() < HOTBAR_SIZE:
				hotbar.append(focus.wi.item)
				focus.wi.node.queue_free()
				world_items.erase(focus.wi)
		"po":
			if just:
				while hotbar.size() < HOTBAR_SIZE and pending.size() > 0:
					hotbar.append(pending.pop_front())

func _accept(item: Dictionary, m: Dictionary, via: String) -> bool:
	# spec 03 §2.2 rules 3 and 5 (timeliness = 1.0 in this prototype)
	var right: bool = item.addr == m.addr
	if right:
		money += item.value
		_sting("CHA-CHING  +%d" % item.value, Color(0.3, 1.0, 0.4))
	else:
		var pen := int(item.value * MISDELIVERY_RATIO)
		money -= pen
		_sting("THUD - WRONG HOUSE  -%d\n(it was for %s)" % [pen, item.addr], Color(1.0, 0.35, 0.3))
	print("[%.1f] %s %s item=%s->%s box=%s money=%d" % [elapsed, via, "RIGHT" if right else "WRONG", item.kind, item.addr, m.addr, money])
	return right

func _hand_deliver(m: Dictionary) -> void:
	if hotbar.size() == 0:
		return
	var item = hotbar[selected]
	hotbar.remove_at(selected)
	selected = clampi(selected, 0, max(hotbar.size() - 1, 0))
	if _accept(item, m, "HAND"):
		hand_right += 1
	else:
		hand_wrong += 1

# ---------------- throwing ----------------
func _aim_dir() -> Vector3:
	var d := -pivot.global_transform.basis.z.normalized()
	return d.rotated(pivot.global_transform.basis.x.normalized(), deg_to_rad(LOFT_DEG)).normalized()

func _aim_origin() -> Vector3:
	return pivot.global_position + (-pivot.global_transform.basis.z.normalized()) * 0.6

# one ballistic step. returns {state, pos, vel, mailbox}
func _step(p: Vector3, v: Vector3, dt: float) -> Dictionary:
	v.y -= GRAVITY * dt
	var np := p + v * dt
	for m in mailboxes:
		var box := AABB(m.pos - MAILBOX_HALF, MAILBOX_HALF * 2.0)
		if box.has_point(np):
			return {"state": "mailbox", "pos": np, "vel": v, "mailbox": m}
	for hb in house_boxes:
		if hb.has_point(np):
			return {"state": "ground", "pos": Vector3(p.x, max(p.y, 0.15), p.z), "vel": v}
	if np.y <= 0.15:
		return {"state": "ground", "pos": Vector3(np.x, 0.15, np.z), "vel": v}
	return {"state": "fly", "pos": np, "vel": v}

func _update_arc() -> void:
	arc_mesh.clear_surfaces()
	land_marker.visible = false
	if not aiming or hotbar.size() == 0:
		return
	var item = hotbar[selected]
	var p := _aim_origin()
	var v: Vector3 = _aim_dir() * float(KINDS[item.kind].speed)
	var pts: Array = [p]
	var result := {"state": "fly"}
	var t := 0.0
	while t < MAX_FLIGHT:
		result = _step(p, v, 1.0 / 60.0)
		p = result.pos
		v = result.vel
		pts.append(p)
		t += 1.0 / 60.0
		if result.state != "fly":
			break
	var c := Color.WHITE
	if result.state == "mailbox":
		c = Color(0.3, 1.0, 0.4) if result.mailbox.addr == item.addr else Color(1.0, 0.3, 0.3)
	arc_mat.albedo_color = c
	if pts.size() >= 2:
		arc_mesh.surface_begin(Mesh.PRIMITIVE_LINE_STRIP)
		for q in pts:
			arc_mesh.surface_add_vertex(q)
		arc_mesh.surface_end()
	(land_marker.material_override as StandardMaterial3D).albedo_color = c
	land_marker.global_position = p
	land_marker.visible = true

func _item_node(item: Dictionary, with_label := false) -> Node3D:
	var k = KINDS[item.kind]
	var n := Node3D.new()
	var mi := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = k.size
	mi.mesh = bm
	mi.material_override = _mat(k.color)
	n.add_child(mi)
	if with_label:
		_label3d(n, item.addr, Vector3(0, 0.5, 0), 0.004, 48)
	add_child(n)
	return n

func _throw() -> void:
	if hotbar.size() == 0:
		return
	var item = hotbar[selected]
	hotbar.remove_at(selected)
	selected = clampi(selected, 0, max(hotbar.size() - 1, 0))
	throws_started += 1
	var node := _item_node(item)
	var pos := _aim_origin()
	node.global_position = pos
	thrown.append({"item": item, "node": node, "pos": pos, "vel": _aim_dir() * KINDS[item.kind].speed, "t": 0.0})

func _land(item: Dictionary, pos: Vector3) -> void:
	var n := _item_node(item, true)
	n.global_position = pos
	world_items.append({"item": item, "node": n})

func _step_thrown(delta: float) -> void:
	for th in thrown.duplicate():
		var r := _step(th.pos, th.vel, delta)
		th.t += delta
		th.pos = r.pos
		th.vel = r.vel
		th.node.global_position = th.pos
		var done := false
		if r.state == "mailbox":
			if _accept(th.item, r.mailbox, "THROW"):
				throw_right += 1
			else:
				throw_wrong += 1
			th.node.queue_free()
			done = true
		elif r.state == "ground" or th.t >= MAX_FLIGHT:
			throw_miss += 1
			print("[%.1f] THROW MISS item=%s->%s" % [elapsed, th.item.kind, th.item.addr])
			th.node.queue_free()
			_land(th.item, Vector3(th.pos.x, 0.15, th.pos.z))
			_sting("miss...", Color(0.85, 0.85, 0.85), 0.5)
			done = true
		if done:
			thrown.erase(th)

# ---------------- feedback + HUD ----------------
func _sting(text: String, c: Color, hold := 1.4) -> void:
	toast_l.text = text
	toast_l.add_theme_color_override("font_color", c)
	toast_l.modulate.a = 1.0
	if toast_tween:
		toast_tween.kill()
	toast_tween = create_tween()
	toast_tween.tween_interval(hold * 0.5)
	toast_tween.tween_property(toast_l, "modulate:a", 0.0, hold * 0.5)
	if hold > 1.0:
		flash.color = Color(c.r, c.g, c.b, 0.3)
		if flash_tween:
			flash_tween.kill()
		flash_tween = create_tween()
		flash_tween.tween_property(flash, "color:a", 0.0, 0.35)

func _update_hud() -> void:
	money_l.text = "$%d  (%d:%02d)" % [money, int(elapsed) / 60, int(elapsed) % 60]
	var lines := PackedStringArray()
	for i in range(hotbar.size()):
		var it = hotbar[i]
		lines.append("%s[%d] %-6s -> %s" % [">" if i == selected else " ", i + 1, it.kind, it.addr])
	if hotbar.size() == 0:
		lines.append("(hands empty - take mail at the Post Office)")
	hotbar_l.text = "\n".join(lines)
	if hotbar.size() > 0:
		head_label.text = "%s -> %s" % [hotbar[selected].kind, hotbar[selected].addr]
	else:
		head_label.text = ""
	stats_l.text = "Hand  right %d / wrong %d\nThrow right %d / wrong %d / miss %d" % [hand_right, hand_wrong, throw_right, throw_wrong, throw_miss]
	match focus.type:
		"mailbox":
			var held = hotbar[selected]
			var ok: bool = held.addr == focus.m.addr
			prompt_l.text = "[E hold %d%%] deliver  held: %s  box: %s  %s" % [int(clampf(hold_t / DELIVER_HOLD, 0.0, 1.0) * 100.0), held.addr, focus.m.addr, "MATCH" if ok else "MISMATCH"]
		"ground":
			prompt_l.text = "[E] pick up %s -> %s" % [focus.wi.item.kind, focus.wi.item.addr]
		"po":
			prompt_l.text = "[E] take mail (%d waiting)" % pending.size()
		_:
			prompt_l.text = ""

func _write_stats() -> void:
	var s := "t=%.0fs money=%d hand_right=%d hand_wrong=%d throws=%d throw_right=%d throw_wrong=%d throw_miss=%d\n" % [elapsed, money, hand_right, hand_wrong, throws_started, throw_right, throw_wrong, throw_miss]
	var f := FileAccess.open("user://playtest_stats.txt", FileAccess.READ_WRITE)
	if f == null:
		f = FileAccess.open("user://playtest_stats.txt", FileAccess.WRITE)
	f.seek_end()
	f.store_string(s)
	f.close()
	print("STATS: " + s.strip_edges())

func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_CLOSE_REQUEST:
		_write_stats()
