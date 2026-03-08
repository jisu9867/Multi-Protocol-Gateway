param namePrefix string
param location string

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: take(replace('${namePrefix}-kv', '_', '-'), 24)
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenant().tenantId
    enableRbacAuthorization: true
    enabledForDeployment: false
    enabledForTemplateDeployment: true
    enabledForDiskEncryption: false
    publicNetworkAccess: 'Enabled'
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
  }
}

output id string = kv.id
output name string = kv.name
output vaultUri string = kv.properties.vaultUri
