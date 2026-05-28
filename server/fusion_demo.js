// 融合デモ: 古いテストエンティティ削除 + カメラ検出位置に正確にマップするGPSワーカーを投入
import DB from 'better-sqlite3';
import { recomputeTransform } from './geotransform.js';

const db = new DB('dt.sqlite');
db.prepare("DELETE FROM entities WHERE entity_id='worker_fuse'").run();
db.prepare("DELETE FROM positions WHERE entity_id='worker_fuse'").run();
console.log('deleted worker_fuse');

// 変換パラメータを取得して逆変換 (world -> lat/lon)
const t = recomputeTransform();
if (!t) { console.log('not calibrated'); process.exit(1); }

const MperLat = 111320.0;
function worldToGps(X, Z) {
  // world = (tx,tz) + scale*R*(east,north)  =>  (east,north) = R^-1 * (world-T) / scale
  const dx = X - t.tx, dz = Z - t.tz;
  const c = Math.cos(t.rotation), s = Math.sin(t.rotation);
  // R^-1 = [[c, s],[-s, c]]
  const east = (c * dx + s * dz) / t.scale;
  const north = (-s * dx + c * dz) / t.scale;
  const lon = t.refLon + east / t.mPerLon;
  const lat = t.refLat + north / MperLat;
  return { lat, lon };
}

// person_3 検出位置 (227.97,-126.17) に一致するGPSを算出
const target = { X: 227.97, Z: -126.17 };
const g = worldToGps(target.X, target.Z);
console.log(`worker GPS for world(${target.X},${target.Z}):`, g.lat.toFixed(6), g.lon.toFixed(6));

// POST用に出力
console.log(JSON.stringify({
  entityId: 'worker_demo', entityType: 'worker', displayName: '巡回員',
  source: 'gps', lat: g.lat, lon: g.lon,
}));
