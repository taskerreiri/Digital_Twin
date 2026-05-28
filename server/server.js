// Digital Twin リアルタイム位置集約サーバー
// REST (収集) + WebSocket (配信) + SQLite (永続化)
import express from 'express';
import { WebSocketServer } from 'ws';
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { recordPosition, recordMaterial, getSnapshot } from './db.js';

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

app.get('/api/entities', (req, res) => {
  res.json(getSnapshot());
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

  ws.on('close', () => {
    console.log(`[ws] client disconnected (total: ${wss.clients.size})`);
  });
});

server.listen(PORT, () => {
  console.log(`DT Server listening on http://0.0.0.0:${PORT}`);
  console.log(`  REST: /api/position /api/material /api/entities /api/zones /api/health`);
  console.log(`  WS:   ws://0.0.0.0:${PORT}/ws`);
  console.log(`  Token: ${TOKEN}`);
  console.log(`  Zones: ${zones.zones.length}`);
});
