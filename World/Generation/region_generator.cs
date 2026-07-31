using System;
using System.Buffers;
using System.Collections.Generic;
using Godot;

public partial class region_generator : RefCounted
{
	private int _width;
	private int _height;
	private int _totalCells;
	private int _worldSeed;

	private byte[] _landMaskMap;
	private float[] _heightMap;
	private byte[] _playableMap;
	private int[] _regionMap;
	private int[] _riverMap;

	private int _numRegions;
	private int[] _regionMinAreas;
	private bool[] _requiresCoast;
	private int[] _currentAreas;
	private int[] _finalCentersX;
	private int[] _finalCentersY;
	private PriorityQueue<int, float> _openSet;

	private struct PlacementRegion
	{
		public int Id;
		public int MinArea;
		public bool RequiresCoast;
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

	private static readonly Direction[] Directions = new Direction[]
	{
		new Direction(1, true, -1),
		new Direction(-1, true, 0),
		new Direction(0, false, 0),
		new Direction(0, false, 0),
	};

	public struct PoissonSample
	{
		public int X;
		public int Y;
		public int Idx;
		public float DistanceToCoast;
	}

	private static readonly float[] PrecomputedCos;
	private static readonly float[] PrecomputedSin;

	static region_generator()
	{
		PrecomputedCos = new float[30];
		PrecomputedSin = new float[30];
		for (int i = 0; i < 30; i++)
		{
			float angle = (float)(i * Math.PI * 2.0 / 30.0);
			PrecomputedCos[i] = MathF.Cos(angle);
			PrecomputedSin[i] = MathF.Sin(angle);
		}
	}

	public int[] GenerateRegions(int width, int height, GodotObject world, GodotObject context)
	{
		_width = width;
		_height = height;
		_totalCells = width * height;
		_worldSeed = ((int)world.Get("main_seed"));

		_landMaskMap = ((Variant)context.Get("land_mask_map")).AsByteArray();
		_heightMap = ((Variant)context.Get("height_map")).AsFloat32Array();
		_playableMap = ((Variant)context.Get("playable_map")).AsByteArray();
		_riverMap = ((Variant)context.Get("river_id_map")).AsInt32Array();

		_regionMap = new int[_totalCells];
		Array.Fill(_regionMap, -1);

		int mainContinentSize = ((int)world.Get("mainContinentSize"));

		Godot.Collections.Array regionsArray = (Godot.Collections.Array)world.Get("regions");
		_numRegions = regionsArray.Count;

		_regionMinAreas = new int[_numRegions];
		_requiresCoast = new bool[_numRegions];
		_currentAreas = new int[_numRegions];
		_finalCentersX = new int[_numRegions];
		_finalCentersY = new int[_numRegions];

		for (int i = 0; i < _numRegions; i++)
		{
			GodotObject regionInstance = (GodotObject)regionsArray[i];
			GodotObject definition = (GodotObject)regionInstance.Get("definition");

			int rId = (int)regionInstance.Get("id");
			_regionMinAreas[rId] = (int)definition.Get("min_area");
			_requiresCoast[rId] = (bool)definition.Get("requires_coast");
		}

		Vector2 worldCenter = (Vector2)context.Get("world_center");

		int startIdx = ((int)world.Get("startIdx"));
		int targetCandidatesCount = _numRegions * 9;

		float averageAreaPerCandidate = mainContinentSize / (float)targetCandidatesCount;
		float dynamicRadius = MathF.Sqrt(averageAreaPerCandidate / MathF.PI) * 0.85f;
		if (dynamicRadius < 8.0f)
			dynamicRadius = 8.0f;

		List<PoissonSample> candidates = GeneratePoissonCandidates(
			startIdx,
			dynamicRadius,
			targetCandidatesCount
		);

		GD.Print($"\n==============================================================");
		GD.Print($" * Generation seed: {_worldSeed}");
		GD.Print($" * Regions requested: {_numRegions}");
		GD.Print($" * Main continenet size: {mainContinentSize}");
		GD.Print($" * Possion points requested: {targetCandidatesCount}");
		GD.Print($" * Calculated gap between points: {dynamicRadius:F2} px.");
		GD.Print($" * Realistically generated: {candidates.Count} Poisson points.");

		GD.Print("==============================================================\n");

		PlaceRegionSeeds(candidates);

		bool validationPassed = false;
		int maxFixAttempts = 5;

		HashSet<int> burnedCandidateIds = new HashSet<int>();

		for (int attempt = 0; attempt < maxFixAttempts; attempt++)
		{
			GrowRegionsToMinimums(attempt);
			validationPassed = ValidateMinimumAreas();

			if (validationPassed)
				break;

			for (int rId = 0; rId < _numRegions; rId++)
			{
				if (_currentAreas[rId] >= _regionMinAreas[rId])
					continue;

				_currentAreas[rId] = 0;

				RelocateRegion(rId, candidates, burnedCandidateIds);
			}
		}

		if (validationPassed)
		{
			FinishClaimingContinent();

			Array.Clear(_currentAreas, 0, _numRegions);
			for (int i = 0; i < _totalCells; i++)
			{
				if (_regionMap[i] != -1)
					_currentAreas[_regionMap[i]]++;
			}
			for (int i = 0; i < _numRegions; i++)
			{
				GodotObject regionInstance = (GodotObject)regionsArray[i];
				regionInstance.Set("current_area", _currentAreas[i]);
			}

			AdoptUnclaimedRiverIslands();
		}
		else
		{
			GD.PrintErr($" * FAILED TO CREATE SUITABLE REGIONS! ({maxFixAttempts} attempts).");

			return null;
		}

		return _regionMap;
	}

