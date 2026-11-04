// The API container app: internal ingress only (ADR 0004), scale 0-2, probes on /alive only (never SQL).
param location string
param tags object
param environmentId string
param image string
@description('True while the image is the public quickstart placeholder (port 80, no health endpoints): no probes, target port 80.')
param usePlaceholder bool = false
param identityId string
param registryServer string
@description('Environment variables (plain values and secretRef entries).')
param env array
@description('ACA secrets; Key Vault references only (ADR 0031).')
param secrets array

resource api 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'api'
  location: location
  tags: union(tags, { 'azd-service-name': 'api' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    environmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: usePlaceholder ? 80 : 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [{ latestRevision: true, weight: 100 }]
      }
      registries: [{ server: registryServer, identity: identityId }]
      secrets: secrets
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: env
          probes: usePlaceholder ? [] : [
            {
              type: 'Startup'
              httpGet: { path: '/alive', port: 8080 }
              periodSeconds: 2
              failureThreshold: 30
            }
            {
              type: 'Liveness'
              httpGet: { path: '/alive', port: 8080 }
              periodSeconds: 30
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: { path: '/alive', port: 8080 }
              periodSeconds: 10
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 2
        rules: [
          { name: 'http', http: { metadata: { concurrentRequests: '20' } } }
        ]
      }
    }
  }
}

output name string = api.name
output fqdn string = api.properties.configuration.ingress.fqdn
