// ============================================================================
// Expert Agents – Azure Container Apps Deployment
// infra/main.bicep
//
// Deploy with:  azd up
// Manual:       az deployment sub create -l eastus -f infra/main.bicep \
//                 -p environmentName=dev location=eastus
// ============================================================================

targetScope = 'subscription'

// ---------------------------------------------------------------------------
// Parameters
// ---------------------------------------------------------------------------

@minLength(1)
@maxLength(64)
@description('Name of the environment (e.g. dev, staging, prod)')
param environmentName string

@minLength(1)
@description('Azure region for all resources')
param location string = 'eastus'

@description('Entra ID tenant ID for authentication (leave empty to use Disabled auth mode)')
param entraTenantId string = ''

@description('Entra ID client/app ID registered for Expert Agents API')
param entraClientId string = ''

@description('Entra ID client/app ID registered for the web-chat SPA (public client)')
param entraWebClientId string = ''

@description('Azure OpenAI endpoint (e.g. https://myaccount.openai.azure.com/)')
param azureOpenAiEndpoint string = ''

@description('Azure OpenAI API key (stored in Key Vault)')
@secure()
param azureOpenAiApiKey string = ''

@description('Anthropic API key (stored in Key Vault; leave empty for mock LLM)')
@secure()
param anthropicApiKey string = ''

@description('Postgres admin password (stored in Key Vault)')
@secure()
param postgresAdminPassword string = newGuid()

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var abbrs = {
  resourceGroup: 'rg'
  containerAppsEnv: 'cae'
  containerApp: 'ca'
  logAnalytics: 'log'
  containerRegistry: 'cr'
  keyVault: 'kv'
  postgresFlexible: 'psql'
  redis: 'redis'
  appInsights: 'appi'
}

var resourceToken = uniqueString(subscription().subscriptionId, environmentName, location)
var tags = {
  Environment: environmentName
  Project: 'ExpertAgents'
  ManagedBy: 'azd'
}

var authMode = empty(entraTenantId) ? 'Disabled' : 'EntraId'

// ---------------------------------------------------------------------------
// Resource group
// ---------------------------------------------------------------------------

resource rg 'Microsoft.Resources/resourceGroups@2022-09-01' = {
  name: '${abbrs.resourceGroup}-expert-agents-${environmentName}'
  location: location
  tags: tags
}

// ---------------------------------------------------------------------------
// Modules
// ---------------------------------------------------------------------------

module logAnalytics 'modules/log-analytics.bicep' = {
  name: 'log-analytics'
  scope: rg
  params: {
    name: '${abbrs.logAnalytics}-expert-agents-${resourceToken}'
    location: location
    tags: tags
  }
}

module appInsights 'modules/app-insights.bicep' = {
  name: 'app-insights'
  scope: rg
  params: {
    name: '${abbrs.appInsights}-expert-agents-${resourceToken}'
    location: location
    tags: tags
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
  }
}

module containerRegistry 'modules/container-registry.bicep' = {
  name: 'container-registry'
  scope: rg
  params: {
    name: '${abbrs.containerRegistry}expertagents${resourceToken}'
    location: location
    tags: tags
  }
}

module keyVault 'modules/key-vault.bicep' = {
  name: 'key-vault'
  scope: rg
  params: {
    name: '${abbrs.keyVault}-expert-agents-${resourceToken}'
    location: location
    tags: tags
    azureOpenAiApiKey: azureOpenAiApiKey
    anthropicApiKey: anthropicApiKey
    postgresAdminPassword: postgresAdminPassword
  }
}

module postgres 'modules/postgres.bicep' = {
  name: 'postgres'
  scope: rg
  params: {
    name: '${abbrs.postgresFlexible}-expert-agents-${resourceToken}'
    location: location
    tags: tags
    adminPassword: postgresAdminPassword
  }
}

module containerAppsEnv 'modules/container-apps-env.bicep' = {
  name: 'container-apps-env'
  scope: rg
  params: {
    name: '${abbrs.containerAppsEnv}-expert-agents-${resourceToken}'
    location: location
    tags: tags
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
    logAnalyticsCustomerId: logAnalytics.outputs.customerId
    logAnalyticsSharedKey: logAnalytics.outputs.sharedKey
  }
}

module agentHost 'modules/agenthost-app.bicep' = {
  name: 'agenthost-app'
  scope: rg
  params: {
    name: '${abbrs.containerApp}-agenthost-${resourceToken}'
    location: location
    tags: tags
    containerAppsEnvId: containerAppsEnv.outputs.id
    containerRegistryServer: containerRegistry.outputs.loginServer
    containerRegistryId: containerRegistry.outputs.id
    imageName: '${containerRegistry.outputs.loginServer}/agenthost:latest'
    keyVaultName: keyVault.outputs.name
    postgresConnectionString: 'Host=${postgres.outputs.fqdn};Database=expertdb;Username=pgadmin;Password=${postgresAdminPassword}'
    azureOpenAiEndpoint: azureOpenAiEndpoint
    appInsightsConnectionString: appInsights.outputs.connectionString
    authMode: authMode
    entraTenantId: entraTenantId
    entraClientId: entraClientId
  }
}

module webChat 'modules/webchat-app.bicep' = {
  name: 'webchat-app'
  scope: rg
  params: {
    name: '${abbrs.containerApp}-webchat-${resourceToken}'
    location: location
    tags: tags
    containerAppsEnvId: containerAppsEnv.outputs.id
    containerRegistryServer: containerRegistry.outputs.loginServer
    containerRegistryId: containerRegistry.outputs.id
    imageName: '${containerRegistry.outputs.loginServer}/webchat:latest'
    agentHostUrl: 'https://${agentHost.outputs.fqdn}'
    authMode: authMode
    entraTenantId: entraTenantId
    entraWebClientId: entraWebClientId
  }
}

// ---------------------------------------------------------------------------
// Outputs (consumed by azd)
// ---------------------------------------------------------------------------

output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.outputs.loginServer
output AGENTHOST_URL string = 'https://${agentHost.outputs.fqdn}'
output WEBCHAT_URL string = 'https://${webChat.outputs.fqdn}'
output APP_INSIGHTS_CONNECTION_STRING string = appInsights.outputs.connectionString
