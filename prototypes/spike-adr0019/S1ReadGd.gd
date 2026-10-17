extends Node

func _ready() -> void:
	print("[s1gd] GDScript reader running")
	for p in ["res://content/test.jsonc", "res://content/manifest.txt"]:
		var b: PackedByteArray = FileAccess.get_file_as_bytes(p)
		print("[s1gd] ", p, " bytes=", b.size(), " err=", FileAccess.get_open_error(), " exists=", FileAccess.file_exists(p))
	print("[s1gd] stretch/mode=", ProjectSettings.get_setting("display/window/stretch/mode", "<unset>"), " aspect=", ProjectSettings.get_setting("display/window/stretch/aspect", "<unset>"))
	get_tree().quit()
