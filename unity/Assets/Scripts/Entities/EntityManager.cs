using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using UnityEngine;
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
    }

    /// <summary>
    /// DTサーバーのWebSocketを購読し、全エンティティのアバターを生成/移動/管理する。
    /// 座標変換は既存 GPSCalibrator.GPSToUnity() を再利用。
    /// </summary>
    public class EntityManager : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] string serverWsUrl = "ws://localhost:9300/ws";

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

        class Entity
        {
            public GameObject go;
            public Vector3 targetPos;
            public string type;
            public double lat, lon;   // 最新GPS (再キャリブレーション用)
            public bool fused;        // カメラ検出: GPS融合済みか (色更新判定用)
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
        }

        /// <summary>キャリブレーション変更時、全エンティティの位置を再計算する</summary>
        public void RecalculateAll()
        {
            foreach (var e in entities.Values)
            {
                if (e.go == null) continue;
                Vector3 pos = ResolvePosition(e.lat, e.lon, e.type);
                e.targetPos = pos;
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
                    entities.Remove(msg.entityId);
                }
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
                entities[msg.entityId] = entity;
            }
            entity.lat = msg.lat;
            entity.lon = msg.lon;
            entity.targetPos = pos;
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
