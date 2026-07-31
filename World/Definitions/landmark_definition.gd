extends Resource
class_name LandmarkDefinition

@export var landmark_name: String

enum categorization {
	MAJOR,
	MINOR
}

@export var importance: categorization

@export var scene: PackedScene

@export var min_distance_from_edge: float # ie. sector-wise... for instance "city cannot be too close to a sector border"
@export var near_river: bool
