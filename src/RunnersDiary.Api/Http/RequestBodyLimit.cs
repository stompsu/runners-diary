using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;

namespace RunnersDiary.Api.Http;

/// <summary>Request body size limits (<c>RequestLimits</c> section).</summary>
public sealed class RequestLimitsOptions
{
    public const string SectionName = "RequestLimits";

    /// <summary>Largest accepted request body for every <c>/api</c> endpoint unless the endpoint carries its own <see cref="IRequestSizeLimitMetadata"/> (the GPX upload).</summary>
    public long MaxBodyBytes { get; set; } = 64 * 1024;
}

/// <summary>
/// Rejects oversized bodies of <c>/api</c> requests with 413 before authentication or binding read them. The limit is
/// <see cref="RequestLimitsOptions.MaxBodyBytes"/> unless the matched endpoint declares a larger one through
/// <see cref="IRequestSizeLimitMetadata"/> (a null, i.e. unlimited, declaration is ignored). By <c>Content-Length</c> up
/// front, and by the server's body size limit (Kestrel) for chunked bodies, whose <see cref="BadHttpRequestException"/> the exception
/// handler turns into the same 413 problem.
/// </summary>
public static class RequestBodyLimit
{
    public static IApplicationBuilder UseApiRequestBodyLimit(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                var max = context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize
                    ?? context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestLimitsOptions>>().Value.MaxBodyBytes;
                if (context.Request.ContentLength > max)
                {
                    await TooLarge().ExecuteAsync(context);
                    return;
                }

                var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (feature is { IsReadOnly: false })
                {
                    feature.MaxRequestBodySize = max;
                }
            }

            await next(context);
        });

    internal static IResult TooLarge() => Results.Problem(
        statusCode: StatusCodes.Status413PayloadTooLarge,
        title: "Request body too large",
        detail: "The request body exceeds the maximum size accepted by this API.");
}

/// <summary>Endpoint metadata that raises the request body limit of one route (read by <see cref="RequestBodyLimit"/>).</summary>
public sealed class EndpointBodyLimit(long maxBytes) : IRequestSizeLimitMetadata
{
    public long? MaxRequestBodySize { get; } = maxBytes;
}
