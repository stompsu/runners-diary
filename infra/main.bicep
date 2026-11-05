// Runners Diary: all Azure resources for one environment (ADR 0030). Subscription scope: creates the resource group.
// Provisioned only by `azd provision`, run by the user after an explicit confirmation (plan gates G1/G2).
targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('azd environment name (AZURE_ENV_NAME), e.g. rd-prod. Used for tags and resource name uniqueness.')
param environmentName string

@minLength(1)
@description('Primary region (AZURE_LOCATION), e.g. westeurope. Everything lives here except the Maps account and, when SQL_LOCATION is set, the SQL server.')
param location string

@allowed(['dev', 'prod'])
@description('Stage suffix in resource names (rg-runnersdiary-<stage>). Local Aspire is the dev environment; the cloud has prod only.')
param stage string = 'prod'

@description('Optional custom domain for the web app (CUSTOM_DOMAIN). DNS first: CNAME to the web FQDN and TXT asuid.<name> = the environment verification id. A free managed certificate is issued and bound automatically.')
param webCustomDomain string = ''

@description('Region of the Azure SQL server and database (SQL_LOCATION). Empty = the primary region. West Europe was not accepting new SQL servers for this subscription on 2026-11-04 (RegionDoesNotAllowProvisioning), so prod uses polandcentral (westeurope and northeurope are restricted for this subscription per the SQL capabilities API).')
param sqlLocation string = ''

@description('Region of the Azure Maps account. It must support managed identities (not "global"); West/North Europe sit behind eu.atlas.microsoft.com.')
param mapsLocation string = 'westeurope'

@description('Object id of the person running azd (AZURE_PRINCIPAL_ID); gets Key Vault Secrets Officer and optionally Cognitive Services User.')
param principalId string = ''

@allowed(['User', 'ServicePrincipal'])
param principalType string = 'User'

// ---- Entra External ID (non-secret ids; empty tenant id = guests-only deployment, ADR 0015) ----
param entraTenantId string = ''
param entraTenantSubdomain string = ''
param entraApiClientId string = ''
param entraSpaClientId string = ''
param entraIssuer string = ''

// ---- AI (ADR 0027). Strings because azd parameter files substitute text; converted below. ----
@description('Ai__Enabled kill switch. Stays false until the live eval passed (plan G6 -> G7).')
param aiEnabled string = 'false'
@description('Foundry capacity in thousands of tokens per minute (1 = 1,000 TPM).')
param aiCapacityKtpm string = '30'
param aiModelName string = 'gpt-5.4-mini'
param aiModelVersion string = '2026-03-17'
@description('Give the azd user Cognitive Services User on Foundry for the live eval from a laptop.')
param grantDeveloperAiAccess string = 'true'

// ---- Maps (ADR 0033) ----
@description('Azure Maps display via SAS tokens (plan G9). Default off: MapLibre + OpenFreeMap, Azure Maps only for geocoding.')
param mapsDisplay string = 'false'
param geocodeDailyLimit string = '75'

// ---- Budget (ADR 0034) ----
param budgetAmount string = '25'
@description('E-mail for the budget and alerts. Required; set it with `azd env set BUDGET_CONTACT_EMAIL ...`, never commit it.')
param budgetContactEmail string
@description('First day of a month (yyyy-MM-01). Set once by infra/hooks/preprovision.sh as the azd value BUDGET_START_DATE so re-provisioning does not move the budget.')
@minLength(10)
param budgetStartDate string

// ---- Jobs, CI ----
param guestCleanupCron string = '15 3 * * 1'
param githubRepository string = 'stompsu/runners-diary'
param githubEnvironment string = 'prod'

@description('Numeric GitHub owner and repository ids. When both are set, the federated credential uses GitHub\'s immutable OIDC subject (repo:<owner>@<ownerId>/<repo>@<repoId>:environment:<env>), which the repository sends (use_immutable_subject). Empty = the name-only subject. Public values: GET /users/<owner> and /repos/<owner>/<repo>.')
param githubOwnerId string = '12047598'
param githubRepositoryId string = '1444016204'

@secure()
@description('First provision only: written to Key Vault by the template. infra/hooks/preprovision.sh fills it when the secret does not exist yet.')
param guestSigningKeyBootstrap string = ''

@description('Set by azd (SERVICE_API_RESOURCE_EXISTS): keep the deployed image instead of resetting to the placeholder.')
param apiExists bool = false
param webExists bool = false

