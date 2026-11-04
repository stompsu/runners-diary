// Container Apps environment: Consumption only, no VNet, logs to Log Analytics (ADR 0030 §4).
param location string
param tags object
param stage string
param workspaceName string

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' existing = {
  name: workspaceName
}

resource environment 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: 'cae-rd-${stage}'
  location: location
  tags: tags
  properties: {
    // Consumption profile only: no dedicated profile, so no environment management meter (plan §8).
    workloadProfiles: [
      { name: 'Consumption', workloadProfileType: 'Consumption' }
    ]
    zoneRedundant: false
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        // Resolved at deployment time only; never written to a file or output.
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
  }
}

output id string = environment.id
output name string = environment.name
output defaultDomain string = environment.properties.defaultDomain
