// 融合デモ用: 巡回員GPSを検出person_3位置に継続POST (融合状態を維持)
const LAT = 35.68199487723673, LON = 139.7670623170977;
async function post() {
  try {
    await fetch('http://localhost:9300/api/position', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: 'Bearer dt-poc-token' },
      body: JSON.stringify({
        entityId: 'worker_demo', entityType: 'worker', displayName: '巡回員',
        source: 'gps', lat: LAT, lon: LON,
      }),
    });
    process.stdout.write('.');
  } catch (e) { process.stdout.write('x'); }
}
setInterval(post, 2000);
post();
console.log('posting worker_demo (巡回員) every 2s');
