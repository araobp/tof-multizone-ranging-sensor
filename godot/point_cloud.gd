extends Node3D

const POINT = preload("res://point.tscn")

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	add_point(0.1, 0.1, 0.1, 0.05)
	pass # Replace with function body.


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	pass
	
	
func add_point(x: float, y: float, z: float, r: float) -> Object:
	var instance = POINT.instantiate()

	instance.position = Vector3(x, y, z)
	var s = 2 * r
	instance.scale = Vector3(s, s, s)
	
	get_tree().current_scene.add_child(instance)
	
	return instance
