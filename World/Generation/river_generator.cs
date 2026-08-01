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
			string riverName = (string)definition.Get("river_name");

			bool riverSuccessfullyPlaced = false;

			for (int attempt = 0; attempt < 5; attempt++)
			{
				GD.Print($" -> Generating river with ID {riverId}: '{riverName}' (Attempt {attempt + 1})");

				int tilesCarved = ExecuteAutonomousRiver(
					riverId,
					definition,
					riverInstance,
					attempt
				);

				if (tilesCarved >= 40)
				{
					riverSuccessfullyPlaced = true;
					break;
				}

				GD.Print(
					$"    [RETRY]: River '{riverName}' was too short ({tilesCarved} tiles). Finding different coast..."
				);
				for (int c = 0; i < _totalCells; i++)
				{
					if (_riverMap[c] == riverId)
						_riverMap[c] = -1;
				}
			}

			if (!riverSuccessfullyPlaced)
			{
				GD.PrintErr(
					$" ! WARNING: River '{riverName}' did not find suitable path (5 attempts)."
				);
			}
		}

		context.Set("river_id_map", _riverMap);
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

	private int ExecuteAutonomousRiver(
		int riverId,
		GodotObject definition,
		GodotObject riverInstance,
		int attempt
	)
	{
		int startTile = FindRandomCoastalTile(riverId, attempt);
		if (startTile == -1)
			return 0;

		int sx = startTile % _width;
		int sy = startTile / _width;
		Vector2 startPos = new Vector2(sx, sy);

		Vector2 seaVector = Vector2.Zero;
		for (int ty = -5; ty <= 5; ty++)
		{
			for (int tx = -5; tx <= 5; tx++)
			{
				int nx = sx + tx;
				int ny = sy + ty;
				if (nx >= 0 && nx < _width && ny >= 0 && ny < _height)
				{
					if (_landMaskMap[nx + ny * _width] == 0)
						seaVector += new Vector2(tx, ty);
				}
			}
		}

		Vector2 initialDirection =
			seaVector != Vector2.Zero
				? -seaVector.Normalized()
				: (new Vector2(_width / 2f, _width / 2f) - startPos).Normalized();

		List<Vector2> allNetworkSplinePoints = new List<Vector2>();

		SimulateRiverBranchGrowth(
			definition,
			startPos,
			initialDirection,
			allNetworkSplinePoints,
			0,
			riverId
		);

		ApplyNoiseCorridor(riverId, allNetworkSplinePoints, definition);

		List<int> riverTiles = new List<int>();
		for (int i = 0; i < _totalCells; i++)
		{
			if (_riverMap[i] == riverId)
				riverTiles.Add(i);
		}

		int[] pathArray = new int[riverTiles.Count];
		for (int t = 0; t < riverTiles.Count; t++)
			pathArray[t] = riverTiles[t];
		riverInstance.Set("path_tile_indices", pathArray);

		return riverTiles.Count;
	}

	/*
	Recursive function for river growing.
	*/
	private void SimulateRiverBranchGrowth(
		GodotObject branchDef,
		Vector2 startPos,
		Vector2 direction,
		List<Vector2> outGlobalPoints,
		int depth,
		int currentRiverId
	)
	{
		int lengthTiles = (int)branchDef.Get("length_tiles");
		float branchStartPct = (float)branchDef.Get("branch_start_percentage");

		Vector2 currentPos = startPos;
		Vector2 currentDir = direction.Normalized();
		int branchTriggerStep = (int)(lengthTiles * branchStartPct);

		Random rand = new Random(_worldSeed + depth * 555 + currentRiverId * 77);
		Vector2 lastStoredPos = startPos;

		for (int step = 0; step < lengthTiles; step++)
		{
			int tx = (int)MathF.Round(currentPos.X);
			int ty = (int)MathF.Round(currentPos.Y);

			if (tx < 0 || tx >= _width || ty < 0 || ty >= _height)
				break;
			int idx = tx + ty * _width;

			if (_riverMap[idx] != -1 && _riverMap[idx] != currentRiverId && step > 10)
			{
				GD.Print(
					$"    Branch merged with another river - ID {_riverMap[idx]} na kroku {step}."
				);
				break;
			}

			if (_landMaskMap[idx] == 0 && step > 40)
				break;

			int x0 = (int)MathF.Round(lastStoredPos.X);
			int y0 = (int)MathF.Round(lastStoredPos.Y);
			int x1 = tx;
			int y1 = ty;

			int dxAbs = Math.Abs(x1 - x0);
			int dyAbs = Math.Abs(y1 - y0);
			int sx = x0 < x1 ? 1 : -1;
			int sy = y0 < y1 ? 1 : -1;
			int err = dxAbs - dyAbs;

			while (true)
			{
				outGlobalPoints.Add(new Vector2(x0, y0));
				if (x0 == x1 && y0 == y1)
					break;
				int e2 = 2 * err;
				if (e2 > -dyAbs)
				{
					err -= dyAbs;
					x0 += sx;
				}
				if (e2 < dxAbs)
				{
					err += dxAbs;
					y0 += sy;
				}
			}

			lastStoredPos = currentPos;

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

					SimulateRiverBranchGrowth(
						subDef,
						currentPos,
						new Vector2(rotX, rotY),
						outGlobalPoints,
						depth + 1,
						currentRiverId
					);
				}
			}

			float wiggle = ((float)rand.NextDouble() - 0.5f) * 0.25f;
			currentDir = (
				currentDir + new Vector2(-currentDir.Y, currentDir.X) * wiggle
			).Normalized();
			currentPos += currentDir;
		}
	}

	/*
	Function locates a tile near water to start a river from.
	*/
	private int FindRandomCoastalTile(int riverId, int attempt)
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
					if (_landMaskMap[checkX + checkY * _width] == 0)
					{
						isTooNarrow = true;
						break;
					}
				}

				if (!isTooNarrow)
					validCoastTiles.Add(i);
			}
		}

		if (validCoastTiles.Count == 0)
		{
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

		Random rand = new Random(_worldSeed + riverId * 88 + attempt * 17);
		return validCoastTiles[rand.Next(validCoastTiles.Count)];
	}

	private void ApplyNoiseCorridor(int riverId, List<Vector2> splinePoints, GodotObject definition)
	{
		float corridorWidth = (float)definition.Get("corridor_width");
		float frequency = (float)definition.Get("noise_frequency");
		float thickness = (float)definition.Get("river_thickness");
		float heightBias = 1;

		_riverNoise.Seed = _worldSeed + riverId * 1337;
		_riverNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
		_riverNoise.Frequency = frequency;
		_riverNoise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
		_riverNoise.FractalOctaves = 3;

		int riverTilesCarved = 0;

		for (int idx = 0; idx < _totalCells; idx++)
		{
			if (_landMaskMap[idx] == 0)
				continue;

			int x = idx % _width;
			int y = idx / _width;
			Vector2 tilePos = new Vector2(x, y);

			float minDistanceSq = float.MaxValue;
			int closestNodeIdx = 0;

			for (int i = 0; i < splinePoints.Count; i++)
			{
				float distSq = tilePos.DistanceSquaredTo(splinePoints[i]);
				if (distSq < minDistanceSq)
				{
					minDistanceSq = distSq;
					closestNodeIdx = i;
				}
			}

			float distance = MathF.Sqrt(minDistanceSq);
			float progressAlongRiver = (float)closestNodeIdx / splinePoints.Count;

			float startFade = 1.0f;
			if (closestNodeIdx < 15)
			{
				startFade = closestNodeIdx / 15f;
			}

			float currentWarpStrength = corridorWidth * startFade;

			float warpX = _riverNoise.GetNoise2D(x, y) * currentWarpStrength * 1.5f;
			float warpY = _riverNoise.GetNoise2D(x + 1000f, y + 1000f) * currentWarpStrength * 1.5f;

			Vector2 warpedTilePos = new Vector2(x + warpX, y + warpY);

			float warpedMinDistanceSq = float.MaxValue;
			for (int i = 0; i < splinePoints.Count; i++)
			{
				float distSq = warpedTilePos.DistanceSquaredTo(splinePoints[i]);
				if (distSq < warpedMinDistanceSq)
					warpedMinDistanceSq = distSq;
			}
			float warpedDistance = MathF.Sqrt(warpedMinDistanceSq);

			float growthFactor = 1.0f + progressAlongRiver * 1.5f;
			float currentBaseRadius = thickness * 8.0f * growthFactor;

			float islandNoise = _riverNoise.GetNoise2D(x * 0.4f, y * 0.4f);
			float thicknessModulation = 1.0f + islandNoise * 0.45f;

			float terrainNarrowing = _heightMap[idx] * heightBias * currentBaseRadius;
			float finalRiverRadius = (currentBaseRadius * thicknessModulation) - terrainNarrowing;

			if (finalRiverRadius < 1.2f)
				finalRiverRadius = 1.2f;

			if (warpedDistance < finalRiverRadius)
			{
				if (_riverMap[idx] == -1)
				{
					_riverMap[idx] = riverId;
					riverTilesCarved++;
				}
			}
		}
	}
}
