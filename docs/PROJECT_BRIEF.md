You are the lead engineer on **Runners Diary**, a web app that helps runners plan and track their training and races. We will work in phases. **Do not skip ahead.** At the end of each phase, stop, summarize what you did, list open questions, and wait for my approval.

## 1. Product scope

### 1.1 Core features (MVP)

1. **Monthly calendar view** of the diary (Monday as first day of week, ISO weeks, week number column with weekly totals: distance, time, elevation gain).
2. **Navigation** between months and years (prev/next, "Today", month/year picker). The URL reflects the state (`/diary/2026/11`) so views are bookmarkable.
3. **Add / edit a run** by clicking a day (date prefilled) or via an "Add run" button. Each run has:
   - Date + start time, status: **Planned** or **Completed** (a planned run can be converted to completed)
   - Run type (see 1.2), title, notes
   - Location: picked on the map (pin) and/or searched by name (geocoding), stored as lat/lon + display name
   - Distance (km), duration, computed pace, elevation gain/loss
   - Optional: avg/max heart rate, RPE (1–10 perceived effort), shoes/gear, weather, surface (road/trail/track/treadmill)
   - Race-only: race name, official distance category, bib number, target time, result time, overall/category place, link to results
   - **Experience rating** (1–5 stars + short comment), available once the run is Completed (main use: after a race)
4. **GPX import** for a run, both **before** (planned race course preview) and **after** (actual recorded track). Show the track on the map with start/finish markers and an elevation profile. When a GPX is imported for a completed run, offer to auto-fill distance, duration, elevation, and start location from the file.
5. **Calendar "bubbles"**: each day shows small icons/chips for its runs, color-coded by run type, with distance; planned runs are visually distinct (outlined/dashed) from completed ones (filled). Races get a distinct marker (e.g. trophy/medal).
6. **Day / month list**: clicking a day or the month header opens a list of runs for that period with actions **View details**, **Edit**, **Delete** (with confirmation).
7. **Run types** (configurable enum, seeded):
   - **Races**: Road race (5K, 10K, Half marathon, Marathon, Other), Trail race, Ultra (road or trail), Track race, Cross-country, Virtual race
   - **Trainings**: Easy run, Recovery run, Long run, Tempo / Threshold, Intervals, Fartlek, Hill repeats, Progression run, Race-pace run, Strides / drills, Trail training, Treadmill run
   - **Other**: Cross-training (bike/swim/gym — logged, counts toward load but not toward running distance), Rest / injury day (note only)
8. **Insights dashboard**:
   - Training load: weekly distance/time/elevation; session load = duration (min) × RPE; acute (7-day) vs chronic (28-day) load and their ratio (ACWR) with simple "low / optimal / high" bands; monotony
   - Trends: week-over-week and month-over-month comparisons, year-to-date totals, distribution by run type, pace trend for easy runs, race results history
   - **AI summary**: a generated narrative for a selected period (week / month / training block before a race) — what went well, risks (e.g. load spikes), suggestions. Numbers are always **computed deterministically in code**; the LLM only interprets and narrates them, and must not invent data.

### 1.2 Non-functional requirements

- Multi-user from day one (each user sees only their own data). **Every page and API endpoint requires authentication** — there is no anonymous access to the app (only the landing/sign-in page is public).
- **Guest (demo) access**: the sign-in page has a "Try as guest" button next to "Sign in". See 1.3.
- Units: metric (km, min/km) by default; store everything in SI units, format in UI.
- UI language: English first, structure strings for i18n (Polish next).
- Responsive (usable on a phone after a run), accessible (keyboard nav, ARIA on calendar).
- GPX files up to ~20 MB; validate and sanitize; never trust file content.
- Cost-conscious: this is a personal/side project — prefer consumption-based and free tiers, scale-to-zero where possible.

### 1.3 Guest / demo access

- "Try as guest" creates an **isolated, temporary guest account** on the backend and signs the visitor in with a short-lived, app-issued JWT (role `guest`). Guests go through the same authorization pipeline as real users — no endpoint is anonymous.
- Each guest gets their **own copy of a prefilled demo dataset**, so guests can add, edit and delete freely without affecting each other:
  - ~6 months of realistic history ending at the current date (dates generated relative to "today" so the demo never looks stale): 3–5 runs per week across most training types, a mix of road and trail, RPE and heart rate filled in
  - 3–4 completed races with GPX tracks, results and experience ratings (e.g. a 10K, a half marathon, a trail race) and 1–2 **planned** upcoming races with a course GPX
  - At least one week with a load spike and one taper week, so the Insights dashboard and AI summary have something interesting to say
  - Locations around Gliwice / Silesia, Poland (sample GPX files committed to the repo under `seed/demo/`, generated or recorded, with no personal data)
