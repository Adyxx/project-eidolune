using System;
using System.Buffers;
using System.Collections.Generic;
using Godot;

public partial class sector_generator : RefCounted
{
	private int _width;
	private int _height;
	private int _totalCells;
	private int _worldSeed;

	private int[] _regionMap;
	private float[] _heightMap;
	private int[] _sectorMap;

	private int _totalSectorsCount;
	private int[] _sectorRegionIds;
	private int[] _sectorMinAreas;
	private int[] _currentSectorAreas;
	private int[] _sectorCentersIdx;

	private int[][] _regionCells;
	private PriorityQueue<int, float> _openSet;

	private struct PlacementSector
	{
		public int GlobalId;
		public int RegionId;
		public int MinArea;
	}

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

	public int[] GenerateSectors(int width, int height, GodotObject world, GodotObject context)
	{
		InitializeGenerator(width, height, world, context);

		List<PlacementSector> sortedSectors = LoadAndSortSectors(world);
		if (_totalSectorsCount == 0)
			return _sectorMap;

		PlaceSectorSeeds(sortedSectors);

		GrowSectorsToMinimums();

		FinishClaimingRegions();
		
		SaveResultsToGodot(world, context);
		
		return _sectorMap;
	}

	private void InitializeGenerator(int width, int height, GodotObject world, GodotObject context)
	{
		_width = width;
		_height = height;
		_totalCells = width * height;
		_worldSeed = (int)world.Get("main_seed");

		_regionMap = ((Variant)context.Get("region_id_map")).AsInt32Array();
		_heightMap = ((Variant)context.Get("height_map")).AsFloat32Array();

		_sectorMap = new int[_totalCells];
		Array.Fill(_sectorMap, -1);

		if (_openSet == null)
			_openSet = new PriorityQueue<int, float>(_totalCells / 10);
		else
			_openSet.Clear();

		Godot.Collections.Array regionsArray = (Godot.Collections.Array)world.Get("regions");
		int numRegions = regionsArray.Count;

		int[] regionCellCounts = ArrayPool<int>.Shared.Rent(numRegions);
		Array.Clear(regionCellCounts, 0, numRegions);

		for (int i = 0; i < _totalCells; i++)
		{
			int rId = _regionMap[i];
			if (rId >= 0 && rId < numRegions)
				regionCellCounts[rId]++;
		}

		_regionCells = new int[numRegions][];
		int[] writeOffsets = ArrayPool<int>.Shared.Rent(numRegions);
		Array.Clear(writeOffsets, 0, numRegions);

		for (int r = 0; r < numRegions; r++)
			_regionCells[r] = new int[regionCellCounts[r]];

		for (int i = 0; i < _totalCells; i++)
		{
			int rId = _regionMap[i];
			if (rId >= 0 && rId < numRegions)
				_regionCells[rId][writeOffsets[rId]++] = i;
		}

		ArrayPool<int>.Shared.Return(regionCellCounts);
		ArrayPool<int>.Shared.Return(writeOffsets);
	}

	private List<PlacementSector> LoadAndSortSectors(GodotObject world)
	{
		Godot.Collections.Array regionsArray = (Godot.Collections.Array)world.Get("regions");
		int numRegions = regionsArray.Count;

		List<PlacementSector> sectorsList = new List<PlacementSector>();
		int globalIdCounter = 0;

		for (int rId = 0; rId < numRegions; rId++)
		{
			GodotObject regionInstance = (GodotObject)regionsArray[rId];
			Godot.Collections.Array sectorsArray = (Godot.Collections.Array)
				regionInstance.Get("sectors");

			for (int s = 0; s < sectorsArray.Count; s++)
			{
				GodotObject sectorInstance = (GodotObject)sectorsArray[s];
				GodotObject definition = (GodotObject)sectorInstance.Get("definition");

				sectorInstance.Set("id", globalIdCounter);

				sectorsList.Add(
					new PlacementSector
					{
						GlobalId = globalIdCounter,
						RegionId = rId,
						MinArea = (int)definition.Get("min_area"),
					}
				);

				globalIdCounter++;
			}
		}

		_totalSectorsCount = sectorsList.Count;

		sectorsList.Sort((a, b) => b.MinArea - a.MinArea);

		_sectorRegionIds = new int[_totalSectorsCount];
		_sectorMinAreas = new int[_totalSectorsCount];
		_currentSectorAreas = new int[_totalSectorsCount];
		_sectorCentersIdx = new int[_totalSectorsCount];

		for (int i = 0; i < sectorsList.Count; i++)
		{
			int gId = sectorsList[i].GlobalId;
			_sectorRegionIds[gId] = sectorsList[i].RegionId;
			_sectorMinAreas[gId] = sectorsList[i].MinArea;
		}

		return sectorsList;
	}

	private void PlaceSectorSeeds(List<PlacementSector> sortedSectors)
	{
		Random rand = new Random(_worldSeed + 777);

		for (int i = 0; i < sortedSectors.Count; i++)
		{
			int gId = sortedSectors[i].GlobalId;
			int rId = sortedSectors[i].RegionId;
			int[] availableCells = _regionCells[rId];

			if (availableCells.Length == 0)
				continue;

			int bestStartIdx = -1;

			for (int attempt = 0; attempt < 50; attempt++)
			{
				int potentialIdx = availableCells[rand.Next(availableCells.Length)];
				if (_sectorMap[potentialIdx] == -1)
				{
					bestStartIdx = potentialIdx;
					break;
				}
			}

			if (bestStartIdx == -1)
				bestStartIdx = availableCells[rand.Next(availableCells.Length)];

			_sectorMap[bestStartIdx] = gId;
			_sectorCentersIdx[gId] = bestStartIdx;
			_currentSectorAreas[gId] = 1;
		}
	}

