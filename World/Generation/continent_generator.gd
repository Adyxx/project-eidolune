

class_name ContinentGenerator

var context = GenerationContext
var sample = Sample

func _init(c: GenerationContext, s: Sample):
	context = c
	sample = s
	

func generate() -> void:
	for x in range(WorldSettings.MAP_WIDTH):
		for y in range(WorldSettings.MAP_HEIGHT):
			var idx = sample.index(x, y)
			if context.height_map[idx] >= WorldSettings.SEA_LEVEL:
				context.land_mask_map[idx] = 1
			else:
				context.land_mask_map[idx] = 0
