# Cost & Token Dashboard

Token usage is exported as OTel metrics via the `AgentHost.Llm` meter and can be queried in Application Insights.

## App Insights KQL Queries

### Total tokens by agent and model (last 24h)

```kql
customMetrics
| where timestamp > ago(24h)
| where name in ("llm.tokens.input", "llm.tokens.output")
| extend agent = tostring(customDimensions["agent"])
| extend provider = tostring(customDimensions["provider"])
| extend model = tostring(customDimensions["model"])
| summarize totalTokens = sum(value) by name, agent, provider, model
| order by totalTokens desc
```

### Input vs output token trend (hourly)

```kql
customMetrics
| where timestamp > ago(7d)
| where name in ("llm.tokens.input", "llm.tokens.output")
| summarize tokens = sum(value) by name, bin(timestamp, 1h)
| render timechart
```

### Cost estimate (GPT-4o pricing: $5/1M input, $15/1M output)

```kql
customMetrics
| where timestamp > ago(24h)
| where name in ("llm.tokens.input", "llm.tokens.output")
| extend agent = tostring(customDimensions["agent"])
| extend model = tostring(customDimensions["model"])
| summarize
    inputTokens = sumif(value, name == "llm.tokens.input"),
    outputTokens = sumif(value, name == "llm.tokens.output")
  by agent, model
| extend estimatedCostUSD =
    (inputTokens / 1000000.0 * 5) + (outputTokens / 1000000.0 * 15)
| order by estimatedCostUSD desc
```

### LLM request count and latency by agent

```kql
customMetrics
| where timestamp > ago(24h)
| where name == "llm.requests"
| extend agent = tostring(customDimensions["agent"])
| summarize requests = sum(value) by agent, bin(timestamp, 1h)
| render timechart
```

### P95 retrieval latency

```kql
customMetrics
| where timestamp > ago(24h)
| where name == "retrieval.duration"
| summarize p95 = percentile(value, 95), avg = avg(value) by bin(timestamp, 1h)
| render timechart
```

### Agent task throughput and error rate

```kql
requests
| where timestamp > ago(24h)
| where name in ("SendTask", "SendTaskSubscribe")
| summarize
    count = count(),
    errors = countif(success == false)
  by bin(timestamp, 5m)
| extend errorRate = errors * 100.0 / count
| render timechart
```

## Real-time sanity check (dev only)

The `/admin/usage` endpoint returns in-process counters since startup:

```bash
curl http://localhost:5000/admin/usage
```

Response format:

```json
{
  "since": "startup",
  "totalRequests": 42,
  "totalInputTokens": 12500,
  "totalOutputTokens": 8000,
  "breakdown": [
    {
      "agentId": "fhir-server-expert",
      "provider": "OpenAI",
      "model": "gpt-4o",
      "requests": 30,
      "inputTokens": 9000,
      "outputTokens": 6000,
      "totalTokens": 15000
    }
  ]
}
```

## Pilot gaps

- User-ID dimension is not yet propagated to metrics (tags will show blank `user`).
- No Azure Cost Management integration — cost estimates are manual KQL calculations.
- `llm.request.cost.usd` metric is emitted by `AgentHostMetrics` but not yet populated (provider-specific pricing tables not yet implemented).
