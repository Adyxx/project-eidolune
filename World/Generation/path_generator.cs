using System;
using System.Buffers;
using System.Collections.Generic;
using Godot;

public partial class path_generator : RefCounted
{
	private int _width;
	private int _height;
	private int _totalCells;
	private int _worldSeed;

	private int[] _sectorMap;
	private int[] _riverMap;
	private float[] _heightMap;

	private int[] _pathMap;
	private byte[] _bridgeMap;

	private List<int> _connectedLandmarkTiles = new List<int>();

	private float[] _gScoreBuffer;
	private int[] _cameFromBuffer;

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

	public void RunPathGeneration(int width, int height, GodotObject world, GodotObject context)
	{
		InitializeGenerator(width, height, world, context);

		GD.Print($"\n==== GENERÁTOR CEST: START ====");
		GD.Print(
			$" * Počet registrovaných center pro silniční síť: {_connectedLandmarkTiles.Count}"
		);

		if (_connectedLandmarkTiles.Count < 2)
		{
			GD.Print(" * Nedostatek propojených měst. Silnice netřeba generovat.");
			context.Set("path_id_map", _pathMap);
			context.Set("bridge_id_map", _bridgeMap);
			return;
		}

		ExecuteSmartRoadNetwork();

		context.Set("path_id_map", _pathMap);
		context.Set("bridge_id_map", _bridgeMap);
		GD.Print($"==== GENERÁTOR CEST: DOKONČENO ====\n");
	}

	private void InitializeGenerator(int width, int height, GodotObject world, GodotObject context)
	{
		_width = width;
		_height = height;
		_totalCells = _width * _height;
		_worldSeed = (int)world.Get("main_seed");

		_sectorMap = ((Variant)context.Get("sector_id_map")).AsInt32Array();
		_riverMap = ((Variant)context.Get("river_id_map")).AsInt32Array();
		_heightMap = ((Variant)context.Get("height_map")).AsFloat32Array();

		_pathMap = new int[_totalCells];
		Array.Fill(_pathMap, -1);

		_bridgeMap = new byte[_totalCells];

		if (_gScoreBuffer == null || _gScoreBuffer.Length != _totalCells)
		{
			_gScoreBuffer = new float[_totalCells];
			_cameFromBuffer = new int[_totalCells];
		}

		_connectedLandmarkTiles.Clear();

		Godot.Collections.Array regionsArray = (Godot.Collections.Array)world.Get("regions");

		for (int r = 0; r < regionsArray.Count; r++)
		{
			GodotObject regionInstance = (GodotObject)regionsArray[r];
			Godot.Collections.Array sectorsArray = (Godot.Collections.Array)
				regionInstance.Get("sectors");

			for (int s = 0; s < sectorsArray.Count; s++)
			{
				GodotObject sectorInstance = (GodotObject)sectorsArray[s];
				Godot.Collections.Array landmarksArray = (Godot.Collections.Array)
					sectorInstance.Get("landmarks");

				foreach (Variant lmVariant in landmarksArray)
				{
					GodotObject lmInstance = (GodotObject)lmVariant;
					if (lmInstance == null)
						continue;

					GodotObject definition = (GodotObject)lmInstance.Get("definition");

					int importance = (int)definition.Get("importance");
					bool isPathConnected = (bool)definition.Get("is_path_connected");

					if (importance == 0 && isPathConnected)
					{
						Vector2 pos = (Vector2)lmInstance.Get("position");
						if (pos != Vector2.Zero)
						{
							int idx = (int)pos.X + (int)pos.Y * _width;
							_connectedLandmarkTiles.Add(idx);
						}
					}
				}
			}
		}
	}


