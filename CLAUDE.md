# Digital_Twin (DT) - Project Instructions

## Overview
GPS連動型デジタルツイン。Blenderで職場を3Dモデリング→Unityで仮想空間化→GPS座標と連動。

## Stack
- Blender 5.1 (3D modeling)
- Unity LTS (runtime)
- C# (Unity scripts)
- GPS: スマホ内蔵GPS + 手動キャリブレーション

## Directory structure
- `blender/` — .blend ソースファイル
- `unity/` — Unity プロジェクト
- `docs/` — 設計・測量データ

## Conventions
- Blenderモデルは実寸（1 Blender unit = 1m）
- エクスポート形式: FBX または glTF
- Unity座標系: Y-up（Blenderは Z-up なのでエクスポート時に変換）
- GPS座標は WGS84（緯度経度）で統一

## Current State (2026-05-28)

### 多エンティティ リアルタイム追跡 PoC (SPEC: docs/SPEC.md)
単一ユーザー型 → 多エンティティ監視型へ転換。N端末→サーバー集約→1監視ビュー。

- **server/**: Node.js + Express + ws + SQLite 位置集約サーバー (port 9300)
  - REST: /api/position /api/material /api/entities /api/zones /api/health
  - WS: /ws (スナップショット + リアルタイム差分配信)
  - equipment_simulator.js: 重機/作業員GPSフィードのモック
- **pwa/**: 作業員PWA (Geolocation連続送信 + QR/手動ゾーンチェックイン + IndexedDBオフライン同期 + 敷材登録)
  - serve.js (port 8090)。実機はHTTPS必須 (localhost例外)
- **unity/.../Entities/**: EntityManager (WS購読→アバター生成/移動, GPSCalibrator再利用) + EntityLabelRenderer
  - WebSocketPlugin.jslib (WebGL WS橋渡し), 自動再接続付き

### 検証済み
- サーバー REST/WS 疎通 (test_ws.js)、シミュレータE2E
- PWA→サーバー: 実ブラウザ(Playwright)でGPS追跡/ゾーン/敷材登録→DB到達確認 (UTF-8正常)
- Unity スクリプト コンパイル成功 (6000.3.16f1)

### 旧 (単一ユーザーGPS基盤, 流用中)
- GPSProvider/WebGLGPSProvider/MockGPSProvider, GPSCalibrator (2点アフィン変換), FacilityBuilder

### ジオリファレンス (ランドマーク方式, docs/GEOREFERENCE.md)
スマホGPS実測値でGPS↔Unity座標を確定。詳細は docs/GEOREFERENCE.md。
- server/landmarks.json: ランドマークのUnity座標(既知)。/api/landmarks, /api/calibration
- GPSCalibrator: N点最小二乗(Procrustes 2D相似変換)。RMSログ出力
- CalibrationSync: landmarks+samples取得→landmarkIdでペアリング→適用→全エンティティ再配置
- PWA「基準点記録」: 10秒平均GPSを送信
- GPSDebugUIは外部キャリブレーション状態を「Calibrated! (N points)」緑表示
- 検証済み: PWA→サーバー→CalibrationSync→GPSCalibrator→エンティティ再配置のE2E

### 日本語表示
- Resources/NotoSansJP.ttf (SIL OFL) を DTFonts 経由で全IMGUIに適用。WebGLで日本語表示OK

### Phase 2 設計 (docs/SPEC_PHASE2.md)
監視カメラAI解析。60台CCTV+メンバー端末, ハイブリッドAI(ローカルYOLO一次+Claude昇格)。
MVP=物体検出・位置 (person/vehicle/material → Homographyでworld座標 → P1 GPSと融合)。
状態判定/OCRは後続(2.2/2.3)。

### Phase 2 MVP 実装済み (Python 3.12: ultralytics/opencv)
- vision/detect.py: YOLO (COCO person/car/truck/bus → person/vehicle)
- vision/homography.py: 画像px→world地面座標 (cv2.findHomography)
- vision/ingest.py: 画像/動画/RTSP → YOLO → homography → POST /api/detection
- tools/calibrate_camera.py: カメラHomographyキャリブ (GUI/CLI)
- server: cameras.json, /api/cameras, /api/detection, geotransform.js(GPS→world JS版), fusion.js(近接融合)
- EntityManager: detection_update/removeでカメラブリップ描画(融合=緑/匿名=シアン半透明)
- 検証済み: bus.jpg→YOLO(person×3+vehicle)→world→監視ビュー表示、GPS巡回員と融合し「CAM:worker_demo」表示

### Phase 2 スケール対応 実装済み
- vision/motion.py: 動体検知ゲート(フレーム差分)。静止シーンはYOLOスキップ。ingest.py統合
- vision/manager.py: 複数カメラを優先度別サンプリング(priority 1/2/3 → 2/1/0.5fps)で並行処理。cameras.jsonにcam_01〜03定義
- detect.py: OpenVINOエクスポート+推論(DT_YOLO_DEVICE=openvino, DT_OV_DEVICE=gpu)。Intel Arc GPU.0で動作確認、失敗時CPU/PyTorchフォールバック
- 検証済み: motion gate単体PASS, manager 3カメラ優先度比4:1サンプリング, OpenVINO GPU推論

### Phase 2.2 状態判定 / 2.3 OCR (Claude Vision昇格) 実装済み
- vision/escalate.py: Claude Vision (claude-opus-4-7, adaptive thinking, 構造化出力, プロンプトキャッシュ)
  で状態判定(operating/idle/abnormal+混雑度)とOCR。ANTHROPIC_API_KEY未設定時はモック
- vision/scene_monitor.py: カメラ画像を定期解析し /api/scene-analysis に送信
- server: /api/scene-analysis (POST/GET) + WS scene_analysis配信
- Unity: SceneAnalysisRenderer が右上パネルに状態(色分け)/混雑度/OCRを表示
- 検証済み: フレーム→escalate(mock)→サーバー→Unityパネル表示のE2E。実結果はAPIキー設定時
- 注: cv2.imreadは日本語パス不可→np.fromfile+imdecodeで回避

### 点群メッシュ受け入れ準備 実装済み (docs/POINTCLOUD_INTEGRATION.md)
E57到着後すぐ流し込める受け入れ機構。実データ未着のため合成地形で検証済み。
- Editor/TerrainMeshLoader.cs: Assets/PointCloud/のメッシュFBX/OBJを地形配置、頂点カラー
  マテリアル、MeshCollider付与、FacilityBuilderプレースホルダ非表示。SceneSetup統合
- Editor/LandmarkPicker.cs: Sceneビューで実メッシュ上のlandmarkをクリック取得→landmarks.json更新
- Shaders/VertexColor.shader: Built-in RP用頂点カラー描画 (点群RGB表示)
- gen_synthetic_terrain.py: 検証用合成地形FBX生成
- 検証済み: 合成地形FBX→TerrainMeshLoader配置→頂点カラー表示→プレースホルダ非表示
- Assets/PointCloud/ はgitignore (実メッシュは大容量)

### Phase 3 現場配信 実装済み (PWA拡張)
作業員PWAがサーバーWSを購読し、周辺状況をリアルタイム表示。
- WS購読: position_update/material_placed/detection_update/scene_analysis を受信・追跡
- 近接アラート: 自GPS↔周辺エンティティの距離計算。重機20m以内で警告+バイブ(navigator.vibrate)、作業員10m以内で通知
- 俯瞰マップ: canvasレーダー(中心=自分, 橙=重機/青=作業員/緑=敷材/水=カメラ, 20m警告円+100m半径)
- エリア状態: scene_analysisを稼働/停止/異常+混雑度で表示
- 検証済み: 実ブラウザで受信中・周辺5体・重機接近15m/16m警告・レーダー描画
- 注: SWキャッシュ更新時はsw.jsのCACHEバージョンを上げる(古いapp.js配信回避)

### 次のステップ
- E57到着→pipeline→FBX→Assets/PointCloud/配置→LandmarkPicker整合 (手順書通り)
- ANTHROPIC_API_KEY設定でカメラAI解析を実運用化 (現状mock)
- Mission Bridge 双方向通信、実機デバイスでの統合テスト
