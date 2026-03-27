@allowed([
  'dev'
  'qa'
  'prod'
])
param environment string

param location string = 'westeurope'

// Dev/QA: pass 'F1' to create an inline Free-tier Windows plan.
// Prod: leave at default — the inline plan is skipped when sharedPlanName is set.
@allowed([
  'F1'
])
param planSkuName string = 'F1'

// Declare planSkuTier so .bicepparam files that pass it are valid.
// Unused for prod (sharedPlanName takes effect); used for dev/qa inline plan.
param planSkuTier string = 'Free'

// Provide the name of the pre-existing shared App Service Plan for prod.
// Leave empty for dev/qa — an F1 Windows plan is created inline instead.
param sharedPlanName string = ''

var namePrefix = 'changsta-ai-mixrec'
var webAppName = '${namePrefix}-${environment}'
var storageAccountName = 'stchangstamixrec${environment}'
var logAnalyticsName = 'log-${namePrefix}-${environment}'
var appInsightsName = 'appi-${namePrefix}-${environment}'
var useSharedPlan = !empty(sharedPlanName)
var aspnetcoreEnvironment = environment == 'prod' ? 'Production' : (environment == 'qa' ? 'QA' : 'Development')

// Dev/QA only: F1 Windows plan created inline.
// Skipped for prod — the shared B1 Linux plan (infra/shared/plan-prod.bicep) is used instead.
resource inlinePlan 'Microsoft.Web/serverfarms@2022-09-01' = if (!useSharedPlan) {
  name: 'asp-${namePrefix}-${environment}'
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

// Resolve the plan ID:
//   prod  → shared plan (pre-deployed via infra/shared/plan-prod.bicep)
//   dev/qa → inline F1 plan above
var resolvedPlanId = useSharedPlan
  ? resourceId('Microsoft.Web/serverfarms', sharedPlanName)
  : inlinePlan.id

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
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

module webapp 'modules/webapp.bicep' = {
  name: 'deploy-webapp-${environment}'
  params: {
    location: location
    webAppName: webAppName
    appServicePlanId: resolvedPlanId
    isLinux: useSharedPlan
    alwaysOn: useSharedPlan
    appSettings: [
      {
        name: 'ASPNETCORE_ENVIRONMENT'
        value: aspnetcoreEnvironment
      }
      {
        name: 'Azure__BlobCatalog__ServiceEndpoint'
        value: storageAccount.properties.primaryEndpoints.blob
      }
      {
        name: 'Azure__BlobCatalog__ContainerName'
        value: 'mix-catalog'
      }
      {
        name: 'Azure__BlobCatalog__BlobName'
        value: 'catalog.json'
      }
      {
        name: 'RateLimiting__TrustCloudflareHeader'
        value: 'true'
      }
      {
        name: 'ApplicationInsights__ConnectionString'
        value: appInsights.properties.ConnectionString
      }
    ]
  }
}

// Grant the App Service's system-assigned identity permission to read and write blobs.
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource blobRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(storageAccount.id, webAppName, storageBlobDataContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: webapp.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

output webAppName string = webapp.outputs.webAppName
output defaultHostName string = webapp.outputs.defaultHostName
output appServicePlanId string = resolvedPlanId
output storageAccountName string = storageAccount.name
output appInsightsName string = appInsights.name