	private void GrowSectorsToMinimums()
	{
		_openSet.Clear();

		int[] parentIndices = ArrayPool<int>.Shared.Rent(_totalCells);
		Array.Fill(parentIndices, -1, 0, _totalCells);

		Random rand = new Random(_worldSeed + 888);

		for (int s = 0; s < _totalSectorsCount; s++)
		{
			int startIdx = _sectorCentersIdx[s];
			if (startIdx != 0 || _sectorMap[startIdx] == s)
			{
				parentIndices[startIdx] = startIdx;
				_openSet.Enqueue(startIdx, 0f);
			}
		}

		Direction[] localDirs = new Direction[]
		{
			new Direction(1, true, _width - 1),
			new Direction(-1, true, 0),
			new Direction(_width, false, 0),
			new Direction(-_width, false, 0),
		};

		float terrainWeight = 200.0f;
		float distanceWeight = 1.0f;

		while (_openSet.Count > 0)
		{
			int idx = _openSet.Dequeue();
			int currentSectorId = _sectorMap[idx];

			if (_currentSectorAreas[currentSectorId] >= _sectorMinAreas[currentSectorId])
				continue;

			int cx = idx % _width;
			float currentHeight = _heightMap[idx];
			float parentHeight = _heightMap[parentIndices[idx]];
			float heightDelta = MathF.Abs(currentHeight - parentHeight);

			foreach (var dir in localDirs)
			{
				if (dir.CheckBounds && cx == dir.XLimit)
					continue;

				int nIdx = idx + dir.Offset;
				if (nIdx < 0 || nIdx >= _totalCells)
					continue;
				int targetRegionId = _regionMap[nIdx];
				if (targetRegionId == _sectorRegionIds[currentSectorId] && _sectorMap[nIdx] == -1)
				{
					if (_currentSectorAreas[currentSectorId] < _sectorMinAreas[currentSectorId])
					{
						_sectorMap[nIdx] = currentSectorId;
						parentIndices[nIdx] = idx;
						_currentSectorAreas[currentSectorId]++;
						float cost =
							distanceWeight
							+ (heightDelta * terrainWeight)
							+ ((float)rand.NextDouble() * 0.1f);
						_openSet.Enqueue(nIdx, cost);
					}
				}
			}
		}
		ArrayPool<int>.Shared.Return(parentIndices);
	}

	private void FinishClaimingRegions()
	{
		_openSet.Clear();
		int[] parentIndices = ArrayPool<int>.Shared.Rent(_totalCells);
		Array.Fill(parentIndices, -1, 0, _totalCells);
		for (int i = 0; i < _totalCells; i++)
		{
			if (_sectorMap[i] != -1)
			{
				parentIndices[i] = i;
				_openSet.Enqueue(i, 0f);
			}
		}
		Direction[] localDirs = new Direction[]
		{
			new Direction(1, true, _width - 1),
			new Direction(-1, true, 0),
			new Direction(_width, false, 0),
			new Direction(-_width, false, 0),
		};
		float terrainWeight = 200.0f;
		float distanceWeight = 1.0f;
		while (_openSet.Count > 0)
		{
			int idx = _openSet.Dequeue();
			int currentSectorId = _sectorMap[idx];
			int cx = idx % _width;
			float currentHeight = _heightMap[idx];
			float parentHeight = _heightMap[parentIndices[idx]];
			float heightDelta = MathF.Abs(currentHeight - parentHeight);
			foreach (var dir in localDirs)
			{
				if (dir.CheckBounds && cx == dir.XLimit)
					continue;
				int nIdx = idx + dir.Offset;
				if (nIdx < 0 || nIdx >= _totalCells)
					continue;
				if (_regionMap[nIdx] == _sectorRegionIds[currentSectorId] && _sectorMap[nIdx] == -1)
				{
					_sectorMap[nIdx] = currentSectorId;
					_currentSectorAreas[currentSectorId]++;
					parentIndices[nIdx] = idx;
					float cost = distanceWeight + (heightDelta * terrainWeight);
					_openSet.Enqueue(nIdx, cost);
				}
			}
		}
		ArrayPool<int>.Shared.Return(parentIndices);
	}

	private void SaveResultsToGodot(GodotObject world, GodotObject context)
	{
		Godot.Collections.Array regionsArray = (Godot.Collections.Array)world.Get("regions");
		for (int s = 0; s < _totalSectorsCount; s++)
		{
			int myRegionId = _sectorRegionIds[s];
			int centerIdx = _sectorCentersIdx[s];
			Godot.Collections.Array sectorsArray = (Godot.Collections.Array)
				((GodotObject)regionsArray[myRegionId]).Get("sectors");
			foreach (GodotObject sectorObj in sectorsArray)
			{
				if ((int)sectorObj.Get("id") == s)
				{
					// Uložíme finální data přímo do runtime třídy Sector v Godotu

					sectorObj.Set("center", new Vector2(centerIdx % _width, centerIdx / _width));
					sectorObj.Set("current_area", _currentSectorAreas[s]);
					break;
				}
			}
		}

		// Zapíšeme mapu do kontextu

		context.Set("sector_id_map", _sectorMap);
	}
}
