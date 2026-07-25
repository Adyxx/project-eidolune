using Godot;
using System;
using System.Collections.Generic;

public partial class region_generator : RefCounted
{
	public Godot.Collections.Dictionary RunRegionGeneration(
		int width, int height, int numRegions, float[] heightMap, byte[] landMaskMap, float[] sizeWeights
	)
	{
		int totalCells = width * height;
		int[] regionMap = new int[totalCells];
		Array.Fill(regionMap, -1);

		List<int> landIndices = new List<int>();
		for (int i = 0; i < totalCells; i++)
		{
			if (landMaskMap[i] == 1) landIndices.Add(i);
		}

		int[] centersX = new int[numRegions];
		int[] centersY = new int[numRegions];
		Random rand = new Random();

		for (int r = 0; r < numRegions; r++)
		{
			if (landIndices.Count == 0) break;
			int randIdx = landIndices[rand.Next(landIndices.Count)];
			centersX[r] = randIdx % width;
			centersY[r] = randIdx / width;
			regionMap[randIdx] = r;
		}

		Queue<int>[] frontiersCurrent = new Queue<int>[numRegions];
		Queue<int>[] frontiersNext = new Queue<int>[numRegions];
		float[] accumulators = new float[numRegions];
		byte[] queuedMap = new byte[totalCells];

		int[] dx = { 1, -1, 0, 0 };
		int[] dy = { 0, 0, 1, -1 };

		for (int r = 0; r < numRegions; r++)
		{
			frontiersCurrent[r] = new Queue<int>();
			frontiersNext[r] = new Queue<int>();
			
			int cx = centersX[r];
			int cy = centersY[r];

			for (int i = 0; i < 4; i++)
			{
				int nx = cx + dx[i];
				int ny = cy + dy[i];
				if (nx >= 0 && ny >= 0 && nx < width && ny < height)
				{
					int nIdx = nx + ny * width;
					if (landMaskMap[nIdx] == 1 && regionMap[nIdx] == -1)
					{
						frontiersCurrent[r].Enqueue(nIdx);
						queuedMap[nIdx] = 1;
					}
				}
			}
		}

		bool activeGrowth = true;
		while (activeGrowth)
		{
			activeGrowth = false;
			for (int r = 0; r < numRegions; r++)
			{
				if (frontiersCurrent[r].Count == 0) continue;
				activeGrowth = true;

				accumulators[r] += sizeWeights[r];
				if (accumulators[r] < 1.0f) continue;

				int steps = (int)MathF.Floor(accumulators[r]);
				accumulators[r] -= steps;

				for (int step = 0; step < steps; step++)
				{
					if (frontiersCurrent[r].Count == 0) break;

					while (frontiersCurrent[r].Count > 0)
					{
						int cellIdx = frontiersCurrent[r].Dequeue();
						queuedMap[cellIdx] = 0;

						if (regionMap[cellIdx] != -1) continue;

						int cx = cellIdx % width;
						int cy = cellIdx / width;

						if (heightMap[cellIdx] > 0.45f && rand.NextDouble() > 0.3)
						{
							frontiersNext[r].Enqueue(cellIdx);
							continue;
						}

						regionMap[cellIdx] = r;

						for (int i = 0; i < 4; i++)
						{
							int nx = cx + dx[i];
							int ny = cy + dy[i];
							if (nx >= 0 && ny >= 0 && nx < width && ny < height)
							{
								int nIdx = nx + ny * width;
								if (landMaskMap[nIdx] == 1 && regionMap[nIdx] == -1 && queuedMap[nIdx] == 0)
								{
									queuedMap[nIdx] = 1;
									frontiersNext[r].Enqueue(nIdx);
								}
							}
						}
					}
					var temp = frontiersCurrent[r];
					frontiersCurrent[r] = frontiersNext[r];
					frontiersNext[r] = temp;
				}
			}
		}

		var output = new Godot.Collections.Dictionary();
		output["region_map"] = regionMap;
		output["centers_x"] = centersX;
		output["centers_y"] = centersY;
		return output;
	}
}