	/* Relocates region's possion point */
	private void RelocateRegion(
		int rId,
		List<PoissonSample> candidates,
		HashSet<int> burnedCandidateIds
	)
	{
		for (int c = 0; c < candidates.Count; c++)
		{
			if (candidates[c].X == _finalCentersX[rId] && candidates[c].Y == _finalCentersY[rId])
			{
				burnedCandidateIds.Add(c);
				break;
			}
		}

		bool foundNewHome = false;

		for (int i = 0; i < candidates.Count; i++)
		{
			if (burnedCandidateIds.Contains(i))
				continue;

			var candidate = candidates[i];

			if (_requiresCoast[rId] && candidate.DistanceToCoast > 40.0f)
				continue;
			if (!_requiresCoast[rId] && candidate.DistanceToCoast <= 40.0f)
				continue;

			if (!HasEnoughFreeSpaceAround(candidate.X, candidate.Y, _regionMinAreas[rId]))
				continue;

			burnedCandidateIds.Add(i);
			_finalCentersX[rId] = candidate.X;
			_finalCentersY[rId] = candidate.Y;
			foundNewHome = true;

			GD.Print(
				$" * Region {rId} relocated to new poisson point: [{candidate.X}, {candidate.Y}]."
			);
			break;
		}

		if (!foundNewHome)
		{
			for (int i = 0; i < candidates.Count; i++)
			{
				if (!burnedCandidateIds.Contains(i))
				{
					burnedCandidateIds.Add(i);
					_finalCentersX[rId] = candidates[i].X;
					_finalCentersY[rId] = candidates[i].Y;
					GD.Print($" * WARNING: Region {rId} relocated to fallback point.");
					break;
				}
			}
		}
	}

	/*
	Sorts and returns regions, based on priority:
	Primarly: Requires Coast = true.
	Secondarily: MinArea.
	*/
	private List<PlacementRegion> GetSortedRegions()
	{
		List<PlacementRegion> sorted = new List<PlacementRegion>(_numRegions);

		for (int i = 0; i < _numRegions; i++)
		{
			sorted.Add(
				new PlacementRegion
				{
					Id = i,
					MinArea = _regionMinAreas[i],
					RequiresCoast = _requiresCoast[i],
				}
			);
		}

		sorted.Sort(
			(a, b) =>
			{
				if (a.RequiresCoast != b.RequiresCoast)
					return b.RequiresCoast ? 1 : -1;

				return b.MinArea - a.MinArea;
			}
		);

		return sorted;
	}

