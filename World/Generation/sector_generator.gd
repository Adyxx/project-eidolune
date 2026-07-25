
class_name SectorGenerator

const NEIGHBOR_OFFSETS = [
	Vector2i(1, 0),
	Vector2i(-1, 0),
	Vector2i(0, 1),
	Vector2i(0, -1)
]

var sample: Sample
var world: World
var context: GenerationContext

var map_width: int = WorldSettings.MAP_WIDTH
var map_height: int = WorldSettings.MAP_HEIGHT

func _init(c: GenerationContext, s: Sample, w: World):
	context = c
	sample = s
	world = w

func generate() -> void:
	if world.regions.is_empty():
		return
		
	var region_map = context.region_id_map
	var sector_map = context.sector_id_map
	
	sector_map.resize(map_width * map_height)
	sector_map.fill(-1) 


	var flat_sectors: Array[Sector] = []
	var sector_to_region_id: Array[int] = []
	
	for r_id in range(world.regions.size()):
		var region = world.regions[r_id]
		for sector in region.sectors:
			flat_sectors.append(sector)
			sector_to_region_id.append(r_id)
			
	var num_total_sectors = flat_sectors.size()
	if num_total_sectors == 0:
		return

	for r_id in range(world.regions.size()):
		var region = world.regions[r_id]
		if region.sectors.is_empty():
			continue
			
		var r_sector_centers = _find_balanced_sector_centers(r_id, region.sectors.size())
		
		for local_s_id in range(region.sectors.size()):
			var sector = region.sectors[local_s_id]
			var center_pos = r_sector_centers[local_s_id]
			sector.center = Vector2(center_pos.x, center_pos.y)

	for global_s_id in range(num_total_sectors):
		var sector = flat_sectors[global_s_id]
		var c_idx = sample.index(int(sector.center.x), int(sector.center.y))
		sector_map[c_idx] = global_s_id

	var sector_frontiers_current: Array[Array] = []
	var sector_frontiers_next: Array[Array] = []
	sector_frontiers_current.resize(num_total_sectors)
	sector_frontiers_next.resize(num_total_sectors)
	
	var sector_accumulators := PackedFloat32Array()
	sector_accumulators.resize(num_total_sectors)
	sector_accumulators.fill(0.0)

	var queued_map := PackedByteArray()
	queued_map.resize(map_width * map_height)
	queued_map.fill(0)

	for global_s_id in range(num_total_sectors):
		sector_frontiers_current[global_s_id] = []
		sector_frontiers_next[global_s_id] = []
		
		var sector = flat_sectors[global_s_id]
		var my_region_id = sector_to_region_id[global_s_id]
		var center_v2i = Vector2i(int(sector.center.x), int(sector.center.y))
		
		for offset in NEIGHBOR_OFFSETS:
			var neighbor = center_v2i + offset
			if sample.is_valid(neighbor.x, neighbor.y):
				var n_idx = sample.index(neighbor.x, neighbor.y)

				if region_map[n_idx] == my_region_id and sector_map[n_idx] == -1:
					sector_frontiers_current[global_s_id].append(neighbor)
					queued_map[n_idx] = 1

	var active_growth = true
	
	while active_growth:
		active_growth = false
		
		for global_s_id in range(num_total_sectors):
			var current_frontier: Array = sector_frontiers_current[global_s_id]
			var next_frontier: Array = sector_frontiers_next[global_s_id]
			
			if current_frontier.is_empty():
				continue
				
			active_growth = true
			var sector = flat_sectors[global_s_id]
			var my_region_id = sector_to_region_id[global_s_id]
			var weight = sector.definition.size_weight
			
			sector_accumulators[global_s_id] += weight
			if sector_accumulators[global_s_id] < 1.0:
				continue
				
			var steps_to_execute = floor(sector_accumulators[global_s_id])
			sector_accumulators[global_s_id] -= steps_to_execute
			
			for step in range(int(steps_to_execute)):
				if current_frontier.is_empty():
					break
					
				for i in range(current_frontier.size()):
					var cell: Vector2i = current_frontier[i]
					var idx = sample.index(cell.x, cell.y)
					
					if sector_map[idx] != -1:
						continue
						
					var noise_val = sample.height(cell.x, cell.y)
					if noise_val > 0.45 and randf() > 0.3:
						next_frontier.append(cell)
						continue
						
					sector_map[idx] = global_s_id
					
					for offset in NEIGHBOR_OFFSETS:
						var neighbor = cell + offset
						
						if sample.is_valid(neighbor.x, neighbor.y):
							var n_idx = sample.index(neighbor.x, neighbor.y)
							
							if region_map[n_idx] == my_region_id and sector_map[n_idx] == -1 and queued_map[n_idx] == 0:
								queued_map[n_idx] = 1
								next_frontier.append(neighbor)
								
				current_frontier.clear()
				var temp = current_frontier
				current_frontier = next_frontier
				next_frontier = temp
				
				if current_frontier.is_empty():
					break
			
			sector_frontiers_current[global_s_id] = current_frontier
			sector_frontiers_next[global_s_id] = next_frontier


func _find_balanced_sector_centers(target_region_id: int, sector_count: int) -> Array[Vector2i]:
	var centers: Array[Vector2i] = []
	var region_map = context.region_id_map
	
	var valid_region_cells: Array[Vector2i] = []
	for x in range(map_width):
		for y in range(map_height):
			var idx = sample.index(x, y)
			if region_map[idx] == target_region_id:
				valid_region_cells.append(Vector2i(x, y))
				
	if valid_region_cells.is_empty():
		centers.resize(sector_count)
		centers.fill(Vector2i(map_width / 2, map_height / 2))
		return centers

	valid_region_cells.shuffle()
	for i in range(min(sector_count, valid_region_cells.size())):
		centers.append(valid_region_cells[i])
		
	while centers.size() < sector_count:
		centers.append(valid_region_cells[0])

	for relaxation in range(2):
		var sum_x: Array[float] = []
		var sum_y: Array[float] = []
		var cell_count: Array[float] = []
		sum_x.resize(sector_count)
		sum_y.resize(sector_count)
		cell_count.resize(sector_count)
		sum_x.fill(0.0)
		sum_y.fill(0.0)
		cell_count.fill(0.0)
		
		for i in range(0, valid_region_cells.size(), 2):
			var cell = valid_region_cells[i]
			var best_dist = INF
			var best_id = 0
			for s in range(sector_count):
				var d = Vector2(cell.x, cell.y).distance_squared_to(Vector2(centers[s].x, centers[s].y))
				if d < best_dist:
					best_dist = d
					best_id = s
			sum_x[best_id] += cell.x
			sum_y[best_id] += cell.y
			cell_count[best_id] += 1.0
			
		for s in range(sector_count):
			if cell_count[s] > 0:
				var target_x = int(sum_x[s] / cell_count[s])
				var target_y = int(sum_y[s] / cell_count[s])
				var t_idx = sample.index(target_x, target_y)
				
				if region_map[t_idx] == target_region_id:
					centers[s] = Vector2i(target_x, target_y)
				else:
					var closest_cell = centers[s]
					var min_d = 999999.0
					for c in valid_region_cells:
						var d = Vector2(target_x, target_y).distance_squared_to(Vector2(c.x, c.y))
						if d < min_d:
							min_d = d
							closest_cell = c
					centers[s] = closest_cell
					
	return centers
