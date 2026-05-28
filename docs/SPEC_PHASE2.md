# Phase 2: 監視カメラAI解析 SPEC

作成日: 2026-05-28 / 前提: Phase 1 (GPS多エンティティ追跡) の上に積む

## 1. 背景と目的

Phase 1 は GPS/QR ベースで作業員・重機・敷材を追跡する。
Phase 2 は **構内60台のCCTV + メンバー端末** の映像をAI解析し、
GPSを持たない対象も含めて物体を検出・3Dツインに位置反映する。

GPSの弱点(端末を持たない人/物、屋内精度)をカメラ映像が補完し、
逆にカメラの弱点(死角、個体識別不可)をGPSが補完する**相互補完**が狙い。

## 2. スコープ

### MVP (本SPECの主対象): 物体検出・位置
- カメラ映像から **person / vehicle(重機) / material** を検出
- カメラ画像座標 → ワールド(Unity)座標へ変換し3Dツインに配置
- Phase 1 のGPSエンティティと**融合**(近接でひも付け、重複排除)
- 監視ビューにカメラ由来ブリップを表示(GPS由来と視覚的に区別)

### 非ゴール (後続)
- 状態判定(稼働/停止/異常・混雑度) → Phase 2.2
- OCR(資材タグ・看板・ナンバー) → Phase 2.3
- 個体再識別(Re-ID: 同一人物の追跡) → 将来
- Phase 3 (現場メンバーへの情報配信)

## 3. アーキテクチャ

```
[固定CCTV ×60]──RTSP/ONVIF──┐
[メンバー端末]──HTTP upload──┤
                            ↓
              ┌──────────────────────────────────┐
              │  Frame Ingest (Python)            │
              │  ├ RTSPフレーム取得 (ffmpeg/OpenCV) │
              │  ├ サンプリング (1fps or 動体検知)   │
              │  └ 処理キュー (カメラ別スケジュール)  │
              └──────────────────────────────────┘
                            ↓
              ┌──────────────────────────────────┐
              │  AI推論 (ハイブリッド)              │
              │  ├ ローカル一次: YOLO (person/車両) │
              │  │    速い・無料・全フレーム         │
              │  └ クラウド昇格: Claude Vision      │
              │       低信頼/重要判断/複雑シーンのみ │
              └──────────────────────────────────┘
                            ↓ 検出bbox
              ┌──────────────────────────────────┐
              │  座標変換 (Homography)             │
              │  bbox足元 → 地面ワールド座標(X,Z)   │
              │  カメラ別キャリブ行列で射影          │
              └──────────────────────────────────┘
                            ↓
              [DT Server] POST /api/detection
                ├ カメラ検出を保存
                ├ GPS融合 (近接ひも付け/重複排除)
                └ WS配信 (source=camera)
                            ↓
              [Unity 監視ビュー] カメラブリップ表示
```

### 技術選定理由
- **ローカルYOLO** (ultralytics YOLOv8/11): person/vehicle検出に十分、高速、無料。Intel Arc 140T を OpenVINO で活用可。60台はサンプリング+キューで捌く。
- **Claude Vision 昇格**: YOLOで分類できない/重要シーン/将来のOCR・状態判定のみ。コスト最小化([[llm-routing]] の方針と整合)。
- **Ollama Vision (qwen2.5-vl等)**: 中間層。YOLOより意味理解が要るが Claude を使うほどでない判断に(任意)。
- **Homography**: 固定カメラは地面平面射影で画像→世界座標が確定できる(標準手法)。

## 4. データスキーマ

### カメラレジストリ (server/cameras.json)
```json
{
  "cameraId": "cam_01",
  "label": "第1棟入口",
  "type": "cctv",            // cctv | mobile
  "rtspUrl": "rtsp://...",   // cctv時
  "homography": [9 floats],  // 画像px → 世界(X,Z) の3x3行列。キャリブ後に設定
  "coverageZone": "building_1",
  "enabled": true
}
```

