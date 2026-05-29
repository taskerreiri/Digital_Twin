// Digital Twin リアルタイム位置集約サーバー
// REST (収集) + WebSocket (配信) + SQLite (永続化)
import express from 'express';
import { WebSocketServer } from 'ws';
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { recordPosition, recordMaterial, getSnapshot, getTracks, recordCalibration, getCalibration,
         recordDetection, getDetections, purgeStaleDetections } from './db.js';
import { recomputeTransform } from './geotransform.js';
import { fuseDetection } from './fusion.js';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PORT = process.env.DT_PORT || 9300;
const TOKEN = process.env.DT_TOKEN || 'dt-poc-token';

const zonesPath = path.join(__dirname, 'zones.json');
let zones = { zones: [] };
try {
  zones = JSON.parse(fs.readFileSync(zonesPath, 'utf-8'));
} catch (e) {
  console.warn('zones.json not loaded:', e.message);
}
const zoneMap = new Map(zones.zones.map((z) => [z.zoneId, z]));

const landmarksPath = path.join(__dirname, 'landmarks.json');
let landmarks = { landmarks: [] };
try {
  landmarks = JSON.parse(fs.readFileSync(landmarksPath, 'utf-8'));
} catch (e) {
  console.warn('landmarks.json not loaded:', e.message);
}

const camerasPath = path.join(__dirname, 'cameras.json');
let cameras = { cameras: [] };
try {
  cameras = JSON.parse(fs.readFileSync(camerasPath, 'utf-8'));
} catch (e) {
  console.warn('cameras.json not loaded:', e.message);
}

const app = express();
app.use(express.json({ limit: '5mb' }));

