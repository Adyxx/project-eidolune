extends Resource
class_name SectorDefinition


@export var sector_name: String = ""

@export var size_weight: float = 1.0

@export_group("Size")
@export var min_area: int = 0

@export_group("Terrain & Visuals")
@export var terrain_rules: Array[TerrainRuleDefinition] = []
@export var terrain_visuals:Array[TerrainVisualDefinition]
@export var tile_set: TileSet

@export_group("Gameplay")
@export var landmarks: Array[LandmarkDefinition] = []



"""

later might add mask modifiers... for instance

secotr says... temperature -0.2 (if cold) or moisture+0.3 (if swamp), etc.

"""
