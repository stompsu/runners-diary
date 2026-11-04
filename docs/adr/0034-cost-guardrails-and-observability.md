# 0034 – Cost guardrails and observability in Azure

**Status:** Accepted (M7 plan approved 2026-11-04; amended 2026-11-04 after Part A implementation, and 2026-11-04 for the AI deployment type, see the end)
**Date:** 2026-11-04

## Context

- The brief requires that the app be cost-conscious, with consumption-based, free-tier and scale-to-zero choices, and asks for App Insights + Log Analytics via OpenTelemetry.
- The AI spend ceiling is bounded by durable quotas at about **$3.18–6.37 a day** (ADR 0028 amendment), and the deployment needs at least 30k TPM (0027 amendment). (→ **$2.89–5.79 a day** at Global Standard prices since 2026-11-04, see the amendment "AI deployment type" at the end.)
- ServiceDefaults already emits OpenTelemetry (OTLP to the Aspire dashboard locally). It contains a commented-out `UseAzureMonitor()`. The meter and activity source `RunnersDiary.Ai` exist, and no prompt or answer content is recorded.

Verified on 2026-11-04:
- **Log Analytics:** the first 5 GB of Analytics ingestion per month are free (billing account), then $2.99 per GB in `swedencentral` (retail API). A daily cap (`workspaceCapping.dailyQuotaGb`) stops collection after the threshold; some overshoot is billed. For workspace-based App Insights the effective cap is the minimum of the two caps ([daily cap](https://learn.microsoft.com/azure/azure-monitor/logs/daily-cap)).
- **SQL free offer:** exposes a "free amount remaining" metric; Learn suggests a **free** alert below 10,000 vCore-s ([free tier](https://learn.microsoft.com/azure/azure-sql/database/free-tier)).
- **Budgets:** `Microsoft.Consumption/budgets` (RG scope) supports `Actual` and `Forecasted` notifications with `contactEmails` ([template ref](https://learn.microsoft.com/azure/templates/microsoft.consumption/budgets)). Budgets are monthly and do not stop spend.
- **NuGet `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0** (2026-07-27) depends on OpenTelemetry.Extensions.Hosting ≥ 1.15.3. The repo pins 1.20.1, which is compatible.

## Decision

1. **Telemetry:** add `Azure.Monitor.OpenTelemetry.AspNetCore` to ServiceDefaults. Justification: the Microsoft-supported exporter for App Insights. It also adds live metrics; no extra collector is needed.
   - `UseAzureMonitor()` is called only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set. Locally nothing changes (OTLP → dashboard).
   - **Sampling:** rate-limited sampling at 5 traces per second, so a request flood cannot blow the cap.
   - **Levels:** logs at `Warning` and above for `Microsoft.*` and `System.*`, `Information` for `RunnersDiary.*`.
   - No request bodies and no query strings with personal data (the existing logging already has neither).
   - SQL text is captured only as parameterized command text: the default of the SqlClient instrumentation, which the reviewer re-checks.
2. **Workspace:** one Log Analytics workspace (PerGB2018), retention **30 days**, **daily cap 0.15 GB**, which keeps a full-cap month around 4.6 GB, under the 5 GB free allowance. App Insights is workspace-based, with no separate cap needed. The ACA environment sends console and system logs to the same workspace.
3. **Budget:** an RG-scope monthly budget of **$25**, with notifications:
   - Actual 40 % ($10, about twice the idle cost: an early anomaly signal);
   - Actual 80 %;
   - Actual 100 %;
   - Forecasted 100 %.

   The contact email is an azd env value (`BUDGET_CONTACT_EMAIL`), never committed.
4. **Metric alerts** (one action group, e-mail only, free):

   | Alert | Signal | Threshold |
   |---|---|---|
   | AI spend per day | Foundry account token metrics, input + output over 24 h (⚠ exact metric names verified at implementation) | > 400k tokens per 24 h (≈ $1–2, between the typical and the ceiling) (→ ≈ $0.5, corrected in the amendment "AI deployment type") |
   | SQL free allowance | `free_amount_remaining` (database) | < 10,000 vCore-s |
   | API failures | App Insights `requests/failed` | > 20 in 15 min |
   | Maps usage *(only with display enabled)* | Maps account `Usage` | > 4,000 per 24 h |
   | Log cap reached | not created (a log alert costs money); the budget and the portal banner cover it | — |

5. **Spend levers that need no redeploy:**
   - `Ai__Enabled=false` (kill switch, IOptionsMonitor, ADR 0027);
   - `Maps__Provider=MapLibre`;
   - `api` `maxReplicas`.

   These are env or scale changes, so each is a new revision but no new image.
6. **Dashboards:** none custom in M7. Use the App Insights built-ins (Application map, Failures, Performance, Live metrics) and the saved KQL queries in the runbook (M7 plan §9): AI generations by outcome, guest creations per hour, 429s by policy, cold-start duration.
7. **Defender for Cloud and other paid plans stay off.** The reviewer checks after provisioning that no paid plan was auto-enabled.

## Consequences

- Idle cost is dominated by ACR Basic (about $5 a month). Everything else is free-tier or usage-based (plan §4).
- Monthly budgets only notify. The real bounds come from the in-app quotas (AI), the per-replica geocode budget, the SQL `AutoPause` behaviour (an outage instead of a bill), the LA cap and the replica caps. Container Apps request billing and replica-hours have **no hard cap**; a sustained flood costs at most about $113 a month at 2 replicas each, plus requests.
- When the LA cap is hit, telemetry stops until the daily reset. This is accepted as the price of a bounded bill.

## Alternatives considered

- **ACA managed OpenTelemetry agent (no package):** fewer dependencies, but it only forwards traces and logs (no metrics, so no AI token meter in App Insights), and it is configured per environment. Rejected.
- **Classic App Insights with its own cap:** classic resources are retired. Rejected.
- **Budget actions that stop resources (automation runbook):** more moving parts and identities. Deferred; the levers above are a minute away.
- **A daily budget:** budgets have no daily grain. The daily AI alert fills that gap.

## Amendment (2026-11-04, M7 Part A implemented)

1. **Exporter (§1), as built.** `Azure.Monitor.OpenTelemetry.AspNetCore` **1.6.0** in ServiceDefaults; `UseAzureMonitor()` is registered **only** when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set (a test asserts it is absent otherwise); rate-limited sampling **5 traces/s**. `Microsoft.EntityFrameworkCore.Database.Command` logs at `Warning` in production (no SQL text in Information logs).
2. **Production configuration validator.** In Production the API (and job mode) checks the required Azure settings at startup and fails with the **key names only, never values**. A startup failure logs `Startup failed (<ExceptionType>)` and exits 1 ([ADR 0032 amendment](0032-migrations-and-jobs-in-azure.md) §6).
3. **Health endpoint and SQL wakes (new; review H1).** `GET /api/health` used to run the full report (including the SQL check) for anonymous callers, cached 5 min, so any anonymous visitor or bot could wake the paused database every 5 minutes. Now, **outside Development**:
   - **anonymous** (no token, or an invalid one): only the checks tagged `live` (no SQL, no Blob), uncached;
   - **with a valid bearer token** (user or guest): the full report including SQL, cached 5 min per replica;
   - both forms pass the new rate policy **`health`**: 30 per minute per caller (user id, else client IP; `RateLimiting:Health`);
   - `/alive` (probes) is unchanged and never touches SQL;
   - the SPA's `ApiStatus` fetches once per page load (no polling, no refetch on focus), only after a session exists, and **without a token** (`/api/health` is in the anonymous list of `web/src/api/client.ts`), so in Azure it never triggers the SQL check; `smoke.sh` uses the authenticated form so a deploy still proves the SQL path.
4. **Metric alerts (§4) as built** (`infra/modules/alerts-budget.bicep`, `metricAlerts@2026-01-01`). The ⚠ on metric names is resolved:

   | Alert | Metric (namespace) | Aggregation, window | Threshold |
   |---|---|---|---|
   | `alert-rd-ai-tokens` | `TokenTransaction` (`Microsoft.CognitiveServices/accounts`) | Total, 24 h, evaluated hourly | ⚠ **see note** |
   | `alert-rd-sql-free` | `free_amount_remaining` (`Microsoft.Sql/servers/databases`) | Minimum, 6 h | < 10,000 vCore-s |
   | `alert-rd-api-5xx` | `requests/failed` (App Insights) | Count, 15 min | > 20 |
   | `alert-rd-maps-usage` | `Usage` (`Microsoft.Maps/accounts`) | Count, 24 h | > 200 |

   - The Maps alert is now **unconditional** (geocoding always uses the account) and sized for the 75/day geocode budget ([ADR 0033 amendment](0033-production-maps-and-azure-maps.md) §6), instead of "> 4,000, only with display".
   - **Note (resolved 2026-11-04):** the Bicep `threshold` was 20,000 while the description, the comment and this ADR said > 400k tokens per 24 h. It is now `400000`, matching this ADR.
5. **Budget (§3).** `Microsoft.Consumption/budgets@2026-06-01`; `startDate` = `BUDGET_START_DATE`, set once by `preprovision.sh` to the first day of the month of the first provision. ⚠ The semantics of a first-of-month start set later in that month are confirmed at G1/G2.
6. **API versions** satisfy the `use-recent-api-versions` linter rule (730 days), see [ADR 0030 amendment](0030-azure-topology-iac-and-deployment.md) §2.

**Still unverified (⚠):** whether the `url.full` attribute that the HTTP client instrumentation exports redacts query strings (the geocoder's `query=` is personal data; check App Insights dependencies at G5); the canary search for prompt text (AC11).

## Amendment (2026-11-04, AI deployment type)

The Foundry deployment is **Global Standard** ([ADR 0027 amendment "deployment type"](0027-ai-model-provider-foundry-and-local-dev.md#amended-2026-11-04-deployment-type)); gpt-5.4-mini costs $0.75 in / $4.50 out per 1M tokens (was $0.825 / $4.95). Global Standard prices since 2026-11-04:
- **AI ceiling (Context):** ≈ **$2.89–5.79 a day** (was $3.18–6.37), i.e. ≈ $88–176 a month at 30.4 days (M7 plan §4), from 203–406 calls × ≈ $0.0143 ([ADR 0028 amendment "deployment type"](0028-summary-cache-quotas-and-demo-summaries.md)).
- **AI token alert stays at > 400k tokens per 24 h** (`alert-rd-ai-tokens`, `TokenTransaction`); it is token-based, so the price change does not move it. Its dollar annotation in §4 ("≈ $1–2") did not follow from its own inputs: at the cap's token mix (≈ 10k in / 1.5k out per call) 400k tokens are ≈ 35 calls ≈ **$0.5** (≈ $0.55 at Data Zone prices). It still sits between typical use and the ceiling (≈ 203 × 11.5k ≈ 2.3M tokens/day).
- The $25 RG budget and its thresholds are unchanged.

## References (checked 2026-11-04)

- Log Analytics daily cap and App Insights interaction: https://learn.microsoft.com/azure/azure-monitor/logs/daily-cap
- Retail prices API, Log Analytics `swedencentral` (5 GB free, $2.99/GB ingestion, $0.13/GB-month retention beyond the included period): `https://prices.azure.com/api/retail/prices?$filter=serviceName eq 'Log Analytics' and armRegionName eq 'swedencentral'`
- Azure SQL free offer (metric, alert suggestion): https://learn.microsoft.com/azure/azure-sql/database/free-tier
- Budgets Bicep (`Microsoft.Consumption/budgets@2024-08-01`): https://learn.microsoft.com/azure/templates/microsoft.consumption/budgets
- NuGet `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0: https://www.nuget.org/packages/Azure.Monitor.OpenTelemetry.AspNetCore
- ADRs 0027, 0028 (amendments: TPM and the cost ceiling).
