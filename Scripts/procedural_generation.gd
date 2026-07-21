extends TileMapLayer

var main_seed: int

var height = FastNoiseLite.new()
var temperature = FastNoiseLite.new()
var moisture = FastNoiseLite.new()

const MAP_WIDTH = 100
const MAP_HEIGHT = 100
const NUM_REGIONS = 40

enum Biome { WATER, DESERT, PLAINS, FOREST, SWAMP, SNOW }
const BIOME_TILES = {
	Biome.WATER: Vector2i(0, 0),
	Biome.DESERT: Vector2i(0, 2),
	Biome.PLAINS: Vector2i(1, 0),
	Biome.FOREST: Vector2i(2, 0),
	Biome.SWAMP: Vector2i(3, 0),
	Biome.SNOW: Vector2i(0, 1)
}

class Region:
	var center: Vector2
	var total_height: float = 0.0
	var total_temp: float = 0.0
	var total_moist: float = 0.0
	var tile_count: int = 0
	
	var avg_height: float = 0.0
	var avg_temp: float = 0.0
	var avg_moist: float = 0.0
	var dominant_biome: Biome

func _ready() -> void:
	main_seed = randi()
	_initialize_noise()
	_generate_voronoi_world()

func _initialize_noise() -> void:
	height.noise_type = FastNoiseLite.TYPE_PERLIN
	temperature.noise_type = FastNoiseLite.TYPE_PERLIN
	moisture.noise_type = FastNoiseLite.TYPE_PERLIN
	
	height.seed = main_seed
	temperature.seed = main_seed + 1234
	moisture.seed = main_seed + 5678
	
	height.frequency = 0.01
	temperature.frequency = 0.005
	moisture.frequency = 0.01

func _generate_voronoi_world() -> void:
	# GENERATE VORONOI CENTERS
	var regions: Array[Region] = []
	for i in range(NUM_REGIONS):
		var reg = Region.new()
		# Random position on the map
		reg.center = Vector2(randf_range(0, MAP_WIDTH), randf_range(0, MAP_HEIGHT))
		regions.append(reg)
	
	var tile_region_map = []
	for x in range(MAP_WIDTH):
		tile_region_map.append([])
		for y in range(MAP_HEIGHT):
			tile_region_map[x].append(-1)

	# ASSIGN TILES TO A VORONOI REGION AND COLLECT REGION DATA
	for x in range(MAP_WIDTH):
		for y in range(MAP_HEIGHT):
			var current_pos = Vector2(x, y)
			
			# Find closes voronoi center
			var closest_region_idx = 0
			var min_dist = INF
			
			for i in range(regions.size()):
				var dist = current_pos.distance_to(regions[i].center)
				if dist < min_dist:
					min_dist = dist
					closest_region_idx = i
			
			tile_region_map[x][y] = closest_region_idx
			
			var h_val = height.get_noise_2d(x, y)
			var t_val = temperature.get_noise_2d(x, y)
			var m_val = moisture.get_noise_2d(x, y)
			
			var reg = regions[closest_region_idx]
			reg.total_height += h_val
			reg.total_temp += t_val
			reg.total_moist += m_val
			reg.tile_count += 1

	# CALCULATE AVERAGE VORONOI REGION DATA AND ASSIGN DOMINANT BIOME
	for reg in regions:
		if reg.tile_count > 0:
			reg.avg_height = reg.total_height / reg.tile_count
			reg.avg_temp = reg.total_temp / reg.tile_count
			reg.avg_moist = reg.total_moist / reg.tile_count
			
			# the higher the height, the lower the temp
			if reg.avg_height > 0.2:
				reg.avg_temp -= (reg.avg_height * 0.5)
			
			# assign dominant biome to voronoi region
			reg.dominant_biome = _calculate_biome(reg.avg_height, reg.avg_temp, reg.avg_moist)

	# DRAW THE MAP
	for x in range(MAP_WIDTH):
		for y in range(MAP_HEIGHT):
			var region_idx = tile_region_map[x][y]
			var associated_region = regions[region_idx]
			
			# Get tile from assigned dominant biome
			var biome_type = associated_region.dominant_biome
			var tile_coords = BIOME_TILES[biome_type]
			
			set_cell(Vector2i(x, y), 0, tile_coords)

func _calculate_biome(h: float, t: float, m: float) -> Biome:
	if h < -0.15:
		return Biome.WATER
		
	if t < -0.2:
		return Biome.SNOW
		
	if t > 0.1:
		if m < -0.1:
			return Biome.DESERT
		else:
			return Biome.FOREST
			
	if m > 0.1:
		return Biome.SWAMP
	else:
		return Biome.PLAINS


func _on_button_pressed() -> void:
	main_seed = randi()
	_initialize_noise()
	_generate_voronoi_world()
