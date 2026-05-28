# ジオリファレンス手順 (ランドマーク方式)

スマホGPS実測値とUnityシーン座標を対応付け、全エンティティを正しい位置に表示する。

## 仕組み

```
作業員がランドマーク地点に立つ
  → PWA「基準点記録」(10秒平均GPS)
  → POST /api/calibration {landmarkId, lat, lon}
  → サーバー保存
  → Unity CalibrationSync が landmarks.json(Unity座標既知) と紐付け
  → GPSCalibrator が最小二乗(Procrustes 2D相似)で変換確定
  → 全エンティティが正しい位置に再配置
```

ランドマークのUnity座標は既知 (FacilityBuilder座標式から算出、`server/landmarks.json`)。
スマホは緯度経度のみ提供し、シーン座標側は事前定義で自動ペアリングされる。

## 運用手順

1. **DTスタック起動**: `.\start_stack.ps1`
2. **作業員がPWAを開く** (http://<server>:8090/、実機はHTTPS必須)
3. 敷地に**広く分散した3点以上**のランドマークを巡回 (精度のため対角に離す):
   - 例: 正門 → 8ヤード → 設備倉庫
   - 各地点で「基準点を記録」を押し、10秒間その場で静止
4. 3点記録すると監視ビューに自動反映 (CalibrationSyncが15秒間隔でポーリング)
5. 監視ビューのデバッグパネルに「Calibrated! (N points)」と緑表示されれば完了

## 精度のポイント

- スマホGPS精度は屋外で2〜5m、起動直後や屋内は悪化 → **屋外・空が開けた場所**で記録
- 基準点は**敷地の対角**に離すほど回転・スケール誤差が小さい
- **3点以上**で最小二乗フィット → 1点のノイズが平均化される
- GPSCalibratorログに `RMS=Xm` が出る。これが各点の残差。数mなら良好
- 点群メッシュ(実地形)導入後は `landmarks.json` のUnity座標を実地形に合わせて更新する

## ランドマーク定義の更新

`server/landmarks.json` の unityX/unityZ は現状 FacilityBuilder の概算配置。
実点群メッシュ統合後、各ランドマークの実際のシーン座標に更新すること。

## 関連ファイル

- `server/landmarks.json` — ランドマーク定義 (id, label, unityX, unityZ)
- `server/server.js` — /api/landmarks, /api/calibration (GET/POST)
- `unity/.../GPS/GPSCalibrator.cs` — 最小二乗2D相似変換
- `unity/.../GPS/CalibrationSync.cs` — landmarks+samples取得→ペアリング→適用
- `pwa/app.js` — 基準点記録 (10秒平均GPS送信)
