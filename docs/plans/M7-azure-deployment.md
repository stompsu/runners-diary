# M7 – Azure deployment: plan

**Status:** Approved 2026-11-04 (all §11 defaults except Q2, see §11; claims checked against Microsoft Learn). **Part A (offline) merged** (stompsu/runners-diary#12; deviations and review/QA findings: §12). **Part B: G1–G7 done (2026-11-04 to 06)** with the user's explicit approval per gate (log: §12.7). **Production is live** at `https://runners-diary.highpace.dev` (custom domain with a managed certificate; the default `*.azurecontainerapps.io` address also works). It runs as 25 resources in `rg-runnersdiary-prod` (primary region **`westeurope`**, SQL in **`polandcentral`**). Deploys run after green CI on `main`, behind the `prod` approval, with automatic rollback, and both apps have health probes. **AI summaries are on** (gpt-5.4-mini Global Standard, prompt v2, fact sheet v2). Production is guests-only until **G8** (Entra), which waits for the user's tenant. No further cloud resource is created, changed or deployed without the user's explicit confirmation of that specific gate.

**Builds on:**
- M1–M6 (all merged or awaiting merge);
- ADR 0004 (API never public), 0006 (SQL free offer), 0009/0014/0015 (Entra, SPA config), 0011 (guests), 0013 §7 (no startup migrations in Azure), 0020–0023 (GPX/Blob), 0022 (Azure Maps deferred), 0026–0028 (AI, ≥ 30k TPM, $3.18–6.37/day ceiling; $2.89–5.79/day at Global Standard prices since 2026-11-04, §12 D-AI).

**New ADRs (proposed):**
- [0029](../adr/0029-web-tier-nginx-container.md) web tier = nginx (supersedes 0004);
- [0030](../adr/0030-azure-topology-iac-and-deployment.md) topology, Bicep + azd, CI deploy, rollback;
- [0031](../adr/0031-identities-secrets-and-data-access.md) identities, roles, secrets;
- [0032](../adr/0032-migrations-and-jobs-in-azure.md) migration job, guest-cleanup job;
- [0033](../adr/0033-production-maps-and-azure-maps.md) production maps;
- [0034](../adr/0034-cost-guardrails-and-observability.md) cost guardrails and observability.

## 1. Goal & acceptance criteria

**Goal (brief §4 M7):**
- Bicep + azd for all resources, managed identities, Key Vault, App Insights, and a GitHub Actions deploy with OIDC.
- *"Show me the plan and an estimated monthly cost before deploying anything."*

M7 also closes the deferred items:
- Azure Maps adapters (ADR 0022);
- CSP and headers, including `redirect.html` without COOP (M3/M4);
- the X-Forwarded-For check, replica caps and `tzdata` (M2 §11);
- the live AI eval (M6 §12.6);
- the ACA timeout (≥ 65 s);
- the real Entra sign-in (M3 AC3/AC4, once the tenant exists).

**Out of scope:**
- custom domain;
- VNet and private endpoints;
- a second cloud environment;
- geo-redundancy;
- reverse geocoding;
- guest → user conversion;
- automated budget actions.

The milestone has two halves.
- **Part A (offline):** code, IaC and workflows, verified in CI. Merging Part A creates nothing in Azure.
- **Part B (cloud):** each action is an individual gate (§6.2).

| # | Criterion | Part | How verified |
|---|---|---|---|
| AC1 | `infra/main.bicep` builds and lints clean (`az bicep build`, `bicepconfig.json` rules as errors: `outputs-should-not-contain-secrets`, `secure-secrets-in-params`, `no-hardcoded-env-urls`, `use-secure-value-for-secure-inputs`). `azure.yaml` declares `api` and `web`. No secret, key, password or email address in any committed file. | A | CI `deploy-artifacts` job + reviewer |
| AC2 | Three images build in CI (`api`, `web`, `migrate`), with base images pinned by digest. The API image is non-root, chiseled, and has ICU + tzdata (a test resolves `Europe/Warsaw`). | A | CI |
| AC3 | The web image passes the **whole Playwright suite** (fake API via `page.route`), with **zero CSP violations** (a fixture fails on `securitypolicyviolation`). `curl` checks: the header table in ADR 0029 §4; `/redirect.html` has no COOP and `no-store`; deep links fall back to `index.html`; a missing `/assets/x.js` gives 404; `/api/*` is proxied; a 21 MB body passes nginx. | A | CI |
| AC4 | `e2e-fullstack` (main only) runs **the built API and web images** instead of `dotnet run` + `vite preview`: journey, tracks upload, insights, demo summary. | A | CI (main) |
| AC5 | Backend: `--job guest-cleanup` entry mode (exit 0/1); cleanup idle-skip; `UseAzureMonitor()` only with a connection string; startup validation for the Azure settings (§5.2); probes use `/alive` only. Tests included. | A | Api.Tests / Domain.Tests |
| AC6 | Maps: `AzureMapsGeocoder` (Entra MI token, EU domain, `top=5`); global daily geocode budget → 429; `GET /api/maps/token` + cached SAS issuer; `client-config.maps.azureMapsDomain`; `AzureMapsMap.tsx` (lazy, `authType: "sas"`) with session fallback; privacy note. OpenAPI/TS regenerated. Initial bundle +≤ 0.3 kB. All tests offline (fake HTTP handlers, stubbed SDK). | A | Api.Tests, Vitest, Playwright, `bundle-budget` |
| AC7 | `deploy.yml`: OIDC (`id-token: write`), environment `prod`, SHA-pinned actions, immutable `sha-<sha>` tags, migrate job → apps → smoke → automatic rollback on smoke failure. Guarded by `vars.DEPLOY_ENABLED == 'true'`, so merging it cannot deploy. `actionlint` clean. | A | CI + reviewer |
| AC8 | After G2+G4: the web is public over HTTPS; **the API is unreachable from the internet** (its FQDN refuses or 404s from outside); SQL Entra-only; storage shared key disabled; Foundry `disableLocalAuth`; Key Vault holds exactly one secret; no secret value in any container app env. | B | read-only `az` checklist (§7.3) |
| AC9 | **X-Forwarded-For verified:** 6 guest creations from one IP → the 6th is 429 (per-client policy), and a creation from a second network succeeds. | B | manual, G5 |
| AC10 | Scale to zero observed: `api` and `web` at 0 replicas after idle, and SQL `Paused` about 15–20 min after the last request. No probe or timer wakes SQL (≥ 1 h idle observed). | B | read-only `az` |
| AC11 | Guardrails exist: RG budget with 4 notifications, 3 metric alerts (+1 with Maps display), LA cap 0.15 GB/day and 30-day retention. App Insights shows requests, dependencies and the `RunnersDiary.Ai` meter, with no prompt or answer text (canary search). | B | `az` + portal |
| AC12 | Live AI eval: 3 runs, first-attempt pass ≥ 90 %, tokens and cost recorded (§12). After G7, a generation through `web` succeeds in production. | B | G6/G7 |
| AC13 | Real Entra sign-in and sign-out in production and locally (M3 AC3/AC4). **Deferred again if the tenant does not exist yet**; production then runs guests-only (`Auth__Entra__Enabled=false`). | B | manual, G8 |
| AC14 | Rollback drill: dispatching the previous SHA with `skip_migrations: true` restores it and the smoke test passes. A deliberately failing smoke step triggers the automatic rollback (once, on a test branch run). | B | manual |

## 2. Target architecture

```
                         Internet (HTTPS only, ACA-managed TLS)
                                     │
                    ┌────────────────▼────────────────┐  Container Apps environment `cae-rd-prod`
                    │ web  (external ingress, 0–2)    │  Consumption plan, no VNet, westeurope (was swedencentral; §12.7)
                    │ nginx: SPA + headers/CSP        │──── id-rd-web ──▶ ACR (AcrPull)
                    │ /api/* → https://<api int FQDN> │
                    └────────────────┬────────────────┘
                                     │ internal only
                    ┌────────────────▼────────────────┐
                    │ api  (internal ingress, 0–2)    │──── id-rd-api ──┬─▶ Azure SQL (free offer, Entra-only, polandcentral): db_datareader/writer
                    │ ASP.NET Core 10, /alive probes  │                 ├─▶ Storage blob `gpx` (shared key off): Blob Data Contributor
                    └─────────────────────────────────┘                 ├─▶ Key Vault: Secrets User (guest-token-signing-key only)
   job-guest-cleanup (cron weekly, API image, id-rd-api) ───────────────┤─▶ Foundry AIServices (local auth off), gpt-5.4-mini Global Std 30k TPM
   job-migrate (manual, efbundle + sqlcmd, id-rd-migrator = SQL admin)  └─▶ Azure Maps Gen2 (westeurope): Search via Entra (server-side)
                                                                       [opt. G9: SAS for display via id-rd-maps-sas]
   Log Analytics (cap 0.15 GB/day, 30 d) ◀── App Insights (OTel, Azure Monitor exporter) ◀── api, jobs; ACA console/system logs
   Budget (RG, $25) + metric alerts → action group (e-mail)

 Browser ── tiles/sprites/glyphs ──▶ tiles.openfreemap.org (MapLibre; privacy note)
 Browser ── MSAL redirect ──▶ <subdomain>.ciamlogin.com (Entra External ID, user's tenant)
 GitHub Actions (env `prod`, required reviewer) ── OIDC ──▶ id-rd-deployer: AcrPush, Container Apps (+Jobs) Contributor, MI Operator
 User laptop: `azd provision` / `azd down` (only through gates), live eval with `az login`
```

**Data flow:**
1. The browser hits `web`, either for static files or for `/api/*`.
2. nginx keeps the rightmost `X-Forwarded-For` value (the client) and proxies to `api` over the environment's internal ingress (HTTPS to the internal FQDN, certificate verified; §12).
3. The API validates the Entra or guest JWT and runs owner-scoped EF queries against SQL, using its managed identity.
4. GPX files are streamed through the API to and from Blob. Geocoding is a server-side call to `eu.atlas.microsoft.com`. An AI summary is a server-side call to the Foundry v1 endpoint.

Nothing in the browser holds an Azure credential.

**Region (as provisioned at G2, 2026-11-04; §12.7):** everything is in **`westeurope`** (user decision, Q2) except the **SQL server and database, which are in `polandcentral`** (`SQL_LOCATION`), because new SQL servers could not be provisioned for this subscription in West Europe, North Europe or Norway East. App ↔ DB traffic is therefore **cross-region** (West Europe → Poland Central: a few tens of ms per round trip, on top of the SQL resume time after auto-pause; acceptable at hobby scale, ⚠ to watch in App Insights dependency durations at G5). All data at rest stays in the EU. Maps was already `westeurope`, so it is now co-located.

## 3. Files by owning agent

### azure-infra
```
azure.yaml                                   # services api, web (host: containerapp, docker); hooks
infra/main.bicep                             # targetScope subscription → rg-runnersdiary-<env> + modules
infra/main.parameters.json                   # ${AZURE_ENV_NAME}, ${AZURE_LOCATION}, ${ENTRA_*}, ${AI_*}, ${MAPS_DISPLAY}, ${BUDGET_*} (no secrets)
infra/bicepconfig.json                       # linter rules as errors (AC1)
infra/modules/{monitoring,registry,identities,keyvault,storage,sql,foundry,maps,aca-env,app-api,app-web,jobs,alerts-budget,github-oidc,fetch-container-image}.bicep
infra/hooks/{postprovision.sh,predown.sh}    # ensure guest key if absent (never printed); print FQDN, redirect URIs, GitHub values
infra/sql/grant-app-users.sql                # idempotent CREATE USER … WITH SID … TYPE = E; role membership
infra/docker/{migrate.Dockerfile,migrate.sh} # runtime-deps + efbundle + go-sqlcmd
infra/scripts/smoke.sh                       # post-deploy smoke (§7.2)
src/RunnersDiary.Api/Dockerfile, .dockerignore (repo root)
web/Dockerfile, web/nginx/{default.conf.template,security-headers.conf.template}, web/public/robots.txt
.github/workflows/deploy.yml                 # new
.github/workflows/ci.yml                     # + job deploy-artifacts (bicep, actionlint, 3 images, web-image Playwright + curl); e2e-fullstack uses images
.github/dependabot.yml                       # + docker ecosystem (Dockerfiles), github-actions SHA bumps
```

### backend-dotnet
```
src/RunnersDiary.Api/Program.cs                         # `--job guest-cleanup` entry mode (Infrastructure-only host, exit code)
src/RunnersDiary.Infrastructure/Guests/GuestCleanupService.cs  # idle-skip: tick runs only if a SQL connection opened since last tick
src/RunnersDiary.Infrastructure/Persistence/SqlActivityInterceptor.cs  # DbConnectionInterceptor → connection counter
src/RunnersDiary.ServiceDefaults/Extensions.cs          # UseAzureMonitor() when APPLICATIONINSIGHTS_CONNECTION_STRING; sampling; resolve TODO(M7) (probes)
src/RunnersDiary.ServiceDefaults/RunnersDiary.ServiceDefaults.csproj, Directory.Packages.props  # Azure.Monitor.OpenTelemetry.AspNetCore 1.6.0 (pin; re-check at install)
src/RunnersDiary.Api/appsettings.json                   # Maps:Geocoding:GlobalDailyLimit, RateLimiting:MapsToken
tests/RunnersDiary.Api.Tests/**                         # job mode, idle-skip, config validation, exporter off without conn string
```

### maps-gpx (+ backend-dotnet wiring the endpoints, frontend-react the UI placement)
```
src/RunnersDiary.Infrastructure/Maps/{AzureMapsGeocoder.cs, GeocodeBudget.cs, AzureMapsSasTokenIssuer.cs, AzureMapsOptions.cs}
src/RunnersDiary.Api/Features/Maps/GetMapsToken.cs      # GET /api/maps/token
src/RunnersDiary.Api/Features/ClientConfig/GetClientConfig.cs   # maps.azureMapsDomain; accept Provider=AzureMaps with validation
src/RunnersDiary.Api/openapi/runnersdiary-api.json, web/src/api/generated/schema.d.ts  # regenerated
web/src/components/map/azure/AzureMapsMap.tsx           # azure-maps-control 3.x, lazy chunk; npm dependency (justified: brief-mandated Web SDK)
web/src/components/map/MapView.tsx                      # session fallback to MapLibre on token failure
web/src/i18n/en/maps.json                               # privacy note, geocode-budget message
```

### frontend-react
```
web/playwright.config.ts                     # PLAYWRIGHT_BASE_URL → no webServer (run against the nginx image)
web/e2e/support.ts                           # fixture: fail on securitypolicyviolation / CSP console errors
web/src/components/layout/ApiStatus.tsx      # verify: no /api/health call before a session exists (anonymous visits must not wake SQL)
```

### ai-insights
- Live eval runbook (§9.3) and the result record in §12.
- Verifies the Foundry token metric names for the daily alert (ADR 0034).
- Confirms the TPM unit (1 capacity = 1k TPM) for Data Zone Standard (→ Global Standard since 2026-11-04, §12 D-AI; resolved by the Learn check below).

### qa-tester
- Fills test gaps for Part A.
- Executes the live checklist (§7.3) together with the user after G4.
- Runs the rollback drill (AC14).

### reviewer
- Part A diff:
  - IaC least privilege against ADR 0031;
  - no secrets;
  - nginx headers and XFF;
  - workflow permissions and pins;
  - migration compatibility rule;
  - cost settings against §4.
- Part B: read-only review of the deployed configuration.

### architect
- After implementation: deviations (§12), `architecture.md` §6, ADR statuses, `api-contract.md` (maps token, client-config, 429 geocode budget).

## 4. Monthly cost estimate (USD, retail, `swedencentral` unless noted; retail prices API 2026-11-04)

> **Region changed at G2 (2026-11-04):** resources are in `westeurope`, SQL in `polandcentral` (§2, §12.7). The table below still uses the `swedencentral` retail prices it was built from. European region prices for these meters are usually close, and the large items (SQL free offer, Container Apps free grant, Foundry Global Standard per-token price) do not depend on the region. **⚠ Re-check the `westeurope` (and `polandcentral` for SQL) retail prices** with the retail prices API before G4; the numbers are not updated here until then. Cross-region app ↔ DB traffic adds inter-region data transfer (⚠ price not checked; negligible at hobby volumes of a few MB a day). Subscription: a Visual Studio subscriber (MSDN) credit subscription with a spending limit by default, so actual spend is capped by the monthly credit unless the user removes the limit (when the credit runs out, resources are disabled until the next period).

Assumptions:
- **Idle:** nobody uses the app.
- **Low traffic:** 1–3 users a day plus a few guests; ≤ 60 h a month of both apps awake; ≈ 30 AI summaries a month.
- **Worst case:** every in-app guardrail is hit every day (AI global budget, geocode budget), both apps run 2 replicas 24×7, and 10 M requests. Azure Maps display is **off** (default).

| Resource | SKU / setting | Idle | Low traffic | Worst case | Basis |
|---|---|---|---|---|---|
| Container Registry | Basic | 5.07 | 5.07 | 5.07 | $0.1666/day; 10 GB included |
| Container Apps (`api` 0.5 vCPU/1 GiB, `web` 0.25/0.5, jobs) | Consumption, min 0 / max 2 | 0 | 0 | ≈ 113 + 3 | Free per month: 180k vCPU-s, 360k GiB-s, 2 M requests (≈ 66 h of both apps awake). Active $0.000024/vCPU-s, $0.000003/GiB-s, $0.40 per 1 M requests |
| Azure SQL Database | GP serverless **free offer**, 0.5–2 vCores, `AutoPause` | 0 | 0 | **0** (the DB pauses until month end: an outage, not a bill) | 100k vCore-s, 32 GB data, 32 GB backup |
| Foundry gpt-5.4-mini | **Global Standard**, 30k TPM (was Data Zone Standard EU; §12 D-AI) | 0 | ≈ 0.2–1 | ≈ 88 (203 calls/day) – 176 (with transport retries) (was 97–194) | $0.75 / $4.50 per 1 M tokens (Global Standard prices since 2026-11-04; Data Zone was $0.825 / $4.95); no fixed fee (pay-per-token, [deployment types](https://learn.microsoft.com/azure/foundry/foundry-models/concepts/deployment-types)) |
| Azure Maps | Gen2, `westeurope`; geocoding only | 0 | 0 | **0** (≤ 2,325 searches: 75/day, durable and global across replicas and restarts, < 5,000 free; corrected in §12) | $4.50 per 1 K beyond 5 K free (search and base-map tiles) |
| Log Analytics + App Insights | PerGB2018, cap 0.15 GB/day, 30 d | 0 | 0 | ≈ 0–2 | 5 GB/month free, then $2.99/GB |
| Storage | StorageV2, Hot LRS | < 0.05 | < 0.10 | ≈ 1 | $0.0184/GB-month; ops $0.05 per 10 K writes |
| Key Vault | Standard, 1 secret | < 0.01 | < 0.01 | < 0.05 | $0.03 per 10 K operations |
| Monitor | 3–4 metric alerts, action group (e-mail) | ≈ 0.3–0.4 | ≈ 0.4 | ≈ 0.4 | ⚠ $0.10 per alert rule per month (not re-verified) |
| Budget, Entra External ID | — | 0 | 0 | 0 | budgets free; External ID free < 50 k MAU (email OTP add-on price ⚠) |
| **Total** | | **≈ $5.5** | **≈ $6–7** | **≈ $211 – $300** (was $220 – $320) | |

**One-off costs:**
- live AI eval ≤ ~$0.68 (48 calls × $0.0143 at the cap; typically about $0.18) (was ≤ $0.75 / $0.0157 / $0.20 at Data Zone prices);
- each deploy: about 2 min of migration-job compute (free grant) and one SQL wake.

**Azure Maps display (G9, off by default):**
- the first ≈ 75,000 tiles a month are free, then ≈ $0.30 per 1,000 tiles (a session is ≈ 100–300 tiles);
- **theoretical worst case at the SAS cap of 10 requests/s ≈ $7,900 a month** (ADR 0033), bounded only by alerts and the kill switch.

**Guardrails** (ADRs 0032–0034):
- **In-app hard bounds:** AI ledger quotas; geocode daily budget; SQL `AutoPause`; LA daily cap; `maxReplicas` 2; scale to zero; probes that never touch SQL; cleanup idle-skip; weekly (not daily) cleanup job; `robots.txt`.
- **Alerts:** a $25 RG budget (Actual 40/80/100 %, Forecasted 100 %); a daily AI-token alert; SQL free-amount < 10 %; API 5xx.
- **Levers without an image change:** `Ai__Enabled=false`, `Maps__Provider=MapLibre`, `maxReplicas`.

## 5. Contracts

### 5.1 Resources

Names use `<token>` = `uniqueString(subscription().id, env, location)`. All resources carry the tags `azd-env-name` and `app=runnersdiary`.

| Resource | Name | Key settings |
|---|---|---|
| Resource group | `rg-runnersdiary-prod` | `westeurope` (was `swedencentral`; §12.7) |
| Log Analytics / App Insights | `log-rd-prod` / `appi-rd-prod` | 30 d, `dailyQuotaGb` 0.15; workspace-based |
| ACR | `acrrd<token>` | Basic, admin user off |
| Identities | `id-rd-{api,web,migrator,deployer}` (+ `id-rd-maps-sas` in `westeurope` if G9) | ADR 0031 table |
| Key Vault | `kv-rd-<token>` | RBAC, soft delete 7 d, purge protection off; secret `guest-token-signing-key` (hook) |
| Storage | `strd<token>` | StorageV2 Hot LRS, shared key off, public blob off, TLS 1.2, container `gpx`, soft delete 7 d |
| SQL server / DB | `sql-rd-<token>` / `runnersdiary`; with `SQL_LOCATION` set: `sql-rd-<token>-<sqlLocation>` (prod: `sql-rd-m3dcwctu5kpwi-polandcentral`) | Region `SQL_LOCATION` (empty = primary; prod `polandcentral`); Entra-only, admin `id-rd-migrator`, firewall rule `AllowAzureServices` (0.0.0.0; was `AllowAllWindowsAzureIps`); GP_S_Gen5_2, min 0.5, `useFreeLimit: true`, `freeLimitExhaustionBehavior: AutoPause` |
| Foundry | `ais-rd-<token>` (custom subdomain) | kind `AIServices`, S0, `disableLocalAuth: true`; deployment `gpt-54-mini`: model `gpt-5.4-mini` `2026-03-17`, sku **`GlobalStandard`** (was `DataZoneStandard`; §12 D-AI), capacity `${AI_CAPACITY_KTPM}` = 30 (unit = 1k TPM, confirmed) |
| Azure Maps | `map-rd-prod` (`westeurope`) | Gen2 `G2`; `disableLocalAuth: true` unless G9; CORS = web origin |
| ACA environment | `cae-rd-prod` | Consumption, logs → `log-rd-prod`, no VNet |
| `web` | container app | external, target port 8080, 0.25/0.5, min 0 / max 2, HTTP scale 20 concurrent |
| `api` | container app | internal, port 8080, 0.5/1 GiB, min 0 / max 2, probes `/alive` (startup: 30 × 2 s) |
| `job-migrate` | job | Manual, 0.5/1 GiB, timeout 900 s, retry 1 |
| `job-guest-cleanup` | job | Schedule `${GUEST_CLEANUP_CRON}` = `15 3 * * 1`, 0.25/0.5, timeout 600 s, retry 1 |
| Budget, alerts | `budget-rd-prod`, `ag-rd-prod`, `alert-rd-*` | ADR 0034 |
| GitHub OIDC | federated credential on `id-rd-deployer` | subject `repo:stompsu/runners-diary:environment:prod` |

### 5.2 Configuration mapping

azd env values live in `.azure/prod/.env` (git-ignored, non-secret; the env was created as `prod`, not `rd-prod`, §12.7). They feed Bicep params, which become container env vars.

| API env var | Local (AppHost) | Azure `prod` | From |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Development | Production | Bicep |
| `ConnectionStrings__runnersdiary` | SQL container | `Server=tcp:<sql>.database.windows.net,1433;Database=runnersdiary;Authentication=Active Directory Managed Identity;User Id=<id-rd-api clientId>;Encrypt=True;Connect Timeout=60` | Bicep |
| `ConnectionStrings__blobs` | Azurite | `https://<st>.blob.core.windows.net/` | Bicep |
| `AZURE_CLIENT_ID` | — | `id-rd-api` client id (used by `DefaultAzureCredential` in the Blob client) | Bicep |
| `Database__MigrateOnStartup`, `Storage__CreateContainerOnStartup` | `true` | **unset** | — |
| `ForwardedHeaders__Enabled` / `__ForwardLimit` / `__KnownIPNetworks__0`, `__1` | `false` | `true` / `2` / `0.0.0.0/0`, `::/0` (ADR 0029 §3) | Bicep |
| `Auth__Guest__SigningKey` | Aspire secret param | `secretRef` → Key Vault (versionless) | Bicep + hook |
| `Auth__Entra__Enabled`, `__TenantId`, `__TenantSubdomain`, `__Audience`, `__SpaClientId`, `__ValidIssuers__0` | user-secrets `Entra:*` | `ENTRA_TENANT_ID`, `ENTRA_TENANT_SUBDOMAIN`, `ENTRA_API_CLIENT_ID`, `ENTRA_SPA_CLIENT_ID`, `ENTRA_ISSUER`; **`Enabled=false` when the tenant id is empty** | azd env |
| `Ai__Provider` / `Ai__Enabled` | None·Fake·Foundry / true | `Foundry` / `${AI_ENABLED}` (**false until G7**) | azd env |
| `Ai__Foundry__Endpoint`, `__Deployment`, `__ManagedIdentityClientId` | user-secrets `Ai:*` | Foundry `https://<sub>.openai.azure.com/`, `gpt-54-mini`, `id-rd-api` client id | Bicep outputs |
| `Maps__Provider` | MapLibre | `MapLibre` (`AzureMaps` after G9) | azd env `MAPS_DISPLAY` |
| `Maps__Geocoding__Provider`, `__GlobalDailyLimit` | None, — | `AzureMaps`, 75 | Bicep / appsettings |
| `Maps__AzureMaps__AccountClientId`, `__ManagedIdentityClientId`, `__Domain` | — (optional user-secrets) | Maps `uniqueId`, `id-rd-api` client id, `eu.atlas.microsoft.com` | Bicep |
| `Maps__AzureMaps__AccountResourceId`, `__SasPrincipalId`, `__SasRegions__0..1`, `__SasMaxRatePerSecond` | — | only with G9: resource id, `id-rd-maps-sas` principal id, `westeurope`/`northeurope`, 10 | Bicep |
| `APPLICATIONINSIGHTS_CONNECTION_STRING`, `OTEL_SERVICE_NAME` | — / `api` | App Insights / `api` | Bicep |

**`web` env:**
- `API_UPSTREAM=https://<api internal FQDN>` (Bicep output; the image default `http://api` is only for the CI/local stub; §12);
- `CSP_ENTRA_HOST=https://<subdomain>.ciamlogin.com` (empty → omitted);
- `CSP_MAP_HOSTS=https://tiles.openfreemap.org` (+ the atlas hosts with G9).

**Jobs:**
- `job-guest-cleanup`: the API's settings (except Entra and the guest key, which job mode does not need) + args `--job guest-cleanup`.
- `job-migrate`: `SQL_SERVER`, `SQL_DATABASE`, `AZURE_CLIENT_ID` (`id-rd-migrator`), `API_IDENTITY_NAME`, `API_IDENTITY_CLIENT_ID`.

**Other azd env values:** `AZURE_ENV_NAME`, `AZURE_LOCATION` (prod `westeurope`), `AZURE_SUBSCRIPTION_ID`, `AZURE_PRINCIPAL_ID` (set by azd), `BUDGET_AMOUNT` (25), `BUDGET_CONTACT_EMAIL`, `BUDGET_START_DATE` (`yyyy-MM-01`; must be set by hand before the first provision, §10), `SQL_LOCATION` (empty = primary region; prod `polandcentral`), `AI_CAPACITY_KTPM` (30), `GUEST_CLEANUP_CRON`, `GRANT_DEVELOPER_AI_ACCESS` (true).

### 5.3 API deltas

| Method & path | operationId | Policy | Success | Errors |
|---|---|---|---|---|
| `GET /api/maps/token` | `getMapsToken` | Auth; rate policy `maps-token` (users 30/h, guests 10/h; replaces the general one) | 200 `{ token: string; expiresAt: string }`, `Cache-Control: private, no-store` | 401, 429, 503 `feature-unavailable` (provider ≠ AzureMaps or `listSas` failure; no details leak) |
| `GET /api/geocoding/search` | unchanged | + global daily budget (durable in SQL, all replicas; corrected in §12) | unchanged | + 429 `rate-limited`, `code: geocode-budget` (`Retry-After` until 00:00 UTC) |
| `GET /api/health` | unchanged | Anon; rate policy `health` (added in review, §12) | unchanged; anonymous = live checks only (no SQL) outside Development | + 429 |
| `GET /api/client-config` | unchanged | — | `maps` gains `azureMapsDomain: string \| null` | — |

**Job CLI:** `dotnet RunnersDiary.Api.dll --job guest-cleanup` returns exit code 0 on success, 1 on failure. Any other `--job` value fails at startup.

### 5.4 Web tier

Headers, routing, timeouts and client-IP handling: [ADR 0029](../adr/0029-web-tier-nginx-container.md) §1–§4 (normative).

### 5.5 Deploy workflow (`deploy.yml`)

- **Triggers:**
  - `workflow_dispatch` with inputs `ref` (default `main`) and `skip_migrations` (bool);
  - `workflow_run` of `CI` completed with success on `main`.
  - Every job has `if: vars.DEPLOY_ENABLED == 'true'`.
- **Settings:** `environment: prod` (required reviewer = the user; deployment branches `main`); `permissions: { contents: read, id-token: write }`; `concurrency: deploy-prod` (no cancel).
- **Secrets** (environment `prod`): `AZURE_CLIENT_ID` (`id-rd-deployer`), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.
- **Variables:** `AZURE_RESOURCE_GROUP`, `ACR_NAME`, `WEB_FQDN`.
- **Steps:**
  1. checkout `ref`;
  2. `azure/login` (OIDC);
  3. `az acr login`;
  4. `docker build`/`push` `api`, `web` and `migrate` with tag `sha-<sha>` (`ef migrations bundle` is built inside the migrate Dockerfile);
  5. unless `skip_migrations`: `az containerapp job update -n job-migrate --image …`, then `job start`, then poll `job execution show` until `Succeeded`/`Failed` (10 min);
  6. read the current `api`/`web` images;
  7. `az containerapp update` `api`, `web` → new tag; `az containerapp job update job-guest-cleanup`;
  8. `infra/scripts/smoke.sh https://$WEB_FQDN` (§7.2);
  9. on failure: re-apply the step-6 images, then exit 1.

### 5.6 Entra External ID: what the user adds in their tenant (after G2, when the FQDN is known)

- SPA registration `runners-diary-web` → *Authentication → Single-page application*:
  - add `https://<web-fqdn>/redirect.html` (sign-in bridge, ADR 0014);
  - add `https://<web-fqdn>/signin` (MSAL `postLogoutRedirectUri`, `web/src/auth/entra/msalClient.ts`).

  The localhost URIs stay.
- No API registration change. No client secret.
- `azd env set ENTRA_TENANT_ID … ENTRA_TENANT_SUBDOMAIN … ENTRA_API_CLIENT_ID … ENTRA_SPA_CLIENT_ID … [ENTRA_ISSUER …]`, then G8.

## 6. Implementation steps and confirmation gates

### 6.1 Part A: offline; each step is one reviewable commit on `m7-azure-deployment`

1. **azure-infra + frontend-react: images and the web tier.**
   - Dockerfiles, nginx templates, `robots.txt`, `.dockerignore`.
   - Playwright `PLAYWRIGHT_BASE_URL` override and the CSP-violation fixture.
   - The CI `deploy-artifacts` job (3 image builds, web image + Playwright + curl header and proxy assertions with a stub upstream).
   - `e2e-fullstack` switched to the images.
   - Dependabot `docker`.
2. **backend-dotnet: Azure readiness.**
   - Job mode, idle-skip interceptor, Azure Monitor exporter, config validation, probe TODO.
   - Check that `ApiStatus` does not poll anonymously (frontend-react).
   - Tests.
3. **maps-gpx (+ backend-dotnet, frontend-react): Azure Maps adapters** per §5.3 and ADR 0033.
   - **Spike first:** `listSas` with a narrow role is checked only at G9, so the code takes the role-independent path and has tests with a fake `ArmClient`/HTTP handler.
   - OpenAPI/TS regenerated.
4. **azure-infra: Bicep + azd.**
   - All modules, `azure.yaml`, hooks, `bicepconfig.json`, the grant SQL, the migrate Dockerfile.
   - The CI job gains `az bicep build` + lint.
   - Offline only; **what-if is G1**.
5. **azure-infra: `deploy.yml` + `smoke.sh`.** Guarded by `DEPLOY_ENABLED`; `actionlint` in CI.
6. **qa-tester:** gaps, plus a full local run including the image-based full-stack job.
7. **reviewer:** review of the Part A diff.
8. **architect:** docs (deviations in §12).
9. **The main agent:** commit "M7 (part A): deployment artifacts". **Stop for approval.** Nothing in Azure exists at this point.

### 6.2 Part B: cloud actions, each needing the user's explicit "yes" for that gate, in order

| Gate | Action (who runs it) | What changes | Cost impact |
|---|---|---|---|
| **G0** | User: §10 steps 1–4 (subscription, `azd` install, quota check: **read-only**) | nothing | $0 |
| **G1** ✔ 2026-11-04 | `azd provision --preview` (what-if) by the user, or by azure-infra after the user's yes; full detail via `az deployment sub what-if` | nothing (shows the planned resources) | $0 |
| **G2** ✔ 2026-11-04 | `azd hooks run preprovision`, `azd provision`, `azd hooks run postprovision` (§10 step 5; the guest key is generated by `preprovision`, written by Bicep, cleared locally by `postprovision`) | created the RG and 25 resources (§5.1), the identities and roles, and the GitHub federated credential; apps run a placeholder image | from now on ≈ **$5.5/month idle** (ACR; ⚠ westeurope prices, §4) |
| **G3** ✔ 2026-11-04 | User: GitHub environment `prod` (reviewer, `main` only), 3 secrets, variables, `DEPLOY_ENABLED=true` | GitHub settings only | $0 |
| **G4** ✔ 2026-11-05 (run 4) | User: run `deploy.yml` (dispatch) and **approve** it | pushes 3 images (≈ 0.5 GB of the 10 GB ACR quota), runs the migration job (schema + API DB user), deploys apps and jobs | ≈ $0 (free grants) |
| **G5** ✔ 2026-11-05 | User + qa-tester: go-live checklist §7.3, incl. the XFF test (6 guest creations) | app data only (guests expire) | $0 |
| **G6** ✔ 2026-11-06 (prompt v2) | User: live AI eval from the laptop (§9.3), with AI still disabled in prod | none | ≤ ~$0.68 (was ≤ $0.75) |
| **G7** ✔ 2026-11-06 | `azd env set AI_ENABLED true` + `azd provision` (new `api` revision) | AI generation on in prod | usage; ceiling $88–176/month (was $97–194; Global Standard prices since 2026-11-04) |
| **G8** | User registers the redirect URIs (§5.6) in their tenant, then `azd env set ENTRA_*` + `azd provision` | Entra sign-in on | $0 (< 50 k MAU) |
| G9 *(optional)* | `azd env set MAPS_DISPLAY true` + `azd provision` | SAS identity, custom roles, Maps local auth on, `Maps__Provider=AzureMaps` | free ≤ 75 k tiles/month; risk per §4 |
| G10 *(recurring)* | every later deploy = GitHub approval; every Bicep change = G1 + G2 again | — | — |
| G11 *(as needed)* | Break-glass SQL: user as Entra admin + client-IP rule; revert afterwards | SQL server config | $0 |
| G12 *(as needed)* | PITR restore (§9.2): creates a **new, billable** serverless DB | new DB | ≈ GP serverless rates until deleted ⚠ |
| G13 *(end)* | `azd down --purge` (§9.4) | deletes everything, including data | stops all cost |

The AI eval (G6) comes before G7, so a failing model or prompt never reaches users. G8 can happen any time after G4. Production is guests-only until then.

## 7. Test plan (qa-tester)

### 7.1 Offline (CI, every PR)

- **`deploy-artifacts` job:**
  - `az bicep build` + lint (rules as errors); `actionlint`;
  - `docker build` of the three images;
  - `docker run` web with a stub upstream:
    - `curl -I` assertions per ADR 0029 §4 (CSP present and environment-templated; COOP absent only on `/redirect.html`; `no-store` on HTML; `immutable` on `/assets`; HSTS; `nosniff`; `frame-ancestors 'none'`);
    - SPA fallback for `/diary/2026/11`; 404 for `/assets/missing.js`;
    - `/api/x` reaches the stub with a single-value `X-Forwarded-For` even when the client sends a spoofed chain;
    - a 21 MB POST passes; a 90 s delayed stub response is not cut;
  - the Playwright suite against the image with the CSP fixture.
- **.NET:**
  - job mode (exit codes, Infrastructure-only host, no Kestrel);
  - idle-skip (no tick query without prior activity; a tick after activity);
  - `UseAzureMonitor` not registered without a connection string;
  - Azure config validation (missing Maps ids with `AzureMaps` → startup failure);
  - geocoder against a fake handler (EU domain, `x-ms-client-id`, `top=5`, bearer scope) and the daily budget → 429 + `Retry-After`;
  - SAS issuer: caching, refresh at < 30 min, one in-flight refresh, failure → 503 without details;
  - `/api/maps/token` in the endpoint inventory (Auth), the 401 sweep and isolation (no user data);
  - `Europe/Warsaw` resolves in the API image (the full-stack journey creates a run in that zone).
- **Vitest / Playwright:**
  - the `AzureMapsMap` provider with a stubbed SDK (token flow, `domain`, fallback to MapLibre on 503);
  - the privacy note;
  - `bundle-budget` (Azure Maps stays a lazy chunk).
- **Full-stack (main):** the same specs against the images (AC4).

### 7.2 Smoke (`smoke.sh`, every deploy)

1. `GET /` → 200 with a CSP header.
2. `GET /redirect.html` → no COOP.
3. `GET /api/health` anonymous → 200 (live checks only); after step 5, with the guest token → 200 within 150 s (full report incl. SQL; tolerates a SQL resume).
4. `GET /api/client-config` → 200.
5. `POST /api/guest-sessions` → 201, then `GET /api/me` (zone `Europe/Warsaw`) and `GET /api/calendar` for the current month → non-empty.
6. `GET /api/maps/token` → 503 unless G9.

Each run creates one guest.

### 7.3 Live checklist (after G4; read-only `az` + browser)

- AC8: `az containerapp show` (ingress external/internal, env has no plain secret values); `curl` to the API FQDN from outside fails; `az sql server ad-only-auth get`; `az storage account show --query allowSharedKeyAccess`; `az cognitiveservices account show --query properties.disableLocalAuth`; `az keyvault secret list` (exactly one).
- AC9: the XFF test.
- AC10: `az containerapp replica list` = 0 after 10 min idle; `az sql db show --query status` = Paused after ~20 min.
- AC11: budget and alerts listed; App Insights requests present; a canary search for prompt text returns nothing.
- After 48 h: **Cost analysis by meter** shows no unexpected meter (in particular no Container Apps "Environment Management Hour", ⚠ new meter effective 2026-11-01) and no Defender plan.
- Rollback drill (AC14). One manual `job-guest-cleanup` start succeeds.

## 8. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| SQL free allowance exhausted by wakes (≈ 220 wake cycles/month) | DB paused until next month: **app down** | Probes `/alive` only; cleanup idle-skip; weekly job; anonymous endpoints without SQL (`client-config`, and `ApiStatus` only after sign-in); `robots.txt`; free-amount alert at 10 %; Q10 (`BillOverUsage` is irreversible). |
| No Data Zone quota for gpt-5.4-mini in `swedencentral`, or < 30k TPM | G2 fails at the deployment | Read the quota before G1 (§10 step 3); `AI_CAPACITY_KTPM` param; fallback region from ADR 0027's EU list (polandcentral, germanywestcentral); AI is off until G7 anyway. **Materialized 2026-11-04:** Data Zone quota 0 in all five EU regions checked → Global Standard (§12 D-AI). |
| `GlobalStandard` quota is a subscription-wide pool shared with the user's other deployments (1 000k TPM, 700k used) | If other deployments grow past 970k, G2 fails, or a later re-provision does | Re-read the free quota (≥ 30k) right before G1 and any later G2; `AI_CAPACITY_KTPM` can go lower (more 503 `ai-busy`). |
| ~~⚠ The capacity unit for `DataZoneStandard` is not 1k TPM~~ | Wrong throughput | Resolved: 1 capacity = 1k TPM (Learn check below). |
| AI prompts processed outside the EU (Global Standard) | Data-residency expectation | Only the metrics-only fact sheet is sent (ADR 0026 amendment); user accepted 2026-11-04; switch back to `DataZoneStandard` if EU quota is granted. |
| New SQL servers restricted for the subscription in a region (**materialized at G2, 2026-11-04**: westeurope, northeurope, norwayeast) | Provision fails at SQL; a failed create can hold the server name in that region | Check the SQL capabilities API before G1 (§10 step 5.1); `SQL_LOCATION` + region-suffixed server name (§12.7 B2/B3). A teardown and re-provision must re-check, since availability can change. |
| XFF chain differs from the design (extra hop) | All guests share one bucket, or spoofing | nginx overwrites to a single value; `ForwardLimit` and `KnownIPNetworks` are config; AC9 before announcing the URL. |
| CSP or COOP breaks MSAL or MapLibre | Sign-in or map fails | CI runs the full Playwright suite on the image with CSP; COOP off for `redirect.html`; real-tenant check at G8; header tweaks need only a web redeploy. |
| Container Apps "Environment Management Hour" ($0.10/h, new meter 2026-11-01) applies to Consumption environments | +$73/month | Learn says management fees apply only to Dedicated profiles, private endpoints and planned maintenance (none used); checked in Cost analysis 48 h after G2; if charged, stop and reassess (Q14). |
| Migration incompatible with the running release | Errors during deploy; rollback impossible | Expand/contract rule (ADR 0032), reviewer gate, migration before app update, fix-forward. |
| `CREATE USER … WITH SID … TYPE = E` or the UAMI admin `sid` behave differently than documented ⚠ | First migration job fails | The job fails before the apps update; fallback G11 break-glass with the same script run by the user once. |
| Deployer OIDC identity abused (compromised workflow or action) | Code runs as the app identities | Environment approval + `main`-only; SHA-pinned actions; no Owner, Key Vault or SQL-control rights; `DEPLOY_ENABLED` switch. |
| Cold start: web + API from zero + SQL resume (up to ~60 s) | Slow first guest creation | M3 slow-start UI; `Connect Timeout=60` + EF retry; accepted for scale to zero. |
| OpenFreeMap outage (no SLA) | Blank map | Fallback panel and tables; G9 or a self-hosted PMTiles path later. |
| LA daily cap hit | Telemetry gap until the reset | Sampling; the budget notifies; cap tuned after 2 weeks of data. |
| Soft-deleted Key Vault or Foundry after `azd down` | Name or quota blocked on re-create | `azd down --purge`. |
| Image supply chain (base images, npm) | Vulnerable runtime | Digest pins + Dependabot; chiseled API image; `npm ci` with lockfile. |

## 9. Runbook

### 9.1 Rollback
- **Code:** Actions → `deploy` → *Run workflow* with `ref` = the last good SHA and `skip_migrations` = true, then approve.
- **Infra:** `git revert`, then G1 + G2.
- **A bad migration:** a fix-forward migration through the normal deploy. There are no down-migrations.

### 9.2 Backup and restore (basics)
- **SQL:** automatic backups with point-in-time restore (⚠ retention days on the free offer to be read after G2). A restore creates a **new database** that cannot be on the free offer (Learn: the free tier applies only to new databases), so it is gate **G12**. Procedure:
  1. `az sql db restore --dest-name runnersdiary-restore --time <UTC>`;
  2. verify via break-glass (G11);
  3. either repoint `ConnectionStrings__runnersdiary` or copy data back;
  4. delete the restored DB.
- **Blobs:** soft delete 7 days (`az storage blob undelete` per prefix).
- **Key Vault:** soft delete 7 days; the guest key is regenerable anyway.
- **Code, infra and seed data:** git. No geo-redundant backup (cost).

### 9.3 Live AI eval (G6; closes M6 §12.6)
1. `azd env get-values` → Foundry endpoint and deployment.
2. `dotnet user-secrets set "Ai:Endpoint" … "Ai:Deployment" …` in the AppHost.
3. `az login`. The user holds Cognitive Services User through `GRANT_DEVELOPER_AI_ACCESS`; allow ~5 min for propagation.
4. Run `RD_AI_LIVE_EVAL=1 dotnet test --project tests/RunnersDiary.Ai.Tests` three times.
5. Do one manual generation in the AppHost.
6. Record in §12: the first-attempt pass rate, tokens, cost, `ModelId`, and the 429 headers if seen.

Below 90 %: prompt v2 or a model change (a Bicep param, G1 + G2) before G7.

### 9.4 Teardown
1. `azd down --purge` (G13). It deletes the RG (data, identities, federated credential, budget, alerts) and purges Key Vault and Foundry.
2. If G9 was done: `az role definition delete` for `RD Maps SAS Issuer` and `RD Maps Render Reader`.
3. Remove the production redirect URIs from the SPA registration.
4. Delete the GitHub environment `prod` and set `DEPLOY_ENABLED=false`.

### 9.5 Saved KQL (App Insights)
- **AI generations by outcome:** `customMetrics | where name startswith "runnersdiary.ai" | summarize sum(valueSum) by name, bin(timestamp, 1d)` (names per the M6 meter).
- **429 by policy:** `requests | where resultCode == "429" | summarize count() by name, bin(timestamp, 1h)`.
- **Guest creations:** `requests | where name == "POST /api/guest-sessions" | summarize count() by bin(timestamp, 1h)`.
- **Cold starts:** `traces | where message has "Application started" | project timestamp, cloud_RoleInstance`.

## 10. What the user must do (in order)

1. **Subscription:** choose or create the Azure subscription (a pay-as-you-go or credits subscription in your name; *Azure for Students Starter* is not eligible for the SQL free offer). Tell me its type: credits or a spending limit change the risk picture. Make sure **no other free-offer SQL database** in it pins a different region.
2. **Tools:**
   - install `azd` (`brew install azd`; the plan assumes the current 1.x);
   - `azd auth login`;
   - `az login` (2.91.0 is fine);
   - `az account set -s <id>`.
3. **Quota check (read-only):** in the Foundry portal or `az cognitiveservices usage list -l swedencentral`, check **`OpenAI.GlobalStandard.gpt-5.4-mini`**: limit minus current usage must be **≥ 30** (k TPM). (Changed 2026-11-04 from the Data Zone Standard quota, which is 0 on this subscription; §12 D-AI. Done once on 2026-11-04: 1 000 limit, 700 used, 300 free. Re-read right before G1, because the pool is shared with your other deployments.)
4. **Answer §11.** Optionally create the Entra external tenant now (M2 §10); otherwise production starts guests-only.
5. **G1 + G2: the provisioning procedure** (as run on 2026-11-04 with azd 1.37.2; §12.7). Replace the `<…>` values; nothing here is secret, but the e-mail stays out of the repo.
   1. **Check SQL region availability first** (read-only). New SQL servers can be restricted per subscription and region, independently of the portal's region list:
      ```bash
      az rest --method get --url "https://management.azure.com/subscriptions/<sub-id>/providers/Microsoft.Sql/locations/<region>/capabilities?api-version=2023-08-01" --query "{status:status, reason:reason}"
      ```
      Use a region whose `status` is `Available`. "Provisioning is restricted in this region" needs a support request; "Subscriptions are restricted" is a hard no. On 2026-11-04 `westeurope`, `northeurope` and `norwayeast` were restricted for this subscription; `polandcentral` was available.
   2. Tools and environment:
      ```bash
      brew install azd
      azd auth login
      azd env new prod --subscription <sub-id> --location westeurope
      azd env set BUDGET_CONTACT_EMAIL <e-mail>
      azd env set BUDGET_START_DATE <yyyy-MM-01>      # first day of the current month
      azd env set SQL_LOCATION polandcentral          # omit (empty) to keep SQL in the primary region
      ```
      `BUDGET_START_DATE` must be set by hand: **azd validates required Bicep parameters before it runs the `preprovision` hook**, so the hook cannot supply it on the first run.
   3. **G1** (what-if, $0): `azd hooks run preprovision`, then `azd provision --preview`. azd's summary lists only the main resource types; for the full per-resource detail run `az deployment sub what-if` with the same template and parameters. After a partial provision, what-if may report `NestedDeploymentShortCircuited` and skip modules whose parameters use outputs of existing resources: an ARM limitation, not a template error (§12.7).
   4. **G2** (provision): run the hooks explicitly around the provision, because `azd provision` (incl. `--no-prompt`) did **not** run the project hooks in this setup (⚠ §12.7):
      ```bash
      azd hooks run preprovision      # creates GUEST_KEY_BOOTSTRAP only if the vault secret is absent; never prints it
      azd provision
      azd hooks run postprovision     # clears GUEST_KEY_BOOTSTRAP locally; prints the web URL, redirect URIs and GitHub values
      ```
      Never skip `postprovision`: otherwise the bootstrap key stays in `.azure/prod/.env`. Send me the printed web FQDN.

      `azd provision` deploys only when the template or its parameters changed since its last run (it compares a hash, not Azure). When only the live state changed (e.g. the first provision after G4, which turns the placeholder settings into port 8080 + our probes), use `azd provision --no-state`.
6. **G3:** in GitHub, create the environment `prod` with yourself as required reviewer and branch `main`; add the 3 secrets and the variables printed by the hook; set `DEPLOY_ENABLED=true`. Optionally add the new CI job to branch protection. The values from the 2026-11-04 provision are in §12.7.
7. **G4:** run *deploy* and approve it. **G5:** the go-live checklist with qa-tester (the XFF test from your network, then from your phone on mobile data).
8. **G6:** the live AI eval. **G7:** enable AI.
9. **G8** (when the tenant is ready): add the two redirect URIs (§5.6), set the `ENTRA_*` values, provision.
10. Approve each later deploy in GitHub; keep the budget e-mail address current.

## 11. Open questions (recommended defaults in bold)

1. **Environments:** **one `prod`** (local Aspire = dev), or `prod` + a cloud `dev` (≈ +$0–5/month, doubled gates).
2. **Region:** **`swedencentral`** (Maps in `westeurope`), or `polandcentral` (closest; also in the gpt-5.4-mini Data Zone table). (2026-11-04: both are also in the Global Standard table; the Data Zone quota is 0 in both, so this no longer favours either. §12 D-AI.)
3. **Registry:** **ACR Basic ($5.07/month, MI pull)**, or GHCR (free; private images need a PAT stored as an ACA secret, which breaks "MI everywhere", or public images).
4. **Map display in production:** **MapLibre + OpenFreeMap, with Azure Maps geocoding (capped under the free tier); Azure Maps display built but off (G9)**, or Azure Maps display from day one (brief literal; open-ended worst case).
5. **Guest cleanup job:** **weekly (Mon 03:15 UTC)**, or daily (brief literal; ≈ 14–27 % of the SQL free allowance), or none (hosted service only).
6. **Budget:** **$25/month on the RG**, notifications at Actual 40/80/100 % and Forecasted 100 % to one e-mail (set as an azd env value, never committed).
7. **AI at go-live:** **off until the live eval passes (G6 → G7)**, or on immediately.
8. **Deploy trigger:** **after green CI on `main` plus manual dispatch, always behind your approval**, or manual dispatch only.
9. **Domain:** **the default `*.azurecontainerapps.io` FQDN**, or a custom domain now (managed certificate free; DNS needed).
10. **SQL at free-limit exhaustion:** **`AutoPause` (an outage, never a bill)**, or `BillOverUsage` (irreversible; ≈ GP serverless rates).
11. **Foundry capacity:** **30k TPM** (ADR 0027), or lower if the quota is short (more 503 `ai-busy`).
12. **Go-live without Entra:** **yes, guests-only until G8**, or wait for the tenant.
13. **Telemetry:** **LA cap 0.15 GB/day, 30-day retention, 5 traces/s sampling**, or a higher cap (cost beyond 5 GB/month is $2.99/GB).
14. **If the new ACA "Environment Management Hour" meter turns out to apply to Consumption environments ($73/month):** **stop and reassess** (e.g. move `web` + `api` into one app, or to App Service F1/B1), or accept.

**Answers (user, 2026-11-04):** all recommended defaults.
- Q1 one `prod`.
- Q2 ~~`swedencentral` (Maps in `westeurope`)~~ → **`westeurope`** (user decision 2026-11-04, before G1), so everything except SQL is co-located with Maps. gpt-5.4-mini Global Standard is offered there; the Global Standard quota pool is shared across regions (300k of 1 000k free). **SQL in `polandcentral`** (user choice at G2, 2026-11-04), because new SQL servers were not available to this subscription in `westeurope` (§12.7).
- Q3 ACR Basic.
- Q4 MapLibre + OpenFreeMap display; Azure Maps geocoding capped under the free tier; Azure Maps display built but off (G9).
- Q5 weekly cleanup job.
- Q6 $25 RG budget.
- Q7 AI off until G6 passes.
- Q8 deploy after green CI on `main` + manual dispatch, always approved.
- Q9 default FQDN.
- Q10 `AutoPause`.
- Q11 30k TPM. (2026-11-04: on a **Global Standard** deployment; 300k TPM free in the subscription's global pool. §12 D-AI.)
- Q12 guests-only until G8.
- Q13 LA cap 0.15 GB/day, 30 days, 5 traces/s.
- Q14 resolved by the Learn check: the management meter applies only to features not used here.

Approval covers Part A only; every Part B gate still needs its own explicit "yes".

## 12. Implementation notes & deviations

**Part A implemented 2026-11-04 on branch `m7-azure-deployment` (steps 1–5 of §6.1, plus QA and review). Nothing exists in Azure.** The ADRs carry the normative detail in their 2026-11-04 amendments: [0029](../adr/0029-web-tier-nginx-container.md), [0030](../adr/0030-azure-topology-iac-and-deployment.md), [0031](../adr/0031-identities-secrets-and-data-access.md), [0032](../adr/0032-migrations-and-jobs-in-azure.md), [0033](../adr/0033-production-maps-and-azure-maps.md), [0034](../adr/0034-cost-guardrails-and-observability.md).

**Verified (2026-11-04):** local, no Azure:
- Build: 0 warnings; no OpenAPI/TS drift; no vulnerable NuGet packages (8 projects); `npm audit --omit=dev` finds 0 vulnerabilities.
- .NET: 2399 tests, 2393 passed, 6 live AI evals skipped (`CI=true`, Testcontainers).
- Web: Vitest 963/963; `test:tz` 766 per zone; Playwright 108/108 on `vite preview`; bundle initial load 179.1 kB (183,419 B) against a 201.5 kB limit; lint, typecheck and Prettier clean.
- Infra: `az bicep build` and lint clean on main and all modules; `actionlint` and `shellcheck` clean.
- Images: API, web and migrate all build and run non-root (`app`, `nginx`, 1654).
- Web image: `verify-web-image.sh` passes every check; Playwright against the nginx image under the real CSP 111/111.
- Image-based full stack (the `efbundle` applied all 4 migrations, including `GeocodeUsage`): 9/9.
- `smoke.sh` against it: 9/9.
- A bad-config API container exits 1 in about 1 s, naming keys only.

### 12.1 What was built (summary)

- **Images** (all base images digest-pinned):
  - `api`: `aspnet:10.0-noble-chiseled-extra`, non-root (user name `app`), ICU + tzdata, ≈ 101 MB compressed;
  - `web`: nginx unprivileged, ≈ 27 MB;
  - `migrate`: self-contained EF bundle (built in the Dockerfile) + go-sqlcmd `v1.10.0` (SHA-256 checked) on `runtime-deps`, ≈ 116 MB.
- **Web tier:** nginx per ADR 0029 + amendment (CSP without `unsafe-inline`/`unsafe-eval`, style hashes, `.mjs` MIME fix, `/nginx-alive`, HTTPS upstream with verification, XFF rightmost + overwrite, 120 s timeouts, 21 MB body).
- **Frontend:** `z.config({ jitless: true })` in `web/src/features/runs/runFormSchema.ts`; Playwright `PLAYWRIGHT_BASE_URL` + CSP-violation fixture + `web/e2e/csp-guard.spec.ts`; `ApiStatus` once per page load; `AzureMapsMap` (`azure-maps-control` 3.7.4, lazy: `azure-maps-vendor` ≈ 440 kB gzip, `AzureMapsMap` ≈ 1.8 kB) with session fallback and the privacy note.
- **Backend:** `--job guest-cleanup` (exit 0/1, passes until none removed, cap 1,000); `SqlActivityInterceptor` + idle-skip; Azure Monitor exporter only with a connection string (5 traces/s); production config validator (key names only); top-level startup failure handling; EF command log at Warning; health split (anonymous = live only, authenticated = full, policy `health`); durable geocode budget (`GeocodeUsage`); `AzureMapsGeocoder`, `AzureMapsSasTokenIssuer`, `GET /api/maps/token`, `client-config.maps.azureMapsDomain`. OpenAPI + TS regenerated.
- **IaC:** `infra/main.bicep` + 15 modules (50 resource declarations), `bicepconfig.json` with **all** rules as errors incl. `use-recent-api-versions` (730 d); hooks `preprovision.sh` (new), `postprovision.sh`, `predown.sh`; `infra/sql/grant-app-users.sql`; `infra/scripts/smoke.sh`.
- **CI/CD:** `ci.yml` job `deploy-artifacts` (Bicep build/lint, actionlint via a digest-pinned image, shellcheck, 3 image builds, header verify script, whole Playwright suite against the web image); `e2e-fullstack` on the built images; `deploy.yml` (OIDC, env `prod`, `DEPLOY_ENABLED`, SHA-pinned actions); Dependabot `docker`.

### 12.2 Deviations from the plan

| # | Plan said | Built | Why / where recorded |
|---|---|---|---|
| D1 | `API_UPSTREAM=http://api` (§2, §5.2; ADR 0029 §1) | `https://<api internal FQDN>` with SNI, `proxy_ssl_verify on` + CA bundle; image default `http://api` only for the CI stub | TLS-verified hop, no `allowInsecure` on the API. ADR 0029 amend. §2 |
| D2 | CSP per ADR 0029 §4; `style-src-attr 'unsafe-inline'` as the fallback | `style-src 'self'` + **sha256 hashes** (`CSP_STYLE_HASHES`) for the `<style>` elements of Sonner and react-remove-scroll; `worker-src 'self' blob:`; `font-src` incl. map hosts; `frame-src 'self' <entra>` or `'none'`; `frame-ancestors 'self'` only on `/redirect.html` | Zero violations without any `unsafe-*`; MSAL silent iframe. ADR 0029 amend. §6 |
| D3 | — | `.mjs` served as `text/javascript` | The base image lacks it; MapLibre's module worker failed under `nosniff`. ADR 0029 amend. §5 |
| D4 | — | zod `jitless` | No `unsafe-eval` in the CSP. ADR 0029 amend. §6 |
| D5 | Web probes unspecified | `GET /nginx-alive` (nginx only) | Probes never reach the API. ADR 0029 amend. §1 |
| D6 | `postprovision` creates the guest key via `az` (ADR 0030 §6, 0031 §2) | **`preprovision.sh`** generates it only if absent → secure param `guestSigningKeyBootstrap` (temporary azd value `GUEST_KEY_BOOTSTRAP`, cleared by `postprovision.sh`); also sets `BUDGET_START_DATE` once | Bicep writes the secret in the same deployment that references it; no second copy on disk. ADR 0030 amend. §4, 0031 amend. §2 |
| D7 | Key Vault Secrets User on the secret (ADR 0031 §1) | at **vault** scope | The secret may not exist when roles are assigned; the vault holds one secret. ADR 0031 amend. §1 |
| D8 | Global geocode budget **per replica, in memory** (ADR 0033 §2; §4 "≤ 4,560", §5.3) | **Durable** `GeocodeUsage` table, atomic increment, global across replicas and restarts; 429 `code: geocode-budget` | Review H2. Worst case 2,325/month. ADR 0033 amend. §1; §4 and §5.3 corrected above |
| D9 | Maps usage alert only with display, > 4,000/24 h (ADR 0033 §3, 0034 §4) | **Unconditional**, > 200/24 h | Review H2. ADR 0034 amend. §4 |
| D10 | Anonymous `/api/health` = full report, cached | Outside Development: anonymous = live checks only; token = full (cached 5 min); policy `health` 30/min | Review H1. ADR 0034 amend. §3, api-contract *Added in M7* |
| D11 | Apps start on a placeholder (implicit) | `mcr.microsoft.com/k8se/quickstart`, **target port 80, no probes** until the first deploy | Review M1. ADR 0030 amend. §3 |
| D12 | — | `id-rd-api` gets Managed Identity Operator on `id-rd-maps-sas` (G9 only); SAS custom roles RG-scoped | Review M5. ADR 0031 amend. §3 |
| D13 | Migrate image ≈ 80–100 MB; bundle built by CI | ≈ 116 MB; bundle built inside the Dockerfile | Simpler CI. ADR 0032 amend. §1 |
| D14 | Budgets API `2024-08-01` | `2026-06-01` (and KV `2026-02-01`, SQL `2025-01-01`, Maps `2023-06-01`, `metricAlerts` `2026-01-01`) | `use-recent-api-versions` as error. ADR 0030 amend. §2 |
| D15 | Metric names ⚠ | `TokenTransaction` (Total), `free_amount_remaining` (Minimum), `requests/failed` (Count), Maps `Usage` (Count) | Resolved at implementation. ADR 0034 amend. §4 |
| D16 | — | API startup failure → `Startup failed (<Type>)`, exit 1 | QA finding Q1 below. ADR 0032 amend. §6 |
| **D-AI** | Foundry gpt-5.4-mini **Data Zone Standard EU** (ADR 0027 §5, §5.1, §4) | **Global Standard**: `infra/modules/foundry.bicep` `sku: { name: 'GlobalStandard', capacity: capacityKtpm }` (30k TPM, `swedencentral`); live-eval cost in `tests/RunnersDiary.Ai.Tests/Evals/EvalRunner.cs` at Global prices 0.75 / 4.50 USD per 1M | User decision 2026-11-04. Read-only quota check: `OpenAI.DataZoneStandard.gpt-5.4-mini` = **0** in westeurope, swedencentral, polandcentral, germanywestcentral, francecentral; `OpenAI.GlobalStandard.gpt-5.4-mini` = 1 000k TPM, 700k used, **300k free** (30k needed). Data at rest stays in `swedencentral`; prompts may be processed in any Azure region, accepted because the model sees only the metrics-only fact sheet. Costs × ≈ 0.909 (§4). Data Zone stays preferred if EU quota is granted (one Bicep value + G1/G2); EU-quota alternatives for G6: gpt-6-luna (100), gpt-5-mini (300). ADR 0027 amend. "deployment type", 0026, 0028, 0030, 0034 amend. |

### 12.3 Review findings and resolutions

| Id | Finding | Resolution |
|---|---|---|
| **H1** | Anonymous `/api/health` ran the SQL check (cached 5 min), so any visitor or bot could wake the paused DB every 5 minutes and drain the free allowance | Live-only for anonymous callers outside Development; full report only with a valid token; rate policy `health`; SPA once per page load and tokenless; `smoke.sh` uses the token (D10) |
| **H2** | Geocode budget per replica and in memory: restarts and replicas multiply it; the Maps alert did not exist without display | Durable global `GeocodeUsage` budget (D8); unconditional Maps alert > 200/day (D9) |
| **M1** | First provision: apps on the placeholder image would fail port 8080 / `/alive` probes | Placeholder → port 80, no probes, until a real image runs (D11) |
| **M2** | `workflow_dispatch` could deploy any ref, including unmerged branches | The checked-out commit must be an ancestor of `origin/main` (rollbacks to older `main` SHAs still allowed) |
| **M3** | Rollback ran only on smoke failure, not on a half-applied update or a cancellation | Rollback `if: always()` once the update has started and the smoke step did not succeed |
| **M4** | Key bootstrap could rotate the key on a transient `az` error or a wrong subscription | `--subscription` everywhere, `az group exists`, fail closed on unexpected errors, refuse with a soft-deleted tagged vault, never print (D6) |
| **M5** | `listSas` with `principalId` of another UAMI needs Managed Identity Operator on it | Assigned in `maps.bicep` (G9 only), built-in role `f1a07417-…` (D12) |
| L1 nginx did not verify the internal API's TLS certificate | `proxy_ssl_verify on` + CA bundle + depth 3 on both proxy locations; checked: a valid certificate passes, `self-signed.badssl.com` gets 502, plain-http stub unaffected | Fixed |
| L2 budget start date hard-coded `2026-11-01` | `BUDGET_START_DATE` azd value set once by `preprovision.sh` (first of the current month), `budgetStartDate` param without default | Fixed |
| L3 log volume against the 0.15 GB/day cap | `Microsoft.EntityFrameworkCore.Database.Command` at Warning in `appsettings.json` (Information kept in Development); `Microsoft.AspNetCore` already Warning | Fixed |
| L4 silent 503 in `GetMapsToken.cs` | logs the exception type only (`LoggerMessage`) | Fixed |
| L5 MSAL silent renewal likely blocked by the CSP | `frame-src 'self' <entra host>`; `frame-ancestors 'self'` on `/redirect.html` only, `'none'` elsewhere; verify script asserts both; confirm live at G8 | Fixed (⚠ G8) |
| L6 job-mode startup failure printed `ex.Message` | prints `Job '…' failed to start (<Type>).`; API startup prints `Startup failed (<Type>)` (message only for the key-naming `InvalidOperationException`) | Fixed |
| Notes | ACR Basic has no retention policy (≈ 20 deploys per 10 GB; prune by hand occasionally, ADR 0030 amend. §1); the API image user is the name `app` (fine on ACA) | Accepted |

### 12.4 QA findings

- **Q1 – container startup hang (fixed).** A failing startup (e.g. invalid production config) inside the API container hung at about 99 % CPU instead of exiting. Fixed with the top-level catch (D16); covered by tests.
- **Q2 – the image's default CSP has no Azure hosts (by design).** Running the web image without Bicep's env gives OpenFreeMap only and `frame-src 'none'`; Azure Maps and Entra hosts are added per environment (`CSP_MAP_HOSTS`, `CSP_ENTRA_HOST`). Not a defect.

### 12.5 Open item found while documenting (for azure-infra, before G1)

- **AI token alert threshold mismatch — resolved (main agent, 2026-11-04).** `alert-rd-ai-tokens` had `threshold: 20000` while its description, comment and ADR 0034 §4 said > 400k tokens per 24 h. The Bicep threshold is now `400000`, matching the ADR: typical use stays below it, and the ceiling of about 203 calls × 11.5k tokens ≈ 2.3M/day triggers it early.

### 12.6 Still unverified until Part B (⚠)

| Item | Closed at |
|---|---|
| Role-assignment propagation on the first provision (pulls, Key Vault reference) | G2 |
| Deployer: built-in **Container Apps Contributor** sufficient for app/job update and start (reviewer believes `Microsoft.App/containerApps/*` covers write); **Container Apps Jobs Contributor** GUID `4e3d2b60-…` | G2/G4 |
| `CREATE USER … WITH SID … TYPE = E` and the UAMI admin `sid` (client id) on the first migrate run | G4 |
| go-sqlcmd managed-identity flags against Azure SQL | G4 |
| azd parameter defaults `${VAR=default}` and `SERVICE_API_RESOURCE_EXISTS` (fetch-image pattern) | G1 |
| Foundry endpoint property used for `Ai__Foundry__Endpoint`; `GlobalStandard` free quota for gpt-5.4-mini still ≥ 30k right before G1 (was `DataZoneStandard`, found to be 0; D-AI) | G0/G1 |
| Azure Maps Web SDK under the real CSP; narrow `listSas` role vs documented Contributor | G9 |
| OpenTelemetry `url.full` query-string redaction (geocode `query=` is personal data) | G5 (App Insights dependencies) |
| MSAL silent renewal under the CSP (`frame-src`, `frame-ancestors` on `/redirect.html`) | G8 |
| Budget `startDate` semantics (first of month, set later in the month) | G1/G2 |
| XFF behaviour live (AC9) | G5 |

### 12.7 Part B outcomes

*(Filled in per gate. Still to come: G4 first migrate run, live eval results (G6), first 48 h cost by meter.)*

**Gate status (2026-11-04):** G0 ✔, **G1 ✔, G2 ✔** (each with the user's explicit approval). **Next: G3.** The IaC changes made during G2 are on branch `m7-sql-region` (uncommitted at the time of writing).

**Subscription:** "Visual Studio Ultimate z subskrypcją MSDN" (Visual Studio subscriber monthly credit, spending limit on by default, so the cost risk is capped by the credit unless the limit is removed). Subscription id in the local azd environment (not recorded in this public repository). azd 1.37.2, azd env `prod`, `AZURE_LOCATION=westeurope`.

#### G1: what-if

- `azd provision --preview` succeeded. Its summary lists only the main resource types; the full `az deployment sub what-if` on the first run showed **46 Create, 0 Modify, 0 Delete**.
- **Finding G1-a:** azd validates required Bicep parameters **before** running the `preprovision` hook. `BUDGET_START_DATE` (set by the hook in the Part A design, D6/L2) therefore had to be set by hand (`azd env set BUDGET_START_DATE 2026-11-01`), and the hook (guest key) was run with `azd hooks run preprovision` before provisioning.
- **Finding G1-b:** for the per-resource detail, `az deployment sub what-if` is needed; azd's preview is a summary.
- **Finding G1-c:** once some resources exist (after the failed G2 runs), what-if reports `NestedDeploymentShortCircuited` and silently skips modules whose parameters reference outputs of existing resources. Known ARM what-if limitation, not a template error. A re-run G1 after a partial provision is therefore incomplete; review the module list it covers.
- From §12.6: the azd parameter defaults resolved (preview and provision both succeeded) and the first-provision path used the placeholder image as designed. The fetch-image path for an *existing* app image is exercised only by the first re-provision after G4 (G7 or G8) ⚠.

#### G2: provision (4 runs)

| Run | SQL location / name | Result |
|---|---|---|
| 1 | `westeurope` / `sql-rd-m3dcwctu5kpwi` | Everything up to SQL created, then **`RegionDoesNotAllowProvisioning`**: "Location 'West Europe' is not accepting creation of new Windows Azure SQL Database servers". azd also warned that the firewall rule name `AllowAllWindowsAzureIps` contains the reserved word "Windows". |
| 2 | `northeurope` / same name | **`InvalidResourceLocation`**: the failed westeurope create still held the name `sql-rd-m3dcwctu5kpwi` (no server exists; an ARM leftover). |
| 3 | `northeurope` / `sql-rd-m3dcwctu5kpwi-northeurope` | **`RegionDoesNotAllowProvisioning`** again. |
| — | read-only check | SQL capabilities API (`GET /subscriptions/{sub}/providers/Microsoft.Sql/locations/{region}/capabilities?api-version=2023-08-01`) for this subscription: `westeurope`, `northeurope`, `norwayeast` "Provisioning is restricted in this region" (support request needed); `uksouth` "Subscriptions are restricted"; `swedencentral`, `germanywestcentral`, `francecentral`, **`polandcentral`**, `italynorth`, `spaincentral`, `switzerlandnorth` "Available". |
| 4 | **`polandcentral`** / `sql-rd-m3dcwctu5kpwi-polandcentral` (user's choice) | **Succeeded in 3 min 22 s.** |

**Result:**
- **25 resources** in `rg-runnersdiary-prod` (`westeurope`).
- SQL server `sql-rd-m3dcwctu5kpwi-polandcentral`; database `runnersdiary`, GP_S_Gen5_2, free offer (`useFreeLimit`, `AutoPause`), status Online.
- ACR `acrrdm3dcwctu5kpwi`.
- Budget `budget-rd-prod` ($25); alerts `maps-usage`, `ai-tokens`, `api-5xx`, `sql-free`.
- Web FQDN `web.grayisland-d22ea91b.westeurope.azurecontainerapps.io`.
- Apps run the placeholder image until G4. AI off (`AI_ENABLED` false).
- Hooks: `azd hooks run preprovision` before, `azd hooks run postprovision` after (it cleared `GUEST_KEY_BOOTSTRAP` locally and printed the G3/G8 values).

**Values for G3** (non-secret identifiers, recorded on purpose; stored as GitHub *secrets* only per Learn guidance):
- Environment `prod` secrets: `AZURE_CLIENT_ID` (client id of `id-rd-deployer`), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`. The values are printed by `azd hooks run postprovision` and are kept out of this public repository.
- Environment `prod` variables: `AZURE_RESOURCE_GROUP` = `rg-runnersdiary-prod`, `ACR_NAME` = `acrrdm3dcwctu5kpwi`, `WEB_FQDN` = `web.grayisland-d22ea91b.westeurope.azurecontainerapps.io`.
- Repository variable: `DEPLOY_ENABLED` = `true`.

**Values for G8** (SPA redirect URIs, §5.6): `https://web.grayisland-d22ea91b.westeurope.azurecontainerapps.io/redirect.html` and `https://web.grayisland-d22ea91b.westeurope.azurecontainerapps.io/signin`.

#### G3/G4: first deploy attempts (2026-11-04)

- **Skipped runs:** `DEPLOY_ENABLED` had been created as a `prod` **environment** variable. The job-level `if: vars.DEPLOY_ENABLED == 'true'` is evaluated before the job enters its environment, so it only sees **repository** variables. Fix: `DEPLOY_ENABLED` must be a repository variable (the environment copy is removed).
- **Missing approval:** the `prod` environment had only a branch policy and no **Required reviewers** rule. Without it, the automatic run after green CI would deploy unattended. The user is added as required reviewer before deploys are enabled.
- **OIDC login failed:** `AADSTS700213: No matching federated identity record`. The repository uses GitHub's immutable OIDC subject (`use_immutable_subject: true`, the default for repositories since 2026), so the token subject is `repo:stompsu@12047598/runners-diary@1444016204:environment:prod`, while the federated credential expected `repo:stompsu/runners-diary:environment:prod`. Fix (user chose the immutable format): new Bicep params `githubOwnerId`/`githubRepositoryId` (public numeric ids); `github-oidc.bicep` builds the immutable subject when both are set. The credential `github-prod` is updated in place by the next `azd provision`.

#### G4: first deploy, run 1 (2026-11-04, approved by the user)

- **Passed:** OIDC login (immutable subject), build and push of the three `sha-31f64ce` images, and **the migration job**. That run proves the migrator identity, the Entra-only SQL login and `CREATE USER … WITH SID` with the client id. The app and job updates also passed.
- **Smoke test failed:** `GET / has no Content-Security-Policy header`. Cause: provisioning on the placeholder image sets the ingress `targetPort` to 80 with no probes (review M1), and `az containerapp update --image` changes only the image. Our images listen on 8080, so ingress answered with a Container Apps error page.
- **Rollback worked:** all three apps were restored to the placeholder. The migration stays applied (forward-only, harmless).
- **Fix:** `deploy.yml` now runs `az containerapp ingress update --target-port 8080` for `api` and `web` after the image update. The rollback sets the port back to 80 for the placeholder, or 8080 for one of our images. The first `azd provision` after a successful deploy sees a real image and restores the startup/liveness/readiness probes.

#### G4: first deploy, run 2 (2026-11-05, approved by the user)

- Everything up to the app update passed again, including the migration job and the new `ingress update --target-port 8080`.
- **Smoke test failed:** `GET / has no Content-Security-Policy header`. The system log shows a race:
  - the image was swapped while ingress was still on 80, so the default startup probe on 80 failed and the replica restarted;
  - the port moved to 8080 at 10:05:32, and the smoke test ran in the same second;
  - Container Apps kept **100 % of the traffic on the old placeholder revision** (`web--0000002`) because the new one wasn't ready, so the smoke test got the placeholder's 200 without our CSP. nginx logged no request.
- **The rollback worked again.**
- **Fix:**
  - `deploy.yml` sets the ingress port **before** swapping the image;
  - it waits until `latestReadyRevisionName == latestRevisionName` for `api` and `web` (up to 5 minutes);
  - `smoke.sh` retries the first `GET /` until it gets a 200 with our CSP (`SMOKE_READY_TIMEOUT_SECONDS`, default 300), which also covers scale-from-zero.
  - After the first successful deploy, one `azd provision` writes port 8080 with our probes permanently.

#### G4: first deploy, run 3 (2026-11-05, approved by the user)

- **The readiness wait worked:** `api--0000005` and `web--0000005` were ready before the smoke test.
- **Smoke checks that passed:**
  - `GET /` returned 200 **with our CSP** (nginx live);
  - `/redirect.html` had no COOP and was `no-store`;
  - `/api/client-config` returned 200;
  - **`POST /api/guest-sessions` returned 201**, a SQL write through the API's managed identity;
  - anonymous `/api/health` returned 200.
- **Failed:** the authenticated `/api/health` (full report) stayed at 503 for 150 s.
  - Cause: Aspire's built-in Blob health check calls the account-level service properties, and the API identity holds Storage Blob Data Contributor **on the `gpx` container only** (ADR 0031), so the check got `403 AuthorizationPermissionMismatch`.
  - Locally and in CI, Azurite uses the account key, with no RBAC, so tests couldn't see it.
- **The rollback worked.**
- **Fix (least privilege kept):** the Aspire blob health check is disabled, and a container-scoped `BlobContainerHealthCheck` ("blobs") lists at most one blob in `gpx`. Tests cover the registration, healthy and unhealthy cases.

#### G4: first deploy, run 4 — success (2026-11-05, approved by the user)

- All steps passed and the rollback was skipped.
  - The readiness wait brought `api--0000007` and `web--0000007` up in about 1.5 min.
  - Smoke 9/9: `GET /` with CSP; `/redirect.html` without COOP and with no-store; `/api/client-config`; guest creation 201; anonymous and **authenticated** `/api/health` (SQL plus the new container-scoped blob check); `/api/me` (Europe/Warsaw); the calendar with demo runs; `/api/maps/token` 503 (display off).
- Verified from outside: title "Runners Diary", CSP/COOP/HSTS/nosniff/no-store headers, `/api/health` Healthy, both apps on `sha-95d7822` port 8080.
- The four runs gave three real fixes (#16 ingress port, #18 readiness wait plus smoke retry, #19 container-scoped blob health check), and a date-dependent full-stack test fixed in #17.
- Proven in Azure by G4:
  - the migrator identity with Entra-only SQL and `CREATE USER … WITH SID` (client id) via go-sqlcmd;
  - the API's managed identity to SQL, Blob, Key Vault (guest key) and Foundry (AI off);
  - the deployer's roles (ACR push, Container Apps and jobs update, ingress update);
  - role propagation, which gave no errors.

#### Probes provision (2026-11-05, approved by the user)

- `azd provision` first answered "no changes" (its template and parameter hash was unchanged), so the run used `azd provision --no-prompt --no-state`, with the hooks around it.
- The Bicep saw the real images and wrote port 8080 with our probes: `api` startup, liveness and readiness on `/alive`; `web` liveness and readiness on `/nginx-alive`.
- The deployed images and the guest key were kept (`api--0000008`, `web--0000008` ready).

#### G5: go-live checks (2026-11-05)

- **Per-IP guest limit:** from the user's network, 5 guest creations got 201 and the 6th got 429 (`GuestCreate` 5/h per IP). A second attempt from the same machine was correctly refused. From a **different network (the user's work machine)** it succeeded at the same time, so the client IP survives the ACA edge → nginx (rightmost XFF) → internal ingress → API (`ForwardLimit` 2), and the limit is per visitor.
- **Screens, checked in a real browser under the production CSP:**
  - calendar (November, demo runs, week totals, planned runs);
  - run details;
  - recorded track map (`ustron-trail-16k.gpx` on OpenFreeMap: 62 vertices, elevation profile, splits, privacy note, download);
  - Insights with the "Example summary for the demo data" (August spike, ACWR 1.48 High);
  - "API healthy" badge;
  - no console errors.
- **Logs:** no errors or exceptions in `api`/`web` console logs since the probes provision.
- **App Insights:** every request type succeeds except the intended 429. The calendar's p95 is about 0.4 s, including the westeurope → polandcentral SQL hop (H-4: acceptable).
- **Observation (H-6):** guest creation reaches about 11 s at p95 when the auto-paused free SQL database resumes while the demo data (~120 runs + GPX) is written; it is about 1–2 s once SQL is awake. Acceptable for the demo; revisit if real users find the first request slow (options: a lighter demo seed, a warm-up on `web` start, or a non-free SQL tier).

#### Custom domain `runners-diary.highpace.dev` (2026-11-05, applied with the user's OK)

- **DNS (set by the user, checked):** the CNAME `runners-diary.highpace.dev` points to `web.grayisland-d22ea91b.westeurope.azurecontainerapps.io`, and the TXT `asuid.runners-diary.highpace.dev` matches the environment's `customDomainVerificationId`.
- **IaC:**
  - New parameter `webCustomDomain` (`CUSTOM_DOMAIN`; empty = none).
  - `app-web.bicep` (now `containerApps@2026-07-01`) adds `customDomains: [{ name, bindingType: 'Auto' }]`.
  - A `managedEnvironments/managedCertificates` resource (`domainControlValidation: 'CNAME'`, free DigiCert certificate, renewed automatically) is created after the binding.
  - With `Auto`, Container Apps attaches the certificate once it is issued (HTTP only until then), so a single provision is enough and re-provisioning keeps the binding.
  - **Don't add the domain in the portal:** the next `azd provision` would remove it, because the ingress is defined in Bicep.
- **To apply:** `azd env set CUSTOM_DOMAIN runners-diary.highpace.dev`, then the usual hooks + `azd provision`. Allow a few minutes for issuance.
- **Requirements (Learn):**
  - the CNAME must point straight at the app FQDN (no proxy such as Cloudflare in between);
  - any CAA record on `highpace.dev` must allow `digicert.com`;
  - `web` must be running during issuance and renewals (it scales from zero on the validation request).
- **G8:** register the redirect URIs on the custom domain, `https://runners-diary.highpace.dev/redirect.html` and `/signin`, and optionally also the default FQDN. The CSP needs no change (`'self'`).

- **Applied:** `azd env set CUSTOM_DOMAIN runners-diary.highpace.dev`, then hooks + `azd provision` (7 min 35 s, including certificate issuance).
  - The managed certificate `mc-runners-diary-highpace-dev` was issued by DigiCert (GeoTrust TLS RSA CA G1), valid until 2027-05-05 and auto-renewed.
  - It is bound with `Auto`.
  - `https://runners-diary.highpace.dev` returns 200 with our CSP, and `/api/health` reports Healthy.

#### G6: live AI eval (2026-11-05 and 06)

- **Runs 1 and 2 (prompt v1, fact sheet v1, gpt-5.4-mini Global Standard, effort low):** failed with 2/6 and then 3/6 cases at 3/3, about $0.11 per run.
  - **taper-before-race 0/3 in both runs:** the pre-race taper was listed as a watch-out.
  - **insufficient-history 0/3:** "not enough history" was put in watch-outs, or the answer was rejected by the validator.
  - **partial-period:** 2/3 and 1/3.
  - **load-spike:** 0/3, then 3/3, showing high run-to-run variance.
- **Tuning (#22, user-approved, $2 cap):**
  - The live eval now shows per-attempt rule codes and drafts.
  - Diagnosis: a missing fact-sheet signal for tapers, plus unclear instructions; number words and stray braces caused the validator rejections. The validator was **not** stricter than ADR 0026 intends.
  - Fix: fact sheet v2 (`week.N.taper`, `week.N.loadSpike`) and prompt v2 (v1 kept).
- **Final full live run:** **6/6 cases at 3/3**, 16/18 valid on the first attempt. All live calls cost **about $0.40** in total, and the model is unchanged.

#### G7: AI on in production (2026-11-06)

- PR #22 was deployed first (smoke 9/9), so production runs prompt v2.
- Then `AI_ENABLED=true` with hooks + `azd provision` (2 min 14 s): `Ai__Enabled=true`, and `client-config.ai.summaryGeneration=true`.
- **Live check on the custom domain:** a guest generated a summary for October 2026.
  - HTTP 200 in about 6 s, on the first attempt;
  - `source: model` (`gpt-5.4-mini-2026-03-17`), prompt v2, fact sheet v2;
  - the narrative cited the trail race, the pre-race taper (under went well) and the pace trend; all values came from placeholders;
  - the guest quota showed 1/3 used.
- **Cost at this point:** about $0.006 per summary. The worst-case ceiling is about $2.89–5.79/day under the quotas; the token alert (> 400k/24 h) and the $25 RG budget are active.

#### Foundry project (2026-11-06, prepared; provision pending the user's OK)

- The new Foundry portal didn't list `ais-rd-m3dcwctu5kpwi` (no project management, no project), while the user's portal-created resources had both.
- `foundry.bicep` now enables `allowProjectManagement` (with a system-assigned identity) and adds the project `runnersdiary`, for portal visibility only (ADR 0027 amendment). The app is unaffected.

#### Part B deviations

| # | Plan said | Done | Why / where recorded |
|---|---|---|---|
| B1 | Primary region `swedencentral` (Q2 default; ADR 0030 §3) | **`westeurope`** | User decision before G1; co-located with Maps; gpt-5.4-mini Global Standard offered there. ADR 0030 amend. "Region" |
| B2 | SQL in the primary region | **`polandcentral`** via the new param `sqlLocation` (`SQL_LOCATION`, empty = primary region) | New SQL servers restricted for this subscription in `westeurope`/`northeurope`/`norwayeast` (capabilities API). Cross-region app ↔ DB; data stays in the EU (§2). ADR 0030 amend. "Region" |
| B3 | SQL server name `sql-rd-<token>` | `sql-rd-<token>-<sqlLocation>` when `SQL_LOCATION` is set (Bicep `nameSuffix`) | A failed create holds the plain name in the other region (`InvalidResourceLocation`). ADR 0030 amend. "Region" |
| B4 | Firewall rule `AllowAllWindowsAzureIps` | **`AllowAzureServices`** (still 0.0.0.0 = Azure services) | azd warned about the reserved word "Windows". ADR 0031 amend. "G2" |
| B5 | Hooks run by `azd provision`; `preprovision` sets `BUDGET_START_DATE` (D6, L2) | Hooks run by hand (`azd hooks run preprovision` / `postprovision`); `BUDGET_START_DATE` set by hand | Findings G1-a and H-1 below. §10 step 5; ADR 0030 amend. "Region", 0031 amend. "G2" |
| B6 | azd env `rd-prod` (ADR 0030 §2, §5.2) | azd env **`prod`** | Matches the resource names (`rg-runnersdiary-prod`, `budget-rd-prod`). Docs updated in §5.2. |

**Open items from Part B so far (⚠):**
- **H-7 (G7):** the prompt's pace example produces "from {pace} to {pace} per km", and the UI's formatted pace already ends in "/km", so the page can read "/km per km". Fix: a wording tweak in a prompt v3, plus re-running the affected live case.
- **H-6 (G5):** slow first guest creation after SQL auto-resume (~11 s p95); see the G5 log.
- **H-1: why azd 1.37.2 skipped the project `preprovision`/`postprovision` hooks** on `azd provision --no-prompt` in this setup. To investigate (azure-infra): the hook declaration in `azure.yaml` (project-level `hooks:` block, `posix` shell/`run` paths, `continueOnError`), and whether `--no-prompt` or the azd version changes hook behaviour. Until resolved, the manual sequence in §10 step 5 is **the required procedure**, and a forgotten `postprovision` leaves `GUEST_KEY_BOOTSTRAP` in `.azure/prod/.env`.
- **H-2: `BUDGET_START_DATE` as a hook-set value does not work on a first provision** (G1-a). azure-infra should either give `budgetStartDate` a non-required path or document the manual `azd env set` as the only path (already in §10).
- ~~**H-3: stale Bicep description**~~ fixed in #14. **H-3 (old):** — the `sqlLocation` `@description` in `infra/main.bicep` says "prod uses northeurope"; it is `polandcentral`. One-line fix for azure-infra (no infra change; a later what-if will show no diff).
- **H-4 (checked at G5, acceptable):** latency of the cross-region SQL hop and the inter-region transfer price: check at G5 (App Insights dependency durations) and in the 48 h cost-by-meter view.
- **H-5:** `westeurope`/`polandcentral` retail prices for the §4 table (⚠ not re-checked).
- Resolved at G4: role-assignment propagation, deployer roles, `CREATE USER … WITH SID` and go-sqlcmd flags. Still open from §12.6: budget `startDate` semantics (first notification in November).

## Assumptions

1. The user's subscription is in a workforce tenant they own. The Entra **external** tenant (for app users) is separate and does not hold the Azure resources.
2. The SQL free offer is eligible for the subscription and still unused in it. (Confirmed at G2: the database was created with `useFreeLimit`.)
3. One maintainer. The GitHub repo `stompsu/runners-diary` keeps `main` as the only deploy branch.
4. USD retail prices apply; billing in EUR/PLN differs by the exchange rate. Azure credits, if any, offset but do not change these numbers.
5. The deploy workflow may build images on GitHub-hosted runners (Docker available), so no ACR Tasks are needed.
6. Traffic stays at hobby scale (≤ a few hundred requests a day). The worst-case column is about abuse, not growth.

## Docs checked (2026-11-04)

- Container Apps:
  - ingress (240 s timeout, X-Forwarded-For append, internal ingress, app-name calls; updated 2026-10-22): https://learn.microsoft.com/azure/container-apps/ingress-overview
  - billing (free grant; management fee only for Dedicated, private endpoints and planned maintenance; updated 2026-03-25): https://learn.microsoft.com/azure/container-apps/billing
  - jobs (updated 2026-09-16): https://learn.microsoft.com/azure/container-apps/jobs
  - secrets and Key Vault references (updated 2026-09-11): https://learn.microsoft.com/azure/container-apps/manage-secrets
- Aspire:
  - ACA deployment: https://aspire.dev/deployment/azure/container-apps/
  - JavaScript `PublishAsStaticWebsite` (Aspire 13.6, experimental): https://aspire.dev/deployment/javascript-apps/
- azd:
  - `azure.yaml` schema: https://learn.microsoft.com/azure/developer/azure-developer-cli/azd-schema
  - remote builds: https://learn.microsoft.com/azure/developer/azure-developer-cli/remote-builds
- GitHub OIDC with a UAMI (updated 2026-11-04): https://learn.microsoft.com/azure/developer/github/connect-from-azure-openid-connect
- Azure SQL free offer (15-min fixed auto-pause, `AutoPause`/`BillOverUsage`, metric; updated 2026-11-02): https://learn.microsoft.com/azure/azure-sql/database/free-tier
- SQL managed identities and `CREATE USER … WITH SID … TYPE = E` (ja-jp edition): https://learn.microsoft.com/azure/azure-sql/database/authentication-azure-ad-user-assigned-managed-identity · `Microsoft.Sql/servers` template: https://learn.microsoft.com/azure/templates/microsoft.sql/servers
- Azure Maps:
  - authentication (`disableLocalAuth` disables SAS; SAS, CORS; updated 2026-09-15): https://learn.microsoft.com/azure/azure-maps/azure-maps-authentication
  - transactions (15 tiles = 1; free 5 K; updated 2026-09-15): https://learn.microsoft.com/azure/azure-maps/understanding-azure-maps-transactions
  - geographic scope: https://learn.microsoft.com/azure/azure-maps/geographic-scope
- Foundry deployment types (`DataZoneStandard`, `GlobalStandard` "may be processed in any Azure region", data at rest in the geography, pay-per-token, EU Data Boundary; updated 2026-08-12, re-read 2026-11-04 for D-AI) · region availability (gpt-5.4-mini Global Standard in all European regions incl. `swedencentral`; updated 2026-10-19): https://learn.microsoft.com/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability · https://learn.microsoft.com/azure/foundry/foundry-models/concepts/deployment-types · deployments template: https://learn.microsoft.com/azure/templates/microsoft.cognitiveservices/accounts/deployments
- Log Analytics daily cap (updated 2026-01-09): https://learn.microsoft.com/azure/azure-monitor/logs/daily-cap · budgets template: https://learn.microsoft.com/azure/templates/microsoft.consumption/budgets
- Entra External ID pricing (core free < 50 K MAU): https://learn.microsoft.com/entra/external-id/external-identities-pricing
- NuGet `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0: https://www.nuget.org/packages/Azure.Monitor.OpenTelemetry.AspNetCore
- Azure SQL location capabilities (per-subscription provisioning status), REST `Capabilities - List By Location` (top-level `status`, plus `reason` when restricted; the page documents api-version `2025-01-01`, the call at G2 used `2023-08-01`, both work), called read-only at G2 (2026-11-04): https://learn.microsoft.com/rest/api/sql/capabilities/list-by-location
- Azure retail prices API (2026-11-04): Container Apps, Container Registry, Key Vault, Log Analytics and Storage in `swedencentral`; Azure Maps in `westeurope`. `https://prices.azure.com/api/retail/prices?$filter=serviceName eq '<service>' and armRegionName eq '<region>'`

**Checked against Microsoft Learn (MCP server, 2026-11-04, main agent):**
- **Foundry capacity:** a `capacity` of 1 = 1,000 TPM ([automate quota deployments](https://learn.microsoft.com/azure/foundry/openai/how-to/automate-quota-deployments)), so `AI_CAPACITY_KTPM` = 30 → 30k TPM. Resolved.
- **New risk, Data Zone quota for gpt-5.4-mini:** the [quota tier tables](https://learn.microsoft.com/azure/foundry/openai/quotas-limits) list `gpt-5.4-mini` only as `GlobalStandard`, while `gpt-5.4-nano` and other models have `DataZoneStandard` rows. The model is in the Data Zone Standard EU *region* table (ADR 0027), but subscription quota may be missing or need a request. The read-only quota check at G0 (`az cognitiveservices usage list -l swedencentral` and the model capacities API) must confirm `OpenAI.DataZoneStandard.gpt-5.4-mini` > 0 before G1. If it is 0, the options are a quota request, `GlobalStandard` (EU data at rest, processing global; the user decides), or `gpt-5.4-nano` `DataZoneStandard` after the live eval.
  - **Result (2026-11-04, read-only check on the user's subscription):** `OpenAI.DataZoneStandard.gpt-5.4-mini` limit **0** in westeurope, swedencentral, polandcentral, germanywestcentral and francecentral (other models do have EU Data Zone quota, e.g. gpt-6-luna 100, gpt-5-mini 300, gpt-5.4 100). `OpenAI.GlobalStandard.gpt-5.4-mini` limit 1 000k TPM, 700k used by the user's other deployments, **300k free**. **Decision (user): `GlobalStandard`** (§12 D-AI, ADR 0027 amendment "deployment type").
- **SQL identities:** `CREATE USER … WITH SID = …, TYPE = E` is documented in the English [CREATE USER reference](https://learn.microsoft.com/sql/t-sql/statements/create-user-transact-sql) for Azure SQL Database. `TYPE = E` covers users and service principals/managed identities, and for a service principal or managed identity the SID is the **application/client id**. A service principal issuing `CREATE USER … FROM EXTERNAL PROVIDER` would need Directory Readers on the server identity, so the `WITH SID` form is the right choice. The UAMI **admin `sid` is likewise the client id** ([set Entra admin](https://learn.microsoft.com/azure/azure-sql/database/authentication-aad-configure): "for applications (service principals), the Application ID is displayed"). ADR 0031 is corrected; ADR 0032 already used the client id.
- **Container Apps management meter:** [billing](https://learn.microsoft.com/azure/container-apps/billing) says the Dedicated Plan Management charge applies to features such as private endpoints and planned maintenance, regardless of plan. This plan uses neither, so it is $0. Open question 14 is resolved; the 48 h cost-by-meter check stays as a safety net.
- **Azure Maps SAS:** [authentication](https://learn.microsoft.com/azure/azure-maps/azure-maps-authentication) states that creating SAS tokens needs **Contributor** on the Maps account and the user-assigned identities. A narrower custom `listSas` role is still unconfirmed and only matters for the optional G9. Rates above a token's `maxRatePerSecond` are not billed (they get 429), and limits are enforced per Azure location.

**Not verified (marked ⚠ above):**
- the narrow `listSas` custom role;
- the exact Foundry token metric names;
- whether OpenFreeMap serves all `liberty` style assets from one host;
- the metric-alert price;
- the email-OTP add-on price;
- the PITR retention on the free offer;
- go-sqlcmd managed-identity flags;
- the list of Azure Maps account regions.
