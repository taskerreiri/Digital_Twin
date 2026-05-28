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

### 次のステップ
- 点群メッシュ (点群検証/output/pipeline) を地形として統合し、landmarks.json のUnity座標を実地形に更新
- Mission Bridge 双方向通信、Phase 2 (カメラAI解析)
