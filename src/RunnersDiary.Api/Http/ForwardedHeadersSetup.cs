using Microsoft.AspNetCore.HttpOverrides;

namespace RunnersDiary.Api.Http;

public static class ForwardedHeadersSetup
{
    public const string EnabledKey = "ForwardedHeaders:Enabled";

    /// <summary>
    /// Whether X-Forwarded-For/-Proto are honoured must be a conscious deployment decision because the per-IP rate limits depend on
    /// it: outside Development, <c>ForwardedHeaders:Enabled</c> has to be configured explicitly (true or false) or startup fails.
    /// When true, the headers are honoured only from <c>ForwardedHeaders:KnownIPNetworks</c> (CIDR list; default: loopback) and at most
    /// <c>ForwardedHeaders:ForwardLimit</c> hops (default 1: set it to the number of trusted proxies in front of the API).
    /// M7 must verify the proxy chain before go-live (ADR 0011 section 7).
    /// </summary>
    /// <param name="relaxed">Development or build-time OpenAPI generation: an unset switch means disabled.</param>
    public static bool AddConfiguredForwardedHeaders(this IServiceCollection services, IConfiguration configuration, bool relaxed)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var raw = configuration[EnabledKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return relaxed
                ? false
                : throw new InvalidOperationException(
                    $"{EnabledKey} must be configured explicitly (true behind a trusted proxy, false otherwise); per-IP rate limits depend on it.");
        }

        if (!bool.TryParse(raw, out var enabled))
        {
            throw new InvalidOperationException($"{EnabledKey} must be 'true' or 'false', not '{raw}'.");
        }

        if (!enabled)
        {
            return false;
        }

        var networks = configuration.GetSection("ForwardedHeaders:KnownIPNetworks").Get<string[]>() ?? [];
        var forwardLimit = configuration.GetValue("ForwardedHeaders:ForwardLimit", 1);
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = forwardLimit;
            if (networks.Length > 0)
            {
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                foreach (var cidr in networks)
                {
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
                }
            }
        });
        return true;
    }
}
