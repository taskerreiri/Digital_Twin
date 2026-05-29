using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using DT.GPS;

#if !UNITY_WEBGL || UNITY_EDITOR
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
#endif

namespace DT.Entities
{
    [Serializable]
    public class DTMessage
    {
        public string type;          // position_update | material_placed | detection_update | detection_remove
        public string entityId;
        public string entityType;    // worker | equipment | material | person | vehicle
        public string displayName;
        public string color;         // hex "#RRGGBB"
        public double lat;
        public double lon;
        public string zoneId;
        public string source;        // gps | qr_zone | camera
        public long timestamp;
        // カメラ検出用 (detection_update)
        public double worldX;
        public double worldZ;
        public string cameraId;
        public string fusedWith;     // 融合先GPSエンティティID
        public float confidence;
        // シーン解析用 (scene_analysis)
        public string state;         // operating | idle | abnormal | unknown
        public string congestion;    // low | medium | high
        public SceneText[] texts;    // OCR結果
        public string sourceAi;      // yolo | claude | mock
    }

    [Serializable]
    public class SceneText
    {
        public string text;
        public string kind;
    }

    /// <summary>
    /// DTサーバーのWebSocketを購読し、全エンティティのアバターを生成/移動/管理する。
    /// 座標変換は既存 GPSCalibrator.GPSToUnity() を再利用。
    /// </summary>
    public class EntityManager : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] string serverWsUrl = "ws://localhost:9300/ws";
        [SerializeField] string apiBaseUrl = "http://localhost:9300";

        [Header("References")]
        [SerializeField] GPSCalibrator calibrator;

        [Header("Avatar")]
        [SerializeField] float moveLerpSpeed = 5f;
        [SerializeField] float workerHeight = 1.7f;
        [SerializeField] float equipmentScale = 3f;
        [SerializeField] float materialScale = 2f;

        [Header("Fallback (未キャリブレーション時の仮配置)")]
        [Tooltip("GPS基準点。シミュレータ/敷地中心と整合させる")]
        [SerializeField] double fallbackBaseLat = 35.6817;
        [SerializeField] double fallbackBaseLon = 139.7674;
        [Tooltip("仮配置の原点オフセット。FacilityBuilder施設中心(250,-175)に合わせる")]
        [SerializeField] Vector3 fallbackOrigin = new Vector3(250f, 0f, -175f);

        readonly Dictionary<string, Entity> entities = new();
        readonly ConcurrentQueue<string> incoming = new();

        // シーン解析結果 (cameraId -> 最新)。SceneAnalysisRenderer が表示
        public static readonly Dictionary<string, DTMessage> SceneAnalyses = new();

        class Entity
        {
            public GameObject go;
            public Vector3 targetPos;
            public string type;
            public double lat, lon;   // 最新GPS (再キャリブレーション用)
            public bool fused;        // カメラ検出: GPS融合済みか (色更新判定用)
            public EntityTrail trail; // 移動軌跡 (GPS/QRエンティティのみ。カメラblipは無し)
        }

        // ---- WebGL jslib bridge ----
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void DTConnect(string url, string obj, string onMsg, string onStatus);
        [DllImport("__Internal")]
        static extern void DTDisconnect();
#endif

#if !UNITY_WEBGL || UNITY_EDITOR
        ClientWebSocket socket;
        CancellationTokenSource cts;
