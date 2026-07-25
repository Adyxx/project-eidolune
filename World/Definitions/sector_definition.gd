extends Resource
class_name SectorDefinition


@export var sector_name: String = ""

@export var size_weight: float = 1.0

@export var terrain_rules: Array[TerrainRuleDefinition] = []

@export var landmarks: Array[LandmarkDefinition] = []

@export var tile_set: TileSet

@export var terrain_visuals:Array[TerrainVisualDefinition]


"""

later might add mask modifiers... for instance

secotr says... temperature -0.2 (if cold) or moisture+0.3 (if swamp), etc.

"""
