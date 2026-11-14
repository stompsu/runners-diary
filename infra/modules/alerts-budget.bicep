// Cost guardrails (ADR 0034): RG budget with e-mail notifications and metric alerts. Nothing here stops spend.
param tags object
param stage string
@minLength(3)
param contactEmail string
param budgetAmount int
@description('First day of a month (YYYY-MM-01). Fixed on purpose: a moving start date would change the budget on every provision.')
param budgetStartDate string
param foundryName string
param sqlServerName string
param databaseName string
param appInsightsName string
param mapsAccountName string
param raceImportJobName string

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-rd-${stage}'
  location: 'global'
  tags: tags
  properties: {
    groupShortName: 'rd-${stage}'
    enabled: true
    emailReceivers: [
      { name: 'owner', emailAddress: contactEmail, useCommonAlertSchema: true }
    ]
  }
}

resource budget 'Microsoft.Consumption/budgets@2026-06-01' = {
  name: 'budget-rd-${stage}'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: { startDate: budgetStartDate }
    notifications: {
      actual40: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 40
        thresholdType: 'Actual'
        contactEmails: [contactEmail]
      }
      actual80: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [contactEmail]
      }
      actual100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: [contactEmail]
      }
      forecast100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [contactEmail]
      }
    }
  }
}

resource foundry 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: foundryName
}

resource sqlServer 'Microsoft.Sql/servers@2025-01-01' existing = {
  name: sqlServerName

  resource database 'databases' existing = {
    name: databaseName
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

// > 400k tokens a day is between the typical use and the ceiling (ADR 0034 §4). TokenTransaction = prompt + generated tokens.
resource aiTokens 'Microsoft.Insights/metricAlerts@2026-01-01' = {
  name: 'alert-rd-ai-tokens'
  location: 'global'
  tags: tags
  properties: {
    description: 'Foundry token usage over 24 hours is above 400k.'
    severity: 2
    enabled: true
    scopes: [foundry.id]
    evaluationFrequency: 'PT1H'
    windowSize: 'P1D'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'tokens'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'TokenTransaction'
          metricNamespace: 'Microsoft.CognitiveServices/accounts'
          operator: 'GreaterThan'
          threshold: 400000
          timeAggregation: 'Total'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroup.id }]
  }
}

// Free allowance left below 10,000 vCore-seconds (10 %): the database pauses until next month when it runs out.
resource sqlFree 'Microsoft.Insights/metricAlerts@2026-01-01' = {
  name: 'alert-rd-sql-free'
  location: 'global'
  tags: tags
  properties: {
    description: 'Azure SQL free offer: less than 10,000 vCore-seconds left this month.'
    severity: 2
    enabled: true
    scopes: [sqlServer::database.id]
    evaluationFrequency: 'PT1H'
    windowSize: 'PT6H'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'freeRemaining'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'free_amount_remaining'
          metricNamespace: 'Microsoft.Sql/servers/databases'
          operator: 'LessThan'
          threshold: 10000
          timeAggregation: 'Minimum'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroup.id }]
  }
}

resource apiFailures 'Microsoft.Insights/metricAlerts@2026-01-01' = {
  name: 'alert-rd-api-5xx'
  location: 'global'
  tags: tags
  properties: {
    description: 'More than 20 failed requests in 15 minutes.'
    severity: 2
    enabled: true
    scopes: [appInsights.id]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'failedRequests'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'requests/failed'
          metricNamespace: 'microsoft.insights/components'
          operator: 'GreaterThan'
          threshold: 20
          timeAggregation: 'Count'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroup.id }]
  }
}

resource mapsAccount 'Microsoft.Maps/accounts@2023-06-01' existing = {
  name: mapsAccountName
}

// Unconditional: geocoding (and, with display, tiles) always call the account. Threshold sized for geocoding.
resource mapsUsage 'Microsoft.Insights/metricAlerts@2026-01-01' = {
  name: 'alert-rd-maps-usage'
  location: 'global'
  tags: tags
  properties: {
    description: 'Azure Maps usage above 200 transactions in 24 hours (geocoding is capped by a durable daily budget; the free tier is 5,000 transactions a month).'
    severity: 2
    enabled: true
    scopes: [mapsAccount.id]
    evaluationFrequency: 'PT1H'
    windowSize: 'P1D'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'usage'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'Usage'
          metricNamespace: 'Microsoft.Maps/accounts'
          operator: 'GreaterThan'
          threshold: 200
          timeAggregation: 'Count'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroup.id }]
  }
}

resource raceImportJob 'Microsoft.App/jobs@2025-01-01' existing = {
  name: raceImportJobName
}

// Weekly job: evaluated hourly (PT1H, the maximum frequency) over a 24 h window; a failed execution alerts within about an hour (ADR 0038 section 5).
// Metric `Executions` (display name "Job Executions"), dimension `state`:
// https://learn.microsoft.com/azure/azure-monitor/reference/supported-metrics/microsoft-app-jobs-metrics
// The page does not list the dimension values; `Failed` matches the execution status the platform reports
// (`az containerapp job execution show` -> properties.status), checked against the first real failure (plan G-R3).
resource raceImportFailed 'Microsoft.Insights/metricAlerts@2026-01-01' = {
  name: 'alert-rd-race-import-failed'
  location: 'global'
  tags: tags
  properties: {
    description: 'job-race-import had a failed execution in the last 24 hours.'
    severity: 2
    enabled: true
    scopes: [raceImportJob.id]
    evaluationFrequency: 'PT1H'
    windowSize: 'P1D'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'failedExecutions'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'Executions'
          metricNamespace: 'Microsoft.App/jobs'
          dimensions: [{ name: 'state', operator: 'Include', values: ['Failed'] }]
          operator: 'GreaterThan'
          threshold: 0
          timeAggregation: 'Total'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroup.id }]
  }
}
