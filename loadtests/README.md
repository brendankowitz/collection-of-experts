# Load Tests

k6-based load tests for the AgentHost backend.

## Prerequisites

- [k6](https://k6.io/docs/get-started/installation/) installed locally **or** Docker.
- AgentHost running with mock provider (no LLM keys needed):
  ```bash
  cd src/AgentHost && dotnet run
  ```

## Running tests

### Docker (recommended, no k6 install required)

```bash
# SSE streaming test
docker run --rm -i --network host grafana/k6 run - < loadtests/agenthost-sse.k6.js

# SignalR fan-out test
docker run --rm -i --network host grafana/k6 run - < loadtests/signalr-fanout.k6.js
```

### Local k6

```bash
k6 run loadtests/agenthost-sse.k6.js
k6 run loadtests/signalr-fanout.k6.js
```

### PowerShell helper (Windows)

```powershell
.\scripts\run-load-tests.ps1
```

## Configuring the target

Override the base URL with an environment variable:

```bash
k6 run -e BASE_URL=https://agenthost.example.com loadtests/agenthost-sse.k6.js
```

## Test descriptions

### `agenthost-sse.k6.js`

- **Scenario**: ramps from 1 → 20 concurrent users over 2 minutes.
- **Endpoint**: `POST /tasks/sendSubscribe` (SSE streaming).
- **Assertions**:
  - P95 first-byte ≤ 1s
  - P95 stream completion ≤ 10s
  - Error rate < 5%
- Uses mock provider for deterministic latency.

### `signalr-fanout.k6.js`

- **Scenario**: 10 concurrent WebSocket clients connected to `/hub/chat` for 60s.
- **Assertions**:
  - P95 fan-out latency ≤ 2s
  - Connect error rate < 5%

## Interpreting results

k6 prints a summary after each run. Key metrics:

| Metric | Target |
|--------|--------|
| `sse_first_byte_ms` p95 | ≤ 1000ms |
| `sse_completion_ms` p95 | ≤ 10000ms |
| `signalr_fanout_latency_ms` p95 | ≤ 2000ms |
| `sse_errors` rate | < 5% |
