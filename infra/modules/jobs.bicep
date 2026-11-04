// Container Apps jobs: the manual migration job and the weekly guest cleanup (ADR 0032).
param location string
param tags object
param environmentId string
param registryServer string
param migrateImage string
param cleanupImage string
param cleanupCron string

param migratorIdentityId string
param migratorClientId string
param apiIdentityId string
param apiIdentityName string
param apiIdentityClientId string
param sqlServerFqdn string
param databaseName string
@description('API settings for the cleanup job (no Entra, no guest key: job mode does not need them).')
param cleanupEnv array

resource migrate 'Microsoft.App/jobs@2025-01-01' = {
  name: 'job-migrate'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${migratorIdentityId}': {} }
  }
  properties: {
    environmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 900
      replicaRetryLimit: 1
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      registries: [{ server: registryServer, identity: migratorIdentityId }]
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: migrateImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'SQL_SERVER', value: sqlServerFqdn }
            { name: 'SQL_DATABASE', value: databaseName }
            { name: 'AZURE_CLIENT_ID', value: migratorClientId }
            { name: 'API_IDENTITY_NAME', value: apiIdentityName }
            { name: 'API_IDENTITY_CLIENT_ID', value: apiIdentityClientId }
          ]
        }
      ]
    }
  }
}

resource cleanup 'Microsoft.App/jobs@2025-01-01' = {
  name: 'job-guest-cleanup'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${apiIdentityId}': {} }
  }
  properties: {
    environmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Schedule'
      replicaTimeout: 600
      replicaRetryLimit: 1
      scheduleTriggerConfig: { cronExpression: cleanupCron, parallelism: 1, replicaCompletionCount: 1 }
      registries: [{ server: registryServer, identity: apiIdentityId }]
    }
    template: {
      containers: [
        {
          name: 'guest-cleanup'
          image: cleanupImage
          args: ['--job', 'guest-cleanup']
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: cleanupEnv
        }
      ]
    }
  }
}

output migrateJobName string = migrate.name
output cleanupJobName string = cleanup.name
