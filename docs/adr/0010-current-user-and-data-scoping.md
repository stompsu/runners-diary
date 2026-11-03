# 0010 – Current user and per-user data scoping (ICurrentUser + named query filters)

**Status:** Accepted (M2 plan approved by the user 2026-11-03; implemented in M2)
**Date:** 2026-11-03

## Context

Every endpoint must only ever touch the caller's data (users and guests), and foreign resources must look exactly like missing ones (404). ADR 0007 requires a single `ICurrentUser` abstraction; handlers never read claims. One forgotten `Where(r => r.UserId == …)` would be an IDOR, so scoping must be the default, not a convention.

## Decision

1. **Internal user id everywhere.** `Users.Id` (GUID) is the only owner key in data. External identities map to it: Entra `(tid, oid)` → `Users(ExternalTenantId, ExternalObjectId)` (Microsoft's recommended stable key; `sub` is pairwise and not used). Guests: the guest JWT `sub` *is* `Users.Id`.
2. **JIT provisioning** for Entra users in the `Entra` scheme's `OnTokenValidated`: look up `(tid, oid)` (memory-cached 10 min), insert a `User` (`Kind = User`, `DisplayName` from `name` if present, `TimeZoneId = Etc/UTC`, `TimeZoneIsDefault = true`) on first sight; a unique-index violation from a concurrent first request is caught and re-read. The resolved id is added to the principal as claim `rd_uid`. The `Guest` scheme adds `rd_uid = sub` after its `Kind = Guest` check.
3. **`ICurrentUser`** (interface in `RunnersDiary.Infrastructure/Identity/`, implementation `HttpCurrentUser` in the API, scoped):
   ```csharp
   public interface ICurrentUser
   {
       bool IsAuthenticated { get; }
       Guid UserId { get; }          // throws InvalidOperationException when not authenticated
       Guid? UserIdOrNull { get; }   // used by EF query filters; null => filter matches nothing
       bool IsGuest { get; }
   }
   ```
   It reads only `rd_uid` and the authenticating scheme. Background and system work never relies on whatever `ICurrentUser` the DI scope happens to have; it constructs its context with an **explicit** caller:
   - `DemoDataSeeder` writes through a `RunnersDiaryDbContext` created with `FixedCurrentUser(guestId, isGuest: true)` — it acts *as* the new guest, so the filter and the ownership guard apply normally.
   - `GuestCleanup` uses a context created with `AnonymousCurrentUser` and touches only the unfiltered `Users` table (`ExecuteDelete`, `Kind = Guest` repeated in the predicate); cascades remove runs and tracks. Its opportunistic run is started with `ExecutionContext.SuppressFlow()` so it never inherits the triggering request's `HttpContext`/user.
   - `UserProvisioner` (JIT, guest check) reads/inserts only `Users`, in its own DI scope.
4. **EF Core named query filter** `"Owner"` on every user-owned aggregate root (`Run`, `GpxTrack`): `e => e.UserId == _currentUser.UserIdOrNull`. The DbContext takes `ICurrentUser` in its constructor, so it is registered with **`AddDbContext` (no pooling)** + Aspire `EnrichSqlServerDbContext`. With no user, queries return nothing (fail closed).
5. **Writes**: new aggregates get `UserId` from `ICurrentUser.UserId` inside domain factories (`Run.Create(userId, …)`); request DTOs never contain a user id (JSON `UnmappedMemberHandling.Disallow` rejects one). A `SaveChangesInterceptor` (`OwnershipGuardInterceptor`) asserts that every Added/Modified/Deleted owned entity (`IUserOwned`) has `UserId == ` the context's caller, and **rejects any owned-entity write from a context without a caller** (background work, migrator, design time) — code acting for a user must say so explicitly. Violations throw → 500 + log (a bug, not user error). Bulk `ExecuteDelete`/`ExecuteUpdate` bypass `SaveChanges` and are used only on `Users` by guest cleanup.
   The database backs this up for tracks: composite FK `GpxTracks(RunId, UserId)` → `Runs(Id, UserId)` (`AK_Runs_Id_UserId`), so a track can never belong to a different user than its run.
6. **Bypass is explicit and rare**: no code writes owned entities for a user other than its explicit caller (the seeder *is* the guest via `FixedCurrentUser`); only `Guests/GuestCleanup` deletes across users, and only via `Users`. `IgnoreQueryFilters(["Owner"])` is not used in M2; any future use needs a reviewer-visible justification.
7. **404 for foreign**: get/update/delete load through the filtered set; not found ⇒ 404 problem details. No 403 is ever returned for ownership.

## Consequences

- Forgetting a `Where` cannot leak data; isolation tests (two users, two guests, user vs guest) prove it per endpoint.
- No DbContext pooling (small per-request allocation cost; irrelevant at this scale).
- Child data that lives inside `Run` (race details, rating, location — see ADR 0013) is covered by the `Run` filter automatically.
- M5 aggregate queries and M6 AI tools inherit the filter, so the agent's tools cannot read another user's metrics.

## Alternatives considered

- **Explicit scoping only** (every handler adds `Where(UserId == …)`) — transparent but one omission = IDOR; tests can't cover future endpoints.
- **SQL Server Row-Level Security with `SESSION_CONTEXT`** — strongest isolation, but needs session-context plumbing on pooled connections, complicates migrations/tests; overkill for one app.
- **Repository layer** wrapping DbContext — more code for the same guarantee as a query filter.

## References (checked 2026-11-03)

- EF Core 10 named query filters: https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew#named-query-filters , https://learn.microsoft.com/ef/core/querying/filters
- Aspire EF Core SQL Server (`AddDbContext` + `EnrichSqlServerDbContext`, no pooling): https://aspire.dev/integrations/databases/efcore/sql-server/sql-server-connect/
- Access token claims (`oid` + `tid` as user key) — not re-fetched for this ADR, verify during implementation: https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference
