// Assets/Scripts/HeightmapNormalizer.cs
// v2.1 — нормализация относительно реального min/max RAW-значений.
// Не интерпретирует ushort как метры — только нормализует в [0..1].
// Реальный вертикальный размер задаётся снаружи через maxHeightMeters в TerrainLoader.

public static class HeightmapNormalizer
{
    /// <summary>
    /// Нормализует 16-битный RAW-массив в диапазон [0..1] относительно
    /// реального min/max внутри файла.
    /// rawMin и rawMax — ushort-значения (0–65535), НЕ метры напрямую.
    /// </summary>
    public static float[,] Normalize(byte[] raw, int resolution,
                                     out ushort rawMin, out ushort rawMax)
    {
        rawMin = ushort.MaxValue;
        rawMax = ushort.MinValue;

        // Проход 1: найти min/max
        for (int i = 0; i < resolution * resolution; i++)
        {
            int idx = i * 2;
            if (idx + 1 >= raw.Length) continue;
            ushort val = (ushort)((raw[idx + 1] << 8) | raw[idx]);
            if (val < rawMin) rawMin = val;
            if (val > rawMax) rawMax = val;
        }

        float range = rawMax - rawMin;
        if (range < 1f) range = 1f;

        float[,] heights = new float[resolution, resolution];

        // Проход 2: нормализация
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int idx = (y * resolution + x) * 2;
                if (idx + 1 >= raw.Length) continue;
                ushort val = (ushort)((raw[idx + 1] << 8) | raw[idx]);
                heights[y, x] = (val - rawMin) / range;
            }
        }

        return heights;
    }
}