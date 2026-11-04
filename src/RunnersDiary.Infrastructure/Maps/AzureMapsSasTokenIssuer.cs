using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace RunnersDiary.Infrastructure.Maps;

/// <summary>
/// Mints one shared SAS token with ARM <c>Microsoft.Maps/accounts/listSas</c> (<c>api-version=2023-06-01</c>), signed with the account's
/// primary key on behalf of the SAS identity, with a short lifetime, a request-rate cap and allowed regions. The token is cached and
/// renewed when less than <see cref="AzureMapsOptions.SasRefreshBefore"/> remains; one caller refreshes while the others wait. The API
/// authenticates to ARM with its managed identity (<c>https://management.azure.com/.default</c>). Tokens are never logged.
/// A plain HTTP call is used instead of <c>Azure.ResourceManager.Maps</c>: one POST does not justify that package and its dependency tree.
/// </summary>
public sealed partial class AzureMapsSasTokenIssuer(
    HttpClient http, TokenCredential credential, AzureMapsOptions options, TimeProvider timeProvider, ILogger<AzureMapsSasTokenIssuer> logger)
    : IMapsTokenIssuer, IDisposable
{
    public const string ApiVersion = "2023-06-01";
    public const string TokenScope = "https://management.azure.com/.default";
    private const int MaxTokenLength = 8192;

    /// <summary>The start is backdated so a replica with a slow clock still sees a valid token.</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>After a failed mint nothing is retried for this long, so an outage does not turn into a stream of ARM calls.</summary>
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan UsableMargin = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private MapsToken? _cached;
    private DateTimeOffset _retryNotBefore;

    public bool IsEnabled => true;

    public async Task<MapsToken> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (Fresh(_cached) is { } fresh)
        {
            return fresh;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Fresh(_cached) is { } renewed)
            {
                return renewed;
            }

            var now = timeProvider.GetUtcNow();
            if (now < _retryNotBefore)
            {
                return Fallback() ?? throw new InvalidOperationException("Azure Maps token issuing is backing off after a failure.");
            }

            try
            {
                _cached = await MintAsync(now, cancellationToken);
                return _cached;
            }
#pragma warning disable CA1031 // Any failure keeps the previous token if it is still usable, otherwise it surfaces as an outage.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                _retryNotBefore = now + FailureBackoff;
                LogMintFailed(logger, ex.GetType().Name);
                if (Fallback() is { } previous)
                {
                    return previous;
                }

                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private MapsToken? Fresh(MapsToken? token) =>
        token is not null && token.ExpiresAt - timeProvider.GetUtcNow() > options.SasRefreshBefore ? token : null;

    /// <summary>A token that is past its refresh point but still has a usable margin.</summary>
    private MapsToken? Fallback() =>
        _cached is { } token && token.ExpiresAt - timeProvider.GetUtcNow() > UsableMargin ? token : null;

    private async Task<MapsToken> MintAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expiry = now + options.SasLifetime;
        var body = new Dictionary<string, object>
        {
            ["signingKey"] = "primaryKey",
            ["principalId"] = options.SasPrincipalId!,
            ["maxRatePerSecond"] = options.SasMaxRatePerSecond,
            ["start"] = (now - ClockSkew).UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["expiry"] = expiry.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };
        if (options.SasRegions.Count > 0)
        {
            body["regions"] = options.SasRegions;
        }

        var arm = await credential.GetTokenAsync(new TokenRequestContext([TokenScope]), cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.AccountResourceId}/listSas?api-version={ApiVersion}")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + arm.Token);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"listSas answered HTTP {(int)response.StatusCode}.", inner: null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var token = document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("accountSasToken", out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
        {
            throw new InvalidOperationException("listSas returned no usable token.");
        }

        return new MapsToken(token, expiry);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Issuing an Azure Maps SAS token failed ({ExceptionType}).")]
    private static partial void LogMintFailed(ILogger logger, string exceptionType);
}
