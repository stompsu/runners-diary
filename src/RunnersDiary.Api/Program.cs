using System.Reflection;
using Microsoft.AspNetCore.Http;
using RunnersDiary.Api.Auth;
using RunnersDiary.Api.Features.Calendar;
using RunnersDiary.Api.Features.ClientConfig;
using RunnersDiary.Api.Features.GuestSessions;
using RunnersDiary.Api.Features.Health;
using RunnersDiary.Api.Features.Account;
using RunnersDiary.Api.Features.RunTypes;
using RunnersDiary.Api.Features.Runs;
using RunnersDiary.Api.Http;
using RunnersDiary.Api.OpenApi;
using RunnersDiary.Api.RateLimiting;
using RunnersDiary.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// The build-time OpenAPI generator runs this entry point under "GetDocument.Insider" with no
// connection strings, so infrastructure that needs real resources is skipped there (ADR 0002).
// Authentication is still registered so the document carries the bearer security scheme.
var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

if (!isOpenApiGeneration)
{
    builder.AddServiceDefaults();
    builder.AddInfrastructure();
}

var auth = builder.AddAppAuthentication(relaxed: isOpenApiGeneration);
var forwardedHeaders = builder.Services.AddConfiguredForwardedHeaders(builder.Configuration, relaxed: isOpenApiGeneration || builder.Environment.IsDevelopment());

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ValidationErrorKeys.CamelCase);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddApiJson();
// Bad requests (malformed JSON, failed binding) throw so ApiExceptionHandler can answer with a validation problem in every environment.
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddValidation();
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<BearerSecurityRequirementTransformer>();
    options.AddSchemaTransformer<TimeOnlySchemaTransformer>();
});
builder.Services.AddHealthFeature();
builder.Services.Configure<RequestLimitsOptions>(builder.Configuration.GetSection(RequestLimitsOptions.SectionName));

var app = builder.Build();

if (!auth.EntraEnabled)
{
    StartupLog.EntraDisabled(app.Logger, auth.EntraDisabledReason ?? "unknown");
}

if (auth.SpaClientIdMissing)
{
    StartupLog.EntraSpaClientIdMissing(app.Logger);
}

if (auth.GuestKeyIsEphemeral)
{
    StartupLog.EphemeralGuestKey(app.Logger);
}

if (forwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseApiRequestBodyLimit();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    // Development only (never mapped elsewhere): browsable without a token, unlike every other endpoint.
    app.MapOpenApi().AllowAnonymous();
}

if (!isOpenApiGeneration)
{
    app.MapDefaultEndpoints();
}

// Every /api endpoint requires an authenticated caller and passes the general limiter; the few anonymous ones opt out explicitly.
var anonymousApi = app.MapGroup("/api").AllowAnonymous().DisableRateLimiting();
anonymousApi.MapHealthEndpoints();
anonymousApi.MapClientConfigEndpoints();
// The only anonymous write: guest creation, guarded by the per-IP "guest-create" limiter. It needs its own group because
// DisableRateLimiting on a group always wins over a policy on the endpoint.
var guestApi = app.MapGroup("/api").AllowAnonymous();
CreateGuestSession.Map(guestApi);

var api = app.MapGroup("/api")
    .RequireAuthorization()
    .RequireRateLimiting(RateLimitingSetup.GeneralPolicy)
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
    .ProducesProblem(StatusCodes.Status429TooManyRequests);
api.MapMeEndpoints();
api.MapRunTypeEndpoints();
api.MapRunEndpoints();
api.MapCalendarEndpoints();

await app.RunAsync();

/// <summary>Entry point type, exposed for <c>WebApplicationFactory</c> in integration tests.</summary>
public partial class Program;
