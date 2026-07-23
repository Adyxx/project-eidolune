class_name Terrain

enum Type {
	PLAINS, # Fertile, green, open land. Baseline movement.
	FOREST, # High density of trees.
	STEPPE, # Vast, dry, golden-brown grassland. Open, but scarce water/resources.
	WETLAND, # Muddy, flooded terrain (Swamp/Marsh). Drastically reduces movement speed.
	DESERT, # Scorched, sandy, or cracked earth. Causes heat exhaustion or attrition.
	ROCKY, # Maze-like fields of boulders and exposed bedrock. Hard wall boundaries.
	OCEAN # Deep water. Impassable without bridges.
}
