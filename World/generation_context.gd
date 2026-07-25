class_name GenerationContext

var height = FastNoiseLite.new()
var temperature = FastNoiseLite.new()
var moisture = FastNoiseLite.new()

var warp_x = FastNoiseLite.new()
var warp_y = FastNoiseLite.new()

var height_map := PackedFloat32Array() 
var temperature_map := PackedFloat32Array() 
var moisture_map := PackedFloat32Array() 

var land_mask_map := PackedByteArray()
var region_id_map := PackedInt32Array()
var sector_id_map := PackedInt32Array()

"""
maybe later also...

road_id_map
river_id_map

"""

var world_center := Vector2(
	WorldSettings.MAP_WIDTH / 2.0,
	WorldSettings.MAP_HEIGHT / 2.0
)
