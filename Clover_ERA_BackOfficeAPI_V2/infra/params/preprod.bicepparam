using '../main-rg.bicep'

// Pre-Prod (UAT). Deployed automatically by azure-pipelines-api.yml on every
// push to the pre-prod branch: provision (this file) -> bootstrap and migrate the
// database -> deploy the API.
//
// Deployed with main-rg.bicep INTO the existing resource group
// rg-rrida-preprod-001, through the rrida-preprod-arm service connection, which
// is scoped to that group. That scope cannot create resource groups or role
// assignments, so the group is created once by hand and the vault uses access
// policies instead of RBAC.
param env = 'preprod'
param location = 'eastus2'
param resourceGroupName = 'rg-rrida-preprod-001'

// names
param postgresServerName = 'pg-rrida-preprod-001'
param keyVaultName = 'kv-rrida-preprod-001'
param appServicePlanName = 'asp-rrida-preprod-001'
param apiAppName = 'app-rrida-api-preprod'
param logAnalyticsName = 'log-rrida-preprod-001'
param appInsightsName = 'appi-rrida-preprod-001'

// postgres — burstable, no HA, minimum backup window
param postgresAdminLogin = 'rridaadmin'
param postgresAdminPassword = readEnvironmentVariable('RRIDA_PG_ADMIN_PASSWORD')
param postgresDatabaseName = 'rrida'
param postgresSkuName = 'Standard_B2s'
param postgresSkuTier = 'Burstable'
param postgresStorageSizeGB = 64
param postgresVersion = '16'
param postgresBackupRetentionDays = 7
param postgresGeoRedundantBackup = 'Disabled'
param postgresHighAvailability = 'Disabled'
param allowAzureServices = true

// The pipeline opens and closes its own firewall rule around migrations. Add
// office/home IPs here to reach the server with psql by hand.
// param clientIpAllowList = [ { name: 'office', startIp: '1.2.3.4', endIp: '1.2.3.4' } ]
param clientIpAllowList = []

// app — ASPNETCORE_ENVIRONMENT=PreProd loads appsettings.PreProd.json and shows
// Swagger at / (filtered by Swagger:VisibleControllers).
param appServicePlanSku = {
  name: 'B2'
  tier: 'Basic'
}
param environmentNameSetting = 'PreProd'
param aspNetCoreEnvironment = 'PreProd'
param connectionStringSecretName = 'RRIDA-PREPROD'
param createStagingSlot = false
param healthCheckPath = '/health'
param keyVaultPurgeProtection = false

// Pre-Prod frontend. Sets the email links, the Stripe return pages and the
// first CORS origin on the web app, matching appsettings.PreProd.json.
// Override per deployment with RRIDA_FRONTEND_BASE_URL if the URL changes.
param frontendBaseUrl = readEnvironmentVariable('RRIDA_FRONTEND_BASE_URL', 'https://rrida-uat.azurewebsites.net')

// The pipeline reads its own service principal object id from its access token
// and exports it, so nothing is hard-coded here. It gets get/list/set on the
// vault (access policy) because it writes RRIDA-PREPROD and copies the shared
// application secrets on the first deploy.
param pipelinePrincipalId = readEnvironmentVariable('RRIDA_PIPELINE_PRINCIPAL_ID', '')

// Developers who seed the vault by hand (object id from: az ad signed-in-user
// show --query id -o tsv). Listed here so a deploy does not drop their access,
// since every deploy rewrites the vault's access-policy list.
param vaultWriterObjectIds = [
  '727c4f43-ff3c-4df7-a439-23ead3ba0efb' // Muhammad.N.Anjum@cloverera.com
]
param pipelineCanWriteSecrets = true
