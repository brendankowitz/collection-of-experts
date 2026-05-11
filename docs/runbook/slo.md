# SLO — Expert Agents Pilot

Declared Service Level Objectives for the Expert Agents pilot deployment.

## Availability

| SLO | Target | Measurement window |
|-----|--------|--------------------|
| API availability | 99.0% | Rolling 30-day |
| Excludes: planned maintenance windows (announced ≥ 24h in advance) | | |

**KQL query** (App Insights):

```kql
requests
| where timestamp > ago(30d)
| summarize
    total = count(),
    failed = countif(success == false)
| extend availability = (total - failed) * 100.0 / total
```

## Latency

| SLO | Target | Measurement |
|-----|--------|-------------|
| P95 task response (`/tasks/send`) | ≤ 5s | App Insights `requests` table |
| P95 first-byte SSE (`/tasks/sendSubscribe`) | ≤ 1s | Custom metric `sse_first_byte_ms` |
| P95 stream completion | ≤ 10s | Custom metric `sse_completion_ms` |

**KQL query**:

```kql
requests
| where timestamp > ago(7d)
| where name == "SendTask"
| summarize percentiles(duration, 50, 95, 99) by bin(timestamp, 1h)
| render timechart
```

## Eval quality

| SLO | Target |
|-----|--------|
| Retrieval@3 | ≥ 40% of golden-set questions |
| Retrieval@10 | ≥ 70% |
| LLM judge average score | ≥ 3/5 |
| Nightly eval pass rate | ≥ 70% |

The nightly eval workflow (`nightly-evals.yml`) opens a GitHub issue automatically on regression.

## Error budget

With 99.0% availability SLO, the error budget per 30 days is:

```
30 days × 24h × 60min = 43,200 minutes × 1% = 432 minutes ≈ 7.2 hours
```

## Pilot gaps (to address before GA)

- No per-user latency SLOs (user-id not yet propagated to telemetry).
- No SLO for indexing pipeline freshness.
- No automated availability alerting (configure Azure Monitor alerts manually).
