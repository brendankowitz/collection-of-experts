# Playbook: LLM Rate Limited (429 errors)

## Symptoms

- `429 Too Many Requests` errors in AgentHost logs from the LLM provider.
- Agent task duration spikes; many tasks stalled in `Working` state.
- App Insights shows requests piling up with latency > 10s.

## Confirm with KQL

```kql
// Confirm 429 spike from LLM provider calls
dependencies
| where timestamp > ago(1h)
| where name contains "openai" or name contains "anthropic" or name contains "azure"
| where resultCode == "429"
| summarize count() by bin(timestamp, 5m), target
| render timechart
```

```kql
// Slow agent tasks
requests
| where timestamp > ago(30m)
| where name in ("SendTask", "SendTaskSubscribe")
| where duration > 5000
| summarize count() by bin(timestamp, 5m)
| render timechart
```

## Mitigation Steps

### Step 1 — Identify the rate-limited provider

Check logs:

```bash
# ACA (Azure Container Apps) logs
az containerapp logs show -n agenthost-app -g <rg> --follow
```

### Step 2 — Rotate to a backup provider

Update `appsettings.Production.json` (or environment variable override):

```json
{
  "Llm": {
    "DefaultProvider": "AzureOpenAI"  // switch from OpenAI → AzureOpenAI, or vice versa
  }
}
```

Then redeploy:

```bash
azd deploy --service agenthost
```

### Step 3 — Lower concurrency (temporary)

If only one provider is available, reduce parallel agent slots:

```json
{
  "Orchestration": {
    "Coordinator": { "MaxParallelAgents": 2 }
  }
}
```

### Step 4 — Monitor recovery

```kql
dependencies
| where timestamp > ago(30m)
| where resultCode == "429"
| summarize count() by bin(timestamp, 1m)
| render timechart
```

Rate should drop to 0 within 5 minutes of switching providers.

## Recovery Validation

All of the following must be true:
- `429` count in dependencies table = 0 for ≥5 min
- `/health` returns 200
- A test task via `/tasks/send` completes < 5s

## Prevention

- Configure at least two LLM providers in production (`OpenAI` + `AzureOpenAI`).
- Add Azure Monitor alert: "dependencies | where resultCode == '429' | summarize count() > 10 in 5 min".
- Track token budget via `/admin/usage` endpoint.
