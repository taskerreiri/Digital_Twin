"""
検出パイプライン: 画像/動画/RTSP → YOLO検出 → Homography(画像→world) → POST /api/detection

使い方:
  python ingest.py --image sample.jpg --camera cam_01
  python ingest.py --video sample.mp4 --camera cam_01 --fps 1
  python ingest.py --rtsp rtsp://... --camera cam_01 --fps 1
"""
import argparse
import json
import os
import sys
import time
import urllib.request

sys.path.insert(0, os.path.dirname(__file__))
from detect import detect
from homography import apply_homography, bbox_foot
from motion import MotionGate

SERVER = os.environ.get("DT_SERVER", "http://localhost:9300")
TOKEN = os.environ.get("DT_TOKEN", "dt-poc-token")


def load_camera(camera_id):
    cams_path = os.path.join(os.path.dirname(__file__), "..", "server", "cameras.json")
    with open(cams_path, "r", encoding="utf-8") as f:
        data = json.load(f)
    for c in data.get("cameras", []):
        if c["cameraId"] == camera_id:
            return c
    return None


def post_detections(camera_id, detections):
    body = json.dumps({"cameraId": camera_id, "detections": detections}).encode("utf-8")
    req = urllib.request.Request(
        f"{SERVER}/api/detection", data=body, method="POST",
        headers={"Content-Type": "application/json", "Authorization": f"Bearer {TOKEN}"},
    )
    try:
        with urllib.request.urlopen(req, timeout=5) as resp:
            return json.loads(resp.read())
    except Exception as e:
        print(f"  POST failed: {e}")
        return None


def process_frame(frame, camera_id, H, frame_idx=0):
    """1フレームを検出→world変換→POST"""
    raw = detect(frame)
    out = []
    for i, d in enumerate(raw):
        det = {
            "class": d["class"],
            "confidence": d["confidence"],
            "bbox": d["bbox"],
            "trackId": f"{camera_id}_{d['class']}_{i}",  # 簡易ID(MVP: トラッキング無し)
            "source_ai": "yolo",
        }
        if H:
            fu, fv = bbox_foot(d["bbox"])
            w = apply_homography(H, fu, fv)
            if w:
                det["worldX"], det["worldZ"] = round(w[0], 2), round(w[1], 2)
        out.append(det)
    if out:
        res = post_detections(camera_id, out)
        print(f"  frame {frame_idx}: {len(out)} detections posted "
              f"({sum(1 for d in out if 'worldX' in d)} with world coords)")
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--camera", required=True)
    ap.add_argument("--image")
    ap.add_argument("--video")
    ap.add_argument("--rtsp")
    ap.add_argument("--fps", type=float, default=1.0, help="処理fps (動画/RTSP)")
    ap.add_argument("--loop", action="store_true", help="画像を繰り返しPOST(デモ用)")
    ap.add_argument("--no-motion-gate", action="store_true", help="動体検知ゲートを無効化")
    args = ap.parse_args()

    cam = load_camera(args.camera)
    if cam is None:
        print(f"camera '{args.camera}' not found in cameras.json")
        sys.exit(1)
    H = cam.get("homography")
    if H is None:
        print(f"WARNING: {args.camera} has no homography - world coords will be omitted")

    if args.image:
        if args.loop:
            print("loop mode: posting every 1s (Ctrl+C to stop)")
            i = 0
            while True:
                process_frame(args.image, args.camera, H, i)
                i += 1
                time.sleep(1)
        else:
            process_frame(args.image, args.camera, H)
    elif args.video or args.rtsp:
        import cv2
        src = args.video or args.rtsp
        cap = cv2.VideoCapture(src)
        if not cap.isOpened():
            print(f"cannot open {src}")
            sys.exit(1)
        interval = 1.0 / args.fps
        last = 0
        idx = 0
        gate = None if args.no_motion_gate else MotionGate()
        gated = 0
        processed = 0
        while True:
            ok, frame = cap.read()
            if not ok:
                break
            now = time.time()
            if now - last >= interval:
                last = now
                # 動体検知ゲート: 動きが無ければYOLOスキップ
                if gate is not None:
                    motion, score = gate.has_motion(frame)
                    if not motion:
                        gated += 1
                        idx += 1
                        continue
                process_frame(frame, args.camera, H, idx)
                processed += 1
                idx += 1
        cap.release()
        if gate is not None:
            total = gated + processed
            print(f"motion gate: processed {processed}/{total} frames, "
                  f"gated {gated} ({gated/max(total,1)*100:.0f}% skipped)")
    else:
        print("specify --image / --video / --rtsp")
        sys.exit(1)


if __name__ == "__main__":
    main()
