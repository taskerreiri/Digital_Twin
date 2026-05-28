"""
複数カメラマネージャ。cameras.json の有効カメラを優先度別サンプリングで並行処理する。
各カメラを独立スレッドのワーカーで回し、動体検知ゲート→YOLO→homography→POSTする。

優先度(priority): 1=高頻度, 2=標準, 3=低頻度。fpsを優先度でスケール。
画像ソース(imageSource)があればRTSPの代わりにそれを使う(デモ/テスト用)。

使い方:
  python manager.py                 # cameras.jsonの全有効カメラ
  python manager.py --cameras cam_01,cam_02
  python manager.py --duration 20   # 20秒で停止
"""
import argparse
import json
import os
import sys
import threading
import time

sys.path.insert(0, os.path.dirname(__file__))
from detect import detect
from homography import apply_homography, bbox_foot
from motion import MotionGate
import urllib.request

SERVER = os.environ.get("DT_SERVER", "http://localhost:9300")
TOKEN = os.environ.get("DT_TOKEN", "dt-poc-token")
CAMERAS_JSON = os.path.join(os.path.dirname(__file__), "..", "server", "cameras.json")

# 優先度 → ベースfps
PRIORITY_FPS = {1: 2.0, 2: 1.0, 3: 0.5}

_stop = threading.Event()
_stats_lock = threading.Lock()
_stats = {}


def post_detections(camera_id, detections):
    body = json.dumps({"cameraId": camera_id, "detections": detections}).encode("utf-8")
    req = urllib.request.Request(
        f"{SERVER}/api/detection", data=body, method="POST",
        headers={"Content-Type": "application/json", "Authorization": f"Bearer {TOKEN}"},
    )
    try:
        with urllib.request.urlopen(req, timeout=5) as resp:
            return json.loads(resp.read())
    except Exception:
        return None


def process_frame(frame_or_path, camera_id, H):
    raw = detect(frame_or_path)
    out = []
    for i, d in enumerate(raw):
        det = {
            "class": d["class"], "confidence": d["confidence"], "bbox": d["bbox"],
            "trackId": f"{camera_id}_{d['class']}_{i}", "source_ai": "yolo",
        }
        if H:
            fu, fv = bbox_foot(d["bbox"])
            w = apply_homography(H, fu, fv)
            if w:
                det["worldX"], det["worldZ"] = round(w[0], 2), round(w[1], 2)
        out.append(det)
    if out:
        post_detections(camera_id, out)
    return len(out)


def camera_worker(cam):
    cam_id = cam["cameraId"]
    H = cam.get("homography")
    priority = cam.get("priority", 2)
    fps = PRIORITY_FPS.get(priority, 1.0)
    interval = 1.0 / fps
    src = cam.get("imageSource") or cam.get("rtspUrl")
    is_image = bool(cam.get("imageSource"))

    with _stats_lock:
        _stats[cam_id] = {"processed": 0, "gated": 0, "detections": 0}

    if not src:
        print(f"[{cam_id}] no source (rtspUrl/imageSource empty) - skipping")
        return

    if is_image:
        # 静止画ソース(デモ): 動きが無いので毎回処理する(ゲートはスキップ)
        while not _stop.is_set():
            n = process_frame(src, cam_id, H)
            with _stats_lock:
                _stats[cam_id]["processed"] += 1
                _stats[cam_id]["detections"] += n
            _stop.wait(interval)
        return

    # 動画/RTSPソース
    import cv2
    cap = cv2.VideoCapture(src)
    if not cap.isOpened():
        print(f"[{cam_id}] cannot open {src}")
        return
    gate = MotionGate()
    last = 0
    while not _stop.is_set():
        ok, frame = cap.read()
        if not ok:
            # ループ再生(RTSPは通常continuousだが動画ファイルは末尾でループ)
            cap.set(cv2.CAP_PROP_POS_FRAMES, 0)
            continue
        now = time.time()
        if now - last < interval:
            continue
        last = now
        motion, _ = gate.has_motion(frame)
        if not motion:
            with _stats_lock:
                _stats[cam_id]["gated"] += 1
            continue
        n = process_frame(frame, cam_id, H)
        with _stats_lock:
            _stats[cam_id]["processed"] += 1
            _stats[cam_id]["detections"] += n
    cap.release()


def load_cameras(filter_ids=None):
    with open(CAMERAS_JSON, "r", encoding="utf-8") as f:
        data = json.load(f)
    cams = [c for c in data.get("cameras", []) if c.get("enabled", True)]
    if filter_ids:
        cams = [c for c in cams if c["cameraId"] in filter_ids]
    return cams


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cameras", help="カンマ区切りカメラID (省略時は全有効カメラ)")
    ap.add_argument("--duration", type=float, default=0, help="秒数で停止 (0=無限)")
    args = ap.parse_args()

    filter_ids = args.cameras.split(",") if args.cameras else None
    cams = load_cameras(filter_ids)
    if not cams:
        print("no enabled cameras")
        sys.exit(1)

    print(f"=== Camera Manager: {len(cams)} cameras ===")
    for c in cams:
        pri = c.get("priority", 2)
        print(f"  {c['cameraId']} ({c['label']}) priority={pri} "
              f"fps={PRIORITY_FPS.get(pri,1.0)} "
              f"H={'set' if c.get('homography') else 'none'}")

    threads = []
    for c in cams:
        t = threading.Thread(target=camera_worker, args=(c,), daemon=True)
        t.start()
        threads.append(t)

    try:
        if args.duration > 0:
            time.sleep(args.duration)
            _stop.set()
        else:
            while True:
                time.sleep(5)
                with _stats_lock:
                    summary = ", ".join(
                        f"{k}:{v['processed']}f/{v['detections']}d/{v['gated']}g"
                        for k, v in _stats.items())
                print(f"[stats] {summary}")
    except KeyboardInterrupt:
        _stop.set()

    for t in threads:
        t.join(timeout=2)

    print("\n=== Final stats ===")
    with _stats_lock:
        for k, v in _stats.items():
            print(f"  {k}: processed={v['processed']}, detections={v['detections']}, gated={v['gated']}")


if __name__ == "__main__":
    main()
