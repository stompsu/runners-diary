// User-assigned managed identities, one per role (ADR 0031 §1).
param location string
param tags object
@description('Only with Azure Maps display (plan G9): the identity attached to the Maps account that signs SAS tokens.')
param enableAzureMapsDisplay bool
param mapsLocation string

resource api 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-rd-api'
  location: location
  tags: tags
}

resource web 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-rd-web'
  location: location
  tags: tags
}

resource migrator 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-rd-migrator'
  location: location
  tags: tags
}

resource deployer 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-rd-deployer'
  location: location
  tags: tags
}

resource mapsSas 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = if (enableAzureMapsDisplay) {
  name: 'id-rd-maps-sas'
  location: mapsLocation
  tags: tags
}

output apiId string = api.id
output apiName string = api.name
output apiClientId string = api.properties.clientId
output apiPrincipalId string = api.properties.principalId
output webId string = web.id
output webName string = web.name
output webPrincipalId string = web.properties.principalId
output migratorId string = migrator.id
output migratorName string = migrator.name
output migratorClientId string = migrator.properties.clientId
output migratorPrincipalId string = migrator.properties.principalId
output deployerName string = deployer.name
output deployerClientId string = deployer.properties.clientId
output deployerPrincipalId string = deployer.properties.principalId
output mapsSasId string = enableAzureMapsDisplay ? mapsSas!.id : ''
output mapsSasPrincipalId string = enableAzureMapsDisplay ? mapsSas!.properties.principalId : ''
