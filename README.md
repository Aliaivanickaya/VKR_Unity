# Geo Pipeline — Unity Terrain Generator

Node.js приложение для автоматической подготовки данных рельефа и текстур
для симуляции местности в Unity.

## Что делает

1. Открываешь браузер на `localhost:3000`
2. Рисуешь прямоугольник на карте
3. Жмёшь «Сгенерировать»
4. Получаешь готовые файлы для Unity:
   - `heightmap.tif` — оригинальный GeoTIFF (для GeoTiffReader.cs)
   - `height.raw` — 16-bit карта высот (для HeightmapNormalizer.cs)
   - `texture.png` — спутниковая текстура 513×513 (Esri World Imagery)

## Установка

```bash
cd geo-pipeline
npm install
```

## Настройка

Скопируй `.env.example` в `.env` и заполни:

```bash
cp .env.example .env
```

```env
OPENTOPO_API_KEY=твой_ключ_здесь

# Опционально — автокопирование в Unity:
UNITY_ASSETS_PATH=/Users/name/UnityProjects/Terrain/Assets
```

**Получить API ключ OpenTopography:**
https://opentopography.org → My Account → Request API Key (бесплатно)

## Запуск

```bash
npm start
# или для авто-перезапуска при изменениях:
npm run dev
```

Открой браузер: http://localhost:3000

## Источники данных

- **Рельеф:** Copernicus GLO-30 через OpenTopography API (30 м/пиксель)
- **Текстура:** Esri World Imagery tiles (публичные, без ключа, ~1-2 м/пиксель)

## Ограничения

- Максимальный размер участка: 1°×1° (~100×100 км)
- Разрешения карт: 257, 513, 1025, 2049 (требование Unity: 2^n + 1)

## Совместимость с Unity скриптами

Файлы готовы для прямого использования с:
- `TerrainLoader.cs` (v2.2+)
- `HeightmapNormalizer.cs`
- `GeoTiffReader.cs`

Положи файлы в `Assets/` Unity проекта и нажми Play.
