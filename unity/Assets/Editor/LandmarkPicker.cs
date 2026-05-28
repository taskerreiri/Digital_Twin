using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// landmark再整合ツール。
/// 実地形メッシュをシーンに読み込んだ後、Sceneビューで各landmark地点を
/// クリックして実際のUnity座標を取得し、server/landmarks.json を更新する。
///
/// 点群メッシュはFacilityBuilderの概算配置と座標系が異なるため、
/// E57導入後はこのツールでlandmarkのunityX/unityZを実メッシュに合わせ直す。
///
/// 使い方:
///   1. 実地形メッシュを配置したシーンを開く (BatchBuild後 or 手動)
///   2. Tools > Digital Twin > Landmark Picker
///   3. landmarkを選択 → "Pick in Scene" → Sceneビューでメッシュ上をクリック
///   4. 全landmark取得後 "Export landmarks.json"
/// </summary>
public class LandmarkPicker : EditorWindow
{
    [System.Serializable]
    class Landmark { public string landmarkId; public string label; public float unityX; public float unityZ; }
    [System.Serializable]
    class LandmarkList { public string _comment; public Landmark[] landmarks; }

    static string LandmarksPath =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "server", "landmarks.json"));

    List<Landmark> landmarks = new();
    int pickingIndex = -1;
    Vector2 scroll;

    [MenuItem("Tools/Digital Twin/Landmark Picker")]
    static void Open() => GetWindow<LandmarkPicker>("Landmark Picker");

    void OnEnable()
    {
        Load();
        SceneView.duringSceneGui += OnSceneGUI;
    }

    void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

    void Load()
    {
        landmarks.Clear();
        try
        {
            var json = File.ReadAllText(LandmarksPath);
            var list = JsonUtility.FromJson<LandmarkList>(json);
            if (list?.landmarks != null) landmarks.AddRange(list.landmarks);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LandmarkPicker] load failed: {e.Message}");
        }
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("landmarks.json", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(LandmarksPath, EditorStyles.miniLabel);
        EditorGUILayout.Space();

        if (GUILayout.Button("Reload from file")) Load();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        for (int i = 0; i < landmarks.Count; i++)
        {
            var lm = landmarks[i];
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{lm.label} ({lm.landmarkId})", GUILayout.Width(160));
            EditorGUILayout.LabelField($"X={lm.unityX:F1} Z={lm.unityZ:F1}", GUILayout.Width(140));
            bool picking = pickingIndex == i;
            if (GUILayout.Button(picking ? "Click in Scene..." : "Pick in Scene", GUILayout.Width(110)))
            {
                pickingIndex = picking ? -1 : i;
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        if (pickingIndex >= 0)
            EditorGUILayout.HelpBox(
                $"Sceneビューで「{landmarks[pickingIndex].label}」の地点(地形メッシュ上)をクリック",
                MessageType.Info);

        if (GUILayout.Button("Export landmarks.json", GUILayout.Height(30)))
            Export();
    }

    void OnSceneGUI(SceneView view)
    {
        if (pickingIndex < 0) return;
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 5000f))
            {
                var lm = landmarks[pickingIndex];
                lm.unityX = hit.point.x;
                lm.unityZ = hit.point.z;
                Debug.Log($"[LandmarkPicker] {lm.landmarkId} -> ({hit.point.x:F2}, {hit.point.z:F2})");
                pickingIndex = -1;
                Repaint();
                e.Use();
            }
            else
            {
                Debug.LogWarning("[LandmarkPicker] no mesh hit - ensure terrain has a Collider");
            }
        }
    }

    void Export()
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"_comment\": \"キャリブレーション用ランドマーク。LandmarkPickerで実地形メッシュに整合済み。\",");
        sb.AppendLine("  \"landmarks\": [");
        for (int i = 0; i < landmarks.Count; i++)
        {
            var lm = landmarks[i];
            string comma = i < landmarks.Count - 1 ? "," : "";
            sb.AppendLine($"    {{ \"landmarkId\": \"{lm.landmarkId}\", \"label\": \"{lm.label}\", " +
                          $"\"unityX\": {lm.unityX:F2}, \"unityZ\": {lm.unityZ:F2} }}{comma}");
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        File.WriteAllText(LandmarksPath, sb.ToString());
        Debug.Log($"[LandmarkPicker] exported {landmarks.Count} landmarks to {LandmarksPath}");
        AssetDatabase.Refresh();
    }
}
