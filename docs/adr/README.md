# Architecture Decision Records

One file per significant decision: `NNNN-kebab-title.md` with sections **Status**, **Context**, **Decision**, **Consequences**, **Alternatives considered**, **References**.
Accepted ADRs are never rewritten — supersede them with a new ADR. Owned by the `architect` agent.

## Index

Statuses (amendments, supersessions) are kept current in the index of [`architecture.md`](../architecture.md#adr-index).

| # | Title | Milestone |
|---|---|---|
| 0001 | [Solution structure and .NET Aspire usage](0001-solution-structure-and-aspire.md) | M1 |
| 0002 | [OpenAPI document and generated TypeScript client](0002-openapi-typescript-client.md) | M1 |
| 0003 | [Frontend stack and UI kit](0003-frontend-stack-and-ui-kit.md) | M1 |
| 0004 | [Hosting the React SPA in Azure](0004-web-hosting.md) (superseded by 0029) | M1 |
| 0005 | [CI pipeline shape](0005-ci-pipeline.md) | M1 |
| 0006 | [Azure SQL Database (serverless, free offer)](0006-database-azure-sql.md) | M1 |
| 0007 | [API surface conventions](0007-api-surface-conventions.md) | M1 |
| 0008 | [Run date & time storage](0008-run-date-and-time-storage.md) | M2 |
| 0009 | [Authentication: Entra External ID + guest JWT](0009-authentication-entra-and-guest-jwt.md) | M2 |
| 0010 | [Current user and per-user data scoping](0010-current-user-and-data-scoping.md) | M2 |
| 0011 | [Guest accounts lifecycle](0011-guest-accounts-lifecycle.md) | M2 |
| 0012 | [Request validation](0012-request-validation.md) | M2 |
| 0013 | [Persistence model and migrations](0013-persistence-model-and-migrations.md) | M2 |
| 0014 | [SPA authentication and token handling](0014-spa-authentication-and-token-handling.md) | M3 |
| 0015 | [SPA runtime configuration via `/api/client-config`](0015-spa-runtime-configuration.md) | M3 |
| 0016 | [Calendar grid and URL state](0016-calendar-grid-and-url-state.md) | M3 |
| 0017 | [Client-side dates without a date library](0017-client-dates-without-a-date-library.md) | M3 |
| 0018 | [Run forms and server errors](0018-run-forms-and-server-errors.md) | M3 |
| 0019 | [Frontend test strategy](0019-frontend-test-strategy.md) | M3 |
| 0020 | [GPX upload and safe parsing](0020-gpx-upload-and-safe-parsing.md) | M4 |
| 0021 | [Track metrics, simplification and GeoJSON](0021-track-metrics-simplification-and-geojson.md) | M4 |
| 0022 | [Map provider, geocoding and Azure Maps tokens](0022-map-provider-geocoding-and-azure-maps-tokens.md) | M4 |
| 0023 | [Run ↔ track, GPX prefill and blob deletion](0023-run-tracks-prefill-and-deletion.md) | M4 |
| 0024 | [Training metrics v1](0024-training-metrics-definitions-v1.md) | M5 |
| 0025 | [Insights report: on-demand computation, one period endpoint, M6 contract](0025-insights-report-computation-and-api.md) | M5 |
| 0026 | [Training Insights agent: fact-sheet input, placeholder-only numbers, validation](0026-training-insights-agent-and-grounding.md) | M6 |
| 0027 | [AI model provider: Foundry over the OpenAI v1 endpoint, keyless](0027-ai-model-provider-foundry-and-local-dev.md) | M6 |
| 0028 | [AI summaries: fingerprint cache, explicit generation, durable quotas, demo summaries](0028-summary-cache-quotas-and-demo-summaries.md) | M6 |
| 0029 | [Web tier: nginx container on Container Apps](0029-web-tier-nginx-container.md) | M7 |
| 0030 | [Azure topology, IaC (Bicep + azd) and the deploy pipeline](0030-azure-topology-iac-and-deployment.md) | M7 |
| 0031 | [Identities, role assignments, secrets and data-plane access](0031-identities-secrets-and-data-access.md) | M7 |
| 0032 | [Schema migrations and background jobs in Azure](0032-migrations-and-jobs-in-azure.md) | M7 |
| 0033 | [Production map provider, Azure Maps geocoding and the SAS display option](0033-production-maps-and-azure-maps.md) | M7 |
| 0034 | [Cost guardrails and observability](0034-cost-guardrails-and-observability.md) | M7 |
| 0035 | [Race catalogue sources, import strategy and the legal/crawling policy](0035-race-catalogue-sources-and-crawling-policy.md) | M8 |
| 0036 | [Race catalogue model, upsert/dedup/removal, and proposal → planned race run](0036-race-catalogue-model-and-race-runs.md) | M8 |
| 0037 | [Race proposals: interests, filtering and ranking, per-user state, calendar integration](0037-race-proposals-filtering-and-state.md) | M8 |
| 0038 | [The race import job on Container Apps](0038-race-import-job.md) | M8 |
| 0039 | [Training goals: model, rules, and the realistic-target estimate](0039-training-goals-and-realistic-target-estimate.md) | M9 |
| 0040 | [Training plan generation: envelope, fact sheet, agent, validation, quotas, failures](0040-training-plan-generation.md) | M9 |
| 0041 | [Training suggestions: storage, accept/dismiss/regenerate, calendar display](0041-training-suggestions-storage-and-calendar.md) | M9 |
