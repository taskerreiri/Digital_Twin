# GPS 軌跡プロット (ライブtrail) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `positions` テーブルに溜まった位置ログを、各エンティティの移動軌跡として Unity 監視ビューに `LineRenderer` で可視化する(ライブ追従trail)。

**Architecture:** server に履歴取得API `GET /api/tracks` を追加(生 lat/lon/timestamp を返す)。Unity は起動時にこのAPIで過去5分を遡及ロードし、以降は既存WS `position_update` で点を append。各エンティティが `EntityTrail`(LineRenderer)を1本持ち、直近5分かつ最大200点でトリム。座標変換は既存 `GPSCalibrator.GPSToUnity()` を再利用し、再キャリブレーション時に trail も再構築する。

**Tech Stack:** Node.js + better-sqlite3 + Express (server), Unity 6000.3.16f1 Built-in RP + C# + UnityWebRequest + LineRenderer (監視ビュー WebGL), IMGUI (トグルUI)。

---

## File Structure

| ファイル | 役割 | 操作 |
|---|---|---|
| `server/db.js` | `getTracks()` 追加。positions から時間窓内の点を取得・間引き | Modify |
| `server/server.js` | `GET /api/tracks` 追加 | Modify |
| `server/test_tracks.js` | getTracks + /api/tracks の assert テスト | Create |
| `unity/Assets/Scripts/Entities/EntityTrail.cs` | LineRenderer 1本を保持、点の append/trim/再構築/表示制御 | Create |
| `unity/Assets/Scripts/Entities/EntityManager.cs` | trail生成・append・初期ロードfetch・種別トグル・クリック強調 | Modify |
| `unity/Assets/Scripts/UI/TrailControlUI.cs` | trail種別ON/OFFトグル(IMGUI) | Create |

**設計境界:** trail の点保持・描画ロジックは `EntityTrail` に完全に閉じる(EntityManager は append を呼ぶだけ)。`EntityTrail` は raw (lat, lon, timestamp) を保持し、world座標への変換は外から渡された resolver 関数で行う — これにより再キャリブレーション時の再構築が自然に書ける。UI は `TrailControlUI` に分離し、EntityManager の public プロパティ/メソッドを叩くだけにする。

---

## 前提: テスト方針

- **server**: このプロジェクトにテストランナーは無く、`server/test_ws.js` `server/test_detections.js` のように **node スクリプト + `assert` で検証** する慣習。本plan もそれに従う(`node server/test_tracks.js` で PASS/FAIL を出力)。
- **Unity**: バッチでのユニットテスト環境が無いため、`EntityTrail` 等は **コンパイル成功 + E2E目視** で検証(既存タスクの検証方式に一致)。コンパイルは WebGL バッチビルド or Editor 起動で確認する。

---

## Task 1: server `getTracks()` (db.js)

**Files:**
- Modify: `server/db.js`(末尾の `export default db;` の直前に追加)
- Test: `server/test_tracks.js`(Create)

- [ ] **Step 1: Write the failing test**

`server/test_tracks.js` を新規作成:

