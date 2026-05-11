/**
 * k6 load test: SignalR WebSocket fan-out latency
 *
 * Connects N concurrent WebSocket clients to /hub/chat and measures
 * message fan-out latency from send to receive.
 *
 * Run: k6 run loadtests/signalr-fanout.k6.js
 */
import ws from 'k6/ws';
import { check, sleep } from 'k6';
import { Trend, Counter, Rate } from 'k6/metrics';

const fanoutLatency = new Trend('signalr_fanout_latency_ms', true);
const connectErrors = new Rate('signalr_connect_errors');
const messageCount = new Counter('signalr_messages_received');

export const options = {
  scenarios: {
    fanout: {
      executor: 'constant-vus',
      vus: 10,
      duration: '60s',
    },
  },
  thresholds: {
    signalr_fanout_latency_ms: ['p(95)<2000'],  // P95 fan-out ≤ 2s
    signalr_connect_errors: ['rate<0.05'],
  },
};

const BASE_URL = __ENV.BASE_URL || 'ws://localhost:5000';
const HUB_URL = `${BASE_URL}/hub/chat`;

// SignalR handshake payload
const HANDSHAKE = JSON.stringify({ protocol: 'json', version: 1 }) + '\x1e';

export default function () {
  const res = ws.connect(HUB_URL, {}, function (socket) {
    socket.on('open', () => {
      // Send SignalR handshake
      socket.send(HANDSHAKE);
    });

    socket.on('message', (data) => {
      // SignalR messages are terminated by \x1e (record separator)
      const messages = data.split('\x1e').filter((m) => m.length > 0);

      for (const msg of messages) {
        try {
          const parsed = JSON.parse(msg);

          // Handshake response
          if (parsed.type === undefined && 'error' in parsed === false) continue;

          if (parsed.type === 1) {
            // Invocation — measure latency from sent timestamp in arguments
            const sentAt = parsed.arguments?.[0]?.sentAt;
            if (sentAt) {
              const latency = Date.now() - sentAt;
              fanoutLatency.add(latency);
            }
            messageCount.add(1);
          }
        } catch {
          // non-JSON frames (handshake ack)
        }
      }
    });

    socket.on('error', (e) => {
      connectErrors.add(1);
    });

    // Send a ping message every 5s to keep the connection alive
    socket.setInterval(() => {
      // SignalR ping (type 6)
      socket.send(JSON.stringify({ type: 6 }) + '\x1e');
    }, 5000);

    socket.setTimeout(() => {
      socket.close();
    }, 55000);
  });

  check(res, { 'connected successfully': (r) => r && r.status === 101 });
  if (!res || res.status !== 101) {
    connectErrors.add(1);
  }

  sleep(1);
}
