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
var storageAccountName = 'stchangstamixrec${environment}'

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
        {
          name: 'Azure__BlobCatalog__ConnectionString'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${az.environment().suffixes.storage}'
        }
        {
          name: 'Azure__BlobCatalog__ContainerName'
          value: 'mix-catalog'
        }
        {
          name: 'Azure__BlobCatalog__BlobName'
          value: 'catalog.json'
        }
      ]
    }
  }
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource catalogContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'mix-catalog'
  properties: {
    publicAccess: 'None'
  }
}

output webAppName string = web.name
output defaultHostName string = web.properties.defaultHostName
output appServicePlanName string = plan.name
output storageAccountName string = storageAccount.name