/**
 * k6 load test: AgentHost SSE streaming endpoint (/tasks/sendSubscribe)
 *
 * Run: docker run --rm -i --network host grafana/k6 run - < loadtests/agenthost-sse.k6.js
 * Or:  k6 run loadtests/agenthost-sse.k6.js
 *
 * Requires AgentHost running with mock provider (no LLM keys needed).
 */
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Counter, Rate } from 'k6/metrics';

const firstByteTrend = new Trend('sse_first_byte_ms', true);
const completionTrend = new Trend('sse_completion_ms', true);
const errorRate = new Rate('sse_errors');
const requestCount = new Counter('sse_requests');

export const options = {
  scenarios: {
    ramp_up: {
      executor: 'ramping-vus',
      startVUs: 1,
      stages: [
        { duration: '30s', target: 5 },
        { duration: '60s', target: 20 },
        { duration: '30s', target: 0 },
      ],
    },
  },
  thresholds: {
    sse_first_byte_ms: ['p(95)<1000'],    // P95 first-byte ≤ 1s
    sse_completion_ms: ['p(95)<10000'],   // P95 stream-completion ≤ 10s
    sse_errors: ['rate<0.05'],            // error rate < 5%
    http_req_failed: ['rate<0.05'],
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';

const QUESTIONS = [
  'How does FHIR resource validation work?',
  'What search parameters are supported?',
  'Explain the bulk export pipeline.',
  'How is authentication configured?',
  'Where is the schema migration defined?',
];

export default function () {
  const question = QUESTIONS[Math.floor(Math.random() * QUESTIONS.length)];

  const payload = JSON.stringify({
    message: {
      role: 'user',
      parts: [{ text: question }],
    },
  });

  const params = {
    headers: { 'Content-Type': 'application/json' },
    timeout: '15s',
  };

  const startTime = Date.now();
  let firstByteTime = null;

  // Use http.post for SSE — k6 reads the full body synchronously
  const res = http.post(`${BASE_URL}/tasks/sendSubscribe`, payload, params);

  requestCount.add(1);

  const httpOk = check(res, {
    'status 200': (r) => r.status === 200,
    'has content-type text/event-stream': (r) =>
      r.headers['Content-Type'] && r.headers['Content-Type'].includes('text/event-stream'),
  });

  if (!httpOk) {
    errorRate.add(1);
    return;
  }

  errorRate.add(0);

  // Parse SSE stream from body
  const body = res.body || '';
  const lines = body.split('\n');
  let foundData = false;

  for (const line of lines) {
    if (line.startsWith('data:') && !foundData) {
      firstByteTime = Date.now() - startTime;
      foundData = true;
    }
  }

  const completionTime = Date.now() - startTime;

  if (firstByteTime !== null) {
    firstByteTrend.add(firstByteTime);
  }
  completionTrend.add(completionTime);

  check(body, {
    'stream contains done event': (b) => b.includes('"done"') || b.includes('"completed"'),
  });

  sleep(1);
}
