using UnityEngine;
using UnityEditor;
using DT.GPS;
using DT.UI;
using DT.Facility;
using DT.Entities;

public class SceneSetup
{
    [MenuItem("Digital Twin/Setup GPS Scene")]
    public static void SetupScene()
    {
        // --- GPS Manager ---
        var gpsManager = new GameObject("GPSManager");
        var mockGPS = gpsManager.AddComponent<MockGPSProvider>();
        var webGLGPS = gpsManager.AddComponent<WebGLGPSProvider>();
        var calibrator = gpsManager.AddComponent<GPSCalibrator>();
        var tracker = gpsManager.AddComponent<PlayerPositionTracker>();
        var debugUI = gpsManager.AddComponent<GPSDebugUI>();

        // --- Facility ---
        var builder = gpsManager.AddComponent<FacilityBuilder>();
        builder.Build();
        Object.DestroyImmediate(builder);
        gpsManager.AddComponent<FacilityLabels>();

        // --- Terrain mesh (point cloud) ---
        // Assets/PointCloud/ に実地形メッシュがあれば配置しプレースホルダを隠す
        var facilityRoot = GameObject.Find("Facility");
        TerrainMeshLoader.LoadIfPresent(facilityRoot);

        // --- Player Marker ---
        var marker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        marker.name = "PlayerMarker";
        marker.transform.position = new Vector3(250, 1, -175);
        var markerRenderer = marker.GetComponent<Renderer>();
        var markerMat = new Material(Shader.Find("Standard"));
        markerMat.color = Color.blue;
        markerRenderer.material = markerMat;

        // --- Camera (overhead view) ---
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(250, 200, -175);
            cam.transform.rotation = Quaternion.Euler(70, 0, 0);
            cam.farClipPlane = 1000;
        }

        // --- Wire up references ---
        var debugSO = new SerializedObject(debugUI);
        debugSO.FindProperty("mockGPSProvider").objectReferenceValue = mockGPS;
        debugSO.FindProperty("webGLGPSProvider").objectReferenceValue = webGLGPS;
        debugSO.FindProperty("calibrator").objectReferenceValue = calibrator;
        debugSO.FindProperty("playerMarker").objectReferenceValue = marker.transform;
        debugSO.ApplyModifiedProperties();

        var trackerSO = new SerializedObject(tracker);
        trackerSO.FindProperty("mockGPSProvider").objectReferenceValue = mockGPS;
        trackerSO.FindProperty("webGLGPSProvider").objectReferenceValue = webGLGPS;
        trackerSO.FindProperty("calibrator").objectReferenceValue = calibrator;
        trackerSO.FindProperty("playerMarker").objectReferenceValue = marker.transform;
        trackerSO.ApplyModifiedProperties();

        // --- Calibration Sync (landmark-based georeference from smartphone GPS) ---
        var calSync = gpsManager.AddComponent<CalibrationSync>();
        var calSyncSO = new SerializedObject(calSync);
        calSyncSO.FindProperty("calibrator").objectReferenceValue = calibrator;
        calSyncSO.ApplyModifiedProperties();

        // --- Entity Manager (multi-entity real-time tracking) ---
        var entityRoot = new GameObject("EntityManager");
        var entityMgr = entityRoot.AddComponent<EntityManager>();
        entityRoot.AddComponent<EntityLabelRenderer>();
        entityRoot.AddComponent<SceneAnalysisRenderer>();
        var entitySO = new SerializedObject(entityMgr);
        entitySO.FindProperty("calibrator").objectReferenceValue = calibrator;
        entitySO.ApplyModifiedProperties();

        // --- EventSystem ---
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        Debug.Log("Digital Twin GPS Scene with facility setup complete!");
    }
}
