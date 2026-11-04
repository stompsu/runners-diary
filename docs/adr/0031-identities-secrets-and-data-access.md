# 0031 – Identities, role assignments, secrets and data-plane access in Azure

**Status:** Accepted (M7 plan approved 2026-11-04; amended 2026-11-04 after Part A implementation and after gate G2, see the end)
**Date:** 2026-11-04

## Context

The brief and CLAUDE.md require:
- managed identity everywhere (API → SQL, Blob, Foundry, Maps);
- Key Vault only for what cannot use MI;
- no secrets in the repo;
- Maps keys never reaching the browser.

Earlier ADRs fixed the app side:
- the guest HS256 key (0009 §5);
- keyless Foundry with `Ai:Foundry:ManagedIdentityClientId` (0027);
- the Blob container `gpx` provisioned by IaC, so the app needs no create rights (0011 §3);
- migrations applied by a separate step (0013 §7).

Verified on 2026-11-04:
- **Container Apps can reference Key Vault secrets** with a UAMI (`keyVaultUrl` + `identity: <UAMI resource id>`). The identity needs the **Key Vault Secrets User** role. A versionless URL picks up new versions within 30 minutes and restarts the revisions that use it ([manage secrets](https://learn.microsoft.com/azure/container-apps/manage-secrets)).
- **Start-job permission** implies use of the job's secrets and identities. The built-in Container Apps Contributor and Jobs roles include `listSecrets` ([jobs](https://learn.microsoft.com/azure/container-apps/jobs)).
- **SQL free offer:** Entra-only authentication is configured on the server (`administrators` with `azureADOnlyAuthentication`, `principalType: Application`). For a UAMI, the admin `sid` is its **client (application) id**, not its principal (object) id. Microsoft Learn ("Set Microsoft Entra admin": for applications/service principals the Application ID is shown) and the `CREATE USER` reference (for a service principal or managed identity the SID is the application/client id) agree. Corrected 2026-11-04 after checking Microsoft Learn; confirm with what-if and a first connect.
- **SQL users from managed identities:** `CREATE USER … WITH SID = …, TYPE = E` creates an Entra user without Microsoft Graph or Directory Readers rights ([Learn, managed identities for Azure SQL](https://learn.microsoft.com/azure/azure-sql/database/authentication-azure-ad-user-assigned-managed-identity), read in the ja-jp edition; ⚠ re-check the English page).
- **Azure Maps `disableLocalAuth: true`** disables **shared keys and SAS tokens**. SAS tokens are signed with the account keys and authorized through a UAMI attached to the account ([Maps authentication](https://learn.microsoft.com/azure/azure-maps/azure-maps-authentication), updated 2026-09-15). This answers the open item in ADR 0022: SAS display requires local auth to stay **enabled**.

## Decision

### 1. User-assigned identities (one per role, all in the RG)

| Identity | Attached to | Role assignments (scope) |
|---|---|---|
| `id-rd-api` | `api`, `job-guest-cleanup` | AcrPull (ACR) · **Storage Blob Data Contributor** (container `gpx` only) · **Key Vault Secrets User** (secret `guest-token-signing-key` only) · **Cognitive Services User** (Foundry account) · **Azure Maps Search and Render Data Reader** (Maps account; server-side geocoding only) · *only if Maps display is enabled:* custom role `RD Maps SAS Issuer` = `Microsoft.Maps/accounts/read` + `Microsoft.Maps/accounts/listSas/action` (Maps account; ⚠ Learn says SAS creation needs "Contributor" on the account and the UAMI, so the step-4 spike confirms the narrow role or falls back to built-in **Azure Maps Contributor**) · SQL DB user: `db_datareader`, `db_datawriter` |
| `id-rd-web` | `web` | AcrPull (ACR) |
| `id-rd-migrator` | `job-migrate` | AcrPull (ACR) · **SQL server Entra admin** (the only admin; Entra-only auth) |
| `id-rd-maps-sas` *(only if Maps display is enabled)* | the Maps account | custom role `RD Maps Render Reader` = `Microsoft.Maps/accounts/services/render/read` (Maps account) |
| `id-rd-deployer` | none (federated credential `repo:stompsu/runners-diary:environment:prod`, issuer `https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange`) | AcrPush (ACR) · Container Apps Contributor (RG) · Container Apps Jobs Contributor (RG) · Managed Identity Operator (the three app identities) · Reader (RG) |
| the user (`azd` principal) | — | Owner / User Access Administrator (subscription, pre-existing) · **Key Vault Secrets Officer** (vault; to create/rotate the guest key) · **Cognitive Services User** (Foundry; live eval from the laptop, optional param) |

- **SQL:** the API never gets DDL rights. The migrator is admin only so that the migration job can create schema and the API's DB user idempotently, with no human SQL step (ADR 0032).
- **Break-glass:** the user temporarily sets their own account as Entra admin and adds a client-IP firewall rule. Both are confirmation gates, and both are reverted afterwards.
- **Deployer:** it can push images and update or start jobs, and so it effectively controls code that runs as `id-rd-api` and `id-rd-migrator`. That is inherent to any deployer. It holds **no** role-assignment, Key Vault, SQL control-plane or Foundry rights.
- **The Maps SAS pieces are conditional** on the Bicep param `enableAzureMapsDisplay` (default `false`; [ADR 0033](0033-production-maps-and-azure-maps.md)), so the default deployment creates **no custom role definitions**.

### 2. Secrets inventory: Key Vault holds exactly one secret

| Value | Where | Why |
|---|---|---|
| `guest-token-signing-key` (HS256, ≥ 32 bytes) | **Key Vault** secret → ACA secret reference (versionless) → `Auth__Guest__SigningKey` | It cannot be replaced by MI: the API signs with it. It is created by the `postprovision` hook **only if absent** (`openssl rand -hex 32`, never printed), so re-provisioning does not rotate it. Rotation: set the old value in `Auth__Guest__PreviousSigningKeys__0`, then write a new version (0009 §1). |
| SQL, Blob, Foundry, Maps access | none (managed identity) | Keyless connection strings: `Server=tcp:<srv>.database.windows.net,1433;Database=runnersdiary;Authentication=Active Directory Managed Identity;User Id=<client id>;Encrypt=True;Connect Timeout=60`, Blob service URI + `AZURE_CLIENT_ID`. |
| App Insights connection string | plain env var `APPLICATIONINSIGHTS_CONNECTION_STRING` (from the Bicep resource) | Microsoft does not treat it as a credential: it only allows ingestion. Not worth a vault read per app. |
| Log Analytics shared key for the ACA environment's log stream | Bicep `listKeys()` at deployment time only | Never in a file or variable. |
| Entra ids, Foundry endpoint and deployment, Maps client id | plain env vars from azd env values | Non-secret (0015). |
| GitHub | environment `prod` secrets `AZURE_CLIENT_ID` (deployer), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` | Not credentials (OIDC), stored as secrets per Learn guidance. |

**No API key, SQL password, storage key or Maps key is configured anywhere.**

### 3. Resource hardening

- **SQL:**
  - `azureADOnlyAuthentication: true`; minimal TLS 1.2.
  - Firewall rule *Allow Azure services* (0.0.0.0). ACA Consumption without a VNet has no fixed egress IP; Entra-only auth is the control.
  - No other rules.
- **Storage:**
  - `allowSharedKeyAccess: false`, `allowBlobPublicAccess: false`, TLS 1.2.
  - Container `gpx` private.
  - Blob and container soft delete 7 days. No versioning (cost).
- **Key Vault:**
  - RBAC mode, soft delete 7 days, purge protection **off**, so `azd down --purge` can tear down cleanly. Accepted for a hobby project with one regenerable secret.
- **Foundry (AIServices):** `disableLocalAuth: true`, a custom subdomain (needed for Entra auth), public network access.
- **Maps:**
  - `disableLocalAuth: false` only because SAS needs it. Nobody is granted `listKeys` except subscription Owners.
  - If `enableAzureMapsDisplay` stays `false`, set `disableLocalAuth: true` (geocoding uses Entra).
  - CORS lists only the web origin.
- **ACR:** admin user disabled; pulls use MI only.
- **Container Apps:** the API ingress is internal; `allowInsecure: false` on `web`.

## Consequences

- A leaked repository or config file exposes nothing usable. The only secret lives in Key Vault, and only `id-rd-api` can read it.
- The default deployment needs no human SQL step and no Entra directory permissions (no Directory Readers).
- The SQL "Allow Azure services" rule is broader than a private endpoint. A private endpoint plus a VNet would add a per-hour environment charge, so this is accepted and revisited if the app holds sensitive data beyond training logs.
- Custom roles appear only with Maps display enabled. Role definitions are not deleted with the RG, so teardown lists `az role definition delete`.

## Alternatives considered

- **System-assigned identities:** cannot be used in the same deployment for Key Vault references and ACR pulls before the app exists, and they die with the app. Rejected.
- **The user as SQL admin + a manual `CREATE USER` script:** an extra human step on every rebuild. Rejected; this is the break-glass path only.
- **An Entra group as SQL admin:** needs a group in the workforce tenant (an extra gate) for little gain with one maintainer. Deferred.
- **Guest key generated by Bicep (`newGuid()` param default):** rotates on every provision and invalidates every guest token. Rejected.
- **Built-in Container Apps roles vs a custom deployer role without `listSecrets`:** the deployer can already run arbitrary code as the app identity, so a custom role adds residue without real protection. Built-ins chosen.

## Amendment (2026-11-04, M7 Part A implemented)

Deviations from §1–§3 as written in Bicep (`infra/modules/{identities,keyvault,maps,github-oidc,registry,storage,foundry}.bicep`). Nothing is provisioned yet.

1. **Key Vault Secrets User is assigned at vault scope**, not on the single secret. The vault holds exactly one secret, so the effective access is the same, and a vault-scope assignment avoids a secret-scope role assignment that would have to exist before the secret is first written. Key Vault Secrets Officer for the user is also vault-scoped (unchanged).
2. **Guest key creation moved to `preprovision.sh` (changes §2 "created by the `postprovision` hook").** The hook generates a key only when the secret is absent and passes it to Bicep as the secure parameter `guestSigningKeyBootstrap` (via the temporary azd value `GUEST_KEY_BOOTSTRAP`, cleared again by `postprovision.sh`). It never prints the key and fails closed on unexpected errors; details in [ADR 0030 amendment](0030-azure-topology-iac-and-deployment.md) §4. The user's Key Vault Secrets Officer role is still granted (rotation by hand).
3. **Maps SAS (G9 only), additional role:** `id-rd-api` gets **Managed Identity Operator** (`f1a07417-d97a-45cb-824c-7a7467783830`) on `id-rd-maps-sas`, because `listSas` names that identity as the token's principal (review M5). The two custom roles (`RD Maps SAS Issuer`, `RD Maps Render Reader`) are defined with **RG-scoped** `assignableScopes` and are created only with `enableAzureMapsDisplay`.
4. **Deployer roles as implemented:** AcrPush (ACR), Container Apps Contributor `358470bc-…` (RG), Container Apps Jobs Contributor `4e3d2b60-…` (RG), Reader (RG), Managed Identity Operator on `id-rd-api`, `id-rd-web`, `id-rd-migrator`. ⚠ Whether Container Apps Contributor alone suffices for app updates, and the Jobs Contributor GUID, are confirmed at G2/G4.
5. **SQL identities (unchanged, ⚠ until the first migrate run):** the UAMI admin `sid` = the migrator's client id, and `CREATE USER … WITH SID = <api client id>, TYPE = E` run by `infra/sql/grant-app-users.sql`. Both are documented (see the 2026-11-04 Learn check in plan §11) but first exercised at G4.
6. **Role propagation:** the first provision creates identities, role assignments and the apps in one deployment. A pull or Key Vault reference may fail for a few minutes until RBAC propagates; ACA retries. ⚠ Observed at G2.

## Amendment: G2 (2026-11-04, first provision)

Run log: [M7 plan §12.7](../plans/M7-azure-deployment.md#127-part-b-outcomes).

1. **Guest key bootstrap, as actually run.** azd 1.37.2 did not run the project hooks during `azd provision` in this setup ([ADR 0030](0030-azure-topology-iac-and-deployment.md) amendment "Region" §4), so the key was bootstrapped with **`azd hooks run preprovision`** (generated `GUEST_KEY_BOOTSTRAP` because the vault secret did not exist yet; never printed), written to Key Vault by Bicep through the secure parameter during `azd provision`, and the local copy was cleared with **`azd hooks run postprovision`**. The design of Part A amendment §2 is unchanged; only the invocation is manual. Every later provision must use the same sequence, or the bootstrap value can stay in the local azd env file (`postprovision` skipped) — on a re-provision the secret exists, so `preprovision` generates nothing and the key is not rotated.
2. **SQL firewall rule renamed** from `AllowAllWindowsAzureIps` to **`AllowAzureServices`** (§3). Same range (0.0.0.0–0.0.0.0 = "Allow Azure services"), same control model (Entra-only auth). azd warned that the old name contains the reserved word "Windows". The SQL server is now in `polandcentral` (ADR 0030 amendment "Region"); identities, roles and the Entra-only admin are unchanged.
3. Role propagation (Part A amendment §6): the provision itself succeeded; whether pulls and the Key Vault reference work without retries is observed at G4, when the real images run.

## References (checked 2026-11-04)

- ACA secrets and Key Vault references: https://learn.microsoft.com/azure/container-apps/manage-secrets
- ACA jobs, permissions warning: https://learn.microsoft.com/azure/container-apps/jobs
- Azure Maps authentication (`disableLocalAuth` disables SAS, SAS via UAMI, roles, CORS, non-billed 429): https://learn.microsoft.com/azure/azure-maps/azure-maps-authentication
- SQL managed identities and `CREATE USER … WITH SID … TYPE = E` (ja-jp edition read): https://learn.microsoft.com/azure/azure-sql/database/authentication-azure-ad-user-assigned-managed-identity · `Microsoft.Sql/servers` Bicep (`administrators`, `principalType`): https://learn.microsoft.com/azure/templates/microsoft.sql/servers
- GitHub OIDC with a UAMI: https://learn.microsoft.com/azure/developer/github/connect-from-azure-openid-connect

## Amendment: GitHub OIDC subject (2026-11-04, G4)

The deployer's federated credential matches GitHub's **immutable** OIDC subject, `repo:<owner>@<ownerId>/<repo>@<repoId>:environment:prod` (the repository has `use_immutable_subject: true`). The numeric ids are Bicep parameters `githubOwnerId`/`githubRepositoryId`, defaulting to this repository's public ids. Including them means a renamed or re-created repository with the same name cannot obtain tokens for this identity. If both are empty, the module falls back to the name-only subject. The first deploy failed with `AADSTS700213` until this was applied.

