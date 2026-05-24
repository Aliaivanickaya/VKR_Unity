// Assets/Scripts/DroneWaypointFlyer.cs
// БЛА летит по точкам маршрута, удерживая постоянную высоту над рельефом.
// Высота над землёй определяется через Physics.Raycast вниз.

using UnityEngine;

public class DroneWaypointFlyer : MonoBehaviour
{
    [Header("Маршрут")]
    [Tooltip("Список точек маршрута. Координаты XZ берутся из точек, высота рассчитывается автоматически.")]
    public Transform[] waypoints;

    [Header("Параметры полёта")]
    [Tooltip("Скорость полёта, м/с (в единицах Unity).")]
    public float speed = 20f;

    [Tooltip("Высота над рельефом, м (в единицах Unity).")]
    public float altitudeAboveTerrain = 50f;

    [Tooltip("Расстояние до точки маршрута при котором считается что точка достигнута.")]
    public float waypointRadius = 5f;

    [Tooltip("Слой террейна для Raycast. Оставьте 0 чтобы использовать все слои.")]
    public LayerMask terrainLayer;

    [Header("Состояние (только чтение)")]
    public int currentWaypoint = 0;
    public float currentAltitudeAboveTerrain = 0f;
    public float currentSpeed = 0f;

    private Vector3 prevPosition;
    private bool finished = false;

    void Start()
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            Debug.LogError("[DroneWaypointFlyer] Точки маршрута не заданы!");
            enabled = false;
            return;
        }

        // Ставим БЛА на старт с правильной высотой
        Vector3 startPos = waypoints[0].position;
        transform.position = new Vector3(startPos.x, GetTerrainHeight(startPos) + altitudeAboveTerrain, startPos.z);
        prevPosition = transform.position;

        Debug.Log($"[DroneWaypointFlyer] Старт. Точек маршрута: {waypoints.Length}, высота: {altitudeAboveTerrain} м");
    }

    void Update()
    {
        if (finished) return;

        Transform target = waypoints[currentWaypoint];

        // Целевая позиция: XZ из точки маршрута, Y = рельеф + высота
        float terrainY = GetTerrainHeight(target.position);
        Vector3 targetPos = new Vector3(target.position.x, terrainY + altitudeAboveTerrain, target.position.z);

        // Текущая позиция тоже удерживает высоту над рельефом
        float currentTerrainY = GetTerrainHeight(transform.position);
        float desiredY = currentTerrainY + altitudeAboveTerrain;

        // Плавно корректируем высоту и двигаемся к цели
        Vector3 correctedTarget = new Vector3(targetPos.x, desiredY, targetPos.z);
        transform.position = Vector3.MoveTowards(transform.position, correctedTarget, speed * Time.deltaTime);

        // Разворачиваем нос к цели
        Vector3 dir = (correctedTarget - transform.position);
        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(dir);

        // Замеряем фактическую высоту над рельефом
        currentAltitudeAboveTerrain = transform.position.y - GetTerrainHeight(transform.position);

        // Скорость (м/с)
        currentSpeed = Vector3.Distance(transform.position, prevPosition) / Time.deltaTime;
        prevPosition = transform.position;

        // Проверяем достижение точки по XZ
        float distXZ = Vector2.Distance(
            new Vector2(transform.position.x, transform.position.z),
            new Vector2(targetPos.x, targetPos.z));

        if (distXZ < waypointRadius)
        {
            Debug.Log($"[DroneWaypointFlyer] Достигнута точка {currentWaypoint}");
            currentWaypoint++;
            if (currentWaypoint >= waypoints.Length)
            {
                finished = true;
                Debug.Log("[DroneWaypointFlyer] Маршрут завершён.");
            }
        }
    }

    /// <summary>
    /// Возвращает высоту рельефа под заданной точкой через Raycast.
    /// </summary>
    float GetTerrainHeight(Vector3 pos)
    {
        Ray ray = new Ray(new Vector3(pos.x, 5000f, pos.z), Vector3.down);
        RaycastHit hit;
        int mask = terrainLayer == 0 ? ~0 : (int)terrainLayer;
        if (Physics.Raycast(ray, out hit, 10000f, mask))
            return hit.point.y;
        // Запасной вариант через Terrain API
        Terrain t = Terrain.activeTerrain;
        if (t != null) return t.SampleHeight(pos) + t.transform.position.y;
        return 0f;
    }

    // Рисуем маршрут в редакторе
    void OnDrawGizmos()
    {
        if (waypoints == null) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            Gizmos.DrawSphere(waypoints[i].position, 3f);
            if (i < waypoints.Length - 1 && waypoints[i+1] != null)
                Gizmos.DrawLine(waypoints[i].position, waypoints[i+1].position);
        }
    }
}
