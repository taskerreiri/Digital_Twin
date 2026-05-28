// DT Field Tracker PWA
// Geolocation連続送信 + QRスキャン + IndexedDBオフラインバッファ + 同期

const TOKEN = 'dt-poc-token';
const $ = (id) => document.getElementById(id);
const logEl = $('log');
function log(msg) {
  const t = new Date().toLocaleTimeString();
  logEl.textContent = `[${t}] ${msg}\n` + logEl.textContent;
  if (logEl.textContent.length > 4000) logEl.textContent = logEl.textContent.slice(0, 4000);
}

// --- 設定の永続化 ---
const cfg = {
  get serverUrl() { return localStorage.getItem('dt_server') || 'http://localhost:9300'; },
  set serverUrl(v) { localStorage.setItem('dt_server', v); },
  get entityId() { return localStorage.getItem('dt_id') || ''; },
  set entityId(v) { localStorage.setItem('dt_id', v); },
  get displayName() { return localStorage.getItem('dt_name') || ''; },
  set displayName(v) { localStorage.setItem('dt_name', v); },
};

$('serverUrl').value = cfg.serverUrl;
$('entityId').value = cfg.entityId || `worker_${Math.floor(Math.random() * 1000)}`;
$('displayName').value = cfg.displayName || '作業員';
cfg.entityId = $('entityId').value;
cfg.displayName = $('displayName').value;

$('serverUrl').addEventListener('change', e => { cfg.serverUrl = e.target.value.trim(); });
$('entityId').addEventListener('change', e => { cfg.entityId = e.target.value.trim(); });
$('displayName').addEventListener('change', e => { cfg.displayName = e.target.value.trim(); });

