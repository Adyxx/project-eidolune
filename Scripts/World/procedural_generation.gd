extends TileMapLayer
class_name WorldGenerator

var height = FastNoiseLite.new()
var temperature = FastNoiseLite.new()
var moisture = FastNoiseLite.new()

const MAP_WIDTH = 300
const MAP_HEIGHT = 300

class World:
	var seed: int
	var regions: Array[Region] = []
	var rivers: Array[River] = []
	var roads: Array[Road] = []

class River:
	var name: String
	var path: PackedVector2Array
	
class Road:
	var name: String
	var path: PackedVector2Array
	
class Region extends RefCounted:
	var definition: RegionDefinition
	var center : Vector2
	
	var sectors: Array[Sector] = []

class Sector extends RefCounted:
	var definition: SectorDefinition
	var center : Vector2
	
	var landmarks: Array[Landmark] = []

class Landmark extends RefCounted:
	var definition: LandmarkDefinition
	var position: Vector2



func _initialize_noise(s) -> void:
	height.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	temperature.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	moisture.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	
	height.seed = s
	temperature.seed = s + 1234
	moisture.seed = s + 5678

	height.frequency = 0.01
	temperature.frequency = 0.003
	moisture.frequency = 0.004

func _initialize_world(w) -> void:
	pass


func _generate_world() -> void:
	pass


func _ready() -> void:
	var world = World.new()
	world.seed = randi()
	
	_initialize_noise(world.seed)
	_initialize_world(world)
	_generate_world()

func _on_button_pressed() -> void:
	var world = World.new()
	world.seed = randi()
	
	_initialize_noise(world.seed)
	_initialize_world(world)
	_generate_world()