	/*
	Evenly places poisson points across the playable region.
	Every point remebers its X, Y, and DistanceToCoast.
	*/
	private List<PoissonSample> GeneratePoissonCandidates(
		int firstTile,
		float radius,
		int maxCandidates
	)
	{
		List<PoissonSample> samples = new List<PoissonSample>(maxCandidates);
		if (firstTile == -1)
			return samples;

		int processCapacity = _totalCells;
		int[] processList = ArrayPool<int>.Shared.Rent(processCapacity);
		int head = 0;
		int tail = 0;

		float cellSize = radius / 1.41421356f;
		float invCellSize = 1.0f / cellSize;
		int gridWidth = (int)MathF.Ceiling(_width * invCellSize);
		int gridHeight = (int)MathF.Ceiling(_height * invCellSize);

		int[] grid = ArrayPool<int>.Shared.Rent(gridWidth * gridHeight);
		Array.Fill(grid, -1, 0, gridWidth * gridHeight);

		GD.Print(
			$"CANDIDATES: totalcells: {_totalCells}, radius: {radius}, maxcandidates: {maxCandidates}"
		);

		int fx = firstTile % _width;
		int fy = firstTile / _width;

		PoissonSample firstSample = new PoissonSample
		{
			X = fx,
			Y = fy,
			Idx = firstTile,
			DistanceToCoast = 0f,
		};

		int firstSampleID = samples.Count;
		samples.Add(firstSample);
		processList[tail++] = firstSampleID;
		grid[(int)(fx * invCellSize) + (int)(fy * invCellSize) * gridWidth] = firstSampleID;

		float radiusSq = radius * radius;
		Random rand = new Random(_worldSeed + 123);

		while (head < tail && samples.Count < maxCandidates)
		{
			int currentSampleID = processList[head++];
			int cx = samples[currentSampleID].X;
			int cy = samples[currentSampleID].Y;

			for (int i = 0; i < 30; i++)
			{
				float r = radius * ((float)rand.NextDouble() + 1.0f);
				int candidateX = (int)(cx + r * PrecomputedCos[i]);
				int candidateY = (int)(cy + r * PrecomputedSin[i]);

				if (!IsInBounds(candidateX, candidateY))
					continue;

				int candidateIdx = candidateX + candidateY * _width;
				if (_playableMap[candidateIdx] == 0)
					continue;

				if (
					IsPoissonPositionValid(
						candidateX,
						candidateY,
						invCellSize,
						gridWidth,
						gridHeight,
						grid,
						radiusSq,
						samples
					)
				)
				{
					PoissonSample newSample = new PoissonSample
					{
						X = candidateX,
						Y = candidateY,
						Idx = candidateIdx,
						DistanceToCoast = 0f,
					};

					int newSampleID = samples.Count;
					samples.Add(newSample);

					processList[tail++] = newSampleID;
					grid[
						(int)(candidateX * invCellSize)
							+ (int)(candidateY * invCellSize) * gridWidth
					] = newSampleID;

					if (samples.Count >= maxCandidates)
						break;
				}
			}
		}

		for (int i = 0; i < samples.Count; i++)
		{
			var sample = samples[i];
			sample.DistanceToCoast = CalculateDistanceToSea(sample.X, sample.Y);
			samples[i] = sample;
		}

		ArrayPool<int>.Shared.Return(processList);
		ArrayPool<int>.Shared.Return(grid);
		return samples;
	}

	private bool IsPoissonPositionValid(
		int cx,
		int cy,
		float invCellSize,
		int gWidth,
		int gHeight,
		int[] grid,
		float radiusSq,
		List<PoissonSample> samples
	)
	{
		int cellX = (int)(cx * invCellSize);
		int cellY = (int)(cy * invCellSize);

		int minX = cellX > 0 ? cellX - 1 : 0;
		int maxX = cellX < gWidth - 1 ? cellX + 1 : gWidth - 1;
		int minY = cellY > 0 ? cellY - 1 : 0;
		int maxY = cellY < gHeight - 1 ? cellY + 1 : gHeight - 1;

		for (int y = minY; y <= maxY; y++)
		{
			int rowOffset = y * gWidth;
			for (int x = minX; x <= maxX; x++)
			{
				int otherSampleID = grid[x + rowOffset];
				if (otherSampleID != -1)
				{
					float dx = cx - samples[otherSampleID].X;
					float dy = cy - samples[otherSampleID].Y;
					if ((dx * dx + dy * dy) < radiusSq)
						return false;
				}
			}
		}
		return true;
	}

