"""
シーン監視: カメラ画像を定期的に Claude Vision で状態判定+OCRし、
/api/scene-analysis に送信する。ハイブリッドAIのクラウド昇格部分。

ANTHROPIC_API_KEY 未設定時はモック結果を送信(疎通確認用)。

使い方:
  python scene_monitor.py --camera cam_01 --interval 30
  python scene_monitor.py --all --interval 60   # 全カメラを巡回
"""
import argparse
import json
import os
import sys
import time
import urllib.request

sys.path.insert(0, os.path.dirname(__file__))
from escalate import classify_state, extract_text, has_api_key

SERVER = os.environ.get("DT_SERVER", "http://localhost:9300")
TOKEN = os.environ.get("DT_TOKEN", "dt-poc-token")
CAMERAS_JSON = os.path.join(os.path.dirname(__file__), "..", "server", "cameras.json")


def load_cameras(camera_id=None):
    with open(CAMERAS_JSON, "r", encoding="utf-8") as f:
        data = json.load(f)
    cams = [c for c in data.get("cameras", []) if c.get("enabled", True)]
    if camera_id:
        cams = [c for c in cams if c["cameraId"] == camera_id]
    return cams


def grab_frame(cam):
    """カメラから1フレーム取得 (imageSource静止画 or RTSP)"""
    import cv2
    import numpy as np
    src = cam.get("imageSource") or cam.get("rtspUrl")
    if not src:
        return None
    if cam.get("imageSource"):
        # cv2.imread は非ASCIIパス(日本語等)を読めないため fromfile+imdecode
        try:
            buf = np.fromfile(src, dtype=np.uint8)
            return cv2.imdecode(buf, cv2.IMREAD_COLOR)
        except Exception:
            return None
    cap = cv2.VideoCapture(src)
    ok, frame = cap.read()
    cap.release()
    return frame if ok else None


def post_analysis(camera_id, state, ocr):
    body = json.dumps({
        "cameraId": camera_id,
        "state": state.get("state"),
        "congestion": state.get("congestion"),
        "texts": ocr.get("texts", []),
        "sourceAi": state.get("source_ai", "unknown"),
    }).encode("utf-8")
    req = urllib.request.Request(
        f"{SERVER}/api/scene-analysis", data=body, method="POST",
        headers={"Content-Type": "application/json", "Authorization": f"Bearer {TOKEN}"},
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            return json.loads(resp.read())
    except Exception as e:
        print(f"  POST failed: {e}")
        return None


def analyze_camera(cam):
    cam_id = cam["cameraId"]
    frame = grab_frame(cam)
    if frame is None:
        print(f"  [{cam_id}] no frame")
        return
    state = classify_state(frame)
    ocr = extract_text(frame)
    post_analysis(cam_id, state, ocr)
    print(f"  [{cam_id}] state={state.get('state')} "
          f"congestion={state.get('congestion')} "
          f"texts={len(ocr.get('texts', []))} ({state.get('source_ai')})")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--camera")
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--interval", type=float, default=30)
    ap.add_argument("--once", action="store_true")
    args = ap.parse_args()

    print(f"Scene monitor -> {SERVER} (API key: {has_api_key()})")
    if not has_api_key():
        print("  WARNING: no ANTHROPIC_API_KEY - sending mock analysis")

    cams = load_cameras(None if args.all else args.camera)
    if not cams:
        print("no cameras")
        sys.exit(1)

    while True:
        for cam in cams:
            analyze_camera(cam)
        if args.once:
            break
        time.sleep(args.interval)


if __name__ == "__main__":
    main()
