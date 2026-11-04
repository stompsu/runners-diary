using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using RunnersDiary.Api.Auth;
using RunnersDiary.Api.Features.Calendar;
using RunnersDiary.Api.Features.ClientConfig;
using RunnersDiary.Api.Features.Geocoding;
using RunnersDiary.Api.Features.GuestSessions;
using RunnersDiary.Api.Features.Health;
using RunnersDiary.Api.Features.Insights;
using RunnersDiary.Api.Features.Maps;
using RunnersDiary.Api.Features.Account;
using RunnersDiary.Api.Features.RunTypes;
using RunnersDiary.Api.Features.Runs;
using RunnersDiary.Api.Features.Tracks;
using RunnersDiary.Api.Http;
using RunnersDiary.Api.Jobs;
using RunnersDiary.Api.OpenApi;
using RunnersDiary.Api.RateLimiting;
using RunnersDiary.Infrastructure;
using RunnersDiary.Infrastructure.Gpx;

// One-shot job entry mode (ADR 0032 section 2): an Infrastructure-only host that runs the job and sets the exit code.
if (JobMode.TryGetJob(args, out var job))
{
    return await JobMode.RunAsync(job, args);
}

// Startup failures must end the process deterministically: an unhandled exception in the entry point makes the .NET crash handling
// spin at full CPU in some containers (seen on Docker Desktop arm64) instead of exiting. HostAbortedException is rethrown: the
// build-time OpenAPI generator aborts the host with it on purpose. Only when this assembly is the process entry point: test hosts
// (WebApplicationFactory) and the generator run it inside another process and need the original exception to propagate.
try
{
    await RunApiAsync(args);
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException && ReferenceEquals(Assembly.GetEntryAssembly(), typeof(Program).Assembly))
{
    // No values: the message is printed only for InvalidOperationException (the configuration validators and "missing key" errors,
    // which name keys only); every other exception contributes its type name only.
    await Console.Error.WriteLineAsync(ex is InvalidOperationException
        ? $"Startup failed ({ex.GetType().Name}): {ex.Message}"
        : $"Startup failed ({ex.GetType().Name}).");
    await Console.Error.FlushAsync();
    return 1;
}

static async Task RunApiAsync(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    // The build-time OpenAPI generator runs this entry point under "GetDocument.Insider" with no
    // connection strings, so infrastructure that needs real resources is skipped there (ADR 0002).
    // Authentication is still registered so the document carries the bearer security scheme.
    var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    if (!isOpenApiGeneration)
    {
        ProductionConfigurationValidator.ValidateApi(builder.Configuration, builder.Environment);
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
    // Form parsing limits (the only form endpoint is the GPX upload; ASP.NET Core 10 has no per-endpoint form options for minimal APIs,
    // so these are global): file sections up to the largest file limit, and only the few small fields the upload defines.
    var gpxLimits = builder.Configuration.GetSection(GpxLimitsOptions.SectionName).Get<GpxLimitsOptions>() ?? new GpxLimitsOptions();
    builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = Math.Max(gpxLimits.MaxFileBytes, gpxLimits.GuestMaxFileBytes) + TracksEndpoints.MultipartOverheadBytes;
        options.ValueCountLimit = 16;
        options.ValueLengthLimit = 1024;
    });
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
    anonymousApi.MapClientConfigEndpoints();
    // The only anonymous write: guest creation, guarded by the per-IP "guest-create" limiter. It needs its own group because
    // DisableRateLimiting on a group always wins over a policy on the endpoint.
    // Health is anonymous too but carries a light per-IP limit (a group-level DisableRateLimiting would win over it).
    var healthApi = app.MapGroup("/api").AllowAnonymous();
    healthApi.MapHealthEndpoints();
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
    api.MapInsightsEndpoints();
    // The upload route's body limit is fixed at mapping time from configuration (the other routes keep the 64 KB default).
    api.MapTrackEndpoints(gpxLimits);
    api.MapGeocodingEndpoints();
    api.MapMapsEndpoints();

    await app.RunAsync();
}

/// <summary>Entry point type, exposed for <c>WebApplicationFactory</c> in integration tests.</summary>
public partial class Program;
