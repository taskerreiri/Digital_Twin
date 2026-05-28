"""
YOLO物体検出。画像/フレームから person / vehicle / material(代替) を検出する。
ultralytics YOLO (COCO学習済み) を使用。person, car/truck/bus → vehicle にマップ。

OpenVINO最適化: DT_YOLO_DEVICE=openvino でIntel Arc/CPU向けOpenVINO推論を使う。
初回に yolov8n.pt → yolov8n_openvino_model/ をエクスポート(自動)。無効時はPyTorch。
"""
import os
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
_device = None   # ultralytics predict device (例: "intel:gpu")


def _openvino_model_dir(weights):
    base = weights.replace(".pt", "")
    return f"{base}_openvino_model"


def export_openvino(weights="yolov8n.pt"):
    """yolov8n.pt → OpenVINO IR にエクスポート (Intel GPU/CPU高速化用)"""
    ov_dir = _openvino_model_dir(weights)
    if os.path.isdir(ov_dir):
        print(f"OpenVINO model already exists: {ov_dir}")
        return ov_dir
    print(f"Exporting {weights} to OpenVINO...")
    m = YOLO(weights)
    m.export(format="openvino")  # creates <base>_openvino_model/
    print(f"Exported: {ov_dir}")
    return ov_dir


def get_model(weights="yolov8n.pt"):
    """
    モデルをロード(キャッシュ)。yolov8n=軽量nano。初回は自動DL。
    DT_YOLO_DEVICE=openvino でOpenVINO IRを使用(無ければ自動エクスポート、失敗時PyTorch)。
    """
    global _model, _device
    if _model is not None:
        return _model

    device = os.environ.get("DT_YOLO_DEVICE", "").lower()
    if device == "openvino":
        try:
            ov_dir = _openvino_model_dir(weights)
            if not os.path.isdir(ov_dir):
                export_openvino(weights)
            _model = YOLO(ov_dir, task="detect")
            # 推論デバイス: DT_OV_DEVICE=gpu/cpu/npu (既定gpu=Intel Arc, 失敗時はcpu)
            ov_dev = os.environ.get("DT_OV_DEVICE", "gpu").lower()
            _device = f"intel:{ov_dev}"
            print(f"[detect] using OpenVINO model: {ov_dir} (device={_device})")
            return _model
        except Exception as e:
            print(f"[detect] OpenVINO load failed ({e}), falling back to PyTorch")

    _model = YOLO(weights)
    return _model


def detect(image_path_or_array, conf=0.35, weights="yolov8n.pt"):
    """
    画像から検出。
    return: [{class, confidence, bbox:[x,y,w,h]}, ...]  (DTクラスのみ)
    """
    model = get_model(weights)
    kwargs = {"conf": conf, "verbose": False}
    if _device:
        kwargs["device"] = _device
    try:
        results = model(image_path_or_array, **kwargs)
    except Exception as e:
        # GPU指定が失敗したらCPUへフォールバック
        if _device and "device" in kwargs:
            print(f"[detect] device {_device} failed ({e}), retrying on CPU")
            kwargs["device"] = "intel:cpu"
            results = model(image_path_or_array, **kwargs)
        else:
            raise
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
    if len(sys.argv) > 1 and sys.argv[1] == "--export-openvino":
        export_openvino()
        sys.exit(0)
    img = sys.argv[1] if len(sys.argv) > 1 else None
    if not img:
        print("usage: python detect.py <image>")
        print("       python detect.py --export-openvino")
        sys.exit(1)
    dets = detect(img)
    print(f"detections: {len(dets)}")
    for d in dets:
        print(f"  {d['class']} {d['confidence']} bbox={[round(x) for x in d['bbox']]}")
