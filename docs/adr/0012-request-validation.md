# 0012 – Request validation: .NET 10 built-in Minimal API validation + domain rules (no FluentValidation)

**Status:** Accepted (M2 plan approved by the user 2026-11-03; implemented in M2)
**Date:** 2026-11-03

## Context

The brief allows FluentValidation "or built-in validation". .NET 10 ships `Microsoft.Extensions.Validation` for Minimal APIs (`builder.Services.AddValidation()`), source-generated, supporting DataAnnotations, `IValidatableObject`, nested objects and collections, returning `400` validation problem details before the handler runs. Run input has many simple range/length rules and a handful of cross-field rules (rating only when completed, race details only for race types, heart-rate ordering, rest days without metrics) that are domain knowledge and should be unit-tested in `RunnersDiary.Domain`.

## Decision

1. **Shape rules** (required, ranges, lengths, enum membership, URL format) are DataAnnotations on the request records in `RunnersDiary.Api/Features/**`. `AddValidation()` is called in `Program.cs` — the same assembly as the request types, so the source generator sees them (no cross-assembly registration needed).
2. **Cross-field / domain rules** live in `RunnersDiary.Domain` (`Runs/RunRules.Validate(RunValues) → IReadOnlyList<DomainError>`), pure and unit-tested. Request records implement `IValidatableObject` and delegate to `RunRules`, so all errors surface through the same pipeline and format. Domain entity methods re-check invariants and throw `DomainRuleException` as a safety net (mapped to 400 by the exception handler; should never fire from the API).
3. **One error format**: `400 application/problem+json` (`HttpValidationProblemDetails`) with `errors` keyed by camelCase JSON paths (`race.resultTimeSeconds`, `rating.stars`); built-in validation reports C# member paths, which a `ProblemDetailsOptions.CustomizeProblemDetails` hook rewrites to camelCase. Unknown JSON properties are rejected (`JsonSerializerOptions.UnmappedMemberHandling = Disallow`) — also prevents mass-assignment of e.g. `userId`. Malformed/incomplete/unknown-member JSON and binding failures (`RouteHandlerOptions.ThrowOnBadRequest = true` → `BadHttpRequestException` → `ApiExceptionHandler`) become the same 400 shape, keyed by the JSON path (`body` if none) or the parameter name, with **fixed messages** — serializer/framework texts can name internal types and are never echoed.
4. Query-string parameters (`from`, `to`) are non-nullable `DateOnly` (the .NET 10 limitation is for *nullable* value-type parameters); range rules (`to ≥ from`, span ≤ 92 days) are checked in the handler and returned as the same validation problem.
5. No `[ValidatableType]` (experimental in .NET 10, ASP0029) is needed: all validated types are discovered from endpoint signatures.

## Consequences

- No new package; rules visible on the contract types; OpenAPI picks up some DataAnnotations (lengths/ranges) for the generated TS types' docs.
- Domain rules are reusable by M4 (GPX auto-fill) and the demo seeder (seed data validated with the same rules in a unit test).
- If built-in validation proves limiting (async DB rules, localization before .NET 11), FluentValidation can be added per endpoint via an endpoint filter without changing the error format.

## Alternatives considered

- **FluentValidation** — expressive, mature; but an extra dependency, rules live away from the contract types, and the brief's preference for minimal deps tips it.
- **Validation only in handlers** — inconsistent error shapes and easy to forget.

## References (checked 2026-11-03)

- Validation in ASP.NET Core (Microsoft.Extensions.Validation; order, `IValidatableObject`, cross-assembly, experimental attributes, nullable value-type parameter limitation): https://learn.microsoft.com/aspnet/core/fundamentals/validation?view=aspnetcore-10.0 (page dated 2026-10-20)
