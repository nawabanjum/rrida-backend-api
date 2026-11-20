@description('Linux App Service plan hosting the API.')
param planName string

@description('{ name: "P0v3", tier: "Premium0V3" } or { name: "B2", tier: "Basic" }')
param planSku object

param apiAppName string
param location string
param tags object

@description('Flat map of app settings. Nested config keys use the __ separator.')
param appSettings object

@description('Path the platform pings for instance health, e.g. /health. Empty disables it.')
param healthCheckPath string = ''

@description('Staging slot for deploy-then-swap. Prod: true.')
param createStagingSlot bool = false

param logAnalyticsWorkspaceId string = ''

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: planSku.name
    tier: planSku.tier
  }
  properties: {
    reserved: true
  }
}

resource api 'Microsoft.Web/sites@2023-12-01' = {
  name: apiAppName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0'
      // The API hosts every background job in-process as a BackgroundService
      // (see AddHostedService calls in Program.cs); without alwaysOn the host
      // unloads when idle and the scheduled jobs stop firing.
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: empty(healthCheckPath) ? null : healthCheckPath
      appSettings: [for setting in items(appSettings): {
        name: setting.key
        value: setting.value
      }]
    }
  }
}

resource stagingSlot 'Microsoft.Web/sites/slots@2023-12-01' = if (createStagingSlot) {
  parent: api
  name: 'staging'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: empty(healthCheckPath) ? null : healthCheckPath
      appSettings: [for setting in items(appSettings): {
        name: setting.key
        value: setting.value
      }]
    }
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (!empty(logAnalyticsWorkspaceId)) {
  name: 'to-log-analytics'
  scope: api
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'AppServiceHTTPLogs'
        enabled: true
      }
      {
        category: 'AppServiceConsoleLogs'
        enabled: true
      }
      {
        category: 'AppServiceAppLogs'
        enabled: true
      }
      {
        category: 'AppServicePlatformLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

output planId string = plan.id
output apiName string = api.name
output apiDefaultHostName string = api.properties.defaultHostName
output apiPrincipalId string = api.identity.principalId
output stagingPrincipalId string = createStagingSlot ? stagingSlot!.identity.principalId : ''