	private void ExecuteSmartRoadNetwork()
	{
		Vector2 mapCenter = new Vector2(_width / 2f, _height / 2f);
		_connectedLandmarkTiles.Sort(
			(a, b) =>
			{
				Vector2 posA = new Vector2(a % _width, a / _width);
				Vector2 posB = new Vector2(b % _width, b / _width);
				return posA.DistanceSquaredTo(mapCenter)
					.CompareTo(posB.DistanceSquaredTo(mapCenter));
			}
		);

		int roadIdCounter = 0;


		int cityA = _connectedLandmarkTiles[0];
		int cityB = _connectedLandmarkTiles[1];

		GD.Print(
			$"   -> Buduji páteřní silnici ID {roadIdCounter} mezi městy na indexech [{cityA}] a [{cityB}]"
		);
		List<int> initialPath = FindRoadPathAStar(
			cityA,
			cityB,
			isMultiTarget: false,
			roadIdCounter
		);

		if (initialPath != null)
			roadIdCounter++;

		for (int i = 2; i < _connectedLandmarkTiles.Count; i++)
		{
			int currentCityTile = _connectedLandmarkTiles[i];

			GD.Print(
				$"   -> Připojuji město na indexu [{currentCityTile}] do stávající silniční sítě... (Silnice ID {roadIdCounter})"
			);

			
			List<int> branchPath = FindRoadPathAStar(
				currentCityTile,
				-1,
				isMultiTarget: true,
				roadIdCounter
			);

			if (branchPath != null && branchPath.Count > 0)
			{
				roadIdCounter++;
			}
		}
	}


	private List<int> FindRoadPathAStar(
		int startIdx,
		int targetIdx,
		bool isMultiTarget,
		int currentRoadId
	)
	{
		Array.Fill(_gScoreBuffer, float.MaxValue);
		Array.Fill(_cameFromBuffer, -1);

		PriorityQueue<int, float> openSet = new PriorityQueue<int, float>();

		_gScoreBuffer[startIdx] = 0f;
		openSet.Enqueue(startIdx, 0f);

		Direction[] localDirs = new Direction[]
		{
			new Direction(1, true, _width - 1),
			new Direction(-1, true, 0),
			new Direction(_width, false, 0),
			new Direction(-_width, false, 0),
		};

		int finalConnectionTile = -1;

		int endX = !isMultiTarget ? targetIdx % _width : 0;
		int endY = !isMultiTarget ? targetIdx / _width : 0;

		while (openSet.Count > 0)
		{
			int curr = openSet.Dequeue();

			if (isMultiTarget && _pathMap[curr] != -1)
			{
				finalConnectionTile = curr;
				break;
			}

			if (!isMultiTarget && curr == targetIdx)
			{
				finalConnectionTile = curr;
				break;
			}

			int cx = curr % _width;

			foreach (var dir in localDirs)
			{
				if (dir.CheckBounds && cx == dir.XLimit)
					continue;

				int nIdx = curr + dir.Offset;
				if (nIdx < 0 || nIdx >= _totalCells)
					continue;

				if (_sectorMap[nIdx] == -1)
					continue;

				float stepCost = 1.0f;

				float heightDelta = MathF.Abs(_heightMap[nIdx] - _heightMap[curr]);
				stepCost += heightDelta * 350f;

				if (_sectorMap[nIdx] != _sectorMap[curr])
				{
					stepCost -= 0.4f; 
				}

				if (_riverMap[nIdx] != -1)
				{
					stepCost += 15.0f;
				}

				float tentativeGScore = _gScoreBuffer[curr] + stepCost;

				if (tentativeGScore < _gScoreBuffer[nIdx])
				{
					_cameFromBuffer[nIdx] = curr;
					_gScoreBuffer[nIdx] = tentativeGScore;

					float h = 0f;
					if (!isMultiTarget)
					{
						h = Math.Abs(nIdx % _width - endX) + Math.Abs(nIdx / _width - endY);
					}

					openSet.Enqueue(nIdx, tentativeGScore + h);
				}
			}
		}

		if (finalConnectionTile != -1)
		{
			List<int> path = new List<int>();
			int current = finalConnectionTile;
			while (current != startIdx && current != -1)
			{
				path.Add(current);
				current = _cameFromBuffer[current];
			}
			path.Add(startIdx);

			foreach (int tile in path)
			{
				if (_pathMap[tile] == -1)
				{
					_pathMap[tile] = currentRoadId;
				}

				if (_riverMap[tile] != -1)
				{
					_bridgeMap[tile] = 1;
				}
			}

			return path;
		}

		return null;
	}
}
