// Assets/Scripts/TerrainLoader.cs
// Версия 3.2

using UnityEngine;
using UnityEngine.Rendering;
using System.IO;

public class TerrainLoader : MonoBehaviour
{
    [Header("Исходные файлы")]
    public string heightRawPath = "Assets/height.raw";
    public string heightTifPath = "Assets/heightmap.tif";
    public string texturePath   = "Assets/texture.png";

    [Header("Разрешение карты высот")]
    public int heightmapResolution = 513;

    [Header("Размер террейна (метры)")]
    public float terrainWidth  = 1000f;
    public float terrainLength = 1000f;

    [Header("Вертикальный масштаб")]
    public float verticalExaggeration = 0.3f;
    public float fallbackMaxHeight    = 400f;

    [Header("Освещение")]
    [Tooltip("0 = чёрные тени, 1 = светлые тени")]
    [Range(0f, 1f)]
    public float shadowBrightness = 0.05f;

    void Start()
    {
        GenerateTerrain();
        SetupLighting();
        SetupCamera();
    }

    void GenerateTerrain()
    {
        if (!File.Exists(heightRawPath))
        {
            Debug.LogError($"[TerrainLoader] RAW не найден: {heightRawPath}");
            return;
        }

        byte[] raw = File.ReadAllBytes(heightRawPath);

        ushort rawMin, rawMax;
        float[,] heights = HeightmapNormalizer.Normalize(raw, heightmapResolution,
                                                         out rawMin, out rawMax);

        float minH, maxH;
        bool tifOk = GeoTiffReader.GetHeightRange(heightTifPath, out minH, out maxH);
        float terrainHeight = tifOk
            ? (maxH - minH) * verticalExaggeration
            : fallbackMaxHeight * verticalExaggeration;

        if (tifOk)
            Debug.Log($"[TerrainLoader] Высоты: {minH:F1}–{maxH:F1} м, диапазон={maxH-minH:F1} м");

        TerrainData td = new TerrainData();
        td.heightmapResolution = heightmapResolution;
        td.size = new Vector3(terrainWidth, terrainHeight, terrainLength);
        td.SetHeights(0, 0, heights);

        GameObject terrainObj = Terrain.CreateTerrainGameObject(td);
        terrainObj.name = "GeneratedTerrain";

        if (!File.Exists(texturePath))
        {
            Debug.LogWarning($"[TerrainLoader] Текстура не найдена: {texturePath}");
            return;
        }

        Texture2D tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(texturePath));

        TerrainLayer layer = new TerrainLayer();
        layer.diffuseTexture = tex;
        layer.tileSize       = new Vector2(terrainWidth, terrainLength);
        td.terrainLayers     = new TerrainLayer[] { layer };

        Terrain terrain = terrainObj.GetComponent<Terrain>();
        terrain.reflectionProbeUsage = ReflectionProbeUsage.Off;

        Shader unlitShader = Shader.Find("Unlit/Texture");
        if (unlitShader != null)
        {
            Material tm = new Material(unlitShader);
            tm.mainTexture = tex;
            terrain.materialTemplate = tm;
        }

        Debug.Log($"[TerrainLoader] Террейн: {terrainWidth}×{terrainHeight:F1}×{terrainLength} м");
    }

    void SetupLighting()
    {
        // Солнце фиксировано — сбоку под 45°, без настроек в Inspector
        GameObject sunObj = GameObject.Find("Directional Light");
        if (sunObj == null)
        {
            sunObj = new GameObject("Directional Light");
            sunObj.AddComponent<Light>().type = LightType.Directional;
        }
        Light sun     = sunObj.GetComponent<Light>();
        sun.type      = LightType.Directional;
        sun.intensity = 1.0f;
        sun.color     = new Color(1.0f, 0.97f, 0.90f);
        sun.shadows   = LightShadows.Soft;
        sunObj.transform.rotation = Quaternion.Euler(45f, 160f, 0f);

        // Яркость теней регулируется через shadowBrightness
        RenderSettings.ambientMode  = AmbientMode.Flat;
        // При Unlit шейдере ambient не влияет на террейн — он просто показывает текстуру
        // Оставляем для освещения других объектов (БЛА)
        RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.65f);

        RenderSettings.fog    = false;
        RenderSettings.skybox = null;

        if (Camera.main != null)
        {
            Camera.main.backgroundColor = new Color(0.40f, 0.55f, 0.75f);
            Camera.main.clearFlags      = CameraClearFlags.SolidColor;
        }

        // Отключаем отражения окружения — источник блеска на рельефе
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.reflectionIntensity = 0f;

        DynamicGI.UpdateEnvironment();
        Debug.Log($"[TerrainLoader] Яркость теней: {shadowBrightness}");
    }

    void SetupCamera()
    {
        if (Camera.main == null) return;

        float cx = terrainWidth  * 0.5f;
        float cz = terrainLength * 0.5f;
        float dist   = terrainWidth * 0.7f;
        float height = terrainWidth * 0.35f;

        Camera.main.transform.position = new Vector3(cx - dist * 0.5f, height, cz - dist);
        Camera.main.transform.LookAt(new Vector3(cx, height * 0.1f, cz));
    }
}
