"""
Claude Vision 昇格 (Phase 2.2 状態判定 / 2.3 OCR)。
ローカルYOLOで判断できない/重要なシーンを Claude Vision API で詳細解析する。

- 状態判定: エリアの稼働/停止/異常 + 混雑度
- OCR: 資材タグ・看板・ナンバー等の文字抽出
- 低信頼検出の再判定

ANTHROPIC_API_KEY 未設定時はモック応答を返す(コストゼロで疎通確認可)。
モデルは claude-opus-4-7、構造化出力 + プロンプトキャッシュを使用。
"""
import base64
import json
import os

MODEL = "claude-opus-4-7"

# 状態判定の指示(プロンプトキャッシュ対象の安定プレフィックス)
STATE_SYSTEM = (
    "あなたは建設ヤードの監視カメラ画像を解析する専門家です。"
    "与えられた画像領域の作業状態を判定してください。"
    "state は operating(稼働中)/idle(停止)/abnormal(異常)/unknown のいずれか、"
    "congestion は low/medium/high、reason は日本語で簡潔に。"
)
OCR_SYSTEM = (
    "あなたは建設ヤードの監視カメラ画像から文字情報を抽出する専門家です。"
    "資材タグ・看板・車両ナンバー・標識などの可読な文字をすべて抽出してください。"
    "各テキストに kind (tag/sign/plate/other) を付与してください。"
)

STATE_SCHEMA = {
    "type": "object",
    "properties": {
        "state": {"type": "string", "enum": ["operating", "idle", "abnormal", "unknown"]},
        "congestion": {"type": "string", "enum": ["low", "medium", "high"]},
        "confidence": {"type": "number"},
        "reason": {"type": "string"},
    },
    "required": ["state", "congestion", "confidence", "reason"],
    "additionalProperties": False,
}
OCR_SCHEMA = {
    "type": "object",
    "properties": {
        "texts": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "text": {"type": "string"},
                    "kind": {"type": "string", "enum": ["tag", "sign", "plate", "other"]},
                },
                "required": ["text", "kind"],
                "additionalProperties": False,
            },
        }
    },
    "required": ["texts"],
    "additionalProperties": False,
}

_client = None


def _get_client():
    global _client
    if _client is None:
        import anthropic
        _client = anthropic.Anthropic()
    return _client


def has_api_key():
    return bool(os.environ.get("ANTHROPIC_API_KEY"))


def _encode_image(image_bgr, bbox=None):
    """cv2 BGR配列(必要ならbboxでクロップ)をJPEG base64に変換"""
    import cv2
    img = image_bgr
    if bbox is not None:
        x, y, w, h = (int(v) for v in bbox)
        x, y = max(0, x), max(0, y)
        img = image_bgr[y:y + h, x:x + w]
    ok, buf = cv2.imencode(".jpg", img, [cv2.IMWRITE_JPEG_QUALITY, 85])
    if not ok:
        raise ValueError("image encode failed")
    return base64.standard_b64encode(buf.tobytes()).decode("utf-8")


def _call_vision(system, schema, image_b64, user_text, effort="medium"):
    """Claude Vision を構造化出力で呼ぶ。systemはプロンプトキャッシュ対象。"""
    client = _get_client()
    resp = client.messages.create(
        model=MODEL,
        max_tokens=2000,
        thinking={"type": "adaptive"},
        output_config={
            "effort": effort,
            "format": {"type": "json_schema", "schema": schema},
        },
        system=[{
            "type": "text",
            "text": system,
            "cache_control": {"type": "ephemeral"},  # 指示文をキャッシュ
        }],
        messages=[{
            "role": "user",
            "content": [
                {"type": "image", "source": {
                    "type": "base64", "media_type": "image/jpeg", "data": image_b64}},
                {"type": "text", "text": user_text},
            ],
        }],
    )
    text = next((b.text for b in resp.content if b.type == "text"), "{}")
    return json.loads(text)


def classify_state(image_bgr, bbox=None, effort="medium"):
    """エリアの稼働状態+混雑度を判定。source_ai='claude' を付与。"""
    if not has_api_key():
        return {"state": "unknown", "congestion": "low", "confidence": 0.0,
                "reason": "(mock: ANTHROPIC_API_KEY未設定)", "source_ai": "mock"}
    img_b64 = _encode_image(image_bgr, bbox)
    result = _call_vision(
        STATE_SYSTEM, STATE_SCHEMA, img_b64,
        "この領域の作業状態と混雑度を判定してください。", effort)
    result["source_ai"] = "claude"
    return result


def extract_text(image_bgr, bbox=None, effort="low"):
    """画像から文字を抽出(OCR)。"""
    if not has_api_key():
        return {"texts": [], "source_ai": "mock"}
    img_b64 = _encode_image(image_bgr, bbox)
    result = _call_vision(
        OCR_SYSTEM, OCR_SCHEMA, img_b64,
        "画像内の可読な文字をすべて抽出してください。", effort)
    result["source_ai"] = "claude"
    return result


def should_escalate(detection, low_conf=0.5):
    """YOLO検出を昇格すべきか判定: 低信頼なら True。"""
    return detection.get("confidence", 1.0) < low_conf


if __name__ == "__main__":
    import sys
    print(f"ANTHROPIC_API_KEY set: {has_api_key()}")
    if len(sys.argv) > 1:
        import cv2
        img = cv2.imread(sys.argv[1])
        if img is None:
            print(f"cannot read {sys.argv[1]}")
            sys.exit(1)
        print("=== state ===")
        print(json.dumps(classify_state(img), ensure_ascii=False, indent=2))
        print("=== ocr ===")
        print(json.dumps(extract_text(img), ensure_ascii=False, indent=2))
    else:
        print("mock state:", classify_state(None))
        print("mock ocr:", extract_text(None))
