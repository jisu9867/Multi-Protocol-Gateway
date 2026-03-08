param namePrefix string
param location string
param eventHubName string

resource namespace 'Microsoft.EventHub/namespaces@2024-01-01' = {
  name: '${namePrefix}-eh'
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
    capacity: 1
  }
  properties: {
    isAutoInflateEnabled: false
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource eventHub 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' = {
  name: '${namespace.name}/${eventHubName}'
  properties: {
    messageRetentionInDays: 1
    partitionCount: 2
    status: 'Active'
  }
}

resource authRule 'Microsoft.EventHub/namespaces/authorizationRules@2024-01-01' = {
  name: '${namespace.name}/RootManageSharedAccessKey'
  properties: {
    rights: [
      'Listen'
      'Send'
      'Manage'
    ]
  }
}

var keys = listKeys(authRule.id, '2024-01-01')
var endpoint = 'Endpoint=sb://${namespace.name}.servicebus.windows.net/;SharedAccessKeyName=${authRule.name};SharedAccessKey=${keys.primaryKey};EntityPath=${eventHubName}'

output namespaceName string = namespace.name
output eventHubName string = eventHub.name
output bootstrapServers string = '${namespace.name}.servicebus.windows.net:9093'
output connectionString string = endpoint
