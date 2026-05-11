# Playbook: Qdrant Down / Retrieval Failures

## Symptoms

- Agent responses contain no file references or code snippets.
- Logs show `QdrantException` or `gRPC Status(StatusCode=Unavailable)`.
- Indexer worker (if running) is stuck or crashing with Qdrant connection errors.
- `/health` may still return 200 (health check doesn't probe Qdrant by default).

## Confirm with KQL

```kql
// Retrieval failures in App Insights
exceptions
| where timestamp > ago(30m)
| where outerMessage contains "Qdrant" or outerMessage contains "retrieval"
| summarize count() by bin(timestamp, 5m), outerMessage
```

```kql
// Custom metric: zero retrieval hits
customMetrics
| where timestamp > ago(30m)
| where name == "retrieval.hits"
| summarize avg(value) by bin(timestamp, 5m)
| render timechart
```

## Mitigation Steps

### Step 1 — Check Qdrant container status

```bash
# docker-compose (local/dev)
docker compose ps qdrant
docker compose logs qdrant --tail=50

# ACA (production)
az containerapp show -n qdrant -g <rg> --query "properties.runningStatus"
```

### Step 2 — Restart Qdrant

```bash
# docker-compose
docker compose restart qdrant

# ACA — scale down then up
az containerapp update -n qdrant -g <rg> --min-replicas 0
sleep 10
az containerapp update -n qdrant -g <rg> --min-replicas 1
```

### Step 3 — Verify Qdrant health

```bash
curl http://localhost:6333/health
# Expected: {"title":"qdrant - vector search engine","version":"..."}
```

### Step 4 — Check collection integrity

```bash
curl http://localhost:6333/collections/agent_memory
curl http://localhost:6333/collections/code_chunks
```

If collections are missing, re-trigger indexing:

```bash
# POST to indexer or restart indexer worker
dotnet run --project src/AgentHost.Indexing -- reindex --repo microsoft/fhir-server
```

### Step 5 — Restore from snapshot (if data loss)

> **Note (pilot gap)**: No automated snapshot policy exists yet for the pilot.
> The steps below apply once a snapshot policy is configured.

```bash
# List available snapshots
curl http://localhost:6333/snapshots

# Restore a snapshot
curl -X POST http://localhost:6333/collections/code_chunks/snapshots/recover \
  -H "Content-Type: application/json" \
  -d '{"location": "<snapshot_url>"}'
```

Until snapshot policy is in place, re-indexing from GitHub source is the recovery path.

## Recovery Validation

- Qdrant `/health` returns `200`
- Collections exist and have non-zero vector count
- Agent task returns file references in response:
  ```bash
  curl -X POST http://localhost:5000/tasks/send \
    -H "Content-Type: application/json" \
    -d '{"message":{"role":"user","parts":[{"text":"How does FHIR validation work?"}]}}'
  ```

## Prevention

- Add Qdrant health check to AgentHost `/health` endpoint (known pilot gap).
- Configure ACA persistent volume for Qdrant data directory.
- Set up Qdrant snapshot schedule (weekly at minimum).
