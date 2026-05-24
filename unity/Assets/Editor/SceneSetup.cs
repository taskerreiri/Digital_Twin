using UnityEngine;
using UnityEditor;
using DT.GPS;
using DT.UI;

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

        // --- Player Marker ---
        var marker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        marker.name = "PlayerMarker";
        marker.transform.position = Vector3.zero;
        var markerRenderer = marker.GetComponent<Renderer>();
        var markerMat = new Material(Shader.Find("Standard"));
        markerMat.color = Color.blue;
        markerRenderer.material = markerMat;

        // --- Ground Plane ---
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(10, 1, 10);
        var groundRenderer = ground.GetComponent<Renderer>();
        var groundMat = new Material(Shader.Find("Standard"));
        groundMat.color = new Color(0.3f, 0.6f, 0.3f);
        groundRenderer.material = groundMat;

        // --- Reference Point Markers ---
        CreateReferencePoint("RefPoint_A", new Vector3(-20, 0.5f, -20), Color.red);
        CreateReferencePoint("RefPoint_B", new Vector3(20, 0.5f, 20), Color.yellow);
        CreateReferencePoint("RefPoint_C", new Vector3(-20, 0.5f, 20), Color.green);

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

        // --- EventSystem ---
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        Debug.Log("Digital Twin GPS Scene setup complete!");
    }

    static void CreateReferencePoint(string name, Vector3 pos, Color color)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.name = name;
        obj.transform.position = pos;
        var renderer = obj.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("Standard"));
        mat.color = color;
        renderer.material = mat;
    }
}
