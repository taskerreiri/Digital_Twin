"""
動体検知ゲート。連続フレームの差分で動きの有無を判定し、
静止シーンではYOLO推論をスキップして60台分の負荷を抑える。
"""
import numpy as np


class MotionGate:
    """
    フレーム差分による動体検知。
    threshold: 動きありと判定する平均ピクセル差(0-255)
    min_area_ratio: 変化ピクセルがこの割合以上で動きあり
    """
    def __init__(self, threshold=12.0, min_area_ratio=0.002, downscale=4):
        self.threshold = threshold
        self.min_area_ratio = min_area_ratio
        self.downscale = downscale
        self.prev = None

    def _prep(self, frame):
        import cv2
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        h, w = gray.shape
        small = cv2.resize(gray, (w // self.downscale, h // self.downscale))
        return small

    def has_motion(self, frame):
        """
        return: (motion: bool, score: float)
        初回フレームは常に motion=True (基準確立)
        """
        cur = self._prep(frame)
        if self.prev is None:
            self.prev = cur
            return True, 1.0

        diff = np.abs(cur.astype(np.int16) - self.prev.astype(np.int16))
        changed = np.count_nonzero(diff > self.threshold)
        ratio = changed / diff.size
        self.prev = cur
        return ratio >= self.min_area_ratio, float(ratio)

    def reset(self):
        self.prev = None


if __name__ == "__main__":
    # 自己テスト: 合成フレームで静止/動きを判定
    import numpy as np
    gate = MotionGate(downscale=1)

    base = np.full((120, 160, 3), 100, dtype=np.uint8)
    m0, s0 = gate.has_motion(base)            # 初回 → True
    m1, s1 = gate.has_motion(base.copy())     # 同一 → 動きなし
    moved = base.copy()
    moved[30:70, 40:120] = 200                # 大きな変化
    m2, s2 = gate.has_motion(moved)           # 動きあり

    print(f"frame0 (init): motion={m0} score={s0:.3f}")
    print(f"frame1 (same): motion={m1} score={s1:.4f}  (expect False)")
    print(f"frame2 (moved): motion={m2} score={s2:.4f}  (expect True)")
    ok = m0 and (not m1) and m2
    print("PASS" if ok else "FAIL")
