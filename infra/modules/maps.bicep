// Azure Maps Gen2 account in a region that supports managed identities (ADR 0033).
param location string
param tags object
param stage string
param apiPrincipalId string
param enableAzureMapsDisplay bool
param webOrigin string
@description('Only with display: the identity that signs SAS tokens and is attached to the account.')
param sasIdentityId string
param sasIdentityPrincipalId string

var mapsSearchRenderReaderRole = '6be48352-4f82-47c9-ad5e-0acacefdb005'
var managedIdentityOperatorRole = 'f1a07417-d97a-45cb-824c-7a7467783830'

resource account 'Microsoft.Maps/accounts@2023-06-01' = {
  name: 'map-rd-${stage}'
  location: location
  tags: tags
  kind: 'Gen2'
  sku: { name: 'G2' }
  identity: enableAzureMapsDisplay ? {
    type: 'UserAssigned'
    userAssignedIdentities: { '${sasIdentityId}': {} }
  } : null
  properties: {
    // SAS tokens need local auth (ADR 0031 §3). Without display, geocoding uses Entra and keys are off.
    disableLocalAuth: !enableAzureMapsDisplay
    cors: {
      corsRules: [
        { allowedOrigins: [webOrigin] }
      ]
    }
  }
}

resource searchReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(account.id, 'api', mapsSearchRenderReaderRole)
  scope: account
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', mapsSearchRenderReaderRole)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Display only (plan G9): custom roles, scoped to this resource group.
resource sasIssuerRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = if (enableAzureMapsDisplay) {
  name: guid(resourceGroup().id, 'RD Maps SAS Issuer')
  properties: {
    roleName: 'RD Maps SAS Issuer (${stage})'
    description: 'Create SAS tokens for the Runners Diary Azure Maps account.'
    type: 'CustomRole'
    permissions: [
      { actions: ['Microsoft.Maps/accounts/read', 'Microsoft.Maps/accounts/listSas/action'] }
    ]
    assignableScopes: [resourceGroup().id]
  }
}

resource renderReaderRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = if (enableAzureMapsDisplay) {
  name: guid(resourceGroup().id, 'RD Maps Render Reader')
  properties: {
    roleName: 'RD Maps Render Reader (${stage})'
    description: 'Read Azure Maps render (tile) data; assigned to the SAS identity.'
    type: 'CustomRole'
    permissions: [
      { dataActions: ['Microsoft.Maps/accounts/services/render/read'] }
    ]
    assignableScopes: [resourceGroup().id]
  }
}

resource sasIssuerAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableAzureMapsDisplay) {
  name: guid(account.id, 'api', 'sas-issuer')
  scope: account
  properties: {
    roleDefinitionId: sasIssuerRole.id
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource renderReaderAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableAzureMapsDisplay) {
  name: guid(account.id, 'sas', 'render-reader')
  scope: account
  properties: {
    roleDefinitionId: renderReaderRole.id
    principalId: sasIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// SAS creation (listSas) with a user-assigned identity also needs rights on that identity (assign-identity check).
resource sasIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = if (enableAzureMapsDisplay) {
  name: 'id-rd-maps-sas'
}

resource sasIdentityOperator 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableAzureMapsDisplay) {
  name: guid(resourceGroup().id, 'api', 'maps-sas-identity-operator')
  scope: sasIdentity
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', managedIdentityOperatorRole)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output id string = account.id
output name string = account.name
output clientId string = account.properties.uniqueId
