targetScope = 'resourceGroup'

@description('Workload short name used for naming resources.')
param workloadName string = 'gateway'

@allowed([
  'dev'
  'prod'
])
param environment string

@description('Azure region. Korea Central by default.')
param location string = 'koreacentral'

@description('Container image for Gateway API.')
param apiImage string

@description('Container image for Gateway UI.')
param uiImage string

@description('Container image for Mosquitto broker.')
param mqttImage string = 'eclipse-mosquitto:2.0'

@description('PostgreSQL administrator username.')
param postgresAdminUsername string = 'gatewayadmin'

@secure()
@description('PostgreSQL administrator password.')
param postgresAdminPassword string

@description('CIDR ranges allowed to access UI ingress.')
param allowedUiCidrs array = []

@description('Monthly budget limit in USD.')
param monthlyBudgetUsd int = 200

@description('Log Analytics retention in days.')
param logRetentionDays int = 30

@description('Container Apps minimum replicas for API.')
param apiMinReplicas int = 0

@description('Container Apps minimum replicas for UI.')
param uiMinReplicas int = 0

@description('Container Apps minimum replicas for MQTT.')
param mqttMinReplicas int = 1

var namePrefix = '${workloadName}-${environment}-kr'
var keyVaultName = take(replace('${namePrefix}-kv', '_', '-'), 24)

module network './modules/network.bicep' = {
  name: 'network'
  params: {
    allowedUiCidrs: allowedUiCidrs
  }
}

module acr './modules/acr.bicep' = {
  name: 'acr'
  params: {
    namePrefix: namePrefix
    location: location
  }
}

module monitoring './modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    namePrefix: namePrefix
    location: location
    logRetentionDays: logRetentionDays
  }
}

module keyVault './modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    namePrefix: namePrefix
    location: location
  }
}

module eventHubs './modules/eventhubs.bicep' = {
  name: 'eventhubs'
  params: {
    namePrefix: namePrefix
    location: location
    eventHubName: 'telemetry-events'
  }
}

module postgres './modules/postgres.bicep' = {
  name: 'postgres'
  params: {
    namePrefix: namePrefix
    location: location
    adminUsername: postgresAdminUsername
    adminPassword: postgresAdminPassword
    databaseName: 'gateway'
    skuName: environment == 'prod' ? 'Standard_B2s' : 'Standard_B1ms'
  }
}

module containerAppsEnv './modules/container-apps-env.bicep' = {
  name: 'containerapps-env'
  params: {
    namePrefix: namePrefix
    location: location
    logAnalyticsCustomerId: monitoring.outputs.workspaceCustomerId
    logAnalyticsSharedKey: monitoring.outputs.workspaceSharedKey
  }
}

resource keyVaultResource 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource eventHubSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVaultResource
  name: 'kafka-sasl-password'
  properties: {
    value: eventHubs.outputs.connectionString
  }
}

resource postgresSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVaultResource
  name: 'postgres-connection-string'
  properties: {
    value: postgres.outputs.connectionString
  }
}

module apiApp './modules/container-app.bicep' = {
  name: 'api-app'
  params: {
    name: '${namePrefix}-api'
    location: location
    containerAppsEnvironmentId: containerAppsEnv.outputs.id
    image: apiImage
    targetPort: 8080
    ingressExternal: false
    minReplicas: apiMinReplicas
    maxReplicas: 2
    cpu: '0.5'
    memory: '1Gi'
    registryServer: acr.outputs.loginServer
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    secrets: [
      {
        name: 'postgres-connection-string'
        keyVaultUrl: '${keyVault.outputs.vaultUri}secrets/postgres-connection-string'
        identity: 'system'
      }
      {
        name: 'kafka-sasl-password'
        keyVaultUrl: '${keyVault.outputs.vaultUri}secrets/kafka-sasl-password'
        identity: 'system'
      }
    ]
    env: [
      {
        name: 'ASPNETCORE_ENVIRONMENT'
        value: 'Production'
      }
      {
        name: 'ConnectionStrings__DefaultConnection'
        secretRef: 'postgres-connection-string'
      }
      {
        name: 'Adapters__Mqtt__Enabled'
        value: 'true'
      }
      {
        name: 'Adapters__Mqtt__Server'
        value: '${namePrefix}-mqtt'
      }
      {
        name: 'Adapters__Mqtt__Port'
        value: '1883'
      }
      {
        name: 'Adapters__Mqtt__Topic'
        value: 'factory/+/+/telemetry'
      }
      {
        name: 'Kafka__BootstrapServers'
        value: eventHubs.outputs.bootstrapServers
      }
      {
        name: 'Kafka__Topic'
        value: eventHubs.outputs.eventHubName
      }
      {
        name: 'Kafka__ConsumerGroupId'
        value: '${workloadName}-${environment}-consumer'
      }
      {
        name: 'Kafka__SecurityProtocol'
        value: 'SaslSsl'
      }
      {
        name: 'Kafka__SaslMechanism'
        value: 'Plain'
      }
      {
        name: 'Kafka__SaslUsername'
        value: '$ConnectionString'
      }
      {
        name: 'Kafka__SaslPassword'
        secretRef: 'kafka-sasl-password'
      }
      {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        value: monitoring.outputs.appInsightsConnectionString
      }
    ]
  }
  dependsOn: [
    eventHubSecret
    postgresSecret
  ]
}

module uiApp './modules/container-app.bicep' = {
  name: 'ui-app'
  params: {
    name: '${namePrefix}-ui'
    location: location
    containerAppsEnvironmentId: containerAppsEnv.outputs.id
    image: uiImage
    targetPort: 8080
    ingressExternal: true
    ingressAllowedCidrs: network.outputs.allowedUiCidrs
    minReplicas: uiMinReplicas
    maxReplicas: 2
    cpu: '0.5'
    memory: '1Gi'
    registryServer: acr.outputs.loginServer
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    secrets: []
    env: [
      {
        name: 'ASPNETCORE_ENVIRONMENT'
        value: 'Production'
      }
      {
        name: 'GatewayApi__BaseUrl'
        value: 'https://${apiApp.outputs.fqdn}'
      }
      {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        value: monitoring.outputs.appInsightsConnectionString
      }
    ]
  }
}

module mqttApp './modules/container-app.bicep' = {
  name: 'mqtt-app'
  params: {
    name: '${namePrefix}-mqtt'
    location: location
    containerAppsEnvironmentId: containerAppsEnv.outputs.id
    image: mqttImage
    targetPort: 1883
    ingressExternal: true
    minReplicas: mqttMinReplicas
    maxReplicas: 1
    cpu: '0.25'
    memory: '0.5Gi'
    registryServer: ''
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    secrets: []
    env: []
  }
}

module apiKvRbac './modules/rbac.bicep' = {
  name: 'api-kv-rbac'
  params: {
    principalId: apiApp.outputs.identityPrincipalId
    roleDefinitionId: '4633458b-17de-408a-b874-0445c86b69e6'
    keyVaultName: keyVault.outputs.name
  }
}

module budget './modules/budget.bicep' = {
  name: 'budget'
  params: {
    budgetName: '${namePrefix}-monthly-budget'
    amount: monthlyBudgetUsd
    contactEmails: ['jisu986702@naver.com']
  }
}

output apiUrl string = apiApp.outputs.url
output uiUrl string = uiApp.outputs.url
output mqttHost string = mqttApp.outputs.fqdn
output keyVaultName string = keyVault.outputs.name
output eventHubNamespace string = eventHubs.outputs.namespaceName
output postgresServer string = postgres.outputs.serverName
