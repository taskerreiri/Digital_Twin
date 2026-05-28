using UnityEngine;
using UnityEditor;
using System.IO;
using DT.Facility;

/// <summary>
/// 点群由来の地形メッシュ受け入れ。
/// Assets/PointCloud/ にメッシュ(FBX/OBJ)が存在すれば地形として配置し、
/// FacilityBuilderの概算プレースホルダ(箱)を非表示にする。
/// 無ければ従来のFacilityBuilderプレースホルダのままにする。
///
/// E57到着後の流れ:
///   1. パイプラインで mesh_to_unity.py がFBXを生成
///   2. FBXを Assets/PointCloud/ にコピー (例: yard_lod0.fbx)
///   3. シーン再生成 (BatchBuild) で自動的に地形として配置される
/// </summary>
public static class TerrainMeshLoader
{
    const string PointCloudDir = "Assets/PointCloud";
    // 地形として読み込むメッシュ (優先順)。pipelineのmesh_to_unity.py命名に対応
    static readonly string[] TerrainAssetNames = {
        "yard_lod0", "yard_lod1", "yard_terrain",
        "indoor_lod0", "indoor_terrain", "terrain"
    };

    /// <summary>地形メッシュが存在すれば配置しtrueを返す。無ければfalse。</summary>
    public static bool LoadIfPresent(GameObject facilityRoot)
    {
        if (!AssetDatabase.IsValidFolder(PointCloudDir))
        {
            Debug.Log($"[TerrainMeshLoader] {PointCloudDir} not found - using FacilityBuilder placeholder");
            return false;
        }

        var loaded = new System.Collections.Generic.List<GameObject>();
        var terrainParent = new GameObject("Terrain");

        foreach (var name in TerrainAssetNames)
        {
            foreach (var ext in new[] { "fbx", "obj" })
            {
                string path = $"{PointCloudDir}/{name}.{ext}";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.name = name;
                instance.transform.SetParent(terrainParent.transform);
                instance.transform.position = Vector3.zero; // 点群はworld座標で焼き込み済み
                instance.transform.localScale = Vector3.one;
                ApplyVertexColorMaterial(instance);
                AddColliders(instance);
                loaded.Add(instance);
                Debug.Log($"[TerrainMeshLoader] loaded terrain: {path}");
            }
        }

        if (loaded.Count == 0)
        {
            Object.DestroyImmediate(terrainParent);
            Debug.Log("[TerrainMeshLoader] no terrain mesh in Assets/PointCloud - using FacilityBuilder placeholder");
            return false;
        }

        // 実地形を読み込めたらプレースホルダの箱を非表示(参照用に残すなら半透明化でも可)
        HidePlaceholder(facilityRoot);
        Debug.Log($"[TerrainMeshLoader] {loaded.Count} terrain mesh(es) placed; placeholder hidden");
        return true;
    }

    static void ApplyVertexColorMaterial(GameObject go)
    {
        // 頂点カラー描画シェーダー (Built-in RP)。無ければStandardにフォールバック
        var shader = Shader.Find("DT/VertexColor") ?? Shader.Find("Standard");
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            r.sharedMaterial = new Material(shader);
        }
    }

    static void AddColliders(GameObject go)
    {
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var mc = mf.gameObject.GetComponent<MeshCollider>();
            if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
        }
    }

    static void HidePlaceholder(GameObject facilityRoot)
    {
        if (facilityRoot == null)
            facilityRoot = GameObject.Find("Facility");
        if (facilityRoot != null)
            facilityRoot.SetActive(false);
    }
}
