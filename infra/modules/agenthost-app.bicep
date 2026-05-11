// AgentHost Container App
param name string
param location string
param tags object
param containerAppsEnvId string
param containerRegistryServer string
param containerRegistryId string
param imageName string
param keyVaultName string
param postgresConnectionString string
param azureOpenAiEndpoint string
param appInsightsConnectionString string
param authMode string
param entraTenantId string
param entraClientId string

// Grant AcrPull on the registry for the system-assigned identity
resource acrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistryId, app.id, '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d') // AcrPull
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource app 'Microsoft.App/containerApps@2023-05-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppsEnvId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        corsPolicy: {
          allowedOrigins: ['*']
          allowedHeaders: ['*']
          allowedMethods: ['GET', 'POST', 'PUT', 'DELETE', 'OPTIONS']
          allowCredentials: false
        }
      }
      registries: [
        {
          server: containerRegistryServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'agenthost'
          image: imageName
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ConnectionStrings__Postgres', value: postgresConnectionString }
            { name: 'AzureOpenAI__Endpoint', value: azureOpenAiEndpoint }
            { name: 'ApplicationInsights__ConnectionString', value: appInsightsConnectionString }
            { name: 'Authentication__Mode', value: authMode }
            { name: 'Authentication__EntraId__Instance', value: 'https://login.microsoftonline.com/' }
            { name: 'Authentication__EntraId__TenantId', value: entraTenantId }
            { name: 'Authentication__EntraId__ClientId', value: entraClientId }
            { name: 'Authentication__EntraId__Audience', value: 'api://${entraClientId}' }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
        rules: [
          {
            name: 'http-scaler'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

output id string = app.id
output fqdn string = app.properties.configuration.ingress.fqdn
output principalId string = app.identity.principalId