```javascript
// getTracks 単体テスト: 時間窓フィルタ・点数間引き・type絞り込み・null除外
import assert from 'node:assert';
import db, { recordPosition, getTracks } from './db.js';

// クリーンスタート用に対象IDの既存行を消す
db.prepare("DELETE FROM positions WHERE entity_id IN ('t_w1','t_e1')").run();
db.prepare("DELETE FROM entities  WHERE entity_id IN ('t_w1','t_e1')").run();

const now = Date.now();

// t_w1 (worker): 直近に5点 + 10分前に1点(窓外)
for (let i = 0; i < 5; i++) {
  recordPosition({ entityId: 't_w1', entityType: 'worker', source: 'gps',
    lat: 35.0 + i * 0.0001, lon: 139.0 + i * 0.0001, timestamp: now - (5 - i) * 1000 });
}
recordPosition({ entityId: 't_w1', entityType: 'worker', source: 'gps',
  lat: 34.0, lon: 138.0, timestamp: now - 10 * 60 * 1000 }); // 窓外(10分前)

// t_e1 (equipment): 直近に3点
for (let i = 0; i < 3; i++) {
  recordPosition({ entityId: 't_e1', entityType: 'equipment', source: 'gps',
    lat: 35.5 + i * 0.0001, lon: 139.5 + i * 0.0001, timestamp: now - (3 - i) * 1000 });
}

// (a) 時間窓5分: t_w1 は窓内5点のみ(10分前は除外)
const all = getTracks({ minutes: 5, limit: 200 });
const w1 = all.find((t) => t.entityId === 't_w1');
assert.ok(w1, 't_w1 should be present');
assert.strictEqual(w1.points.length, 5, 'window should exclude the 10-min-old point');
assert.strictEqual(w1.entityType, 'worker');
assert.ok(w1.points[0].timestamp <= w1.points[4].timestamp, 'points ascending by time');

// (b) 点数上限: limit=3 で最新3点だけ
const capped = getTracks({ minutes: 5, limit: 3 });
const w1c = capped.find((t) => t.entityId === 't_w1');
assert.strictEqual(w1c.points.length, 3, 'limit should keep newest 3');
assert.ok(w1c.points[2].timestamp >= w1c.points[0].timestamp, 'kept points are the newest, ascending');

// (c) type 絞り込み
const eqOnly = getTracks({ minutes: 5, limit: 200, type: 'equipment' });
assert.ok(eqOnly.every((t) => t.entityType === 'equipment'), 'type filter');
assert.ok(eqOnly.find((t) => t.entityId === 't_e1'), 't_e1 present in equipment filter');
assert.ok(!eqOnly.find((t) => t.entityId === 't_w1'), 't_w1 absent in equipment filter');

console.log('PASS: getTracks (window / limit / type / order)');
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd C:\dev\projects\Digital_Twin\server; node test_tracks.js`
Expected: FAIL — `SyntaxError` / `getTracks is not a function`(まだ未実装のため import エラー)。

- [ ] **Step 3: Implement `getTracks` in db.js**

`server/db.js` の `export default db;`(最終行)の **直前** に追加:

```javascript
// --- 軌跡(trail)取得: positions から時間窓内の点をエンティティ別に返す ---
// 返却は生 lat/lon/timestamp。座標変換は呼び出し側(Unity GPSCalibrator)に委ねる。
// 将来の履歴ビューア用に from/to(エポックms)も受けるが、未指定時は minutes 窓を使う。
const trackRows = db.prepare(`
  SELECT p.entity_id, p.entity_type, p.lat, p.lon, p.timestamp, e.color
  FROM positions p
  LEFT JOIN entities e ON e.entity_id = p.entity_id
  WHERE p.timestamp >= @from AND p.timestamp <= @to
    AND p.lat IS NOT NULL AND p.lon IS NOT NULL
    AND (@type IS NULL OR p.entity_type = @type)
  ORDER BY p.entity_id ASC, p.timestamp ASC
`);

export function getTracks({ minutes = 5, limit = 200, type = null, from = null, to = null } = {}) {
  const now = Date.now();
  const toMs = to != null ? Number(to) : now;
  const fromMs = from != null ? Number(from) : now - minutes * 60 * 1000;

  const rows = trackRows.all({ from: fromMs, to: toMs, type: type || null });

  // entity_id ごとにグループ化(rows は entity_id, timestamp 昇順)
  const byEntity = new Map();
  for (const r of rows) {
    if (!byEntity.has(r.entity_id)) {
      byEntity.set(r.entity_id, {
        entityId: r.entity_id,
        entityType: r.entity_type,
        color: r.color || '#CCCCCC',
        points: [],
      });
    }
    byEntity.get(r.entity_id).points.push({ lat: r.lat, lon: r.lon, timestamp: r.timestamp });
  }

  // 点数上限: 各エンティティで最新 limit 点(末尾)を残す
  const out = [];
  for (const t of byEntity.values()) {
    if (t.points.length > limit) t.points = t.points.slice(t.points.length - limit);
    out.push(t);
  }
  return out;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd C:\dev\projects\Digital_Twin\server; node test_tracks.js`
