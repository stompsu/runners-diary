#!/bin/sh
# azd preprovision hook. Runs on the user's machine before `azd provision`; read-only towards Azure.
#
# 1. Checks the settings that have no safe default.
# 2. Makes sure the guest-token signing key exists in Key Vault (ADR 0031 §2): when the secret is absent, a fresh key is
#    generated and handed to the template as GUEST_KEY_BOOTSTRAP so the first deployment can reference it. When the secret
#    exists (every later provision) nothing is generated, so tokens are never invalidated by a re-provision.
#    The key is never printed. postprovision.sh clears the temporary azd value again.
# 3. Sets BUDGET_START_DATE (first day of the current month) once, so the budget start never moves.
set -eu

stage="${RD_STAGE:-prod}"
rg="rg-runnersdiary-${stage}"

if [ -z "${BUDGET_CONTACT_EMAIL:-}" ]; then
  echo "BUDGET_CONTACT_EMAIL is not set. Run: azd env set BUDGET_CONTACT_EMAIL <your e-mail>" >&2
  echo "(budget and alert notifications go there; it is stored in .azure/, never committed)" >&2
  exit 1
fi

if ! command -v az >/dev/null 2>&1; then
  echo "az CLI not found; it is needed to check whether the guest signing key already exists." >&2
  exit 1
fi

# The budget start date is fixed on the first provision (a budget cannot start in a past month later on).
if [ -z "${BUDGET_START_DATE:-}" ]; then
  azd env set BUDGET_START_DATE "$(date -u +%Y-%m-01)" >/dev/null
  echo "preprovision: budget start date set to $(date -u +%Y-%m-01)."
fi

if [ -n "${GUEST_KEY_BOOTSTRAP:-}" ]; then
  echo "preprovision: a bootstrap guest key is already pending from an earlier run."
  exit 0
fi

if [ -z "${AZURE_SUBSCRIPTION_ID:-}" ]; then
  echo "AZURE_SUBSCRIPTION_ID is not set (run: azd env set AZURE_SUBSCRIPTION_ID <id>, or azd auth login / azd init)." >&2
  exit 1
fi
sub="$AZURE_SUBSCRIPTION_ID"

need_new_key=0
if ! exists="$(az group exists --subscription "$sub" --name "$rg" 2>&1)"; then
  echo "preprovision: cannot check whether $rg exists:" >&2
  echo "$exists" >&2
  exit 1
fi
case "$exists" in
  false) need_new_key=1 ;; # first provision: nothing can hold a key yet
  true)
    if ! vault="$(az keyvault list --subscription "$sub" -g "$rg" --query "[?tags.app=='runnersdiary'].name | [0]" -o tsv 2>&1)"; then
      # Generating a new key on an error could rotate the key by mistake.
      echo "preprovision: cannot list the key vaults in $rg:" >&2
      echo "$vault" >&2
      exit 1
    fi
    if [ -z "$vault" ]; then
      need_new_key=1
    elif err="$(az keyvault secret show --vault-name "$vault" --name guest-token-signing-key --query id -o tsv 2>&1 >/dev/null)"; then
      echo "preprovision: guest signing key exists in $vault; keeping it."
      exit 0
    else
      case "$err" in
        *SecretNotFound* | *"was not found"*) need_new_key=1 ;;
        *)
          # Forbidden, network, expired login...
          echo "preprovision: cannot tell whether the guest key exists in $vault:" >&2
          echo "$err" >&2
          exit 1
          ;;
      esac
    fi
    ;;
  *)
    echo "preprovision: unexpected answer from 'az group exists': $exists" >&2
    exit 1
    ;;
esac

# A soft-deleted vault of ours would hold the old key (and block re-creating the same name): do not silently replace it.
if ! deleted="$(az keyvault list-deleted --subscription "$sub" --resource-type vault --query "[?tags.app=='runnersdiary'].name | [0]" -o tsv 2>&1)"; then
  echo "preprovision: cannot list soft-deleted key vaults:" >&2
  echo "$deleted" >&2
  exit 1
fi
if [ -n "$deleted" ]; then
  echo "preprovision: soft-deleted key vault '$deleted' (tag app=runnersdiary) exists. Recover it (az keyvault recover --name $deleted)" >&2
  echo "to keep its guest key, or purge it (az keyvault purge --name $deleted) if it is really gone, then run azd provision again." >&2
  exit 1
fi

if [ "$need_new_key" = 1 ]; then
  if ! command -v openssl >/dev/null 2>&1; then
    echo "openssl not found; it is needed to generate the guest signing key." >&2
    exit 1
  fi
  azd env set GUEST_KEY_BOOTSTRAP "$(openssl rand -hex 32)" >/dev/null
  echo "preprovision: no guest signing key yet; a new one will be stored in Key Vault by this provision (value not shown)."
fi
