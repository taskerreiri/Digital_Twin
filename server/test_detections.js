// WSに接続し、受信した detection_update を一覧する検証ツール
import { WebSocket } from 'ws';
const ws = new WebSocket('ws://localhost:9300/ws');
const msgs = [];
ws.on('message', (d) => msgs.push(JSON.parse(d)));
ws.on('open', () => console.log('[test] connected'));
setTimeout(() => {
  const dets = msgs.filter((x) => x.type === 'detection_update');
  const pos = msgs.filter((x) => x.type === 'position_update');
  console.log(`position_update: ${pos.length}, detection_update: ${dets.length}`);
  for (const d of dets) {
    console.log(`  ${d.entityId} | ${d.entityType} | conf=${d.confidence} | world(${d.worldX},${d.worldZ}) | fused=${d.fusedWith || '-'}`);
  }
  ws.close();
  process.exit(0);
}, 1800);
