# Incident Response

## Severity Definitions

| Severity | Description | Response time | Examples |
|----------|-------------|---------------|---------|
| **SEV-1** | Complete service outage; all requests failing | 15 min | AgentHost unreachable, database down |
| **SEV-2** | Partial degradation; key flows broken for >20% of users | 30 min | LLM provider down, retrieval returning empty |
| **SEV-3** | Minor degradation; workarounds exist | 4 hours | Slow responses, single-agent failure |

## On-Call Rotation (placeholder roles)

| Role | Responsibility |
|------|----------------|
| Primary On-Call | First responder; triages and initiates incident |
| Secondary On-Call | Escalation target if primary unresponsive after 15 min |
| Service Owner | Final escalation; business impact decisions |
| Infra Lead | Azure / ACA infrastructure issues |

_Replace with real names and contact handles before production._

## Escalation Matrix

```
Alert fires
  └─► Primary On-Call (15 min SLA)
        ├─ Resolved → document in incident channel, write postmortem
        └─ Not resolved
              └─► Secondary On-Call (+15 min)
                    ├─ Resolved
                    └─► Service Owner (+30 min)
                              └─► Infra Lead (if ACA/Azure issue)
```

## Communication Templates

### SEV-1 — Initial notification

```
[SEV-1 INCIDENT] Expert Agents platform is DOWN
Time: <UTC timestamp>
Impact: All agent tasks failing / service unreachable
Status: Investigating
Next update: <UTC + 15 min>
Incident channel: #incidents
```

### SEV-2 — Initial notification

```
[SEV-2 INCIDENT] Expert Agents degraded — <component>
Time: <UTC timestamp>
Impact: <description of affected flows>
Workaround: <if any>
Status: Investigating
Next update: <UTC + 30 min>
```

### Resolution notice

```
[RESOLVED] <SEV level> — <component>
Duration: <start> → <end> UTC (<X min>)
Root cause: <brief>
Fix applied: <brief>
Postmortem: <link>
```

## Runbooks

Jump to the relevant playbook based on symptoms:

- 429 errors / latency spike → [LLM Rate Limited](playbook-llm-rate-limited.md)
- Retrieval failures / empty results → [Qdrant Down](playbook-qdrant-down.md)
- DB connection errors / slow queries → [Postgres Saturation](playbook-postgres-saturation.md)
- Bad deployment / need rollback → [Deploy Rollback](playbook-deploy-rollback.md)
