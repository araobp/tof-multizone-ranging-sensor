extends Node3D

const POINT = preload("res://point.tscn")
@export var POINT_RADIUS = 0.01

# Called when the node enters the scene tree for the first time.
func _ready() -> void:	

	var d = 1.0
	for n in 64:
		var pos = test_8x8(d, n)
		print("[", d, ",", n, "] ", pos)
		add_point(pos, POINT_RADIUS)
	pass # Replace with function body.


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	pass
	
	
func add_point(pos: Vector3, r: float) -> Object:
	var instance = POINT.instantiate()

	instance.position = pos
	var s = 2 * r
	instance.scale = Vector3(s, s, s)
	
	get_tree().current_scene.add_child(instance)
	
	return instance
	
		
func test_8x8(d, n):
	var m = n % 8
	var theta_x = deg_to_rad(5.625 * (m - 4) + 2.8125)
	var x = d * sin (theta_x)
	
	var theta_y = deg_to_rad(5.625 * (int((n - m) / 8) - 4) + 2.8125)
	var y = d * sin(theta_y)
	var z = d * cos(theta_x) * cos(theta_y)

	return Vector3(x, y, z)
