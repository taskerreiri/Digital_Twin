// カメラ検出(world座標) × GPSエンティティ(GPS→world変換) の近接融合。
// カメラ検出は匿名のため、近接GPSエンティティとひも付けて個体を推定する。
import { getRecentEntities } from './db.js';
import { gpsToWorld, isCalibrated } from './geotransform.js';

const FUSE_THRESHOLD_M = 5.0;   // この距離内なら同一とみなす
const ENTITY_FRESH_MS = 10000;  // 直近10秒のGPSエンティティのみ対象

/**
 * カメラ検出にworld座標がある前提で、近接GPSエンティティを探してfusedWithを付与。
 * @returns {string|null} 融合したエンティティID
 */
export function fuseDetection(detection) {
  if (!isCalibrated()) return null;
  if (detection.worldX == null || detection.worldZ == null) return null;
  // 人/車両のみGPSと融合 (materialは静的なので対象外)
  if (detection.class !== 'person' && detection.class !== 'vehicle') return null;

  const now = Date.now();
  const entities = getRecentEntities(now - ENTITY_FRESH_MS);

  let best = null;
  let bestDist = FUSE_THRESHOLD_M;
  for (const e of entities) {
    if (e.last_lat == null || e.last_lon == null) continue;
    const w = gpsToWorld(e.last_lat, e.last_lon);
    if (!w) continue;
    const dx = w.x - detection.worldX;
    const dz = w.z - detection.worldZ;
    const dist = Math.sqrt(dx * dx + dz * dz);
    if (dist < bestDist) {
      bestDist = dist;
      best = e.entity_id;
    }
  }
  return best;
}
