extends TileMapLayer

var main_seed: int

var height = FastNoiseLite.new()
var temperature = FastNoiseLite.new()
var moisture = FastNoiseLite.new()
var river_noise = FastNoiseLite.new()

const MAP_WIDTH = 300
const MAP_HEIGHT = 300
const NUM_REGIONS = 400 


@export var debug_mode: bool = false

@export_group("Land Biome Percentages")
@export_range(0, 100) var pct_snow: float = 20.0
@export_range(0, 100) var pct_desert: float = 15.0
@export_range(0, 100) var pct_forest: float = 25.0
@export_range(0, 100) var pct_swamp: float = 15.0
@export_range(0, 100) var pct_plains: float = 25.0

enum Biome { WATER, DESERT, PLAINS, FOREST, SWAMP, SNOW, RIVER, DEBUG_BLACK, DEBUG_GRAY, DEBUG_WHITE }
const BIOME_TILES = {
	Biome.WATER: Vector2i(0, 0),
	Biome.DESERT: Vector2i(0, 2),
	Biome.PLAINS: Vector2i(0, 3),
	Biome.FOREST: Vector2i(3, 0),
	Biome.SWAMP: Vector2i(2, 1),
	Biome.SNOW: Vector2i(0, 1),
	Biome.RIVER: Vector2i(0, 0),

	Biome.DEBUG_BLACK: Vector2i(3,1), 
	Biome.DEBUG_GRAY: Vector2i(2,1), 
	Biome.DEBUG_WHITE: Vector2i(0, 1)  
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
	
	var dominant_biome: Biome = Biome.PLAINS
	var neighbors: Array[Region] = []
	
func _ready() -> void:
	main_seed = randi()
	_initialize_noise()
	if debug_mode:
		_render_noise_debug()
	
		return
		
	_generate_voronoi_world()

func _initialize_noise() -> void:
	height.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	temperature.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	moisture.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	
	river_noise.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	river_noise.fractal_type = FastNoiseLite.FRACTAL_FBM
	river_noise.fractal_octaves = 2
	
	height.seed = main_seed
	temperature.seed = main_seed + 1234
	moisture.seed = main_seed + 5678
	river_noise.seed = main_seed + 8888
	
	height.frequency = 0.01
	temperature.frequency = 0.003
	moisture.frequency = 0.004
	river_noise.frequency = 0.015 

func _render_noise_debug() -> void:
	clear()
	for x in range(MAP_WIDTH):
		for y in range(MAP_HEIGHT):
			var target_pos = Vector2(float(x), float(y))
			
			var raw_noise = river_noise.get_noise_2dv(target_pos)
			
			var r_val = abs(raw_noise)
			
			var debug_biome: Biome
			
			if r_val < 0.06:
				debug_biome = Biome.DEBUG_BLACK
			elif r_val < 0.2:
				debug_biome = Biome.DEBUG_GRAY
			else:
				debug_biome = Biome.DEBUG_WHITE
				
			set_cell(Vector2i(x, y), 0, BIOME_TILES[debug_biome])
			

func _generate_voronoi_world() -> void:
	var regions: Array[Region] = []
	for i in range(NUM_REGIONS):
		var reg = Region.new()
		reg.center = Vector2(randf_range(0, MAP_WIDTH), randf_range(0, MAP_HEIGHT))
		regions.append(reg)
		
	var tile_region_map = []
	var river_mask = [] 
	
	for x in range(MAP_WIDTH):
		tile_region_map.append([])
		river_mask.append([])
		for y in range(MAP_HEIGHT):
			tile_region_map[x].append(-1)
			river_mask[x].append(false)

	for x in range(MAP_WIDTH):
		for y in range(MAP_HEIGHT):
			var fx = float(x)
			var fy = float(y)
			

			var r_val = abs(river_noise.get_noise_2d(fx, fy))
			if r_val < 0.04:
				river_mask[x][y] = true
			
			var current_pos = Vector2(fx, fy)
			var closest_region_idx = 0
			var min_dist = INF
			for i in range(regions.size()):
				var dist = current_pos.distance_to(regions[i].center)
				if dist < min_dist:
					min_dist = dist
					closest_region_idx = i
			
			tile_region_map[x][y] = closest_region_idx
			var reg = regions[closest_region_idx]
			reg.total_height += height.get_noise_2d(fx, fy)
			reg.total_temp += temperature.get_noise_2d(fx, fy)
			reg.total_moist += moisture.get_noise_2d(fx, fy)
			reg.tile_count += 1

	for x in range(MAP_WIDTH - 1):
		for y in range(MAP_HEIGHT - 1):
			var r1 = regions[tile_region_map[x][y]]
			var r2 = regions[tile_region_map[x+1][y]]
			var r3 = regions[tile_region_map[x][y+1]]
			if r1 != r2 and not r1.neighbors.has(r2):
				r1.neighbors.append(r2)
				r2.neighbors.append(r1)
			if r1 != r3 and not r1.neighbors.has(r3):
				r1.neighbors.append(r3)
				r3.neighbors.append(r1)

	var land_regions: Array[Region] = []

	for reg in regions:
		if reg.tile_count > 0:
			reg.avg_height = reg.total_height / reg.tile_count
			reg.avg_temp = reg.total_temp / reg.tile_count
			reg.avg_moist = reg.total_moist / reg.tile_count
			
			if reg.avg_height < -0.05: 
				reg.dominant_biome = Biome.WATER
			else:
				land_regions.append(reg)

	var total_land = land_regions.size()
	var total_quota = pct_snow + pct_desert + pct_forest + pct_swamp + pct_plains
	if total_quota == 0: total_quota = 1.0
	
	var quota_counts = {
		Biome.SNOW: roundi((pct_snow / total_quota) * total_land),
		Biome.DESERT: roundi((pct_desert / total_quota) * total_land),
		Biome.FOREST: roundi((pct_forest / total_quota) * total_land),
		Biome.SWAMP: roundi((pct_swamp / total_quota) * total_land)
	}
	
	for reg in land_regions:
		reg.dominant_biome = Biome.PLAINS
		
	var claimed_regions: Array[Region] = []

	for biome_type in [Biome.SNOW, Biome.DESERT, Biome.FOREST, Biome.SWAMP]:
		var targets_needed = quota_counts[biome_type]
		if targets_needed <= 0: continue
		
		var free_land = land_regions.filter(func(r): return not claimed_regions.has(r))
		if free_land.is_empty(): break
		
		if biome_type == Biome.SNOW:
			free_land.sort_custom(func(a, b): return a.avg_temp < b.avg_temp)
		elif biome_type == Biome.DESERT:
			free_land.sort_custom(func(a, b): return a.avg_moist < b.avg_moist)
		else:
			free_land.sort_custom(func(a, b): return a.avg_moist > b.avg_moist)
			
		var center_seed = free_land[0]
		
		var queue: Array[Region] = [center_seed]
		var infected_count = 0
		
		while queue.size() > 0 and infected_count < targets_needed:
			var current = queue.pop_front()
			
			if not claimed_regions.has(current):
				current.dominant_biome = biome_type
				claimed_regions.append(current)
				infected_count += 1
				
				for neighbor in current.neighbors:
					if neighbor.dominant_biome == Biome.PLAINS and not claimed_regions.has(neighbor) and not queue.has(neighbor):
						queue.append(neighbor)

	for x in range(MAP_WIDTH):
		for y in range(MAP_HEIGHT):
			var region_idx = tile_region_map[x][y]
			var biome_type = regions[region_idx].dominant_biome
			
			if river_mask[x][y] and biome_type != Biome.WATER:
				biome_type = Biome.WATER 
				
			set_cell(Vector2i(x, y), 0, BIOME_TILES[biome_type])


func _on_button_pressed() -> void:
	main_seed = randi()
	_initialize_noise()
	if debug_mode:
		_render_noise_debug()
		return
		
	_generate_voronoi_world()