- Seed data is defined as data (JSON + GPX files), applied by a `DemoDataSeeder` service that both guest creation and local dev use.
- A visible banner in the UI: "You're in demo mode — data is reset after 24 h. Sign in to keep your own diary."
- Guest accounts and their data (DB rows + blobs) **expire after 24 h** and are removed by a scheduled cleanup (Container Apps job or background service).
- Guardrails for guests: rate limiting on guest creation (per IP), limits on GPX upload size/count, and **AI summary limited** (e.g. 3 generations per guest; pre-generated cached summaries for the demo periods so the feature works without spending tokens).
- Optional (later): "Convert to real account" that moves guest data to a newly registered user.

### 1.4 Out of scope for MVP (keep the design open for them)

Strava/Garmin sync, FIT/TCX import, training plan generator, social features, mobile native app.

## 2. Technology & architecture

| Area | Choice |
|---|---|
| Frontend | React + TypeScript + Vite, React Router, TanStack Query, a component library (shadcn/ui + Tailwind, or propose alternative), Recharts for charts, react-hook-form + zod |
| Backend | .NET 10, ASP.NET Core Minimal APIs, vertical-slice organization, EF Core, FluentValidation (or built-in validation), OpenAPI document → generated TypeScript client for the frontend |
| Local dev orchestration | .NET Aspire (AppHost runs API, frontend, DB, Azurite) |
| Database | **Azure SQL Database (serverless, free offer)** via EF Core — runs, race details, ratings, aggregates. Use a `geography` point for location. Justify if you think PostgreSQL Flexible Server or Cosmos DB is a better fit, but trend/aggregate queries favor relational. |
| File storage | Azure Blob Storage: raw GPX (private container) + processed, simplified track as GeoJSON (for fast map rendering) |
| GPX processing | Parse server-side in C# (e.g. a GPX library or `XmlReader` with strict settings — no DTD/external entities), compute distance (haversine), elevation gain with smoothing, duration, splits; simplify geometry (Douglas–Peucker) for display |
| Maps | **Azure Maps (Gen2 pricing)** — covered by Azure credits, free monthly quota for base map tiles and search. Use the Azure Maps Web SDK (MapLibre-based) for display and Azure Maps Search for geocoding. **Never expose a subscription key in the browser**: use Microsoft Entra ID auth or SAS tokens issued by the backend. Wrap the map in a `MapView` abstraction so we can switch to MapLibre GL + OpenFreeMap tiles (free, no key) if costs or limits become an issue. |
| AI | **Microsoft Foundry** model deployment (e.g. a small, cheap GPT-class model; propose which), called from the backend via **Microsoft Agent Framework (.NET, 1.x)**. One "Training Insights" agent with function tools that read pre-computed metrics (no raw DB access, no write tools). Cache generated summaries per user+period; regenerate on demand or when data changes. |
| Auth | Microsoft Entra External ID (customer identity), MSAL React on frontend, JWT bearer on API. A second JWT scheme for guest tokens issued by the API (signing key in Key Vault, short expiry); a combined authorization policy accepts either, and a `guest` role/claim drives demo limits. |
| Hosting | Azure Container Apps: `web` (static React build served by nginx, or propose Azure Static Web Apps if better) and `api`; Azure Container Registry; managed identity everywhere (API → SQL, Blob, Foundry, Maps) |
| Secrets & config | Azure Key Vault + Container Apps secrets references; no secrets in repo |
| Observability | Application Insights + Log Analytics via OpenTelemetry |
| IaC & deploy | Bicep + Azure Developer CLI (`azd`), GitHub Actions with OIDC federated credentials |
| Testing | xUnit + Testcontainers (SQL) for API, Vitest + React Testing Library for UI, Playwright for E2E on the key flows |

Code conventions: C# for all backend code and examples; nullable reference types on; `TreatWarningsAsErrors`; analyzers enabled; TypeScript `strict`. Keep the domain logic (load calculation, GPX metrics) in a pure, unit-tested library with no infrastructure dependencies.

## 3. Phase 0 — Agents & project memory (do this first)

Create Claude Code subagents in `.claude/agents/` (one Markdown file each, with YAML frontmatter: `name`, `description`, `tools`, and `model` where it makes sense). Descriptions must say clearly **when** to delegate to the agent. Restrict tools to what each agent needs.

Proposed agents (adjust if you see gaps or overlap, and explain why):

