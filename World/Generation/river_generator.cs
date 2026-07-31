using System;
using System.Buffers;
using System.Collections.Generic;
using Godot;

public partial class river_generator : RefCounted
{
	private int _width;
	private int _height;
	private int _totalCells;
	private int _worldSeed;

	private byte[] _playableMap;
	private float[] _heightMap;
	private byte[] _landMaskMap;
	private int[] _riverMap;

	private FastNoiseLite _riverNoise = new FastNoiseLite();

	private readonly struct Direction
	{
		public readonly int Offset;
		public readonly bool CheckBounds;
		public readonly int XLimit;

		public Direction(int offset, bool checkBounds, int xLimit)
		{
			Offset = offset;
			CheckBounds = checkBounds;
			XLimit = xLimit;
		}
	}

	public int[] GenerateRivers(int width, int height, GodotObject world, GodotObject context)
	{
		InitializeGenerator(width, height, world, context);

		Godot.Collections.Array riversArray = (Godot.Collections.Array)world.Get("rivers");

		GD.Print($"\n==== GENERATING RIVERS STARTED ====");
		GD.Print($" * Numbers of rivers requested for generation: {riversArray?.Count ?? 0}");

		if (riversArray == null || riversArray.Count == 0)
		{
			context.Set("river_id_map", _riverMap);
			return _riverMap;
		}

		for (int i = 0; i < riversArray.Count; i++)
		{
			GodotObject riverInstance = (GodotObject)riversArray[i];
			GodotObject definition = (GodotObject)riverInstance.Get("definition");

			int riverId = i;
			riverInstance.Set("id", riverId);

			ExecuteAutonomousRiver(riverId, definition, riverInstance);
		}

		context.Set("river_id_map", _riverMap);
		GD.Print($"==== GENERATING RIVERS FINISHED ====\n");

		return _riverMap;
	}

	private void InitializeGenerator(int width, int height, GodotObject world, GodotObject context)
	{
		_width = width;
		_height = height;
		_totalCells = width * height;
		_worldSeed = (int)world.Get("main_seed");

		_playableMap = ((Variant)context.Get("playable_map")).AsByteArray();
		_heightMap = ((Variant)context.Get("height_map")).AsFloat32Array();
		_landMaskMap = ((Variant)context.Get("land_mask_map")).AsByteArray();

		_riverMap = new int[_totalCells];
		Array.Fill(_riverMap, -1);
	}

	private void ExecuteAutonomousRiver(
		int riverId,
		GodotObject definition,
		GodotObject riverInstance
	)
	{
		string riverName = (string)definition.Get("river_name");

		int startTile = FindRandomCoastalTile();
		if (startTile == -1)
			return;

		int sx = startTile % _width;
		int sy = startTile / _width;
		Vector2 startPos = new Vector2(sx, sy);

		Vector2 mapCenter = new Vector2(_width / 2f, _height / 2f);
		Vector2 initialDirection = (mapCenter - startPos).Normalized();

		GD.Print(
			$"   -> River on coast: [{sx}, {sy}], going towards: [{initialDirection.X:F2}, {initialDirection.Y:F2}]"
		);

		List<Vector2> allNetworkSplinePoints = new List<Vector2>();

		SimulateRiverBranchGrowth(
			definition,
			startPos,
			initialDirection,
			allNetworkSplinePoints,
			0
		);

		ApplyNoiseCorridor(riverId, allNetworkSplinePoints, definition);

		List<int> riverTiles = new List<int>();
		for (int i = 0; i < _totalCells; i++)
		{
			if (_riverMap[i] == riverId)
			{
				riverTiles.Add(i);
			}
		}

		int[] pathArray = new int[riverTiles.Count];
		for (int t = 0; t < riverTiles.Count; t++)
			pathArray[t] = riverTiles[t];

		riverInstance.Set("path_tile_indices", pathArray);
		GD.Print(
			$" * River'{riverName}' (ID {riverId}) saved. Contains {riverTiles.Count} water tiles."
		);
	}

