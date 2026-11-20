// RRIDA platform footprint. One template, one parameter file per environment.
// Deploy: az deployment sub create -l eastus2 -f infra/main.bicep -p infra/params/preprod.bicepparam
targetScope = 'subscription'

@description('Environment key. Drives naming defaults and the config ladder the API reads.')
@allowed([ 'dev', 'preprod', 'prod' ])
param env string

param location string = 'eastus2'

@description('Resource group holding the whole RRIDA footprint.')
param resourceGroupName string = 'rg-rrida-hosting-001'

param tags object = {
  product: 'RRIDA'
  environment: env
  managedBy: 'bicep'
}

// ---------------------------------------------------------------- names ----
param postgresServerName string
param keyVaultName string
param appServicePlanName string
param apiAppName string
param logAnalyticsName string
param appInsightsName string

// ------------------------------------------------------------- postgres ----
param postgresAdminLogin string = 'rridaadmin'

@description('Passed at deploy time from a pipeline secret or an env var. Never stored in this repo.')
@secure()
param postgresAdminPassword string

param postgresDatabaseName string = 'rrida'
param postgresSkuName string = 'Standard_B2s'

@allowed([ 'Burstable', 'GeneralPurpose', 'MemoryOptimized' ])
param postgresSkuTier string = 'Burstable'

param postgresStorageSizeGB int = 64

@allowed([ '14', '15', '16', '17' ])
param postgresVersion string = '16'

@minValue(7)
@maxValue(35)
param postgresBackupRetentionDays int = 7

@allowed([ 'Enabled', 'Disabled' ])
param postgresGeoRedundantBackup string = 'Disabled'

@allowed([ 'Disabled', 'ZoneRedundant', 'SameZone' ])
param postgresHighAvailability string = 'Disabled'

@description('pgvector (VECTOR) is required by the embeddings tables and is off until listed here.')
param postgresExtensions string = 'VECTOR,UUID-OSSP,PG_TRGM,PG_STAT_STATEMENTS'

param allowAzureServices bool = true
param clientIpAllowList array = []
param entraAdminObjectId string = ''
param entraAdminPrincipalName string = ''

// ------------------------------------------------------------------ app ----
param appServicePlanSku object = {
  name: 'P0v3'
  tier: 'Premium0V3'
}

@description('Value of Environment:Name that Program.cs switches on. Anything not PreProd/UAT/LIVE/LIVE2 takes the Dev branch.')
@allowed([ 'Development', 'PreProd', 'LIVE' ])
param environmentNameSetting string

@description('ASPNETCORE_ENVIRONMENT, picking the appsettings.<Env>.json overlay.')
param aspNetCoreEnvironment string

@description('Key Vault secret holding this environment connection string, e.g. RRIDA-PREPROD.')
param connectionStringSecretName string

param createStagingSlot bool = false
param healthCheckPath string = ''
param keyVaultPurgeProtection bool = false

@description('Directory FileLoggerProvider creates at startup. appsettings.json ships a Windows relative path ("..\\CloverBackofficeAdminAPIIDLog") which Directory.CreateDirectory rejects on Linux, killing the host during builder.Build(). /home/LogFiles is the persisted, log-stream-visible location on Linux App Service.')
param logFolder string = '/home/LogFiles/rrida'

@description('Escape hatch for settings not modelled here. Merged last, so it wins.')
param additionalApiAppSettings object = {}

@description('Object id of the CI/CD service principal, so the pipeline can read the connection string when it runs migrations. Empty skips the grant.')
param pipelinePrincipalId string = ''

@description('Grant the pipeline Key Vault Secrets Officer instead of Secrets User, so it can write the connection string and seed shared secrets (Pre-Prod).')
param pipelineCanWriteSecrets bool = false

@description('Frontend origin for this environment, e.g. https://rrida-preprod.azurewebsites.net. Sets email links, Stripe return URLs and the first CORS origin. Empty keeps appsettings.<Env>.json.')
param frontendBaseUrl string = ''

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'rrida-monitoring'
  scope: rg
  params: {
    logAnalyticsName: logAnalyticsName
    appInsightsName: appInsightsName
    location: location
    tags: tags
  }
}

module keyvault 'modules/keyvault.bicep' = {
  name: 'rrida-keyvault'
  scope: rg
  params: {
    keyVaultName: keyVaultName
    location: location
    tags: tags
    enablePurgeProtection: keyVaultPurgeProtection
  }
}

