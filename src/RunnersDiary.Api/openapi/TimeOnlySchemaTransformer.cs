using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RunnersDiary.Api.OpenApi;

/// <summary>Documents <see cref="TimeOnly"/> as the <c>HH:mm</c> wire format instead of the default <c>format: time</c>.</summary>
internal sealed class TimeOnlySchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(TimeOnly) || context.JsonTypeInfo.Type == typeof(TimeOnly?))
        {
            // Observed: the type is still unset when schema transformers run and is not filled in afterwards once the format is
            // removed, so set it explicitly (with null for nullable properties).
            var nullable = context.JsonPropertyInfo?.PropertyType == typeof(TimeOnly?) || context.JsonTypeInfo.Type == typeof(TimeOnly?);
            schema.Type = nullable ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
            schema.Format = null;
            schema.Pattern = @"^\d{2}:\d{2}$";
            schema.Description = "Local time of day, HH:mm.";
        }

        return Task.CompletedTask;
    }
}
