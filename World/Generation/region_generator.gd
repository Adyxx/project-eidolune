class_name RegionGenerator


var sample = Sample

"""
sample CONTAINS access and helper functions:
	func index(x:int, y:int) -> int
	func height(x:int, y:int) -> float
	func temperature(x:int, y:int) -> float
	func moisture(x:int, y:int) -> float
	func is_land(x:int, y:int) -> bool
	func is_valid(x:int, y:int) -> bool
"""

var world: World

"""
world CONTAINS:
	var main_seed: int
	var regions: Array[Region] = []
	var rivers: Array[River] = []
	var roads: Array[Road] = []
"""

func _init(s: Sample, w: World):
	sample = s
	world = w


func generate() -> void:
	pass
	
	# world.regions should already be filled with data from world loader
	
	# here it should implement something like...
	# for region in world.regions:
	#	region.center = _find_region_center(region)
	
	# class_name RegionDefinition contains @export var size_weight: float = 1.0
	# runtime class_name Region contains var definition: RegionDefinition and var center : Vector2
	
	# size_weight should be used - larger regions should contains more land mass.

		