module postgres 'modules/postgres.bicep' = {
  name: 'rrida-postgres'
  scope: rg
  params: {
    serverName: postgresServerName
    location: location
    tags: tags
    skuName: postgresSkuName
    skuTier: postgresSkuTier
    storageSizeGB: postgresStorageSizeGB
    postgresVersion: postgresVersion
    backupRetentionDays: postgresBackupRetentionDays
    geoRedundantBackup: postgresGeoRedundantBackup
    highAvailabilityMode: postgresHighAvailability
    administratorLogin: postgresAdminLogin
    administratorPassword: postgresAdminPassword
    databaseName: postgresDatabaseName
    extensions: postgresExtensions
    allowAzureServices: allowAzureServices
    clientIpAllowList: clientIpAllowList
    entraAdminObjectId: entraAdminObjectId
    entraAdminPrincipalName: entraAdminPrincipalName
    logAnalyticsWorkspaceId: monitoring.outputs.workspaceId
  }
}

// Program.cs resolves the connection string by reading Environment:Name and
// then the matching AzureKeyVault:SecretNames entry, which holds the *name* of
// the vault secret. Only the key for this environment is set, so a mis-set
// Environment:Name fails to start rather than silently reaching another database.
var connectionStringSettingByEnvironment = {
  Development: {
    AzureKeyVault__SecretNames__DevConnectionString: connectionStringSecretName
  }
  PreProd: {
    AzureKeyVault__SecretNames__PreProdConnectionString: connectionStringSecretName
  }
  LIVE: {
    AzureKeyVault__SecretNames__LiveConnectionString: connectionStringSecretName
  }
}

var connectionStringSetting = connectionStringSettingByEnvironment[environmentNameSetting]

var frontendAppSettings = empty(frontendBaseUrl) ? {} : {
  SendGrid__FrontendBaseUrl: frontendBaseUrl
  App__FrontendResetUrl: '${frontendBaseUrl}/reset-password'
  Stripe__SuccessUrl: '${frontendBaseUrl}/licenses?session_id={CHECKOUT_SESSION_ID}'
  Stripe__CancelUrl: '${frontendBaseUrl}/subscription/cancel'
  AzureMarketplace__LandingPageRedirectUrl: '${frontendBaseUrl}/login'
  Cors__AllowedOrigins__0: frontendBaseUrl
}

var baseAppSettings = {
  ASPNETCORE_ENVIRONMENT: aspNetCoreEnvironment
  Environment__Name: environmentNameSetting
  AzureKeyVault__VaultUrl: keyvault.outputs.vaultUri
  // Blank forces the DefaultAzureCredential branch, i.e. the system-assigned
  // identity. Setting AzureAd__ClientSecret here would switch Program.cs back
  // to the service-principal path.
  AzureKeyVault__ManagedIdentityClientId: ''
  APPLICATIONINSIGHTS_CONNECTION_STRING: monitoring.outputs.appInsightsConnectionString
  ASPNETCORE_FORWARDEDHEADERS_ENABLED: 'true'
  AppSettings__LogFolder: logFolder
}

module appservice 'modules/appservice.bicep' = {
  name: 'rrida-appservice'
  scope: rg
  params: {
    planName: appServicePlanName
    planSku: appServicePlanSku
    apiAppName: apiAppName
    location: location
    tags: tags
    appSettings: union(baseAppSettings, connectionStringSetting, frontendAppSettings, additionalApiAppSettings)
    healthCheckPath: healthCheckPath
    createStagingSlot: createStagingSlot
    logAnalyticsWorkspaceId: monitoring.outputs.workspaceId
  }
}

// The Function App module (CloverBackoffice.Jobs + its storage account) was
// removed along with that project. Its only job was a 5-minute timer trigger
// that POSTed to the API; the API now runs that work itself in
// BatchPollingBackgroundService, so there is no second compute resource and no
// runtime storage account to provision.

module rbac 'modules/rbac.bicep' = {
  name: 'rrida-rbac'
  scope: rg
  params: {
    keyVaultName: keyvault.outputs.vaultName
    keyVaultReaderPrincipalIds: union(
      [ appservice.outputs.apiPrincipalId ],
      createStagingSlot ? [ appservice.outputs.stagingPrincipalId ] : [],
      empty(pipelinePrincipalId) || pipelineCanWriteSecrets ? [] : [ pipelinePrincipalId ]
    )
    keyVaultOfficerPrincipalIds: empty(pipelinePrincipalId) || !pipelineCanWriteSecrets ? [] : [ pipelinePrincipalId ]
  }
}

output resourceGroup string = rg.name
output apiUrl string = 'https://${appservice.outputs.apiDefaultHostName}'
output apiPrincipalId string = appservice.outputs.apiPrincipalId
output keyVaultUri string = keyvault.outputs.vaultUri
output postgresFqdn string = postgres.outputs.fullyQualifiedDomainName
output postgresDatabase string = postgres.outputs.databaseName
output connectionStringSecretName string = connectionStringSecretName
output keyVaultName string = keyvault.outputs.vaultName
output postgresAdminLogin string = postgresAdminLogin
