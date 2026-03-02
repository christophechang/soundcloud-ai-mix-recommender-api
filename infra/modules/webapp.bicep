param location string
param webAppName string
param appServicePlanId string
param isLinux bool = false
param alwaysOn bool = false
param appSettings array = []

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: isLinux ? 'app,linux' : 'app'
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      alwaysOn: alwaysOn
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      linuxFxVersion: isLinux ? 'DOTNETCORE|10.0' : ''
      appSettings: appSettings
    }
  }
}

output webAppName string = webApp.name
output defaultHostName string = webApp.properties.defaultHostName
