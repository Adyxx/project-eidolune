using Godot;
using System;

public partial class climate_generator : RefCounted 
{
	public Godot.Collections.Dictionary RunGeneration(
		int width, int height, float seaLevel, float continentFalloff, float warpStrength,
		FastNoiseLite nHeight, FastNoiseLite nTemp, FastNoiseLite nMoist, FastNoiseLite nWarpX, FastNoiseLite nWarpY
	)
	{
		int totalCells = width * height;
		float centerX = width / 2.0f;
		float centerY = height / 2.0f;

		float[] heightMap = new float[totalCells];
		float[] temperatureMap = new float[totalCells];
		float[] moistureMap = new float[totalCells];
		byte[] landMaskMap = new byte[totalCells];

		System.Threading.Tasks.Parallel.For(0, height, y =>
		{
			float floatY = (float)y;
			float latitude = floatY / height;
			int rowOffset = y * width;

			float dy = MathF.Abs(floatY - centerY) / centerY;
			float oneMinusDySq = 1.0f - (dy * dy);

			for (int x = 0; x < width; x++)
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
				
				float hVal = Mathf.Clamp(baseHeight - (distMask * continentFalloff), 0.0f, 1.0f);
				heightMap[idx] = hVal;
				landMaskMap[idx] = (byte)(hVal >= seaLevel ? 1 : 0);

				float coldFromHeight = hVal * 0.4f;
				float noiseT = nTemp.GetNoise2D(floatX, floatY) * 0.2f;
				temperatureMap[idx] = Mathf.Clamp(1.0f - latitude - coldFromHeight + noiseT, 0.0f, 1.0f);

				moistureMap[idx] = (nMoist.GetNoise2D(sampleX, sampleY) + 1.0f) * 0.5f;
			}
		});

		var result = new Godot.Collections.Dictionary();
		result["height_map"] = heightMap;
		result["temperature_map"] = temperatureMap;
		result["moisture_map"] = moistureMap;
		result["land_mask_map"] = landMaskMap;
		return result;
	}
}
