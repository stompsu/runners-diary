#!/bin/sh
# azd predown hook: a reminder only. `azd down` is a user-confirmed action (plan gate G13); azd asks for confirmation itself.
set -eu
cat <<'MSG'
azd down deletes the resource group: the database (all runs, tracks and guests), blobs, Key Vault and the deployer identity.
Use `azd down --purge` so Key Vault and the Foundry account are purged and their names/quota are free again.
After it: remove the production redirect URIs from the SPA registration, delete the GitHub environment "prod" and set
DEPLOY_ENABLED=false (plan section 9.4).
MSG