var tags = {
  'azd-env-name': environmentName
  app: 'runnersdiary'
}
var token = toLower(uniqueString(subscription().id, environmentName, location))
var placeholderImage = 'mcr.microsoft.com/k8se/quickstart:latest'

var displayEnabled = toLower(mapsDisplay) == 'true'
var aiEnabledValue = toLower(aiEnabled) == 'true' ? 'true' : 'false'
var entraEnabled = !empty(entraTenantId) && !empty(entraTenantSubdomain) && !empty(entraApiClientId)
var cspEntraHost = empty(entraTenantSubdomain) ? '' : 'https://${entraTenantSubdomain}.ciamlogin.com'
var cspMapHosts = displayEnabled
  ? 'https://tiles.openfreemap.org https://eu.atlas.microsoft.com https://atlas.microsoft.com'
  : 'https://tiles.openfreemap.org'

resource rg 'Microsoft.Resources/resourceGroups@2025-04-01' = {
  name: 'rg-runnersdiary-${stage}'
  location: location
  tags: tags
}

module monitoring 'modules/monitoring.bicep' = {
  scope: rg
  name: 'monitoring'
  params: {
    location: location
    tags: tags
    stage: stage
  }
}

module identities 'modules/identities.bicep' = {
  scope: rg
  name: 'identities'
  params: {
    location: location
    tags: tags
    enableAzureMapsDisplay: displayEnabled
    mapsLocation: mapsLocation
  }
}

module registry 'modules/registry.bicep' = {
  scope: rg
  name: 'registry'
  params: {
    location: location
    tags: tags
    token: token
    apiPrincipalId: identities.outputs.apiPrincipalId
    webPrincipalId: identities.outputs.webPrincipalId
    migratorPrincipalId: identities.outputs.migratorPrincipalId
  }
}

module keyvault 'modules/keyvault.bicep' = {
  scope: rg
  name: 'keyvault'
  params: {
    location: location
    tags: tags
    token: token
    apiPrincipalId: identities.outputs.apiPrincipalId
    userPrincipalId: principalId
    userPrincipalType: principalType
    guestSigningKeyBootstrap: guestSigningKeyBootstrap
  }
}

module storage 'modules/storage.bicep' = {
  scope: rg
  name: 'storage'
  params: {
    location: location
    tags: tags
    token: token
    apiPrincipalId: identities.outputs.apiPrincipalId
  }
}

module sql 'modules/sql.bicep' = {
  scope: rg
  name: 'sql'
  params: {
    location: empty(sqlLocation) ? location : sqlLocation
    nameSuffix: empty(sqlLocation) ? '' : sqlLocation
    tags: tags
    token: token
    migratorName: identities.outputs.migratorName
    migratorClientId: identities.outputs.migratorClientId
  }
}

module foundry 'modules/foundry.bicep' = {
  scope: rg
  name: 'foundry'
  params: {
    location: location
    tags: tags
    token: token
    apiPrincipalId: identities.outputs.apiPrincipalId
    userPrincipalId: principalId
    userPrincipalType: principalType
    grantDeveloperAccess: toLower(grantDeveloperAiAccess) == 'true'
    modelName: aiModelName
    modelVersion: aiModelVersion
    capacityKtpm: int(empty(aiCapacityKtpm) ? '30' : aiCapacityKtpm)
  }
}

module acaEnv 'modules/aca-env.bicep' = {
  scope: rg
  name: 'aca-env'
  params: {
    location: location
    tags: tags
    stage: stage
    workspaceName: monitoring.outputs.workspaceName
  }
}

var webFqdn = 'web.${acaEnv.outputs.defaultDomain}'

module maps 'modules/maps.bicep' = {
  scope: rg
  name: 'maps'
  params: {
    location: mapsLocation
    tags: tags
    stage: stage
    apiPrincipalId: identities.outputs.apiPrincipalId
    enableAzureMapsDisplay: displayEnabled
    webOrigin: 'https://${webFqdn}'
    sasIdentityId: identities.outputs.mapsSasId
    sasIdentityPrincipalId: identities.outputs.mapsSasPrincipalId
  }
}

module apiFetch 'modules/fetch-container-image.bicep' = {
  scope: rg
  name: 'fetch-api-image'
  params: { name: 'api', exists: apiExists }
}