### 検出イベント (Frame Ingest → DT Server)
```json
{
  "cameraId": "cam_01",
  "timestamp": 1779940000000,
  "detections": [
    {
      "class": "person",        // person | vehicle | material
      "confidence": 0.91,
      "bbox": [x, y, w, h],     // 画像ピクセル
      "worldX": 235.4,          // homography適用後 (Unity X)
      "worldZ": -118.2,         // Unity Z
      "trackId": "cam_01_t12",  // カメラ内トラッキングID (任意)
      "source_ai": "yolo"       // yolo | ollama | claude
    }
  ]
}
```

### WS配信メッセージ (追加: source=camera)
Phase 1 の position_update を踏襲しつつ識別子を追加:
```json
{
  "type": "detection_update",
  "entityId": "cam_01_t12",     // カメラトラックID または融合後の統合ID
  "entityType": "person",        // person | vehicle | material
  "source": "camera",
  "cameraId": "cam_01",
  "worldX": 235.4, "worldZ": -118.2,
  "confidence": 0.91,
  "fusedWith": "worker_001",     // GPS融合時、紐付いたGPSエンティティID (任意)
  "timestamp": 1779940000000
}
```

## 5. カメラ→世界座標 変換 (Homography)

固定カメラの最重要設計。各カメラで一度キャリブレーションする。

### 原理
地面を平面と仮定し、画像ピクセル(u,v) → 世界地面(X,Z) の射影変換(homography H, 3x3)を求める。
物体の**bbox足元中心**(地面接地点)に H を適用してワールド座標を得る。

### キャリブ手順 (カメラごと)
1. カメラ画像内で、世界座標が既知の**地面上4点以上**を選ぶ
   - 例: ヤード区画の角、建屋基礎の角、白線交点 — Phase 1の landmarks や点群メッシュから座標取得
2. 各点の画像ピクセル座標と世界(X,Z)のペアを登録
3. `cv2.findHomography()` で H を計算
4. `cameras.json` の homography に保存
5. 検証: 既知点に H を適用し誤差を確認

### ツール
`tools/calibrate_camera.py`: カメラ画像を表示→クリックで画像点指定→世界座標入力→H算出→保存。

### 限界
- 地面平面仮定のため、高所(クレーン上部等)は誤差増。MVPは地面接地物(人・車両)に限定。
- メンバー端末(可動カメラ)は固定Homographyが使えない → 端末GPS+方位での簡易推定 or 位置タグ付き手動アップロードに限定(MVP範囲外)。

## 6. AI推論ハイブリッドルーティング

```
フレーム到着
  ↓
ローカルYOLO 推論 (全フレーム)
  ├ person/vehicle を高信頼(>0.6)検出 → そのまま採用
  ├ 低信頼(0.3〜0.6) or 未分類物体 → Claude Vision に昇格
  └ 重要エリア/アラート対象 → Claude Vision で詳細解析
       (状態判定・OCRはPhase2.2/2.3でここに追加)
```

- ルーティング基準は [[llm-routing]] に準拠(精度許容ならローカル、高品質要否でクラウド)
- Claude呼び出しは**バッチ・サンプリング**でコスト管理(全カメラ常時はしない)
- Claude API利用時は prompt caching を有効化([[claude-api]] スキル参照)

## 7. スケーリング戦略 (60カメラ)

60ストリーム常時高品質推論は非現実的。段階的に捌く:

| 手法 | 内容 |
|------|------|
| **フレームサンプリング** | カメラ毎 1fps (動きがなければさらに低下) |
| **動体検知ゲート** | フレーム差分で動きのあるカメラのみAI推論 |
| **優先度スケジューラ** | 重要エリア(ゲート/作業中ヤード)を高頻度、他は低頻度 |
| **YOLOバッチ推論** | 複数カメラのフレームをバッチGPU推論 |
| **クラウド昇格の絞り込み** | Claudeは全体の数%のフレームのみ |

MVPは**2〜3カメラ**で動作確認 → スケジューラ/キューで台数拡張。

## 8. Phase 1 GPS との融合

カメラ検出は匿名(誰かは不明)。GPSは個体識別済み。両者を近接で融合する。

```
カメラ検出(worldX,Z, class=person)
  ↓
同時刻のGPSエンティティ(worker_*)と距離比較
  ├ 閾値内(<5m)に1体 → 融合: detection.fusedWith = workerId
  │    → 監視ビューは1体として表示(GPS位置を採用、カメラで存在確認)
  └ 近接GPSなし → 匿名ブリップとして表示(GPS未携帯者 or 誤検出)
```

