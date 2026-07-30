extends Resource
class_name RegionDefinition

@export var region_name: String = ""

@export_group("Size")
@export var min_area := 0

@export_group("Placement")
@export var requires_coast := false

@export_group("Gameplay")
@export var sectors : Array[SectorDefinition]
