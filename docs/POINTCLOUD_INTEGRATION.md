# 点群メッシュ統合 手順書

E57点群が届いた後、実地形メッシュを Unity デジタルツインに取り込む手順。
受け入れ機構は実装済み — メッシュをFBX化して所定フォルダに置けば自動配置される。

## 全体フロー

```
E57点群 (提供元/別端末で .rcs → .e57 変換済み)
  ↓ [点群検証/output/pipeline]
scan_inventory → scan_to_ply → merge_and_deduplicate → mesh_reconstruct
  ↓ mesh_lod0/1/2.ply
mesh_to_unity.py (Blender)
  ↓ yard_lod0.fbx 等 (頂点カラー付き)
Assets/PointCloud/ にコピー
  ↓ BatchBuild (シーン再生成)
TerrainMeshLoader が自動配置 + FacilityBuilderプレースホルダを非表示
  ↓
LandmarkPicker で landmark を実メッシュに整合 → landmarks.json更新
  ↓
CalibrationSync が新landmarkで GPS↔Unity 変換を再確定
```

## Step 1: 点群 → メッシュ (E57到着後)

```powershell
cd C:\Users\坂本 正幸\Downloads\点群検証\output\pipeline
.\run_pipeline.ps1 -E57Dir "C:\path\to\e57_files"
# → mesh\mesh_lod0.ply, mesh_lod1.ply, mesh_lod2.ply
```

## Step 2: メッシュ → FBX

```powershell
$blender = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
& $blender --background --python mesh_to_unity.py -- --mesh-dir mesh --group yard
# → unity_export\yard_lod0.fbx 等
```

## Step 3: FBX を Unity に配置

```powershell
# Assets/PointCloud/ を作成しFBXをコピー
mkdir C:\dev\projects\Digital_Twin\unity\Assets\PointCloud -Force
copy unity_export\yard_lod0.fbx C:\dev\projects\Digital_Twin\unity\Assets\PointCloud\
```

FBXインポート設定 (Unity):
- Scale Factor: 1 (パイプラインがworld座標で焼き込み済み)
- Import Cameras/Lights: off
- Normals: Import (頂点カラーは Vertex Color として取り込まれる)

## Step 4: シーン再生成

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.16f1\Editor\Unity.exe" `
  -batchmode -quit -projectPath C:\dev\projects\Digital_Twin\unity `
  -executeMethod BatchBuild.SetupAndBuild
```

`TerrainMeshLoader` が `Assets/PointCloud/` のメッシュを検出して地形配置し、
FacilityBuilderの概算プレースホルダ(箱)を非表示にする。
読み込み対象名: `yard_lod0/1`, `yard_terrain`, `indoor_lod0`, `indoor_terrain`, `terrain` (FBX/OBJ)。

## Step 5: landmark を実メッシュに整合 (重要)

点群メッシュは FacilityBuilder の概算配置と**座標系が異なる**ため、
landmark の Unity座標を実メッシュに合わせ直す必要がある。

1. Unity エディタでシーンを開く (地形メッシュ配置済み)
2. メニュー: **Tools > Digital Twin > Landmark Picker**
3. 各landmark行の "Pick in Scene" → Sceneビューで地形メッシュ上の該当地点をクリック
   - 例: 正門 → 実メッシュの正門位置をクリック
   - 地形メッシュには MeshCollider が自動付与されるのでクリック取得できる
4. 全landmark取得後 "Export landmarks.json"
5. `server/landmarks.json` が更新される

## Step 6: 再キャリブレーション

landmarks.json 更新後、作業員がスマホで各landmark地点の基準点記録をやり直す
(docs/GEOREFERENCE.md 参照)。`CalibrationSync` が新しいlandmark座標で
GPS↔Unity変換を再確定し、全エンティティが実地形に正しく載る。

## 関連ファイル

- `unity/Assets/Editor/TerrainMeshLoader.cs` — 地形メッシュ自動配置
- `unity/Assets/Editor/LandmarkPicker.cs` — landmark整合エディタツール
- `点群検証/output/pipeline/mesh_to_unity.py` — メッシュ→FBX
- `docs/GEOREFERENCE.md` — GPS↔Unityキャリブレーション
- `docs/SPEC.md` / `docs/SPEC_PHASE2.md` — 全体設計

## 注意

- 地形メッシュは大きい(LOD0で数十〜数百MB)。`.gitignore` で `Assets/PointCloud/` を除外推奨
- FacilityBuilderプレースホルダは非表示になるだけで残る(`Facility` オブジェクトをSetActive(false))。
  必要なら参照用に再表示可能
- 屋外(yard)と屋内(indoor)は別メッシュとして配置(SPEC方針: 分離)
