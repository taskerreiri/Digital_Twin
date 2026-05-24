# Digital_Twin (DT)

GPS連動型デジタルツインプロジェクト。職場のフィールドをBlender + Unityで仮想空間に再現する。

## 概要

- **Blender**: 職場環境の3Dモデリング（実寸スケール1:1）
- **Unity**: リアルタイム3D表示 + GPS連動
- **手動GPSキャリブレーション**: 基準点ベースの座標変換（GPS緯度経度 <-> Unity XYZ）

## 技術スタック

- Blender 5.1
- Unity (LTSバージョン)
- C# (Unity スクリプト)
- 通常GPS（スマホ内蔵）

## プロジェクト構成

```
Digital_Twin/
├── blender/          # Blender ソースファイル (.blend)
├── unity/            # Unity プロジェクト
├── docs/             # 設計ドキュメント・測量データ
└── README.md
```

## ロードマップ

1. [ ] 職場の簡易3Dモデル作成（Blender）
2. [ ] Unityプロジェクト作成 + モデルインポート
3. [ ] GPS座標取得機能
4. [ ] 手動キャリブレーション機能（基準点3点以上）
5. [ ] GPS座標 <-> Unity座標 変換
6. [ ] リアルタイム位置表示
