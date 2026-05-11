# Azure Container Apps Deployment

This guide walks you through deploying Expert Agents to Azure using the [Azure Developer CLI (azd)](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/).

## Prerequisites

- [Azure Developer CLI](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/install-azd) ≥ 1.9
- [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli) ≥ 2.60
- An active Azure subscription
- Docker Desktop (for local image builds)

## Required Entra App Registrations

You need **two** app registrations in your Entra ID tenant (optional – omit for `Disabled` auth mode):

### 1 · Expert Agents API (backend)

1. **Azure portal → Entra ID → App registrations → New registration**
   - Name: `Expert Agents API`
   - Supported account types: Single tenant
2. Under **Expose an API**, add a scope: `Experts.Access`
3. Record the **Application (client) ID** → used as `entraClientId` parameter

### 2 · Expert Agents Web (frontend SPA)

1. **New registration**
   - Name: `Expert Agents Web`
   - Supported account types: Single tenant
   - Redirect URI: `https://<your-webchat-fqdn>/` (update after first deploy)
2. Under **Authentication**, enable **Allow public client flows**
3. Under **API permissions**, add `Expert Agents API / Experts.Access` (delegated)
4. Record the **Application (client) ID** → used as `entraWebClientId` parameter

## First Deploy

```bash
# 1. Log in
azd auth login
az login

# 2. Initialize (first time only)
azd init --template . --no-prompt

# 3. Set required parameters
azd env set AZURE_LOCATION eastus
azd env set ENVIRONMENT_NAME dev

# Optional – Entra ID auth (leave unset for Disabled/mock mode)
azd env set ENTRA_TENANT_ID   <your-tenant-id>
azd env set ENTRA_CLIENT_ID   <api-app-client-id>
azd env set ENTRA_WEB_CLIENT_ID <web-app-client-id>

# Optional – LLM providers (leave unset to use Mock LLM)
azd env set AZURE_OPENAI_ENDPOINT https://myaccount.openai.azure.com/
azd env set AZURE_OPENAI_API_KEY  <key>
azd env set ANTHROPIC_API_KEY     <key>

# 4. Provision infrastructure + build and push images + deploy
azd up
```

The `azd up` command:
1. Provisions all Azure resources via `infra/main.bicep`
2. Builds `agenthost` and `webchat` Docker images
3. Pushes images to Azure Container Registry
4. Deploys images to Container Apps

## Iterative Updates

```bash
# Redeploy only the application code (no infra changes)
azd deploy

# Redeploy only infrastructure
azd provision
```

## Accessing the Deployment

After `azd up` completes, the outputs include:

| Output | Description |
|--------|-------------|
| `AGENTHOST_URL` | AgentHost API + Swagger UI |
| `WEBCHAT_URL`   | Web chat interface |
| `APP_INSIGHTS_CONNECTION_STRING` | Application Insights |

Open `$WEBCHAT_URL` in a browser to start chatting.

## Architecture

```
Internet
  │
  ├── webchat Container App  (external ingress, port 80)
  │     └── nginx serving React SPA
  │
  └── agenthost Container App  (external ingress, port 8080)
        ├── ASP.NET Core API (A2A, SignalR, MCP)
        └── Background services (indexing)
              │
              ├── Postgres Flexible Server  (internal)
              └── Qdrant  (pilot: ephemeral in-container)
                         (prod: use Azure Container Apps with Azure Files volume)
```

> **Qdrant note**: The pilot deployment uses Qdrant embedded in the AgentHost container.
> For production, deploy a separate Container App with an Azure Files volume mount, or
> use a managed vector store such as Azure AI Search.

> **Redis/SignalR**: The pilot uses in-memory SignalR (single AgentHost replica).
> For multi-replica scaling, provision Azure Cache for Redis (Basic C0 ≈ $16/mo) and
> set `SignalR__Redis__ConnectionString` as an environment variable on the agenthost app.

## Teardown

```bash
# Remove all Azure resources created by azd
azd down --purge
```

> `--purge` permanently deletes the Key Vault (bypasses soft-delete).
> Omit it if you want to keep Key Vault recoverable for 7 days.
