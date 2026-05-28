// SQLite データレイヤー: エンティティ登録 + 位置履歴
import Database from 'better-sqlite3';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const DB_PATH = process.env.DT_DB_PATH || path.join(__dirname, 'dt.sqlite');

const db = new Database(DB_PATH);
db.pragma('journal_mode = WAL');

db.exec(`
  CREATE TABLE IF NOT EXISTS entities (
    entity_id    TEXT PRIMARY KEY,
    entity_type  TEXT NOT NULL,
    display_name TEXT,
    color        TEXT,
    last_seen    INTEGER,
    last_lat     REAL,
    last_lon     REAL,
    last_zone    TEXT,
    last_source  TEXT,
    meta         TEXT
  );

  CREATE TABLE IF NOT EXISTS positions (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    entity_id   TEXT NOT NULL,
    entity_type TEXT NOT NULL,
    source      TEXT NOT NULL,
    lat         REAL,
    lon         REAL,
    alt         REAL,
    accuracy    REAL,
    zone_id     TEXT,
    timestamp   INTEGER NOT NULL,
    received_at INTEGER NOT NULL
  );
  CREATE INDEX IF NOT EXISTS idx_positions_entity ON positions(entity_id, timestamp);

  CREATE TABLE IF NOT EXISTS materials (
    entity_id   TEXT PRIMARY KEY,
    material_type TEXT NOT NULL,
    display_name TEXT,
    lat         REAL,
    lon         REAL,
    zone_id     TEXT,
    note        TEXT,
    placed_at   INTEGER NOT NULL,
    removed     INTEGER DEFAULT 0
  );

  CREATE TABLE IF NOT EXISTS calibration (
    landmark_id TEXT PRIMARY KEY,
    lat         REAL NOT NULL,
    lon         REAL NOT NULL,
    samples     INTEGER,
    recorded_at INTEGER NOT NULL
  );

  CREATE TABLE IF NOT EXISTS detections (
    track_id    TEXT PRIMARY KEY,
    camera_id   TEXT NOT NULL,
    class       TEXT NOT NULL,
    confidence  REAL,
    world_x     REAL,
    world_z     REAL,
    fused_with  TEXT,
    source_ai   TEXT,
    last_seen   INTEGER NOT NULL
  );
  CREATE INDEX IF NOT EXISTS idx_detections_seen ON detections(last_seen);
`);

const DEFAULT_COLORS = {
  worker: '#4A9EFF',
  equipment: '#FF8C42',
  material: '#7ED957',
};

const upsertEntity = db.prepare(`
  INSERT INTO entities (entity_id, entity_type, display_name, color, last_seen, last_lat, last_lon, last_zone, last_source, meta)
  VALUES (@entity_id, @entity_type, @display_name, @color, @last_seen, @last_lat, @last_lon, @last_zone, @last_source, @meta)
  ON CONFLICT(entity_id) DO UPDATE SET
    last_seen = @last_seen,
    last_lat = @last_lat,
    last_lon = @last_lon,
    last_zone = @last_zone,
    last_source = @last_source,
    display_name = COALESCE(@display_name, display_name),
    color = COALESCE(@color, color)
`);

const insertPosition = db.prepare(`
  INSERT INTO positions (entity_id, entity_type, source, lat, lon, alt, accuracy, zone_id, timestamp, received_at)
  VALUES (@entity_id, @entity_type, @source, @lat, @lon, @alt, @accuracy, @zone_id, @timestamp, @received_at)
`);

export function recordPosition(evt) {
  const now = Date.now();
  const entityType = evt.entityType || 'worker';
  const color = evt.color || DEFAULT_COLORS[entityType] || '#CCCCCC';

  const normalized = {
    entity_id: evt.entityId,
    entity_type: entityType,
    source: evt.source || 'gps',
    lat: evt.lat ?? null,
    lon: evt.lon ?? null,
    alt: evt.alt ?? null,
    accuracy: evt.accuracy ?? null,
    zone_id: evt.zoneId ?? null,
    timestamp: evt.timestamp || now,
    received_at: now,
  };

  insertPosition.run(normalized);
  upsertEntity.run({
    entity_id: evt.entityId,
    entity_type: entityType,
    display_name: evt.displayName ?? null,
    color,
    last_seen: now,
    last_lat: normalized.lat,
    last_lon: normalized.lon,
    last_zone: normalized.zone_id,
    last_source: normalized.source,
    meta: evt.meta ? JSON.stringify(evt.meta) : null,
  });

  return {
    type: 'position_update',
    entityId: evt.entityId,
    entityType,
    displayName: evt.displayName ?? getEntityName(evt.entityId),
    color,
    lat: normalized.lat,
    lon: normalized.lon,
    zoneId: normalized.zone_id,
    source: normalized.source,
    timestamp: normalized.timestamp,
  };
}