	/*
	Recursive function for river growing.
	*/
	private void SimulateRiverBranchGrowth(
		GodotObject branchDef,
		Vector2 startPos,
		Vector2 direction,
		List<Vector2> outGlobalPoints,
		int depth
	)
	{
		string name = (string)branchDef.Get("river_name");

		int lengthTiles = (int)branchDef.Get("length_tiles");
		float branchStartPct = (float)branchDef.Get("branch_start_percentage");

		string indent = new string(' ', depth * 4);
		GD.Print(
			$"{indent}[BRANCH LEVEL {depth}]: Simulate '{name}', length: {lengthTiles} tiles..."
		);

		Vector2 currentPos = startPos;
		Vector2 currentDir = direction.Normalized();

		int branchTriggerStep = (int)(lengthTiles * branchStartPct);

		List<Vector2> localBranchPoints = new List<Vector2>();

		Random rand = new Random(_worldSeed + depth * 555);

		for (int step = 0; step < lengthTiles; step++)
		{
			int tx = (int)MathF.Round(currentPos.X);
			int ty = (int)MathF.Round(currentPos.Y);

			if (tx < 0 || tx >= _width || ty < 0 || ty >= _height)
				break;
			int idx = tx + ty * _width;
			if (_landMaskMap[idx] == 0 && step > 5)
				break;

			localBranchPoints.Add(currentPos);

			if (step == branchTriggerStep)
			{
				Godot.Collections.Array subBranchesArray = (Godot.Collections.Array)
					branchDef.Get("sub_branches");

				for (int b = 0; b < subBranchesArray.Count; b++)
				{
					GodotObject subDef = (GodotObject)subBranchesArray[b];
					if (subDef == null)
						continue;

					float angleDegrees = (float)subDef.Get("branch_angle_degrees");
					float radians = angleDegrees * (MathF.PI / 180f);

					float rotX =
						currentDir.X * MathF.Cos(radians) - currentDir.Y * MathF.Sin(radians);
					float rotY =
						currentDir.X * MathF.Sin(radians) + currentDir.Y * MathF.Cos(radians);
					Vector2 subDirection = new Vector2(rotX, rotY).Normalized();

					SimulateRiverBranchGrowth(
						subDef,
						currentPos,
						subDirection,
						outGlobalPoints,
						depth + 1
					);
				}
			}

			float wiggle = ((float)rand.NextDouble() - 0.5f) * 0.2f;
			currentDir = (
				currentDir + new Vector2(-currentDir.Y, currentDir.X) * wiggle
			).Normalized();

			currentPos += currentDir;
		}

		outGlobalPoints.AddRange(localBranchPoints);
		GD.Print(
			$"{indent}[BRANCH LEVEL {depth}]: '{name}' finished, simulated {localBranchPoints.Count} points."
		);
	}

	/*
	Function locates a tile near water to start a river from.
	*/
	private int FindRandomCoastalTile()
	{
		List<int> validCoastTiles = new List<int>();
		Vector2 mapCenter = new Vector2(_width / 2f, _height / 2f);

		for (int i = 0; i < _totalCells; i++)
		{
			if (_playableMap[i] == 0)
				continue;

			int x = i % _width;
			int y = i / _width;

			if (x == 0 || x == _width - 1 || y == 0 || y == _height - 1)
				continue;

			if (
				_landMaskMap[i + 1] == 0
				|| _landMaskMap[i - 1] == 0
				|| _landMaskMap[i + _width] == 0
				|| _landMaskMap[i - _width] == 0
			)
			{
				Vector2 tilePos = new Vector2(x, y);
				Vector2 toCenterDir = (mapCenter - tilePos).Normalized();

				bool isTooNarrow = false;

				for (int test = 1; test <= 25; test++)
				{
					int checkX = (int)MathF.Round(x + toCenterDir.X * test);
					int checkY = (int)MathF.Round(y + toCenterDir.Y * test);

					if (checkX < 0 || checkX >= _width || checkY < 0 || checkY >= _height)
					{
						isTooNarrow = true;
						break;
					}

					int checkIdx = checkX + checkY * _width;

					if (_landMaskMap[checkIdx] == 0)
					{
						isTooNarrow = true;
						break;
					}
				}

				if (!isTooNarrow)
				{
					validCoastTiles.Add(i);
				}
			}
		}

		GD.Print($"   [SMART COAST]: Found {validCoastTiles.Count} safe coasts for river start.");

		if (validCoastTiles.Count == 0)
		{
			GD.PrintErr(" WARNING: No coast went through the filter. Fallback.");
			for (int i = 0; i < _totalCells; i++)
			{
				if (
					_playableMap[i] > 0
					&& (
						_landMaskMap[i + 1] == 0
						|| _landMaskMap[i - 1] == 0
						|| _landMaskMap[i + _width] == 0
						|| _landMaskMap[i - _width] == 0
					)
				)
					return i;
			}
			return -1;
		}

		Random rand = new Random(_worldSeed + 123);
		return validCoastTiles[rand.Next(validCoastTiles.Count)];
	}

	private void ApplyNoiseCorridor(int riverId, List<Vector2> splinePoints, GodotObject definition)
	{
		float thickness = (float)definition.Get("river_thickness");
		float frequency = (float)definition.Get("noise_frequency");
		float heightBias = (float)definition.Get("heightmap_bias");

		_riverNoise.Seed = _worldSeed + riverId * 1337;
		_riverNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
		_riverNoise.Frequency = frequency;

		float baseRadius = thickness * 3.0f;
		if (baseRadius < 1.0f)
			baseRadius = 1.0f;

		for (int i = 0; i < splinePoints.Count; i++)
		{
			Vector2 p = splinePoints[i];
			int cx = (int)MathF.Round(p.X);
			int cy = (int)MathF.Round(p.Y);

			float noiseMod = _riverNoise.GetNoise2D(cx, cy) * 1.0f;

			int radius = (int)MathF.Max(1, MathF.Round(baseRadius + noiseMod));

			for (int dy = -radius; dy <= radius; dy++)
			{
				for (int dx = -radius; dx <= radius; dx++)
				{
					if (dx * dx + dy * dy > radius * radius)
						continue;

					int nx = cx + dx;
					int ny = cy + dy;

					if (nx >= 0 && nx < _width && ny >= 0 && ny < _height)
					{
						int nIdx = nx + ny * _width;

						if (_landMaskMap[nIdx] > 0 && _riverMap[nIdx] == -1)
						{
							if (_heightMap[nIdx] * heightBias < 0.7f)
							{
								_riverMap[nIdx] = riverId;
							}
						}
					}
				}
			}
		}
	}
}
