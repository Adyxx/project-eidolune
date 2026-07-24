class_name HeightClimateGenerator

var context: GenerationContext


func _init(c: GenerationContext):
	context = c


func generate() -> void:

	for x in range(WorldSettings.MAP_WIDTH):
		for y in range(WorldSettings.MAP_HEIGHT):

			var idx = x + y * WorldSettings.MAP_WIDTH
			var height = generate_height(x,y)

			context.height_map[idx] = height
			context.temperature_map[idx] = generate_temperature(x, y, height)

			context.moisture_map[idx] = generate_moisture(x,y)



func generate_height(x,y):

	var offset_x = context.warp_x.get_noise_2d(x,y) * WorldSettings.DOMAIN_WARP_STRENGTH
	var offset_y = context.warp_y.get_noise_2d(x,y) * WorldSettings.DOMAIN_WARP_STRENGTH

	var base_height = (context.height.get_noise_2d(x + offset_x, y + offset_y) + 1.0) * 0.5

	var dx = abs(x-context.world_center.x)/context.world_center.x
	var dy = abs(y-context.world_center.y)/context.world_center.y

	var dist_mask = 1.0 - (1.0 - dx*dx)*(1.0-dy*dy)

	return clamp(base_height - dist_mask * WorldSettings.CONTINENT_FALLOFF, 0.0, 1.0)


func generate_temperature(x,y,height):

	var latitude = y / WorldSettings.MAP_HEIGHT
	var cold_from_height = height * 0.4
	var noise = context.temperature.get_noise_2d(x,y)*0.2

	return clamp(1.0 - latitude - cold_from_height + noise, 0.0, 1.0)
	
func generate_moisture(x: float, y: float) -> float:
	var offset_x = context.warp_x.get_noise_2d(x, y) * WorldSettings.DOMAIN_WARP_STRENGTH
	var offset_y = context.warp_y.get_noise_2d(x, y) * WorldSettings.DOMAIN_WARP_STRENGTH
	return (context.moisture.get_noise_2d(x + offset_x, y + offset_y) + 1.0) * 0.5
