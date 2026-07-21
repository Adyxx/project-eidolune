extends TileMapLayer

var main_seed: int

var moisture = FastNoiseLite.new()
var temperature = FastNoiseLite.new()
var height = FastNoiseLite.new()

const WATER_TILE = Vector2i(0,0)
const GRASS_TILE = Vector2i(1,0)
const SNOW_TILE = Vector2i(0,1)
const SAND_TILE = Vector2i(0,2)
const ROCK_TILE = Vector2i(3,2)

const MAP_WIDTH = 200
const MAP_HEIGHT = 200

func _ready() -> void:
	main_seed = randi()
	_initialize_noise()
	_generate_world()
	
	
func _process(delta: float) -> void:
	pass


func _initialize_noise():
	
	height.noise_type = FastNoiseLite.TYPE_PERLIN
	temperature.noise_type = FastNoiseLite.TYPE_PERLIN
	moisture.noise_type = FastNoiseLite.TYPE_PERLIN
	
	height.seed = main_seed
	temperature.seed = main_seed + 1234 # Seed offset
	moisture.seed = main_seed + 5678 # Seed offset
	
	# how rough/smooth the map values are
	height.frequency = 0.02       
	temperature.frequency = 0.01  
	moisture.frequency = 0.03

func _generate_world() -> void:
	for x in range(MAP_WIDTH):
		for y in range(MAP_HEIGHT):
			
			var h_val = height.get_noise_2d(x, y)
			var t_val = temperature.get_noise_2d(x, y)
			var m_val = moisture.get_noise_2d(x, y)
			
			var tile_to_place : Vector2i
			
			if h_val < -0.2:
				tile_to_place = WATER_TILE
			elif h_val < -0.1:
				tile_to_place = SAND_TILE
			else:
				if t_val < -0.3:
					tile_to_place = SNOW_TILE
				elif t_val > 0.3 and m_val < -0.2:
					tile_to_place = SAND_TILE
				elif h_val > 0.4:
					tile_to_place = ROCK_TILE
				else:
					tile_to_place = GRASS_TILE

			set_cell(Vector2i(x, y), 0, tile_to_place)


func _on_button_pressed() -> void:
	main_seed = randi()
	_initialize_noise()
	_generate_world()