	private float CalculateDistanceToSea(int cx, int cy)
	{
		if (_landMaskMap[cx + cy * _width] == 0)
			return 0f;

		for (int r = 1; r < 120; r++)
		{
			int minX = cx - r;
			int maxX = cx + r;
			int minY = cy - r;
			int maxY = cy + r;

			for (int x = minX; x <= maxX; x++)
			{
				if (IsInBounds(x, minY) && _landMaskMap[x + minY * _width] == 0)
					return r;
				if (IsInBounds(x, maxY) && _landMaskMap[x + maxY * _width] == 0)
					return r;
			}

			for (int y = minY + 1; y < maxY; y++)
			{
				if (IsInBounds(minX, y) && _landMaskMap[minX + y * _width] == 0)
					return r;
				if (IsInBounds(maxX, y) && _landMaskMap[maxX + y * _width] == 0)
					return r;
			}
		}
		return 120.0f;
	}

	[System.Runtime.CompilerServices.MethodImpl(
		System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
	)]
	private bool IsInBounds(int x, int y)
	{
		return x >= 0 && x < _width && y >= 0 && y < _height;
	}

	/*
	Places a "starting point to grow from" for every region.
	Uses cost - regions requiring coast have a point selected near coast
	*/
	private void PlaceRegionSeeds(List<PoissonSample> poissonCandidates)
	{
		List<PlacementRegion> sortedRegions = GetSortedRegions();

		bool[] isCandidateOccupied = ArrayPool<bool>.Shared.Rent(poissonCandidates.Count);
		Array.Clear(isCandidateOccupied, 0, poissonCandidates.Count);

		List<PoissonSample> placedSeeds = new List<PoissonSample>(_numRegions);
		float minRegionDistanceSq = 80.0f * 80.0f;

		for (int i = 0; i < sortedRegions.Count; i++)
		{
			PlacementRegion region = sortedRegions[i];
			int bestCandidateIdx = -1;
			float bestScore = float.MaxValue;

			for (int c = 0; c < poissonCandidates.Count; c++)
			{
				if (isCandidateOccupied[c])
					continue;

				PoissonSample candidate = poissonCandidates[c];

				if (region.RequiresCoast && candidate.DistanceToCoast > 40.0f)
					continue;
				if (!region.RequiresCoast && candidate.DistanceToCoast <= 40.0f)
					continue;

				bool tooClose = false;
				for (int s = 0; s < placedSeeds.Count; s++)
				{
					float dx = candidate.X - placedSeeds[s].X;
					float dy = candidate.Y - placedSeeds[s].Y;
					if ((dx * dx + dy * dy) < minRegionDistanceSq)
					{
						tooClose = true;
						break;
					}
				}
				if (tooClose)
					continue;

				float score = region.RequiresCoast
					? candidate.DistanceToCoast
					: 1000.0f - candidate.DistanceToCoast;

				if (score < bestScore)
				{
					bestScore = score;
					bestCandidateIdx = c;
				}
			}

			if (bestCandidateIdx == -1)
			{
				for (int c = 0; c < poissonCandidates.Count; c++)
				{
					if (!isCandidateOccupied[c])
					{
						bestCandidateIdx = c;
						break;
					}
				}
			}

			if (bestCandidateIdx != -1)
			{
				isCandidateOccupied[bestCandidateIdx] = true;
				PoissonSample bestCandidate = poissonCandidates[bestCandidateIdx];
				placedSeeds.Add(bestCandidate);

				_finalCentersX[region.Id] = bestCandidate.X;
				_finalCentersY[region.Id] = bestCandidate.Y;

				GD.Print(
					$" * Region ID {region.Id} (RequiresCoast: {region.RequiresCoast}) relocated to [{bestCandidate.X}, {bestCandidate.Y}]."
				);
			}
			else
			{
				GD.PrintErr($" * ERROR: Failed to find free poisson point for region {region.Id}!");
			}
		}

		ArrayPool<bool>.Shared.Return(isCandidateOccupied);
	}

