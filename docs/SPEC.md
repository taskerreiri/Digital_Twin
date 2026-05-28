# Digital Twin リアルタイム多エンティティ追跡 SPEC

作成日: 2026-05-28 / 対象: PoC (Phase 1)

## 1. 背景と目的

### 現状 (実装済み)
既存の Digital_Twin は **単一ユーザー型** GPS可視化:
- Unity WebGL が自端末のブラウザGeolocationを読み、自分1人の位置を表示
- `GPSCalibrator` で GPS(WGS84) → Unity座標(XZ平面) の2点アフィン変換
- `FacilityBuilder` で 21エリアを手続き生成 (500×350m)
- Mission Bridge に iframe 埋め込み (postMessage連携)

### 目標 (本SPECの対象)
**多エンティティ監視型** デジタルツインへ転換:
- 多数の作業員・重機・敷材/製品の位置を1つの監視ビューに集約表示
- 各エンティティがリアルタイムに3Dシーン上を移動/出現

### スコープ転換の本質
```
[旧] 1端末 = 自分の位置を表示する参加者
[新] N端末 → サーバー集約 → 1つのUnity監視ビューが全員を表示
```
Unityの役割が「参加者」から「監視者」へ変わる。GPS変換ロジック (`GPSToUnity`) は各エンティティに対して再利用する。

## 2. ゴール / 非ゴール (PoC)

### ゴール
- 作業員PWAが屋外GPS位置を送信 → 監視ビューにアバター移動 (準リアルタイム, 数秒遅延可)
- 作業員PWAが屋内QRスキャン → ゾーン単位で位置反映
- 重機GPS (PoCではシミュレート) → 重機アバター移動
- 敷材/製品を設置時登録 → マーカー出現
- 複数エンティティの同時表示
- PWAオフライン時はバッファ、オンライン復帰時に一括同期

### 非ゴール (後続フェーズ)
- 監視カメラAI解析 (Phase 2)
- 現場メンバーへの情報配信 (Phase 3)
- cm級高精度屋内測位 (UWB等)
- 認証/権限管理の作り込み (PoCは簡易トークン)

## 3. アーキテクチャ

```
[作業員PWA × N]                      [重機GPS機器 × N]
 ├ 屋外: Geolocation API (連続)        └ HTTP直接送信 (PoCはシミュレータ)
 ├ 屋内: QRスキャン → ゾーンID
 ├ IndexedDB (オフライン蓄積)
 └ オンライン時 POST /api/position
        │                    │
        └────────┬───────────┘
                 ↓
   ┌─────────────────────────────────────┐
   │  DT Server (Node.js + Express + ws) │
   │  ├ POST /api/position (収集, batch可) │
   │  ├ POST /api/material (設置登録)      │
   │  ├ GET  /api/entities (現在状態)      │
   │  ├ GET  /api/zones (ゾーン定義)       │
   │  ├ SQLite (位置履歴 + エンティティ登録)│
   │  └ WS /ws (差分ブロードキャスト)       │
   └─────────────────────────────────────┘
                 ↓ WebSocket
   ┌─────────────────────────────────────┐
   │  Unity WebGL 監視ビュー (既存拡張)     │
   │  ├ EntityManager (新規: WS購読+生成)   │
   │  ├ GPSCalibrator (再利用: 座標変換)    │
   │  ├ 点群メッシュ (別パイプラインで構築)  │
   │  └ FacilityLabels (再利用)            │
   └─────────────────────────────────────┘
                 ↓
        [監視PC / Mission Bridge タイル]
```

### 技術選定理由
- **サーバー= Node.js**: Mission Bridge が既にNode (mission-api.js port 9223)。`ws`ライブラリでWebSocket容易。エコシステム統一。
- **DB= SQLite**: 別サーバー不要。ユーザーの persist-context パターン (SQLite+FTS5) と整合。
- **座標変換= Unity側で実施**: 既存 `GPSCalibrator.GPSToUnity()` を各エンティティに適用。サーバーはジオリファレンス非依存に保つ (生lat/lonを保持)。

## 4. データスキーマ

### 位置イベント (PWA/重機 → サーバー)
```json
{
  "entityId": "worker_001",
  "entityType": "worker",        // worker | equipment | material
  "source": "gps",               // gps | qr_zone
  "lat": 35.681234,              // source=gps時
  "lon": 139.767123,
  "alt": 45.5,
  "accuracy": 3.2,
  "zoneId": "building_3",        // source=qr_zone時
  "timestamp": 1716547890123,
  "meta": {}                     // 任意の付加情報
}
```
- バッチ送信時は `{"events": [ ... ]}` 形式 (オフライン同期用)

### エンティティ登録 (サーバー内部状態)
```json
{
  "entityId": "worker_001",
  "type": "worker",
  "displayName": "作業員A",
  "color": "#4A9EFF",
  "lastSeen": 1716547890123,
  "lastPosition": { "lat": ..., "lon": ..., "zoneId": null }
}
```

### ゾーン定義 (QR用, サーバー保持)
```json
{
  "zoneId": "building_3",
  "label": "第3棟",
  "repLat": 35.6815,             // 代表座標 (ゾーン中心)
  "repLon": 139.7672,
  "indoor": true
}
```
- QRコードは `zoneId` を符号化。PWAスキャン → `source=qr_zone` で送信。

### WebSocket 配信メッセージ (サーバー → Unity)
```json
{
  "type": "position_update",
  "entityId": "worker_001",
  "entityType": "worker",
  "displayName": "作業員A",
  "color": "#4A9EFF",
  "lat": 35.681234, "lon": 139.767123,   // gps時
  "zoneId": null,                         // qr_zone時はlat/lonの代わり
  "timestamp": 1716547890123
}
```
- 接続直後は全エンティティの現在状態をスナップショット送信、以降は差分。

