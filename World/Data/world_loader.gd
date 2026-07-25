
class_name WorldLoader

func load_definitions(world: World) -> void:
	var definitions: Array[RegionDefinition] = [
		load("res://World/Data/Verden.tres"),
		load("res://World/Data/Gromwelt.tres"),
		load("res://World/Data/Altre.tres")
	]

	for definition in definitions:
		world.regions.append(create_region(definition))


func create_region(definition: RegionDefinition) -> Region:

	var region := Region.new()
	region.definition = definition

	for sector_definition in definition.sectors:
		region.sectors.append(create_sector(sector_definition))

	return region
	
	
func create_sector(definition: SectorDefinition) -> Sector:

	var sector := Sector.new()
	sector.definition = definition

	for landmark_definition in definition.landmarks:
		sector.landmarks.append(create_landmark(landmark_definition))

	return sector
	
	
func create_landmark(definition: LandmarkDefinition) -> Landmark:

	var landmark := Landmark.new()
	landmark.definition = definition

	return landmark
