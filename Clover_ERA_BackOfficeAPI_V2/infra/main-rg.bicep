// RRIDA platform footprint, deployed INTO AN EXISTING resource group.
//
// Same resources and modules as main.bicep; the only differences are the scope
// and how the vault grants access:
//   main.bicep     subscription scope. Creates the resource group and uses Key
//                  Vault RBAC (role assignments). Dev and Prod.
//   main-rg.bicep  resource-group scope. The group must already exist, and the
//                  vault uses access policies instead of role assignments, so a
//                  service connection scoped to one resource group with
//                  Contributor rights is enough. Pre-Prod.
//
// Deploy: az deployment group create -g rg-rrida-preprod-001 -f infra/main-rg.bicep -p infra/params/preprod.bicepparam
targetScope = 'resourceGroup'

@description('Environment key. Drives naming defaults and the config ladder the API reads.')
@allowed([ 'dev', 'preprod', 'prod' ])
param env string

param location string = resourceGroup().location

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
  name: 'B2'
  tier: 'Basic'
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

@description('Directory FileLoggerProvider creates at startup. /home/LogFiles is the persisted, log-stream-visible location on Linux App Service.')
param logFolder string = '/home/LogFiles/rrida'

@description('Escape hatch for settings not modelled here. Merged last, so it wins.')
param additionalApiAppSettings object = {}

@description('Object id of the CI/CD service principal. It gets get/list/set on the vault so the pipeline can seed secrets and read the connection string for migrations. Empty skips it.')
param pipelinePrincipalId string = ''

@description('Ignored here; kept so one parameter file works with main.bicep too. Access is always granted by access policy in this template.')
param pipelineCanWriteSecrets bool = true

@description('Frontend origin for this environment. Sets email links, Stripe return URLs and the first CORS origin. Empty keeps appsettings.<Env>.json.')
param frontendBaseUrl string = ''

@description('Extra object ids that get get/list/set on the vault, e.g. a developer who seeds it by hand. Every deploy rewrites the policy list, so an entry added outside the template is lost unless it is listed here.')
param vaultWriterObjectIds array = []

@description('Ignored here; the resource group already exists. Kept so one parameter file works with main.bicep too.')
param resourceGroupName string = resourceGroup().name

module monitoring 'modules/monitoring.bicep' = {
  name: 'rrida-monitoring'
  params: {
    logAnalyticsName: logAnalyticsName
    appInsightsName: appInsightsName
    location: location
    tags: tags
  }
}

module keyvault 'modules/keyvault.bicep' = {
  name: 'rrida-keyvault'
  params: {
    keyVaultName: keyVaultName
    location: location
    tags: tags
    enablePurgeProtection: keyVaultPurgeProtection
    // Access policies, not RBAC: granting a role needs permissions this
    // resource-group-scoped service connection does not have.
    enableRbacAuthorization: false
  }
}

module postgres 'modules/postgres.bicep' = {
  name: 'rrida-postgres'
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

module vaultAccess 'modules/keyvault-policies.bicep' = {
  name: 'rrida-keyvault-policies'
  params: {
    keyVaultName: keyvault.outputs.vaultName
    readerPrincipalIds: union(
      [ appservice.outputs.apiPrincipalId ],
      createStagingSlot ? [ appservice.outputs.stagingPrincipalId ] : []
    )
    writerPrincipalIds: union(
      empty(pipelinePrincipalId) ? [] : [ pipelinePrincipalId ],
      vaultWriterObjectIds
    )
  }
}

output resourceGroup string = resourceGroup().name
output apiUrl string = 'https://${appservice.outputs.apiDefaultHostName}'
output apiPrincipalId string = appservice.outputs.apiPrincipalId
output keyVaultUri string = keyvault.outputs.vaultUri
output keyVaultName string = keyvault.outputs.vaultName
output postgresFqdn string = postgres.outputs.fullyQualifiedDomainName
output postgresDatabase string = postgres.outputs.databaseName
output postgresAdminLogin string = postgresAdminLogin
output connectionStringSecretName string = connectionStringSecretName
