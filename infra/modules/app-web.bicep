// The web container app: nginx (ADR 0029), the only public endpoint, scale 0-2.
param location string
param tags object
param environmentId string
param image string
@description('True while the image is the public quickstart placeholder (port 80, no health endpoints): no probes, target port 80.')
param usePlaceholder bool = false
param identityId string
param registryServer string
@description('Internal API address nginx proxies /api to.')
param apiUpstream string
@description('https://<tenant subdomain>.ciamlogin.com, or empty while guests-only.')
param cspEntraHost string
@description('Space-separated hosts that may serve map tiles, sprites and glyphs.')
param cspMapHosts string

resource web 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'web'
  location: location
  tags: union(tags, { 'azd-service-name': 'web' })
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
        external: true
        targetPort: usePlaceholder ? 80 : 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [{ latestRevision: true, weight: 100 }]
      }
      registries: [{ server: registryServer, identity: identityId }]
    }
    template: {
      containers: [
        {
          name: 'web'
          image: image
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'API_UPSTREAM', value: apiUpstream }
            { name: 'CSP_ENTRA_HOST', value: cspEntraHost }
            { name: 'CSP_MAP_HOSTS', value: cspMapHosts }
          ]
          probes: usePlaceholder ? [] : [
            {
              type: 'Liveness'
              httpGet: { path: '/nginx-alive', port: 8080 }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/nginx-alive', port: 8080 }
              periodSeconds: 10
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

output name string = web.name
output fqdn string = web.properties.configuration.ingress.fqdn
