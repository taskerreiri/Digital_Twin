"""
カメラHomographyキャリブツール。
カメラ画像上で地面の既知点をクリック→world座標を入力→Homography算出→cameras.jsonに保存。

GUIモード:  python calibrate_camera.py <camera_id> <image>
CLIモード:  python calibrate_camera.py <camera_id> --points "u,v,X,Z; u,v,X,Z; ..."

worldのX,Z は Unity座標 (landmarks.json や点群メッシュから取得)。地面上の4点以上。
"""
import sys
import os
import json

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "vision"))
from homography import compute_homography, reprojection_error

CAMERAS_JSON = os.path.join(os.path.dirname(__file__), "..", "server", "cameras.json")


def save_homography(camera_id, H):
    with open(CAMERAS_JSON, "r", encoding="utf-8") as f:
        data = json.load(f)
    found = False
    for c in data.get("cameras", []):
        if c["cameraId"] == camera_id:
            c["homography"] = H
            found = True
            break
    if not found:
        print(f"camera '{camera_id}' not in cameras.json")
        return False
    with open(CAMERAS_JSON, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print(f"saved homography for {camera_id}")
    return True


def parse_points_cli(s):
    img, wld = [], []
    for part in s.split(";"):
        part = part.strip()
        if not part:
            continue
        u, v, X, Z = (float(x) for x in part.split(","))
        img.append((u, v))
        wld.append((X, Z))
    return img, wld


def gui_calibrate(camera_id, image_path):
    import cv2
    img_pts, wld_pts = [], []
    img = cv2.imread(image_path)
    if img is None:
        print(f"cannot read {image_path}")
        return
    clone = img.copy()

    def on_click(event, x, y, flags, param):
        if event == cv2.EVENT_LBUTTONDOWN:
            print(f"clicked image ({x},{y}). Enter world X,Z in terminal:")
            try:
                line = input("  X,Z = ")
                X, Z = (float(v) for v in line.split(","))
                img_pts.append((x, y))
                wld_pts.append((X, Z))
                cv2.circle(clone, (x, y), 6, (0, 255, 0), -1)
                cv2.putText(clone, f"({X},{Z})", (x + 8, y),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 1)
                cv2.imshow("calibrate", clone)
                print(f"  point {len(img_pts)} added")
            except Exception as e:
                print(f"  skip: {e}")

    cv2.namedWindow("calibrate")
    cv2.setMouseCallback("calibrate", on_click)
    cv2.imshow("calibrate", clone)
    print("地面の既知点をクリックし、ターミナルにworld X,Zを入力。4点以上。終わったら画像上で 'q'。")
    while True:
        if cv2.waitKey(20) & 0xFF == ord("q"):
            break
    cv2.destroyAllWindows()

    finish(camera_id, img_pts, wld_pts)


def finish(camera_id, img_pts, wld_pts):
    if len(img_pts) < 4:
        print(f"need >=4 points, got {len(img_pts)}")
        return
    H = compute_homography(img_pts, wld_pts)
    if H is None:
        print("homography computation failed")
        return
    errs = reprojection_error(H, img_pts, wld_pts)
    rms = (sum(e * e for e in errs) / len(errs)) ** 0.5
    print(f"RMS reprojection error: {rms:.2f} m")
    print(f"per-point errors: {[round(e,2) for e in errs]}")
    save_homography(camera_id, H)


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    camera_id = sys.argv[1]
    if sys.argv[2] == "--points":
        img_pts, wld_pts = parse_points_cli(sys.argv[3])
        finish(camera_id, img_pts, wld_pts)
    else:
        gui_calibrate(camera_id, sys.argv[2])


if __name__ == "__main__":
    main()