module webFetch 'modules/fetch-container-image.bicep' = {
  scope: rg
  name: 'fetch-web-image'
  params: { name: 'web', exists: webExists }
}

var apiImage = empty(apiFetch.outputs.image) ? placeholderImage : apiFetch.outputs.image
var webImage = empty(webFetch.outputs.image) ? placeholderImage : webFetch.outputs.image
// Jobs follow the API release: CI pushes rd-api, rd-web and rd-migrate with the same sha-<sha> tag (ADR 0030 §7).
var migrateImage = contains(apiImage, '/rd-api:') ? replace(apiImage, '/rd-api:', '/rd-migrate:') : placeholderImage

// Settings shared by the API app and the cleanup job (plan §5.2).
var sharedApiEnv = concat(
  [
    { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
    {
      name: 'ConnectionStrings__runnersdiary'
      value: 'Server=tcp:${sql.outputs.serverFqdn},1433;Database=${sql.outputs.databaseName};Authentication=Active Directory Managed Identity;User Id=${identities.outputs.apiClientId};Encrypt=True;Connect Timeout=60'
    }
    { name: 'ConnectionStrings__blobs', value: storage.outputs.blobEndpoint }
    { name: 'AZURE_CLIENT_ID', value: identities.outputs.apiClientId }
    { name: 'ForwardedHeaders__Enabled', value: 'true' }
    { name: 'ForwardedHeaders__ForwardLimit', value: '2' }
    { name: 'ForwardedHeaders__KnownIPNetworks__0', value: '0.0.0.0/0' }
    { name: 'ForwardedHeaders__KnownIPNetworks__1', value: '::/0' }
    { name: 'Ai__Provider', value: 'Foundry' }
    { name: 'Ai__Enabled', value: aiEnabledValue }
    { name: 'Ai__Foundry__Endpoint', value: foundry.outputs.openAiEndpoint }
    { name: 'Ai__Foundry__Deployment', value: foundry.outputs.deploymentName }
    { name: 'Ai__Foundry__ManagedIdentityClientId', value: identities.outputs.apiClientId }
    { name: 'Maps__Provider', value: displayEnabled ? 'AzureMaps' : 'MapLibre' }
    { name: 'Maps__Geocoding__Provider', value: 'AzureMaps' }
    { name: 'Maps__Geocoding__GlobalDailyLimit', value: empty(geocodeDailyLimit) ? '75' : geocodeDailyLimit }
    { name: 'Maps__AzureMaps__AccountClientId', value: maps.outputs.clientId }
    { name: 'Maps__AzureMaps__ManagedIdentityClientId', value: identities.outputs.apiClientId }
    { name: 'Maps__AzureMaps__Domain', value: 'eu.atlas.microsoft.com' }
    { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: monitoring.outputs.appInsightsConnectionString }
  ],
  displayEnabled
    ? [
        { name: 'Maps__AzureMaps__AccountResourceId', value: maps.outputs.id }
        { name: 'Maps__AzureMaps__SasPrincipalId', value: identities.outputs.mapsSasPrincipalId }
        { name: 'Maps__AzureMaps__SasRegions__0', value: 'westeurope' }
        { name: 'Maps__AzureMaps__SasRegions__1', value: 'northeurope' }
        { name: 'Maps__AzureMaps__SasMaxRatePerSecond', value: '10' }
      ]
    : []
)

// Only the API serves tokens, so only it gets the guest key and the Entra settings.
var apiOnlyEnv = concat(
  [
    { name: 'Auth__Guest__SigningKey', secretRef: 'guest-signing-key' }
    { name: 'OTEL_SERVICE_NAME', value: 'api' }
    { name: 'Auth__Entra__Enabled', value: entraEnabled ? 'true' : 'false' }
  ],
  entraEnabled
    ? [
        { name: 'Auth__Entra__TenantId', value: entraTenantId }
        { name: 'Auth__Entra__TenantSubdomain', value: entraTenantSubdomain }
        { name: 'Auth__Entra__Audience', value: entraApiClientId }
      ]
    : [],
  entraEnabled && !empty(entraSpaClientId) ? [{ name: 'Auth__Entra__SpaClientId', value: entraSpaClientId }] : [],
  entraEnabled && !empty(entraIssuer) ? [{ name: 'Auth__Entra__ValidIssuers__0', value: entraIssuer }] : []
)

module api 'modules/app-api.bicep' = {
  scope: rg
  name: 'app-api'
  params: {
    location: location
    tags: tags
    environmentId: acaEnv.outputs.id
    image: apiImage
    usePlaceholder: apiImage == placeholderImage
    identityId: identities.outputs.apiId
    registryServer: registry.outputs.loginServer
    env: concat(sharedApiEnv, apiOnlyEnv)
    secrets: [
      {
        name: 'guest-signing-key'
        // Versionless: new versions are picked up within 30 minutes (ADR 0031).
        keyVaultUrl: '${keyvault.outputs.vaultUri}secrets/${keyvault.outputs.guestKeySecretName}'
        identity: identities.outputs.apiId
      }
    ]
  }
}

module web 'modules/app-web.bicep' = {
  scope: rg
  name: 'app-web'
  params: {
    location: location
    tags: tags
    environmentId: acaEnv.outputs.id
    image: webImage
    usePlaceholder: webImage == placeholderImage
    identityId: identities.outputs.webId
    registryServer: registry.outputs.loginServer
    apiUpstream: 'https://${api.outputs.fqdn}'
    cspEntraHost: cspEntraHost
    cspMapHosts: cspMapHosts
    customDomain: webCustomDomain
  }
}

module jobs 'modules/jobs.bicep' = {
  scope: rg
  name: 'jobs'
  params: {
    location: location
    tags: tags
    environmentId: acaEnv.outputs.id
    registryServer: registry.outputs.loginServer
    migrateImage: migrateImage
    cleanupImage: apiImage
    cleanupCron: empty(guestCleanupCron) ? '15 3 * * 1' : guestCleanupCron
    migratorIdentityId: identities.outputs.migratorId
    migratorClientId: identities.outputs.migratorClientId
    apiIdentityId: identities.outputs.apiId
    apiIdentityName: identities.outputs.apiName
    apiIdentityClientId: identities.outputs.apiClientId
    sqlServerFqdn: sql.outputs.serverFqdn
    databaseName: sql.outputs.databaseName
    cleanupEnv: concat(sharedApiEnv, [{ name: 'OTEL_SERVICE_NAME', value: 'job-guest-cleanup' }])
  }
}

module githubOidc 'modules/github-oidc.bicep' = {
  scope: rg
  name: 'github-oidc'
  params: {
    deployerName: identities.outputs.deployerName
    registryName: registry.outputs.name
    githubRepository: githubRepository
    githubOwnerId: githubOwnerId
    githubRepositoryId: githubRepositoryId
    githubEnvironment: githubEnvironment
    managedIdentityNames: [
      identities.outputs.apiName
      identities.outputs.migratorName
      'id-rd-web'
    ]
  }
}

module alertsBudget 'modules/alerts-budget.bicep' = {
  scope: rg
  name: 'alerts-budget'
  params: {
    tags: tags
    stage: stage
    contactEmail: budgetContactEmail
    budgetAmount: int(empty(budgetAmount) ? '25' : budgetAmount)
    budgetStartDate: budgetStartDate
    foundryName: foundry.outputs.name
    sqlServerName: sql.outputs.serverName
    databaseName: sql.outputs.databaseName
    appInsightsName: monitoring.outputs.appInsightsName
    mapsAccountName: maps.outputs.name
  }
}

// ---- Outputs: azd stores these in .azure/<env>/.env; none is a secret ----
output AZURE_LOCATION string = location
output AZURE_TENANT_ID string = tenant().tenantId
output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = registry.outputs.loginServer
output AZURE_CONTAINER_REGISTRY_NAME string = registry.outputs.name
output AZURE_CONTAINER_ENVIRONMENT_NAME string = acaEnv.outputs.name
output WEB_FQDN string = web.outputs.fqdn
output API_INTERNAL_FQDN string = api.outputs.fqdn
output KEY_VAULT_NAME string = keyvault.outputs.name
output SQL_SERVER_NAME string = sql.outputs.serverName
output FOUNDRY_ENDPOINT string = foundry.outputs.openAiEndpoint
output FOUNDRY_DEPLOYMENT string = foundry.outputs.deploymentName
output MAPS_ACCOUNT_NAME string = maps.outputs.name
output MIGRATE_JOB_NAME string = jobs.outputs.migrateJobName
output CLEANUP_JOB_NAME string = jobs.outputs.cleanupJobName
output DEPLOYER_CLIENT_ID string = identities.outputs.deployerClientId