const upsertMaterial = db.prepare(`
  INSERT INTO materials (entity_id, material_type, display_name, lat, lon, zone_id, note, placed_at, removed)
  VALUES (@entity_id, @material_type, @display_name, @lat, @lon, @zone_id, @note, @placed_at, 0)
  ON CONFLICT(entity_id) DO UPDATE SET
    material_type = @material_type,
    display_name = @display_name,
    lat = @lat, lon = @lon, zone_id = @zone_id, note = @note,
    placed_at = @placed_at, removed = 0
`);

export function recordMaterial(evt) {
  const now = Date.now();
  const entityId = evt.entityId || `material_${now}`;
  upsertMaterial.run({
    entity_id: entityId,
    material_type: evt.materialType || 'unknown',
    display_name: evt.displayName ?? null,
    lat: evt.lat ?? null,
    lon: evt.lon ?? null,
    zone_id: evt.zoneId ?? null,
    note: evt.note ?? null,
    placed_at: now,
  });

  return {
    type: 'material_placed',
    entityId,
    entityType: 'material',
    materialType: evt.materialType || 'unknown',
    displayName: evt.displayName ?? evt.materialType,
    color: DEFAULT_COLORS.material,
    lat: evt.lat ?? null,
    lon: evt.lon ?? null,
    zoneId: evt.zoneId ?? null,
    timestamp: now,
  };
}

const getEntity = db.prepare('SELECT * FROM entities WHERE entity_id = ?');
function getEntityName(id) {
  const row = getEntity.get(id);
  return row?.display_name || id;
}

const allEntities = db.prepare('SELECT * FROM entities');
const allMaterials = db.prepare('SELECT * FROM materials WHERE removed = 0');

const upsertCalibration = db.prepare(`
  INSERT INTO calibration (landmark_id, lat, lon, samples, recorded_at)
  VALUES (@landmark_id, @lat, @lon, @samples, @recorded_at)
  ON CONFLICT(landmark_id) DO UPDATE SET
    lat = @lat, lon = @lon, samples = @samples, recorded_at = @recorded_at
`);

export function recordCalibration(evt) {
  upsertCalibration.run({
    landmark_id: evt.landmarkId,
    lat: evt.lat,
    lon: evt.lon,
    samples: evt.samples ?? 1,
    recorded_at: Date.now(),
  });
}

const allCalibration = db.prepare('SELECT * FROM calibration');
export function getCalibration() {
  return allCalibration.all().map((c) => ({
    landmarkId: c.landmark_id,
    lat: c.lat,
    lon: c.lon,
    samples: c.samples,
    recordedAt: c.recorded_at,
  }));
}

const upsertDetection = db.prepare(`
  INSERT INTO detections (track_id, camera_id, class, confidence, world_x, world_z, fused_with, source_ai, last_seen)
  VALUES (@track_id, @camera_id, @class, @confidence, @world_x, @world_z, @fused_with, @source_ai, @last_seen)
  ON CONFLICT(track_id) DO UPDATE SET
    confidence=@confidence, world_x=@world_x, world_z=@world_z,
    fused_with=@fused_with, source_ai=@source_ai, last_seen=@last_seen
`);

export function recordDetection(d) {
  upsertDetection.run({
    track_id: d.trackId,
    camera_id: d.cameraId,
    class: d.class,
    confidence: d.confidence ?? null,
    world_x: d.worldX ?? null,
    world_z: d.worldZ ?? null,
    fused_with: d.fusedWith ?? null,
    source_ai: d.source_ai ?? 'yolo',
    last_seen: Date.now(),
  });
}

const recentWorkers = db.prepare(
  "SELECT entity_id, last_lat, last_lon FROM entities WHERE last_seen > ?"
);
export function getRecentEntities(sinceMs) {
  return recentWorkers.all(sinceMs);
}

const expireDetections = db.prepare('DELETE FROM detections WHERE last_seen < ?');
export function purgeStaleDetections(olderThanMs) {
  return expireDetections.run(olderThanMs).changes;
}

const allDetections = db.prepare('SELECT * FROM detections');
export function getDetections() {
  return allDetections.all().map((d) => ({
    type: 'detection_update',
    entityId: d.track_id,
    entityType: d.class,
    source: 'camera',
    cameraId: d.camera_id,
    worldX: d.world_x,
    worldZ: d.world_z,
    confidence: d.confidence,
    fusedWith: d.fused_with,
    timestamp: d.last_seen,
  }));
}

export function getSnapshot() {
  const entities = allEntities.all().map((e) => ({
    type: 'position_update',
    entityId: e.entity_id,
    entityType: e.entity_type,
    displayName: e.display_name || e.entity_id,
    color: e.color,
    lat: e.last_lat,
    lon: e.last_lon,
    zoneId: e.last_zone,
    source: e.last_source,
    timestamp: e.last_seen,
  }));

  const materials = allMaterials.all().map((m) => ({
    type: 'material_placed',
    entityId: m.entity_id,
    entityType: 'material',
    materialType: m.material_type,
    displayName: m.display_name || m.material_type,
    color: DEFAULT_COLORS.material,
    lat: m.lat,
    lon: m.lon,
    zoneId: m.zone_id,
    timestamp: m.placed_at,
  }));

  return { entities, materials };
}

export default db;
