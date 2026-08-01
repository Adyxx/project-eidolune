using System;
using System.Buffers;
using System.Collections.Generic;
using Godot;

public partial class landmark_generator : RefCounted
{
	private int _width;
	private int _height;
	private int _totalCells;
	private int _worldSeed;

	private int[] _sectorMap;
	private int[] _riverMap;
	private float[] _heightMap;

	private int[] _sectorCentersX;
	private int[] _sectorCentersY;

	private byte[] _occupiedLandmarkMap;
	private int[] _pathMap;

	private Dictionary<int, List<int>> _sectorCellsLookup = new();


	public void RunMajorLandmarkGeneration(
		int width,
		int height,
		GodotObject world,
		GodotObject context
	)
	{
		InitializeGenerator(world, context, width, height);

		Array.Clear(_occupiedLandmarkMap, 0, _totalCells);
		ProcessLandmarksByImportance(world, targetImportance: 0);
	}


	public void RunMinorLandmarkGeneration(
		int width,
		int height,
		GodotObject world,
		GodotObject context
	)
	{
		InitializeGenerator(world, context, width, height);
		ProcessLandmarksByImportance(world, targetImportance: 1);
	}

	private void ProcessLandmarksByImportance(GodotObject world, int targetImportance)
	{
		Godot.Collections.Array regionsArray = (Godot.Collections.Array)world.Get("regions");
		int count = 0;

		for (int r = 0; r < regionsArray.Count; r++)
		{
			GodotObject regionInstance = (GodotObject)regionsArray[r];
			Godot.Collections.Array sectorsArray = (Godot.Collections.Array)
				regionInstance.Get("sectors");

			for (int s = 0; s < sectorsArray.Count; s++)
			{
				GodotObject sectorInstance = (GodotObject)sectorsArray[s];
				int sGlobalId = (int)sectorInstance.Get("id");

				Godot.Collections.Array landmarksArray = (Godot.Collections.Array)
					sectorInstance.Get("landmarks");

				if (landmarksArray.Count == 0)
					continue;

				foreach (Variant landmarkVariant in landmarksArray)
				{
					GodotObject landmarkInstance = (GodotObject)landmarkVariant;
					if (landmarkInstance == null)
						continue;

					GodotObject definition = (GodotObject)landmarkInstance.Get("definition");
					int importance = (int)definition.Get("importance");

					if (importance != targetImportance)
						continue;

					string landmarkName = (string)definition.Get("landmark_name");

					int targetTileIdx = FindBestTileForLandmark(sGlobalId, definition);

					if (targetTileIdx != -1)
					{
						Vector2 finalPos = new Vector2(
							targetTileIdx % _width,
							targetTileIdx / _width
						);
						landmarkInstance.Set("position", finalPos);
						count++;
					}
					else
					{
						GD.PrintErr(
							$"    WARNING: Could not find suitable place for '{landmarkName}' in sector {sGlobalId}."
						);
					}
				}
			}
		}
	}

	private void InitializeGenerator(GodotObject world, GodotObject context, int width, int height)
	{

		_width = width;
		_height = height;
		_totalCells = _width * _height;
		_worldSeed = ((int)world.Get("main_seed"));

		if (_occupiedLandmarkMap == null || _occupiedLandmarkMap.Length != _totalCells)
		{
			_occupiedLandmarkMap = new byte[_totalCells];
		}

		Variant pathVariant = context.Get("path_id_map");
		if (pathVariant.VariantType != Variant.Type.Nil)
		{
			_pathMap = pathVariant.AsInt32Array();
		}

		_sectorMap = ((Variant)context.Get("sector_id_map")).AsInt32Array();
		_riverMap = ((Variant)context.Get("river_id_map")).AsInt32Array();
		_heightMap = ((Variant)context.Get("height_map")).AsFloat32Array();

		_sectorCellsLookup.Clear();

		Godot.Collections.Array regionsArray = (Godot.Collections.Array)world.Get("regions");
		int totalSectorsCount = 0;
		for (int r = 0; r < regionsArray.Count; r++)
		{
			GodotObject reg = (GodotObject)regionsArray[r];
			Godot.Collections.Array secArray = (Godot.Collections.Array)reg.Get("sectors");
			totalSectorsCount += secArray.Count;
		}

		_sectorCentersX = new int[totalSectorsCount];
		_sectorCentersY = new int[totalSectorsCount];

		HashSet<int> uniqueIdsInMap = new HashSet<int>();

		for (int i = 0; i < _totalCells; i++)
		{
			int sId = _sectorMap[i];
			if (sId == -1)
				continue;

			uniqueIdsInMap.Add(sId);

			if (!_sectorCellsLookup.ContainsKey(sId))
				_sectorCellsLookup[sId] = new List<int>();
			_sectorCellsLookup[sId].Add(i);
		}

		for (int r = 0; r < regionsArray.Count; r++)
		{
			GodotObject regionInstance = (GodotObject)regionsArray[r];
			Godot.Collections.Array sectorsArray = (Godot.Collections.Array)
				regionInstance.Get("sectors");

			for (int s = 0; s < sectorsArray.Count; s++)
			{
				GodotObject sectorInstance = (GodotObject)sectorsArray[s];
				int sGlobalId = (int)sectorInstance.Get("id");

				Variant centerVariant = sectorInstance.Get("center");
				Vector2 centerPos = centerVariant.AsVector2();

				_sectorCentersX[sGlobalId] = (int)centerPos.X;
				_sectorCentersY[sGlobalId] = (int)centerPos.Y;
			}
		}
	}

