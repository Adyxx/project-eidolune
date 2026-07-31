extends Resource
class_name RiverDefinition

@export var river_name: String = ""

@export_group("Structure")
@export var length_tiles: int = 150
@export var thickness: float = 0.5

@export_group("Noise")
@export var corridor_width: float = 20.0
@export var noise_frequency: float = 0.03

@export_group("Branching")
# Position on this river (as a percentage from 0.0 to 1.0) where the branch is to diverge.
# E.g., 0.5 means the new branch diverges exactly halfway along the length of this river.
@export_range(0.1, 0.9) var branch_start_percentage: float = 0.5

# Turn angle (in mirror mode, e.g., -45 degrees left, +45 right)
@export_range(-90, 90) var branch_angle_degrees: float = 45.0

@export var sub_branches: Array[RiverDefinition] = []
