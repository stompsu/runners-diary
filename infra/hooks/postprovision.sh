#!/bin/sh
# azd postprovision hook: clears the temporary bootstrap key and prints what the user does next. Prints no secret.
set -eu

if [ -n "${GUEST_KEY_BOOTSTRAP:-}" ]; then
  # The key now lives in Key Vault only. Keeping it in .azure/<env>/.env would put a second copy on disk.
  azd env set GUEST_KEY_BOOTSTRAP "" >/dev/null
fi

web="${WEB_FQDN:-<web-fqdn>}"
cat <<MSG

Provisioned. Next steps (see docs/plans/M7-azure-deployment.md section 10):

Web URL                       https://${web}

Entra External ID (when the tenant exists, plan gate G8) - add to the SPA registration, platform "Single-page application":
  https://${web}/redirect.html
  https://${web}/signin

GitHub (plan gate G3) - environment "prod", required reviewer = you, deployment branch main:
  secrets    AZURE_CLIENT_ID       ${DEPLOYER_CLIENT_ID:-<deployer client id>}
             AZURE_TENANT_ID       ${AZURE_TENANT_ID:-<tenant id>}
             AZURE_SUBSCRIPTION_ID ${AZURE_SUBSCRIPTION_ID:-<subscription id>}
  variables  AZURE_RESOURCE_GROUP  ${AZURE_RESOURCE_GROUP:-<resource group>}
             ACR_NAME              ${AZURE_CONTAINER_REGISTRY_NAME:-<registry name>}
             WEB_FQDN              ${web}
  repository variable DEPLOY_ENABLED=true (deploy.yml does nothing until it is 'true')

Then run the "deploy" workflow (plan gate G4). Until then the apps run a placeholder image.
MSG
