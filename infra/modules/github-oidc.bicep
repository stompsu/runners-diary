// Federated credential + least-privilege roles for the CI deployer identity (ADR 0030 §7, 0031 §1).
// The deployer can push images and update/start apps and jobs. It holds no Key Vault, SQL, Foundry or role-assignment rights.
param deployerName string
param registryName string
param githubRepository string
@description('Numeric owner id; with githubRepositoryId it selects GitHub\'s immutable OIDC subject format.')
param githubOwnerId string = ''
param githubRepositoryId string = ''
param githubEnvironment string
param managedIdentityNames array

var acrPushRole = '8311e382-0749-4cb8-b61a-304f252e45ec'
var containerAppsContributorRole = '358470bc-b998-42bd-ab17-a7e34c199c0f'
var containerAppsJobsContributorRole = '4e3d2b60-56ae-4dc6-a233-09c8e5a82e68'
var managedIdentityOperatorRole = 'f1a07417-d97a-45cb-824c-7a7467783830'
var readerRole = 'acdd72a7-3385-48ef-bd42-f606fba81ae7'

resource deployer 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: deployerName
}

resource registry 'Microsoft.ContainerRegistry/registries@2025-04-01' existing = {
  name: registryName
}

// GitHub sends repo:<owner>@<ownerId>/<repo>@<repoId>:... when the repository uses immutable subjects (its default since
// 2026); Entra needs an exact match (AADSTS700213 otherwise). The ids also stop a renamed or re-created repo from matching.
var repoParts = split(githubRepository, '/')
var subjectRepo = empty(githubOwnerId) || empty(githubRepositoryId)
  ? githubRepository
  : '${repoParts[0]}@${githubOwnerId}/${repoParts[1]}@${githubRepositoryId}'

resource federatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: deployer
  name: 'github-${githubEnvironment}'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${subjectRepo}:environment:${githubEnvironment}'
    audiences: ['api://AzureADTokenExchange']
  }
}

resource acrPush 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, deployer.id, acrPushRole)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPushRole)
    principalId: deployer.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource appsContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deployer.id, containerAppsContributorRole)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', containerAppsContributorRole)
    principalId: deployer.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource jobsContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deployer.id, containerAppsJobsContributorRole)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', containerAppsJobsContributorRole)
    principalId: deployer.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource reader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deployer.id, readerRole)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', readerRole)
    principalId: deployer.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource targetIdentities 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = [for identityName in managedIdentityNames: {
  name: identityName
}]

resource identityOperator 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (identityName, i) in managedIdentityNames: {
  name: guid(targetIdentities[i].id, deployer.id, managedIdentityOperatorRole)
  scope: targetIdentities[i]
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', managedIdentityOperatorRole)
    principalId: deployer.properties.principalId
    principalType: 'ServicePrincipal'
  }
}]
