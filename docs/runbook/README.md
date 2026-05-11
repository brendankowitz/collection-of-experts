# Runbook Index

Operational runbook for the Expert Agents platform (AgentHost).

## Pages

| Page | Summary |
|------|---------|
| [SLO](slo.md) | Declared Service Level Objectives for pilot |
| [Incident Response](incident-response.md) | Escalation matrix, severity definitions, comms templates |
| [LLM Rate Limited](playbook-llm-rate-limited.md) | Symptoms, KQL, mitigation for provider 429s |
| [Qdrant Down](playbook-qdrant-down.md) | Symptoms, recovery for vector store failures |
| [Postgres Saturation](playbook-postgres-saturation.md) | CPU/connection pool saturation recovery |
| [Deploy / Rollback](playbook-deploy-rollback.md) | Safe rollback procedures with `azd` |

## Quick links

- [App Insights](https://portal.azure.com) — search for `agenthost` resource group
- [GitHub Actions](https://github.com) — CI / nightly eval workflows
- [Cost dashboard KQL](../observability/cost-dashboard.md)