## 5. API設計

| メソッド | パス | 用途 |
|---------|------|------|
| POST | `/api/position` | 位置イベント送信 (単発/バッチ)。PWA・重機共通 |
| POST | `/api/material` | 敷材/製品の設置登録 (位置+種別) |
| GET | `/api/entities` | 全エンティティ現在状態 (監視ビュー初期化用) |
| GET | `/api/zones` | ゾーン定義一覧 (PWAのQR照合用) |
| WS | `/ws` | リアルタイム位置配信 (Unity購読) |

- 認証: PoCは固定トークン (`Authorization: Bearer <token>`)。本番でJWT等に置換。
- CORS: Unity WebGL と PWA からのアクセスを許可。

## 6. 座標系とジオリファレンス

### 課題
点群メッシュ/Unityシーンはローカル座標(m)。RCPの GeoReference は Origin=(0,0,0) で **未設定** → 生の点群は任意ローカル座標。GPS(緯度経度)との対応付けが必要。

### 解決策 (既存 GPSCalibrator を活用)
1. 敷地内の **既知点を2点以上** 実測 (例: 正門隅・建屋角)
   - 各点で GPS座標 と Unityシーン上の対応座標を取得
2. `GPSCalibrator.AddCalibrationPoint(name, lat, lon, unityPos)` で登録
3. 以降、全エンティティの lat/lon を `GPSToUnity()` で変換
4. QRゾーンは `zoneId` → ゾーン定義の repLat/repLon → `GPSToUnity()` (または事前計算したUnity座標)

### 重機高精度化の余地
重機GPSがRTK対応なら同じ変換式で cm級も扱える (変換ロジック変更不要)。

## 7. 屋内QRゾーンチェックイン

- 各エリア入口に `zoneId` を符号化したQRを掲示
- PWAでスキャン → `source=qr_zone, zoneId` を送信
- 監視ビューはゾーン代表座標にアバターを配置 (精度はゾーン単位)
- 屋外GPS↔屋内QRの遷移: 最新イベントの source で表示位置を切替

## 8. 敷材/製品の設置登録

- PWAに「設置登録」フォーム: 種別選択 + 現在地(GPS or QRゾーン) + 任意メモ
- POST `/api/material` → entityType=material で永続化
- 監視ビューに静的マーカー表示 (移動しない)
- 撤去操作で非表示化 (PoCは任意)

## 9. PoC完了条件

1. スマホでPWAを開き屋外を歩く → 監視ビューのアバターが追従移動する
2. 建屋入口のQRをスキャン → アバターがそのゾーンに反映される
3. 重機シミュレータのGPSフィード → 重機アバターが移動する
4. PWAで敷材を設置登録 → 監視ビューにマーカーが出現する
5. 複数エンティティ(作業員2+重機1+敷材1)が同時表示される
6. PWAを機内モードにして移動 → オンライン復帰時に軌跡がまとめて反映される

## 10. 再利用する既存コード

| 既存ファイル | 再利用内容 |
|------------|-----------|
| `unity/Assets/Scripts/GPS/GPSCalibrator.cs` | `GPSToUnity()` を全エンティティに適用 (変更不要) |
| `unity/Assets/Scripts/GPS/WebGLGPSProvider.cs` | PWA側Geolocation実装の参考 (jslib CSV形式) |
| `unity/Assets/Scripts/Facility/FacilityLabels.cs` | エンティティ名ラベル表示に転用 |
| `unity/Assets/Scripts/Facility/FacilityBuilder.cs` | 点群メッシュ導入までの暫定地形。ゾーン定義の座標源 |
| `unity/Assets/WebGLTemplates/MissionBridge/index.html` | 監視ビューのMission Bridge埋め込み |
| `serve-webgl.py` | 監視ビューのローカル配信 (port 8765) |
| 点群パイプライン (`点群検証/output/pipeline/`) | 地形メッシュ生成 (E57到着後) |

## 11. 新規実装するもの

| 新規 | 場所 | 内容 |
|------|------|------|
| DT Server | `server/` (新規) | Node.js + Express + ws + SQLite |
| Worker PWA | `pwa/` (新規) | Geolocation + QRスキャン + IndexedDB同期 |
| EntityManager | `unity/Assets/Scripts/Entities/` | WS購読 → アバター生成/移動/プール |
| EquipmentSimulator | `server/` または `tools/` | 重機GPSフィードのモック |
| ゾーン定義データ | `docs/zones.json` | QRゾーンの代表座標 |

## 12. 実装フェーズ (PoC内)

1. **DT Server骨格**: API + WebSocket + SQLite、`/api/position`と`/ws`が動く
2. **EquipmentSimulator**: 偽GPSフィードでサーバーへ送信 (E2E疎通確認)
3. **Unity EntityManager**: WS購読 → アバター複数表示 (シミュレータで検証)
4. **Worker PWA**: Geolocation送信 + オフラインバッファ
5. **QRゾーン**: ゾーン定義 + PWAスキャン + 監視ビュー反映
6. **敷材登録**: PWA登録フォーム + マーカー表示
7. **キャリブレーション**: 実測2点でジオリファレンス確定 + 統合動作確認

## 13. 検証方法

- **サーバー単体**: curl で POST /api/position → GET /api/entities に反映、WSクライアントで配信確認
- **シミュレータ**: 円軌道GPSフィード → Unity上で円移動を目視
- **PWA**: Chrome DevTools の Sensors でGPSエミュレート → 移動確認。Network throttling でオフライン同期確認
- **統合**: 実スマホ + Tailscale経由でサーバー接続 (既存のリモートアクセス基盤を流用)
- **座標精度**: 既知点でアバター位置と実測位置の誤差を測定 (屋外GPS 2-3m目標)
