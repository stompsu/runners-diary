// Azure SQL logical server (Entra-only, admin = the migrator identity) + serverless database on the free offer
// (ADR 0006, 0031, 0032).
param location string
param tags object
param token string
param databaseName string = 'runnersdiary'
param migratorName string
@description('Client (application) id of the migrator identity: the admin sid for a managed identity (ADR 0031).')
param migratorClientId string

resource server 'Microsoft.Sql/servers@2025-01-01' = {
  name: 'sql-rd-${token}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Application'
      login: migratorName
      sid: migratorClientId
      tenantId: tenant().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

// ACA Consumption has no fixed egress IP; Entra-only authentication is the control (ADR 0031 §3).
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2025-01-01' = {
  parent: server
  name: 'AllowAllWindowsAzureIps'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource database 'Microsoft.Sql/servers/databases@2025-01-01' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: { name: 'GP_S_Gen5', tier: 'GeneralPurpose', family: 'Gen5', capacity: 2 }
  properties: {
    minCapacity: json('0.5')
    useFreeLimit: true
    // Free allowance used up: pause until next month (an outage, never a bill). BillOverUsage is irreversible.
    freeLimitExhaustionBehavior: 'AutoPause'
    zoneRedundant: false
    requestedBackupStorageRedundancy: 'Local'
    maxSizeBytes: 34359738368
  }
}

output serverName string = server.name
output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
output databaseId string = database.id
