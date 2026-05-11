# Playbook: Postgres Saturation

## Symptoms

- Agent memory / task store operations slow or timing out.
- Logs show `Npgsql.NpgsqlException: Connection pool exhausted` or `timeout expired`.
- CPU on Postgres host > 80% sustained.
- `/health` may return `200` while task creation is slow.

## Confirm with KQL

```kql
// Postgres dependency failures / slow calls
dependencies
| where timestamp > ago(30m)
| where type == "SQL" or name contains "postgres" or name contains "npgsql"
| where success == false or duration > 2000
| summarize count() by bin(timestamp, 5m), resultCode
| render timechart
```

```kql
// Slow traces
traces
| where timestamp > ago(30m)
| where message contains "Connection pool" or message contains "timeout"
| project timestamp, message, severityLevel
| order by timestamp desc
| take 50
```

## Mitigation Steps

### Step 1 — Check connection count

```sql
-- Run on Postgres
SELECT count(*) AS active_connections FROM pg_stat_activity;
SELECT max_conn FROM pg_settings WHERE name = 'max_connections';
```

If `active_connections` ≈ `max_connections`, the pool is exhausted.

### Step 2 — Identify long-running queries

```sql
SELECT pid, age(clock_timestamp(), query_start) AS age, query
FROM pg_stat_activity
WHERE state != 'idle'
  AND query_start < now() - interval '30 seconds'
ORDER BY age DESC;
```

Terminate offending queries:

```sql
SELECT pg_terminate_backend(pid)
FROM pg_stat_activity
WHERE state != 'idle'
  AND query_start < now() - interval '5 minutes';
```

### Step 3 — Reduce AgentHost connection pool size (temporary)

Update connection string in configuration:

```
Host=...;Maximum Pool Size=10;Connection Idle Lifetime=60
```

Then redeploy:

```bash
azd deploy --service agenthost
```

### Step 4 — Scale Postgres (ACA / Azure Database)

For Azure Database for PostgreSQL Flexible Server:

```bash
az postgres flexible-server update \
  --resource-group <rg> \
  --name <server-name> \
  --sku-name Standard_D4s_v3  # upgrade from D2s
```

### Step 5 — Enable PgBouncer connection pooling

For production, add PgBouncer as a sidecar or use Azure's built-in connection pooling.

## Recovery Validation

- Active connections < 80% of `max_connections`
- No `Connection pool exhausted` errors in logs for ≥5 min
- Task create/complete round-trip < 200ms

## Prevention

- Set `Maximum Pool Size` in connection string to match expected concurrency.
- Add Postgres health check to AgentHost `/health`.
- Configure Azure Monitor alert on connection count metric.
- Review slow query log weekly.
