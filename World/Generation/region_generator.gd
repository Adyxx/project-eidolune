

class_name RegionGenerator

const NEIGHBOR_OFFSETS = [
	Vector2i(1, 0),
	Vector2i(-1, 0),
	Vector2i(0, 1),
	Vector2i(0, -1)
]

var sample: Sample
var world: World
var context: GenerationContext

func _init(c: GenerationContext, s: Sample, w: World):
	context = c
	sample = s
	world = w

func generate() -> void:
	if world.regions.is_empty():
		return
		
	var region_map = context.region_id_map 
	region_map.fill(-1)

	var num_regions = world.regions.size()

	var balanced_centers = _find_balanced_land_centers(num_regions)
	
	for r_id in range(num_regions):
		var region = world.regions[r_id]
		var center_pos = balanced_centers[r_id]
		region.center = Vector2(center_pos.x, center_pos.y)
		
		var c_idx = sample.index(center_pos.x, center_pos.y)
		region_map[c_idx] = r_id

	var region_frontiers_current: Array[Array] = []
	var region_frontiers_next: Array[Array] = []
	region_frontiers_current.resize(num_regions)
	region_frontiers_next.resize(num_regions)
	
	var region_accumulators := PackedFloat32Array()
	region_accumulators.resize(num_regions)
	region_accumulators.fill(0.0)

	var queued_map := PackedByteArray()
	queued_map.resize(WorldSettings.MAP_WIDTH * WorldSettings.MAP_HEIGHT)
	queued_map.fill(0)

	for r_id in range(num_regions):
		region_frontiers_current[r_id] = []
		region_frontiers_next[r_id] = []
		
		var center_v2i = Vector2i(int(world.regions[r_id].center.x), int(world.regions[r_id].center.y))
		
		for offset in NEIGHBOR_OFFSETS:
			var neighbor = center_v2i + offset
			if sample.is_valid(neighbor.x, neighbor.y) and sample.is_land(neighbor.x, neighbor.y):
				var n_idx = sample.index(neighbor.x, neighbor.y)
				if region_map[n_idx] == -1:
					region_frontiers_current[r_id].append(neighbor)
					queued_map[n_idx] = 1

	var active_growth = true
	
	while active_growth:
		active_growth = false
		
		for r_id in range(num_regions):
			var current_frontier: Array = region_frontiers_current[r_id]
			var next_frontier: Array = region_frontiers_next[r_id]
			
			if current_frontier.is_empty():
				continue
				
			active_growth = true
			var weight = world.regions[r_id].definition.size_weight
			
			region_accumulators[r_id] += weight
			if region_accumulators[r_id] < 1.0:
				continue
				
			var steps_to_execute = floor(region_accumulators[r_id])
			region_accumulators[r_id] -= steps_to_execute
			
			for step in range(int(steps_to_execute)):
				if current_frontier.is_empty():
					break
					
				for i in range(current_frontier.size()):
					var cell: Vector2i = current_frontier[i]

					var idx = sample.index(cell.x, cell.y)
					queued_map[idx] = 0
					
					if region_map[idx] != -1:
						continue
						
					var noise_val = sample.height(cell.x, cell.y)
					if noise_val > 0.45 and randf() > 0.3:
						next_frontier.append(cell)
						continue
						
					region_map[idx] = r_id
					
					for offset in NEIGHBOR_OFFSETS:
						var neighbor = cell + offset
						
						if sample.is_valid(neighbor.x, neighbor.y) and sample.is_land(neighbor.x, neighbor.y):
							var n_idx = sample.index(neighbor.x, neighbor.y)
							
							if region_map[n_idx] == -1 and queued_map[n_idx] == 0:
								queued_map[n_idx] = 1
								next_frontier.append(neighbor)

				current_frontier.clear()
				
				var temp = current_frontier
				current_frontier = next_frontier
				next_frontier = temp

				if current_frontier.is_empty():
					break
			
			region_frontiers_current[r_id] = current_frontier
			region_frontiers_next[r_id] = next_frontier

func _find_balanced_land_centers(count: int) -> Array[Vector2i]:
	var centers: Array[Vector2i] = []
	
	while centers.size() < count:
		var rx = randi_range(0, WorldSettings.MAP_WIDTH - 1)
		var ry = randi_range(0, WorldSettings.MAP_HEIGHT - 1)
		if sample.is_land(rx, ry) and not centers.has(Vector2i(rx, ry)):
			centers.append(Vector2i(rx, ry))
			
	for relaxation in range(2):
		var sum_x: Array[float] = []
		var sum_y: Array[float] = []
		var cell_count: Array[float] = []
		sum_x.resize(count)
		sum_y.resize(count)
		cell_count.resize(count)
		sum_x.fill(0.0)
		sum_y.fill(0.0)
		cell_count.fill(0.0)
		
		for x in range(0, WorldSettings.MAP_WIDTH, 4): 
			for y in range(0, WorldSettings.MAP_HEIGHT, 4):
				if sample.is_land(x, y):
					var best_dist = INF
					var best_id = 0
					for i in range(count):
						var d = Vector2(x, y).distance_squared_to(Vector2(centers[i].x, centers[i].y))
						if d < best_dist:
							best_dist = d
							best_id = i
					sum_x[best_id] += x
					sum_y[best_id] += y
					cell_count[best_id] += 1.0
					
		for i in range(count):
			if cell_count[i] > 0:
				var target_x = int(sum_x[i] / cell_count[i])
				var target_y = int(sum_y[i] / cell_count[i])
				if sample.is_land(target_x, target_y):
					centers[i] = Vector2i(target_x, target_y)
					
	return centers
