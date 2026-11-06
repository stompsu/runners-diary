// Foundry (AIServices) account with one Global Standard model deployment, keyless (ADR 0027 amended 2026-11-04, 0031),
// plus one Foundry project so the new Foundry portal lists the resource (it shows project-enabled resources only).
param location string
param tags object
param token string
param apiPrincipalId string
@description('azd principal for the live eval from a laptop (plan G6). Empty = skip.')
param userPrincipalId string
param userPrincipalType string
param grantDeveloperAccess bool
param deploymentName string = 'gpt-54-mini'
param modelName string = 'gpt-5.4-mini'
param modelVersion string = '2026-03-17'
@description('Capacity in thousands of tokens per minute (1 = 1,000 TPM).')
param capacityKtpm int

var cognitiveServicesUserRole = 'a97b65f3-24c7-4388-baec-2e87135dc908'

resource account 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: 'ais-rd-${token}'
  location: location
  tags: tags
  kind: 'AIServices'
  sku: { name: 'S0' }
  // Required with project management; it gets no role assignments (the API calls the model with its own identity).
  identity: { type: 'SystemAssigned' }
  properties: {
    customSubDomainName: 'ais-rd-${token}'
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
    allowProjectManagement: true
  }
}

// A project only for portal visibility (deployments, metrics, playground). The app does not use it: no agents, no connections.
// It waits for the model deployment: the account accepts one child operation at a time, and running both in parallel
// failed the first provision with RequestConflict ("Another operation is in progress").
resource project 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: account
  name: 'runnersdiary'
  location: location
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {
    displayName: 'Runners Diary'
    description: 'Training Insights summaries (gpt-5.4-mini). Portal view of the production Foundry resource.'
  }
  dependsOn: [model]
}

resource model 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: account
  name: deploymentName
  sku: { name: 'GlobalStandard', capacity: capacityKtpm }
  properties: {
    model: { format: 'OpenAI', name: modelName, version: modelVersion }
    versionUpgradeOption: 'NoAutoUpgrade'
  }
}

resource apiUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(account.id, 'api', cognitiveServicesUserRole)
  scope: account
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRole)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource developerUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (grantDeveloperAccess && !empty(userPrincipalId)) {
  name: guid(account.id, 'user', cognitiveServicesUserRole)
  scope: account
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRole)
    principalId: userPrincipalId
    principalType: userPrincipalType
  }
}

output name string = account.name
output openAiEndpoint string = account.properties.endpoints['OpenAI Language Model Instance API']
output deploymentName string = model.name