	/*
	Grows regions in-order (not at the same time) until their minSize is met.
	Uses height terrain bias for more realistic region shapes.
	*/
	private void GrowRegionsToMinimums(int attempt)
	{
		Array.Fill(_regionMap, -1);

		if (_openSet == null)
			_openSet = new PriorityQueue<int, float>(_totalCells / 10);
		else
			_openSet.Clear();

		int[] parentIndices = ArrayPool<int>.Shared.Rent(_totalCells);
		Array.Fill(parentIndices, -1, 0, _totalCells);

		Random rand = new Random(_worldSeed + 500 + attempt);
		Array.Clear(_currentAreas, 0, _numRegions);

		for (int rId = 0; rId < _numRegions; rId++)
		{
			int cx = _finalCentersX[rId];
			int cy = _finalCentersY[rId];
			if (cx == 0 && cy == 0)
				continue;

			int seedIdx = cx + cy * _width;
			if (_playableMap[seedIdx] > 0)
			{
				_regionMap[seedIdx] = rId;
				parentIndices[seedIdx] = seedIdx;
				_currentAreas[rId] = 1;
				_openSet.Enqueue(seedIdx, 0f);
			}
		}

		Direction[] localDirs = new Direction[]
		{
			new Direction(1, true, _width - 1),
			new Direction(-1, true, 0),
			new Direction(_width, false, 0),
			new Direction(-_width, false, 0),
		};

		float terrainWeight = 450.0f;
		float distanceWeight = 1.0f;

		while (_openSet.Count > 0)
		{
			int idx = _openSet.Dequeue();
			int currentRegionId = _regionMap[idx];

			if (_currentAreas[currentRegionId] >= _regionMinAreas[currentRegionId])
				continue;

			int parentIdx = parentIndices[idx];
			int cx = idx % _width;

			float currentHeight = _heightMap[idx];
			float parentHeight = _heightMap[parentIdx];
			float heightDelta = MathF.Abs(currentHeight - parentHeight);

			foreach (var dir in localDirs)
			{
				if (dir.CheckBounds && cx == dir.XLimit)
					continue;

				int nIdx = idx + dir.Offset;
				if (nIdx < 0 || nIdx >= _totalCells)
					continue;

				if (_playableMap[nIdx] > 0 && _regionMap[nIdx] == -1 && _riverMap[nIdx] == -1)
				{
					if (_currentAreas[currentRegionId] < _regionMinAreas[currentRegionId])
					{
						_regionMap[nIdx] = currentRegionId;
						parentIndices[nIdx] = idx;
						_currentAreas[currentRegionId]++;

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

	/*
	After regions have their minSize met, this function finishes claiming the rest of the yet unclaimed tiles.
	Uses height terrain bias for more realistic region shapes.
	*/
	private void FinishClaimingContinent()
	{
		if (_openSet == null)
			_openSet = new PriorityQueue<int, float>(_totalCells / 10);
		else
			_openSet.Clear();

		int[] parentIndices = ArrayPool<int>.Shared.Rent(_totalCells);
		Array.Fill(parentIndices, -1, 0, _totalCells);

		for (int i = 0; i < _totalCells; i++)
		{
			if (_regionMap[i] != -1)
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

		float terrainWeight = 450.0f;
		float distanceWeight = 1.0f;

		while (_openSet.Count > 0)
		{
			int idx = _openSet.Dequeue();
			int currentRegionId = _regionMap[idx];
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

				if (_playableMap[nIdx] > 0 && _regionMap[nIdx] == -1 && _riverMap[nIdx] == -1)
				{
					_regionMap[nIdx] = currentRegionId;
					parentIndices[nIdx] = idx;

					float cost = distanceWeight + (heightDelta * terrainWeight);
					_openSet.Enqueue(nIdx, cost);
				}
			}
		}

		ArrayPool<int>.Shared.Return(parentIndices);
		GD.Print(" * Claiming continent finished.");
	}

	private bool ValidateMinimumAreas()
	{
		bool allValid = true;
		GD.Print($"\n=================== VALIDATE MINIMUM AREAS ===================");

		for (int rId = 0; rId < _numRegions; rId++)
		{
			bool success = _currentAreas[rId] >= _regionMinAreas[rId];
			string status = success ? "✓ OK" : "✗ FAILED";

			GD.Print(
				$" * Region ID {rId}: Calculated {_currentAreas[rId]} / Requsted {_regionMinAreas[rId]} tiles | {status}"
			);

			if (!success)
				allValid = false;
		}
		GD.Print("==============================================================\n");
		return allValid;
	}

	private bool HasEnoughFreeSpaceAround(int cx, int cy, int requiredArea)
	{
		int radius = (int)(MathF.Sqrt(requiredArea / MathF.PI) * 1.5f);
		if (radius < 20)
			radius = 20;

		int freeTilesCount = 0;

		int minX = Math.Max(0, cx - radius);
		int maxX = Math.Min(_width - 1, cx + radius);
		int minY = Math.Max(0, cy - radius);
		int maxY = Math.Min(_height - 1, cy + radius);

		for (int y = minY; y <= maxY; y++)
		{
			int rowOffset = y * _width;
			for (int x = minX; x <= maxX; x++)
			{
				int idx = x + rowOffset;
				if (_playableMap[idx] > 0 && _regionMap[idx] == -1)
				{
					freeTilesCount++;
					if (freeTilesCount >= requiredArea)
						return true;
				}
			}
		}

		return false;
	}

	/*
	Experimental function. Detect unclaimed areas of the main continent (if for instance they get cut by the river).
	And selects most suitable region do claim this area.
	*/
	private void AdoptUnclaimedRiverIslands()
	{
		bool[] visited = ArrayPool<bool>.Shared.Rent(_totalCells);
		Array.Clear(visited, 0, _totalCells);

		int[] bfsQueue = ArrayPool<int>.Shared.Rent(_totalCells);

		Direction[] localDirs = new Direction[]
		{
			new Direction(1, true, _width - 1),
			new Direction(-1, true, 0),
			new Direction(_width, false, 0),
			new Direction(-_width, false, 0),
		};

		for (int startIdx = 0; startIdx < _totalCells; startIdx++)
		{
			if (_playableMap[startIdx] == 0 || _regionMap[startIdx] != -1 || visited[startIdx])
				continue;

			int head = 0;
			int tail = 0;

			List<int> islandTiles = new List<int>();
			HashSet<int> touchingRegionIds = new HashSet<int>();

			bfsQueue[tail++] = startIdx;
			visited[startIdx] = true;

			while (head < tail)
			{
				int idx = bfsQueue[head++];
				islandTiles.Add(idx);
				int cx = idx % _width;

				foreach (var dir in localDirs)
				{
					if (dir.CheckBounds && cx == dir.XLimit)
						continue;

					int nIdx = idx + dir.Offset;
					if (nIdx < 0 || nIdx >= _totalCells)
						continue;

					if (_regionMap[nIdx] != -1)
					{
						touchingRegionIds.Add(_regionMap[nIdx]);
					}
					else if (_playableMap[nIdx] > 0 && !visited[nIdx])
					{
						visited[nIdx] = true;
						bfsQueue[tail++] = nIdx;
					}
				}
			}

			if (touchingRegionIds.Count == 0)
				continue;

			int bestParentId = -1;
			float lowestScale = float.MaxValue;

			foreach (int rId in touchingRegionIds)
			{
				float scale = (float)_currentAreas[rId] / _regionMinAreas[rId];

				if (scale < lowestScale)
				{
					lowestScale = scale;
					bestParentId = rId;
				}
			}

			if (bestParentId != -1)
			{
				foreach (int tileIdx in islandTiles)
				{
					_regionMap[tileIdx] = bestParentId;
					_currentAreas[bestParentId]++;
				}
				GD.Print(
					$" Isolated area with size: {islandTiles.Count} tiles got adpoted by ID {bestParentId} (Size scale: {lowestScale * 100f:F1}%)"
				);
			}
		}

		ArrayPool<bool>.Shared.Return(visited);
		ArrayPool<int>.Shared.Return(bfsQueue);
	}
}
