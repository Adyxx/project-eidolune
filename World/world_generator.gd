extends TileMapLayer
class_name WorldGenerator

var context: GenerationContext
var world: World

func _ready() -> void:
	_new_world()

func _on_button_pressed() -> void:
	_new_world()
	
func _new_world() -> void:
	context = GenerationContext.new()

	world = World.new()
	world.main_seed =  randi() # 885375362  # -80497186 # -412195953  #  -1166632466  # 1154105539 
	
	NoiseInitializer.initialize(context, world.main_seed)
	WorldLoader.new().load_definitions(world)
	
	
	_generate_world()
	

enum RenderMode {
	LAND_MASS,
	REGIONS,
	SECTORS
}

@export var current_render_mode: RenderMode = RenderMode.SECTORS


func _render_world() -> void:
	clear() 
	
	var width: int = WorldSettings.MAP_WIDTH
	var height: int = WorldSettings.MAP_HEIGHT
	var land_mask: PackedByteArray = context.land_mask_map
	
	var active_map: PackedInt32Array
	if current_render_mode == RenderMode.REGIONS:
		active_map = context.region_id_map
	elif current_render_mode == RenderMode.SECTORS:
		active_map = context.sector_id_map

	var cell_pos := Vector2i.ZERO
	var source_id: int = 0
	
	for y in range(height):
		cell_pos.y = y
		var row_offset: int = y * width
		
		for x in range(width):
			cell_pos.x = x
			var idx: int = x + row_offset
			
			if land_mask[idx] == 0:
				set_cell(cell_pos, source_id, Vector2i(0, 0))
				continue
				
			if current_render_mode == RenderMode.LAND_MASS:
				set_cell(cell_pos, source_id, Vector2i(1, 0))
			else:
				var target_id: int = active_map[idx]
				if target_id != -1:
					if current_render_mode == RenderMode.REGIONS:
						var tile_x: int = 1 + (target_id % 3)
						set_cell(cell_pos, source_id, Vector2i(tile_x, 0))
					else:
						var tile_x: int = 1 + (target_id % 3)
						var tile_y: int = (target_id / 3) % 3
						set_cell(cell_pos, source_id, Vector2i(tile_x, tile_y))
				else:
					set_cell(cell_pos, source_id, Vector2i(1, 3))