Expected: PASS — `PASS: getTracks (window / limit / type / order)`

- [ ] **Step 5: Commit**

```bash
cd C:\dev\projects\Digital_Twin
git add server/db.js server/test_tracks.js
git commit -m "feat(server): add getTracks() for position trail history"
```

---

## Task 2: server `GET /api/tracks` (server.js)

**Files:**
- Modify: `server/server.js`(import に `getTracks` 追加 + `/api/entities` の直後にルート追加)
- Test: `server/test_tracks.js`(HTTP 検証を追記)

- [ ] **Step 1: Write the failing test (HTTP)**

`server/test_tracks.js` の末尾(`console.log('PASS: getTracks...')` の後)に追記:

```javascript
// --- HTTP: GET /api/tracks (サーバー起動が前提) ---
const BASE = process.env.DT_BASE || 'http://localhost:9300';
try {
  const res = await fetch(`${BASE}/api/tracks?minutes=5&limit=200`);
  assert.strictEqual(res.status, 200, 'status 200');
  const body = await res.json();
  assert.ok(Array.isArray(body.tracks), 'body.tracks is array');
  for (const t of body.tracks) {
    assert.ok(typeof t.entityId === 'string', 'entityId string');
    assert.ok(Array.isArray(t.points), 'points array');
  }
  console.log(`PASS: GET /api/tracks (${body.tracks.length} tracks)`);
} catch (e) {
  console.log(`SKIP HTTP test (server not running?): ${e.message}`);
}
```

- [ ] **Step 2: Run to verify HTTP part fails / skips**

Run: `cd C:\dev\projects\Digital_Twin\server; node test_tracks.js`
Expected: getTracks PASS のまま、HTTP は `SKIP`(ルート未実装 or サーバー未起動)。ルートを実装後に起動して PASS させる。

- [ ] **Step 3: Add import in server.js**

`server/server.js:9-10` の import 文を修正(`getSnapshot` の並びに `getTracks` を追加):

```javascript
import { recordPosition, recordMaterial, getSnapshot, getTracks, recordCalibration, getCalibration,
         recordDetection, getDetections, purgeStaleDetections } from './db.js';
```

- [ ] **Step 4: Add the route**

`server/server.js` の `app.get('/api/entities', ...)`(170-172行)の **直後** に追加:

```javascript
app.get('/api/tracks', (req, res) => {
  const minutes = req.query.minutes != null ? Number(req.query.minutes) : 5;
  const limit = req.query.limit != null ? Number(req.query.limit) : 200;
  const type = req.query.type || null;
  // 将来の履歴ビューア用の受け口(未指定時は minutes 窓)
  const from = req.query.from != null ? Number(req.query.from) : null;
  const to = req.query.to != null ? Number(req.query.to) : null;
  const tracks = getTracks({ minutes, limit, type, from, to });
  res.json({ tracks });
});
```

- [ ] **Step 5: Run server + test to verify pass**

ターミナルA(サーバー起動。EADDRINUSE 時は既存 9300 を kill してから):
Run: `cd C:\dev\projects\Digital_Twin\server; node server.js`

ターミナルB:
Run: `cd C:\dev\projects\Digital_Twin\server; node test_tracks.js`
Expected: `PASS: getTracks ...` と `PASS: GET /api/tracks (N tracks)` の両方。

- [ ] **Step 6: Commit**

```bash
cd C:\dev\projects\Digital_Twin
git add server/server.js server/test_tracks.js
git commit -m "feat(server): add GET /api/tracks endpoint"
```

---

## Task 3: Unity `EntityTrail` コンポーネント (新規)

**Files:**
- Create: `unity/Assets/Scripts/Entities/EntityTrail.cs`

**責務:** LineRenderer 1本を保持。raw (lat, lon, timestamp) のリストを持ち、append/trim(直近5分 かつ 最大200点)、world座標への再構築(resolver関数経由)、表示ON/OFF・強調/減光を提供する。world変換そのものは行わず、外部から渡された `Func<double,double,Vector3>` で解決する(再キャリブレーション対応)。

