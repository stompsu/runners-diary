// azd "fetch image" pattern: re-provisioning must not reset a deployed app to the placeholder image.
param name string
param exists bool

resource existingApp 'Microsoft.App/containerApps@2025-01-01' existing = if (exists) {
  name: name
}

output image string = exists ? existingApp!.properties.template.containers[0].image : ''
