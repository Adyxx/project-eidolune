extends TileMapLayer
class_name WorldGenerator

var context: GenerationContext
var sample : Sample
var world: World

func _ready() -> void:
	_new_world()

func _on_button_pressed() -> void:
	_new_world()
	
func _new_world() -> void:
	context = GenerationContext.new()
	sample = Sample.new(context)
	
	world = World.new()
	world.main_seed = randi()
	
	NoiseInitializer.initialize(context, world.main_seed)
	WorldLoader.new().load_definitions(world)
	_generate_world()
	
"""
func _render_world() -> void:
	clear() 
	for x in range(WorldSettings.MAP_WIDTH):
		for y in range(WorldSettings.MAP_HEIGHT):
			var idx = sample.index(x, y)
			var is_land = context.land_mask_map[idx] == 1
			
			if is_land:
				set_cell(Vector2i(x, y), 0, Vector2i(1, 0)) 
			else:
				set_cell(Vector2i(x, y), 0, Vector2i(0, 0)) 
"""

enum RenderMode {
	LAND_MASS,
	REGIONS,
	SECTORS
}

@export var current_render_mode: RenderMode = RenderMode.SECTORS


func _render_world() -> void:
	clear() 
	
	for x in range(WorldSettings.MAP_WIDTH):
		for y in range(WorldSettings.MAP_HEIGHT):
			var idx = sample.index(x, y)
			
			if not sample.is_land(x, y):
				set_cell(Vector2i(x, y), 0, Vector2i(0, 0))
				continue
				
			match current_render_mode:
				
				RenderMode.LAND_MASS:
					set_cell(Vector2i(x, y), 0, Vector2i(1, 0))
					
				RenderMode.REGIONS:
					var region_id = context.region_id_map[idx]
					
					if region_id != -1:
						var tile_x = 1 + (region_id % 3)
						set_cell(Vector2i(x, y), 0, Vector2i(tile_x, 0)) 
					else:
						set_cell(Vector2i(x, y), 0, Vector2i(1, 3))
						
				RenderMode.SECTORS:
					var sector_id = context.sector_id_map[idx]
					
					if sector_id != -1:
						var tile_x = 1 + (sector_id % 3)
						var tile_y = (sector_id / 3) % 3
						set_cell(Vector2i(x, y), 0, Vector2i(tile_x, tile_y))
					else:
						set_cell(Vector2i(x, y), 0, Vector2i(1, 3))



func _generate_world() -> void:
	var total_cells = WorldSettings.MAP_WIDTH * WorldSettings.MAP_HEIGHT
	context.height_map.resize(total_cells)
	context.temperature_map.resize(total_cells)
	context.moisture_map.resize(total_cells)
	context.land_mask_map.resize(total_cells)
	
	context.region_id_map.resize(total_cells)


	
	HeightClimateGenerator.new(context).generate()
	ContinentGenerator.new(context, sample).generate()
	RegionGenerator.new(context, sample, world).generate()
	
	SectorGenerator.new(context, sample, world).generate()
	
	_render_world()
	
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
	