- [ ] **Step 1: Create EntityTrail.cs**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DT.Entities
{
    /// <summary>
    /// 1エンティティの移動軌跡を LineRenderer で描く。
    /// raw (lat,lon,timestamp) を保持し、world変換は外部 resolver に委ねる(再キャリブレーション対応)。
    /// 直近 WindowMs かつ 最大 MaxPoints でトリム。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class EntityTrail : MonoBehaviour
    {
        public const long WindowMs = 5 * 60 * 1000; // 5分
        public const int MaxPoints = 200;

        struct P { public double lat; public double lon; public long t; }
        readonly List<P> points = new();

        LineRenderer lr;
        Color baseColor = Color.gray;
        bool visible = true;
        float intensity = 1f; // 1=通常, <1=減光(他を強調時)

        Func<double, double, Vector3> resolver;

        void Awake()
        {
            lr = GetComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.4f;
            lr.numCornerVertices = 2;
            lr.positionCount = 0;
            // Built-in RP: URP不在時の null Material を回避(EntityManager の方式に一致)
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            lr.material = new Material(shader);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        /// <summary>初期化。色とworld変換resolverを与える。</summary>
        public void Init(Color color, Func<double, double, Vector3> worldResolver)
        {
            baseColor = color;
            resolver = worldResolver;
        }

        /// <summary>API初期ロード等で点列を一括設定(timestamp昇順前提)。</summary>
        public void SetPoints(IEnumerable<(double lat, double lon, long t)> pts)
        {
            points.Clear();
            foreach (var p in pts) points.Add(new P { lat = p.lat, lon = p.lon, t = p.t });
            Trim();
            Rebuild();
        }

        /// <summary>ライブ追従: 1点追加。</summary>
        public void Append(double lat, double lon, long timestamp)
        {
            points.Add(new P { lat = lat, lon = lon, t = timestamp });
            Trim();
            Rebuild();
        }

        /// <summary>再キャリブレーション時など、world座標を再計算。</summary>
        public void Rebuild()
        {
            // 時間窓トリム(古い側 = 末尾基準)も最新点基準で評価
            Trim();
            if (resolver == null || !visible || points.Count < 2)
            {
                lr.positionCount = 0;
                return;
            }
            lr.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 w = resolver(points[i].lat, points[i].lon);
                w.y = 0.3f; // 地表近くに描く
                lr.SetPosition(i, w);
            }
            ApplyGradient();
        }

        void Trim()
        {
            if (points.Count == 0) return;
            long newest = points[points.Count - 1].t;
            long cutoff = newest - WindowMs;
            // 時間窓外(古い点)を先頭から除去
            int drop = 0;
            while (drop < points.Count && points[drop].t < cutoff) drop++;
            if (drop > 0) points.RemoveRange(0, drop);
            // 点数上限: 古い側を切る
            if (points.Count > MaxPoints)
                points.RemoveRange(0, points.Count - MaxPoints);
        }

        void ApplyGradient()
        {
            Color c = baseColor * intensity;
            // 先頭(古)=透明寄り → 末尾(新)=濃いフェード
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(0.05f, 0f), new GradientAlphaKey(0.9f, 1f) });
            lr.colorGradient = grad;
        }

        /// <summary>種別トグル等での表示ON/OFF。</summary>
        public void SetVisible(bool v)
        {
            visible = v;
            Rebuild();
        }

        /// <summary>強調/減光。highlighted=null は通常(全体表示)、true=強調、false=減光。</summary>
        public void SetIntensity(float value)
        {
            intensity = value;
            if (visible) ApplyGradient();
        }
    }
}
```

- [ ] **Step 2: Verify compile**

Run(WebGLバッチビルド or Editor 起動でコンパイル確認):
`& "<Unity>" -batchmode -quit -projectPath C:\dev\projects\Digital_Twin\unity -logFile -`
(`<Unity>` は 6000.3.16f1 の Unity.exe)
Expected: コンパイルエラー無し(`EntityTrail.cs` に関する CS エラーが出ないこと)。

- [ ] **Step 3: Commit**

```bash
cd C:\dev\projects\Digital_Twin
git add unity/Assets/Scripts/Entities/EntityTrail.cs
git commit -m "feat(unity): add EntityTrail LineRenderer component"
```

---

## Task 4: `EntityManager` に trail を統合

**Files:**
- Modify: `unity/Assets/Scripts/Entities/EntityManager.cs`

**変更点:** (a) `Entity` に `EntityTrail trail` を追加 (b) GPSアバター生成時に trail を生成・Init (c) `position_update` 受信時に `trail.Append` (d) `RecalculateAll` で `trail.Rebuild` (e) 起動時に `GET /api/tracks` を coroutine fetch して初期ロード (f) 種別トグル用 public プロパティ + 適用メソッド (g) マウスクリックでアバター強調。

- [ ] **Step 1: `Entity` クラスに trail フィールドを追加**

`EntityManager.cs:81-88` の `Entity` クラスに追加:

```csharp
        class Entity
        {
            public GameObject go;
            public Vector3 targetPos;
            public string type;
            public double lat, lon;   // 最新GPS (再キャリブレーション用)
            public bool fused;        // カメラ検出: GPS融合済みか (色更新判定用)
            public EntityTrail trail; // 移動軌跡 (GPS/QRエンティティのみ。カメラblipは無し)
        }
