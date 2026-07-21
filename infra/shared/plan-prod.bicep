// Documentation / idempotent redeployment only.
// This plan already exists in the prod resource group. Do not recreate unless
// intentionally re-provisioning. The API's web apps run on it, so deleting it
// takes production down.
// Deploy with:
//   az deployment group create --resource-group prod \
//     --template-file infra/shared/plan-prod.bicep \
//     --parameters infra/shared/plan-prod.bicepparam

param location string = 'westeurope'

var planName = 'asp-changsta-ai-mixrec-prod'

resource plan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
    capacity: 1
  }
  properties: {
    reserved: true
  }
}

output planId string = plan.id
output planName string = plan.name
