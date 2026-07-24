class_name NoiseInitializer

static func initialize(context: GenerationContext, s: int) -> void:
	context.height.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	context.temperature.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	context.moisture.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	
	context.height.seed = s
	context.temperature.seed = s + 1234
	context.moisture.seed = s + 5678

	context.height.frequency = 0.006 
	context.height.fractal_type = FastNoiseLite.FRACTAL_FBM
	context.height.fractal_octaves = 5
	context.height.fractal_lacunarity = 2.0
	context.height.fractal_gain = 0.5

	context.temperature.frequency = 0.003
	context.moisture.frequency = 0.004

	context.warp_x.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	context.warp_y.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	context.warp_x.seed = s + 999
	context.warp_y.seed = s + 888
	context.warp_x.frequency = 0.015
	context.warp_y.frequency = 0.015
