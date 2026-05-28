// 重機/作業員GPSフィードのモック
// サーバーへ円軌道のGPS位置をPOSTし続けてE2E疎通を確認する
//
// Usage:
//   node equipment_simulator.js [--server http://localhost:9300] [--entities 3] [--interval 1000]

const args = process.argv.slice(2);
function getArg(name, def) {
  const i = args.indexOf(`--${name}`);
  return i >= 0 && args[i + 1] ? args[i + 1] : def;
}

const SERVER = getArg('server', 'http://localhost:9300');
const TOKEN = getArg('token', 'dt-poc-token');
const N_ENTITIES = parseInt(getArg('entities', '3'), 10);
const INTERVAL = parseInt(getArg('interval', '1000'), 10);

// 基準点 (zones.json と整合する敷地中心付近)
const BASE_LAT = 35.6817;
const BASE_LON = 139.7674;
const RADIUS_DEG = 0.0008; // 約80m半径

const entities = [];
for (let i = 0; i < N_ENTITIES; i++) {
  const isEquipment = i % 2 === 0;
  entities.push({
    entityId: isEquipment ? `equip_sim_${i}` : `worker_sim_${i}`,
    entityType: isEquipment ? 'equipment' : 'worker',
    displayName: isEquipment ? `重機${i}` : `作業員${i}`,
    phase: (i / N_ENTITIES) * Math.PI * 2,
    speed: 0.3 + Math.random() * 0.4,
  });
}

let t = 0;

async function tick() {
  t += 0.05;
  const events = entities.map((e) => {
    const angle = e.phase + t * e.speed;
    return {
      entityId: e.entityId,
      entityType: e.entityType,
      displayName: e.displayName,
      source: 'gps',
      lat: BASE_LAT + Math.sin(angle) * RADIUS_DEG,
      lon: BASE_LON + Math.cos(angle) * RADIUS_DEG,
      alt: 0,
      accuracy: 2.5,
      timestamp: Date.now(),
    };
  });

  try {
    const res = await fetch(`${SERVER}/api/position`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${TOKEN}`,
      },
      body: JSON.stringify({ events }),
    });
    const data = await res.json();
    process.stdout.write(`\r[sim] t=${t.toFixed(1)} sent ${data.processed} entities  `);
  } catch (e) {
    process.stdout.write(`\r[sim] ERROR: ${e.message}  `);
  }
}

console.log(`Equipment simulator -> ${SERVER}`);
console.log(`  Entities: ${N_ENTITIES}, Interval: ${INTERVAL}ms`);
console.log(`  Base: (${BASE_LAT}, ${BASE_LON}), Radius: ~80m`);
setInterval(tick, INTERVAL);
tick();
