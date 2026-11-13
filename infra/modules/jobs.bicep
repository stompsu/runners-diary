// Container Apps jobs: the manual migration job, the weekly guest cleanup (ADR 0032) and the weekly race import (ADR 0038).
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
param raceImportImage string
@description('UTC cron. Keep it 10 minutes after the guest cleanup so both share one SQL wake (ADR 0038 section 3).')
param raceImportCron string
param raceImportIdentityId string
param raceImportIdentityName string
param raceImportIdentityClientId string
param sqlServerFqdn string
param databaseName string
@description('API settings for the cleanup job (no Entra, no guest key: job mode does not need them).')
param cleanupEnv array
@description('Settings for the race import job: its own SQL user and nothing else (no Entra, guest key, Blob, Maps or AI).')
param raceImportEnv array

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
            { name: 'RACE_IMPORT_IDENTITY_NAME', value: raceImportIdentityName }
            { name: 'RACE_IMPORT_IDENTITY_CLIENT_ID', value: raceImportIdentityClientId }
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

resource raceImport 'Microsoft.App/jobs@2025-01-01' = {
  name: 'job-race-import'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${raceImportIdentityId}': {} }
  }
  properties: {
    environmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Schedule'
      replicaTimeout: 1800
      replicaRetryLimit: 1
      scheduleTriggerConfig: { cronExpression: raceImportCron, parallelism: 1, replicaCompletionCount: 1 }
      registries: [{ server: registryServer, identity: raceImportIdentityId }]
    }
    template: {
      containers: [
        {
          name: 'race-import'
          image: raceImportImage
          args: ['--job', 'race-import']
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: raceImportEnv
        }
      ]
    }
  }
}

output migrateJobName string = migrate.name
output cleanupJobName string = cleanup.name
output raceImportJobName string = raceImport.name
