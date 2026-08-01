extends TileMapLayer
class_name WorldGenerator

var context: GenerationContext
var world: World
var settings: WorldSettings

func _ready() -> void:
	_new_world()

func _on_button_pressed() -> void:
	_new_world()
	
func _new_world() -> void:
	context = GenerationContext.new()

	world = World.new()
	settings = WorldSettings.new()
	
	world.main_seed =  randi() # 885375362  # -80497186 # -412195953  #  -1166632466  # 1154105539 
	
	NoiseInitializer.initialize(context, world.main_seed)
	WorldLoader.new().load_definitions(world)
	
	
	_generate_world()
	

enum RenderMode {
	LAND_MASS,
	REGIONS,
	SECTORS,
	RIVERS,
	ROADS
}

@export var current_render_mode: RenderMode = RenderMode.SECTORS

func _render_world(container: Node2D) -> void:
	clear() 
	
	var width: int = WorldSettings.MAP_WIDTH
	var height: int = WorldSettings.MAP_HEIGHT
	var land_mask: PackedByteArray = context.land_mask_map

	if land_mask.is_empty():
		return

	var active_map: PackedInt32Array
	if current_render_mode == RenderMode.REGIONS and not context.region_id_map.is_empty():
		active_map = context.region_id_map
	elif current_render_mode == RenderMode.SECTORS and not context.sector_id_map.is_empty():
		active_map = context.sector_id_map
	elif current_render_mode == RenderMode.RIVERS and not context.river_id_map.is_empty():
		active_map = context.river_id_map
	elif current_render_mode == RenderMode.ROADS and not context.path_id_map.is_empty():
		active_map = context.path_id_map

	var river_map: PackedInt32Array = context.river_id_map
	var has_rivers: bool = not river_map.is_empty()

	var path_map: PackedInt32Array = context.path_id_map
	var bridge_map: PackedByteArray = context.bridge_id_map
	var has_paths: bool = not path_map.is_empty()
	var has_bridges: bool = not bridge_map.is_empty()

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
				
			if current_render_mode != RenderMode.LAND_MASS and has_bridges and bridge_map[idx] == 1:
				set_cell(cell_pos, source_id, Vector2i(1, 3)) 
				continue

			if current_render_mode != RenderMode.LAND_MASS and has_rivers and river_map[idx] != -1:
				set_cell(cell_pos, source_id, Vector2i(0, 0))
				continue

			if current_render_mode != RenderMode.LAND_MASS and current_render_mode != RenderMode.RIVERS and has_paths and path_map[idx] != -1:
				set_cell(cell_pos, source_id, Vector2i(2,3))
				continue

			if current_render_mode == RenderMode.LAND_MASS:
				set_cell(cell_pos, source_id, Vector2i(1, 0))
				continue
			
			if current_render_mode == RenderMode.RIVERS or current_render_mode == RenderMode.ROADS:
				set_cell(cell_pos, source_id, Vector2i(1, 0))
				continue

			if active_map.is_empty():
				set_cell(cell_pos, source_id, Vector2i(1, 3))
				continue

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

	for region in world.regions:
		for sector in region.sectors:
			for landmark in sector.landmarks:
				if landmark.position == Vector2.ZERO: continue
				
				var landmark_scene: PackedScene = landmark.definition.scene
				if landmark_scene == null: continue
				
				var landmark_instance = landmark_scene.instantiate()
				var tile_size := 16
				landmark_instance.global_position = landmark.position * tile_size
				
				container.add_child(landmark_instance)


