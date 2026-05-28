"""
カメラ画像座標 → Unity world地面座標 の射影変換 (Homography)。
固定カメラは地面を平面と仮定し、画像px(u,v) → world(X,Z) の3x3行列で射影する。
物体のbbox足元中心(地面接地点)に適用してワールド座標を得る。
"""
import numpy as np


def compute_homography(image_points, world_points):
    """
    image_points: [(u,v), ...]  画像ピクセル (4点以上)
    world_points: [(X,Z), ...]  対応するUnity world地面座標
    return: 3x3 homography (list, row-major) または None
    """
    import cv2
    if len(image_points) < 4 or len(image_points) != len(world_points):
        return None
    src = np.array(image_points, dtype=np.float64)
    dst = np.array(world_points, dtype=np.float64)
    H, mask = cv2.findHomography(src, dst, cv2.RANSAC, 5.0)
    if H is None:
        return None
    return H.flatten().tolist()


def apply_homography(H, u, v):
    """3x3 homography(9要素 list/ndarray) を画像点(u,v)に適用 → (X,Z)"""
    H = np.array(H, dtype=np.float64).reshape(3, 3)
    p = np.array([u, v, 1.0])
    w = H @ p
    if abs(w[2]) < 1e-9:
        return None
    return (float(w[0] / w[2]), float(w[1] / w[2]))


def bbox_foot(bbox):
    """bbox [x,y,w,h] の足元中心(地面接地点) を返す"""
    x, y, w, h = bbox
    return (x + w / 2.0, y + h)


def reprojection_error(H, image_points, world_points):
    """キャリブ検証: 各画像点を射影し世界座標との誤差(m)を返す"""
    errs = []
    for (u, v), (X, Z) in zip(image_points, world_points):
        p = apply_homography(H, u, v)
        if p is None:
            errs.append(float('inf'))
            continue
        errs.append(((p[0] - X) ** 2 + (p[1] - Z) ** 2) ** 0.5)
    return errs


if __name__ == "__main__":
    # 自己テスト: 既知の相似変換でhomographyを復元できるか
    # 画像1920x1080の四隅 → world矩形 (100..400, -50..-250)
    img = [(100, 200), (1800, 200), (1800, 1000), (100, 1000)]
    wld = [(120, -60), (380, -60), (400, -240), (110, -240)]
    H = compute_homography(img, wld)
    print("H computed:", H is not None)
    if H:
        errs = reprojection_error(H, img, wld)
        print("reprojection errors (m):", [round(e, 3) for e in errs])
        # 中心点テスト
        c = apply_homography(H, 950, 600)
        print("image center (950,600) -> world:", tuple(round(x, 1) for x in c))
