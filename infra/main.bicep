@allowed([
  'dev'
  'qa'
  'prod'
])
param environment string

param location string = 'westeurope'

// Enforced Free tier to guarantee zero cost on PAYG subscription
@allowed([
  'F1'
])
param planSkuName string

var namePrefix = 'changsta-ai-mixrec'
var planName = 'asp-${namePrefix}-${environment}'
var webAppName = '${namePrefix}-${environment}'

// Basic tier name for B1, Free tier name for F1
var planSkuTier = planSkuName == 'B1' ? 'Basic' : 'Free'

resource plan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: planName
  location: location
  sku: {
    name: planSkuName
    tier: planSkuTier
    capacity: 1
  }
  properties: {
    reserved: false
  }
}

resource web 'Microsoft.Web/sites@2022-09-01' = {
  name: webAppName
  location: location
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      // AlwaysOn only really matters for paid SKUs
      alwaysOn: planSkuName == 'B1'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: environment == 'prod' ? 'Production' : (environment == 'qa' ? 'QA' : 'Development')
        }
      ]
    }
  }
}

output webAppName string = web.name
output defaultHostName string = web.properties.defaultHostName
output appServicePlanName string = plan.name