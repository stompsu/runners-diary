using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RunnersDiary.Api.Auth;
using RunnersDiary.Api.Http;

namespace RunnersDiary.Api.RateLimiting;

public static class RateLimitingSetup
{
    public const string GuestCreatePolicy = "guest-create";
    public const string GeneralPolicy = "api";
    public const string GpxUploadPolicy = "gpx-upload";
    public const string GeocodePolicy = "geocode";
    public const string AiSummaryPolicy = "ai-summary";
    public const string MapsTokenPolicy = "maps-token";
    public const string HealthPolicy = "health";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));
        services.AddSingleton<GuestCreateGlobalLimiter>();
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitingOptions>>((options, limits) =>
        {
            var guest = limits.Value.GuestCreate;
            var general = limits.Value.General;

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay) ? delay : TimeSpan.FromSeconds(60);
                context.HttpContext.Response.Headers.RetryAfter =
                    Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                await RateLimitedProblem().ExecuteAsync(context.HttpContext);
            };

            options.AddPolicy(GuestCreatePolicy, context => RateLimitPartition.GetSlidingWindowLimiter(
                ClientIp(context),
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = guest.PermitLimit,
                    Window = guest.Window,
                    SegmentsPerWindow = 6,
                    QueueLimit = 0,
                }));

            // Endpoint-level policies replace the group's general policy for their route (the most specific metadata wins).
            options.AddPolicy(GpxUploadPolicy, context => PerCaller(context, limits.Value.GpxUpload));
            options.AddPolicy(GeocodePolicy, context => PerCaller(context, limits.Value.Geocode));
            options.AddPolicy(HealthPolicy, context => PerCaller(context, limits.Value.Health));
            options.AddPolicy(MapsTokenPolicy, context => PerCaller(context, limits.Value.MapsToken));
            options.AddPolicy(AiSummaryPolicy, context => PerCaller(context, limits.Value.AiSummary));

            options.AddPolicy(GeneralPolicy, context => RateLimitPartition.GetTokenBucketLimiter(
                context.User.FindFirst(AppClaims.UserId)?.Value ?? "ip:" + ClientIp(context),
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = general.TokenLimit,
                    TokensPerPeriod = general.TokensPerPeriod,
                    ReplenishmentPeriod = general.ReplenishmentPeriod,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        });
        return services;
    }

    private static RateLimitPartition<string> PerCaller(HttpContext context, RateLimitingOptions.PerCallerLimit limit)
    {
        var isGuest = context.User.FindFirst(AppClaims.Kind)?.Value == AppClaims.KindGuest;
        return RateLimitPartition.GetSlidingWindowLimiter(
            context.User.FindFirst(AppClaims.UserId)?.Value ?? "ip:" + ClientIp(context),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = isGuest ? limit.GuestPermitLimit : limit.UserPermitLimit,
                Window = limit.Window,
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            });
    }

    internal static IResult RateLimitedProblem() => Results.Problem(
        statusCode: StatusCodes.Status429TooManyRequests,
        title: "Too many requests",
        detail: "Rate limit exceeded. Retry after the time given in the Retry-After header.",
        type: ProblemTypes.RateLimited);

    private static string ClientIp(HttpContext context) => ClientPartition.Key(context.Connection.RemoteIpAddress);
}

/// <summary>Maps a remote address to the key per-client limits are partitioned by.</summary>
public static class ClientPartition
{
    /// <summary>
    /// IPv4 as is; IPv4-mapped IPv6 as the IPv4 address; other IPv6 addresses by their /64 (a single subscriber commonly owns a whole
    /// /64, so per-address limits would be trivially bypassed).
    /// </summary>
    public static string Key(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        Span<byte> bytes = stackalloc byte[16];
        if (!address.TryWriteBytes(bytes, out _))
        {
            return "unknown";
        }

        bytes[8..].Clear();
        return new IPAddress(bytes).ToString() + "/64";
    }
}

/// <summary>
/// The process-wide cap on guest creations. It is consulted by the endpoint after the per-client policy has admitted the request,
/// so one client exhausting its own limit cannot burn the global budget.
/// </summary>
public sealed class GuestCreateGlobalLimiter : IDisposable
{
    private readonly SlidingWindowRateLimiter _limiter;

    public GuestCreateGlobalLimiter(IOptions<RateLimitingOptions> options)
    {
        var limit = options.Value.GuestCreate;
        _limiter = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = limit.GlobalPermitLimit,
            Window = limit.GlobalWindow,
            SegmentsPerWindow = 6,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    /// <summary>Null when admitted, otherwise how long the caller should wait.</summary>
    public TimeSpan? TryAcquire()
    {
        using var lease = _limiter.AttemptAcquire();
        if (lease.IsAcquired)
        {
            return null;
        }

        return lease.TryGetMetadata(MetadataName.RetryAfter, out var delay) ? delay : TimeSpan.FromMinutes(1);
    }

    public void Dispose() => _limiter.Dispose();
}
