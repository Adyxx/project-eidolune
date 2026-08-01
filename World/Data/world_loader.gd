
class_name WorldLoader

func load_definitions(world: World) -> void:
	var global_sector_id := 0
	var global_landmark_id := 0
	
	var region_definitions: Array[RegionDefinition] = [
		load("res://World/Data/Regions/Verden.tres"),
		load("res://World/Data/Regions/Gromwelt.tres"),
		load("res://World/Data/Regions/Altre.tres")
	]

	for i in range(region_definitions.size()):
		var region := create_region(region_definitions[i], i, global_sector_id, global_landmark_id)
		world.regions.append(region)
		global_sector_id += region_definitions[i].sectors.size()
		for sector_def in region_definitions[i].sectors:
			global_landmark_id += sector_def.landmarks.size()

	var river_definitions: Array[RiverDefinition] = [
		load("res://World/Data/Rivers/River.tres"),
		load("res://World/Data/Rivers/Rivertwo.tres"),
	]
	
	for i in range(river_definitions.size()):
		var river := create_river(river_definitions[i])
		river.id = i
		world.rivers.append(river)
		
	
func create_region(definition: RegionDefinition, region_id: int, start_sector_id: int, start_landmark_id: int) -> Region:
	var region := Region.new()
	region.id = region_id
	region.definition = definition

	var current_sector_id = start_sector_id
	var current_landmark_id = start_landmark_id

	for i in range(definition.sectors.size()):
		var sector := create_sector(definition.sectors[i], current_sector_id, current_landmark_id)
		region.sectors.append(sector)
		print("Sector:", sector.definition.sector_name)
		current_sector_id += 1
		current_landmark_id += definition.sectors[i].landmarks.size()
		
	return region
	
func create_sector(definition: SectorDefinition, global_sector_id: int, start_landmark_id: int) -> Sector:
	var sector := Sector.new()
	# sector.id = global_sector_id
	sector.definition = definition

	var current_landmark_id = start_landmark_id
	for i in range(definition.landmarks.size()):
		var landmark := create_landmark(definition.landmarks[i], current_landmark_id)
		sector.landmarks.append(landmark)
		current_landmark_id += 1

	return sector

func create_landmark(definition: LandmarkDefinition, global_landmark_id: int) -> Landmark:
	var landmark := Landmark.new()
	landmark.id = global_landmark_id
	landmark.definition = definition
	return landmark

	
func create_river(definition: RiverDefinition) -> River:

	var river := River.new()
	river.definition = definition

	return river
	