func _generate_world() -> void:
	
	var mask_start = Time.get_ticks_msec()
	
	var width = WorldSettings.MAP_WIDTH
	var height = WorldSettings.MAP_HEIGHT

	var climate_gen_cs = load("res://World/Generation/climate_generator.cs").new()
	var sector_gen_cs = load("res://World/Generation/sector_generator.cs").new()
	var region_gen_cs = load("res://World/Generation/region_generator.cs").new()

	var results: Dictionary = climate_gen_cs.RunGeneration(
		WorldSettings.MAP_WIDTH,
		WorldSettings.MAP_HEIGHT,
		WorldSettings.SEA_LEVEL,
		WorldSettings.CONTINENT_FALLOFF,
		WorldSettings.DOMAIN_WARP_STRENGTH,
		context.height,
		context.temperature,
		context.moisture,
		context.warp_x,
		context.warp_y
	)
	
	context.height_map = results["height_map"]
	context.temperature_map = results["temperature_map"]
	context.moisture_map = results["moisture_map"]
	context.land_mask_map = results["land_mask_map"]
	
	var region_start = Time.get_ticks_msec()
	
	
	context.playable_map = PackedByteArray()
	context.playable_map.resize(width * height)
	
	var num_regions = world.regions.size()

	var region_results = region_gen_cs.GenerateRegions(
		width,
		height,
		world,
		context
	)

	context.region_id_map = region_results["region_map"]

	var sector_start = Time.get_ticks_msec()

	var flat_sectors = []
	var sector_to_region_id: Array[int] = []
	var sector_weights: Array[float] = []
	
	for r_id in range(num_regions):
		for sector in world.regions[r_id].sectors:
			flat_sectors.append(sector)
			sector_to_region_id.append(r_id)
			sector_weights.append(sector.definition.size_weight)
			
			
	var total_sectors = flat_sectors.size()
	
	if total_sectors > 0:
		var sector_map_result = sector_gen_cs.RunSectorGeneration(
			width, height, total_sectors,
			context.region_id_map, sector_to_region_id, 
			context.height_map, sector_weights
		)
		
		context.sector_id_map = sector_map_result

	var render_start = Time.get_ticks_msec()
	
	_render_world()
	
	var end = Time.get_ticks_msec()

	print("Generating land:", (region_start-mask_start) / 1000.0, " seconds")
	print("Generating regions:", (sector_start-region_start) / 1000.0, " seconds")
	print("Generation sectors:", (render_start-sector_start) / 1000.0, " seconds")
	print("Rendering :", (end-render_start) / 1000.0, " seconds")
	
	
	# FIRST: CREATE WORLD SHAPE. WE GET LAND MASS.
	# TODO: Use masks, maybe warps and maybe other parameters to create interesting land shape.
	# For now, let's work with assumption that this is one large continent surrounded by water and not multiple islands.
	
	# SECOND: SPLIT THE LAND MASS INTO DIFFERENT SIZE REGIONS 
	# TODO: randomly place Region's var center : Vector2 somewhere on the land mass.
	
	# 1. Additional logic - like points cannot be too close to each other could be added to _generate_world function
	# 2. Additional logic - like "favors north" or "aras with high wetness" could be added to RegionDefinition
	
	# Use voronoi - Grow the region from center BASED ON RegionDefinition.size_weight.
	# Regions with higher size_weight should have more land than regions with lower size_weight
	

	# THIRD: THIS IS REALISTICALLY WHERE RIVER PLACEMENT WILL BE.
	# TODO: Sectors exist, rivers can be placed.
	# Either rivers follow the edge between sectors (could look nice?)
	# But they cannot just do that because later I though I might do things like...
	# "Village lies on top of this river" - which I cannot do if river is only on the sector edge - that could look weird.
	
	# CRITICAL: Since river can "rewrite" land mass of some sector, it might be important to then run some balancing check again?
	# But it should probably not be passing river threshold. If A & B are regions and ~ is water...
	# And AAAAAAAAABBBB got generated previously, then river would cut it to AAAAA~~~~BBBB
	# Then the algorith should probably not grow the region back to something like AAAAA~~~~AABB
	# that would look kind of weird.
	

	# FOURTH: SPLIT THE REGIONS INTO DIFFERENT SIZE SECTORS
	# TODO: randomly place Sector's var center : Vector2 somewhere inside its specific Region.
	
	# Similar logic described in points 1. 2. in SECOND (region segment) could be added here too.
	
	# Use voronoi - Grow the segment from center BASED ON SectorDefinition.size_weight
	# Sectors with higher size_weight should have more land than sectors with lower size_weight
	
	# FIFTH: MAJOR LANDMARKS
	# TODO: place major Landmarks.
	# Major Landmarks include Villages, cities, possibly lore important objects - some named pond, etc.
	# Each major landmark has a specific sector in which it exists.
	
	# Landmarks can reserve other specific conditions, such as min_distance_from_edge,
	# max_distance_from_center, min_distance_from_river, near_road: bool, etc.
	
	# CRITICAL: In case that a major landmark could not be placed (suitable spot does not exist)
	# We cannot be "regenerating world" until everything passes, because that could cause infinite wait.
	# We need to fix the world - and create the spot for the said landmark. This needs to be fully deterministic,
	# because same main_seed of the world need to always guarantee same world shape.
	

	# SIXTH: PATHS
	# TODO: Place some paths between cities, villages, etc.
	
	# This is also DST check that all parts of the map are accessible!
	# If later we add plants to some region and then this region will be impossible to get to 
	# For instance it got cut by river, etc., then it is bad.
	# In this step we should guarantee walkability, including placing bridges over rives.
	# I might need some rivers to be guaranteed to get a bridge - maybe I will make it a parameter
	# Int as "bridge count" might be maybe better than bool "guarantee_bridge" ?? 
	

	# SEVENTH: MINOR LANDMARKS
	# TODO: place minor Landmarks.
	# Minor Landmarks include things that visually enchance the world but are not so gameplay important.
	
	# Landmarks can reserve other specific conditions, such as min_distance_from_edge,
	# max_distance_from_center, min_distance_from_river, near_road: bool, etc.
	
	# In case that a minor landmark could not be placed (suitable spot does not exist)
	# We can probably ignore its placement - these will be just decorations.
	

	# EIGHT: FLORA
	# TODO: place plants across the world.
	
	# Plants can require specific segments, moisture, temperature, etc.
	# Each plant will have "min_required_patches" variable - which notes how many tiles are spawn points for said plant.
	
	# CRITICAL: In case of the placed final patches are less than "min_required_patches", follow in order:
	# 1. increase spawn chance - Increase local density. (ex. spawnChance: 0.02 -> 0.04)
	# 2. Condition Relaxation. (ex. reduce requirment from moisture > 0.7 to moisture > 0.68)
	# repeat 1. > 2. until "min_required_patches" is fulfilled.