// CORS (PWA + Unity WebGL からのアクセス許可)
app.use((req, res, next) => {
  res.header('Access-Control-Allow-Origin', '*');
  res.header('Access-Control-Allow-Headers', 'Content-Type, Authorization');
  res.header('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
  if (req.method === 'OPTIONS') return res.sendStatus(204);
  next();
});

function checkAuth(req, res, next) {
  const auth = req.headers.authorization || '';
  const token = auth.replace(/^Bearer\s+/i, '');
  if (token !== TOKEN) {
    return res.status(401).json({ error: 'unauthorized' });
  }
  next();
}

// QRゾーンイベントに代表座標を補完
function enrichZone(evt) {
  if (evt.source === 'qr_zone' && evt.zoneId) {
    const z = zoneMap.get(evt.zoneId);
    if (z) {
      evt.lat = z.repLat;
      evt.lon = z.repLon;
    }
  }
  return evt;
}

// --- REST API ---

app.get('/api/health', (req, res) => {
  res.json({ status: 'ok', zones: zones.zones.length, clients: wss.clients.size });
});

app.get('/api/zones', (req, res) => {
  res.json(zones);
});

app.get('/api/landmarks', (req, res) => {
  res.json(landmarks);
});

app.post('/api/calibration', checkAuth, (req, res) => {
  const { landmarkId, lat, lon, samples } = req.body;
  if (!landmarkId || typeof lat !== 'number' || typeof lon !== 'number') {
    return res.status(400).json({ error: 'landmarkId, lat, lon required' });
  }
  recordCalibration({ landmarkId, lat, lon, samples });
  recomputeTransform(); // サーバー側GPS→world変換も更新(融合用)
  res.json({ ok: true, landmarkId });
});

app.get('/api/cameras', (req, res) => {
  res.json(cameras);
});

// Phase 2.2/2.3: シーン解析結果 (状態判定/OCR) の保存・配信
const sceneAnalysis = new Map(); // cameraId -> 最新解析

app.post('/api/scene-analysis', checkAuth, (req, res) => {
  const { cameraId, state, congestion, texts, sourceAi } = req.body;
  if (!cameraId) return res.status(400).json({ error: 'cameraId required' });
  const entry = {
    type: 'scene_analysis',
    cameraId,
    state: state || 'unknown',
    congestion: congestion || 'low',
    texts: Array.isArray(texts) ? texts : [],
    sourceAi: sourceAi || 'unknown',
    timestamp: Date.now(),
  };
  sceneAnalysis.set(cameraId, entry);
  broadcast(entry);
  res.json({ ok: true, cameraId });
});

app.get('/api/scene-analysis', (req, res) => {
  res.json({ analyses: Array.from(sceneAnalysis.values()) });
});

app.post('/api/detection', checkAuth, (req, res) => {
  const body = req.body;
  const cameraId = body.cameraId || 'unknown';
  const dets = Array.isArray(body.detections) ? body.detections : [];
  let count = 0;

  for (const d of dets) {
    const trackId = d.trackId || `${cameraId}_${d.class}_${Date.now()}_${count}`;
    const detection = {
      trackId,
      cameraId,
      class: d.class,
      confidence: d.confidence,
      worldX: d.worldX,
      worldZ: d.worldZ,
      source_ai: d.source_ai || 'yolo',
    };
    const fusedWith = fuseDetection(detection);
    detection.fusedWith = fusedWith;
    recordDetection(detection);

    broadcast({
      type: 'detection_update',
      entityId: trackId,
      entityType: d.class,
      source: 'camera',
      cameraId,
      worldX: d.worldX,
      worldZ: d.worldZ,
      confidence: d.confidence,
      fusedWith,
      timestamp: Date.now(),
    });
    count++;
  }
  res.json({ ok: true, processed: count });
});

app.get('/api/calibration', (req, res) => {
  res.json({ samples: getCalibration() });
});

app.get('/api/entities', (req, res) => {
  res.json(getSnapshot());
});

app.get('/api/tracks', (req, res) => {
  const minutes = req.query.minutes != null ? Number(req.query.minutes) : 5;
  const limit = req.query.limit != null ? Number(req.query.limit) : 200;
  const type = req.query.type || null;
  // 将来の履歴ビューア用の受け口(未指定時は minutes 窓)
  const from = req.query.from != null ? Number(req.query.from) : null;
  const to = req.query.to != null ? Number(req.query.to) : null;

  if (!Number.isFinite(minutes) || minutes <= 0 ||
      !Number.isFinite(limit) || limit <= 0 ||
      (from != null && !Number.isFinite(from)) ||
      (to != null && !Number.isFinite(to))) {
    return res.status(400).json({ error: 'invalid query parameter' });
  }

  res.json({ tracks: getTracks({ minutes, limit, type, from, to }) });
});

app.post('/api/position', checkAuth, (req, res) => {
  const body = req.body;
  const events = Array.isArray(body.events) ? body.events : [body];
  let count = 0;

  for (const raw of events) {
    if (!raw.entityId) continue;
    const evt = enrichZone({ ...raw });
    const msg = recordPosition(evt);
    broadcast(msg);
    count++;
  }

  res.json({ ok: true, processed: count });
});

app.post('/api/material', checkAuth, (req, res) => {
  const evt = enrichZone({ ...req.body });
  const msg = recordMaterial(evt);
  broadcast(msg);
  res.json({ ok: true, entityId: msg.entityId });
});

// --- WebSocket 配信 ---

const server = http.createServer(app);
const wss = new WebSocketServer({ server, path: '/ws' });

// Unity JsonUtility は JSON の null 値を解析できないため、null/undefinedキーを除去する
function clean(obj) {
  const out = {};
  for (const [k, v] of Object.entries(obj)) {
    if (v !== null && v !== undefined) out[k] = v;
  }
  return out;
}

function broadcast(msg) {
  const data = JSON.stringify(clean(msg));
  for (const client of wss.clients) {
    if (client.readyState === 1) {
      client.send(data);
    }
  }
}

wss.on('connection', (ws) => {
  console.log(`[ws] client connected (total: ${wss.clients.size})`);
  // 接続直後に全エンティティのスナップショットを送信
  const snapshot = getSnapshot();
  for (const e of snapshot.entities) ws.send(JSON.stringify(clean(e)));
  for (const m of snapshot.materials) ws.send(JSON.stringify(clean(m)));
  for (const d of getDetections()) ws.send(JSON.stringify(clean(d)));
  for (const a of sceneAnalysis.values()) ws.send(JSON.stringify(clean(a)));

  ws.on('close', () => {
    console.log(`[ws] client disconnected (total: ${wss.clients.size})`);
  });
});

// 古いカメラ検出を定期削除し、監視ビューから消す (カメラから外れた対象)
const DETECTION_TTL_MS = 5000;
setInterval(() => {
  const stale = getDetections().filter((d) => Date.now() - d.timestamp > DETECTION_TTL_MS);
  const removed = purgeStaleDetections(Date.now() - DETECTION_TTL_MS);
  if (removed > 0) {
    for (const d of stale) {
      broadcast({ type: 'detection_remove', entityId: d.entityId });
    }
  }
}, 2000);

server.listen(PORT, () => {
  console.log(`DT Server listening on http://0.0.0.0:${PORT}`);
  console.log(`  REST: /api/position /api/material /api/entities /api/zones /api/health /api/tracks`);
  console.log(`  WS:   ws://0.0.0.0:${PORT}/ws`);
  console.log(`  Token: ${TOKEN}`);
  console.log(`  Zones: ${zones.zones.length}`);
});
