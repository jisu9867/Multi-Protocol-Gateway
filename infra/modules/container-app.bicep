param name string
param location string
param containerAppsEnvironmentId string
param image string
param targetPort int
param ingressExternal bool
param minReplicas int
param maxReplicas int
param cpu string
param memory string
param registryServer string
param userAssignedIdentityResourceId string = ''
param appInsightsConnectionString string
param secrets array
param env array
param ingressAllowedCidrs array = []

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  identity: {
    type: empty(userAssignedIdentityResourceId) ? 'SystemAssigned' : 'SystemAssigned,UserAssigned'
    userAssignedIdentities: empty(userAssignedIdentityResourceId) ? null : {
      '${userAssignedIdentityResourceId}': {}
    }
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
          ...(empty(userAssignedIdentityResourceId) ? {
            identity: 'system'
          } : {
            identity: userAssignedIdentityResourceId
          })
        }
      ]
      secrets: secrets
    }
    template: {
      containers: [
        {
          name: 'main'
          image: image
          env: concat([
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsightsConnectionString
            }
          ], env)
          resources: {
            cpu: json(cpu)
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
