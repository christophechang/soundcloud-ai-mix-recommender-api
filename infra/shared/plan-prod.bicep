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
