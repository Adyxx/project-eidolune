
class_name WorldLoader

func load_definitions(world: World) -> void:
	var region_definitions: Array[RegionDefinition] = [
		load("res://World/Data/Verden.tres"),
		load("res://World/Data/Gromwelt.tres"),
		load("res://World/Data/Altre.tres")
	]

	for i in range(region_definitions.size()):
		var region := create_region(region_definitions[i])
		region.id = i
		world.regions.append(region)
		
	var river_definitions: Array[RiverDefinition] = [
		load("res://World/Data/Rivers/River.tres")
	]
	
	for i in range(river_definitions.size()):
		var river := create_river(river_definitions[i])
		river.id = i
		world.rivers.append(river)
		
	
func create_region(definition: RegionDefinition) -> Region:
	var region := Region.new()
	region.definition = definition

	for i in range(definition.sectors.size()):
		var sector := create_sector(definition.sectors[i])
		sector.id = i
		region.sectors.append(sector)
		
	return region
	
func create_sector(definition: SectorDefinition) -> Sector:

	var sector := Sector.new()
	sector.definition = definition

	for i in range(definition.landmarks.size()):
		var landmark := create_landmark(definition.landmarks[i])
		landmark.id = i
		sector.landmarks.append(landmark)

	return sector
	
func create_landmark(definition: LandmarkDefinition) -> Landmark:

	var landmark := Landmark.new()
	landmark.definition = definition

	return landmark
	
	
func create_river(definition: RiverDefinition) -> River:

	var river := River.new()
	river.definition = definition

	return river
	
