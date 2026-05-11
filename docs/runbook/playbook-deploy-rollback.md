# Playbook: Deploy / Rollback

## ⚠️ WARNING: `azd down --purge` is destructive

`azd down --purge` **deletes all resources including databases and storage**. Do NOT use it for rollbacks.

## Preferred rollback: redeploy a specific service

To roll back a single service (e.g., AgentHost) to the previous image:

```bash
# Option 1: Redeploy from the last known-good Git tag
git checkout <last-good-tag>
azd deploy --service agenthost

# Option 2: Update ACA to use the previous container revision
az containerapp revision list -n agenthost-app -g <rg> --output table
# Note the previous revision name, e.g., agenthost-app--abc123

az containerapp ingress traffic set -n agenthost-app -g <rg> \
  --revision-weight agenthost-app--abc123=100
```

## Full environment rollback

If multiple services need rolling back:

```bash
git checkout <last-good-tag>
azd deploy  # redeploys all services; does NOT destroy data resources
```

## Emergency: stop traffic without rolling back

```bash
# Scale AgentHost to 0 replicas (stops traffic, preserves data)
az containerapp update -n agenthost-app -g <rg> --min-replicas 0 --max-replicas 0
```

Restore:

```bash
az containerapp update -n agenthost-app -g <rg> --min-replicas 1 --max-replicas 3
```

## Deployment verification checklist

After any deploy or rollback:

1. `curl https://<host>/health` → `{"status":"Healthy"}`
2. `curl https://<host>/api/info` → correct `version` field
3. `curl -X POST https://<host>/tasks/send -d '{"message":{"role":"user","parts":[{"text":"ping"}]}}'` → task completes
4. Check App Insights for error spike (allow 5 min for metrics to settle)
5. Confirm nightly eval passes on next scheduled run

## Revision management

ACA keeps the last 10 revisions by default. To list and activate a specific revision:

```bash
# List revisions
az containerapp revision list -n agenthost-app -g <rg> \
  --query "[].{name:name, active:properties.active, created:properties.createdTime}" \
  --output table

# Activate a revision (sets it to 100% traffic)
az containerapp revision activate -n agenthost-app -g <rg> --revision <revision-name>
az containerapp ingress traffic set -n agenthost-app -g <rg> \
  --revision-weight <revision-name>=100
```

## azd commands reference

| Command | Effect | Safe for rollback? |
|---------|--------|--------------------|
| `azd deploy` | Build + push image + update ACA revision | ✅ Yes |
| `azd deploy --service <name>` | Update one service only | ✅ Yes |
| `azd down` | Delete ACA app + networking (NOT databases) | ⚠️ Risky |
| `azd down --purge` | Delete ALL resources including databases | ❌ Destructive |
