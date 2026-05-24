// Assets/Scripts/NormalMapGenerator.cs
// Генерирует карту нормалей из карты высот методом Sobel-фильтра.
// Нормали передаются в TerrainLayer — это позволяет спутниковой текстуре
// корректно реагировать на освещение (склоны светлее/темнее в зависимости от солнца).

using UnityEngine;

public static class NormalMapGenerator
{
    /// <summary>
    /// Генерирует Normal Map из массива высот.
    /// strength — интенсивность нормалей (1.0 = нейтрально, 2-4 = выраженный рельеф).
    /// </summary>
    public static Texture2D Generate(float[,] heights, int resolution, float strength = 2.0f)
    {
        Texture2D normalMap = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true);
        normalMap.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[resolution * resolution];

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                // Sobel-фильтр: считываем соседние высоты
                float left   = SampleHeight(heights, x - 1, y,     resolution);
                float right  = SampleHeight(heights, x + 1, y,     resolution);
                float down   = SampleHeight(heights, x,     y - 1, resolution);
                float up     = SampleHeight(heights, x,     y + 1, resolution);

                // Градиент по X и Y
                float dx = (right - left) * strength;
                float dy = (up    - down) * strength;

                // Нормаль из градиента
                Vector3 normal = new Vector3(-dx, -dy, 1.0f).normalized;

                // Упаковываем в цвет (диапазон [-1..1] → [0..1])
                pixels[y * resolution + x] = new Color(
                    normal.x * 0.5f + 0.5f,
                    normal.y * 0.5f + 0.5f,
                    normal.z * 0.5f + 0.5f,
                    1.0f
                );
            }
        }

        normalMap.SetPixels(pixels);
        normalMap.Apply();
        return normalMap;
    }

    private static float SampleHeight(float[,] heights, int x, int y, int res)
    {
        x = Mathf.Clamp(x, 0, res - 1);
        y = Mathf.Clamp(y, 0, res - 1);
        return heights[y, x];
    }
}
