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

// --- 初期化 ---
(async () => {
  db = await openDB();
  updateNetState();
  updateQueuedCount();
  await loadZones();
  setInterval(syncQueue, 10000); // 定期同期
  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('sw.js').catch(e => log(`SW登録失敗: ${e.message}`));
  }
  log('初期化完了');
})();
