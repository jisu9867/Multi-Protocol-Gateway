param namePrefix string
param location string

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: take(replace('${namePrefix}acr', '-', ''), 50)
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
    anonymousPullEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

output id string = acr.id
output loginServer string = acr.properties.loginServer
output adminUsername string = acr.listCredentials().username
output adminPassword string = acr.listCredentials().passwords[0].value
