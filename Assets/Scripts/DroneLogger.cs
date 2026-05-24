// Assets/Scripts/DroneLogger.cs
// Записывает телеметрию полёта БЛА в CSV-файл.
// Подключается к тому же объекту что и DroneWaypointFlyer.
//
// Формат CSV:
// time_s, pos_x, pos_y, pos_z, altitude_above_terrain_m, speed_ms, waypoint

using UnityEngine;
using System.IO;
using System.Text;

public class DroneLogger : MonoBehaviour
{
    [Header("Настройки лога")]
    [Tooltip("Имя файла. Сохраняется рядом с проектом Unity (Application.dataPath/../).")]
    public string fileName = "drone_flight_log.csv";

    [Tooltip("Интервал записи в секундах. 0.1 = 10 записей в секунду.")]
    public float logInterval = 0.1f;

    private DroneWaypointFlyer flyer;
    private StreamWriter writer;
    private float timer = 0f;
    private float missionTime = 0f;
    private string filePath;

    void Start()
    {
        flyer = GetComponent<DroneWaypointFlyer>();
        if (flyer == null)
        {
            Debug.LogError("[DroneLogger] DroneWaypointFlyer не найден на объекте!");
            enabled = false;
            return;
        }

        // Сохраняем рядом с папкой Assets
        filePath = Path.Combine(Application.dataPath, "..", fileName);
        writer = new StreamWriter(filePath, false, Encoding.UTF8);

        // Заголовок CSV
        writer.WriteLine("time_s,pos_x,pos_y,pos_z,altitude_above_terrain_m,speed_ms,waypoint_index");
        writer.Flush();

        Debug.Log($"[DroneLogger] Лог: {filePath}");
    }

    void Update()
    {
        missionTime += Time.deltaTime;
        timer       += Time.deltaTime;

        if (timer >= logInterval)
        {
            timer = 0f;
            WriteRow();
        }
    }

    void WriteRow()
    {
        Vector3 pos = transform.position;

        string line = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0:F2},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2},{6}",
            missionTime,
            pos.x,
            pos.y,
            pos.z,
            flyer.currentAltitudeAboveTerrain,
            flyer.currentSpeed,
            flyer.currentWaypoint
        );

        writer.WriteLine(line);
        writer.Flush();
    }

    void OnDestroy()
    {
        if (writer != null)
        {
            writer.Close();
            Debug.Log($"[DroneLogger] Лог сохранён: {filePath}");
        }
    }

    // При остановке Play тоже закрываем файл
    void OnApplicationQuit()
    {
        if (writer != null) writer.Close();
    }
}
