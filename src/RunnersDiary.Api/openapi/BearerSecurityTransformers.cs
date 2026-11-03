using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using RunnersDiary.Api.Auth;

namespace RunnersDiary.Api.OpenApi;

/// <summary>Documents the bearer scheme (Entra access token or guest token) when the authentication schemes are registered.</summary>
internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider schemes) : IOpenApiDocumentTransformer
{
    public const string SchemeName = "Bearer";

    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (await schemes.GetSchemeAsync(AuthenticationSetup.GuestScheme) is null)
        {
            return;
        }

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Entra External ID access token (scope access_as_user) or a guest token from POST /api/guest-sessions.",
        };
    }
}

/// <summary>Adds the bearer requirement to every operation that requires authorization (anonymous ones stay open).</summary>
internal sealed class BearerSecurityRequirementTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any() || !metadata.OfType<IAuthorizeData>().Any())
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(BearerSecuritySchemeTransformer.SchemeName, context.Document)] = [],
        });
        return Task.CompletedTask;
    }
}