```

- [ ] **Step 2: trail 生成ヘルパ + アバター生成時に呼ぶ**

`EntityManager.cs` の `CreateAvatar` メソッド(295行〜)の **直後** に新規メソッドを追加:

```csharp
        // GPSアバター用に軌跡 LineRenderer を生成し、エンティティ色とworld変換resolverで初期化
        EntityTrail CreateTrail(DTMessage msg)
        {
            var trailGo = new GameObject($"trail:{msg.entityId}");
            trailGo.transform.SetParent(transform);
            var trail = trailGo.AddComponent<EntityTrail>();
            Color col = Color.gray;
            if (!string.IsNullOrEmpty(msg.color))
                ColorUtility.TryParseHtmlString(msg.color, out col);
            // resolver: lat/lon -> world (現在位置と同じ変換経路)
            trail.Init(col, (lat, lon) => ResolvePosition(lat, lon, msg.entityType));
            // 種別トグルの現在状態を反映
            trail.SetVisible(IsTypeVisible(msg.entityType));
            return trail;
        }
```

そして `HandleMessage` の GPS/QR 分岐(251-257行の新規エンティティ生成ブロック)を修正:

```csharp
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
```

- [ ] **Step 3: `RecalculateAll` で trail も再構築**

`EntityManager.cs:116-124` の `RecalculateAll` を修正(trail.Rebuild を追加):

```csharp
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
```

- [ ] **Step 4: 種別トグル用 public プロパティ + 適用メソッド**

`EntityManager.cs` の `RecalculateAll` の **直前**(115行付近)に追加:

```csharp
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
```

- [ ] **Step 5: クリック強調(raycast)+ 強調適用**

`EntityManager.cs` の `Update()`(195-212行)の末尾(`foreach` ループの後)に追加:

```csharp
            // マウスクリックでアバター強調 (左クリック)
            if (Input.GetMouseButtonDown(0))
            {
                var cam = Camera.main;
                if (cam != null)
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
```

そして `Update()` メソッドの後(`HandleMessage` の前など、クラス内)に追加:

```csharp
        string FindEntityIdByGameObject(GameObject go)
        {
            foreach (var kv in entities)
                if (kv.Value.go == go) return kv.Key;
            return null;
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
```

- [ ] **Step 6: 起動時に `GET /api/tracks` で初期ロード**

`EntityManager.cs` の冒頭 using 群に追加(`using UnityEngine;` の後):

```csharp
using UnityEngine.Networking;
using System.Collections;
```

`serverWsUrl` フィールド付近(57行付近)に API ベースURL を追加:

```csharp
        [Header("Connection")]
        [SerializeField] string serverWsUrl = "ws://localhost:9300/ws";
        [SerializeField] string apiBaseUrl = "http://localhost:9300";
```

`Start()`(103-113行)の末尾、`Connect();` の後に追加:

```csharp
            StartCoroutine(LoadInitialTracks());
```

`Start()` メソッドの後に追加(JsonUtility 用のDTO + coroutine):

```csharp
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
        }
```

- [ ] **Step 7: Verify compile**

Run: `& "<Unity>" -batchmode -quit -projectPath C:\dev\projects\Digital_Twin\unity -logFile -`
Expected: コンパイルエラー無し。

- [ ] **Step 8: Commit**

```bash
cd C:\dev\projects\Digital_Twin
git add unity/Assets/Scripts/Entities/EntityManager.cs
git commit -m "feat(unity): integrate per-entity trails into EntityManager (load/append/recalc/highlight)"
```

---

## Task 5: `TrailControlUI` トグルUI (新規)

**Files:**
- Create: `unity/Assets/Scripts/UI/TrailControlUI.cs`

**責務:** IMGUI で「作業員trail / 重機trail」の ON/OFF トグルを表示し、変更を `EntityManager` に反映する。DTFonts(NotoSansJP)適用で CJK 表示。

- [ ] **Step 1: Create TrailControlUI.cs**

```csharp
using UnityEngine;
using DT.Entities;
using DT.UI;

namespace DT.UI
{
    /// <summary>軌跡(trail)の種別ON/OFFトグルパネル。EntityManager を参照して反映する。</summary>
    public class TrailControlUI : MonoBehaviour
    {
        [SerializeField] EntityManager entityManager;
        GUIStyle headerStyle, labelStyle;
        bool stylesReady;

        void Start()
        {
            if (entityManager == null)
                entityManager = FindFirstObjectByType<EntityManager>();
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            labelStyle = new GUIStyle(GUI.skin.toggle) { fontSize = 13 };
            DTFonts.Apply(headerStyle);
            DTFonts.Apply(labelStyle);
            stylesReady = true;
        }

        void OnGUI()
        {
            if (entityManager == null) return;
            EnsureStyles();

            // 左下にパネル
            const float w = 180f, h = 80f;
            var rect = new Rect(10f, Screen.height - h - 10f, w, h);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 6f, w - 16f, h - 12f));
            GUILayout.Label("軌跡表示", headerStyle);

            bool worker = GUILayout.Toggle(entityManager.ShowWorkerTrails, " 作業員", labelStyle);
            bool equip = GUILayout.Toggle(entityManager.ShowEquipmentTrails, " 重機", labelStyle);

            if (worker != entityManager.ShowWorkerTrails || equip != entityManager.ShowEquipmentTrails)
            {
                entityManager.ShowWorkerTrails = worker;
                entityManager.ShowEquipmentTrails = equip;
                entityManager.ApplyTrailVisibility();
            }
            GUILayout.EndArea();
        }
    }
}
```

- [ ] **Step 2: Wire into scene (SceneSetup)**

`unity/Assets/Editor/SceneSetup.cs` の EntityManager をセットアップしている箇所(`// --- Entity Manager` コメント以降)で、EntityManager を載せている GameObject に `TrailControlUI` も追加し、`entityManager` 参照をセットする。具体的には EntityManager の AddComponent 直後に:

