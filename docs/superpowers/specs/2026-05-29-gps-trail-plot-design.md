# GPS 軌跡プロット (ライブtrail) 設計

- 日付: 2026-05-29
- 対象: Digital_Twin 監視ビュー(Unity WebGL, 8765) + 位置集約サーバー(Node, 9300)
- ステータス: 設計承認済み (ライブtrail スコープ)

## 目的

`positions` テーブルに蓄積済みの位置ログを、各エンティティ(作業員・重機)の
移動軌跡として監視ビューに線で可視化する。現状は「リアルタイム現在位置」のみで、
過去ログは溜まっているが描画されていない。

主目的は**両方**(ライブ追従trail + 将来の履歴ビューア)だが、本specは
**ライブtrail に絞る**。履歴ビューア(任意期間の遡及・再生)は将来拡張とし、
サーバーAPI に時間範囲パラメータの受け口だけ残して余地を確保する。

## 要件 (確定事項)

| 論点 | 決定 |
|---|---|
| 主目的 | 両方(今回はライブtrail、履歴ビューアは将来拡張の余地のみ) |
| 保持範囲 | 時間窓 **かつ** 点数上限(デフォルト 直近5分 かつ 最大200点) |
| 表示対象 | 種別トグル(作業員/重機 ON/OFF)+ アバタークリックで個別強調 |
| 初期ロード | 遡及する(履歴取得API を追加。後から開いた監視者にも直近5分が見える) |
| stale時 | GPSエンティティはクライアント側で明示削除されない(サーバーがGPS用の remove をブロードキャストしないため)。更新が止まると trail は最後の形のまま残り、グラデーションでフェード表示される。**時間窓切れで点0→trail破棄するパスは将来のクライアント側stale実装時に有効化**(現状GPSでは未到達)。「直前にどこにいたか追える」要件は満たす。カメラ検出blipは従来どおり detection_remove で trail ごと破棄 |
| デフォルト | 時間窓5分 / 最大200点 / トグル初期=両方ON(定数で調整可能) |

## 描画方式

`LineRenderer` を採用。worldスペースに点列を渡すだけで、線幅・色グラデーション
(先頭濃→末尾薄でフェード)が容易。エンティティごとに1本持たせる。
Built-in RP・WebGL と相性が良く、既存のアバター生成に組み込みやすい。

(不採用: 動的Mesh生成=保守コスト過剰でYAGNI、GL.Lines=太さ/見た目の制御が弱くWebGLで扱いづらい)

## コンポーネント設計

### 1. データ供給 (server)

**`server/db.js` に `getTracks({minutes, limit, type})` を追加**
- `positions` から時間窓内(`timestamp >= now - minutes*60s`)の点を
  `entity_id, timestamp` 順で取得し、エンティティごとに最新 `limit` 点へ間引き。
- `type` 指定時はその種別(worker/equipment)のみ。
- 返却は生 `lat / lon / timestamp` の配列(エンティティごとにグループ化)。
  **座標変換はしない** — Unity側の既存 `GPSCalibrator` に委ねる(現在位置描画と同じ経路)。
- 数値のみ返すため Unity `JsonUtility` の null 解析不可問題を回避。

**`server/server.js` に `GET /api/tracks` を追加**
- クエリ: `?minutes=5&limit=200&type=`(省略時デフォルト適用)
- 初期ロードで全エンティティ分を1回で返す。
- 将来の履歴ビューア用に `from` / `to`(エポックms or ISO)パラメータの**受け口だけ用意**。
  本spec では `minutes` のみ実装し、`from/to` は未実装(パラメータ解釈の枠だけ残す)。
- レスポンス例:
  ```json
  { "tracks": [
      { "entityId": "worker_01", "entityType": "worker", "color": "#33cc66",
        "points": [ {"lat":35.0,"lon":139.0,"timestamp":1700000000000}, ... ] }
  ] }
  ```

### 2. 描画 (Unity `EntityManager` + 新規 `EntityTrail`)

**`EntityTrail`(LineRenderer保持コンポーネント)を新規追加**
- 各アバターに付与。
- 起動時に `GET /api/tracks?minutes=5&limit=200` を取得 → 各点を `GPSCalibrator` で
  world座標へ変換 → LineRenderer の positions にセット。
- 以降は既存 WS `position_update` 受信のたびに点を1つ append。
- **トリミング**: 直近5分(各点に timestamp 保持)**かつ** 最大200点 を超えたら
  先頭(古い側)から削除。
- 色はエンティティ色を流用、末尾を透明側へフェード(LineRenderer の color gradient)。
- マテリアルは Built-in RP 対応(`Standard` か `Sprites/Default` 系。`new Material(null)` 禁止)。

**`EntityManager` の変更**
- アバター生成時に `EntityTrail` を生成・初期化。
- アバター破棄(stale)時は trail を即破棄せず、時間窓が切れて点が0になったら破棄。

### 3. UI (IMGUI, 既存パターン)

- 監視ビューのパネルに以下を追加:
  - 「作業員trail ON/OFF」トグル
  - 「重機trail ON/OFF」トグル
- アバターをクリック(マウスraycast)で**そのエンティティを強調**(他エンティティのtrailを減光)。
  再クリックまたは何もない所クリックで解除。
- DTFonts(NotoSansJP)を適用済みパネルに統合(CJK表示)。

### 4. データフロー

```
[初期ロード]
  監視ビュー起動
    → GET /api/tracks (server: getTracks → positions SELECT)
    → 各点 lat/lon → GPSCalibrator → world
    → EntityTrail.LineRenderer 一括セット

[ライブ追従]
  PWA/シミュレータ → POST /api/position → positions INSERT + WS position_update 配信
    → EntityManager 受信 → アバター移動(既存) + EntityTrail.append(新規)
    → 時間窓5分/最大200点でトリム

[stale]
  15秒無通信 → server stale判定 → アバター削除(既存)
    → trail は残存、時間窓切れで点0 → trail破棄
```

## エラー処理

- `GET /api/tracks` 失敗時: trail 初期ロードをスキップし、WS append のみで構築(画面は開く)。
- キャリブレーション未確立時: world変換できないため trail 描画を保留(現在位置描画と同じ挙動に追従)。
- 点が1点以下のエンティティ: LineRenderer を非表示(線にならない)。

## テスト

- **server**: `getTracks` 単体(時間窓フィルタ・点数間引き・type絞り込み)。`GET /api/tracks` 疎通。
- **E2E**: シミュレータで複数エンティティを動かし、
  1. 初期ロードで過去5分の軌跡が出る(ビュー開き直しで遡及される)
  2. ライブ追従で trail が伸びる
  3. 200点/5分超でトリムされる
  4. トグルで種別ON/OFF が効く
  5. アバタークリックで個別強調
  6. stale後も trail が時間窓切れまで残りフェード消滅
- 既存の `test_ws.js` / シミュレータ E2E パターンを踏襲。

## スコープ外 (将来拡張)

- 履歴ビューア(任意期間の遡及・タイムライン再生・スクラブ)。`/api/tracks` の `from/to` で接続予定。
- PWA レーダーマップへの軌跡表示。
- 軌跡の永続エクスポート(CSV/GeoJSON)。
