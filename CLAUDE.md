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

## Current State (2026-05-24)

- 初期セットアップ完了、GPS基盤スクリプト実装済み
- IMGUI UIに切替、WebGLバッチビルド対応
- Mission Bridge連携: WebGL GPSプロバイダ経由で座標受信
- ヤードマップ抽出済み
- 次のステップ: 実GPS連携テスト、Mission Bridge双方向通信
