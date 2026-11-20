using '../main.bicep'

param env = 'dev'
param location = 'eastus2'
param resourceGroupName = 'rg-rrida-dev-001'

// names
param postgresServerName = 'pg-rrida-dev-001'
param keyVaultName = 'kv-rrida-dev-001'
param appServicePlanName = 'asp-rrida-dev-001'
param apiAppName = 'app-rrida-api-dev'
param logAnalyticsName = 'log-rrida-dev-001'
param appInsightsName = 'appi-rrida-dev-001'

// postgres — smallest burstable, minimum backup, no HA
param postgresAdminLogin = 'rridaadmin'
param postgresAdminPassword = readEnvironmentVariable('RRIDA_PG_ADMIN_PASSWORD')
param postgresDatabaseName = 'rrida'
param postgresSkuName = 'Standard_B1ms'
param postgresSkuTier = 'Burstable'
param postgresStorageSizeGB = 32
param postgresVersion = '16'
param postgresBackupRetentionDays = 7
param postgresGeoRedundantBackup = 'Disabled'
param postgresHighAvailability = 'Disabled'
param allowAzureServices = true

// The CI/CD pipeline opens and closes its own rule around migrations, so this
// list is only for developer machines running psql or dotnet ef by hand.
param clientIpAllowList = []

// app — ASPNETCORE_ENVIRONMENT=Development also switches Swagger UI on at /
param appServicePlanSku = {
  name: 'B1'
  tier: 'Basic'
}
param environmentNameSetting = 'Development'
param aspNetCoreEnvironment = 'Development'
param connectionStringSecretName = 'RRIDA-DEV'
param createStagingSlot = false
param healthCheckPath = ''
param keyVaultPurgeProtection = false

// Object id of the Azure DevOps service connection's service principal
// (rrida-dev-arm). Without it the vault grant in main.bicep is skipped, the
// migrate stage cannot read RRIDA-DEV, and the run fails with
// "Forbidden ... Assignment: (not found)" before it reaches the database.
// Taken from the oid claim in that error. To re-derive it:
//   az ad sp show --id <appId> --query id -o tsv
param pipelinePrincipalId = '693f7176-4613-417b-9624-cfb058266f4a'
