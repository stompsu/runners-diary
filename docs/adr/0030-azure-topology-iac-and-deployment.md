# 0030 – Azure topology, IaC (Bicep + azd) and the deploy pipeline

**Status:** Accepted (M7 plan approved 2026-11-04; amended 2026-11-04 after Part A implementation, 2026-11-04 for the Foundry deployment type, 2026-11-04 for the region at G1/G2, and 2026-11-05 for the first deploy at G4, see the end)
**Date:** 2026-11-04

## Context

Brief §2 and §4 (M7) ask for:
- Bicep + `azd` for all resources;
- Container Apps, ACR, managed identity everywhere, Key Vault and App Insights;
- a GitHub Actions deploy with OIDC.

The constraints:
- cost-conscious and scale-to-zero (brief §1.2);
- **no cloud action without the user's explicit confirmation** (CLAUDE.md);
- the API is never public (0004);
- migrations never run at app startup in Azure (0013 §7).

Today the AppHost is local-only: SQL container, Azurite, Vite. There are no Dockerfiles, no `azure.yaml` and no `infra/`. `azd` is not installed on the developer machine; `az` 2.91.0 is.

Verified on 2026-11-04:
- **Aspire 13.6 can deploy to Container Apps** (`AddAzureContainerAppEnvironment`, `aspire deploy`/azd). It generates Bicep for the environment, ACR, identity and apps, and generated Bicep "is overwritten", so customization goes through C# provisioning APIs ([aspire.dev ACA](https://aspire.dev/deployment/azure/container-apps/)).
- The pieces this project needs are hand-tuned settings that Aspire's ACA output does not cover:
  - SQL free offer flags and Entra-only admin;
  - Maps CORS;
  - a Foundry Data Zone deployment (→ Global Standard since 2026-11-04, see the amendment "Foundry deployment type" at the end);
  - budgets, alerts and Log Analytics caps;
  - jobs, probes and nginx;
  - a GitHub federated credential.

  Covering them would mean adding preview Azure hosting packages to the AppHost (ADR 0027 §4 rejected `Aspire.Hosting.Foundry` because it provisions in run mode).