	private int FindBestTileForLandmark(int sectorGlobalId, GodotObject definition)
	{
		float minDistanceFromEdge = (float)definition.Get("min_distance_from_edge");
		bool nearRiver = (bool)definition.Get("near_river");
		int importance = (int)definition.Get("importance");

		if (
			!_sectorCellsLookup.TryGetValue(sectorGlobalId, out List<int> allowedCells)
			|| allowedCells.Count == 0
		)
			return -1;

		Random rand = new Random(_worldSeed + sectorGlobalId * 444);
		int startTile = _sectorCentersX[sectorGlobalId] + _sectorCentersY[sectorGlobalId] * _width;

		if (startTile < 0 || startTile >= _totalCells || _sectorMap[startTile] != sectorGlobalId)
		{
			startTile = allowedCells[rand.Next(allowedCells.Count)];
		}

		if (importance == 1)
		{
			startTile = allowedCells[rand.Next(allowedCells.Count)];
		}

		PriorityQueue<int, float> openSet = new PriorityQueue<int, float>();
		HashSet<int> visited = new HashSet<int>();

		openSet.Enqueue(startTile, 0f);
		visited.Add(startTile);

		int[] dx = { 1, -1, 0, 0 };
		int[] dy = { 0, 0, 1, -1 };

		int checkRange = (int)MathF.Ceiling(minDistanceFromEdge);
		float minDistanceSq = minDistanceFromEdge * minDistanceFromEdge;

		while (openSet.Count > 0)
		{
			int curr = openSet.Dequeue();

			if (curr < 0 || curr >= _totalCells)
				continue;

			int cx = curr % _width;
			int cy = curr / _width;

			if (_occupiedLandmarkMap[curr] == 1)
				continue;
			if (_pathMap != null && curr < _pathMap.Length && _pathMap[curr] != -1)
				continue;

			bool isValid = true;

			if (minDistanceFromEdge > 0)
			{
				for (int ty = -checkRange; ty <= checkRange; ty++)
				{
					for (int tx = -checkRange; tx <= checkRange; tx++)
					{
						if (tx * tx + ty * ty > minDistanceSq)
							continue;

						int nx = cx + tx;
						int ny = cy + ty;

						if (nx >= 0 && nx < _width && ny >= 0 && ny < _height)
						{
							int nIdx = nx + ny * _width;

							if (nIdx >= 0 && nIdx < _totalCells)
							{
								if (_sectorMap[nIdx] != sectorGlobalId)
								{
									isValid = false;
									break;
								}
							}
							else
							{
								isValid = false;
								break;
							}
						}
						else
						{
							isValid = false;
							break;
						}
					}
					if (!isValid)
						break;
				}
			}

			if (isValid && nearRiver)
			{
				bool hasRiver = false;
				for (int i = 0; i < 4; i++)
				{
					int nx = cx + dx[i];
					int ny = cy + dy[i];
					if (nx >= 0 && nx < _width && ny >= 0 && ny < _height)
					{
						int nIdx = nx + ny * _width;

						if (nIdx >= 0 && nIdx < _totalCells)
						{
							if (_riverMap[nIdx] != -1)
							{
								hasRiver = true;
								break;
							}
						}
					}
				}
				if (!hasRiver)
					isValid = false;
			}

			bool isNextToRoad = false;
			if (isValid && importance == 1 && _pathMap != null)
			{
				for (int i = 0; i < 4; i++)
				{
					int nx = cx + dx[i];
					int ny = cy + dy[i];
					if (nx >= 0 && nx < _width && ny >= 0 && ny < _height)
					{
						int nIdx = nx + ny * _width;
						if (nIdx >= 0 && nIdx < _pathMap.Length && _pathMap[nIdx] != -1)
						{
							isNextToRoad = true;
							break;
						}
					}
				}
			}

			if (isValid)
			{
				_occupiedLandmarkMap[curr] = 1;
				return curr;
			}

			for (int i = 0; i < 4; i++)
			{
				int nx = cx + dx[i];
				int ny = cy + dy[i];

				if (nx >= 0 && nx < _width && ny >= 0 && ny < _height)
				{
					int nIdx = nx + ny * _width;

					if (
						nIdx >= 0
						&& nIdx < _totalCells
						&& _sectorMap[nIdx] == sectorGlobalId
						&& !visited.Contains(nIdx)
					)
					{
						visited.Add(nIdx);

						float priority =
							MathF.Abs(nx - (startTile % _width))
							+ MathF.Abs(ny - (startTile / _width));
						if (importance == 1 && isNextToRoad)
						{
							priority -= 50f;
						}

						openSet.Enqueue(nIdx, priority);
					}
				}
			}
		}

		foreach (int idx in allowedCells)
		{
			if (
				idx >= 0
				&& idx < _totalCells
				&& _occupiedLandmarkMap[idx] == 0
				&& (_pathMap == null || idx >= _pathMap.Length || _pathMap[idx] == -1)
			)
			{
				_occupiedLandmarkMap[idx] = 1;
				return idx;
			}
		}

		return -1;
	}
}
