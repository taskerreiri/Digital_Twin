"""
YOLO物体検出。画像/フレームから person / vehicle / material(代替) を検出する。
ultralytics YOLO (COCO学習済み) を使用。person, car/truck/bus → vehicle にマップ。
"""
from ultralytics import YOLO

# COCOクラス → DTクラスのマッピング
COCO_TO_DT = {
    "person": "person",
    "car": "vehicle",
    "truck": "vehicle",
    "bus": "vehicle",
    "forklift": "vehicle",   # COCOには無いが将来のカスタムモデル用
    "train": "vehicle",
}

_model = None


def get_model(weights="yolov8n.pt"):
    """モデルをロード(キャッシュ)。yolov8n=軽量nano。初回は自動DL。"""
    global _model
    if _model is None:
        _model = YOLO(weights)
    return _model


def detect(image_path_or_array, conf=0.35, weights="yolov8n.pt"):
    """
    画像から検出。
    return: [{class, confidence, bbox:[x,y,w,h]}, ...]  (DTクラスのみ)
    """
    model = get_model(weights)
    results = model(image_path_or_array, conf=conf, verbose=False)
    detections = []
    for r in results:
        names = r.names
        for box in r.boxes:
            cls_name = names[int(box.cls[0])]
            dt_class = COCO_TO_DT.get(cls_name)
            if dt_class is None:
                continue
            x1, y1, x2, y2 = box.xyxy[0].tolist()
            detections.append({
                "class": dt_class,
                "confidence": round(float(box.conf[0]), 3),
                "bbox": [x1, y1, x2 - x1, y2 - y1],
            })
    return detections


if __name__ == "__main__":
    import sys
    img = sys.argv[1] if len(sys.argv) > 1 else None
    if not img:
        print("usage: python detect.py <image>")
        sys.exit(1)
    dets = detect(img)
    print(f"detections: {len(dets)}")
    for d in dets:
        print(f"  {d['class']} {d['confidence']} bbox={[round(x) for x in d['bbox']]}")
