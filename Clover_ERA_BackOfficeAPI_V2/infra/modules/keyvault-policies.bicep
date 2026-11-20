@description('Grants secret access on a NON-RBAC (access policy) vault. Used where the deploying identity may not create role assignments — a service connection scoped to one resource group with Contributor rights can still set access policies.')
param keyVaultName string

@description('Principal ids that read secrets (the API app identity, its staging slot).')
param readerPrincipalIds array = []

@description('Principal ids that read AND write secrets (the CI/CD identity, which seeds the vault).')
param writerPrincipalIds array = []

var readerPolicies = [for principalId in readerPrincipalIds: {
  tenantId: subscription().tenantId
  objectId: principalId
  permissions: {
    secrets: [ 'get', 'list' ]
  }
}]

var writerPolicies = [for principalId in writerPrincipalIds: {
  tenantId: subscription().tenantId
  objectId: principalId
  permissions: {
    secrets: [ 'get', 'list', 'set' ]
  }
}]

// "add" merges these entries into the vault's existing policy list, so repeat
// deployments never drop a policy somebody added by hand.
resource policies 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01' = {
  name: '${keyVaultName}/add'
  properties: {
    accessPolicies: union(readerPolicies, writerPolicies)
  }
}
