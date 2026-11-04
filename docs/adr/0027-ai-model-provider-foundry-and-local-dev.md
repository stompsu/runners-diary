# 0027 – AI model provider: Foundry deployment over the OpenAI v1 endpoint, keyless auth, no cloud before M7

**Status:** Accepted (M6 plan approved 2026-11-04; verified against Microsoft Learn) · Amended 2026-11-04 (implementation, see the end) · Amended 2026-11-04 (deployment type: Global Standard, see the end)
**Date:** 2026-11-04

## Context

Brief §2 "AI" asks for a Microsoft Foundry model deployment (a small, cheap GPT-class model), called from the backend through Microsoft Agent Framework (.NET 1.x), with managed identity in Azure and no keys in the repo. As with Azure Maps in M4 (ADR 0022), **no cloud resource may be created before M7** without the user's explicit confirmation. M6 must therefore be fully buildable, testable and demoable without a live model.

Current facts (verified 2026-11-04):
- **Microsoft Agent Framework (.NET):** `Microsoft.Agents.AI` **1.27.0** stable (2026-10-30; 1.0 GA in April 2026) provides `AIAgent`, `ChatClientAgent` and `RunAsync<T>`. It depends on Microsoft.Extensions.AI ≥ 10.10.0 and also brings `Microsoft.Extensions.AI.Evaluation`, `Microsoft.ML.Tokenizers` and `Microsoft.Extensions.VectorData.Abstractions`.
  - `Microsoft.Agents.AI.OpenAI` 1.27.0 is stable. `Microsoft.Agents.AI.Foundry` is **preview only** (1.27.0-preview), and `Microsoft.Agents.AI.AzureAI` is deprecated.
  - The provider docs install `Azure.AI.OpenAI --prerelease`. That package's stable line is 2.1.0 (2024); its current line is 2.9.0-beta.
- **Microsoft.Extensions.AI** 10.11.0 and **Microsoft.Extensions.AI.OpenAI** 10.11.0 are stable (2026-10-14; the OpenAI package requires `OpenAI` ≥ 2.14.0). **OpenAI** 2.14.0 is stable (2026-09-15). **Azure.Identity** 1.21.0 is stable.
- **Keyless Foundry inference** uses the OpenAI SDK against the **v1 endpoint** `https://<resource>.openai.azure.com/openai/v1/` with `BearerTokenPolicy(credential, "https://ai.azure.com/.default")` (`#pragma warning disable OPENAI001`). No `api-version` is needed. The role **Cognitive Services User** is required on the resource; assignments take up to 5 minutes to propagate.
- **Aspire:**
  - `Aspire.Hosting.Foundry` (`AddFoundry(...).AddDeployment(...)`, `AsExisting`) is **preview** (13.6.0-preview). `AddFoundry` implicitly adds **Azure provisioning in run mode**, which needs a subscription, resource group and location in the AppHost config; `AsExisting` needs the same values.
  - Its documented client path for cloud resources is the OpenAI SDK + `Azure.Identity` + `AsIChatClient()`. `Aspire.OpenAI` does not support managed identity.
- **Models** (Azure retail prices API, `swedencentral`, USD per 1M tokens; lifecycle from the retirement schedule, 2026-09-21):

  | Model (version) | Lifecycle, retirement | Global in / cached / out | Data Zone in / out |
  |---|---|---|---|
  | **gpt-5.4-mini** (2026-03-17) | GA, 2027-09-21 | 0.75 / 0.075 / 4.50 | 0.825 / 4.95 |
  | gpt-5.4-nano (2026-03-17) | GA, 2027-09-21 | 0.20 / 0.02 / 1.25 | 0.22 / 1.375 |
  | gpt-6-luna (2026-09-22) | listed in the reasoning-model table; ⚠ not yet in the retirement schedule | 0.10 / 0.01 / 0.50 (short context) | — |
  | gpt-5-mini / gpt-4.1-mini / gpt-4o-mini | 2025 models; 4.1-mini and 4o-mini are **Deprecated** (blocked for new subscriptions, retire 2027-04-14) | — | — |

  GPT-5.x models are reasoning models:
  - `temperature`/`top_p` are not supported;
  - `max_completion_tokens` counts reasoning tokens;
  - `reasoning_effort` accepts `low`, `medium` and `high` (`minimal` on GPT-5 reasoning models). Microsoft Learn does not document `none`; this ADR uses `low`;
  - structured outputs are supported.
  - GA models are blocked for *new* subscriptions 12 months after launch.

