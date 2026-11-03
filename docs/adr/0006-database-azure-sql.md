# 0006 – Relational database: Azure SQL Database (serverless, free offer)

**Status:** Accepted (M1; schema arrives in M2)
**Date:** 2026-11-03

## Context

Data is relational (users → runs → race details / ratings / GPX tracks) and the Insights feature needs date-range and aggregate queries (weekly/monthly sums, rolling 7/28-day load). Locations need a point type. Budget is near zero.

## Decision

Keep the brief's choice: **Azure SQL Database, General Purpose serverless, under the free offer**, accessed via EF Core (`Microsoft.EntityFrameworkCore.SqlServer`; `geography` via NetTopologySuite in M2). Locally: SQL Server container via Aspire; in tests: Testcontainers `MsSql`.

The free offer (GA since Feb 2025) gives per database 100,000 vCore-seconds/month, 32 GB data and 32 GB backup storage, up to 10 databases per subscription; on exhaustion the DB either auto-pauses until next month or continues at paid rates (we will choose **auto-pause** in M7 to cap cost).

## Consequences

- Auto-pause means the first request after idle can take tens of seconds — acceptable for a personal app; the API must use retry-on-failure (EF `EnableRetryOnFailure`, included by the Aspire integration) and the SPA must show a friendly "waking up" state (M3).
- Managed identity (Entra auth) from API to SQL in Azure — no passwords (M7).
- Local dev on Apple Silicon depends on x64 emulation of the SQL Server container (see M1 plan risks). **Fallback accepted by the user (2026-11-03):** a separate dev database on the Azure SQL free offer, wired into the AppHost via `AddConnectionString`, using Entra auth; created only after explicit confirmation of that action.

## Alternatives considered

- **PostgreSQL Flexible Server** — great fit technically (PostGIS), but no scale-to-zero; cheapest Burstable tier costs money monthly after free 12 months.
- **Cosmos DB (free tier)** — poor fit for aggregate/date-range relational queries and joins.
- **SQLite** — no managed Azure hosting with MI; fine for tests only, but we prefer real SQL Server via Testcontainers to catch provider differences.

## References

- Azure SQL Database free offer: https://learn.microsoft.com/azure/azure-sql/database/free-offer
- GA announcement (AZ467778): https://mc.merill.net/message/AZ467778
