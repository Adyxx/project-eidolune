class_name Sample

var context: GenerationContext

func _init(c: GenerationContext):
	context = c

func index(x:int, y:int) -> int:
	return x + y * WorldSettings.MAP_WIDTH

func height(x:int, y:int) -> float:
	return context.height_map[index(x,y)]

func temperature(x:int, y:int) -> float:
	return context.temperature_map[index(x,y)]

func moisture(x:int, y:int) -> float:
	return context.moisture_map[index(x,y)]
	
func is_land(x:int, y:int) -> bool:
	return context.land_mask_map[index(x, y)] == 1
	
func is_valid(x:int, y:int) -> bool:
	return x >= 0 and y >= 0 and x < WorldSettings.MAP_WIDTH and y < WorldSettings.MAP_HEIGHT
