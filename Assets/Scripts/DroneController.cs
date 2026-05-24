// Assets/Scripts/DroneController.cs
// Всё в одном: создаёт БЛА, летит по маршруту повторяя рельеф, пишет CSV.
// Точки маршрута задаются координатами XZ прямо в Inspector.
// Высота над землёй рассчитывается автоматически через Terrain.SampleHeight.

using UnityEngine;
using System.IO;
using System.Text;

public class DroneController : MonoBehaviour
{
    [Header("Точки маршрута (только X и Z, Y игнорируется)")]
    [Tooltip("Задай координаты XZ точек маршрута. Смотри на размер террейна в консоли и ставь точки внутри него.")]
    public Vector3[] waypointPositions = new Vector3[]
    {
        new Vector3(100, 0, 100),
        new Vector3(300, 0, 100),
        new Vector3(300, 0, 300),
        new Vector3(500, 0, 300),
        new Vector3(500, 0, 500),
    };

    [Header("Параметры полёта")]
    [Tooltip("Высота над рельефом в метрах (единицы Unity).")]
    public float altitudeAboveTerrain = 50f;

    [Tooltip("Скорость полёта в единицах Unity в секунду.")]
    public float speed = 30f;

    [Tooltip("Расстояние до точки при котором считается что она достигнута.")]
    public float waypointRadius = 8f;

    [Header("Лог")]
    [Tooltip("Интервал записи в секундах.")]
    public float logInterval = 0.2f;

    // ── Внутреннее состояние ──────────────────────────────────────────────
    private GameObject droneObj;
    private int currentWaypoint = 0;
    private bool finished = false;
    private StreamWriter writer;
    private float logTimer = 0f;
    private float missionTime = 0f;
    private Vector3 prevPos;
    private Terrain terrain;

    void Start()
    {
        // Ждём один кадр чтобы TerrainLoader успел создать террейн
        Invoke("Init", 0.5f);
    }

    void Init()
    {
        terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            Debug.LogError("[DroneController] Террейн не найден");
            enabled = false;
            return;
        }

        // Создаём модель БЛА из примитивов
        droneObj = new GameObject("Drone");

        // Материалы
        Material bodyMat = new Material(Shader.Find("Standard"));
        bodyMat.color = new Color(0.15f, 0.15f, 0.15f); // тёмно-серый корпус

        Material armMat = new Material(Shader.Find("Standard"));
        armMat.color = new Color(0.25f, 0.25f, 0.25f); // чуть светлее лучи

        Material propMat = new Material(Shader.Find("Standard"));
        propMat.color = new Color(0.08f, 0.08f, 0.08f); // тёмные винты
        propMat.SetFloat("_Glossiness", 0.6f);

        // Корпус — приплюснутый куб
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(droneObj.transform);
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = new Vector3(12f, 2.5f, 12f);
        body.GetComponent<Renderer>().material = bodyMat;

        // Купол сверху — сплюснутая сфера
        GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "Dome";
        dome.transform.SetParent(droneObj.transform);
        dome.transform.localPosition = new Vector3(0, 2f, 0);
        dome.transform.localScale = new Vector3(5f, 3f, 5f);
        dome.GetComponent<Renderer>().material = bodyMat;

        // 4 луча 
        Vector3[] armDirs = {
            new Vector3(1, 0, 1), new Vector3(-1, 0, 1),
            new Vector3(1, 0, -1), new Vector3(-1, 0, -1)
        };
        float armLen = 14f;
        float propDist = 13f;

        for (int i = 0; i < 4; i++)
        {
            // Луч
            GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arm.name = $"Arm_{i}";
            arm.transform.SetParent(droneObj.transform);
            arm.transform.localPosition = armDirs[i].normalized * (armLen * 0.5f);
            arm.transform.localRotation = Quaternion.LookRotation(armDirs[i]);
            arm.transform.localScale = new Vector3(1.5f, 1.5f, armLen);
            arm.GetComponent<Renderer>().material = armMat;

            // Мотор на конце луча
            GameObject motor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            motor.name = $"Motor_{i}";
            motor.transform.SetParent(droneObj.transform);
            motor.transform.localPosition = armDirs[i].normalized * propDist;
            motor.transform.localScale = new Vector3(2.5f, 1.5f, 2.5f);
            motor.GetComponent<Renderer>().material = bodyMat;

            // Винт — плоский цилиндр
            GameObject prop = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            prop.name = $"Prop_{i}";
            prop.transform.SetParent(droneObj.transform);
            prop.transform.localPosition = armDirs[i].normalized * propDist + new Vector3(0, 2f, 0);
            prop.transform.localScale = new Vector3(6f, 0.3f, 6f);
            prop.GetComponent<Renderer>().material = propMat;
        }
        foreach (var col in droneObj.GetComponentsInChildren<Collider>())
            col.enabled = false;

