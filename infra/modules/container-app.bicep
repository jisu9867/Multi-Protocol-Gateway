param name string
param location string
param containerAppsEnvironmentId string
param image string
param targetPort int
param ingressExternal bool
param minReplicas int
param maxReplicas int
param cpu float
param memory string
param registryServer string
param appInsightsConnectionString string
param secrets array
param env array
param ingressAllowedCidrs array = []

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        external: ingressExternal
        targetPort: targetPort
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
        ipSecurityRestrictions: [
          for cidr in ingressAllowedCidrs: {
            name: 'allow-${replace(cidr, '/', '-')}'
            ipAddressRange: cidr
            action: 'Allow'
          }
        ]
      }
      registries: empty(registryServer) ? [] : [
        {
          server: registryServer
          identity: 'system'
        }
      ]
      secrets: concat(
        [
          for s in secrets: if (contains(s, 'value')) {
            name: s.name
            value: s.value
          }
        ],
        [
          for s in secrets: if (contains(s, 'keyVaultUrl')) {
            name: s.name
            keyVaultUrl: s.keyVaultUrl
            identity: s.identity
          }
        ]
      )
    }
    template: {
      containers: [
        {
          name: 'main'
          image: image
          env: concat(
            [
              {
                name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
                value: appInsightsConnectionString
              }
            ],
            [
              for e in env: if (contains(e, 'value')) {
                name: e.name
                value: string(e.value)
              }
            ],
            [
              for e in env: if (contains(e, 'secretRef')) {
                name: e.name
                secretRef: e.secretRef
              }
            ]
          )
          resources: {
            cpu: cpu
            memory: memory
          }
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}

output id string = app.id
output name string = app.name
output url string = ingressExternal ? 'https://${app.properties.configuration.ingress.fqdn}' : ''
output fqdn string = app.properties.configuration.ingress.fqdn
output identityPrincipalId string = app.identity.principalId