// --- IndexedDB オフラインキュー ---
let db;
function openDB() {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open('dt_tracker', 1);
    req.onupgradeneeded = () => {
      req.result.createObjectStore('queue', { keyPath: 'id', autoIncrement: true });
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

function queueAdd(record) {
  return new Promise((resolve, reject) => {
    const tx = db.transaction('queue', 'readwrite');
    tx.objectStore('queue').add(record);
    tx.oncomplete = resolve;
    tx.onerror = () => reject(tx.error);
  });
}

function queueAll() {
  return new Promise((resolve, reject) => {
    const tx = db.transaction('queue', 'readonly');
    const req = tx.objectStore('queue').getAll();
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

function queueDelete(ids) {
  return new Promise((resolve, reject) => {
    const tx = db.transaction('queue', 'readwrite');
    const store = tx.objectStore('queue');
    ids.forEach(id => store.delete(id));
    tx.oncomplete = resolve;
    tx.onerror = () => reject(tx.error);
  });
}

async function updateQueuedCount() {
  const all = await queueAll();
  $('queued').textContent = all.length;
}

// --- サーバー送信 + 同期 ---
async function syncQueue() {
  if (!navigator.onLine) return;
  const all = await queueAll();
  if (all.length === 0) return;

  const positions = all.filter(r => r.kind === 'position');
  const materials = all.filter(r => r.kind === 'material');

  try {
    if (positions.length > 0) {
      const events = positions.map(r => r.payload);
      const res = await fetch(`${cfg.serverUrl}/api/position`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${TOKEN}` },
        body: JSON.stringify({ events }),
      });
      if (res.ok) {
        await queueDelete(positions.map(r => r.id));
        log(`位置 ${positions.length}件 同期完了`);
      }
    }
    for (const m of materials) {
      const res = await fetch(`${cfg.serverUrl}/api/material`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${TOKEN}` },
        body: JSON.stringify(m.payload),
      });
      if (res.ok) {
        await queueDelete([m.id]);
        log(`敷材登録 同期完了: ${m.payload.materialType}`);
      }
    }
  } catch (e) {
    log(`同期失敗(オフライン?): ${e.message}`);
  }
  updateQueuedCount();
}

// --- 位置トラッキング ---
let watchId = null;
let lastPos = null;

function startTracking() {
  if (!navigator.geolocation) { log('Geolocation非対応'); return; }
  watchId = navigator.geolocation.watchPosition(onPosition, onGeoError, {
    enableHighAccuracy: true, maximumAge: 1000, timeout: 15000,
  });
  $('trackState').innerHTML = '<span class="dot on"></span>追跡中';
  $('btnTrack').textContent = 'トラッキング停止';
  $('btnTrack').className = 'btn-stop';
  log('トラッキング開始');
}

function stopTracking() {
  if (watchId != null) navigator.geolocation.clearWatch(watchId);
  watchId = null;
  $('trackState').innerHTML = '<span class="dot off"></span>停止中';
  $('btnTrack').textContent = 'トラッキング開始';
  $('btnTrack').className = 'btn-primary';
  log('トラッキング停止');
}

async function onPosition(pos) {
  const c = pos.coords;
  lastPos = { lat: c.latitude, lon: c.longitude, accuracy: c.accuracy };
  $('lat').textContent = c.latitude.toFixed(6);
  $('lon').textContent = c.longitude.toFixed(6);
  $('acc').textContent = `${c.accuracy.toFixed(1)} m`;

  await queueAdd({
    kind: 'position',
    payload: {
      entityId: cfg.entityId,
      entityType: 'worker',
      displayName: cfg.displayName,
      source: 'gps',
      lat: c.latitude, lon: c.longitude,
      alt: c.altitude || 0, accuracy: c.accuracy,
      timestamp: Date.now(),
    },
  });
  updateQueuedCount();
  syncQueue();
  updateProximity();  // Phase 3: 自位置更新ごとに周辺再計算
}

function onGeoError(err) { log(`GPS エラー: ${err.message}`); }

$('btnTrack').addEventListener('click', () => {
  watchId == null ? startTracking() : stopTracking();
});

// --- QRゾーンチェックイン ---
async function loadZones() {
  try {
    const res = await fetch(`${cfg.serverUrl}/api/zones`);
    const data = await res.json();
    const sel = $('zoneSelect');
    data.zones.forEach(z => {
      const opt = document.createElement('option');
      opt.value = z.zoneId;
      opt.textContent = `${z.label} (${z.zoneId})`;
      sel.appendChild(opt);
    });
    log(`ゾーン ${data.zones.length}件 読込`);
  } catch (e) {
    log(`ゾーン読込失敗: ${e.message}`);
  }
}

async function checkinZone(zoneId) {
  if (!zoneId) { log('ゾーン未選択'); return; }
  await queueAdd({
    kind: 'position',
    payload: {
      entityId: cfg.entityId,
      entityType: 'worker',
      displayName: cfg.displayName,
      source: 'qr_zone',
      zoneId,
      timestamp: Date.now(),
    },
  });
  updateQueuedCount();
  syncQueue();
  log(`チェックイン: ${zoneId}`);
}

$('btnCheckin').addEventListener('click', () => checkinZone($('zoneSelect').value));

// QRスキャン (BarcodeDetector: Android Chrome対応, iOS非対応→手動選択フォールバック)
let scanStream = null;
async function startScan() {
  if (!('BarcodeDetector' in window)) {
    log('QRスキャン非対応(iOS等)。手動選択を使用してください');
    return;
  }
  try {
    const detector = new BarcodeDetector({ formats: ['qr_code'] });
    scanStream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } });
    const video = $('scanVideo');
    video.style.display = 'block';
    video.srcObject = scanStream;
    await video.play();
    log('QRスキャン中...');

    const tick = async () => {
      if (!scanStream) return;
      try {
        const codes = await detector.detect(video);
        if (codes.length > 0) {
          const zoneId = codes[0].rawValue.trim();
          stopScan();
          checkinZone(zoneId);
          return;
        }
      } catch (e) { /* ignore frame errors */ }
      requestAnimationFrame(tick);
    };
    tick();
  } catch (e) {
    log(`カメラ起動失敗: ${e.message}`);
  }
}

function stopScan() {
  if (scanStream) scanStream.getTracks().forEach(t => t.stop());
  scanStream = null;
  $('scanVideo').style.display = 'none';
}

$('btnScan').addEventListener('click', () => scanStream ? stopScan() : startScan());

