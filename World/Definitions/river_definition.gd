extends Resource
class_name RiverDefinition

@export var river_name: String = "Svatá řeka"

enum RiverType {
	BORDER,
	THROUGH_SECTORS
}
@export var type: RiverType = RiverType.THROUGH_SECTORS

@export_group("Trasa (Použije se pro THROUGH_SECTORS)")
@export var region_path: Array[RegionDefinition] = []
@export var sector_path: Array[SectorDefinition] = []

@export_group("Přírodní Hranice (Použije se pro BORDER)")
@export var target_border_region: RegionDefinition

enum RiverEndType {
	SEA,
	LAKE
}

@export_group("Zakončení toku (Použije se pro THROUGH_SECTORS)")
@export var end_type: RiverEndType = RiverEndType.SEA
