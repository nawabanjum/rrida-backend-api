@description('Flexible server name. Globally unique within .postgres.database.azure.com.')
param serverName string

param location string
param tags object

@description('Compute SKU, e.g. Standard_B2s (Burstable) or Standard_D2ds_v5 (GeneralPurpose).')
param skuName string

@allowed([ 'Burstable', 'GeneralPurpose', 'MemoryOptimized' ])
param skuTier string

@description('Storage in GB. Can grow, never shrink.')
param storageSizeGB int = 64

@allowed([ '14', '15', '16', '17' ])
param postgresVersion string = '16'

@minValue(7)
@maxValue(35)
param backupRetentionDays int = 7

@allowed([ 'Enabled', 'Disabled' ])
param geoRedundantBackup string = 'Disabled'

@description('ZoneRedundant is not available on the Burstable tier.')
@allowed([ 'Disabled', 'ZoneRedundant', 'SameZone' ])
param highAvailabilityMode string = 'Disabled'

param administratorLogin string

@secure()
param administratorPassword string

@description('Application database.')
param databaseName string = 'rrida'

@description('Server-level allowlist for extensions. pgvector ships as VECTOR and is off until listed here.')
param extensions string = 'VECTOR,UUID-OSSP,PG_TRGM,PG_STAT_STATEMENTS'

@description('Opens the 0.0.0.0 rule that lets Azure services (App Service) reach the server.')
param allowAzureServices bool = true

@description('Extra firewall rules: [ { name: "office", startIp: "x.x.x.x", endIp: "x.x.x.x" } ]')
param clientIpAllowList array = []

@description('Object ID of an Entra user/group to make Postgres AAD admin. Empty disables Entra auth.')
param entraAdminObjectId string = ''

@description('UPN or group display name matching entraAdminObjectId.')
param entraAdminPrincipalName string = ''

@allowed([ 'User', 'Group', 'ServicePrincipal' ])
param entraAdminPrincipalType string = 'User'

param logAnalyticsWorkspaceId string = ''

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: serverName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuTier
  }
  properties: {
    version: postgresVersion
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    authConfig: {
      passwordAuth: 'Enabled'
      activeDirectoryAuth: empty(entraAdminObjectId) ? 'Disabled' : 'Enabled'
      tenantId: empty(entraAdminObjectId) ? null : subscription().tenantId
    }
    storage: {
      storageSizeGB: storageSizeGB
      autoGrow: 'Enabled'
    }
    backup: {
      backupRetentionDays: backupRetentionDays
      geoRedundantBackup: geoRedundantBackup
    }
    highAvailability: {
      mode: highAvailabilityMode
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
  }
}

// Child operations on a flexible server are serialised on purpose: the control
// plane rejects concurrent writes to the same server with a conflict.
resource extensionsConfig 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: server
  name: 'azure.extensions'
  properties: {
    value: extensions
    source: 'user-override'
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: server
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
  dependsOn: [ extensionsConfig ]
}

resource allowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = if (allowAzureServices) {
  parent: server
  name: 'AllowAllAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
  dependsOn: [ database ]
}

@batchSize(1)
resource clientRules 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = [for rule in clientIpAllowList: {
  parent: server
  name: rule.name
  properties: {
    startIpAddress: rule.startIp
    endIpAddress: rule.endIp
  }
  dependsOn: [ allowAzure ]
}]

resource entraAdmin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = if (!empty(entraAdminObjectId)) {
  parent: server
  name: entraAdminObjectId
  properties: {
    principalName: entraAdminPrincipalName
    principalType: entraAdminPrincipalType
    tenantId: subscription().tenantId
  }
  dependsOn: [ clientRules ]
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (!empty(logAnalyticsWorkspaceId)) {
  name: 'to-log-analytics'
  scope: server
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        categoryGroup: 'allLogs'
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

output serverName string = server.name
output fullyQualifiedDomainName string = server.properties.fullyQualifiedDomainName
output databaseName string = databaseName
