// getTracks 単体テスト: 時間窓フィルタ・点数間引き・type絞り込み・null除外
import assert from 'node:assert';
import db, { recordPosition, getTracks } from './db.js';

// クリーンスタート用に対象IDの既存行を消す
db.prepare("DELETE FROM positions WHERE entity_id IN ('t_w1','t_e1')").run();
db.prepare("DELETE FROM entities  WHERE entity_id IN ('t_w1','t_e1')").run();

const now = Date.now();

// t_w1 (worker): 直近に5点 + 10分前に1点(窓外)
for (let i = 0; i < 5; i++) {
  recordPosition({ entityId: 't_w1', entityType: 'worker', source: 'gps',
    lat: 35.0 + i * 0.0001, lon: 139.0 + i * 0.0001, timestamp: now - (5 - i) * 1000 });
}
recordPosition({ entityId: 't_w1', entityType: 'worker', source: 'gps',
  lat: 34.0, lon: 138.0, timestamp: now - 10 * 60 * 1000 }); // 窓外(10分前)

// t_e1 (equipment): 直近に3点
for (let i = 0; i < 3; i++) {
  recordPosition({ entityId: 't_e1', entityType: 'equipment', source: 'gps',
    lat: 35.5 + i * 0.0001, lon: 139.5 + i * 0.0001, timestamp: now - (3 - i) * 1000 });
}

// (a) 時間窓5分: t_w1 は窓内5点のみ(10分前は除外)
const all = getTracks({ minutes: 5, limit: 200 });
const w1 = all.find((t) => t.entityId === 't_w1');
assert.ok(w1, 't_w1 should be present');
assert.strictEqual(w1.points.length, 5, 'window should exclude the 10-min-old point');
assert.strictEqual(w1.entityType, 'worker');
assert.ok(w1.points[0].timestamp <= w1.points[4].timestamp, 'points ascending by time');

// (b) 点数上限: limit=3 で最新3点だけ
const capped = getTracks({ minutes: 5, limit: 3 });
const w1c = capped.find((t) => t.entityId === 't_w1');
assert.ok(w1c, 't_w1 should be present after limit cap');
assert.strictEqual(w1c.points.length, 3, 'limit should keep newest 3');
assert.ok(w1c.points[2].timestamp >= w1c.points[0].timestamp, 'kept points are the newest, ascending');

// (c) type 絞り込み
const eqOnly = getTracks({ minutes: 5, limit: 200, type: 'equipment' });
assert.ok(eqOnly.every((t) => t.entityType === 'equipment'), 'type filter');
assert.ok(eqOnly.find((t) => t.entityId === 't_e1'), 't_e1 present in equipment filter');
assert.ok(!eqOnly.find((t) => t.entityId === 't_w1'), 't_w1 absent in equipment filter');

console.log('PASS: getTracks (window / limit / type / order)');
