// WebSocket配信の疎通テスト: 接続→スナップショット受信→配信メッセージ数カウント
import { WebSocket } from 'ws';

const ws = new WebSocket('ws://localhost:9300/ws');
let count = 0;
let snapshot = 0;
let firstMsgTime = null;

ws.on('open', () => console.log('[test] WS connected'));
ws.on('message', (data) => {
  count++;
  if (firstMsgTime === null) firstMsgTime = Date.now();
  const msg = JSON.parse(data);
  if (count <= 5) {
    console.log(`[test] msg ${count}: ${msg.type} ${msg.entityId} (${msg.lat?.toFixed(5)}, ${msg.lon?.toFixed(5)})`);
  }
});
ws.on('error', (e) => console.error('[test] error:', e.message));

setTimeout(() => {
  console.log(`[test] received ${count} messages in 5s`);
  console.log(count > 0 ? 'PASS: WebSocket broadcast working' : 'FAIL: no messages');
  ws.close();
  process.exit(count > 0 ? 0 : 1);
}, 5000);