#endif

        void Start()
        {
            if (calibrator == null)
                calibrator = FindFirstObjectByType<GPSCalibrator>();

            var calSync = FindFirstObjectByType<CalibrationSync>();
            if (calSync != null)
                calSync.OnCalibrationApplied += RecalculateAll;

            Connect();
            StartCoroutine(LoadInitialTracks());
        }

        [Serializable] class TrackPoint { public double lat; public double lon; public long timestamp; }
        [Serializable] class TrackData { public string entityId; public string entityType; public string color; public TrackPoint[] points; }
        [Serializable] class TrackResponse { public TrackData[] tracks; }

        IEnumerator LoadInitialTracks()
        {
            string url = $"{apiBaseUrl}/api/tracks?minutes=5&limit=200";
            using var req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[EntityManager] track load failed: {req.error} (WSのappendのみで構築)");
                yield break;
            }
            TrackResponse resp;
            try { resp = JsonUtility.FromJson<TrackResponse>(req.downloadHandler.text); }
            catch (Exception ex) { Debug.LogWarning($"[EntityManager] track parse error: {ex.Message}"); yield break; }
            if (resp?.tracks == null) yield break;

            foreach (var td in resp.tracks)
            {
                if (td == null || td.points == null || td.points.Length == 0) continue;
                // エンティティがまだ無ければ最新点でアバターを生成しておく(WSスナップショットと重複しても上書きされるだけ)
                var last = td.points[td.points.Length - 1];
                var seed = new DTMessage {
                    type = "position_update", entityId = td.entityId, entityType = td.entityType,
                    color = td.color, lat = last.lat, lon = last.lon, timestamp = last.timestamp };
                if (!entities.TryGetValue(td.entityId, out var entity))
                {
                    Vector3 pos = ResolvePosition(seed.lat, seed.lon, seed.entityType);
                    entity = new Entity { type = seed.entityType, lat = seed.lat, lon = seed.lon, targetPos = pos };
                    entity.go = CreateAvatar(seed);
                    entity.go.transform.position = pos;
                    entity.trail = CreateTrail(seed);
                    entities[td.entityId] = entity;
                }
                var pts = new List<(double, double, long)>(td.points.Length);
                foreach (var p in td.points) pts.Add((p.lat, p.lon, p.timestamp));
                entity.trail?.SetPoints(pts);
            }
            ApplyHighlight(); // 遅延seedされたtrailにも現在の強調状態を反映
        }

        // 種別トグル (TrailControlUI から設定)。初期=両方ON
        public bool ShowWorkerTrails = true;
        public bool ShowEquipmentTrails = true;
        string highlightedId = null; // クリック強調中のエンティティ (null=全体通常)

        bool IsTypeVisible(string entityType)
        {
            if (entityType == "equipment") return ShowEquipmentTrails;
            if (entityType == "worker") return ShowWorkerTrails;
            return true; // その他(material等)は trail を持たないが念のため
        }

        /// <summary>種別トグル変更時に全 trail の表示状態を再適用する。強調は ApplyHighlight に一本化。</summary>
        public void ApplyTrailVisibility()
        {
            foreach (var e in entities.Values)
            {
                if (e.trail == null) continue;
                e.trail.SetVisible(IsTypeVisible(e.type));
            }
            ApplyHighlight();
        }

        // 強調中は対象 trail を濃く・他を減光。強調なしは全て通常。
        void ApplyHighlight()
        {
            foreach (var kv in entities)
            {
                var e = kv.Value;
                if (e.trail == null) continue;
                float intensity = (highlightedId == null || kv.Key == highlightedId) ? 1f : 0.25f;
                e.trail.SetIntensity(intensity);
            }
        }

        string FindEntityIdByGameObject(GameObject go)
        {
            foreach (var kv in entities)
                if (kv.Value.go == go) return kv.Key;
            return null;
        }

        /// <summary>キャリブレーション変更時、全エンティティの位置を再計算する</summary>
        public void RecalculateAll()
        {
            foreach (var e in entities.Values)
            {
                if (e.go == null) continue;
                Vector3 pos = ResolvePosition(e.lat, e.lon, e.type);
                e.targetPos = pos;
                e.trail?.Rebuild();
            }
        }

        Vector3 ResolvePosition(double lat, double lon, string entityType)
        {
            Vector3 pos = (calibrator != null && calibrator.IsCalibrated)
                ? calibrator.GPSToUnity(lat, lon)
                : FallbackPosition(lat, lon);
            pos.y = entityType == "worker" ? workerHeight : 0.5f;
            return pos;
        }

        void Connect()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            DTConnect(serverWsUrl, gameObject.name, nameof(OnWsMessage), nameof(OnWsStatus));
#else
            _ = ConnectNativeAsync();
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        async Task ConnectNativeAsync()
        {
            cts = new CancellationTokenSource();
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    socket = new ClientWebSocket();
                    await socket.ConnectAsync(new Uri(serverWsUrl), cts.Token);
                    Debug.Log("[EntityManager] WS connected");
                    await ReceiveLoop();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EntityManager] WS error: {e.Message}, retry in 3s");
                    await Task.Delay(3000);
                }
            }
        }

        async Task ReceiveLoop()
        {
            var buffer = new byte[8192];
            var sb = new StringBuilder();
            while (socket.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage)
                {
                    incoming.Enqueue(sb.ToString());
                    sb.Clear();
                }
            }
        }
