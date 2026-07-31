using System;
using System.Buffers;
using System.Collections.Generic;
using Godot;

public partial class climate_generator : RefCounted
{
	private int _width;
	private int _height;

	private byte[] _playableMap;

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

	public Godot.Collections.Dictionary RunGeneration(GodotObject settings, GodotObject context)
	{
		_width = ((int)settings.Get("MAP_WIDTH"));
		_height = ((int)settings.Get("MAP_HEIGHT"));

		Vector2 worldCenter = (Vector2)context.Get("world_center");

		var nWarpX = ((FastNoiseLite)context.Get("warp_x"));
		var nWarpY = ((FastNoiseLite)context.Get("warp_y"));

		var warpStrength = ((float)settings.Get("DOMAIN_WARP_STRENGTH"));

		var continentFalloff = ((float)settings.Get("CONTINENT_FALLOFF"));
		var seaLevel = ((float)settings.Get("SEA_LEVEL"));

		var nHeight = ((FastNoiseLite)context.Get("height"));
		var nTemp = ((FastNoiseLite)context.Get("temperature"));
		var nMoist = ((FastNoiseLite)context.Get("moisture"));

		int totalCells = _width * _height;
		float centerX = _width / 2.0f;
		float centerY = _height / 2.0f;

		float[] heightMap = new float[totalCells];
		float[] temperatureMap = new float[totalCells];
		float[] moistureMap = new float[totalCells];
		byte[] landMaskMap = new byte[totalCells];

		_playableMap = ((Variant)context.Get("playable_map")).AsByteArray();
		Array.Fill(_playableMap, (byte)0);

		System.Threading.Tasks.Parallel.For(
			0,
			_height,
			y =>
			{
				float floatY = (float)y;
				float latitude = floatY / _height;
				int rowOffset = y * _width;

				float dy = MathF.Abs(floatY - centerY) / centerY;
				float oneMinusDySq = 1.0f - (dy * dy);

				for (int x = 0; x < _width; x++)
				{
					float floatX = (float)x;
					int idx = x + rowOffset;

					float offsetX = nWarpX.GetNoise2D(floatX, floatY) * warpStrength;
					float offsetY = nWarpY.GetNoise2D(floatX, floatY) * warpStrength;

					float sampleX = floatX + offsetX;
					float sampleY = floatY + offsetY;

					float baseHeight = (nHeight.GetNoise2D(sampleX, sampleY) + 1.0f) * 0.5f;
					float dx = MathF.Abs(floatX - centerX) / centerX;
					float distMask = 1.0f - ((1.0f - dx * dx) * oneMinusDySq);

					float hVal = Mathf.Clamp(
						baseHeight - (distMask * continentFalloff),
						0.0f,
						1.0f
					);
					heightMap[idx] = hVal;
					landMaskMap[idx] = (byte)(hVal >= seaLevel ? 1 : 0);

					float coldFromHeight = hVal * 0.4f;
					float noiseT = nTemp.GetNoise2D(floatX, floatY) * 0.2f;
					temperatureMap[idx] = Mathf.Clamp(
						1.0f - latitude - coldFromHeight + noiseT,
						0.0f,
						1.0f
					);

					moistureMap[idx] = (nMoist.GetNoise2D(sampleX, sampleY) + 1.0f) * 0.5f;
				}
			}
		);

		int startIdx = FindStartLandTile(worldCenter, landMaskMap);
		int mainContinentSize = 0;

		if (startIdx != -1)
		{
			mainContinentSize = FloodFillMainContinent(startIdx, totalCells, landMaskMap);
		}

		var result = new Godot.Collections.Dictionary();
		result["height_map"] = heightMap;
		result["temperature_map"] = temperatureMap;
		result["moisture_map"] = moistureMap;
		result["land_mask_map"] = landMaskMap;
		result["playable_map"] = _playableMap;
		result["mainContinentSize"] = mainContinentSize;

		result["startIdx"] = startIdx;

		return result;
	}

	/*
	Finds first main continent tile, starting from world center.
	*/
	private int FindStartLandTile(Vector2 worldCenter, byte[] _landMaskMap)
	{
		int cx = (int)worldCenter.X;
		int cy = (int)worldCenter.Y;

		if (IsInBounds(cx, cy))
		{
			int centerIdx = cx + cy * _width;
			if (_landMaskMap[centerIdx] > 0)
				return centerIdx;
		}

		for (int r = 1; r < 150; r++)
		{
			int minX = cx - r;
			int maxX = cx + r;
			int minY = cy - r;
			int maxY = cy + r;

			for (int x = minX; x <= maxX; x++)
			{
				if (IsInBounds(x, minY) && _landMaskMap[x + minY * _width] > 0)
					return x + minY * _width;
				if (IsInBounds(x, maxY) && _landMaskMap[x + maxY * _width] > 0)
					return x + maxY * _width;
			}

			for (int y = minY + 1; y < maxY; y++)
			{
				if (IsInBounds(minX, y) && _landMaskMap[minX + y * _width] > 0)
					return minX + y * _width;
				if (IsInBounds(maxX, y) && _landMaskMap[maxX + y * _width] > 0)
					return maxX + y * _width;
			}
		}

		return -1;
	}

	/*
	Runs a floodfill on the main continent and maps the playable continent.
	Returns tile count of the main continent.
	*/
	private int FloodFillMainContinent(int startIdx, int _totalCells, byte[] _landMaskMap)
	{
		int tilesCount = 0;
		int head = 0;
		int tail = 0;

		int[] bfsQueue = ArrayPool<int>.Shared.Rent(_totalCells);
		bfsQueue[tail++] = startIdx;
		_playableMap[startIdx] = 1;

		Direction[] localDirs = new Direction[]
		{
			new Direction(1, true, _width - 1),
			new Direction(-1, true, 0),
			new Direction(_width, false, 0),
			new Direction(-_width, false, 0),
		};

		while (head < tail)
		{
			int idx = bfsQueue[head++];
			tilesCount++;
			int cx = idx % _width;

			foreach (var dir in localDirs)
			{
				if (dir.CheckBounds && cx == dir.XLimit)
					continue;

				int nIdx = idx + dir.Offset;

				if (nIdx < 0 || nIdx >= _totalCells)
					continue;

				if (_landMaskMap[nIdx] > 0 && _playableMap[nIdx] == 0)
				{
					_playableMap[nIdx] = 1;
					bfsQueue[tail++] = nIdx;
				}
			}
		}

		ArrayPool<int>.Shared.Return(bfsQueue);
		return tilesCount;
	}

	[System.Runtime.CompilerServices.MethodImpl(
		System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
	)]
	private bool IsInBounds(int x, int y)
	{
		return x >= 0 && x < _width && y >= 0 && y < _height;
	}
}
