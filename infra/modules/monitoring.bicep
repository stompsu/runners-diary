// Log Analytics workspace with a daily cap + workspace-based Application Insights (ADR 0034).
param location string
param tags object
param stage string
@description('Daily ingestion cap in GB. 0.15 keeps a full-cap month near 4.6 GB, under the 5 GB free allowance.')
param dailyQuotaGb string = '0.15'
param retentionInDays int = 30

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: 'log-rd-${stage}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: retentionInDays
    workspaceCapping: { dailyQuotaGb: json(dailyQuotaGb) }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-rd-${stage}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
  }
}

output workspaceName string = workspace.name
output appInsightsName string = appInsights.name
// Not a credential: it only allows telemetry ingestion (ADR 0031 §2).
output appInsightsConnectionString string = appInsights.properties.ConnectionString