// --- 基準点記録 (ランドマーク方式ジオリファレンス) ---
async function loadLandmarks() {
  try {
    const res = await fetch(`${cfg.serverUrl}/api/landmarks`);
    const data = await res.json();
    const sel = $('landmarkSelect');
    (data.landmarks || []).forEach(l => {
      const opt = document.createElement('option');
      opt.value = l.landmarkId;
      opt.textContent = l.label;
      sel.appendChild(opt);
    });
    log(`ランドマーク ${(data.landmarks || []).length}件 読込`);
  } catch (e) {
    log(`ランドマーク読込失敗: ${e.message}`);
  }
}

let calibrating = false;
$('btnCalib').addEventListener('click', () => {
  if (calibrating) return;
  const landmarkId = $('landmarkSelect').value;
  if (!landmarkId) { log('ランドマーク未選択'); return; }
  if (!navigator.geolocation) { log('Geolocation非対応'); return; }

  calibrating = true;
  const btn = $('btnCalib');
  btn.textContent = '記録中...';
  const samples = [];
  const startT = Date.now();
  const DURATION = 10000;

  const wId = navigator.geolocation.watchPosition(
    (pos) => {
      samples.push({ lat: pos.coords.latitude, lon: pos.coords.longitude, acc: pos.coords.accuracy });
      const remain = Math.max(0, Math.ceil((DURATION - (Date.now() - startT)) / 1000));
      $('calibStatus').textContent = `記録中... 残り${remain}秒 (${samples.length}サンプル)`;
    },
    (err) => log(`GPS エラー: ${err.message}`),
    { enableHighAccuracy: true, maximumAge: 0, timeout: 15000 }
  );

  setTimeout(async () => {
    navigator.geolocation.clearWatch(wId);
    btn.textContent = '基準点を記録 (10秒平均)';
    calibrating = false;

    if (samples.length === 0) {
      $('calibStatus').textContent = 'サンプル取得失敗';
      log('基準点記録失敗: サンプル0');
      return;
    }
    // 平均 (精度重み付けはせず単純平均)
    const avgLat = samples.reduce((s, x) => s + x.lat, 0) / samples.length;
    const avgLon = samples.reduce((s, x) => s + x.lon, 0) / samples.length;
    const avgAcc = samples.reduce((s, x) => s + x.acc, 0) / samples.length;

    try {
      const res = await fetch(`${cfg.serverUrl}/api/calibration`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${TOKEN}` },
        body: JSON.stringify({ landmarkId, lat: avgLat, lon: avgLon, samples: samples.length }),
      });
      if (res.ok) {
        $('calibStatus').textContent =
          `記録完了: ${landmarkId} (${samples.length}件平均, 精度~${avgAcc.toFixed(1)}m)`;
        log(`基準点記録: ${landmarkId} (${avgLat.toFixed(6)}, ${avgLon.toFixed(6)})`);
      } else {
        $('calibStatus').textContent = '送信失敗';
      }
    } catch (e) {
      $('calibStatus').textContent = `送信失敗: ${e.message}`;
      log(`基準点送信失敗: ${e.message}`);
    }
  }, DURATION);
});

// --- 敷材登録 ---
$('btnMaterial').addEventListener('click', async () => {
  const type = $('matType').value.trim();
  if (!type) { log('種別を入力してください'); return; }
  if (!lastPos) { log('先に位置を取得してください(トラッキング開始)'); return; }

  await queueAdd({
    kind: 'material',
    payload: {
      materialType: type,
      displayName: type,
      lat: lastPos.lat, lon: lastPos.lon,
      note: $('matNote').value.trim(),
    },
  });
  updateQueuedCount();
  syncQueue();
  log(`敷材登録キュー: ${type}`);
  $('matType').value = ''; $('matNote').value = '';
});

// --- オンライン状態 ---
function updateNetState() {
  const el = $('netState');
  el.innerHTML = navigator.onLine
    ? '<span class="dot on"></span>オンライン'
    : '<span class="dot off"></span>オフライン';
}
window.addEventListener('online', () => { updateNetState(); log('オンライン復帰'); syncQueue(); });
window.addEventListener('offline', () => { updateNetState(); log('オフライン'); });

// --- Phase 3: 現場ビュー (WS購読 + 近接アラート + 俯瞰マップ) ---
const entities = new Map();   // entityId -> {type, lat, lon, name, color, ts}
const sceneByCam = new Map(); // cameraId -> {state, congestion, texts}
let dtWs = null;
let lastAlertVibe = 0;

const EQUIP_ALERT_M = 20;   // 重機この距離内で警告
const WORKER_INFO_M = 10;   // 他作業員この距離内で通知
const MAP_RADIUS_M = 100;   // マップ表示半径

function metersBetween(lat1, lon1, lat2, lon2) {
  const mPerLat = 111320;
  const mPerLon = mPerLat * Math.cos(lat1 * Math.PI / 180);
  const dx = (lon2 - lon1) * mPerLon;
  const dy = (lat2 - lat1) * mPerLat;
  return { dist: Math.sqrt(dx * dx + dy * dy), dx, dy };
}

function connectWS() {
  const wsUrl = cfg.serverUrl.replace(/^http/, 'ws') + '/ws';
  try {
    dtWs = new WebSocket(wsUrl);
  } catch (e) {
    log(`WS接続失敗: ${e.message}`);
    return;
  }
  dtWs.onopen = () => {
    $('wsState').innerHTML = '<span class="dot on"></span>受信中';
    log('現場配信に接続');
  };
  dtWs.onmessage = (ev) => {
    try { handleWSMessage(JSON.parse(ev.data)); } catch (e) { /* ignore */ }
  };
  dtWs.onclose = () => {
    $('wsState').innerHTML = '<span class="dot off"></span>未接続';
    setTimeout(connectWS, 3000); // 自動再接続
  };
  dtWs.onerror = () => {};
}

function handleWSMessage(msg) {
  if (msg.type === 'position_update' && msg.lat != null) {
    if (msg.entityId === cfg.entityId) return; // 自分は除外
    entities.set(msg.entityId, {
      type: msg.entityType, lat: msg.lat, lon: msg.lon,
      name: msg.displayName || msg.entityId, ts: Date.now(),
    });
  } else if (msg.type === 'material_placed' && msg.lat != null) {
    entities.set(msg.entityId, {
      type: 'material', lat: msg.lat, lon: msg.lon,
      name: msg.displayName || '敷材', ts: Date.now(),
    });
  } else if (msg.type === 'detection_update' && msg.lat != null) {
    // カメラ検出はlat/lonがあれば(融合時)周辺に含める
    entities.set(msg.entityId, {
      type: 'camera', lat: msg.lat, lon: msg.lon,
      name: 'カメラ:' + msg.entityType, ts: Date.now(),
    });
  } else if (msg.type === 'detection_remove') {
    entities.delete(msg.entityId);
  } else if (msg.type === 'scene_analysis') {
    sceneByCam.set(msg.cameraId, {
      state: msg.state, congestion: msg.congestion, texts: msg.texts || [],
    });
    renderAreaStatus();
  }
}

function pruneStale() {
  const now = Date.now();
  for (const [id, e] of entities) {
    if (e.type !== 'material' && now - e.ts > 15000) entities.delete(id);
  }
}

function updateProximity() {
  if (!lastPos) return;
  pruneStale();
  const near = [];
  for (const [id, e] of entities) {
    const { dist } = metersBetween(lastPos.lat, lastPos.lon, e.lat, e.lon);
    near.push({ id, e, dist });
  }
  near.sort((a, b) => a.dist - b.dist);
  $('nearCount').textContent = near.length;

  // アラート生成
  const alertsEl = $('alerts');
  alertsEl.innerHTML = '';
  let danger = false;
  for (const n of near) {
    let level = null;
    if (n.e.type === 'equipment' && n.dist < EQUIP_ALERT_M) { level = 'danger'; danger = true; }
    else if (n.e.type === 'worker' && n.dist < WORKER_INFO_M) level = 'info';
    if (!level) continue;
    const div = document.createElement('div');
    div.style.cssText = `padding:8px;border-radius:6px;margin-bottom:4px;font-size:0.85rem;` +
      (level === 'danger' ? 'background:#5a1a1a;color:#ffb0b0;border:1px solid #FF5C5C'
                          : 'background:#2a3a2a;color:#b0ffc0');
    div.textContent = (level === 'danger' ? '⚠ 重機接近: ' : '・') +
      `${n.e.name} ${n.dist.toFixed(0)}m`;
    alertsEl.appendChild(div);
  }
  // 重機危険接近でバイブ (Android, 3秒に1回)
  if (danger && navigator.vibrate && Date.now() - lastAlertVibe > 3000) {
    navigator.vibrate([200, 100, 200]);
    lastAlertVibe = Date.now();
  }
  drawMiniMap(near);
}

function drawMiniMap(near) {
  const cv = $('miniMap');
  const ctx = cv.getContext('2d');
  const W = cv.width, H = cv.height;
  ctx.clearRect(0, 0, W, H);
  // グリッド
  ctx.strokeStyle = '#2a2f3a';
  ctx.beginPath();
  ctx.moveTo(W / 2, 0); ctx.lineTo(W / 2, H);
  ctx.moveTo(0, H / 2); ctx.lineTo(W, H / 2);
  ctx.stroke();
  // 半径円
  ctx.strokeStyle = '#3a4250';
  ctx.beginPath(); ctx.arc(W / 2, H / 2, W / 2 - 4, 0, Math.PI * 2); ctx.stroke();
  ctx.beginPath(); ctx.arc(W / 2, H / 2, (W / 2 - 4) * (EQUIP_ALERT_M / MAP_RADIUS_M), 0, Math.PI * 2);
  ctx.strokeStyle = '#FF5C5C44'; ctx.stroke();
  // 自分 (中心)
  ctx.fillStyle = '#fff';
  ctx.beginPath(); ctx.arc(W / 2, H / 2, 5, 0, Math.PI * 2); ctx.fill();
  // 周辺エンティティ
  const colors = { equipment: '#FF8C42', worker: '#4A9EFF', material: '#7ED957', camera: '#3AD8FF' };
  const scale = (W / 2 - 4) / MAP_RADIUS_M;
  if (!lastPos) return;
  for (const n of near) {
    const { dx, dy } = metersBetween(lastPos.lat, lastPos.lon, n.e.lat, n.e.lon);
    if (n.dist > MAP_RADIUS_M) continue;
    const px = W / 2 + dx * scale;
    const py = H / 2 - dy * scale; // 北が上
    ctx.fillStyle = colors[n.e.type] || '#ccc';
    ctx.beginPath(); ctx.arc(px, py, 4, 0, Math.PI * 2); ctx.fill();
  }
}

function renderAreaStatus() {
  const el = $('areaStatus');
  if (sceneByCam.size === 0) { el.textContent = '解析待ち...'; return; }
  const stateJp = { operating: '稼働中', idle: '停止', abnormal: '異常', unknown: '不明' };
  const stateColor = { operating: '#7ED957', idle: '#aaa', abnormal: '#FF5C5C', unknown: '#e0c84a' };
  el.innerHTML = '';
  for (const [cam, s] of sceneByCam) {
    const div = document.createElement('div');
    div.style.cssText = 'font-size:0.85rem;margin-bottom:3px;color:#e8eaed';
    const c = stateColor[s.state] || '#ccc';
    div.innerHTML = `${cam}: <span style="color:${c}">${stateJp[s.state] || s.state}</span> ` +
      `/ 混雑${s.congestion}` + (s.texts.length ? ` / OCR:${s.texts.map(t => t.text).join(' ')}` : '');
    el.appendChild(div);
  }
}

// --- 初期化 ---
(async () => {
  db = await openDB();
  updateNetState();
  updateQueuedCount();
  await loadZones();
  await loadLandmarks();
  setInterval(syncQueue, 10000); // 定期同期
  connectWS();                    // Phase 3: 現場配信を購読
  setInterval(updateProximity, 2000); // 周辺を定期再計算(GPS更新が無い時も)
  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('sw.js').catch(e => log(`SW登録失敗: ${e.message}`));
  }
  log('初期化完了');
})();