- azd services with `host: containerapp` and a `docker` block are supported; the azure.yaml schema says the host covers apps and jobs ([azd schema](https://learn.microsoft.com/azure/developer/azure-developer-cli/azd-schema), [remote builds](https://learn.microsoft.com/azure/developer/azure-developer-cli/remote-builds)).
- `azure/login` supports OIDC with a **user-assigned managed identity** that has a federated credential; the workflow needs `id-token: write` ([Learn](https://learn.microsoft.com/azure/developer/github/connect-from-azure-openid-connect), updated 2026-11-04).

## Decision

1. **Hand-written Bicep under `infra/`, provisioned only by `azd provision` run by the user.**
   - The AppHost stays local-only and gets no Azure hosting packages.
   - `infra/main.bicep` is subscription-scoped and creates one resource group plus modules.
   - Bicep params come from `azd` env values (`.azure/<env>/.env`, git-ignored, non-secret).
   - **CI never provisions.** Role assignments need Owner-level rights that the pipeline must not hold.
   - Every `azd provision` (and its `--preview` what-if) is a user-confirmed action.
2. **One environment, `prod`** (azd env `rd-prod`, RG `rg-runnersdiary-prod`). Local Aspire is the dev environment.
   - A second cloud environment would double the confirmation gates and the free-offer usage for no current benefit.
   - `azd env new` can add one later without changing the Bicep.
3. **Region `swedencentral`** for everything except the Maps account:
   - it offers gpt-5.4-mini Data Zone Standard EU (ADR 0027 §Verification), Container Apps, the SQL free offer, Key Vault and Storage;
   - processing stays in the EU.

   (→ 2026-11-04: the model deployment is Global Standard, so AI prompts may be processed outside the EU; the region choice stands, see the amendment "Foundry deployment type" at the end.)

   The **Maps account goes to `westeurope`**. A Maps account in location `global` cannot use managed identities ([Maps auth](https://learn.microsoft.com/azure/azure-maps/azure-maps-authentication)), and West/North Europe sit behind `eu.atlas.microsoft.com`.

   `polandcentral` (closest to Gliwice, also in the Data Zone table) is an alternative (plan Q2); latency is not a deciding factor here. (→ it is also in the Global Standard table; the Data Zone quota there is 0 as well, see the amendment at the end.)
4. **Topology:**
   - **Container Apps environment:** Consumption plan, no VNet, no private endpoints, no planned-maintenance configuration (each would trigger the per-environment management meter).
   - **Apps and jobs:**
     - `web`: external ingress, nginx (ADR 0029);
     - `api`: **internal ingress**;
     - `job-migrate`: manual trigger;
     - `job-guest-cleanup`: scheduled.
   - **ACR Basic.** Images are tagged `sha-<git sha>` (immutable) and never `latest`.
   - **Shared services:** Log Analytics + workspace-based App Insights, Key Vault (RBAC), Storage (blob), Azure SQL (free offer), Foundry (AIServices) and Azure Maps (Gen2).
   - **Scale:** `api` and `web` min 0 / max 2.
     - `api`: 0.5 vCPU / 1 GiB, enough for GPX buffers (ADR 0020).
     - `web`: 0.25 vCPU / 0.5 GiB.
     - Max 2 keeps the per-replica limiters meaningful (ADR 0011).
   - **Probes** use `/alive` only (liveness, readiness and startup), never a probe that queries SQL. Otherwise serverless SQL would never auto-pause while a replica lives (ADR 0006, 0007).
5. **Images:**
   - **API:** `src/RunnersDiary.Api/Dockerfile` (build context: repo root). Runtime `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`: non-root, no shell, ICU + **tzdata** for IANA zones (M2 §5 risk).
   - **Web:** see ADR 0029.
   - **Migration:** see [ADR 0032](0032-migrations-and-jobs-in-azure.md).
   - **Base images** are pinned by digest; Dependabot's `docker` ecosystem bumps them.
6. **`azure.yaml`** declares the services `api` and `web` (`host: containerapp`, `docker.path`), so `azd provision` / `azd deploy` / `azd down` work from a laptop. Hooks:
   - `postprovision`: create the guest signing key only if absent (ADR 0031), then print the web FQDN, the Entra redirect URIs to register and the GitHub values;
   - `predown`: a reminder about data loss.
7. **The CI deploy (`.github/workflows/deploy.yml`) uses `docker` + `az` rather than `azd deploy`.** It needs a fixed order that azd does not express: push → migrate job → apps → smoke → automatic rollback.
   - **Triggers:** `workflow_dispatch` (input `ref`, optional `skip_migrations` for rollbacks), and `workflow_run` after a green CI on `main`.
   - **Approval:** each run waits on the **GitHub environment `prod` with the user as required reviewer**. That approval is the per-deploy confirmation.
   - **Steps:**
     1. `azure/login` (OIDC, deployer UAMI).
     2. `az acr login`; build and push the three images.
     3. Update `job-migrate` to the new tag and start it; poll until `Succeeded` (10 min timeout).
     4. Record the current `api`/`web` images.
     5. `az containerapp update --image` for `api`, `web` and `job-guest-cleanup`.
     6. Smoke tests (plan §6.3).
     7. If the smoke tests fail: re-apply the recorded images and fail the run.
   - **Actions** are pinned by full commit SHA (revisits ADR 0005), with `permissions: { contents: read, id-token: write }`.
   - **Concurrency:** group `deploy-prod`, never cancel in progress.
8. **Rollback:**
   - Run the workflow again with an earlier `ref` and `skip_migrations: true`.
   - Migrations are forward-only and must stay compatible with the previous release (expand → deploy → contract in a later release; ADR 0032).
   - Bicep drift is fixed by re-provisioning from git (a gate).
   - Bicep re-provisioning keeps the running image: the app modules read the current image of an existing app (the azd "fetch image" pattern) instead of a placeholder.

## Consequences

- Full control over cost-relevant settings, in reviewable files validated offline (`az bicep build` + lint in CI). The cost is more Bicep to maintain than Aspire would generate.
- Two ways to deploy code exist: CI (normal) and `azd deploy` (manual fallback, which skips the migration job). The runbook says which to use.
- `azd` is required only on the provisioning machine; the CI runner needs only `az` and `docker`.
- Changes to infrastructure always go through the user (what-if + provision); code changes go through the GitHub approval.
- A cloud dev/staging environment is not available. Risky changes are tested locally and in CI, and rolled back by image.

## Alternatives considered

- **Aspire publish/deploy (`aspire deploy` or `azd` reading the AppHost):** less Bicep, but the generated files are overwritten. It would also mean adding preview hosting integrations and run-mode provisioning risk to the AppHost. Rejected.
- **`azd deploy` in CI:** simpler YAML, but no migrate-before-app ordering, no automatic rollback, and azd's own env reconstruction in CI. Rejected; kept for manual use.
- **CI also provisions (`azd provision` in Actions):** needs Owner / User Access Administrator on the pipeline identity, and turns every merge into a potential infra change. Rejected.
- **`prod` + `dev` in the cloud:** about +$5/month (a second ACR, unless shared) plus doubled gates and free-offer usage. Deferred.
- **Terraform:** the brief names Bicep and azd. Rejected.

## Amendment (2026-11-04, M7 Part A implemented)

Nothing exists in Azure. Everything below is verified offline (CI) only.

1. **Images (§5).**
   - **API:** `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`, non-root (the image's user is the name `app`, which Container Apps accepts), ≈ 101 MB compressed.
   - **Web:** nginx unprivileged, ≈ 27 MB ([ADR 0029 amendment](0029-web-tier-nginx-container.md)).
   - **Migrate:** ≈ 116 MB ([ADR 0032 amendment](0032-migrations-and-jobs-in-azure.md)).
   - Every `FROM` is digest-pinned; Dependabot's `docker` ecosystem bumps them.
   - **ACR Basic has no retention policy.** At ≈ 0.25–0.5 GB per deploy (three images, shared layers deduplicated) the 10 GB quota holds roughly 20 deploys. Untagged/old `sha-*` tags must be pruned by hand occasionally (`az acr repository show-tags` / `az acr manifest delete`); no automation, so as not to delete a rollback target.
2. **Bicep (§1).** `infra/main.bicep` + 15 modules, 50 resource declarations. `infra/bicepconfig.json` sets **every** linter rule to `error`, including `use-recent-api-versions` (730 days). API versions chosen to satisfy it: Key Vault `2026-02-01`, SQL `2025-01-01`, Maps `2023-06-01` (newest GA), Budgets `2026-06-01`, `metricAlerts` `2026-01-01`. CI runs `az bicep build` + `az bicep lint`.
3. **First provision uses a placeholder image.** Before the first deploy there is no image in ACR. The apps start on `mcr.microsoft.com/k8se/quickstart` with **target port 80 and no probes** (it has no `/alive`). Once an app runs a real image, the fetch-image module keeps it, and port 8080 + probes apply (review M1). Web probes use `/nginx-alive`; API probes `/alive`.
4. **Hooks (changes §6).**
   - **New `preprovision.sh`** (replaces "postprovision creates the key via `az`"):
     - requires `BUDGET_CONTACT_EMAIL` and `AZURE_SUBSCRIPTION_ID`;
     - sets `BUDGET_START_DATE` to the first day of the current month **once** (a budget cannot later start in a past month);
     - decides whether a guest key is needed: `az group exists --subscription …`; if the RG exists, it looks up the tagged vault and the secret `guest-token-signing-key`;
     - only when the secret is absent, it generates `openssl rand -hex 32` into the temporary azd value `GUEST_KEY_BOOTSTRAP`, which Bicep receives as the `@secure()` parameter `guestSigningKeyBootstrap` and writes as the secret; when the secret exists, Bicep does not touch it, so re-provisioning never rotates the key;
     - **fails closed** on any unexpected `az` error (forbidden, network, expired login), so an error can never cause a silent rotation; and **refuses** when a soft-deleted vault tagged `app=runnersdiary` exists (recover or purge first);
     - never prints the key (review M4).
   - **`postprovision.sh`** clears `GUEST_KEY_BOOTSTRAP` (so no second copy stays in `.azure/<env>/.env`) and prints the web URL, the two Entra redirect URIs and the GitHub environment values. It prints no secret.
   - `predown.sh`: data-loss reminder (unchanged).
5. **Deploy workflow (§7, §8).**
   - Triggers as decided; every job is guarded by `vars.DEPLOY_ENABLED == 'true'`; `workflow_run` only for a **successful CI run of a `push`** to `main` (never a fork PR).
   - **A dispatched `ref` must be an ancestor of `origin/main`** (`git merge-base --is-ancestor`); earlier `main` SHAs stay valid, so rollbacks work, but an unmerged branch cannot be deployed (review M2).
   - Images are tagged `sha-<sha>`; an existing tag is reused as is (rollbacks do not rebuild).
   - Order: migrate job (unless `skip_migrations`) → record current images → update `api`, `web`, `job-guest-cleanup` → `infra/scripts/smoke.sh`.
   - **Rollback** runs `if: always()` once the update step has set its "started" marker **and the smoke step did not succeed**: smoke failure, a half-failed update, or a cancellation all restore the recorded images (review M3).
   - `smoke.sh` calls `/api/health` with the smoke guest's token, so it exercises the full (SQL) report ([ADR 0034 amendment](0034-cost-guardrails-and-observability.md) §3).
   - All actions are pinned by full SHA; `actionlint` (digest-pinned container image) and `shellcheck` run in CI.
6. **CI (`ci.yml`).** New job **`deploy-artifacts`**: Bicep build + lint, actionlint, shellcheck, the three image builds, the web-image header verify script, and the whole Playwright suite against the web image. `e2e-fullstack` (main only) now runs the built `api` and `web` images instead of `dotnet run` + `vite preview`.

**Still unverified (⚠, Part B):**
- azd parameter defaults `${VAR=default}` and `SERVICE_API_RESOURCE_EXISTS` for the fetch-image pattern (G1 what-if);
- role-assignment propagation on the first provision (apps may fail to pull or read the vault for a few minutes);
- whether built-in **Container Apps Contributor** is enough for `az containerapp update`/`job update`/`job start` (the reviewer believes `Microsoft.App/containerApps/*` covers write) and the **Container Apps Jobs Contributor** GUID `4e3d2b60-…` (G2/G4);
- the budget `startDate` semantics with a first-of-month date set on a later day of the month (G1/G2).

## Amendment (2026-11-04, Foundry deployment type)

User decision 2026-11-04 (M7 plan §12 deviation **D-AI**; normative detail in the [ADR 0027 amendment "deployment type"](0027-ai-model-provider-foundry-and-local-dev.md#amended-2026-11-04-deployment-type)).

- **Foundry deployment:** gpt-5.4-mini **Global Standard** (`infra/modules/foundry.bicep`: `sku: { name: 'GlobalStandard', capacity: capacityKtpm }`, 30k TPM), not Data Zone Standard. The read-only quota check found `OpenAI.DataZoneStandard.gpt-5.4-mini` = 0 in westeurope, swedencentral, polandcentral, germanywestcentral and francecentral, and `OpenAI.GlobalStandard.gpt-5.4-mini` = 1 000k TPM with 700k used (300k free).
- **Region (§3) unchanged: `swedencentral`.** It still offers gpt-5.4-mini as Global Standard ([region availability](https://learn.microsoft.com/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability), Global Standard → Europe, page updated 2026-10-19), plus Container Apps, the SQL free offer, Key Vault and Storage. `polandcentral` is no longer an advantage for AI quota (Data Zone is 0 there too).
- **§3 "processing stays in the EU" is narrowed:** all resources and data at rest stay in the EU (Foundry included), but **AI prompts and responses may be processed in any Azure region** ([deployment types](https://learn.microsoft.com/azure/foundry/foundry-models/concepts/deployment-types)). Only the metrics-only fact sheet is sent ([ADR 0026 amendment](0026-training-insights-agent-and-grounding.md)).
- Switching back to Data Zone if EU quota is granted: one Bicep value + G1 + G2.

## Amendment: Region (2026-11-04, M7 Part B gates G1 and G2)

Recorded after G1 (what-if) and G2 (provision), both run with the user's explicit approval. Run log: [M7 plan §12.7](../plans/M7-azure-deployment.md#127-part-b-outcomes).

1. **Primary region `westeurope`** (user decision, plan Q2), replacing `swedencentral` in §3. Everything is in `westeurope` except the SQL server and database: the resource group, Container Apps environment, ACR, Key Vault, Storage, Log Analytics / App Insights and Foundry. The Maps account was already in `westeurope`, so it is now co-located. gpt-5.4-mini Global Standard is offered in `westeurope`, and the Global Standard quota is a subscription-wide pool (300k of 1 000k TPM free), so the AI decision is unaffected.
2. **SQL in `polandcentral`**, through a new Bicep parameter `sqlLocation` (azd value `SQL_LOCATION`; empty = the primary region), passed to `infra/modules/sql.bicep`.
   - **Why:** new SQL servers are restricted for this subscription, not the template. G2 failed twice with `RegionDoesNotAllowProvisioning` (West Europe, North Europe). The SQL capabilities API (`GET /subscriptions/{sub}/providers/Microsoft.Sql/locations/{region}/capabilities`, [REST reference](https://learn.microsoft.com/rest/api/sql/capabilities/list-by-location)) returned "Provisioning is restricted in this region" for `westeurope`, `northeurope` and `norwayeast` (a support request would be needed), "Subscriptions are restricted" for `uksouth`, and "Available" for `swedencentral`, `germanywestcentral`, `francecentral`, `polandcentral`, `italynorth`, `spaincentral` and `switzerlandnorth`. The user chose `polandcentral` (closest to Gliwice).
   - **Consequences:** app ↔ DB traffic crosses regions (West Europe → Poland Central), adding latency per round trip and a small inter-region transfer charge (⚠ both to be observed at G5 and in the 48 h cost check). All data at rest stays in the EU. The free offer applied in `polandcentral` (database created with `useFreeLimit` + `AutoPause`, Online).
   - **Rule for the future:** check the capabilities API before any provision that creates a SQL server (plan §10 step 5.1).
3. **Name-suffix rule.** When `SQL_LOCATION` is set, the server is named `sql-rd-<token>-<sqlLocation>` (module param `nameSuffix`; prod: `sql-rd-m3dcwctu5kpwi-polandcentral`); otherwise `sql-rd-<token>` as before. Reason: a failed create in another region kept holding the plain name (`InvalidResourceLocation` with no server existing), and server names are global. `<token>` still comes from the primary location, so the other resource names are unchanged.
4. **Hooks are run by hand** (changes §6 and the Part A amendment §4 in practice; the hook scripts are unchanged).
   - azd validates required Bicep parameters **before** the `preprovision` hook runs, so `BUDGET_START_DATE` must be set with `azd env set` before the first provision.
   - `azd provision` (azd 1.37.2, also with `--no-prompt`) did **not** run the project `preprovision`/`postprovision` hooks in this setup. ⚠ Cause not yet known (azure-infra to investigate).
   - Required procedure until then: `azd hooks run preprovision` → `azd provision` → `azd hooks run postprovision`. Skipping the last step leaves `GUEST_KEY_BOOTSTRAP` in the local azd env file.
5. **what-if limits** (affects "every `azd provision --preview` is a user-confirmed action" in §1): azd's preview prints only main resource types; `az deployment sub what-if` gives the full detail. After resources exist, what-if can report `NestedDeploymentShortCircuited` and skip modules whose parameters depend on existing resources' outputs (an ARM limitation). Later G1 reviews must note which modules were skipped.
6. **azd env name `prod`** (not `rd-prod` as in §2); the resource group is `rg-runnersdiary-prod` as decided.

**Alternatives considered:** keep `swedencentral` for everything (SQL "Available" there): rejected by the user in favour of West Europe before G1. Request SQL access in West Europe through a support ticket: slower and uncertain; can still be done later, but moving the database would need an export/import or a new free-offer database, so it is not planned. `germanywestcentral` or `swedencentral` for SQL: equally available; `polandcentral` chosen by the user.

## References (checked 2026-11-04)

- Aspire: deploy to Azure Container Apps: https://aspire.dev/deployment/azure/container-apps/ · configure ACA environments: https://aspire.dev/integrations/cloud/azure/configure-container-apps/
- azd `azure.yaml` schema: https://learn.microsoft.com/azure/developer/azure-developer-cli/azd-schema · remote builds: https://learn.microsoft.com/azure/developer/azure-developer-cli/remote-builds
- GitHub Actions OIDC with a UAMI (`azure/login`, `id-token: write`; updated 2026-11-04): https://learn.microsoft.com/azure/developer/github/connect-from-azure-openid-connect
- Container Apps billing (Consumption free grant; management charge only for Dedicated profiles, private endpoints and planned maintenance): https://learn.microsoft.com/azure/container-apps/billing
- Container Apps jobs (manual/scheduled triggers, cron in UTC, `az containerapp job start`, start ⇒ access to the job's secrets and identities): https://learn.microsoft.com/azure/container-apps/jobs

## Amendment: first deploy (2026-11-05, G4)

The first deploy needed four runs; each failure was caught by the smoke test and undone by the automatic rollback, so production never served a broken release. Three changes to the deploy path came out of it:

1. **Ingress port (#16):** provisioning on the placeholder image sets the ingress `targetPort` to 80 with no probes, and `az containerapp update --image` keeps the port. `deploy.yml` now sets the target port to 8080 (80 when rolling back to the placeholder).
2. **Readiness before smoke (#18):** Container Apps keeps the traffic on the old revision until the new one is ready. `deploy.yml` sets the port **before** swapping the images and waits until `latestReadyRevisionName == latestRevisionName` for `api` and `web`; `smoke.sh` retries the first `GET /` until it sees our CSP (default 300 s), which also covers scale-from-zero.
3. **Container-scoped blob health check (#19):** Aspire's built-in Blob check reads account-level properties, which the API identity's container-scoped role (ADR 0031) does not allow (403 `AuthorizationPermissionMismatch`; invisible with Azurite's account key). It is replaced by `BlobContainerHealthCheck` (lists at most one blob in `gpx`).

After the first successful deploy, one `azd provision --no-state` turned the placeholder settings into port 8080 with our probes. `--no-state` is needed because `azd provision` compares only its template/parameter hash, not the live state. G5 then confirmed the per-visitor guest limit through the whole forwarding chain (ADR 0029) and the main screens under the production CSP.

## Amendment: custom domain (2026-11-05)

The `web` app can carry one custom domain (`CUSTOM_DOMAIN`, prod: `runners-diary.highpace.dev`) with a **free managed certificate**. It is bound with `bindingType: 'Auto'` (`Microsoft.App/containerApps@2026-07-01`), so the certificate created after the binding is attached automatically, and one deployment is enough. Validation is by CNAME. The domain lives in Bicep, not the portal, because a re-provision would otherwise drop it. The default `*.azurecontainerapps.io` address keeps working.

