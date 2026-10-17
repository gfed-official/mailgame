extends SceneTree
func _init():
	for s in ["display/window/size/min_width","display/window/size/minimum_width","display/window/stretch/aspect","display/window/stretch/mode","display/window/stretch/scale_mode","display/window/size/viewport_width"]:
		print(s, " has=", ProjectSettings.has_setting(s), " val=", ProjectSettings.get_setting(s, "<none>"))
	for p in ProjectSettings.get_property_list():
		if p.name in ["display/window/stretch/aspect","display/window/stretch/mode"]:
			print(p.name, " hint=", p.hint, " str=", p.hint_string)
	for p in ProjectSettings.get_property_list():
		if str(p.name).begins_with("display/window/size/"): print(p.name)
	print("sub exists via DirAccess res: ", DirAccess.dir_exists_absolute("res://sub"), " file: ", FileAccess.file_exists("res://sub/a.txt"))
	print("res root dirs: ", DirAccess.get_directories_at("res://"))
	var cls = ClassDB.class_get_property_list("Window")
	for p in cls:
		if p.name in ["min_size","content_scale_aspect","content_scale_mode","content_scale_factor","content_scale_size"]: print("Window.", p.name)
	print("AccessibilityLiveMode enum: ", ClassDB.class_get_enum_constants("AccessibilityServer","AccessibilityLiveMode"))
	print("Control.accessibility_live: ", ClassDB.class_has_method("Control","get_accessibility_live"))
	print("Window aspect enum: ", ClassDB.class_get_enum_constants("Window","ContentScaleAspect"))
	quit()