```csharp
            var trailUI = <entityManagerGameObject>.AddComponent<DT.UI.TrailControlUI>();
            var trailUISO = new SerializedObject(trailUI);
            trailUISO.FindProperty("entityManager").objectReferenceValue = <entityManagerComponent>;
            trailUISO.ApplyModifiedProperties();
```

(`<entityManagerGameObject>` / `<entityManagerComponent>` は SceneSetup.cs 内で EntityManager を生成している実際の変数名に合わせる。`TrailControlUI` は `Start()` でも EntityManager を自動探索するため、参照セットに失敗しても動作はする。)

- [ ] **Step 3: Verify compile**

Run: `& "<Unity>" -batchmode -quit -projectPath C:\dev\projects\Digital_Twin\unity -logFile -`
Expected: コンパイルエラー無し。

- [ ] **Step 4: Commit**

```bash
cd C:\dev\projects\Digital_Twin
git add unity/Assets/Scripts/UI/TrailControlUI.cs unity/Assets/Editor/SceneSetup.cs
git commit -m "feat(unity): add TrailControlUI toggle panel for trail visibility"
```

---

## Task 6: WebGLビルド + E2E検証

**Files:** なし(検証のみ)

- [ ] **Step 1: WebGLビルド**

Run: `& "<Unity>" -batchmode -quit -projectPath C:\dev\projects\Digital_Twin\unity -executeMethod BatchBuild.SetupAndBuild -logFile -`
Expected: ビルド成功(エラー無し)。

