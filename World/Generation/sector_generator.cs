using Godot;
using System;
using System.Collections.Generic;

public partial class sector_generator : RefCounted
{
	public int[] RunSectorGeneration(
		int width, int height, int totalSectors, int[] regionMap, int[] sectorToRegionId, float[] heightMap, float[] sectorWeights
	)
	{
		int totalCells = width * height;
		int[] sectorMap = new int[totalCells];
		Array.Fill(sectorMap, -1);

		Dictionary<int, List<int>> cellsByRegion = new Dictionary<int, List<int>>();
		for (int i = 0; i < totalCells; i++)
		{
			int rId = regionMap[i];
			if (rId != -1)
			{
				if (!cellsByRegion.ContainsKey(rId))
					cellsByRegion[rId] = new List<int>();
				cellsByRegion[rId].Add(i);
			}
		}

		Queue<int>[] frontiersCurrent = new Queue<int>[totalSectors];
		Queue<int>[] frontiersNext = new Queue<int>[totalSectors];
		float[] accumulators = new float[totalSectors];
		byte[] queuedMap = new byte[totalCells];
		Random rand = new Random();

		int[] dx = { 1, -1, 0, 0 };
		int[] dy = { 0, 0, 1, -1 };

		for (int s = 0; s < totalSectors; s++)
		{
			frontiersCurrent[s] = new Queue<int>();
			frontiersNext[s] = new Queue<int>();

			int myRegionId = sectorToRegionId[s];
			if (!cellsByRegion.ContainsKey(myRegionId) || cellsByRegion[myRegionId].Count == 0)
				continue;

			List<int> regionCells = cellsByRegion[myRegionId];
			int cIdx = -1;
			
			for (int attempt = 0; attempt < 50; attempt++)
			{
				int potentialIdx = regionCells[rand.Next(regionCells.Count)];
				if (sectorMap[potentialIdx] == -1)
				{
					cIdx = potentialIdx;
					break;
				}
			}

			if (cIdx == -1) cIdx = regionCells[rand.Next(regionCells.Count)];

			sectorMap[cIdx] = s;
			frontiersCurrent[s].Enqueue(cIdx);
			queuedMap[cIdx] = 1;
		}

		bool activeGrowth = true;
		while (activeGrowth)
		{
			activeGrowth = false;
			for (int s = 0; s < totalSectors; s++)
			{
				if (frontiersCurrent[s].Count == 0) continue;
				activeGrowth = true;

				accumulators[s] += sectorWeights[s];
				if (accumulators[s] < 1.0f) continue;

				int steps = (int)MathF.Floor(accumulators[s]);
				accumulators[s] -= steps;

				for (int step = 0; step < steps; step++)
				{
					if (frontiersCurrent[s].Count == 0) break;

					while (frontiersCurrent[s].Count > 0)
					{
						int cellIdx = frontiersCurrent[s].Dequeue();
						queuedMap[cellIdx] = 0;

						if (sectorMap[cellIdx] != -1 && sectorMap[cellIdx] != s) continue;

						int cx = cellIdx % width;
						int cy = cellIdx / width;
						int myRegionId = sectorToRegionId[s];

						if (heightMap[cellIdx] > 0.45f && rand.NextDouble() > 0.3)
						{
							frontiersNext[s].Enqueue(cellIdx);
							continue;
						}

						sectorMap[cellIdx] = s;

						for (int i = 0; i < 4; i++)
						{
							int nx = cx + dx[i];
							int ny = cy + dy[i];
							if (nx >= 0 && ny >= 0 && nx < width && ny < height)
							{
								int nIdx = nx + ny * width;
								if (regionMap[nIdx] == myRegionId && sectorMap[nIdx] == -1 && queuedMap[nIdx] == 0)
								{
									queuedMap[nIdx] = 1;
									frontiersNext[s].Enqueue(nIdx);
								}
							}
						}
					}
					var temp = frontiersCurrent[s];
					frontiersCurrent[s] = frontiersNext[s];
					frontiersNext[s] = temp;
				}
			}
		}

		return sectorMap;
	}
}
