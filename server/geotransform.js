// サーバー側 GPS(WGS84) → Unity world(X,Z) 変換。
// Unity の GPSCalibrator と同一の Procrustes 2D相似変換をJSで再現し、
// カメラ検出(world座標)とGPSエンティティの融合を同一空間で行えるようにする。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { getCalibration } from './db.js';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const MetersPerDegreeLat = 111320.0;

let landmarkMap = new Map();
try {
  const lm = JSON.parse(fs.readFileSync(path.join(__dirname, 'landmarks.json'), 'utf-8'));
  landmarkMap = new Map((lm.landmarks || []).map((l) => [l.landmarkId, l]));
} catch (e) {
  console.warn('[geotransform] landmarks.json not loaded:', e.message);
}

let transform = null; // { refLat, refLon, mPerLon, rotation, scale, tx, tz }

// キャリブレーションサンプル + ランドマークUnity座標から変換を再計算
export function recomputeTransform() {
  const samples = getCalibration();
  const pairs = [];
  for (const s of samples) {
    const lm = landmarkMap.get(s.landmarkId);
    if (lm) pairs.push({ lat: s.lat, lon: s.lon, ux: lm.unityX, uz: lm.unityZ });
  }
  if (pairs.length < 2) { transform = null; return null; }

  const n = pairs.length;
  const refLat = pairs.reduce((s, p) => s + p.lat, 0) / n;
  const refLon = pairs.reduce((s, p) => s + p.lon, 0) / n;
  const mPerLon = MetersPerDegreeLat * Math.cos((refLat * Math.PI) / 180);

  let cqx = 0, cqz = 0;
  const src = pairs.map((p) => ({
    e: (p.lon - refLon) * mPerLon,
    nth: (p.lat - refLat) * MetersPerDegreeLat,
  }));
  pairs.forEach((p) => { cqx += p.ux; cqz += p.uz; });
  cqx /= n; cqz /= n;

  let a = 0, b = 0, denom = 0;
  for (let i = 0; i < n; i++) {
    const tx = pairs[i].ux - cqx;
    const tz = pairs[i].uz - cqz;
    a += src[i].e * tx + src[i].nth * tz;
    b += src[i].e * tz - src[i].nth * tx;
    denom += src[i].e ** 2 + src[i].nth ** 2;
  }
  if (denom < 1e-9) { transform = null; return null; }

  const rotation = Math.atan2(b, a);
  const scale = Math.sqrt(a * a + b * b) / denom;
  transform = { refLat, refLon, mPerLon, rotation, scale, tx: cqx, tz: cqz, points: n };
  return transform;
}

export function isCalibrated() {
  return transform !== null;
}

export function gpsToWorld(lat, lon) {
  if (!transform) return null;
  const east = (lon - transform.refLon) * transform.mPerLon;
  const north = (lat - transform.refLat) * MetersPerDegreeLat;
  const c = Math.cos(transform.rotation);
  const s = Math.sin(transform.rotation);
  return {
    x: transform.tx + transform.scale * (c * east - s * north),
    z: transform.tz + transform.scale * (s * east + c * north),
  };
}

// 起動時に一度計算
recomputeTransform();
