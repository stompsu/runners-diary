namespace RunnersDiary.Infrastructure.Maps;

/// <summary>A short-lived Azure Maps SAS token and the moment it stops working.</summary>
public sealed record MapsToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Issues the token the browser's Azure Maps SDK authenticates with (ADR 0022 section 5, ADR 0033). Keys never reach the browser.</summary>
public interface IMapsTokenIssuer
{
    /// <summary>False unless <c>Maps:Provider</c> is <c>AzureMaps</c>; the endpoint then answers 503 <c>feature-unavailable</c>.</summary>
    bool IsEnabled { get; }

    /// <summary>The shared token, renewed before it expires. Throws when the provider cannot issue one.</summary>
    Task<MapsToken> GetTokenAsync(CancellationToken cancellationToken);
}

/// <summary>The default: map display runs on MapLibre, so no token exists.</summary>
public sealed class DisabledMapsTokenIssuer : IMapsTokenIssuer
{
    public bool IsEnabled => false;

    public Task<MapsToken> GetTokenAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Azure Maps display is not enabled.");
}
