@description('Least-privilege role grants for the app identities. Runs after the identities exist.')
param keyVaultName string

@description('Principal ids that read secrets from Key Vault (API, staging slot).')
param keyVaultReaderPrincipalIds array = []

@description('Principal ids that also write secrets (the Pre-Prod pipeline seeds its own vault).')
param keyVaultOfficerPrincipalIds array = []

// The storage role grants that used to live here (Blob Data Owner, Queue Data
// Contributor, Table Data Contributor) existed only for the Functions runtime's
// identity-based AzureWebJobsStorage access. The Function App and its storage
// account went with CloverBackoffice.Jobs, so nothing needs those roles now.

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

var keyVaultSecretsUser = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var keyVaultSecretsOfficer = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')

resource kvSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in keyVaultReaderPrincipalIds: {
  name: guid(vault.id, principalId, keyVaultSecretsUser)
  scope: vault
  properties: {
    roleDefinitionId: keyVaultSecretsUser
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

resource kvSecretsOfficer 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in keyVaultOfficerPrincipalIds: {
  name: guid(vault.id, principalId, keyVaultSecretsOfficer)
  scope: vault
  properties: {
    roleDefinitionId: keyVaultSecretsOfficer
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]