1. **architect** — Read-only planning. Produces/updates `docs/architecture.md`, ADRs in `docs/adr/`, data model, API contract. Consulted before any new milestone and for cross-cutting decisions. Tools: Read, Grep, Glob, WebSearch/WebFetch.
2. **backend-dotnet** — Implements ASP.NET Core endpoints, EF Core model + migrations, validation, auth policies, domain logic. Must write unit/integration tests alongside code.
3. **frontend-react** — Implements React UI: calendar grid, run forms, lists, dashboard, routing, state, accessibility, responsive layout. Uses the generated API client only.
4. **maps-gpx** — Owns everything geospatial: GPX parsing/validation/metrics/simplification (C#), GeoJSON contract, `MapView` abstraction, Azure Maps integration and token flow, elevation profile.
5. **ai-insights** — Owns the metrics → prompt → narrative pipeline: Microsoft Agent Framework agent, tool definitions, prompt templates (versioned in repo), grounding rules, caching, cost/token limits, evaluation samples.
6. **azure-infra** — Bicep modules, `azure.yaml`, Container Apps, ACR, SQL, Storage, Key Vault, Maps account, Foundry resource, App Insights, managed identities and role assignments, GitHub Actions. Must never run `azd up`/deploy or change cloud resources without my explicit confirmation.
7. **qa-tester** — Writes and runs tests (xUnit, Vitest, Playwright), builds test data (sample GPX files: road, trail with elevation, corrupted, huge), reports failures with reproduction steps.
8. **reviewer** — Read-only code & security review after each milestone: correctness, OWASP issues (file upload, XML parsing, authZ on every endpoint — users and guests can only access their own runs, guest token abuse, guest rate limits), secrets, cost risks, consistency with ADRs.

Also in Phase 0:
- Create `CLAUDE.md` at the repo root: project summary, repo layout, build/test/run commands, conventions, the "how we work" rules from section 5, and which agent to use for what.
- Propose the repo structure (e.g. `src/RunnersDiary.Api`, `src/RunnersDiary.Domain`, `src/RunnersDiary.Infrastructure`, `src/RunnersDiary.AppHost`, `src/RunnersDiary.ServiceDefaults`, `web/`, `infra/`, `tests/`, `docs/`).
- **Stop** and show me: the agent list with one-line purpose each, the repo structure, and any questions.

## 4. Phase 1+ — Implementation milestones

Before each milestone, have **architect** produce a short plan (files to touch, contracts, risks). After each milestone, have **qa-tester** run all tests and **reviewer** review the diff, then stop for my approval. Commit per milestone with a clear message.

- **M1 – Skeleton**: solution + Aspire AppHost, API with health endpoint, React app shell with routing, generated API client, local SQL + Azurite, CI building and testing both apps. Acceptance: `dotnet run` on AppHost brings everything up; CI green.
- **M2 – Domain & Runs API**: data model (User, Run, RunType, RaceDetails, Rating, GpxTrack, Location), migrations, seeded run types, CRUD endpoints with per-user authorization, query endpoint by date range. Auth wired: Entra External ID for real users **plus guest access** (guest token endpoint, `DemoDataSeeder`, demo dataset in `seed/demo/`, 24 h expiry cleanup, rate limits). Acceptance: no endpoint answers without a valid token; two guests never see each other's data; a fresh guest sees a populated calendar for the current month.
- **M3 – Calendar UI**: sign-in page with "Sign in" and "Try as guest", demo-mode banner, month grid with bubbles, week totals, month/year navigation, day/month list panel, add/edit/delete flows, planned vs completed, rating after completion.
- **M4 – Maps & GPX**: location picker + geocoding, GPX upload (planned course or recorded track), server-side parsing & metrics, auto-fill, map track display, elevation profile, secure token flow for Azure Maps.
- **M5 – Insights**: metrics service (weekly/monthly aggregates, session load, ACWR, monotony, type distribution, pace trends), dashboard charts, period comparisons.
- **M6 – AI summary**: Training Insights agent on Microsoft Foundry via Microsoft Agent Framework, grounded only in computed metrics, cached, with a "regenerate" button, token/cost guardrails, and a handful of evaluation cases (e.g. load spike, taper before a race, empty month).
- **M7 – Azure deployment**: Bicep + azd for all resources, managed identities, Key Vault, App Insights, GitHub Actions deploy with OIDC. Show me the plan and an estimated monthly cost before deploying anything.

## 5. How we work

- Ask me when requirements are ambiguous; state assumptions explicitly when you proceed.
- Prefer small, reviewable changes; keep the build green.
- Every endpoint enforces that the caller owns the data. Every upload is validated (size, type, XML safety).
- No secrets, keys or connection strings in code or config files committed to git.
- Don't add dependencies without a one-line justification; prefer well-maintained ones.
- Check current docs (WebSearch/WebFetch) for Azure Maps, Microsoft Foundry, Microsoft Agent Framework and Aspire APIs instead of relying on memory — these change often.
- Keep `CLAUDE.md` and `docs/` up to date as decisions change.

**Start now with Phase 0.**