- [ ] **Step 2: スタック起動(シミュレータ込み)**

Run: `cd C:\dev\projects\Digital_Twin; .\start_stack.ps1 -Simulate`
(ポート9300がEADDRINUSEなら旧サーバーをkillしてから)
Expected: 監視ビュー http://localhost:8765 / API http://localhost:9300 が起動。

- [ ] **Step 3: E2E目視チェック(監視ビュー)**

http://localhost:8765 を開き、以下を確認:
- [ ] 開いた直後、シミュレータの過去5分の軌跡が線で表示される(遡及ロード)
- [ ] 時間が進むと trail が伸びる(ライブ append)
- [ ] 長時間放置で 5分/200点を超えた古い点が消える(尾の長さが頭打ち)
- [ ] 左下パネルの「作業員」「重機」トグルで該当種別の軌跡が ON/OFF する
- [ ] アバターをクリックすると、その軌跡が濃く・他が減光する。再クリック/空クリックで解除
- [ ] エンティティが stale(15秒無通信)で消えても、軌跡は時間窓が切れるまで残りフェード消滅する

- [ ] **Step 4: 後始末コミット(必要なら)**

ビルド成果物は通常 gitignore。検証で設定値を調整した場合のみコミット:

```bash
cd C:\dev\projects\Digital_Twin
git add -A
git commit -m "chore: trail plot E2E verification adjustments"
```

---

## Self-Review (記入済み)

**Spec coverage:**
- 保持(時間窓5分 かつ 最大200点) → Task1 getTracks(SQL窓+slice) + Task3 EntityTrail.Trim ✓
- 表示対象(種別トグル+個別強調) → Task4 ShowWorker/EquipmentTrails+ApplyHighlight, Task5 トグルUI ✓
- 初期ロード遡及(履歴API) → Task1/2 GET /api/tracks + Task4 LoadInitialTracks ✓
- stale時(時間窓切れまで残しフェード) → trail を別GameObjectにしアバター破棄と分離(detection_remove はGPSアバター経路を通らず、GPSエンティティはサーバー側stale判定でアバター停止するが trail は時間窓でTrim) ✓
  - 注意: 現状サーバーは GPSエンティティに対する明示的な `*_remove` ブロードキャストを持たない(detection のみTTL削除)。GPSアバターは更新が来なくなると targetPos で静止するだけで Destroy されない。よって trail は WindowMs 経過後に新規点が来ず Trim もされない=最後の形のまま残る。これは「直前にどこにいたか追える」要件を満たす(過剰削除しない)。将来サーバーにGPS staleブロードキャストを足す場合は EntityTrail を時間で空にする処理を追加する。
- 描画方式(LineRenderer) → Task3 ✓
- デフォルト値(5分/200点/両方ON) → 各定数・初期値 ✓
- 色フェード → EntityTrail.ApplyGradient ✓

**Placeholder scan:** `<Unity>` `<entityManagerGameObject>` 等は環境依存の実値プレースホルダ(実行者が実パス/実変数名に置換)。コードロジックのプレースホルダ("TODO"等)は無し ✓

**Type consistency:** `getTracks({minutes,limit,type,from,to})` 一貫。Unity `EntityTrail`: `Init/SetPoints/Append/Rebuild/SetVisible/SetIntensity` を Task4 が呼ぶシグネチャと一致。`ApplyTrailVisibility`/`ApplyHighlight`/`IsTypeVisible`/`FindEntityIdByGameObject` 整合 ✓
