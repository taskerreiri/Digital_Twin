"""
合成地形メッシュFBX生成 (TerrainMeshLoader検証用)。
点群メッシュ風の起伏した頂点カラー付き地形を生成し、
Assets/PointCloud/yard_terrain.fbx として出力する。

blender --background --python gen_synthetic_terrain.py
"""
import bpy
import bmesh
import math
import os
import random

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.join(SCRIPT_DIR, "Assets", "PointCloud")

def clear():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)

def make_terrain():
    # FacilityBuilderと同じ座標域 (X 0..500, Z -350..0) に起伏面を生成
    nx, nz = 60, 42
    size_x, size_z = 500.0, 350.0
    bm = bmesh.new()
    verts = {}
    random.seed(42)
    for iz in range(nz + 1):
        for ix in range(nx + 1):
            x = ix / nx * size_x
            z = -(iz / nz * size_z)
            # 起伏: ゆるやかな波 + 微小ノイズ
            y = (math.sin(x * 0.02) * 2.0 + math.cos(z * 0.03) * 1.5
                 + random.uniform(-0.3, 0.3))
            verts[(ix, iz)] = bm.verts.new((x, y, z))
    bm.verts.ensure_lookup_table()
    for iz in range(nz):
        for ix in range(nx):
            bm.faces.new([
                verts[(ix, iz)], verts[(ix + 1, iz)],
                verts[(ix + 1, iz + 1)], verts[(ix, iz + 1)],
            ])

    mesh = bpy.data.meshes.new("yard_terrain")
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new("yard_terrain", mesh)
    bpy.context.scene.collection.objects.link(obj)

    # 頂点カラー (高さで色分け: 点群RGBの代用)
    color_layer = mesh.color_attributes.new(name="Col", type='BYTE_COLOR', domain='POINT')
    for i, v in enumerate(mesh.vertices):
        h = v.co.y
        t = max(0.0, min(1.0, (h + 3) / 6))
        color_layer.data[i].color = (0.3 + t * 0.5, 0.4 + t * 0.3, 0.3, 1.0)

    # 頂点カラーを表示するマテリアル
    matrl = bpy.data.materials.new("TerrainVC")
    matrl.use_nodes = True
    nodes = matrl.node_tree.nodes
    links = matrl.node_tree.links
    bsdf = nodes["Principled BSDF"]
    vc = nodes.new('ShaderNodeVertexColor')
    vc.layer_name = "Col"
    links.new(vc.outputs['Color'], bsdf.inputs['Base Color'])
    obj.data.materials.append(matrl)
    return obj

def main():
    clear()
    obj = make_terrain()
    os.makedirs(OUT_DIR, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    out = os.path.join(OUT_DIR, "yard_terrain.fbx")
    bpy.ops.export_scene.fbx(
        filepath=out, use_selection=True,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y',
        colors_type='SRGB', bake_anim=False,
    )
    print(f"exported synthetic terrain: {out}")
    print(f"verts={len(obj.data.vertices)}, polys={len(obj.data.polygons)}")

if __name__ == "__main__":
    main()