func _generate_world() -> void:
	var old_container = get_node_or_null("LandmarksContainer")
	if old_container:
		remove_child(old_container)
		old_container.free()
		
	var landmarks_container = Node2D.new()
	landmarks_container.name = "LandmarksContainer"
	add_child(landmarks_container)
	
	var mask_start = Time.get_ticks_msec()
	
	var width = WorldSettings.MAP_WIDTH
	var height = WorldSettings.MAP_HEIGHT

	var climate_gen_cs = load("res://World/Generation/climate_generator.cs").new()
	var sector_gen_cs = load("res://World/Generation/sector_generator.cs").new()
	var region_gen_cs = load("res://World/Generation/region_generator.cs").new()
	var river_gen_cs = load("res://World/Generation/river_generator.cs").new()
	var landmark_gen_cs = load("res://World/Generation/landmark_generator.cs").new()
	var path_gen_cs = load("res://World/Generation/path_generator.cs").new()
	
	
	context.playable_map = PackedByteArray()
	context.playable_map.resize(width * height)
	
	context.river_id_map = PackedInt32Array()
	context.river_id_map.resize(width * height)
	context.river_id_map.fill(-1)
	
	var results: Dictionary = climate_gen_cs.RunGeneration(
		settings, context
	)
	
	context.height_map = results["height_map"]
	context.temperature_map = results["temperature_map"]
	context.moisture_map = results["moisture_map"]
	context.land_mask_map = results["land_mask_map"]
	context.playable_map = results["playable_map"]
	world.mainContinentSize = results["mainContinentSize"]
	world.startIdx = results["startIdx"]
	
	# TODO: Tweak river logic.
	# 1. Currently it can hit sea too early and end prematurely.
	# 2. Sometimes river does not start exactly at coast tile, just near it.
	# 3. Sometimes the river becomes unconnected (gaps) as it is growing.
	# 4. Maybe add new parameters regarding general source from where the river flows - so two rivers do not start too close to each other.
	
	var river_start = Time.get_ticks_msec()
	var river_results = river_gen_cs.GenerateRivers(width, height, world, context)

	# TODO: Maybe at some point, tweak region relocation logic.
	# Implementation of rivers decreased chance to correctly generate regions.
	
	var region_start = Time.get_ticks_msec()
	var region_results = region_gen_cs.GenerateRegions(width, height, world, context)
	context.region_id_map = region_results
	
	# TODO: Implement validation and retry for meeting the min size requirment.
	# Currently the min sizes of sectors are not guaranteed.
	# TODO: Make sectors respect the rivers and not spread through them.
	
	var sector_start = Time.get_ticks_msec()
	var sector_map_result = sector_gen_cs.GenerateSectors(width, height, world, context)
	#context.sector_id_map = sector_map_result
	
	var major_landmark_start = Time.get_ticks_msec()
	landmark_gen_cs.RunMajorLandmarkGeneration(width, height, world, context)
	
	# TODO: Implement additional function for bridge placing.
	# For islands that belong to playablemapmask, that got cut off by river and became inaccessible.
	# Probably allow bridges to be placed on the sea tiles (but suppose bridge max length=5, or something like that.
	
	var path_start = Time.get_ticks_msec()
	path_gen_cs.RunPathGeneration(width, height, world, context)
	
	var minor_landmark_start = Time.get_ticks_msec()
	landmark_gen_cs.RunMinorLandmarkGeneration(width, height, world, context)
	
	var render_start = Time.get_ticks_msec()
	_render_world(landmarks_container)
	
	var end = Time.get_ticks_msec()

	print("Generating land: ", (river_start-mask_start) / 1000.0, " seconds")
	print("Generating rivers: ", (region_start-river_start) / 1000.0, " seconds")
	print("Generating regions: ", (sector_start-region_start) / 1000.0, " seconds")
	print("Generating sectors: ", (major_landmark_start-sector_start) / 1000.0, " seconds")
	print("Placing major landmarks: ", (path_start-major_landmark_start) / 1000.0, " seconds")
	print("Building roads: ", (minor_landmark_start-path_start) / 1000.0, " seconds")
	print("Placing minor landmarks: ", (render_start-minor_landmark_start) / 1000.0, " seconds")
	print("Rendering: ", (end-render_start) / 1000.0, " seconds")
