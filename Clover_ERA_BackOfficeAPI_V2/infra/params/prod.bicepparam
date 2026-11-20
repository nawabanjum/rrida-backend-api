using '../main.bicep'

param env = 'prod'
param location = 'eastus2'
param resourceGroupName = 'rg-rrida-hosting-001'

// names — matches the pg-hosting-eastus2-001 convention already in eng-hosting-001
param postgresServerName = 'pg-rrida-eastus2-001'
param keyVaultName = 'kv-rrida-001'
param appServicePlanName = 'asp-rrida-eastus2-prod-001'
param apiAppName = 'app-rrida-api-001'
param logAnalyticsName = 'log-rrida-001'
param appInsightsName = 'appi-rrida-001'

// postgres — general purpose, zone-redundant HA, geo-redundant backup
param postgresAdminLogin = 'rridaadmin'
param postgresAdminPassword = readEnvironmentVariable('RRIDA_PG_ADMIN_PASSWORD')
param postgresDatabaseName = 'rrida'
param postgresSkuName = 'Standard_D2ds_v5'
param postgresSkuTier = 'GeneralPurpose'
param postgresStorageSizeGB = 128
param postgresVersion = '16'
param postgresBackupRetentionDays = 35
param postgresGeoRedundantBackup = 'Enabled'
param postgresHighAvailability = 'ZoneRedundant'
param allowAzureServices = true
param clientIpAllowList = []

// app
param appServicePlanSku = {
  name: 'P0v3'
  tier: 'Premium0V3'
}
param environmentNameSetting = 'LIVE'
param aspNetCoreEnvironment = 'Production'
param connectionStringSecretName = 'RRIDA-LIVE'
param createStagingSlot = true
param healthCheckPath = ''
param keyVaultPurgeProtection = true
