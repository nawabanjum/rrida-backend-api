@description('Key Vault name. Globally unique, 3-24 chars.')
param keyVaultName string

param location string
param tags object

@description('Purge protection cannot be turned off once enabled. Prod: true. Dev and Pre-Prod: false.')
param enablePurgeProtection bool = false

@description('Days a deleted secret is recoverable.')
@minValue(7)
@maxValue(90)
param softDeleteRetentionInDays int = 90

@description('RBAC (role assignments) when true � Dev and Prod. Access policies when false, for Pre-Prod, whose service connection is scoped to one resource group and cannot create role assignments.')
param enableRbacAuthorization bool = true

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    // No secret VALUES are ever set here, whichever access model is used.
    enableRbacAuthorization: enableRbacAuthorization
    // Required by the API even when the list is empty ("The parameter
    // accessPolicies is not specified" otherwise). ARM replaces the whole list
    // on every deployment, so entries added afterwards by
    // keyvault-policies.bicep are re-added on each deploy, and an entry granted
    // by hand (a developer seeding the vault) is dropped — re-grant it with
    // seed-keyvault-from-user-secrets.ps1 -GrantSelfAccess when needed.
    accessPolicies: []
    enableSoftDelete: true
    softDeleteRetentionInDays: softDeleteRetentionInDays
    enablePurgeProtection: enablePurgeProtection ? true : null
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

output vaultName string = vault.name
output vaultUri string = vault.properties.vaultUri
output vaultId string = vault.id