        // Ставим на первую точку
        Vector3 start = waypointPositions[0];
        float startY = GetTerrainY(start) + altitudeAboveTerrain;
        droneObj.transform.position = new Vector3(start.x, startY, start.z);
        prevPos = droneObj.transform.position;

        // Открываем CSV
        string path = Path.Combine(Application.dataPath, "..", "drone_log.csv");
        writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine("time_s,pos_x,pos_y,pos_z,altitude_above_terrain_m,speed_ms,waypoint");
        Debug.Log($"[DroneController] Лог: {path}");
        Debug.Log($"[DroneController] Террейн: {terrain.terrainData.size}, точек: {waypointPositions.Length}");
    }

    void Update()
    {
        if (finished || droneObj == null) return;

        missionTime += Time.deltaTime;
        logTimer    += Time.deltaTime;

        Vector3 wp = waypointPositions[currentWaypoint];

        // Смотрим высоту рельефа в нескольких точках впереди по курсу
        Vector3 curPos = droneObj.transform.position;
        Vector3 lookDir = new Vector3(wp.x - curPos.x, 0, wp.z - curPos.z).normalized;
        float lookAheadDist = speed * 2f; // смотрим на 2 секунды вперёд
        float maxTerrainY = GetTerrainY(curPos); // под собой
        for (int step = 1; step <= 5; step++)
        {
            Vector3 probe = curPos + lookDir * (lookAheadDist * step / 5f);
            float h = GetTerrainY(probe);
            if (h > maxTerrainY) maxTerrainY = h;
        }

        float targetY = maxTerrainY + altitudeAboveTerrain;

        // Плавно поднимаемся если впереди гора, опускаемся если долина
        float currentY = Mathf.Lerp(curPos.y, targetY, Time.deltaTime * 3f);

        // Целевая позиция
        Vector3 target = new Vector3(wp.x, targetY, wp.z);
        Vector3 nextPos = new Vector3(
            Mathf.MoveTowards(curPos.x, wp.x, speed * Time.deltaTime),
            currentY,
            Mathf.MoveTowards(curPos.z, wp.z, speed * Time.deltaTime));

        // Движение
        droneObj.transform.position = nextPos;

        // Поворот
        Vector3 dir = target - droneObj.transform.position;
        if (dir.sqrMagnitude > 0.1f)
            droneObj.transform.rotation = Quaternion.LookRotation(dir);

        // Фактическая высота над рельефом
        float actualAlt = droneObj.transform.position.y - GetTerrainY(droneObj.transform.position);

        // Скорость
    float spd = Vector3.Distance(droneObj.transform.position, prevPos) / logTimer;
    if (logTimer >= logInterval) prevPos = droneObj.transform.position;

        // Запись в лог
        if (logTimer >= logInterval)
        {
            logTimer = 0f;
            Vector3 p = droneObj.transform.position;
            writer.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:F2},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2},{6}",
                missionTime, p.x, p.y, p.z, actualAlt, spd, currentWaypoint));
            writer.Flush();
        }

        // Достигли точки?
        float distXZ = Vector2.Distance(
            new Vector2(droneObj.transform.position.x, droneObj.transform.position.z),
            new Vector2(wp.x, wp.z));

        if (distXZ < waypointRadius)
        {
            Debug.Log($"[DroneController] Точка {currentWaypoint} достигнута");
            currentWaypoint++;
            if (currentWaypoint >= waypointPositions.Length)
            {
                finished = true;
                writer.Close();
                Debug.Log("[DroneController] Маршрут завершён. Лог сохранён.");
            }
        }
    }

    float GetTerrainY(Vector3 pos)
    {
        if (terrain == null) return 0f;
        return terrain.SampleHeight(pos) + terrain.transform.position.y;
    }

    void OnApplicationQuit()
    {
        if (writer != null) writer.Close();
    }

    // Рисуем маршрут в Scene view
    void OnDrawGizmos()
    {
        if (waypointPositions == null) return;
        Gizmos.color = Color.yellow;
        for (int i = 0; i < waypointPositions.Length; i++)
        {
            Gizmos.DrawSphere(waypointPositions[i], 5f);
            if (i < waypointPositions.Length - 1)
                Gizmos.DrawLine(waypointPositions[i], waypointPositions[i + 1]);
        }
    }
}
