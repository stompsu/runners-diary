using System.Reflection;
using RunnersDiary.Api.Features.Health;
using RunnersDiary.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// The build-time OpenAPI generator runs this entry point under "GetDocument.Insider" with no
// connection strings, so infrastructure that needs real resources is skipped there (ADR 0002).
var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

if (!isOpenApiGeneration)
{
    builder.AddServiceDefaults();
    builder.AddInfrastructure();
}

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi("v1");
builder.Services.AddHealthFeature();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!isOpenApiGeneration)
{
    app.MapDefaultEndpoints();
}

var api = app.MapGroup("/api");
api.MapHealthEndpoints();

await app.RunAsync();

/// <summary>Entry point type, exposed for <c>WebApplicationFactory</c> in integration tests.</summary>
public partial class Program;
