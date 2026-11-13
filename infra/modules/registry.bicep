// Container Registry Basic, admin user off, pulls through managed identity (ADR 0030/0031).
param location string
param tags object
param token string
param apiPrincipalId string
param webPrincipalId string
param migratorPrincipalId string
param raceImportPrincipalId string

var acrPullRole = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource registry 'Microsoft.ContainerRegistry/registries@2025-04-01' = {
  name: 'acrrd${token}'
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
    anonymousPullEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

resource pullApi 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, 'api', acrPullRole)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRole)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource pullWeb 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, 'web', acrPullRole)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRole)
    principalId: webPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource pullMigrator 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, 'migrator', acrPullRole)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRole)
    principalId: migratorPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource pullRaceImport 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, 'race-import', acrPullRole)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRole)
    principalId: raceImportPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output name string = registry.name
output loginServer string = registry.properties.loginServer