- 融合により「GPS未携帯の作業員/第三者」をカメラだけで可視化できる(安全管理価値)
- 重機: GPS付き重機はカメラ検出と融合、GPSなし重機はカメラのみ

## 9. 新規実装するもの

| 新規 | 場所 | 内容 |
|------|------|------|
| Frame Ingest | `vision/ingest.py` | RTSP/アップロード取得+サンプリング+キュー |
| YOLO推論 | `vision/detect.py` | ultralytics YOLO, person/vehicle検出 |
| クラウド昇格 | `vision/escalate.py` | Claude Vision呼び出し(昇格条件付き) |
| Homography | `vision/homography.py` + `tools/calibrate_camera.py` | 画像→世界変換+キャリブツール |
| カメラレジストリ | `server/cameras.json` | カメラ定義+H行列 |
| 検出API | `server/server.js` 拡張 | POST /api/detection, GET /api/cameras |
| 融合ロジック | `server/fusion.js` | カメラ検出×GPS近接融合 |
| Unity表示 | EntityManager拡張 | source=camera のブリップ描画(GPSと区別) |

## 10. Phase 1 からの再利用

- **DT Server** (server/): /api/detection を既存API群に追加、WS配信基盤を流用
- **EntityManager**: source識別を追加しカメラブリップを描画(アバター生成基盤を流用)
- **GPSCalibrator / landmarks.json**: Homographyキャリブの世界既知点ソースとして活用
- **点群メッシュ** (点群検証/pipeline): カメラキャリブの地面基準・死角確認に活用

## 11. MVP実装フェーズ

1. **カメラレジストリ + 検出API**: cameras.json, POST /api/detection, GET /api/cameras
2. **YOLO推論単体**: 1カメラ(またはサンプル動画)でperson/vehicle検出を確認
3. **Homographyキャリブ**: calibrate_camera.py で1カメラのH算出+検証
4. **Frame Ingest**: RTSP取得→サンプリング→YOLO→検出POST のパイプライン
5. **Unity表示**: カメラブリップを監視ビューに表示(GPSと色/形で区別)
6. **GPS融合**: 近接ひも付け+重複排除
7. **複数カメラ拡張**: スケジューラ/動体検知ゲートで2〜3台同時
8. **クラウド昇格**: 低信頼検出をClaude Visionで再判定(任意)

## 12. 検証方法

- **YOLO単体**: サンプル画像/動画で検出精度・速度を測定(fps, 検出率)
- **Homography**: 既知点での再投影誤差(目標 数十cm〜1m)
- **パイプラインE2E**: テスト動画 → 検出 → サーバー → 監視ビューにブリップ出現
- **融合**: GPSエンティティとカメラ検出が同一地点で1体に融合されること
- **スケール**: 3カメラ同時でフレーム落ち/遅延を測定
- **コスト**: Claude昇格頻度とAPIコストを計測し閾値調整

## 13. 主要リスクと対策

| リスク | 対策 |
|--------|------|
| 60ストリームの処理負荷 | サンプリング+動体検知ゲート+優先度スケジューラ。MVPは少数台 |
| Homographyの地面平面誤差 | 地面接地物に限定。高所はPhase後送り |
| カメラ個体識別不可 | GPS融合で補完。Re-IDは将来 |
| Claude APIコスト増 | 昇格を数%に絞る+prompt caching+バッチ |
| RTSP/ONVIF互換性 | カメラ機種確認後にingest実装。ffmpeg汎用取得を基本 |
| メンバー端末の可動カメラ位置特定 | MVP範囲外。端末GPS+手動タグで将来対応 |
| 点群メッシュ未統合 | 地面基準はlandmarks/概算で開始、メッシュ導入後に精緻化 |

## 14. 前提・確認事項 (実装着手前)

- CCTV 60台の**映像取得方式** (RTSP URL/ONVIF/録画サーバーAPI) の確認
- カメラの**画角・設置位置・地面可視性** (Homography可否)
- ローカル推論機の能力 (Intel Arc 140T + OpenVINO で何ストリーム捌けるか実測)
- Claude API キーとコスト上限
