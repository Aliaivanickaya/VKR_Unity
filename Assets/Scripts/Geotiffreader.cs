// Assets/Scripts/GeoTiffReader.cs
// Минимальный парсер GeoTIFF для извлечения диапазона высот (min/max в метрах).
// Работает без GDAL и внешних библиотек — только System.IO.
//
// Поддерживает форматы пикселей GLO-30:
//   - Float32 (основной формат Copernicus GLO-30)
//   - Int16 / UInt16 (SRTM и некоторые другие DEM)
//
// Использование:
//   float minH, maxH;
//   GeoTiffReader.GetHeightRange("Assets/heightmap.tif", out minH, out maxH);
//   // minH, maxH — реальные метры над уровнем моря

using System;
using System.IO;
using UnityEngine;

public static class GeoTiffReader
{
    // TIFF-теги, которые нас интересуют
    private const ushort TAG_IMAGE_WIDTH        = 256;
    private const ushort TAG_IMAGE_HEIGHT       = 257;
    private const ushort TAG_BITS_PER_SAMPLE    = 258;
    private const ushort TAG_STRIP_OFFSETS      = 273;
    private const ushort TAG_SAMPLE_FORMAT      = 339; // 1=UInt, 2=Int, 3=Float
    private const ushort TAG_STRIP_BYTE_COUNTS  = 279;

    /// <summary>
    /// Читает GeoTIFF и возвращает реальный диапазон высот в метрах.
    /// </summary>
    /// <returns>true если файл успешно прочитан</returns>
    public static bool GetHeightRange(string tifPath, out float minHeight, out float maxHeight)
    {
        minHeight = 0f;
        maxHeight = 600f; // fallback

        if (!File.Exists(tifPath))
        {
            Debug.LogWarning($"[GeoTiffReader] Файл не найден: {tifPath}");
            return false;
        }

        try
        {
            byte[] data = File.ReadAllBytes(tifPath);

            // --- Определяем порядок байт (endianness) ---
            // "II" (0x4949) = little-endian, "MM" (0x4D4D) = big-endian
            bool littleEndian = (data[0] == 0x49);

            // --- Читаем смещение первого IFD ---
            uint ifdOffset = ReadUInt32(data, 4, littleEndian);

            // --- Парсим IFD (Image File Directory) ---
            ushort entryCount = ReadUInt16(data, (int)ifdOffset, littleEndian);

            int width = 0, height = 0, bitsPerSample = 32;
            int sampleFormat = 3; // по умолчанию Float (GLO-30)
            long stripOffset = 0;

            int baseOffset = (int)ifdOffset + 2;

            for (int i = 0; i < entryCount; i++)
            {
                int entryPos = baseOffset + i * 12;
                ushort tag  = ReadUInt16(data, entryPos,     littleEndian);
                ushort type = ReadUInt16(data, entryPos + 2, littleEndian);
                uint   count = ReadUInt32(data, entryPos + 4, littleEndian);
                int    valueOffset = entryPos + 8;

                switch (tag)
                {
                    case TAG_IMAGE_WIDTH:
                        width = (int)ReadValue(data, valueOffset, type, littleEndian);
                        break;
                    case TAG_IMAGE_HEIGHT:
                        height = (int)ReadValue(data, valueOffset, type, littleEndian);
                        break;
                    case TAG_BITS_PER_SAMPLE:
                        bitsPerSample = (int)ReadValue(data, valueOffset, type, littleEndian);
                        break;
                    case TAG_SAMPLE_FORMAT:
                        sampleFormat = (int)ReadValue(data, valueOffset, type, littleEndian);
                        break;
                    case TAG_STRIP_OFFSETS:
                        // Если стрип один — значение прямо в поле, иначе — указатель
                        if (count == 1)
                            stripOffset = (long)ReadValue(data, valueOffset, type, littleEndian);
                        else
                            stripOffset = (long)ReadUInt32(data, valueOffset, littleEndian);
                        break;
                }
            }

            if (width == 0 || height == 0 || stripOffset == 0)
            {
                Debug.LogWarning("[GeoTiffReader] Не удалось прочитать метаданные TIFF.");
                return false;
            }

            // --- Сканируем пиксели для поиска min/max ---
            float min = float.MaxValue;
            float max = float.MinValue;
            int bytesPerSample = bitsPerSample / 8;
            long pos = stripOffset;

            for (int p = 0; p < width * height; p++)
            {
                float val = ReadSample(data, pos, sampleFormat, bytesPerSample, littleEndian);
                pos += bytesPerSample;

                // Игнорируем NoData-значения (GLO-30 использует -32768 или очень большие числа)
                if (val < -10000f || val > 10000f) continue;

                if (val < min) min = val;
                if (val > max) max = val;
            }

            if (min == float.MaxValue)
            {
                Debug.LogWarning("[GeoTiffReader] Не найдено валидных значений высот.");
                return false;
            }

            minHeight = min;
            maxHeight = max;

            Debug.Log($"[GeoTiffReader] Диапазон высот: {min:F1}–{max:F1} м " +
                      $"(размер {width}×{height}, {bitsPerSample}bit, format={sampleFormat})");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[GeoTiffReader] Ошибка чтения: {e.Message}");
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Вспомогательные методы чтения байтов
    // -----------------------------------------------------------------------

    private static float ReadSample(byte[] data, long pos, int sampleFormat,
                                    int bytesPerSample, bool le)
    {
        int p = (int)pos;
        switch (sampleFormat)
        {
            case 3: // IEEE Float
                return le
                    ? BitConverter.ToSingle(new[] { data[p], data[p+1], data[p+2], data[p+3] }, 0)
                    : BitConverter.ToSingle(new[] { data[p+3], data[p+2], data[p+1], data[p] }, 0);
            case 2: // Int16
                short s = le
                    ? (short)((data[p+1] << 8) | data[p])
                    : (short)((data[p]   << 8) | data[p+1]);
                return s;
            case 1: // UInt16
            default:
                ushort u = le
                    ? (ushort)((data[p+1] << 8) | data[p])
                    : (ushort)((data[p]   << 8) | data[p+1]);
                return u;
        }
    }

    private static uint ReadUInt32(byte[] data, int offset, bool le)
    {
        if (le)
            return (uint)(data[offset] | (data[offset+1] << 8) |
                          (data[offset+2] << 16) | (data[offset+3] << 24));
        return (uint)((data[offset] << 24) | (data[offset+1] << 16) |
                       (data[offset+2] << 8) | data[offset+3]);
    }

    private static ushort ReadUInt16(byte[] data, int offset, bool le)
    {
        return le
            ? (ushort)(data[offset] | (data[offset + 1] << 8))
            : (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static long ReadValue(byte[] data, int offset, ushort type, bool le)
    {
        switch (type)
        {
            case 3: return ReadUInt16(data, offset, le); // SHORT
            case 4: return ReadUInt32(data, offset, le); // LONG
            default: return ReadUInt16(data, offset, le);
        }
    }
}