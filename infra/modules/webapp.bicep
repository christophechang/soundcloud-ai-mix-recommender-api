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
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      alwaysOn: alwaysOn
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      linuxFxVersion: isLinux ? 'DOTNETCORE|10.0' : ''
      // The app maps /health (Program.cs); wiring it here lets the platform auto-heal an
      // unhealthy instance instead of leaving a wedged process running. See issue #91.
      healthCheckPath: '/health'
      appSettings: appSettings
    }
  }
}

output webAppName string = webApp.name
output defaultHostName string = webApp.properties.defaultHostName
output principalId string = webApp.identity.principalId
