// WebChat Container App (nginx SPA)
param name string
param location string
param tags object
param containerAppsEnvId string
param containerRegistryServer string
param containerRegistryId string
param imageName string
param agentHostUrl string
param authMode string
param entraTenantId string
param entraWebClientId string

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
        targetPort: 80
        transport: 'auto'
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
          name: 'webchat'
          image: imageName
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'VITE_API_BASE_URL', value: agentHostUrl }
            { name: 'VITE_AUTH_MODE', value: authMode == 'EntraId' ? 'entra-id' : 'disabled' }
            { name: 'VITE_ENTRA_TENANT_ID', value: entraTenantId }
            { name: 'VITE_ENTRA_CLIENT_ID', value: entraWebClientId }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 2
      }
    }
  }
}

output id string = app.id
output fqdn string = app.properties.configuration.ingress.fqdn