#endif

        // WebGL jslib callbacks (main thread)
        public void OnWsMessage(string json)
        {
            incoming.Enqueue(json);
        }

        public void OnWsStatus(string status)
        {
            Debug.Log($"[EntityManager] WS status: {status}");
        }

        void Update()
        {
            // 受信メッセージを処理 (メインスレッド)
            while (incoming.TryDequeue(out var json))
            {
                try { HandleMessage(json); }
                catch (Exception e) { Debug.LogWarning($"[EntityManager] parse error: {e.Message}"); }
            }

            // アバターを目標位置へ補間移動
            float dt = Time.deltaTime * moveLerpSpeed;
            foreach (var e in entities.Values)
            {
                if (e.go != null)
                    e.go.transform.position = Vector3.Lerp(
                        e.go.transform.position, e.targetPos, dt);
            }

            // マウスクリックでアバター強調 (左クリック)
            if (Input.GetMouseButtonDown(0))
            {
                var cam = Camera.main;
                if (cam == null)
                {
                    Debug.LogWarning("[EntityManager] クリック強調: MainCamera タグのカメラが無く無効化");
                }
                else
                {
                    var ray = cam.ScreenPointToRay(Input.mousePosition);
                    if (Physics.Raycast(ray, out var hit))
                    {
                        // アバター(またはその子)に対応するエンティティを探す
                        string hitId = FindEntityIdByGameObject(hit.collider.gameObject);
                        highlightedId = (hitId != null && hitId == highlightedId) ? null : hitId;
                    }
                    else
                    {
                        highlightedId = null; // 何もない所をクリックで解除
                    }
                    ApplyHighlight();
                }
            }
        }

        void HandleMessage(string json)
        {
            var msg = JsonUtility.FromJson<DTMessage>(json);
            if (msg == null) return;

            // カメラ検出の消失
            if (msg.type == "detection_remove")
            {
                if (!string.IsNullOrEmpty(msg.entityId) &&
                    entities.TryGetValue(msg.entityId, out var rem))
                {
                    if (rem.go != null) Destroy(rem.go);
                    if (rem.trail != null) Destroy(rem.trail.gameObject);
                    entities.Remove(msg.entityId);
                }
                return;
            }

            // シーン解析結果 (Phase 2.2/2.3): カメラ別に保存
            if (msg.type == "scene_analysis")
            {
                if (!string.IsNullOrEmpty(msg.cameraId))
                    SceneAnalyses[msg.cameraId] = msg;
                return;
            }

            if (string.IsNullOrEmpty(msg.entityId)) return;

            // カメラ検出: worldX/Z をそのまま使用 (homography適用済み, calibrator不要)
            if (msg.type == "detection_update")
            {
                HandleDetection(msg);
                return;
            }

            // GPS/QR: lat/lon を calibrator で変換
            Vector3 pos = ResolvePosition(msg.lat, msg.lon, msg.entityType);

            if (!entities.TryGetValue(msg.entityId, out var entity))
            {
                entity = new Entity { type = msg.entityType };
                entity.go = CreateAvatar(msg);
                entity.go.transform.position = pos;
                entity.trail = CreateTrail(msg);
                entities[msg.entityId] = entity;
            }
            entity.lat = msg.lat;
            entity.lon = msg.lon;
            entity.targetPos = pos;
            entity.trail?.Append(msg.lat, msg.lon, msg.timestamp);
        }

        void HandleDetection(DTMessage msg)
        {
            float y = msg.entityType == "person" ? workerHeight : 0.5f;
            Vector3 pos = new Vector3((float)msg.worldX, y, (float)msg.worldZ);
            bool fused = !string.IsNullOrEmpty(msg.fusedWith);

            if (!entities.TryGetValue(msg.entityId, out var entity))
            {
                entity = new Entity { type = msg.entityType, fused = fused };
                entity.go = CreateCameraBlip(msg);
                entity.go.transform.position = pos;
                entities[msg.entityId] = entity;
            }
            else if (entity.fused != fused && entity.go != null)
            {
                // 融合状態が変化したら色とラベルを更新
                UpdateBlipAppearance(entity.go, msg);
                entity.fused = fused;
            }
            entity.targetPos = pos;
        }

        Vector3 FallbackPosition(double lat, double lon)
        {
            // 簡易: 基準点からのメートル換算 + 施設中心オフセット (キャリブレーション前の暫定表示)
            const double mPerLat = 111320.0;
            double mPerLon = mPerLat * Math.Cos(fallbackBaseLat * Math.PI / 180.0);
            float x = (float)((lon - fallbackBaseLon) * mPerLon);
            float z = (float)((lat - fallbackBaseLat) * mPerLat);
            return new Vector3(fallbackOrigin.x + x, fallbackOrigin.y, fallbackOrigin.z + z);
        }

        GameObject CreateAvatar(DTMessage msg)
        {
            GameObject go;
            switch (msg.entityType)
            {
                case "equipment":
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.transform.localScale = Vector3.one * equipmentScale;
                    break;
                case "material":
                    go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    go.transform.localScale = new Vector3(materialScale, 0.3f, materialScale);
                    break;
                default: // worker
                    go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    go.transform.localScale = new Vector3(0.6f, workerHeight / 2f, 0.6f);
                    break;
            }

            go.name = $"{msg.entityType}:{msg.entityId}";
            go.transform.SetParent(transform);

            // 色設定
            var renderer = go.GetComponent<Renderer>();
            Color col = Color.gray;
            if (!string.IsNullOrEmpty(msg.color))
                ColorUtility.TryParseHtmlString(msg.color, out col);
            // このプロジェクトはBuilt-in RP (Standard)。URP不在時のnull Materialを回避
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = col;
            renderer.material = mat;

            // ラベル用にメタを保持
            var label = go.AddComponent<EntityLabel>();
            label.displayName = string.IsNullOrEmpty(msg.displayName) ? msg.entityId : msg.displayName;

            return go;
        }

        // GPSアバター用に軌跡 LineRenderer を生成し、エンティティ色とworld変換resolverで初期化
        EntityTrail CreateTrail(DTMessage msg)
        {
            var trailGo = new GameObject($"trail:{msg.entityId}");
            trailGo.transform.SetParent(transform);
            var trail = trailGo.AddComponent<EntityTrail>();
            Color col = Color.gray;
            if (!string.IsNullOrEmpty(msg.color))
                ColorUtility.TryParseHtmlString(msg.color, out col);
            // resolver: lat/lon -> world (現在位置と同じ変換経路)。msg全体を抱えないよう型だけローカル捕捉
            string et = msg.entityType;
            trail.Init(col, (lat, lon) => ResolvePosition(lat, lon, et));
            // 種別トグルの現在状態を反映
            trail.SetVisible(IsTypeVisible(msg.entityType));
            return trail;
        }

        GameObject CreateCameraBlip(DTMessage msg)
        {
            // カメラ検出はGPSアバターと区別: 半透明の縦長マーカー(person)/平たい箱(vehicle)
            GameObject go;
            if (msg.entityType == "vehicle")
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.localScale = new Vector3(equipmentScale, 1f, equipmentScale);
            }
            else // person
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.transform.localScale = new Vector3(0.7f, workerHeight / 2f, 0.7f);
            }
            go.name = $"cam:{msg.entityType}:{msg.entityId}";
            go.transform.SetParent(transform);

            var renderer = go.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            // 半透明設定 (Standard)
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = 3000;
            renderer.material = mat;

            go.AddComponent<EntityLabel>();
            ApplyBlipStyle(go, msg);
            return go;
        }

        // 融合済みは緑(GPSで個体確認済), 未融合はシアン(匿名検出)
        void ApplyBlipStyle(GameObject go, DTMessage msg)
        {
            bool fused = !string.IsNullOrEmpty(msg.fusedWith);
            Color col = fused ? new Color(0.4f, 0.9f, 0.6f, 0.6f)
                              : new Color(0.3f, 0.85f, 1f, 0.5f);
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = col;
            var label = go.GetComponent<EntityLabel>();
            if (label != null)
                label.displayName = fused ? $"CAM:{msg.fusedWith}" : $"CAM:{msg.entityType}";
        }

        void UpdateBlipAppearance(GameObject go, DTMessage msg)
        {
            ApplyBlipStyle(go, msg);
        }

        void OnDestroy()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            DTDisconnect();
#else
            cts?.Cancel();
            socket?.Dispose();
#endif
        }
    }

    /// <summary>エンティティ名ラベル保持用</summary>
    public class EntityLabel : MonoBehaviour
    {
        public string displayName;
    }
}
