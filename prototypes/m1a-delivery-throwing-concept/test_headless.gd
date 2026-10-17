# PROTOTYPE - NOT FOR PRODUCTION
# Question: smoke-test the M-1a prototype paths headless (no rendering).
# Date: 2026-10-15
extends SceneTree

func _init() -> void:
	var main = load("res://main.tscn").instantiate()
	root.add_child(main)
	await process_frame
	await process_frame
	var ok := true
	# 1. PO focus + take mail
	for i in range(3):
		main.pending.append(main._make_item())
	main.player.global_position = Vector3(0, 0.05, -29)
	main._update_focus()
	print("focus near PO: ", main.focus.type)
	ok = ok and main.focus.type == "po"
	while main.hotbar.size() < 3 and main.pending.size() > 0:
		main.hotbar.append(main.pending.pop_front())
	# 2. hand delivery, right and wrong
	var target = main.mailboxes[0]
	main.player.global_position = Vector3(target.pos.x + 1.0, 0.05, target.pos.z)
	main.hotbar = [{"kind": "Letter", "addr": target.addr, "value": 8}, {"kind": "Medium", "addr": "nowhere", "value": 70}]
	main.selected = 0
	main._update_focus()
	print("focus near mailbox: ", main.focus.type)
	ok = ok and main.focus.type == "mailbox"
	main._hand_deliver(target)
	print("money after right hand delivery: ", main.money)
	ok = ok and main.money == 8
	main.selected = 0
	main._hand_deliver(target)
	print("money after wrong hand delivery: ", main.money)
	ok = ok and main.money == 8 - 35
	# 3. find an aim that lands a throw in a mailbox, then throw it for real
	main.money = 0
	var found := false
	main.player.global_position = Vector3(0, 0.05, 0)
	for yaw_i in range(0, 360, 2):
		for pit_i in range(-10, 40, 2):
			main.player.rotation.y = deg_to_rad(yaw_i)
			main.pivot.rotation.x = deg_to_rad(pit_i)
			var p: Vector3 = main._aim_origin()
			var v: Vector3 = main._aim_dir() * 14.0
			var res := {"state": "fly"}
			var t := 0.0
			while t < 3.0:
				res = main._step(p, v, 1.0 / 60.0)
				p = res.pos
				v = res.vel
				t += 1.0 / 60.0
				if res.state != "fly":
					break
			if res.state == "mailbox":
				found = true
				main.hotbar = [{"kind": "Letter", "addr": res.mailbox.addr, "value": 8}]
				main.selected = 0
				main._throw()
				break
		if found:
			break
	print("found a mailbox-landing aim: ", found)
	ok = ok and found
	for i in range(240):
		await physics_frame
	print("throw_right=", main.throw_right, " throw_wrong=", main.throw_wrong, " miss=", main.throw_miss, " money=", main.money)
	ok = ok and main.throw_right == 1 and main.money == 8
	# 4. a throw that misses lands on the ground and can be picked up
	main.hotbar = [{"kind": "Letter", "addr": "x", "value": 8}]
	main.selected = 0
	main.player.rotation.y = 0.0
	main.pivot.rotation.x = deg_to_rad(-30)
	main._throw()
	for i in range(240):
		await physics_frame
	print("misses=", main.throw_miss, " world_items=", main.world_items.size())
	ok = ok and main.throw_miss == 1 and main.world_items.size() == 1
	main._write_stats()
	print("RESULT: ", "PASS" if ok else "FAIL")
	quit(0 if ok else 1)