## Decision

1. **Client stack (Infrastructure, `Ai/`):**
   - `OpenAI.Chat.ChatClient(model: <deployment>, authenticationPolicy: BearerTokenPolicy(credential, "https://ai.azure.com/.default"), options: { Endpoint = <endpoint>/openai/v1/ })`;
   - `.AsIChatClient()` (Microsoft.Extensions.AI.OpenAI);
   - the `ChatClientBuilder` pipeline `UseOpenTelemetry()` (sensitive data **off**) + `UseLogging()`;
   - `new ChatClientAgent(…)` (Microsoft.Agents.AI).

   The **Chat Completions** API, not Responses: the agent needs no hosted tools, Chat Completions keeps no server-side response state, and the pipeline is the same `IChatClient` either way. The SDK's transport retry is limited to 1, so time and cost stay bounded (the pipeline's own retry is for invalid output only; ADR 0026 §3).
   - Not used: `Azure.AI.OpenAI` (prerelease), `Microsoft.Agents.AI.Foundry` (preview, aimed at Foundry Agent Service) and `Microsoft.Agents.AI.OpenAI` (convenience only).
2. **Providers** (`Ai:Provider`, validated at startup):
   - `None` (default everywhere): generation is unavailable (503 `feature-unavailable`). Cached, demo and template summaries are still served.
   - `Foundry`: requires `Ai:Foundry:Endpoint` (absolute https) and `Ai:Foundry:Deployment`. Credential:
     - **`ManagedIdentityCredential(clientId)`** when `Ai:Foundry:ManagedIdentityClientId` is set (Azure, M7);
     - otherwise **`DefaultAzureCredential`** with interactive sources excluded (local: the developer's `az login`).
     - **No API key setting exists**, so there is nothing secret to configure.
   - `Fake`: a deterministic, rule-based `IChatClient` that writes a valid draft from the fact sheet. It is **allowed only in Development and Testing** (otherwise startup fails), so the UI can be exercised locally without any cloud.
3. **Kill switch:** `Ai:Enabled` (default `true`). `false` disables generation at runtime (503) without a redeploy; Container Apps env change in M7. Reads stay available.
4. **AppHost (local):**
   - **No `Aspire.Hosting.Foundry`.** It is preview, and in run mode it provisions Azure resources and needs subscription config. Starting the AppHost could otherwise create billable resources.
   - Same pattern as Entra (M2 §10): the AppHost reads its user-secrets `Ai:Endpoint` and `Ai:Deployment` (non-secret ids, never committed) and forwards `Ai__Provider=Foundry`, `Ai__Foundry__Endpoint` and `Ai__Foundry__Deployment` only when both are present.
   - `Ai:UseFake=true` forwards `Ai__Provider=Fake`. Otherwise it forwards `Ai__Provider=None`.
5. **Default model: `gpt-5.4-mini`**, **Data Zone Standard (EU)** (→ superseded by the 2026-11-04 amendment "deployment type": **Global Standard**), `reasoning_effort: low`, `MaxOutputTokens` 1 500 (reasoning included). The deployment name is configuration (`Ai:Foundry:Deployment`), and the model label stored with summaries is the response's `ModelId`.
   - **Why mini:** strict placeholder and schema compliance matters more than per-token price at this volume.
   - **Why Data Zone EU:** training data stays processed in the EU for +10 %, cents per month. (→ superseded by the 2026-11-04 amendment "deployment type": the subscription has no Data Zone quota for gpt-5.4-mini.)
   - gpt-5.4-nano and gpt-6-luna are cheaper candidates. They are judged by the live eval in M7, before deployment (plan §9 Q2).
6. **Cost per summary** (gpt-5.4-mini, Data Zone EU): about 6k input tokens (prompt ≈ 2k + fact sheet ≤ 4k) and ≤ 1.5k output (visible ≈ 600 + low reasoning). That is ≈ 6 000 × 0.825 / 10⁶ + 1 500 × 4.95 / 10⁶ ≈ **$0.0124**, ≤ $0.025 with the one retry. The ceiling with the default quotas (ADR 0028) is 200 generations per day ≈ $2.5/day ≈ **$75/month** worst case; one active user at 2/day ≈ $0.75/month. Standard deployments have no fixed monthly fee (⚠ re-check in the M7 cost estimate). **Superseded by the amendment at the end** (per-call cost at the cap, attempt-based ceiling).
7. **M7 (azure-infra; only with the user's confirmation):**
   - a Foundry resource (`Microsoft.CognitiveServices/accounts`, kind `AIServices`, `disableLocalAuth: true`) in an EU region that offers the Data Zone deployment (→ since the 2026-11-04 amendment: an EU region that offers the Global Standard deployment, `swedencentral`);
   - the model deployment with a small TPM capacity;
   - **Cognitive Services User** for the API's user-assigned identity;
   - a budget alert;
   - no Key Vault secret.

## Verification against Microsoft Learn (MCP server, 2026-11-04)

- **Confirmed:**
  - `new ChatClientAgent(chatClient, instructions: …)`, `AIAgent.RunAsync<T>` and `ChatResponseFormat.ForJsonSchema<T>()` for structured output ([Agent Framework structured outputs](https://learn.microsoft.com/agent-framework/agents/structured-outputs)).
  - Token usage is on `AgentResponse.Usage` (`UsageDetails?`, Microsoft.Agents.AI.Abstractions).
- **Confirmed:** keyless v1 endpoint: `BearerTokenPolicy(new DefaultAzureCredential(), "https://ai.azure.com/.default")` with `Endpoint = https://<resource>.openai.azure.com/openai/v1/` and `#pragma warning disable OPENAI001` ([v1 API](https://learn.microsoft.com/azure/foundry/openai/api-version-lifecycle)).
  - The REST reference and the endpoint-switching guide also show the scope `https://cognitiveservices.azure.com/.default`.
  - The token for the scope used must be accepted; the 401 troubleshooting row names `https://ai.azure.com/.default`.
- **Confirmed:** RBAC: **Cognitive Services User** on the Foundry resource. Cognitive Services OpenAI User covers OpenAI models only; Owner and Contributor grant no inference. Allow 5 minutes for propagation ([keyless auth troubleshooting](https://learn.microsoft.com/azure/foundry/foundry-models/how-to/configure-entra-id)).
- **Confirmed:** gpt-5.4-mini (2026-03-17) is offered as **Data Zone Standard in Europe**: francecentral, germanywestcentral, italynorth, norwayeast, polandcentral, spaincentral, **swedencentral**, switzerlandnorth and westeurope. It is not offered in northeurope. Source: [region availability](https://learn.microsoft.com/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability). An older Q&A answer saying "US only" is superseded by this table. (→ Region availability is not subscription quota: the user's subscription has a Data Zone quota of 0 for this model, see the 2026-11-04 amendment "deployment type".)
- **Confirmed:** reasoning models use `max_completion_tokens` (includes reasoning tokens) on Chat Completions. `temperature`, `top_p`, penalties, `logprobs` and `max_tokens` are unsupported ([reasoning models](https://learn.microsoft.com/azure/foundry/openai/how-to/reasoning)).
- **Note:** Learn recommends the Responses API for new applications, with Chat Completions "when you need its message-based interface". Chat Completions stays here, for the reasons in Decision 1: no hosted tools and no server-side state. The `IChatClient` seam keeps a switch cheap.
- **Not on Learn:** the Aspire Foundry hosting specifics (preview package, run-mode provisioning) are documented on aspire.dev only. Learn confirms the general rule that Aspire Azure hosting integrations provision resources at AppHost start unless `AsExisting`/publish-only is used.

## Consequences

- M6 needs no Azure resource: CI uses the fake client, and local dev uses `None` or `Fake`. Pointing local dev at a real deployment is opt-in, with two user-secrets and `az login`.
- Keyless only: a leaked config file exposes nothing usable. In Azure the identity, not a key, grants access.
- The client is plain OpenAI SDK over the v1 endpoint, so any Foundry model with an OpenAI-compatible chat API is a config change. The `IChatClient` seam keeps the agent independent of the provider.
- `Microsoft.Agents.AI` brings sizeable transitive packages (tokenizers, vector-data and evaluation abstractions) into the API. A step-2 spike checks that the build stays warning-free (`TreatWarningsAsErrors`) and that no vulnerable transitive package needs a security pin.
- Models retire about 18 months after launch and block new subscriptions after 12. The model choice is revisited in M7 and at each retirement notice. A model change makes cached summaries stale (ADR 0028), which is intended.

## Alternatives considered

- **`Aspire.Hosting.Foundry` with `AsExisting`:** preview, needs subscription config, and `AddFoundry` provisions in run mode. Rejected for M6; can be reconsidered in M7 for the publish path.
- **`Azure.AI.OpenAI` client:** prerelease for current features. The v1 endpoint makes it unnecessary. Rejected.
- **Foundry Agent Service (`Microsoft.Agents.AI.Foundry`, server-side agents):** preview, stateful service-side resources, more moving parts. Rejected.
- **API key in Key Vault:** works, but managed identity is the brief's rule and needs no secret. Rejected.
- **Foundry Local / a local model in CI:** CI runners have no suitable runtime and outputs would still vary. The scripted fake gives deterministic CI. Foundry Local stays a possible developer option.
- **Responses API:** recommended by the docs for hosted tools. It stores responses by default, which this use case does not need. It can be switched behind `IChatClient` later.

## References (checked 2026-11-04)

- NuGet `Microsoft.Agents.AI` 1.27.0 (2026-10-30, dependencies): https://www.nuget.org/packages/Microsoft.Agents.AI/
- NuGet profile MicrosoftAgentFramework (stable vs preview packages): https://www.nuget.org/profiles/MicrosoftAgentFramework/
- NuGet `Microsoft.Agents.AI.OpenAI` 1.27.0: https://www.nuget.org/packages/Microsoft.Agents.AI.OpenAI/
- NuGet `Microsoft.Extensions.AI.OpenAI` 10.11.0 (2026-10-14): https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI/
- NuGet `OpenAI` 2.14.0 (2026-09-15): https://www.nuget.org/packages/OpenAI/ · `Azure.Identity` 1.21.0: https://www.nuget.org/packages/Azure.Identity/ · `Azure.AI.OpenAI` (stable 2.1.0, 2.9.0-beta.1): https://www.nuget.org/packages/Azure.AI.OpenAI/
- Agent Framework, Azure OpenAI provider (updated 2026-11-02): https://learn.microsoft.com/agent-framework/integrations/by-component/model-providers/azure-openai
- Azure OpenAI SDK language support (v1 endpoint, `BearerTokenPolicy`, scope `https://ai.azure.com/.default`; updated 2026-07-22): https://learn.microsoft.com/azure/foundry/openai/supported-languages
- Keyless authentication, Cognitive Services User role, 5-minute propagation (2026-08-31): https://learn.microsoft.com/azure/foundry/foundry-models/how-to/configure-entra-id
- Aspire Microsoft Foundry hosting (preview package, `AsExisting` config, provisioning in run mode; Aspire 13.6): https://aspire.dev/integrations/cloud/azure/azure-ai-foundry/azure-ai-foundry-host/ · client side: https://aspire.dev/integrations/cloud/azure/azure-ai-foundry/azure-ai-foundry-connect/
- Reasoning models: parameters and support matrix (updated 2026-10-30): https://learn.microsoft.com/azure/foundry/openai/how-to/reasoning
- Model lifecycle (12-month deprecation for new customers, 18-month retirement; 2026-07-24): https://learn.microsoft.com/azure/foundry/openai/concepts/model-retirements · retirement schedule (2026-09-21): https://learn.microsoft.com/azure/foundry/openai/concepts/model-retirement-schedule
- Azure retail prices API (gpt-5.4-mini/nano, gpt-6-luna, gpt-5.6-luna meters; effective 2026-03/09): `https://prices.azure.com/api/retail/prices?$filter=armRegionName eq 'swedencentral' and contains(meterName, '5.4 mini')`. The public pricing page renders its prices client-side and could not be read.

## Amended 2026-11-04 (implementation)

Details: [M6 plan §12](../plans/M6-ai-summary.md#12-implementation-notes--deviations-2026-11-04).

### Cost per call (replaces Decision 6's figures)

Prices are unchanged: gpt-5.4-mini Data Zone EU, $0.825 in / $4.95 out per 1M tokens. (→ superseded by the 2026-11-04 amendment "deployment type": Global Standard prices and recomputed figures below.)

| Case | Input tokens | Output tokens | Cost per HTTP call |
|---|---|---|---|
| **Worst case:** fact sheet at the 24 000-character cap + prompt | ≈ 10 000 | 1 500 (cap) | ≈ 10 000 × 0.825 / 10⁶ + 1 500 × 4.95 / 10⁶ ≈ **$0.0157** |
| **Typical:** demo-sized sheet (≈ 16.2k chars) and a short answer | — | — | ≈ **$0.007** |

The daily ceiling is in ADR 0028's amendment. It counts **attempts** (HTTP calls), not generations.

### Provider failures

- **Provider 429** (after the SDK's single transport retry) → **503 `ai-busy`**:
  - `Retry-After` comes from the provider's `retry-after-ms`, `x-ms-retry-after-ms` or `Retry-After` header, capped at 5 minutes; the default is 10 s.
  - A throttle **never counts against the caller's user or guest quota**. A ledger row with outcome `Throttled` is written only when an earlier attempt of the same generation was billed, and it counts toward the global attempt budgets only (ADR 0028 amendment).
- **Credential failures** → **503 `feature-unavailable`**, with **no ledger row**:
  - `AuthenticationFailedException` or `CredentialUnavailableException`, also when wrapped (e.g. no `az login` locally, or a missing role);
  - no HTTP request reaches the provider, so nothing was billed.
- **Kill switch:** `Ai:Enabled` is read through `IOptionsMonitor`, so a configuration change takes effect at runtime without a restart. This covers both the POST check and `client-config.ai.summaryGeneration`; the latter is cached by the client for up to 5 minutes.

### M7 capacity sizing (review finding M4)

Azure counts **prompt tokens + `max_completion_tokens`** of each request toward the deployment's tokens-per-minute (TPM) limit, not the tokens actually generated. One call at the cap therefore counts roughly 10–12k toward TPM (≈ 10k input + 1 500). Two concurrent generations already count more than 20k; with a retry or a second replica within the same minute, the need passes 30k.

- **Size the M7 deployment at ≥ 30k TPM**, not the 10k suggested in plan §8 item 3.
- Below that, users see 503 `ai-busy`, which is safe (no quota is used) but frequent.
- azure-infra re-checks the TPM accounting rule against the current quota docs in M7.

### Verified on the wire

`FoundryWireTests` (ADR 0026 amendment) checks the exact request: the v1 path, the bearer token for `https://ai.azure.com/.default`, `max_completion_tokens`, `reasoning_effort: low`, strict `json_schema`, and no `temperature`/`max_tokens`. The real endpoint is still unexercised (live check in M7, plan §9 Q9).

## Amended 2026-11-04 (deployment type)

User decision, 2026-11-04 (M7 plan §12 deviation **D-AI**). Replaces the deployment type in Decision 5 and the prices in Decision 6 and in the implementation amendment above.

### Why the change

A read-only quota check (`az cognitiveservices usage list`) on the user's subscription ("Visual Studio Ultimate z subskrypcją MSDN"):

| Quota | Regions checked | Limit | Used | Free |
|---|---|---|---|---|
| `OpenAI.DataZoneStandard.gpt-5.4-mini` | westeurope, swedencentral, polandcentral, germanywestcentral, francecentral | **0** | — | **0** |
| `OpenAI.GlobalStandard.gpt-5.4-mini` | subscription-wide global pool | 1 000k TPM | 700k (the user's other deployments) | **300k** (the app needs 30k) |

Other models do have EU Data Zone quota on this subscription, e.g. gpt-6-luna 100, gpt-5-mini 300, gpt-5.4 100. This confirms the risk recorded in the M7 plan ("Checked against Microsoft Learn"): the region table listing a model under Data Zone does not mean the subscription has quota for it.

### Decision

1. **Decision 5 now reads: `gpt-5.4-mini`, Global Standard** (Bicep `sku: { name: 'GlobalStandard', capacity: capacityKtpm }`, 30k TPM). Everything else in Decision 5 is unchanged (`reasoning_effort: low`, `MaxOutputTokens` 1 500, deployment name as configuration).
2. **Data residency:**
   - **Data at rest** stays in the resource's region, `swedencentral` (EU).
   - **Prompts and responses may be processed in any Azure region worldwide** ([deployment types](https://learn.microsoft.com/azure/foundry/foundry-models/concepts/deployment-types): "Global types: May be processed in any Azure region"; page updated 2026-08-12).
   - The user accepts global processing because the model sees only the **fact sheet**: numeric training metrics, dates and run-type codes. No free text, names, notes, location, heart rate or ids reach the model (ADR 0026 §1). ADR 0026 is amended to make that rule the data-residency safeguard.
3. **Region:** gpt-5.4-mini is offered as Global Standard in `swedencentral` (and in every other European region of the table) ([region availability](https://learn.microsoft.com/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability), Global Standard → Europe; page updated 2026-10-19). The region choice (ADR 0030 §3) does not change.
4. **Data Zone stays the preferred option.** If EU Data Zone quota for gpt-5.4-mini is granted later (quota request), switching back is one Bicep value (`sku.name: 'DataZoneStandard'`) plus a re-provision (M7 gates G1 + G2; ⚠ whether Azure changes the SKU of an existing deployment in place or needs it deleted and re-created is checked at that time). Cost goes up by 10 %. The model is the same, and cached summaries stay fresh as long as the deployment name is kept (ADR 0028's validity tuple includes the configured deployment, not the SKU).
5. **EU-quota alternatives for the G6 live eval** (if the user prefers EU processing over gpt-5.4-mini): **gpt-6-luna** (Data Zone quota 100; cheapest, ⚠ lifecycle not yet published) and **gpt-5-mini** (Data Zone quota 300; a 2025 model, check its retirement date first). Either one is a Bicep model/version change plus the live eval; the agent and prompt need no change if the model passes the same eval.

### Cost (replaces Decision 6 and the implementation amendment's per-call table)

Global Standard prices since 2026-11-04 (Azure retail, already in the Context table): gpt-5.4-mini **$0.75 in / $0.075 cached in / $4.50 out per 1M tokens**. Every AI cost figure scales by 0.75 / 0.825 = 4.50 / 4.95 ≈ **0.909**.

| Case | Input tokens | Output tokens | Cost per HTTP call (Data Zone → Global) |
|---|---|---|---|
| **Worst case:** fact sheet at the 24 000-character cap + prompt | ≈ 10 000 | 1 500 (cap) | 10 000 × 0.75 / 10⁶ + 1 500 × 4.50 / 10⁶ = 0.0075 + 0.00675 ≈ **$0.0143** (was $0.0157) |
| **Typical:** demo-sized sheet (≈ 16.2k chars) and a short answer | — | — | ≈ **$0.0064** (was $0.007) |

- Cached input ($0.075) is not counted: the fact sheet changes per request and the prompt prefix may or may not hit the cache, so the figures stay an upper bound.
- The daily and monthly ceilings are recomputed in ADR 0028's amendment "deployment type" ($2.89–5.79/day). The 30k TPM sizing and the 400k-tokens/24 h alert (ADR 0034) are token-based and do not change.

### References (checked 2026-11-04)

- Deployment types, data processing per type: https://learn.microsoft.com/azure/foundry/foundry-models/concepts/deployment-types
- Region availability, Global Standard and Data Zone Standard tables: https://learn.microsoft.com/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability
- Quota tiers (gpt-5.4-mini listed under `GlobalStandard`): https://learn.microsoft.com/azure/foundry/openai/quotas-limits
