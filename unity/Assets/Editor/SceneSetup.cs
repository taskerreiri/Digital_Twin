using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using DT.GPS;
using DT.UI;

public class SceneSetup
{
    [MenuItem("Digital Twin/Setup GPS Scene")]
    static void SetupScene()
    {
        // --- GPS Manager ---
        var gpsManager = new GameObject("GPSManager");
        var mockGPS = gpsManager.AddComponent<MockGPSProvider>();
        var webGLGPS = gpsManager.AddComponent<WebGLGPSProvider>();
        var calibrator = gpsManager.AddComponent<GPSCalibrator>();
        var tracker = gpsManager.AddComponent<PlayerPositionTracker>();

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

        // --- Reference Point Markers (calibration guides) ---
        CreateReferencePoint("RefPoint_A", new Vector3(-20, 0.5f, -20), Color.red);
        CreateReferencePoint("RefPoint_B", new Vector3(20, 0.5f, 20), Color.yellow);
        CreateReferencePoint("RefPoint_C", new Vector3(-20, 0.5f, 20), Color.green);

        // --- Canvas (UI) ---
        var canvasObj = new GameObject("GPSCanvas");
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        // Panel background
        var panel = CreateUIPanel(canvasObj.transform, "InfoPanel",
            new Vector2(320, 300), new Vector2(170, -160));

        // GPS Status
        var statusText = CreateUIText(panel.transform, "GPSStatusText",
            "Waiting for GPS...", new Vector2(0, -10), Color.green);

        // Coordinates
        var coordText = CreateUIText(panel.transform, "CoordinateText",
            "Lat: --\nLon: --\nAlt: --", new Vector2(0, -60), Color.white);

        // Unity Position
        var unityPosText = CreateUIText(panel.transform, "UnityPositionText",
            "Unity Pos: --", new Vector2(0, -120), Color.cyan);

        // Calibration Status
        var calibText = CreateUIText(panel.transform, "CalibrationText",
            "Not calibrated", new Vector2(0, -160), Color.yellow);

        // Point Name Input
        var inputObj = CreateUIInputField(panel.transform, "PointNameInput",
            new Vector2(0, -200));

        // Calibrate Button
        var buttonObj = CreateUIButton(panel.transform, "CalibrateButton",
            "Add Calibration Point", new Vector2(0, -240));

        // --- GPS Debug UI ---
        var debugUI = gpsManager.AddComponent<GPSDebugUI>();

        // Wire up references via SerializedObject
        var so = new SerializedObject(debugUI);
        so.FindProperty("mockGPSProvider").objectReferenceValue = mockGPS;
        so.FindProperty("webGLGPSProvider").objectReferenceValue = webGLGPS;
        so.FindProperty("calibrator").objectReferenceValue = calibrator;
        so.FindProperty("playerMarker").objectReferenceValue = marker.transform;
        so.FindProperty("gpsStatusText").objectReferenceValue = statusText;
        so.FindProperty("coordinateText").objectReferenceValue = coordText;
        so.FindProperty("unityPositionText").objectReferenceValue = unityPosText;
        so.FindProperty("calibrationText").objectReferenceValue = calibText;
        so.FindProperty("pointNameInput").objectReferenceValue = inputObj;
        so.FindProperty("calibrateButton").objectReferenceValue = buttonObj;
        so.ApplyModifiedProperties();

        // Wire up PlayerPositionTracker
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
        EditorUtility.DisplayDialog("Setup Complete",
            "GPS scene has been configured.\n\n" +
            "- GPSManager: MockGPS + Calibrator + Tracker + DebugUI\n" +
            "- PlayerMarker (blue capsule)\n" +
            "- Ground (100m x 100m)\n" +
            "- 3 Reference Points (red/yellow/green)\n" +
            "- UI Panel with GPS info + calibration controls\n\n" +
            "Press Play to test. Use arrow keys to simulate GPS movement.",
            "OK");
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

    static GameObject CreateUIPanel(Transform parent, string name, Vector2 size, Vector2 pos)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(10, -10);

        var image = obj.AddComponent<Image>();
        image.color = new Color(0, 0, 0, 0.7f);

        return obj;
    }

    static Text CreateUIText(Transform parent, string name, string content, Vector2 pos, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.sizeDelta = new Vector2(-20, 40);
        rect.anchoredPosition = pos;

        var text = obj.AddComponent<Text>();
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 14;
        text.color = color;
        text.alignment = TextAnchor.UpperLeft;

        return text;
    }

    static InputField CreateUIInputField(Transform parent, string name, Vector2 pos)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.sizeDelta = new Vector2(-20, 30);
        rect.anchoredPosition = pos;

        var image = obj.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f);

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(obj.transform, false);
        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = new Vector2(-10, 0);
        var text = textObj.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 14;
        text.color = Color.white;
        text.supportRichText = false;

        var placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(obj.transform, false);
        var phRect = placeholderObj.AddComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero;
        phRect.anchorMax = Vector2.one;
        phRect.sizeDelta = new Vector2(-10, 0);
        var phText = placeholderObj.AddComponent<Text>();
        phText.text = "Point name...";
        phText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        phText.fontSize = 14;
        phText.fontStyle = FontStyle.Italic;
        phText.color = new Color(1, 1, 1, 0.3f);

        var input = obj.AddComponent<InputField>();
        input.textComponent = text;
        input.placeholder = phText;

        return input;
    }

    static Button CreateUIButton(Transform parent, string name, string label, Vector2 pos)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.sizeDelta = new Vector2(-20, 35);
        rect.anchoredPosition = pos;

        var image = obj.AddComponent<Image>();
        image.color = new Color(0.2f, 0.5f, 0.8f);

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(obj.transform, false);
        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        var text = textObj.AddComponent<Text>();
        text.text = label;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 14;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;

        obj.AddComponent<Button>();

        return obj.GetComponent<Button>();
    }
}
