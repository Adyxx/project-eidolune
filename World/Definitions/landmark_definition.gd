extends Resource
class_name LandmarkDefinition

@export var landmark_name: String
@export var importance: float

@export var scene: PackedScene

# later maybe min_distance_from_edge / max_distance_from_center, 
# min_distance_from_river, near_road: bool, etc.
